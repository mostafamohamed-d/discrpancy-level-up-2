using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Writes changed cells back into a loading schedule workbook, and touches nothing else.
    ///
    /// Hand written for the reason <see cref="XlsxWorkbook"/> is. The workbook is a zip of xml
    /// parts, and a changed cell is a change to one element of one sheet's part: the styles,
    /// the merges, the column widths, the other sheets and every other cell are carried across
    /// byte for byte. What a cell is changed to is written inline - a number as a number, text
    /// as an inline string - so the shared string table is never rewritten.
    ///
    /// A formula cell is refused rather than written. Typing over it would throw the formula
    /// away, and a schedule whose total stops adding up its parts is a worse schedule than the
    /// one being corrected. The workbook is marked to recalculate when Excel next opens it, so
    /// a formula fed by a changed cell comes out right there.
    ///
    /// The file as it was is copied aside before it is written over, and where the copy went
    /// is handed back, so a write that turns out wrong can be undone by hand.
    /// </summary>
    internal static class XlsxCellWriter
    {
        private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>
        /// Writes the edits into one sheet of the workbook and answers where the copy of the
        /// file as it was went. Throws, having written nothing, when the sheet is not there or
        /// an edit lands on a formula; throws <see cref="IOException"/> when the file cannot be
        /// written, which is almost always Excel having it open.
        /// </summary>
        public static string Write(string path, string sheetName, IList<XlsxCellEdit> edits)
        {
            if (edits == null || edits.Count == 0)
            {
                return null;
            }

            // Opened for writing with nobody else allowed in before anything else is done, which
            // is what fails - with nothing read, copied or written - when Excel has it open.
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                byte[] original = new byte[file.Length];
                int read = 0;
                while (read < original.Length)
                {
                    int got = file.Read(original, read, original.Length - read);
                    if (got <= 0)
                    {
                        throw new IOException("The workbook could not be read to the end.");
                    }
                    read += got;
                }

                byte[] written = Rewrite(original, sheetName, edits);

                string backup = BackupPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.WriteAllBytes(backup, original);

                file.Position = 0;
                file.SetLength(0);
                file.Write(written, 0, written.Length);

                return backup;
            }
        }

        /// <summary>The workbook with the edits made, as bytes. Kept apart from the file so it can be tested.</summary>
        internal static byte[] Rewrite(byte[] workbook, string sheetName, IList<XlsxCellEdit> edits)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                memory.Write(workbook, 0, workbook.Length);

                using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Update, true))
                {
                    ZipArchiveEntry bookEntry = Entry(archive, "xl/workbook.xml");
                    XDocument book = Load(bookEntry);
                    if (book == null)
                    {
                        throw new InvalidDataException("This is not an xlsx file: it has no workbook part.");
                    }

                    ZipArchiveEntry sheetEntry = Entry(archive, SheetPart(archive, book, sheetName));
                    XDocument sheet = Load(sheetEntry);
                    if (sheet == null)
                    {
                        throw new InvalidDataException($"The workbook has no sheet called \"{sheetName}\".");
                    }

                    Apply(sheet, edits);
                    Save(sheetEntry, sheet);

                    RecalculateOnOpen(book);
                    Save(bookEntry, book);
                }

                return memory.ToArray();
            }
        }

        /// <summary>
        /// Makes the edits in a sheet's xml. Every edit is checked before any is made, so a
        /// refusal leaves the sheet as it was rather than half written.
        /// </summary>
        internal static void Apply(XDocument sheet, IList<XlsxCellEdit> edits)
        {
            XNamespace ns = sheet.Root == null ? MainNamespace : sheet.Root.Name.Namespace;

            XElement data = sheet.Root?.Elements(ns + "sheetData").FirstOrDefault();
            if (data == null)
            {
                throw new InvalidDataException("The sheet has no cell data part.");
            }

            NumberImplicitReferences(data, ns);

            List<string> formulas = edits
                .Where(edit => FindCell(data, ns, edit.Row, edit.Column)?.Element(ns + "f") != null)
                .Select(edit => edit.Reference)
                .ToList();

            if (formulas.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{string.Join(", ", formulas)} {(formulas.Count == 1 ? "holds a formula" : "hold formulas")}, "
                    + "and writing a value over one would throw the formula away. Change it in Excel.");
            }

            foreach (XlsxCellEdit edit in edits)
            {
                XElement row = RowFor(data, ns, edit.Row);
                XElement cell = CellFor(row, ns, edit.Row, edit.Column);
                SetValue(cell, ns, edit.Text);
            }
        }

        #region the cells

        /// <summary>
        /// Writes the row and cell references out wherever the file left them to be counted.
        /// Excel always writes them; some other writers do not, and a cell can only be found to
        /// be changed once it has a name. Numbered exactly the way the reader counts them.
        /// </summary>
        private static void NumberImplicitReferences(XElement data, XNamespace ns)
        {
            int rowNumber = 0;

            foreach (XElement row in data.Elements(ns + "row"))
            {
                int stated;
                if (int.TryParse((string)row.Attribute("r"), NumberStyles.Integer,
                                 CultureInfo.InvariantCulture, out stated) && stated > 0)
                {
                    rowNumber = stated;
                }
                else
                {
                    rowNumber++;
                    row.SetAttributeValue("r", rowNumber.ToString(CultureInfo.InvariantCulture));
                }

                int columnNumber = 0;

                foreach (XElement cell in row.Elements(ns + "c"))
                {
                    string reference = (string)cell.Attribute("r");
                    int cellRow = rowNumber;
                    int cellColumn = columnNumber + 1;

                    if (!string.IsNullOrEmpty(reference))
                    {
                        XlsxSheet.SplitReference(reference, ref cellRow, ref cellColumn);
                    }
                    else
                    {
                        cell.SetAttributeValue("r", XlsxSheet.Reference(rowNumber, cellColumn));
                    }

                    columnNumber = cellColumn;
                }
            }
        }

        private static XElement FindCell(XElement data, XNamespace ns, int row, int column)
        {
            string wanted = XlsxSheet.Reference(row, column);

            return data.Elements(ns + "row")
                       .Where(r => RowNumber(r) == row)
                       .SelectMany(r => r.Elements(ns + "c"))
                       .FirstOrDefault(c => string.Equals((string)c.Attribute("r"), wanted,
                                                          StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The row, made where the sheet has none yet and put in its place among the others.</summary>
        private static XElement RowFor(XElement data, XNamespace ns, int number)
        {
            XElement after = null;

            foreach (XElement row in data.Elements(ns + "row"))
            {
                int at = RowNumber(row);
                if (at == number)
                {
                    return row;
                }
                if (at < number)
                {
                    after = row;
                }
            }

            XElement made = new XElement(ns + "row",
                                         new XAttribute("r", number.ToString(CultureInfo.InvariantCulture)));
            if (after == null)
            {
                data.AddFirst(made);
            }
            else
            {
                after.AddAfterSelf(made);
            }
            return made;
        }

        /// <summary>The cell, made where the row has none yet and put in its place along it.</summary>
        private static XElement CellFor(XElement row, XNamespace ns, int rowNumber, int column)
        {
            XElement after = null;

            foreach (XElement cell in row.Elements(ns + "c"))
            {
                int cellRow = rowNumber;
                int cellColumn = 0;
                XlsxSheet.SplitReference((string)cell.Attribute("r") ?? "", ref cellRow, ref cellColumn);

                if (cellColumn == column)
                {
                    return cell;
                }
                if (cellColumn < column)
                {
                    after = cell;
                }
            }

            // The spans a row states are only a hint to the reader, and a stale one is worse
            // than none, so a row that has grown a cell says none.
            row.Attribute("spans")?.Remove();

            XElement made = new XElement(ns + "c", new XAttribute("r", XlsxSheet.Reference(rowNumber, column)));
            if (after == null)
            {
                row.AddFirst(made);
            }
            else
            {
                after.AddAfterSelf(made);
            }
            return made;
        }

        /// <summary>
        /// Puts a value into a cell, keeping its formatting. A number goes in as a number, so
        /// Excel sums it; anything else goes in as text; nothing empties the cell.
        /// </summary>
        private static void SetValue(XElement cell, XNamespace ns, string text)
        {
            cell.Attribute("t")?.Remove();
            cell.Elements(ns + "v").Remove();
            cell.Elements(ns + "is").Remove();

            string value = (text ?? "").Trim();
            if (value.Length == 0)
            {
                return;
            }

            string number;
            if (TryNumber(value, out number))
            {
                cell.AddFirst(new XElement(ns + "v", number));
                return;
            }

            cell.SetAttributeValue("t", "inlineStr");
            cell.AddFirst(new XElement(ns + "is",
                new XElement(ns + "t",
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    value)));
        }

        /// <summary>
        /// Whether the text is a number, and that number the way xlsx stores one. Typed on a
        /// machine that writes a decimal comma it is still a number, and it is stored with a
        /// point, because that is the only way the file ever stores one.
        /// </summary>
        internal static bool TryNumber(string text, out string stored)
        {
            stored = null;

            double value;
            bool read = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);

            if (!read || double.IsNaN(value) || double.IsInfinity(value))
            {
                return false;
            }

            stored = value.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        private static int RowNumber(XElement row)
        {
            int number;
            return int.TryParse((string)row.Attribute("r"), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
        }

        #endregion

        #region the parts

        /// <summary>
        /// Asks Excel to work every formula out again when it next opens the file, so a total
        /// fed by a changed cell is right there rather than showing what it last came to.
        /// </summary>
        private static void RecalculateOnOpen(XDocument book)
        {
            XElement root = book.Root;
            if (root == null)
            {
                return;
            }

            XNamespace ns = root.Name.Namespace;
            XElement calc = root.Element(ns + "calcPr");

            if (calc == null)
            {
                calc = new XElement(ns + "calcPr");

                // calcPr has a place in the workbook part: after the sheets and the names that
                // follow them, before everything else. Anywhere else and Excel calls the file
                // damaged.
                XElement after = root.Elements()
                    .LastOrDefault(e => e.Name == ns + "sheets" || e.Name == ns + "functionGroups"
                                        || e.Name == ns + "externalReferences" || e.Name == ns + "definedNames");
                if (after == null)
                {
                    return;
                }
                after.AddAfterSelf(calc);
            }

            calc.SetAttributeValue("fullCalcOnLoad", "1");
        }

        /// <summary>The path inside the zip of the sheet with this name.</summary>
        private static string SheetPart(ZipArchive archive, XDocument book, string sheetName)
        {
            XElement sheet = XlsxWorkbook.Descendants(book, "sheet").FirstOrDefault(
                s => string.Equals(XlsxWorkbook.Attribute(s, "name"), sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet == null)
            {
                return null;
            }

            string id = XlsxWorkbook.Attribute(sheet, "id");

            XDocument relationships = Load(Entry(archive, "xl/_rels/workbook.xml.rels"));
            if (relationships == null)
            {
                return null;
            }

            XElement relationship = XlsxWorkbook.Descendants(relationships, "Relationship")
                .FirstOrDefault(r => XlsxWorkbook.Attribute(r, "Id") == id);

            string target = relationship == null ? null : XlsxWorkbook.Attribute(relationship, "Target");
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            target = target.Replace('\\', '/').TrimStart('/');
            return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
        }

        private static ZipArchiveEntry Entry(ZipArchive archive, string path)
        {
            return path == null
                ? null
                : archive.Entries.FirstOrDefault(
                    entry => string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase));
        }

        private static XDocument Load(ZipArchiveEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
            }
        }

        /// <summary>Writes a part back in place, so the zip keeps its order.</summary>
        private static void Save(ZipArchiveEntry entry, XDocument document)
        {
            using (Stream stream = entry.Open())
            {
                stream.SetLength(0);
                document.Save(stream, SaveOptions.DisableFormatting);
            }
        }

        /// <summary>
        /// Where the file as it was is kept: beside the tool's other files rather than beside
        /// the workbook, so a shared job folder does not fill up with copies.
        /// </summary>
        private static string BackupPath(string path)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DiscrepancyScanner", "schedule backups");

            string name = Path.GetFileNameWithoutExtension(path) + " "
                          + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)
                          + Path.GetExtension(path);

            return Path.Combine(folder, name);
        }

        #endregion
    }
}
