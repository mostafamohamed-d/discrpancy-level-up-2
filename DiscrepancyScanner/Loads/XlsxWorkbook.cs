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
    /// Enough of an xlsx reader to get a loading schedule out of one, and no more.
    ///
    /// Hand written for the same reason <see cref="ExcelWorkbookWriter"/> is: the add-in ships
    /// into Revit's own process as a single assembly, and a spreadsheet library loaded in
    /// there is a version clash waiting to happen for the sake of reading four columns. An
    /// xlsx file is a zip of a few xml parts, so it is opened as one.
    ///
    /// Everything is read as text. A loading schedule is numbers typed into cells, and the
    /// difference between 3 and 3.0 in the file is not a difference the check cares about.
    ///
    /// Merged cells are left exactly as the file stores them: the value sits on the top left
    /// cell of the merge and the rest are empty. That is the right way round here - a schedule
    /// entry starts at the row its serial number is on, and the total and the live load for
    /// that entry are merged down from the same row.
    /// </summary>
    internal sealed class XlsxWorkbook
    {
        private readonly List<string> sheetNames = new List<string>();
        private readonly Dictionary<string, XlsxSheet> sheets =
            new Dictionary<string, XlsxSheet>(StringComparer.OrdinalIgnoreCase);

        private XlsxWorkbook()
        {
        }

        /// <summary>The sheets in the order the workbook lists them, which is tab order.</summary>
        public IList<string> SheetNames { get { return sheetNames; } }

        /// <summary>One sheet by name, or null when the workbook has no such sheet.</summary>
        public XlsxSheet Sheet(string name)
        {
            XlsxSheet sheet;
            return name != null && sheets.TryGetValue(name, out sheet) ? sheet : null;
        }

        /// <summary>
        /// Reads the whole workbook into memory. Throws when the file is not there, is not a
        /// zip, or has no workbook part in it - all of which mean the same thing to the caller,
        /// which is that this is not a spreadsheet.
        /// </summary>
        public static XlsxWorkbook Open(string path)
        {
            return Open(path, true);
        }

        /// <summary>
        /// Reads the whole workbook, with or without the cells changed in the tool laid over
        /// what the file says. The check always wants them - they are what the user said the
        /// schedule is - and the schedule window wants the file on its own, so it can show
        /// which cells differ from it.
        /// </summary>
        public static XlsxWorkbook Open(string path, bool withToolEdits)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentNullException(nameof(path));
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("There is no file at " + path, path);
            }

            XlsxWorkbook workbook = new XlsxWorkbook();

            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read))
            {
                XDocument book = ReadXml(archive, "xl/workbook.xml");
                if (book == null)
                {
                    throw new InvalidDataException(Path.GetFileName(path) +
                        " has no workbook part in it, so it is not an xlsx file.");
                }

                Dictionary<string, string> targets = ReadRelationships(archive);
                List<string> strings = ReadSharedStrings(archive);

                foreach (XElement sheet in Descendants(book, "sheet"))
                {
                    string name = Attribute(sheet, "name");
                    string id = Attribute(sheet, "id");

                    if (string.IsNullOrEmpty(name) || workbook.sheets.ContainsKey(name))
                    {
                        continue;
                    }

                    string target;
                    if (!targets.TryGetValue(id ?? "", out target))
                    {
                        continue;
                    }

                    XDocument grid = ReadXml(archive, target);
                    if (grid == null)
                    {
                        continue;
                    }

                    XlsxSheet read = XlsxSheet.Read(name, grid, strings);
                    if (withToolEdits)
                    {
                        read.ToolEditCount = ScheduleEdits.Current.ApplyTo(path, read);
                    }

                    workbook.sheetNames.Add(name);
                    workbook.sheets[name] = read;
                }
            }

            return workbook;
        }

        #region the parts

        /// <summary>
        /// The relationship id to part path map for the workbook, with every path made
        /// absolute within the zip so it can be looked up directly.
        /// </summary>
        private static Dictionary<string, string> ReadRelationships(ZipArchive archive)
        {
            Dictionary<string, string> targets = new Dictionary<string, string>(StringComparer.Ordinal);

            XDocument relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
            if (relationships == null)
            {
                return targets;
            }

            foreach (XElement relationship in Descendants(relationships, "Relationship"))
            {
                string id = Attribute(relationship, "Id");
                string target = Attribute(relationship, "Target");

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(target))
                {
                    continue;
                }

                target = target.Replace('\\', '/').TrimStart('/');
                if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                {
                    target = "xl/" + target;
                }

                targets[id] = target;
            }

            return targets;
        }

        /// <summary>
        /// The shared string table, which is where every piece of text in a sheet actually
        /// lives. A string split into several runs by its formatting is joined back up, so a
        /// heading with one word in bold reads as the heading rather than as its pieces.
        /// </summary>
        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            List<string> strings = new List<string>();

            XDocument document = ReadXml(archive, "xl/sharedStrings.xml");
            if (document == null)
            {
                return strings;
            }

            foreach (XElement item in Descendants(document, "si"))
            {
                strings.Add(TextOf(item));
            }

            return strings;
        }

        private static XDocument ReadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.FullName, path, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return null;
            }

            try
            {
                using (Stream stream = entry.Open())
                {
                    return XDocument.Load(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region reading past the namespaces

        /// <summary>
        /// Elements by local name, ignoring which namespace they were written in. Which
        /// namespace an xlsx uses depends on what wrote it, and none of them change what the
        /// elements mean.
        /// </summary>
        internal static IEnumerable<XElement> Descendants(XContainer container, string localName)
        {
            return container.Descendants().Where(element => element.Name.LocalName == localName);
        }

        internal static IEnumerable<XElement> Children(XContainer container, string localName)
        {
            return container.Elements().Where(element => element.Name.LocalName == localName);
        }

        /// <summary>An attribute by local name, so r:id and id are both found by "id".</summary>
        internal static string Attribute(XElement element, string localName)
        {
            XAttribute attribute = element.Attributes()
                .FirstOrDefault(candidate => candidate.Name.LocalName == localName);
            return attribute?.Value;
        }

        /// <summary>
        /// All the text under an element, with the phonetic runs a Japanese or Chinese
        /// workbook carries left out - those are a reading guide rather than the cell's text.
        /// </summary>
        internal static string TextOf(XElement element)
        {
            string text = string.Concat(element.Descendants()
                .Where(node => node.Name.LocalName == "t")
                .Where(node => node.Parent == null || node.Parent.Name.LocalName != "rPh")
                .Select(node => node.Value));

            return text;
        }

        #endregion
    }

    /// <summary>
    /// One sheet, as a sparse map of cell text by row and column. Rows and columns are
    /// numbered from one, the way a spreadsheet numbers them and the way anyone reading the
    /// file alongside the code will count them.
    /// </summary>
    internal sealed class XlsxSheet
    {
        private readonly Dictionary<long, string> cells = new Dictionary<long, string>();
        private readonly HashSet<long> formulas = new HashSet<long>();

        private XlsxSheet(string name)
        {
            Name = name;
        }

        public string Name { get; }

        /// <summary>The last row that has anything on it. Zero for an empty sheet.</summary>
        public int RowCount { get; private set; }

        /// <summary>The last column that has anything in it. Zero for an empty sheet.</summary>
        public int ColumnCount { get; private set; }

        /// <summary>
        /// What is in one cell, trimmed, or an empty string when the cell is empty or outside
        /// the sheet. Never null, so every caller can go straight to comparing it.
        /// </summary>
        public string Text(int row, int column)
        {
            string value;
            return cells.TryGetValue(Key(row, column), out value) ? value : "";
        }

        /// <summary>
        /// True for a cell the workbook works out with a formula. What it holds is whatever it
        /// last calculated to, so it is shown and read but never edited from the tool: a new
        /// value typed over it would throw the formula away.
        /// </summary>
        public bool IsFormula(int row, int column)
        {
            return formulas.Contains(Key(row, column));
        }

        /// <summary>
        /// How many of this sheet's cells hold a value changed in the tool rather than the one
        /// in the file. Zero for a sheet read as the file has it.
        /// </summary>
        public int ToolEditCount { get; internal set; }

        /// <summary>
        /// Puts text into one cell, or empties it when the text is empty. How a change kept in
        /// the tool is laid over what the file says.
        /// </summary>
        internal void Set(int row, int column, string text)
        {
            if (row <= 0 || column <= 0)
            {
                return;
            }

            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0)
            {
                cells.Remove(Key(row, column));
                return;
            }

            cells[Key(row, column)] = trimmed;
            RowCount = Math.Max(RowCount, row);
            ColumnCount = Math.Max(ColumnCount, column);
        }

        /// <summary>
        /// The number in one cell. Read invariantly, because that is how xlsx stores numbers
        /// whatever the machine that wrote it was set to.
        /// </summary>
        public bool TryNumber(int row, int column, out double value)
        {
            return TryNumber(Text(row, column), out value);
        }

        public static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        internal static XlsxSheet Read(string name, XDocument document, IList<string> sharedStrings)
        {
            XlsxSheet sheet = new XlsxSheet(name);

            foreach (XElement row in XlsxWorkbook.Descendants(document, "row"))
            {
                int rowNumber = 0;
                int.TryParse(XlsxWorkbook.Attribute(row, "r"), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out rowNumber);

                int columnNumber = 0;

                foreach (XElement cell in XlsxWorkbook.Children(row, "c"))
                {
                    string reference = XlsxWorkbook.Attribute(cell, "r");
                    int cellRow = rowNumber;
                    int cellColumn = columnNumber + 1;

                    if (!string.IsNullOrEmpty(reference))
                    {
                        SplitReference(reference, ref cellRow, ref cellColumn);
                    }

                    columnNumber = cellColumn;

                    if (cellRow > 0 && cellColumn > 0 && XlsxWorkbook.Children(cell, "f").Any())
                    {
                        sheet.formulas.Add(Key(cellRow, cellColumn));
                    }

                    string text = ValueOf(cell, sharedStrings);
                    if (text.Length == 0 || cellRow <= 0 || cellColumn <= 0)
                    {
                        continue;
                    }

                    sheet.cells[Key(cellRow, cellColumn)] = text;
                    sheet.RowCount = Math.Max(sheet.RowCount, cellRow);
                    sheet.ColumnCount = Math.Max(sheet.ColumnCount, cellColumn);
                }
            }

            return sheet;
        }

        /// <summary>
        /// What one cell holds, as text. A cell is either a number written straight into it, a
        /// pointer into the shared string table, or - from some writers - a string put inline.
        /// A formula cell carries the value it last calculated to next to the formula, which
        /// is the one that is wanted.
        /// </summary>
        private static string ValueOf(XElement cell, IList<string> sharedStrings)
        {
            string type = XlsxWorkbook.Attribute(cell, "t") ?? "n";

            if (type == "inlineStr")
            {
                XElement inline = XlsxWorkbook.Children(cell, "is").FirstOrDefault();
                return inline == null ? "" : XlsxWorkbook.TextOf(inline).Trim();
            }

            XElement value = XlsxWorkbook.Children(cell, "v").FirstOrDefault();
            if (value == null)
            {
                return "";
            }

            if (type == "s")
            {
                int index;
                if (int.TryParse(value.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                    && index >= 0 && index < sharedStrings.Count)
                {
                    return (sharedStrings[index] ?? "").Trim();
                }
                return "";
            }

            return value.Value.Trim();
        }

        /// <summary>Splits a cell reference such as "AB12" into its row and its column.</summary>
        internal static void SplitReference(string reference, ref int row, ref int column)
        {
            int letters = 0;
            while (letters < reference.Length && !char.IsDigit(reference[letters]))
            {
                letters++;
            }

            int index = 0;
            for (int i = 0; i < letters; i++)
            {
                char c = char.ToUpperInvariant(reference[i]);
                if (c < 'A' || c > 'Z')
                {
                    return;
                }
                index = index * 26 + (c - 'A' + 1);
            }

            if (index > 0)
            {
                column = index;
            }

            int number;
            if (letters < reference.Length
                && int.TryParse(reference.Substring(letters), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out number))
            {
                row = number;
            }
        }

        /// <summary>A cell's name the way a spreadsheet writes it: row 12, column 3 is "C12".</summary>
        internal static string Reference(int row, int column)
        {
            return ColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A column's letters: 1 is "A", 27 is "AA".</summary>
        internal static string ColumnName(int column)
        {
            string name = "";
            while (column > 0)
            {
                int remainder = (column - 1) % 26;
                name = (char)('A' + remainder) + name;
                column = (column - 1) / 26;
            }
            return name;
        }

        private static long Key(int row, int column)
        {
            return ((long)row << 20) | (uint)column;
        }
    }
}
