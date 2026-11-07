using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading a line load schedule sheet, and turning what it says into the load an ETABS
    /// frame should carry.
    ///
    /// Every awkward thing pinned here came off the reference workbook rather than out of
    /// anybody's head: the value column holds bare numbers and numbers with units in the same
    /// column and they do not mean the same thing, the area schedule sits on a tab beside this
    /// one with a heading that half matches, and FACADE is typed with a cedilla on the C.
    /// </summary>
    [TestClass]
    public class LineLoadScheduleTests
    {
        #region the value column

        /// <summary>
        /// The two ways the same column writes a value. A bare number is a load per square
        /// metre of elevation and wants multiplying by a wall height; a number marked kN/m' is
        /// a load per metre run and must not be touched. Getting these the wrong way round is
        /// a factor of three, so both are pinned.
        /// </summary>
        [TestMethod]
        public void ABareNumberIsALoadPerSquareMetreOfElevation()
        {
            LineLoadSchedule schedule = Read(TowerSheet());

            LineLoadEntry entry = schedule.Find(1);

            Assert.IsNotNull(entry, "row 1 should have been read");
            Assert.AreEqual(4.8, entry.Value, 1e-9);
            Assert.IsFalse(entry.HeightIndependent,
                           "a bare number is a load on elevation and depends on the wall height");
        }

        [TestMethod]
        public void AValueMarkedPerMetreRunIsTheLoadItself()
        {
            LineLoadSchedule schedule = Read(TowerSheet());

            LineLoadEntry entry = schedule.Find(5);

            Assert.IsNotNull(entry, "row 5 should have been read");
            Assert.AreEqual(1.5, entry.Value, 1e-9);
            Assert.IsTrue(entry.HeightIndependent, "1.50 kN/m' is already a load per metre run");
        }

        /// <summary>
        /// Excel turns a typed apostrophe into a curly one given half a chance, and a sheet
        /// that has been through a round of editing has both in the same column. Both are the
        /// same mark.
        /// </summary>
        [TestMethod]
        public void ACurlyApostropheMarksPerMetreRunJustAsAPlainOneDoes()
        {
            Assert.IsTrue(LineLoadScheduleReader.IsPerMetreRun("1.50 kN/m'"));
            Assert.IsTrue(LineLoadScheduleReader.IsPerMetreRun("1.50 kN/m’"));
            Assert.IsTrue(LineLoadScheduleReader.IsPerMetreRun("1.50 KN/M ′"));
        }

        /// <summary>
        /// Without the prime it is a load on elevation however the units were written. A cell
        /// reading "4.8 kN/m2" is the same thing as a cell reading "4.8".
        /// </summary>
        [TestMethod]
        public void UnitsWithoutAPrimeAreStillALoadOnElevation()
        {
            double value;
            bool perMetreRun;

            Assert.IsTrue(LineLoadScheduleReader.TryValue("4.8 kN/m2", out value, out perMetreRun));
            Assert.AreEqual(4.8, value, 1e-9);
            Assert.IsFalse(perMetreRun);
        }

        [TestMethod]
        public void ARowWhoseValueIsAWordIsReportedRatherThanReadAsNought()
        {
            FakeSheet sheet = TowerSheet();
            sheet.Put(5, 4, "SEE NOTE 3");

            LineLoadSchedule schedule = Read(sheet);

            Assert.IsNull(schedule.Find(2), "a row whose value did not read is not an entry");
            Assert.IsTrue(schedule.Issues.Any(issue => issue.Contains("SEE NOTE 3")),
                          "the row that could not be read should be named, so it can be found");
        }

        #endregion

        #region the sheet

        [TestMethod]
        public void EveryNumberedRowIsRead()
        {
            LineLoadSchedule schedule = Read(TowerSheet());

            Assert.AreEqual(5, schedule.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 },
                                      schedule.Entries.Select(entry => entry.Number).ToArray());
        }

        [TestMethod]
        public void TheTitleRowNamesThePartOfTheBuilding()
        {
            LineLoadSchedule schedule = Read(TowerSheet());

            Assert.AreEqual("TOWER - (L03) TO (TOP OF ROOF)", schedule.Title);
        }

        /// <summary>
        /// The area schedule lives on a tab beside this one and its heading carries SL NO. and
        /// SIDL too, so picking the wrong tab is the likely mistake. Its TOTAL and LIVE LOAD
        /// columns are what tell it apart, and the reader says which one it found rather than
        /// reporting an unreadable sheet.
        /// </summary>
        [TestMethod]
        public void AnAreaScheduleTabIsNamedAsOneRatherThanReadAsRubbish()
        {
            FakeSheet sheet = new FakeSheet();
            sheet.Put(4, 2, "SL. NO.");
            sheet.Put(4, 4, "DESCRIPTION");
            sheet.Put(4, 5, "SIDL (kN/m2)");
            sheet.Put(4, 9, "TOTAL (kN/m2)");
            sheet.Put(4, 10, "LIVE LOAD (kN/m2)");
            sheet.Put(5, 1, "1");

            LineLoadSchedule schedule = Read(sheet);

            Assert.AreEqual(0, schedule.Count);
            Assert.IsTrue(schedule.Issues.Any(issue => issue.Contains("area loading schedule")),
                          "the reader should say which schedule it was actually given");
        }

        /// <summary>
        /// The serial numbers are not always under their own heading - the area schedule in
        /// the same workbook heads column B and types into column A, because B carries the
        /// legend swatch. The line load sheets put them under the heading, so both have to
        /// read.
        /// </summary>
        [TestMethod]
        public void TheNumbersAreFoundWhereverTheyAreTypedRelativeToTheirHeading()
        {
            FakeSheet beside = new FakeSheet();
            beside.Put(3, 2, "SL NO.");
            beside.Put(3, 4, "SIDL (kN/m2 )\nLOAD ON ELEVATION");
            beside.Put(3, 5, "DESCRIPTION");
            beside.Put(4, 1, "7");
            beside.Put(4, 4, "2.4");
            beside.Put(4, 5, "BLOCK WALL");

            LineLoadSchedule schedule = Read(beside);

            Assert.AreEqual(1, schedule.Count);
            Assert.AreEqual(2.4, schedule.Find(7).Value, 1e-9);
        }

        #endregion

        #region the wall height

        /// <summary>
        /// The one that would go wrong silently. The reference workbook types FACADE with a
        /// cedilla - FA(C with cedilla)ADE - so a plain comparison misses every facade row on
        /// the sheet, takes 200mm off a height that should be full, and reports a load a few
        /// per cent light on exactly the lines carrying most of it. Nothing about the output
        /// would look wrong.
        /// </summary>
        [TestMethod]
        public void FacadeIsRecognisedWhenItIsTypedWithACedilla()
        {
            const string names = "facade, cladding, glazing";

            Assert.IsTrue(LineLoadHeight.WantsFullHeight(
                "ALLOWANCE FOR FULL HEIGHT FAÇADE GLAZING SYSTEM INCLUDING ALL FIXING", names),
                "the schedule types FACADE with a cedilla, and it is the same word");

            Assert.IsTrue(LineLoadHeight.WantsFullHeight("ALLOWANCE FOR FACADE ACCESS MONORAIL", names));
            Assert.IsTrue(LineLoadHeight.WantsFullHeight("allowance for edge of slab cladding", names));
        }

        [TestMethod]
        public void AWallThatIsNotAFacadeLosesTheAllowanceOffItsHeight()
        {
            const string names = "facade, cladding, glazing";

            Assert.AreEqual(2800,
                LineLoadHeight.WallHeight("ALLOWANCE FOR 200mm THICK SOLID BLOCK WALL", 3000, 200, names),
                1e-9);

            Assert.AreEqual(3000,
                LineLoadHeight.WallHeight("ALLOWANCE FOR FULL HEIGHT FAÇADE GLAZING SYSTEM", 3000, 200, names),
                1e-9);
        }

        [TestMethod]
        public void ALoadOnElevationIsMultipliedByTheWallHeight()
        {
            LineLoadEntry entry = new LineLoadEntry(1, "ALLOWANCE FOR 200mm THICK SOLID BLOCK WALL",
                                                    4.8, false, "4.8");

            ExpectedLineLoad expected = LineLoadHeight.Expected(entry, 3000, 200, "facade, cladding, glazing");

            Assert.IsTrue(expected.Known);
            Assert.AreEqual(2800, expected.HeightMM, 1e-9);
            Assert.AreEqual(4.8 * 2.8, expected.PerMetre, 1e-9);
        }

        [TestMethod]
        public void ALoadPerMetreRunIsNotMultipliedByAnything()
        {
            LineLoadEntry entry = new LineLoadEntry(5, "ALLOWANCE FOR GLASS BALUSTRADE / RAILING",
                                                    1.5, true, "1.50 kN/m'");

            ExpectedLineLoad expected = LineLoadHeight.Expected(entry, 3000, 200, "facade, cladding, glazing");

            Assert.IsTrue(expected.Known);
            Assert.AreEqual(1.5, expected.PerMetre, 1e-9);
            Assert.AreEqual(0, expected.HeightMM, 1e-9,
                            "a load per metre run stands over no particular height");
        }

        /// <summary>
        /// The topmost story has nothing above it to measure a wall height to. A load per
        /// metre run is still checkable there; one on elevation is not, and saying so is the
        /// honest answer - nought would read as ETABS having too much on every line up there.
        /// </summary>
        [TestMethod]
        public void WithNoStoryAboveALoadOnElevationIsNotGuessedAt()
        {
            LineLoadEntry onElevation = new LineLoadEntry(1, "BLOCK WALL", 4.8, false, "4.8");
            LineLoadEntry perMetreRun = new LineLoadEntry(5, "BALUSTRADE", 1.5, true, "1.50 kN/m'");

            Assert.IsFalse(LineLoadHeight.Expected(onElevation, 0, 200, "facade").Known);
            Assert.IsTrue(LineLoadHeight.Expected(perMetreRun, 0, 200, "facade").Known);
        }

        /// <summary>
        /// 4.8 kN/m2 over 5.425 m of wall is 26.04, which is modelled as 26.1: the plan's side
        /// is rounded up the way the designer rounds it, never down.
        /// </summary>
        [TestMethod]
        public void TheWorkedOutLoadIsRoundedUpToTheStep()
        {
            LineLoadEntry entry = new LineLoadEntry(1, "BLOCK WALL", 4.8, false, "4.8");

            ExpectedLineLoad expected = LineLoadHeight.Expected(entry, 5625, 200, "facade", 0.1);

            Assert.AreEqual(26.1, expected.PerMetre, 1e-9);
            StringAssert.Contains(expected.How, "rounded up to 26.1");

            Assert.AreEqual(26.04, LineLoadHeight.Expected(entry, 5625, 200, "facade", 0).PerMetre, 1e-9,
                            "a step of nought leaves the value as worked out");
        }

        [TestMethod]
        public void AValueAlreadyOnTheStepIsNotPushedUpByFloatingPoint()
        {
            Assert.AreEqual(26.0, LineLoadHeight.RoundUp(26.0000000001, 0.1), 1e-9);
            Assert.AreEqual(1.5, LineLoadHeight.RoundUp(1.5, 0.1), 1e-9);
            Assert.AreEqual(13.5, LineLoadHeight.RoundUp(4.8 * 2.8, 0.1), 1e-9, "13.44 goes up");
        }

        [TestMethod]
        public void AStoryAboveShallowerThanTheAllowanceIsReportedRatherThanTurnedUpsideDown()
        {
            LineLoadEntry entry = new LineLoadEntry(1, "BLOCK WALL", 4.8, false, "4.8");

            ExpectedLineLoad expected = LineLoadHeight.Expected(entry, 150, 200, "facade");

            Assert.IsFalse(expected.Known, "a 150mm gap less a 200mm allowance is not a -50mm wall");
            Assert.IsTrue(expected.Why.Length > 0);
        }

        #endregion

        #region the sheet under test

        /// <summary>
        /// A sheet built the way the reference workbook builds one: a title, a heading with
        /// the legend column between the number and the value, and a value column carrying
        /// bare numbers and per metre run numbers together.
        /// </summary>
        private static FakeSheet TowerSheet()
        {
            FakeSheet sheet = new FakeSheet();

            sheet.Put(2, 2, "TOWER - (L03) TO (TOP OF ROOF)");

            sheet.Put(3, 2, "SL NO.");
            sheet.Put(3, 3, "LEGEND");
            sheet.Put(3, 4, "SIDL (kN/m2 )\nLOAD ON ELEVATION");
            sheet.Put(3, 5, "DESCRIPTION");
            sheet.Put(3, 6, "Remarks");

            Row(sheet, 4, 1, "4.8",
                "ALLOWANCE FOR 200mmTHICK SOLID BLOCK WALL WITH DENSITY OF 2100kg/m3 + 15mm PLASTER");
            Row(sheet, 5, 2, "2.4", "ALLOWANCE FOR 100mm THICK SOLID BLOCK WALL");
            Row(sheet, 6, 3, "1",
                "ALLOWANCE FOR FULL HEIGHT FAÇADE GLAZING SYSTEM INCLUDING ALL FIXING AND SUPPORTS");
            Row(sheet, 7, 4, "5", "ALLOWANCE FOR 200mm THICK SECOND CAST WALL");
            Row(sheet, 8, 5, "1.50 kN/m'", "ALLOWANCE FOR GLASS BALUSTRADE / RAILING");

            return sheet;
        }

        private static void Row(FakeSheet sheet, int row, int number, string value, string description)
        {
            sheet.Put(row, 2, number.ToString());
            sheet.Put(row, 4, value);
            sheet.Put(row, 5, description);
        }

        private static LineLoadSchedule Read(FakeSheet sheet)
        {
            LineLoadSchedule schedule = new LineLoadSchedule("", "Line Load D (TOWER)");
            sheet.ReadInto(schedule);
            return schedule;
        }

        /// <summary>
        /// A sheet written out as the xml part of a workbook and read back through the real
        /// reader, so the cell reading is exercised along with the parsing.
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

            public void ReadInto(LineLoadSchedule schedule)
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

                LineLoadScheduleReader.Read(sheet, schedule);
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
