using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Writes a single sheet .xlsx workbook, every cell as text.
    ///
    /// An xlsx file is a zip of a few xml parts, so it is written out by hand here rather than
    /// by taking on a spreadsheet library: the add-in ships into Revit as one assembly, and an
    /// export of names and marks needs nothing that a library would bring with it.
    /// </summary>
    public static class ExcelWorkbookWriter
    {
        private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

        private const string ContentTypes = Declaration +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "</Types>";

        private const string RootRelationships = Declaration +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" " +
            "Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private const string WorkbookRelationships = Declaration +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" " +
            "Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" " +
            "Target=\"worksheets/sheet1.xml\"/>" +
            "</Relationships>";

        /// <summary>
        /// Writes <paramref name="rows"/> to <paramref name="path"/>, overwriting whatever is
        /// there. The first row is written like any other, so a header is simply the first row
        /// handed in. A short row leaves the cells past its end empty.
        /// </summary>
        public static void Write(string path, string sheetName, IList<string[]> rows)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            if (rows == null)
            {
                throw new ArgumentNullException(nameof(rows));
            }

            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                addPart(archive, "[Content_Types].xml", ContentTypes);
                addPart(archive, "_rels/.rels", RootRelationships);
                addPart(archive, "xl/workbook.xml", workbook(safeSheetName(sheetName)));
                addPart(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
                addPart(archive, "xl/worksheets/sheet1.xml", sheet(rows));
            }
        }

        private static void addPart(ZipArchive archive, string partName, string xml)
        {
            ZipArchiveEntry entry = archive.CreateEntry(partName, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            // No byte order mark: the xml declaration already names the encoding, and Excel
            // is happier without one.
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(xml);
            }
        }

        private static string workbook(string sheetName)
        {
            return Declaration +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + escape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "</workbook>";
        }

        private static string sheet(IList<string[]> rows)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(Declaration)
                   .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">")
                   .Append("<sheetData>");

            for (int row = 0; row < rows.Count; row++)
            {
                string[] cells = rows[row] ?? new string[0];
                builder.Append("<row r=\"").Append(row + 1).Append("\">");

                for (int column = 0; column < cells.Length; column++)
                {
                    // An empty cell is left out altogether rather than written as an empty
                    // string, which is what a spreadsheet reads as blank.
                    if (string.IsNullOrEmpty(cells[column]))
                    {
                        continue;
                    }

                    builder.Append("<c r=\"").Append(cellReference(column, row))
                           .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                           .Append(escape(cells[column]))
                           .Append("</t></is></c>");
                }

                builder.Append("</row>");
            }

            return builder.Append("</sheetData></worksheet>").ToString();
        }

        /// <summary>The A1 style reference of a cell, from its zero based column and row.</summary>
        private static string cellReference(int column, int row)
        {
            string name = "";
            int remaining = column;
            while (remaining >= 0)
            {
                name = (char)('A' + remaining % 26) + name;
                remaining = remaining / 26 - 1;
            }
            return name + (row + 1);
        }

        /// <summary>
        /// A sheet name Excel will accept: no more than 31 characters and none of the
        /// characters it reserves.
        /// </summary>
        private static string safeSheetName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "Sheet1";
            }

            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                builder.Append("[]:*?/\\".IndexOf(c) >= 0 ? ' ' : c);
            }

            string cleaned = builder.ToString().Trim('\'', ' ');
            if (cleaned.Length == 0)
            {
                return "Sheet1";
            }
            return cleaned.Length > 31 ? cleaned.Substring(0, 31) : cleaned;
        }

        /// <summary>
        /// Escapes what xml reserves and drops the control characters xml cannot carry, which
        /// a parameter read out of a model occasionally holds.
        /// </summary>
        private static string escape(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    default:
                        if (c < 0x20 && c != '\t' && c != '\n' && c != '\r')
                        {
                            break;
                        }
                        builder.Append(c);
                        break;
                }
            }
            return builder.ToString();
        }
    }
}
