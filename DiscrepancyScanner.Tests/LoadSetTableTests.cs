using System;
using System.Collections.Generic;
using System.Linq;
using DiscrepancyScanner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading shell uniform load sets out of the ETABS tables.
    ///
    /// A shell can carry loads assigned straight to it, a load set, or both, and the API has
    /// no word for the second - so a shell carrying only a set read as carrying nothing and
    /// was reported as failing its loading plan. That is a wrong verdict rather than a blank
    /// one, which is what makes this worth pinning down.
    ///
    /// The rows here are the ones out of a real model, columns and all.
    /// </summary>
    [TestClass]
    public class LoadSetTableTests
    {
        // TABLE: Shell Uniform Load Sets
        private static readonly string[] DefinitionFields = { "Name", "LoadPattern", "LoadValue", "GUID" };

        private static readonly string[] DefinitionData =
        {
            "-GROUND ASSUMPTIONS",       "SIDL", "10",  "ee360788-1c11-46e9-ac8d-8f56748d196a",
            "-GROUND ASSUMPTIONS",       "MECH", "15",  "ee360788-1c11-46e9-ac8d-8f56748d196a",
            "-LANDSCAPE-L17-ASSUMPTIONS", "SIDL", "15", "3c1dcef0-2ace-4256-99cf-f425a4dd5963",
            "-LANDSCAPE-L17-ASSUMPTIONS", "MECH", "5",  "3c1dcef0-2ace-4256-99cf-f425a4dd5963",
            "GRP TANK",                  "SIDL", "6.5", "50e55710-6b84-4c6e-a2be-87cd90499fbf"
        };

        // TABLE: Area Load Assignments - Uniform Load Sets
        private static readonly string[] AssignmentFields = { "Story", "Label", "UniqueName", "LoadSet" };

        private static readonly string[] AssignmentData =
        {
            "TOR", "F113", "170639", "GRP TANK",
            "TOR", "F114", "170640", "-GROUND ASSUMPTIONS",
            "TOR", "F115", "170641", "-LANDSCAPE-L17-ASSUMPTIONS"
        };

        private static List<string> NoIssues()
        {
            return new List<string>();
        }

        // ---------------------------------------------------------------- the definitions

        [TestMethod]
        public void ASetGathersEveryPatternItPutsOn()
        {
            Dictionary<string, List<LoadSetTables.PatternValue>> sets = LoadSetTables.Definitions(
                DefinitionFields, DefinitionData, 5, NoIssues());

            Assert.AreEqual(3, sets.Count, "three named sets across five rows");

            List<LoadSetTables.PatternValue> ground = sets["-GROUND ASSUMPTIONS"];
            Assert.AreEqual(2, ground.Count, "one row per load pattern in the set");

            Assert.AreEqual("SIDL", ground[0].Pattern);
            Assert.AreEqual(10, ground[0].Value, 1e-9);
            Assert.AreEqual("MECH", ground[1].Pattern);
            Assert.AreEqual(15, ground[1].Value, 1e-9);
        }

        [TestMethod]
        public void AFractionalLoadIsReadAsWritten()
        {
            Dictionary<string, List<LoadSetTables.PatternValue>> sets = LoadSetTables.Definitions(
                DefinitionFields, DefinitionData, 5, NoIssues());

            Assert.AreEqual(6.5, sets["GRP TANK"].Single().Value, 1e-9);
        }

        // ---------------------------------------------------------------- the assignments

        [TestMethod]
        public void EachAreaIsFoundByItsUniqueNameNotItsLabel()
        {
            // Two areas on different stories share a label; only the unique name is unique,
            // and the unique name is what the rest of the import knows an area by.
            Dictionary<string, string> assigned = LoadSetTables.Assignments(
                AssignmentFields, AssignmentData, 3, NoIssues());

            Assert.AreEqual("GRP TANK", assigned["170639"]);
            Assert.AreEqual("-GROUND ASSUMPTIONS", assigned["170640"]);
            Assert.IsFalse(assigned.ContainsKey("F113"), "the label is not the key");
        }

        // ---------------------------------------------------------------- reading the flat array

        [TestMethod]
        public void ColumnsAreFoundByNameSoAnExtraColumnDoesNotShiftTheRead()
        {
            // The same table with a column inserted in front. Counting along the row would
            // read the story as the name; finding the column by its key still works.
            string[] fields = { "ExtraThing", "Name", "LoadPattern", "LoadValue", "GUID" };
            string[] data = { "x", "SET A", "SIDL", "4", "guid" };

            Dictionary<string, List<LoadSetTables.PatternValue>> sets =
                LoadSetTables.Definitions(fields, data, 1, NoIssues());

            Assert.AreEqual(4, sets["SET A"].Single().Value, 1e-9);
        }

        [TestMethod]
        public void ColumnNamesAreMatchedHoweverTheyArePunctuated()
        {
            Assert.AreEqual(0, LoadSetTables.ColumnOf(new[] { "Unique Name", "Load Set" }, "UniqueName"));
            Assert.AreEqual(1, LoadSetTables.ColumnOf(new[] { "Unique Name", "Load Set" }, "LoadSet"));
            Assert.AreEqual(-1, LoadSetTables.ColumnOf(new[] { "Story", "Label" }, "UniqueName"));
        }

        [TestMethod]
        public void ATableWithoutTheColumnsSaysSoRatherThanReadingRubbish()
        {
            List<string> issues = NoIssues();

            Dictionary<string, string> assigned = LoadSetTables.Assignments(
                new[] { "Story", "Label" }, new[] { "TOR", "F113" }, 1, issues);

            Assert.AreEqual(0, assigned.Count);
            Assert.AreEqual(1, issues.Count, "and it is reported, not swallowed");
            StringAssert.Contains(issues[0], "Story, Label", "the columns it did find are named");
        }

        [TestMethod]
        public void AShortLastRowIsDroppedRatherThanReadOffTheEnd()
        {
            // ETABS reports a record count; the array is trusted no further than it goes.
            Dictionary<string, string> assigned = LoadSetTables.Assignments(
                AssignmentFields, new[] { "TOR", "F113", "170639", "GRP TANK", "TOR", "F114" },
                3, NoIssues());

            Assert.AreEqual(1, assigned.Count);
        }

        // ---------------------------------------------------------------- units

        [TestMethod]
        public void LoadValuesAreConvertedFromWhateverUnitsTheTableIsShownIn()
        {
            // The screenshot's own units.
            Assert.AreEqual(1, LoadSetTables.AreaLoadFactorFrom("kN/m2"), 1e-9);
            Assert.AreEqual(1, LoadSetTables.AreaLoadFactorFrom("kN/m²"), 1e-9, "the squared sign");
            Assert.AreEqual(1, LoadSetTables.AreaLoadFactorFrom("KN/M^2"), 1e-9);

            // 1 kgf/m2 is 9.80665 N/m2, which is 0.00980665 kN/m2.
            Assert.AreEqual(0.00980665, LoadSetTables.AreaLoadFactorFrom("kgf/m2"), 1e-12);

            // 1 N/mm2 is a megapascal, which is 1000 kN/m2.
            Assert.AreEqual(1000, LoadSetTables.AreaLoadFactorFrom("N/mm2"), 1e-9);
        }

        [TestMethod]
        public void UnitsThatCannotBeMadeOutAreRefusedRatherThanGuessedAt()
        {
            // Being out by a factor of a thousand is silent and worse than not reading at all,
            // so anything unrecognised answers negative and the caller stops.
            Assert.IsTrue(LoadSetTables.AreaLoadFactorFrom("furlong/fortnight") < 0);
            Assert.IsTrue(LoadSetTables.AreaLoadFactorFrom("kN") < 0, "no length at all");
            Assert.IsTrue(LoadSetTables.AreaLoadFactorFrom("") < 0);
            Assert.IsTrue(LoadSetTables.AreaLoadFactorFrom(null) < 0);
        }

        // ---------------------------------------------------------------- the join, end to end

        [TestMethod]
        public void AnAreaEndsUpCarryingTheLoadsItsSetPutsOn()
        {
            Dictionary<string, List<LoadSetTables.PatternValue>> sets = LoadSetTables.Definitions(
                DefinitionFields, DefinitionData, 5, NoIssues());

            Dictionary<string, string> assigned = LoadSetTables.Assignments(
                AssignmentFields, AssignmentData, 3, NoIssues());

            List<LoadSetTables.PatternValue> onArea = sets[assigned["170641"]];

            CollectionAssert.AreEqual(new[] { "SIDL", "MECH" }, onArea.Select(l => l.Pattern).ToArray());
            CollectionAssert.AreEqual(new[] { 15d, 5d }, onArea.Select(l => l.Value).ToArray());
        }

        [TestMethod]
        public void TheSetsNamesAreTheOnesTheSettingsAlreadyKnow()
        {
            // The patterns a load set names are SIDL and MECH, and those have to land on the
            // SDL and MEP of the loading schedule or the whole read is for nothing.
            LoadPatternMap.Mapping mapping = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData());

            Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SIDL"));
            Assert.AreEqual(LoadCategory.Mep, mapping.Categorise("MECH"));
            Assert.AreEqual(LoadCategory.Ll, mapping.Categorise("LL"));
            Assert.AreEqual(LoadCategory.Lr, mapping.Categorise("LLR"));
        }

        [TestMethod]
        public void SidlCIsAPatternOfItsOwnRatherThanTheSidl()
        {
            // SIDL_C carries the name SIDL inside it, so the match inside a longer name would
            // read it as superimposed dead load and add a load the schedule never asked for.
            LoadPatternMap.Mapping mapping = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData());

            Assert.AreEqual(LoadCategory.None, mapping.Categorise("SIDL_C"));
            Assert.AreEqual(LoadCategory.None, mapping.Categorise("sidl c"));
            Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SIDL"));
        }

        [TestMethod]
        public void AnExcludedPatternIsReportedRatherThanCountedAsNothing()
        {
            // Left out of the sum, but named on the zone: a load nobody mapped is exactly the
            // load that goes missing without a word.
            LoadPatternMap.Mapping mapping = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData());
            AreaLoad_E[] loads =
            {
                new AreaLoad_E("SIDL", 4, 10, true, false),
                new AreaLoad_E("SIDL_C", 3, 10, true, false)
            };

            AreaLoadSummary summary = AreaLoadSummary.From(loads, mapping);

            Assert.AreEqual(4d, summary.Loads.Sdl, 1e-9);
            CollectionAssert.AreEqual(new[] { "SIDL_C" }, summary.UnmappedPatterns.ToArray());
        }
    }
}
