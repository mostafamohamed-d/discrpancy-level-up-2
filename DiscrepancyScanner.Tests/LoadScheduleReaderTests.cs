using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading a loading schedule sheet.
    ///
    /// The sheet is laid out for a person, and every awkward thing about it here was taken
    /// from the reference workbook rather than imagined: the serial numbers are typed in the
    /// column beside the one the heading sits over, the heading repeats part way down where
    /// the schedule was split across two printed pages, a block is three rows or four, and one
    /// sheet marks its live load LL, LLR or MECH while another leaves that column empty
    /// altogether. None of that is guessable from the code, so it is all pinned here.
    /// </summary>
    [TestClass]
    public class LoadScheduleReaderTests
    {
        #region the sheets under test

        /// <summary>
        /// A sheet built the way the reference workbook builds one: a title, a heading row
        /// with "SL. NO." over column B and the numbers typed into column A, and blocks of
        /// three rows with the total and the live load merged onto the first of them.
        /// </summary>
        private static FakeSheet TowerSheet()
        {
            FakeSheet sheet = new FakeSheet();

            sheet.Put(2, 2, "TOWER - (L03) TO (TOP OF ROOF)");
            sheet.Put(3, 2, "SUPER IMPOSED DEAD & LIVE LOAD SCHEDULE");
            Heading(sheet, 4);

            Entry(sheet, 5, 1, "RESIDENTIAL-TYPE 1", new[] { 1.5, 0.5, 1.0 }, 3, 2, "LLR");
            Entry(sheet, 8, 2, "STAIRCASE", new[] { 0.0, 0.0, 0.0 }, 0, 5, "LL");
            Entry(sheet, 11, 3, "MEP AREA-TYPE 1", new[] { 6.0, 0.5, 0.0 }, 6.5, 5, "MECH");

            return sheet;
        }

        private static void Heading(FakeSheet sheet, int row)
        {
            // "SL. NO." labels column B, and the numbers go in column A beside it. That is
            // how the reference workbook is written, because column B carries the legend.
            sheet.Put(row, 2, "SL. NO.");
            sheet.Put(row, 3, "LEGEND");
            sheet.Put(row, 4, "DESCRIPTION");
            sheet.Put(row, 5, "SIDL (kN/m2)");
            sheet.Put(row, 9, "TOTAL (kN/m2)");
            sheet.Put(row, 10, "LIVE LOAD (kN/m2)");
            sheet.Put(row, 11, "REMARKS");
            sheet.Put(row, 12, "LL");
        }

        /// <summary>
        /// One entry, laid out as the workbook lays one out: the number, description, total
        /// and live load all on the first row of the block, and the breakdown running down
        /// column H under it.
        /// </summary>
        private static void Entry(FakeSheet sheet, int row, int number, string description,
                                  double[] breakdown, object total, double live, string kind)
        {
            sheet.Put(row, 1, number.ToString());
            sheet.Put(row, 4, description);

            if (total != null)
            {
                sheet.Put(row, 9, Convert.ToString(total, System.Globalization.CultureInfo.InvariantCulture));
            }

            sheet.Put(row, 10, live.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(kind))
            {
                sheet.Put(row, 12, kind);
            }

            string[] labels = { "FLOOR FINISH THK.", "SERVICES & CEILING", "PARTITION ALLOWANCE", "ADDITIONALS" };

            for (int i = 0; i < breakdown.Length; i++)
            {
                sheet.Put(row + i, 5, labels[Math.Min(i, labels.Length - 1)]);
                sheet.Put(row + i, 8, breakdown[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static LoadSchedule Read(FakeSheet sheet, string name = "Test-Area Load")
        {
            LoadSchedule schedule = new LoadSchedule("test.xlsx", name);
            sheet.ReadInto(schedule);
            return schedule;
        }

        #endregion

        #region what a row means

        [TestMethod]
        public void TheTotalColumnIsTheSuperimposedDeadLoad()
        {
            LoadScheduleEntry entry = Read(TowerSheet()).Find("1");

            Assert.IsNotNull(entry);
            Assert.AreEqual(3, entry.Loads.Sdl, 1e-9);
            Assert.AreEqual("RESIDENTIAL-TYPE 1", entry.Description);
        }

        [TestMethod]
        public void TheLiveLoadGoesToWhicheverOfTheThreeTheSheetMarksItAs()
        {
            LoadSchedule schedule = Read(TowerSheet());

            // Marked LLR: reducible, so the plain live load and the mechanical are nought.
            LoadSet reducible = schedule.Find("1").Loads;
            Assert.AreEqual(2, reducible.Lr, 1e-9);
            Assert.AreEqual(0, reducible.Ll, 1e-9);
            Assert.AreEqual(0, reducible.Mep, 1e-9);

            LoadSet plain = schedule.Find("2").Loads;
            Assert.AreEqual(5, plain.Ll, 1e-9);
            Assert.AreEqual(0, plain.Lr, 1e-9);

            LoadSet mechanical = schedule.Find("3").Loads;
            Assert.AreEqual(5, mechanical.Mep, 1e-9);
            Assert.AreEqual(0, mechanical.Ll, 1e-9);
        }

        [TestMethod]
        public void AnEmptyLiveLoadColumnReadsAsPlainLiveLoad()
        {
            // The podium sheet of the reference workbook leaves that column empty on every
            // row, so this is the ordinary case rather than a malformed one.
            FakeSheet sheet = TowerSheet();
            sheet.Clear(5, 12);

            Assert.AreEqual(2, Read(sheet).Find("1").Loads.Ll, 1e-9);
            Assert.AreEqual(0, Read(sheet).Find("1").Loads.Lr, 1e-9);
        }

        [TestMethod]
        public void AnEmptyTotalIsAddedUpFromTheBreakdownRows()
        {
            FakeSheet sheet = new FakeSheet();
            Heading(sheet, 4);
            Entry(sheet, 5, 1, "CORRIDOR", new[] { 3.0, 0.5, 0.0, 1.0 }, null, 5, "LL");

            Assert.AreEqual(4.5, Read(sheet).Find("1").Loads.Sdl, 1e-9);
        }

        [TestMethod]
        public void ABlockOfFourRowsIsReadLikeABlockOfThree()
        {
            FakeSheet sheet = new FakeSheet();
            Heading(sheet, 4);
            Entry(sheet, 5, 1, "CORRIDOR", new[] { 3.0, 0.5, 0.0, 1.0 }, 4.5, 5, "LL");
            Entry(sheet, 9, 2, "STORE", new[] { 2.0, 0.5, 0.0 }, 2.5, 6, "LL");

            LoadSchedule schedule = Read(sheet);

            Assert.AreEqual(2, schedule.Count);
            Assert.AreEqual(4.5, schedule.Find("1").Loads.Sdl, 1e-9);
            Assert.AreEqual(2.5, schedule.Find("2").Loads.Sdl, 1e-9);
        }

        #endregion

        #region the shape of the sheet

        [TestMethod]
        public void TheNumbersAreFoundBesideTheHeadingRatherThanUnderIt()
        {
            // The heading says column B and the numbers are in column A. Reading the heading
            // column alone finds nothing at all, which is what this is guarding against.
            LoadSchedule schedule = Read(TowerSheet());

            Assert.AreEqual(3, schedule.Count);
            CollectionAssert.AreEqual(new[] { "1", "2", "3" },
                                      schedule.Entries.Select(entry => entry.Number).ToArray());
        }

        [TestMethod]
        public void TheHeadingRepeatingPartWayDownDoesNotEndTheSheet()
        {
            FakeSheet sheet = TowerSheet();

            // A page break, the way the reference podium sheet carries one at row 111.
            sheet.Put(14, 2, "TOWER - (L03) TO (TOP OF ROOF)");
            Heading(sheet, 15);
            Entry(sheet, 16, 4, "RESTAURANT", new[] { 4.0, 0.5, 1.0 }, 5.5, 5, "LL");

            LoadSchedule schedule = Read(sheet);

            Assert.AreEqual(4, schedule.Count);
            Assert.AreEqual(5.5, schedule.Find("4").Loads.Sdl, 1e-9);
        }

        [TestMethod]
        public void TheTitleAboveTheHeadingIsKept()
        {
            Assert.AreEqual("TOWER - (L03) TO (TOP OF ROOF)", Read(TowerSheet()).Title);
        }

        [TestMethod]
        public void ASheetThatIsNotAnAreaLoadScheduleSaysSoRatherThanComingBackEmpty()
        {
            FakeSheet sheet = new FakeSheet();
            sheet.Put(2, 2, "LINE LOADING SCHEDULE");
            sheet.Put(4, 2, "DESCRIPTION");
            sheet.Put(4, 3, "LOAD (kN/m)");
            sheet.Put(5, 2, "BLOCKWORK");

            LoadSchedule schedule = Read(sheet, "Line Load D (TOWER)");

            Assert.AreEqual(0, schedule.Count);
            Assert.AreEqual(1, schedule.Issues.Count);
            StringAssert.Contains(schedule.Issues[0], "does not read as an area load schedule");
        }

        #endregion

        #region faults in the schedule itself

        [TestMethod]
        public void ANumberUsedTwiceIsReportedAndTheFirstOneWins()
        {
            // The reference workbook really does this: the tower sheet uses 31 and 33 twice
            // each. The check cannot pick between them, so it says so and carries on.
            FakeSheet sheet = new FakeSheet();
            Heading(sheet, 4);
            Entry(sheet, 5, 31, "IN-ACESSABLE BALCONIES", new[] { 1.5, 0.5, 0.0 }, 2, 1, "LLR");
            Entry(sheet, 8, 31, "TOILET AREA", new[] { 2.0, 0.5, 0.0 }, 2.5, 5, "LL");

            LoadSchedule schedule = Read(sheet);

            Assert.AreEqual(1, schedule.Count);
            Assert.AreEqual("IN-ACESSABLE BALCONIES", schedule.Find("31").Description);
            Assert.AreEqual(1, schedule.Issues.Count);
            StringAssert.Contains(schedule.Issues[0], "more than once");
        }

        [TestMethod]
        public void OnlyAWholeNumberStartsAnEntry()
        {
            int number;

            Assert.IsTrue(LoadScheduleReader.TryWholeNumber("12", out number));
            Assert.AreEqual(12, number);

            Assert.IsTrue(LoadScheduleReader.TryWholeNumber("12.0", out number));
            Assert.AreEqual(12, number);

            Assert.IsFalse(LoadScheduleReader.TryWholeNumber("12.5", out number));
            Assert.IsFalse(LoadScheduleReader.TryWholeNumber("SERVICES & CEILING", out number));
            Assert.IsFalse(LoadScheduleReader.TryWholeNumber("", out number));
            Assert.IsFalse(LoadScheduleReader.TryWholeNumber(null, out number));
        }

        #endregion

        #region labels that are not numbers

        [TestMethod]
        public void ALetteredLabelStartsAnEntryJustAsANumberDoes()
        {
            // Some schedules label their loads by the part of the building - H1 for the hotel,
            // L1 for the landscape - rather than counting them.
            FakeSheet sheet = new FakeSheet();
            Heading(sheet, 4);
            Entry(sheet, 5, 0, "HOTEL ROOM", new[] { 1.5, 0.5, 1.0 }, 3, 2, "LLR");
            Entry(sheet, 8, 0, "LANDSCAPE", new[] { 6.0, 0.0, 0.0 }, 6, 5, "LL");
            Entry(sheet, 11, 0, "LOBBY", new[] { 1.5, 0.5, 1.0 }, 3, 5, "LL");
            sheet.Put(5, 1, "H1");
            sheet.Put(8, 1, "L1");
            sheet.Put(11, 1, "7");

            LoadSchedule schedule = Read(sheet);

            Assert.AreEqual(3, schedule.Count, string.Join("; ", schedule.Issues));
            Assert.AreEqual("HOTEL ROOM", schedule.Find("H1").Description);
            Assert.AreEqual(6, schedule.Find("L1").Loads.Sdl, 1e-9);
            Assert.AreEqual("LOBBY", schedule.Find("7").Description);
            CollectionAssert.AreEqual(new[] { "7", "H1", "L1" },
                                      schedule.Entries.Select(entry => entry.Number).ToArray());
        }

        [TestMethod]
        public void ALabelIsFoundWhicheverWayItsCaseAndSpacesWereTyped()
        {
            FakeSheet sheet = new FakeSheet();
            Heading(sheet, 4);
            Entry(sheet, 5, 0, "HOTEL ROOM", new[] { 1.5, 0.5, 1.0 }, 3, 2, "LLR");
            sheet.Put(5, 1, "H1");

            LoadSchedule schedule = Read(sheet);

            Assert.IsNotNull(schedule.Find("h1"));
            Assert.IsNotNull(schedule.Find(" H 1 "));
            Assert.IsNull(schedule.Find("H-1"), "a dash is part of the label");
        }

        [TestMethod]
        public void AWordInTheSerialColumnWithNoLoadOnItsRowIsNotAnEntry()
        {
            // A note typed down the serial column, or a continuation row with a word in it,
            // carries no total and no live load, and must not split the entry above it.
            FakeSheet sheet = TowerSheet();
            sheet.Put(6, 1, "NOTE");
            sheet.Put(14, 1, "NOTES: ALL LOADS ARE UNFACTORED");

            LoadSchedule schedule = Read(sheet);

            Assert.AreEqual(3, schedule.Count);
            Assert.IsNull(schedule.Find("NOTE"));
            Assert.AreEqual(3, schedule.Find("1").Loads.Sdl, 1e-9);
        }

        [TestMethod]
        public void AWholeNumberReadsTheSameHoweverItWasTyped()
        {
            Assert.AreEqual("7", LoadLabel.Read("7"));
            Assert.AreEqual("7", LoadLabel.Read("07"));
            Assert.AreEqual("7", LoadLabel.Read("7.0"));
            Assert.AreEqual("H1", LoadLabel.Read(" H1 "));
            Assert.AreEqual("1E2", LoadLabel.Read("1E2"), "a label is not read as a hundred");
            Assert.AreEqual("", LoadLabel.Read("0"), "an integer parameter left at nought");
            Assert.AreEqual("", LoadLabel.Read("  "));
        }

        [TestMethod]
        public void LabelsAreListedTheWayAPersonCountsThem()
        {
            string[] labels = { "H10", "10", "L1", "H2", "2", "h1" };

            CollectionAssert.AreEqual(new[] { "2", "10", "h1", "H2", "H10", "L1" },
                                      labels.OrderBy(label => label, LoadLabel.Order).ToArray());
        }

        #endregion

        #region a sheet built by hand

        /// <summary>
        /// A sheet of text cells, standing in for one read out of a workbook. Filled through
        /// the same reading path the real one goes through, so what is tested here is the
        /// reader rather than a second copy of it.
        /// </summary>
        private sealed class FakeSheet
        {
            private readonly Dictionary<string, string> cells = new Dictionary<string, string>();
            private int rows;
            private int columns;

            public void Put(int row, int column, string text)
            {
                cells[row + ":" + column] = text;
                rows = Math.Max(rows, row);
                columns = Math.Max(columns, column);
            }

            public void Clear(int row, int column)
            {
                cells.Remove(row + ":" + column);
            }

            /// <summary>
            /// Writes the sheet out as the xml part of a workbook and reads it back through
            /// the real reader, so the cell reading is exercised as well as the parsing.
            /// </summary>
            public void ReadInto(LoadSchedule schedule)
            {
                System.Text.StringBuilder xml = new System.Text.StringBuilder();
                xml.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
                xml.Append("<sheetData>");

                for (int row = 1; row <= rows; row++)
                {
                    System.Text.StringBuilder line = new System.Text.StringBuilder();

                    for (int column = 1; column <= columns; column++)
                    {
                        string text;
                        if (!cells.TryGetValue(row + ":" + column, out text))
                        {
                            continue;
                        }

                        line.Append($"<c r=\"{ColumnName(column)}{row}\" t=\"inlineStr\"><is><t>")
                            .Append(System.Security.SecurityElement.Escape(text))
                            .Append("</t></is></c>");
                    }

                    if (line.Length > 0)
                    {
                        xml.Append($"<row r=\"{row}\">").Append(line).Append("</row>");
                    }
                }

                xml.Append("</sheetData></worksheet>");

                XlsxSheet sheet = XlsxSheet.Read(schedule.SheetName,
                    System.Xml.Linq.XDocument.Parse(xml.ToString()), new List<string>());

                LoadScheduleReader.Read(sheet, schedule);
            }

            private static string ColumnName(int column)
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
        }

        #endregion
    }
}
