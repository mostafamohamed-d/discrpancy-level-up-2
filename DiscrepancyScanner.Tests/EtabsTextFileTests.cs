using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DiscrepancyScanner;
using EtabsTextModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading the shell uniform load sets out of the ETABS model text file - the .$et written
    /// beside the model on every save - rather than out of the model's own tables.
    ///
    /// The whole reason for it is speed, and the whole risk in it is reading the file wrongly
    /// and getting a plausible number rather than an obviously broken one. So what is pinned
    /// down here is not that the happy path works: it is that the section walk ends where it
    /// should, that a line of another kind in the same section is not mistaken for one of
    /// these, and that a line straddling the read buffer still reads.
    ///
    /// The lines are the ones out of a real model, spacing and all.
    /// </summary>
    [TestClass]
    public class EtabsTextFileTests
    {
        #region a file to read

        private readonly List<string> written = new List<string>();

        /// <summary>
        /// Writes the lines to a real file and hands back the path. A real file rather than a
        /// stream, because opening it, sharing it and reading it in buffered chunks is a good
        /// half of what is being tested.
        /// </summary>
        private string File_(string contents)
        {
            string path = Path.Combine(Path.GetTempPath(),
                                       "etabs-text-" + Guid.NewGuid().ToString("N") + ".$et");

            // No byte order mark: ETABS does not write one, and a reader that only copes with
            // a file that has one would fail on every real model.
            System.IO.File.WriteAllText(path, contents,
                                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            written.Add(path);
            return path;
        }

        [TestCleanup]
        public void RemoveTheFiles()
        {
            foreach (string path in written)
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch (IOException)
                {
                    // A temp file left behind is not a failed test.
                }
            }
        }

        /// <summary>A small model, written the way ETABS writes one, with CRLF line endings.</summary>
        private const string Model =
            "$ File C:\\Models\\Tower.$et saved 29/08/2026 8:35:03 PM\r\n" +
            "\r\n" +
            "$ PROGRAM INFORMATION\r\n" +
            "  PROGRAM  \"ETABS\"  VERSION \"22.3.0\"  \r\n" +
            "\r\n" +
            "$ CONTROLS\r\n" +
            "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
            "  TITLE2  \"TOWER B\"  \r\n" +
            "\r\n" +
            "$ SHELL UNIFORM LOAD SETS\r\n" +
            "  SHELLUNIFORMLOADSET \"T1-RESIDENTIAL\"  LOADPAT \"SIDL\"  VALUE 4\r\n" +
            "  SHELLUNIFORMLOADSET \"T1-RESIDENTIAL\"  LOADPAT \"LLR\"  VALUE 2\r\n" +
            "  SHELLUNIFORMLOADSET \"T3-STAIRS\"  LOADPAT \"SIDL\"  VALUE 2.375\r\n" +
            "  SHELLUNIFORMLOADSET \"T3-STAIRS\"  LOADPAT \"LLR\"  VALUE 5\r\n" +
            "\r\n" +
            "$ SHELL OBJECT LOADS\r\n" +
            "  AREALOAD  \"W22\"  \"L01\"  TYPE \"TEMP\"  LC \"TEMP+\"  T 20\r\n" +
            "  AREALOAD  \"F1\"  \"L01\"  TYPE \"UNIFLOADSET\"  \"T1-RESIDENTIAL\"  \r\n" +
            "  AREALOAD  \"F2\"  \"L01\"  TYPE \"UNIFF\"  DIR \"GRAV\"  LC \"Dead\"  FVAL 5\r\n" +
            "  AREALOAD  \"F1\"  \"L02\"  TYPE \"UNIFLOADSET\"  \"T3-STAIRS\"  \r\n" +
            "\r\n" +
            "$ ANALYSIS OPTIONS\r\n" +
            "  ACTIVEDOF \"UX UY UZ RX RY RZ\"  \r\n" +
            "\r\n" +
            "$ END OF MODEL FILE\r\n";

        #endregion

        // ------------------------------------------------------------------ the happy path

        [TestMethod]
        public void ReadsEveryLineOfEverySetItIsAskedFor()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Assert.AreEqual(4, tables.ShellUniformLoadSets.Count, "a row per pattern, not per set");

            ShellUniformLoadSetRow stairs = tables.ShellUniformLoadSets
                                                  .Single(r => r.Name == "T3-STAIRS" && r.LoadPattern == "SIDL");

            Assert.AreEqual(2.375, stairs.Value, 1e-9, "the decimal is read as written");
        }

        [TestMethod]
        public void NamesEachShellByItsLabelAndItsStory()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Assert.AreEqual(2, tables.ShellLoadSetAssignments.Count);

            // The same label on two stories is two different shells, which is the whole reason
            // the story is carried alongside the label rather than the label being the key.
            ShellLoadSetAssignmentRow first = tables.ShellLoadSetAssignments[0];
            ShellLoadSetAssignmentRow second = tables.ShellLoadSetAssignments[1];

            Assert.AreEqual("F1", first.Label);
            Assert.AreEqual("L01", first.Story);
            Assert.AreEqual("T1-RESIDENTIAL", first.LoadSet);

            Assert.AreEqual("F1", second.Label);
            Assert.AreEqual("L02", second.Story);
            Assert.AreEqual("T3-STAIRS", second.LoadSet);
        }

        [TestMethod]
        public void ReadsTheUnitsTheWholeFileIsWrittenIn()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Assert.AreEqual("KN", tables.Units.Force);
            Assert.AreEqual("M", tables.Units.Length);
            Assert.IsTrue(tables.Units.AreKnown);
        }

        [TestMethod]
        public void SaysWhichTablesTheFileActuallyHad()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Assert.IsTrue(tables.Has(EtabsTable.ShellUniformLoadSets));
            Assert.IsTrue(tables.Has(EtabsTable.ShellLoadSetAssignments));
            Assert.IsTrue(tables.Has(EtabsTable.Controls));
        }

        [TestMethod]
        public void ReadsNothingItWasNotAskedFor()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model), EtabsTable.ShellUniformLoadSets);

            Assert.AreEqual(4, tables.ShellUniformLoadSets.Count);
            Assert.AreEqual(0, tables.ShellLoadSetAssignments.Count,
                            "the section holding these is most of a real file - not walking it is the point");
            Assert.IsFalse(tables.Has(EtabsTable.ShellLoadSetAssignments));
        }

        // ------------------------------------------------------------- where a section ends

        [TestMethod]
        public void ASectionEndsAtTheNextHeaderAndNotBefore()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            // ANALYSIS OPTIONS follows the loads and is not asked for. Nothing out of it may
            // arrive, and nothing before it may be lost.
            Assert.AreEqual(2, tables.ShellLoadSetAssignments.Count);
            Assert.AreEqual(0, tables.Issues.Count);
        }

        [TestMethod]
        public void ADollarInsideANameDoesNotEndTheSection()
        {
            // A header is a dollar in the first column. A name is inside quotes, and quotes are
            // never in the first column, so nothing here needs to count quotes to tell them
            // apart - which is worth a test, because getting it wrong loses the rest of a
            // section silently.
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ SHELL UNIFORM LOAD SETS\r\n" +
                "  SHELLUNIFORMLOADSET \"$ SHELL OBJECT LOADS\"  LOADPAT \"SIDL\"  VALUE 4\r\n" +
                "  SHELLUNIFORMLOADSET \"COST $ PER M2\"  LOADPAT \"LLR\"  VALUE 2\r\n" +
                "$ END OF MODEL FILE\r\n";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            Assert.AreEqual(2, tables.ShellUniformLoadSets.Count,
                            "the line after the odd name is still in the section");
            Assert.AreEqual("$ SHELL OBJECT LOADS", tables.ShellUniformLoadSets[0].Name);
        }

        [TestMethod]
        public void AMissingSectionIsNotAnEmptyOne()
        {
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ END OF MODEL FILE\r\n";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            Assert.IsFalse(tables.Has(EtabsTable.ShellUniformLoadSets),
                           "a model with no load sets has no section for them, which is not a failure");
            Assert.AreEqual(0, tables.ShellUniformLoadSets.Count);
            Assert.AreEqual(0, tables.Issues.Count);
        }

        // -------------------------------------------------- the other lines in that section

        [TestMethod]
        public void LoadsOfEveryOtherKindInTheSectionAreLeftAlone()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Assert.IsFalse(tables.ShellLoadSetAssignments.Any(a => a.Label == "W22"),
                           "a temperature load is not a load set");
            Assert.IsFalse(tables.ShellLoadSetAssignments.Any(a => a.Label == "F2"),
                           "a uniform load assigned straight to the shell is not a load set either");
        }

        [TestMethod]
        public void ALoadCaseNamedAfterTheKeywordIsNotAnAssignment()
        {
            // The section is walked by looking for the word in the raw bytes, because that
            // throws out nineteen lines in twenty without allocating anything. This is the case
            // that search gets wrong on its own, so the type is read back and checked.
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ SHELL OBJECT LOADS\r\n" +
                "  AREALOAD  \"F9\"  \"L01\"  TYPE \"UNIFF\"  DIR \"GRAV\"  LC \"UNIFLOADSET\"  FVAL 5\r\n" +
                "  AREALOAD  \"F8\"  \"L01\"  TYPE \"UNIFLOADSET\"  \"T1-RESIDENTIAL\"  \r\n" +
                "$ END OF MODEL FILE\r\n";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            Assert.AreEqual(1, tables.ShellLoadSetAssignments.Count,
                            "only the line that really is one");
            Assert.AreEqual("F8", tables.ShellLoadSetAssignments[0].Label);
        }

        // --------------------------------------------------------------- how the file is written

        [TestMethod]
        public void ReadsAFileWrittenWithBareLineFeeds()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model.Replace("\r\n", "\n")));

            Assert.AreEqual(4, tables.ShellUniformLoadSets.Count);
            Assert.AreEqual(2, tables.ShellLoadSetAssignments.Count);
        }

        [TestMethod]
        public void ReadsALastLineWithNoNewlineAfterIt()
        {
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ SHELL UNIFORM LOAD SETS\r\n" +
                "  SHELLUNIFORMLOADSET \"T1\"  LOADPAT \"SIDL\"  VALUE 4";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            Assert.AreEqual(1, tables.ShellUniformLoadSets.Count,
                            "a file that was cut short still has a last line");
        }

        [TestMethod]
        public void KeepsANameWithCharactersOutsideAscii()
        {
            // ETABS writes the file as UTF-8. Read as ASCII, a name like this comes back as
            // question marks - and then every name with a character in it collides with every
            // other, and the join between the two tables quietly stops working.
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ SHELL UNIFORM LOAD SETS\r\n" +
                "  SHELLUNIFORMLOADSET \"T1 \u2013 RESIDENTIAL\"  LOADPAT \"SIDL\"  VALUE 4\r\n" +
                "$ END OF MODEL FILE\r\n";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            Assert.AreEqual("T1 \u2013 RESIDENTIAL", tables.ShellUniformLoadSets[0].Name);
        }

        [TestMethod]
        public void ReadsALineThatStraddlesTheReadBuffer()
        {
            // The file is walked a megabyte at a time, so on a real model thousands of lines
            // land across the join between two reads and have to be stitched back together.
            // This writes enough to cross it several times over.
            StringBuilder model = new StringBuilder();
            model.Append("$ CONTROLS\r\n  UNITS  \"KN\"  \"M\"  \"C\"  \r\n");
            model.Append("$ SHELL OBJECT LOADS\r\n");

            const int Lines = 40000;

            for (int i = 0; i < Lines; i++)
            {
                model.Append("  AREALOAD  \"F").Append(i).Append("\"  \"L01\"  TYPE \"TEMP\"  LC \"TEMP+\"  T 20\r\n");
                model.Append("  AREALOAD  \"F").Append(i).Append("\"  \"L01\"  TYPE \"UNIFLOADSET\"  \"T1-RESIDENTIAL\"  \r\n");
            }

            model.Append("$ END OF MODEL FILE\r\n");

            string path = File_(model.ToString());
            Assert.IsTrue(new FileInfo(path).Length > 2 * 1024 * 1024, "big enough to cross the buffer");

            EtabsTextTables tables = EtabsTextFile.Read(path);

            Assert.AreEqual(Lines, tables.ShellLoadSetAssignments.Count, "not one line lost at a join");
            Assert.AreEqual("F0", tables.ShellLoadSetAssignments.First().Label);
            Assert.AreEqual("F" + (Lines - 1), tables.ShellLoadSetAssignments.Last().Label);
        }

        // ----------------------------------------------------------------- where the file is

        [TestMethod]
        public void TheTextFileIsBesideTheModelAndNamedAfterIt()
        {
            Assert.AreEqual(@"C:\Models\Tower.$et", EtabsTextFile.Beside(@"C:\Models\Tower.EDB"));
            Assert.AreEqual(@"C:\Models\A B v2.3 R55.$et", EtabsTextFile.Beside(@"C:\Models\A B v2.3 R55.EDB"),
                            "the dots in a model name are part of the name, not the extension");
            Assert.IsNull(EtabsTextFile.Beside(null), "a model that has never been saved has no file beside it");
            Assert.IsNull(EtabsTextFile.Beside(""));
        }

        // --------------------------------------------------------------------- what it is in

        [TestMethod]
        public void TheUnitsOffTheFileScaleTheSameWayTheTableUnitsDo()
        {
            // The file says its force and its length apart from each other where the table says
            // "kN/m2" in one string. Same question, same answer, or the two routes would give
            // two different loads for the same model.
            Assert.AreEqual(1d, LoadSetTables.AreaLoadFactorFrom("KN", "M"), 1e-9);
            Assert.AreEqual(LoadSetTables.AreaLoadFactorFrom("kN/m2"),
                            LoadSetTables.AreaLoadFactorFrom("KN", "M"), 1e-9);

            // 1 N over a square millimetre is a thousand kN over a square metre. Getting this
            // wrong is silent and out by a factor of a thousand.
            Assert.AreEqual(1000d, LoadSetTables.AreaLoadFactorFrom("N", "mm"), 1e-6);

            Assert.IsTrue(LoadSetTables.AreaLoadFactorFrom("bananas", "M") <= 0,
                          "a unit this cannot convert refuses rather than guesses");
        }

        // ------------------------------------------------------------------- $ AREA ASSIGNS

        /// <summary>
        /// A row per area in the model, the way both of the real models write it: the section
        /// always, and a pier, a spandrel or a diaphragm on the areas that have one.
        /// </summary>
        private const string Areas =
            "$ CONTROLS\r\n" +
            "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
            "$ AREA ASSIGNS\r\n" +
            "  AREAASSIGN  \"W22\"  \"L01\"  SECTION \"W300-C65\"  PIER  \"T-CW18A\"  OBJMESHTYPE \"AUTOMESH\"  MESHVERT 3 MESHHORIZ 1 ADDRESTRAINT \"Yes\"  CARDINALPOINT \"MIDDLE\"  TRANSFORMSTIFFNESSFOROFFSETS \"No\"  \r\n" +
            "  AREAASSIGN  \"F1\"  \"L01\"  SECTION \"PT250-C50\"  DIAPH  \"D1\"  OBJMESHTYPE \"NOAUTOMESH\"  ADDRESTRAINT \"No\"  CARDINALPOINT \"TOP\"  TRANSFORMSTIFFNESSFOROFFSETS \"No\"  \r\n" +
            "  AREAASSIGN  \"W9\"  \"L02\"  SECTION \"W400-C40\"  ANG 90 SPANDREL  \"S1\"  OBJMESHTYPE \"AUTOMESH\"  PROPMODF11 0.35 PROPMODF22 0.35 PROPMODF12 0.35 ADDRESTRAINT \"Yes\"  CARDINALPOINT \"MIDDLE\"  TRANSFORMSTIFFNESSFOROFFSETS \"No\"  \r\n" +
            "$ END OF MODEL FILE\r\n";

        [TestMethod]
        public void ReadsTheSectionOnEveryArea()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Areas));

            Assert.AreEqual(3, tables.AreaAssigns.Count);
            Assert.IsTrue(tables.Has(EtabsTable.AreaAssigns));

            AreaAssignRow floor = tables.AreaAssigns.Single(a => a.Label == "F1");
            Assert.AreEqual("L01", floor.Story);
            Assert.AreEqual("PT250-C50", floor.Section, "this is what replaces a call per area");
            Assert.AreEqual("D1", floor.Diaphragm);
        }

        [TestMethod]
        public void ReadsThePierOrSpandrelWhereThereIsOneAndNothingWhereThereIsNot()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Areas));

            AreaAssignRow wall = tables.AreaAssigns.Single(a => a.Label == "W22");
            Assert.AreEqual("T-CW18A", wall.Pier);
            Assert.IsTrue(string.IsNullOrEmpty(wall.Spandrel), "a wall on a pier is not on a spandrel");
            Assert.IsTrue(string.IsNullOrEmpty(wall.Diaphragm));

            AreaAssignRow spandrel = tables.AreaAssigns.Single(a => a.Label == "W9");
            Assert.AreEqual("S1", spandrel.Spandrel);
            Assert.AreEqual("W400-C40", spandrel.Section,
                            "the numbers between the section and the spandrel are stepped over");
            Assert.IsTrue(string.IsNullOrEmpty(spandrel.Pier));
        }

        [TestMethod]
        public void AKeywordInsideASectionNameIsNotReadAsThatKeyword()
        {
            // The section is written before the pier, so looking for "PIER" anywhere along the
            // line finds it inside a section named this and reads the section name as the pier.
            // What makes that worth a test rather than a shrug is that the wrong value is a
            // real name, so nothing further on would ever notice.
            string model =
                "$ CONTROLS\r\n" +
                "  UNITS  \"KN\"  \"M\"  \"C\"  \r\n" +
                "$ AREA ASSIGNS\r\n" +
                "  AREAASSIGN  \"W1\"  \"L01\"  SECTION \"PIER300-C40\"  OBJMESHTYPE \"AUTOMESH\"  \r\n" +
                "  AREAASSIGN  \"W2\"  \"L01\"  SECTION \"SPANDREL-DIAPH-250\"  PIER  \"P7\"  \r\n" +
                "$ END OF MODEL FILE\r\n";

            EtabsTextTables tables = EtabsTextFile.Read(File_(model));

            AreaAssignRow first = tables.AreaAssigns.Single(a => a.Label == "W1");
            Assert.AreEqual("PIER300-C40", first.Section);
            Assert.IsTrue(string.IsNullOrEmpty(first.Pier), "the section name is not a pier");

            AreaAssignRow second = tables.AreaAssigns.Single(a => a.Label == "W2");
            Assert.AreEqual("SPANDREL-DIAPH-250", second.Section);
            Assert.AreEqual("P7", second.Pier, "the real pier is still found");
            Assert.IsTrue(string.IsNullOrEmpty(second.Spandrel));
            Assert.IsTrue(string.IsNullOrEmpty(second.Diaphragm));
        }

        [TestMethod]
        public void TheNamesThatRepeatAreKeptOnce()
        {
            // A tower shares a few dozen section names between a hundred and seventy thousand
            // areas. Keeping one string per name rather than one per row is the difference
            // between a few kilobytes and tens of megabytes held for as long as the caller
            // holds the tables.
            StringBuilder model = new StringBuilder();
            model.Append("$ CONTROLS\r\n  UNITS  \"KN\"  \"M\"  \"C\"  \r\n$ AREA ASSIGNS\r\n");

            for (int i = 0; i < 500; i++)
            {
                model.Append("  AREAASSIGN  \"F").Append(i).Append("\"  \"L01\"  SECTION \"PT250-C50\"  DIAPH  \"D1\"  \r\n");
            }

            model.Append("$ END OF MODEL FILE\r\n");

            EtabsTextTables tables = EtabsTextFile.Read(File_(model.ToString()));

            Assert.AreEqual(500, tables.AreaAssigns.Count);

            string section = tables.AreaAssigns[0].Section;
            Assert.IsTrue(tables.AreaAssigns.All(a => ReferenceEquals(a.Section, section)),
                          "every row names the same string, not five hundred equal ones");
            Assert.AreEqual("PT250-C50", section);
        }

        [TestMethod]
        public void AreaAssignsAreOnlyWalkedWhenAskedFor()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Areas), EtabsTable.Controls);

            Assert.AreEqual(0, tables.AreaAssigns.Count);
            Assert.IsFalse(tables.Has(EtabsTable.AreaAssigns));
            Assert.AreEqual("KN", tables.Units.Force, "the units still come back");
        }

        // ------------------------------------------------- from the file's names to the API's

        /// <summary>
        /// The file calls a shell by its label and its story; the rest of the import calls it by
        /// the unique name ETABS gave it. This join is the whole of the difference between the
        /// two routes, and it is the one place where getting it wrong gives a wrong answer
        /// rather than no answer: a join that misses leaves the shell reading as carrying
        /// nothing, and a shell carrying nothing is reported as failing its loading plan.
        /// </summary>
        private static List<ShellLoadSetAssignmentRow> Rows(params string[] labelStorySet)
        {
            List<ShellLoadSetAssignmentRow> rows = new List<ShellLoadSetAssignmentRow>();

            for (int i = 0; i < labelStorySet.Length; i += 3)
            {
                rows.Add(new ShellLoadSetAssignmentRow
                {
                    Label = labelStorySet[i],
                    Story = labelStorySet[i + 1],
                    LoadSet = labelStorySet[i + 2]
                });
            }

            return rows;
        }

        [TestMethod]
        public void TheSameLabelOnTwoStoriesIsTwoDifferentShells()
        {
            // F1 on L01 and F1 on L02 are two areas with two unique names. Joining on the label
            // alone would give both of them whichever set was read last.
            string[] names = { "170639", "170640" };
            string[] labels = { "F1", "F1" };
            string[] stories = { "L01", "L02" };

            int unmatched;
            Dictionary<string, string> assigned = EtabsEngine.joinToAreaNames(
                Rows("F1", "L01", "T1-RESIDENTIAL",
                     "F1", "L02", "T3-STAIRS"),
                2, names, labels, stories, out unmatched);

            Assert.AreEqual(0, unmatched);
            Assert.AreEqual("T1-RESIDENTIAL", assigned["170639"]);
            Assert.AreEqual("T3-STAIRS", assigned["170640"]);
        }

        [TestMethod]
        public void ALabelAndAStoryCannotRunIntoEachOther()
        {
            // Label "F1 A" on story "B" and label "F1" on story "A B" are different shells, and
            // would be the same key if the two were simply written one after the other with a
            // space between. Hence the separator no name can hold.
            string[] names = { "1", "2" };
            string[] labels = { "F1 A", "F1" };
            string[] stories = { "B", "A B" };

            int unmatched;
            Dictionary<string, string> assigned = EtabsEngine.joinToAreaNames(
                Rows("F1 A", "B", "SET ONE",
                     "F1", "A B", "SET TWO"),
                2, names, labels, stories, out unmatched);

            Assert.AreEqual(0, unmatched);
            Assert.AreEqual(2, assigned.Count, "two shells, not one written over the other");
            Assert.AreEqual("SET ONE", assigned["1"]);
            Assert.AreEqual("SET TWO", assigned["2"]);
        }

        [TestMethod]
        public void ShellsTheModelHasNotGotAreCountedRatherThanDropped()
        {
            // This is the measure of whether an old file still describes this model, and it is
            // a better one than the file's date: a file saved an hour ago that still joins is
            // describing this model, and one that joins badly is describing another.
            string[] names = { "170639" };
            string[] labels = { "F1" };
            string[] stories = { "L01" };

            int unmatched;
            Dictionary<string, string> assigned = EtabsEngine.joinToAreaNames(
                Rows("F1", "L01", "T1-RESIDENTIAL",
                     "F900", "L44", "T3-STAIRS"),
                1, names, labels, stories, out unmatched);

            Assert.AreEqual(1, assigned.Count);
            Assert.AreEqual(1, unmatched, "the shell on a story this model has not got is counted");
        }

        [TestMethod]
        public void ASetGathersEveryPatternItPutsOnFromTheTextFile()
        {
            EtabsTextTables tables = EtabsTextFile.Read(File_(Model));

            Dictionary<string, List<LoadSetTables.PatternValue>> sets =
                EtabsEngine.definitionsFrom(tables);

            Assert.AreEqual(2, sets.Count, "two named sets across four rows");

            List<LoadSetTables.PatternValue> residential = sets["T1-RESIDENTIAL"];
            Assert.AreEqual(2, residential.Count, "one row per load pattern in the set");
            Assert.AreEqual(4d, residential.Single(p => p.Pattern == "SIDL").Value, 1e-9);
            Assert.AreEqual(2d, residential.Single(p => p.Pattern == "LLR").Value, 1e-9);
        }
    }
}
