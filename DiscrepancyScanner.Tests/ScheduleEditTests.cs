using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DiscrepancyScanner.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Changing the loading schedule from inside the tool: written into the workbook, or kept
    /// in the tool with the workbook left alone.
    /// </summary>
    [TestClass]
    public class ScheduleEditTests
    {
        private string path;

        [TestInitialize]
        public void MakeWorkbook()
        {
            path = Path.Combine(Path.GetTempPath(), "schedule-edit-" + Guid.NewGuid().ToString("N") + ".xlsx");
            File.WriteAllBytes(path, Workbook());
        }

        [TestCleanup]
        public void RemoveWorkbook()
        {
            ScheduleEdits.Current.ClearSheet(path, "Line Load");
            ScheduleEdits.Current.ClearSheet(path, "Area Load");
            File.Delete(path);
        }

        #region writing into the workbook

        [TestMethod]
        public void AChangedNumberIsWrittenAndTheRestOfTheSheetIsLeftAlone()
        {
            Write(new XlsxCellEdit(3, 3, "7.5"));

            XlsxSheet sheet = Sheet("Line Load");
            Assert.AreEqual("7.5", sheet.Text(3, 3));
            Assert.AreEqual("MRG_STR-Line Load-L1 Podium", sheet.Text(3, 2), "the shared string beside it");
            Assert.AreEqual("12", sheet.Text(4, 3), "the next row");
            Assert.AreEqual("Area", Sheet("Area Load").Text(1, 1), "the other sheet");
        }

        [TestMethod]
        public void TextIsWrittenAsTextAndANumberAsANumber()
        {
            Write(new XlsxCellEdit(3, 2, "Parapet"), new XlsxCellEdit(4, 3, "9"));

            XDocument part = SheetXml(File.ReadAllBytes(path), "xl/worksheets/sheet1.xml");
            XElement text = Cell(part, "B3");
            XElement number = Cell(part, "C4");

            Assert.AreEqual("inlineStr", (string)text.Attribute("t"));
            Assert.AreEqual("Parapet", Sheet("Line Load").Text(3, 2));
            Assert.IsNull(number.Attribute("t"));
            Assert.AreEqual("9", number.Elements().First(e => e.Name.LocalName == "v").Value);
        }

        [TestMethod]
        public void AStyleOnTheCellSurvivesTheChange()
        {
            Write(new XlsxCellEdit(3, 3, "8"));

            XDocument part = SheetXml(File.ReadAllBytes(path), "xl/worksheets/sheet1.xml");
            Assert.AreEqual("4", (string)Cell(part, "C3").Attribute("s"));
        }

        [TestMethod]
        public void ACellAndARowTheSheetDidNotHaveAreMadeInTheirPlace()
        {
            Write(new XlsxCellEdit(3, 5, "note"), new XlsxCellEdit(2, 1, "0"), new XlsxCellEdit(9, 1, "3"));

            XDocument part = SheetXml(File.ReadAllBytes(path), "xl/worksheets/sheet1.xml");
            List<string> rows = part.Descendants().Where(e => e.Name.LocalName == "row")
                                    .Select(e => (string)e.Attribute("r")).ToList();
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "9" }, rows);

            List<string> row3 = part.Descendants().Where(e => e.Name.LocalName == "row" && (string)e.Attribute("r") == "3")
                                    .Elements().Select(e => (string)e.Attribute("r")).ToList();
            CollectionAssert.AreEqual(new[] { "A3", "B3", "C3", "E3" }, row3);

            XlsxSheet sheet = Sheet("Line Load");
            Assert.AreEqual("note", sheet.Text(3, 5));
            Assert.AreEqual("3", sheet.Text(9, 1));
        }

        [TestMethod]
        public void EmptyingACellLeavesItEmpty()
        {
            Write(new XlsxCellEdit(4, 3, ""));

            Assert.AreEqual("", Sheet("Line Load").Text(4, 3));
        }

        [TestMethod]
        public void AFormulaIsRefusedAndNothingIsWritten()
        {
            byte[] before = File.ReadAllBytes(path);

            InvalidOperationException refused = Assert.ThrowsException<InvalidOperationException>(
                () => Write(new XlsxCellEdit(3, 3, "1"), new XlsxCellEdit(5, 3, "99")));

            StringAssert.Contains(refused.Message, "C5");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [TestMethod]
        public void TheWorkbookIsToldToRecalculateWhenExcelOpensIt()
        {
            Write(new XlsxCellEdit(3, 3, "8"));

            XDocument book = SheetXml(File.ReadAllBytes(path), "xl/workbook.xml");
            XElement calc = book.Root.Elements().Single(e => e.Name.LocalName == "calcPr");

            Assert.AreEqual("1", (string)calc.Attribute("fullCalcOnLoad"));
            Assert.AreEqual("sheets", ((XElement)calc.PreviousNode).Name.LocalName);
        }

        [TestMethod]
        public void TheFileAsItWasIsKeptAside()
        {
            byte[] before = File.ReadAllBytes(path);

            string backup = XlsxCellWriter.Write(path, "Line Load", new[] { new XlsxCellEdit(3, 3, "8") });
            try
            {
                CollectionAssert.AreEqual(before, File.ReadAllBytes(backup));
            }
            finally
            {
                File.Delete(backup);
            }
        }

        [TestMethod]
        public void AWorkbookSomebodyHasOpenIsNotWritten()
        {
            byte[] before = File.ReadAllBytes(path);

            // The way Excel holds a workbook it has open: others may read it, nobody may write.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                Assert.ThrowsException<IOException>(
                    () => XlsxCellWriter.Write(path, "Line Load", new[] { new XlsxCellEdit(3, 3, "8") }));
            }

            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        #endregion

        #region keeping it in the tool

        [TestMethod]
        public void AChangeKeptInTheToolIsWhatTheCheckReads()
        {
            ScheduleEdits.Current.Set(path, "Line Load", new XlsxCellEdit(3, 3, "7.5"));

            XlsxSheet read = XlsxWorkbook.Open(path).Sheet("Line Load");
            XlsxSheet file = XlsxWorkbook.Open(path, false).Sheet("Line Load");

            Assert.AreEqual("7.5", read.Text(3, 3));
            Assert.AreEqual(1, read.ToolEditCount);
            Assert.AreEqual("5", file.Text(3, 3), "the workbook itself is left alone");
            Assert.AreEqual(0, file.ToolEditCount);
        }

        [TestMethod]
        public void AReadWithChangesKeptInTheToolSaysSo()
        {
            ScheduleEdits.Current.Set(path, "Line Load", new XlsxCellEdit(3, 3, "7.5"));

            LineLoadSchedule schedule = LineLoadScheduleReader.Read(path, "Line Load");

            Assert.IsTrue(schedule.Issues.Any(issue => issue.Contains("changed in the tool")));
        }

        [TestMethod]
        public void AChangeKeptOverWhatBecameAFormulaIsNotLaidOverIt()
        {
            ScheduleEdits.Current.Set(path, "Line Load", new XlsxCellEdit(5, 3, "99"));

            XlsxSheet read = XlsxWorkbook.Open(path).Sheet("Line Load");

            Assert.AreEqual("17", read.Text(5, 3));
            Assert.AreEqual(0, read.ToolEditCount);
        }

        [TestMethod]
        public void ChangesKeptInTheToolGoThroughTheSettingsFile()
        {
            ScheduleEdits saved = new ScheduleEdits();
            saved.Set(path, "Line Load", new XlsxCellEdit(3, 3, "7.5"));
            saved.Set(path, "Line Load", new XlsxCellEdit(12, 28, "text"));

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { ScheduleEdits.Branch, saved.ToJson() }
            };

            ScheduleEdits read = new ScheduleEdits();
            read.ReadFrom(Json.Read(Json.Write(view)));

            IList<XlsxCellEdit> edits = read.For(path, "line load");
            Assert.AreEqual(2, edits.Count);
            Assert.AreEqual("C3", edits[0].Reference);
            Assert.AreEqual("7.5", edits[0].Text);
            Assert.AreEqual("AB12", edits[1].Reference);
        }

        [TestMethod]
        public void ForgettingASheetLeavesTheOthers()
        {
            ScheduleEdits edits = new ScheduleEdits();
            edits.Set(path, "Line Load", new XlsxCellEdit(3, 3, "7.5"));
            edits.Set(path, "Area Load", new XlsxCellEdit(1, 1, "x"));

            Assert.AreEqual(1, edits.ClearSheet(path, "Line Load"));
            Assert.AreEqual(0, edits.Count(path, "Line Load"));
            Assert.AreEqual(1, edits.Count(path, "Area Load"));
        }

        #endregion

        #region the workbook

        private void Write(params XlsxCellEdit[] edits)
        {
            File.WriteAllBytes(path, XlsxCellWriter.Rewrite(File.ReadAllBytes(path), "Line Load", edits));
        }

        private XlsxSheet Sheet(string name)
        {
            return XlsxWorkbook.Open(path, false).Sheet(name);
        }

        private static XElement Cell(XDocument part, string reference)
        {
            return part.Descendants().Single(e => e.Name.LocalName == "c" && (string)e.Attribute("r") == reference);
        }

        private static XDocument SheetXml(byte[] workbook, string part)
        {
            using (ZipArchive archive = new ZipArchive(new MemoryStream(workbook), ZipArchiveMode.Read))
            using (Stream stream = archive.GetEntry(part).Open())
            {
                return XDocument.Load(stream);
            }
        }

        /// <summary>
        /// A small line load schedule the way Excel writes one: shared strings, a styled cell,
        /// a formula total with its last answer beside it, and a second sheet.
        /// </summary>
        private static byte[] Workbook()
        {
            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

            Dictionary<string, string> parts = new Dictionary<string, string>
            {
                ["[Content_Types].xml"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                    + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                    + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                    + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
                    + "</Types>",
                ["xl/workbook.xml"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + $"<workbook xmlns=\"{main}\" xmlns:r=\"{rel}\">"
                    + "<sheets><sheet name=\"Line Load\" sheetId=\"1\" r:id=\"rId1\"/>"
                    + "<sheet name=\"Area Load\" sheetId=\"2\" r:id=\"rId2\"/></sheets>"
                    + "</workbook>",
                ["xl/_rels/workbook.xml.rels"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                    + "<Relationship Id=\"rId1\" Type=\"worksheet\" Target=\"worksheets/sheet1.xml\"/>"
                    + "<Relationship Id=\"rId2\" Type=\"worksheet\" Target=\"worksheets/sheet2.xml\"/>"
                    + "</Relationships>",
                ["xl/sharedStrings.xml"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + $"<sst xmlns=\"{main}\"><si><t>No.</t></si><si><t>MRG_STR-Line Load-L1 Podium</t></si>"
                    + "<si><t>Area</t></si></sst>",
                ["xl/worksheets/sheet1.xml"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + $"<worksheet xmlns=\"{main}\"><sheetData>"
                    + "<row r=\"1\" spans=\"1:3\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>"
                    + "<row r=\"3\" spans=\"1:3\"><c r=\"A3\"><v>1</v></c><c r=\"B3\" t=\"s\"><v>1</v></c>"
                    + "<c r=\"C3\" s=\"4\"><v>5</v></c></row>"
                    + "<row r=\"4\"><c r=\"A4\"><v>2</v></c><c r=\"C4\"><v>12</v></c></row>"
                    + "<row r=\"5\"><c r=\"C5\"><f>C3+C4</f><v>17</v></c></row>"
                    + "</sheetData></worksheet>",
                ["xl/worksheets/sheet2.xml"] =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                    + $"<worksheet xmlns=\"{main}\"><sheetData>"
                    + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>2</v></c></row>"
                    + "</sheetData></worksheet>"
            };

            using (MemoryStream memory = new MemoryStream())
            {
                using (ZipArchive archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    foreach (KeyValuePair<string, string> part in parts)
                    {
                        using (Stream stream = archive.CreateEntry(part.Key).Open())
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(part.Value);
                            stream.Write(bytes, 0, bytes.Length);
                        }
                    }
                }

                return memory.ToArray();
            }
        }

        #endregion
    }
}
