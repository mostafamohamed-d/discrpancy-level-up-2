using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DiscrepancyScanner.Wpf;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The load pattern mapping from both ends: what the schedule calls each load, which
    /// columns of it hold what when said by hand, and which ETABS pattern is which load.
    /// </summary>
    [TestClass]
    public class LoadPatternMappingTests
    {
        private const string Project = @"C:\jobs\tower.rvt";
        private const string Workbook = @"C:\jobs\loading schedule.xlsx";

        private Dictionary<string, string> saved;

        [TestInitialize]
        public void KeepPreferences()
        {
            PrefrencesData p = PrefrencesData.getPrefrencesData();
            saved = new Dictionary<string, string>
            {
                ["sidl"] = p.ScheduleSidlHeadingNames,
                ["total"] = p.ScheduleTotalHeadingNames,
                ["live"] = p.ScheduleLiveHeadingNames,
                ["ll"] = p.ScheduleLiveMarkings,
                ["lr"] = p.ScheduleReducibleLiveMarkings,
                ["mep"] = p.ScheduleMepMarkings,
                ["etabsSdl"] = p.SdlLoadPatternNames,
                ["etabsLl"] = p.LiveLoadPatternNames,
                ["etabsLr"] = p.ReducibleLiveLoadPatternNames,
                ["etabsMep"] = p.MepLoadPatternNames,
                ["etabsNever"] = p.ExcludedLoadPatternNames,
                ["etabsLine"] = p.LineLoadPatternNames,
                ["etabsSidlC"] = p.SidlCLoadPatternNames
            };
            ScheduleColumnOverrides.Current.ActiveProject = Project;
        }

        [TestCleanup]
        public void PutPreferencesBack()
        {
            PrefrencesData p = PrefrencesData.getPrefrencesData();
            p.ScheduleSidlHeadingNames = saved["sidl"];
            p.ScheduleTotalHeadingNames = saved["total"];
            p.ScheduleLiveHeadingNames = saved["live"];
            p.ScheduleLiveMarkings = saved["ll"];
            p.ScheduleReducibleLiveMarkings = saved["lr"];
            p.ScheduleMepMarkings = saved["mep"];
            p.SdlLoadPatternNames = saved["etabsSdl"];
            p.LiveLoadPatternNames = saved["etabsLl"];
            p.ReducibleLiveLoadPatternNames = saved["etabsLr"];
            p.MepLoadPatternNames = saved["etabsMep"];
            p.ExcludedLoadPatternNames = saved["etabsNever"];
            p.LineLoadPatternNames = saved["etabsLine"];
            p.SidlCLoadPatternNames = saved["etabsSidlC"];

            ScheduleColumnOverrides.Current.ClearProject(Project);
            ScheduleColumnOverrides.Current.ActiveProject = "";
        }

        #region what the schedule calls each load

        [TestMethod]
        public void TheDefaultsReadTheLiveMarkingsTheReaderAlwaysRead()
        {
            ScheduleNames names = ScheduleNames.Current;

            Assert.AreEqual(LoadCategory.Ll, names.LiveCategory(""));
            Assert.AreEqual(LoadCategory.Ll, names.LiveCategory("LL"));
            Assert.AreEqual(LoadCategory.Lr, names.LiveCategory("LLR"));
            Assert.AreEqual(LoadCategory.Lr, names.LiveCategory("lr"));
            Assert.AreEqual(LoadCategory.Lr, names.LiveCategory("Reduced"));
            Assert.AreEqual(LoadCategory.Lr, names.LiveCategory("ROOF"));
            Assert.AreEqual(LoadCategory.Mep, names.LiveCategory("MECH"));
            Assert.AreEqual(LoadCategory.Mep, names.LiveCategory("Plant room"));
            Assert.AreEqual(LoadCategory.Ll, names.LiveCategory("something else"));
        }

        [TestMethod]
        public void AMarkingRenamedInTheSettingsIsReadAsItsLoad()
        {
            PrefrencesData.getPrefrencesData().ScheduleReducibleLiveMarkings = "R";
            PrefrencesData.getPrefrencesData().ScheduleMepMarkings = "M";

            ScheduleNames names = ScheduleNames.Current;

            Assert.AreEqual(LoadCategory.Lr, names.LiveCategory("R"));
            Assert.AreEqual(LoadCategory.Mep, names.LiveCategory("M"));
            Assert.AreEqual(LoadCategory.Ll, names.LiveCategory("LLR"), "no longer named, so plain live load");
        }

        [TestMethod]
        public void AnAreaScheduleHeadedSdlIsReadOnceTheSettingsSaySo()
        {
            Sheet sheet = AreaSheet("SDL (kN/m2)", "SUM (kN/m2)");

            Assert.AreEqual(0, ReadArea(sheet).Count, "SUM is not a heading the defaults know");

            PrefrencesData.getPrefrencesData().ScheduleSidlHeadingNames = "SDL";
            PrefrencesData.getPrefrencesData().ScheduleTotalHeadingNames = "Sum";

            LoadScheduleEntry entry = ReadArea(sheet).Find("1");
            Assert.IsNotNull(entry);
            Assert.AreEqual(3, entry.Loads.Sdl, 1e-9);
            Assert.AreEqual(2, entry.Loads.Lr, 1e-9);
        }

        [TestMethod]
        public void ALineScheduleHeadedSdlIsReadOnceTheSettingsSaySo()
        {
            Sheet sheet = new Sheet();
            sheet.Put(3, 1, "SL NO.");
            sheet.Put(3, 2, "SDL (kN/m')");
            sheet.Put(3, 3, "DESCRIPTION");
            sheet.Put(4, 1, "1");
            sheet.Put(4, 2, "2.4");

            Assert.AreEqual(0, ReadLine(sheet).Count);

            PrefrencesData.getPrefrencesData().ScheduleSidlHeadingNames = "SIDL, SDL";

            Assert.AreEqual(2.4, ReadLine(sheet).Find(1).Value, 1e-9);
        }

        [TestMethod]
        public void TheColumnsReadAreSaidByRole()
        {
            LoadSchedule schedule = ReadArea(AreaSheet("SIDL (kN/m2)", "TOTAL (kN/m2)"));

            Assert.AreEqual(LoadScheduleReader.RoleNumber, schedule.ColumnRoles[1]);
            Assert.AreEqual(LoadScheduleReader.RoleDescription, schedule.ColumnRoles[2]);
            Assert.AreEqual(LoadScheduleReader.RoleLive, schedule.ColumnRoles[5]);
            Assert.AreEqual(LoadScheduleReader.RoleLiveKind, schedule.ColumnRoles[6]);
            StringAssert.StartsWith(schedule.ColumnRoles[4], LoadScheduleReader.RoleSidl);
        }

        #endregion

        #region columns said by hand

        [TestMethod]
        public void AColumnSaidByHandWinsOverTheHeading()
        {
            Sheet sheet = AreaSheet("SIDL (kN/m2)", "TOTAL (kN/m2)");
            sheet.Put(5, 7, "9");

            ScheduleColumnOverrides.Current.Set(Project, Workbook, "Area", ScheduleRole.Sidl, 7);

            LoadSchedule schedule = ReadArea(sheet);
            Assert.AreEqual(9, schedule.Find("1").Loads.Sdl, 1e-9);
            Assert.AreEqual(2, schedule.Find("1").Loads.Lr, 1e-9, "the rest still come from the headings");
        }

        [TestMethod]
        public void ColumnsSaidByHandReadASheetWhoseHeadingsCannotBeFound()
        {
            Sheet sheet = new Sheet();
            sheet.Put(1, 1, "Load");
            sheet.Put(1, 2, "What");
            sheet.Put(2, 1, "4");
            sheet.Put(2, 2, "PARAPET");
            sheet.Put(2, 3, "1.8");

            Assert.AreEqual(0, ReadLine(sheet).Count);

            ScheduleColumnOverrides.Current.Set(Project, Workbook, "Line", ScheduleRole.Number, 1);
            ScheduleColumnOverrides.Current.Set(Project, Workbook, "Line", ScheduleRole.Sidl, 3);

            LineLoadSchedule schedule = ReadLine(sheet);
            Assert.AreEqual(1.8, schedule.Find(4).Value, 1e-9);
            Assert.AreEqual(LoadScheduleReader.RoleSidl, schedule.ColumnRoles[3]);
        }

        [TestMethod]
        public void ColumnsSaidByHandBelongToTheirProject()
        {
            Sheet sheet = AreaSheet("SIDL (kN/m2)", "TOTAL (kN/m2)");
            sheet.Put(5, 7, "9");

            ScheduleColumnOverrides.Current.Set(@"C:\jobs\other.rvt", Workbook, "Area", ScheduleRole.Sidl, 7);
            try
            {
                Assert.AreEqual(3, ReadArea(sheet).Find("1").Loads.Sdl, 1e-9);
            }
            finally
            {
                ScheduleColumnOverrides.Current.ClearProject(@"C:\jobs\other.rvt");
            }
        }

        [TestMethod]
        public void AColumnHoldsOneRole()
        {
            ScheduleColumnOverrides columns = new ScheduleColumnOverrides();
            columns.Set(Project, Workbook, "Area", ScheduleRole.Sidl, 7);
            columns.Set(Project, Workbook, "Area", ScheduleRole.Live, 7);

            IDictionary<ScheduleRole, int> roles = columns.For(Project, Workbook, "Area");
            Assert.AreEqual(1, roles.Count);
            Assert.AreEqual(7, roles[ScheduleRole.Live]);
        }

        [TestMethod]
        public void ColumnsSaidByHandGoThroughTheSettingsFileAndHoldForTheSameFileElsewhere()
        {
            ScheduleColumnOverrides saved = new ScheduleColumnOverrides();
            saved.Set(Project, Workbook, "Area", ScheduleRole.Sidl, 7);
            saved.Set(Project, Workbook, "Area", ScheduleRole.LiveKind, 12);

            ScheduleColumnOverrides read = new ScheduleColumnOverrides();
            read.ReadFrom(Json.Read(Json.Write(new Dictionary<string, object>
            {
                { ScheduleColumnOverrides.Branch, saved.ToJson() }
            })));

            IDictionary<ScheduleRole, int> roles = read.For(Project, @"D:\copy\Loading Schedule.xlsx", "area");
            Assert.AreEqual(7, roles[ScheduleRole.Sidl]);
            Assert.AreEqual(12, roles[ScheduleRole.LiveKind]);
        }

        [TestMethod]
        public void ColumnsSaidByHandTravelInTheProjectFile()
        {
            CollectionAssert.Contains(ProjectFile.Branches, ScheduleColumnOverrides.Branch);
        }

        #endregion

        #region which ETABS pattern is which load

        private static LoadPatternsViewModel Patterns(params string[] names)
        {
            PrefrencesData p = PrefrencesData.getPrefrencesData();
            LoadPatternsViewModel model = new LoadPatternsViewModel(p, names.Select(n => new ModelLoadPattern(n, 10, 5)));
            model.SdlNames = "SDL, SIDL";
            model.LiveNames = "LL";
            model.ReducibleNames = "LLR";
            model.MepNames = "MEP";
            model.NeverNames = "SIDL_C";
            model.LineNames = "SIDL";
            model.SidlCNames = "SIDL_C";
            return model;
        }

        private static LoadPatternRow Row(LoadPatternsViewModel model, string name)
        {
            return model.Rows.Single(row => row.Name == name);
        }

        [TestMethod]
        public void EachPatternSaysWhatItIsReadAs()
        {
            LoadPatternsViewModel model = Patterns("SIDL", "SIDL_C", "LL", "WIND");

            Assert.AreEqual(LoadCategory.Sdl, Row(model, "SIDL").AreaChoice.Category);
            Assert.IsTrue(Row(model, "SIDL").LineRead);
            Assert.AreEqual(LoadCategory.None, Row(model, "SIDL_C").AreaChoice.Category);
            Assert.IsFalse(Row(model, "SIDL_C").LineRead);
            Assert.AreEqual(LoadCategory.Ll, Row(model, "LL").AreaChoice.Category);
            Assert.IsTrue(Row(model, "WIND").IsUnused);
        }

        [TestMethod]
        public void PickingALoadForAPatternWritesItsNameIntoThatList()
        {
            LoadPatternsViewModel model = Patterns("Finishes", "LL");

            Row(model, "Finishes").AreaChoice = model.Choices.Single(c => c.Category == LoadCategory.Sdl);

            StringAssert.Contains(model.SdlNames, "Finishes");
            Assert.AreEqual(LoadCategory.Sdl, Row(model, "Finishes").AreaChoice.Category);

            Row(model, "Finishes").AreaChoice = model.Choices.Single(c => c.Category == LoadCategory.Mep);

            Assert.IsFalse(model.SdlNames.Contains("Finishes"), "taken off the list it was on");
            Assert.AreEqual(LoadCategory.Mep, Row(model, "Finishes").AreaChoice.Category);
        }

        [TestMethod]
        public void APatternCaughtInsideALongerNameIsLeftOutByTheNeverList()
        {
            LoadPatternsViewModel model = Patterns("SIDL-EXT");
            Assert.AreEqual(LoadCategory.Sdl, Row(model, "SIDL-EXT").AreaChoice.Category);
            StringAssert.Contains(Row(model, "SIDL-EXT").Note, "inside its name");

            Row(model, "SIDL-EXT").AreaChoice = model.Choices.Single(c => c.Category == LoadCategory.None);

            StringAssert.Contains(model.NeverNames, "SIDL-EXT");
            Assert.AreEqual(LoadCategory.None, Row(model, "SIDL-EXT").AreaChoice.Category);
        }

        /// <summary>
        /// A SIDL_C pattern is none of the four and not a line load, without being on the never
        /// list - and is read by a check all the same, so not counted as unused.
        /// </summary>
        [TestMethod]
        public void ASidlCPatternIsNotTheSidl()
        {
            LoadPatternsViewModel model = Patterns("SIDL", "SIDL_LWC");
            model.SidlCNames = "SIDL_C, SIDL_LWC";

            LoadPatternRow row = Row(model, "SIDL_LWC");
            Assert.AreEqual(LoadCategory.None, row.AreaChoice.Category);
            Assert.IsFalse(row.LineRead);
            Assert.IsTrue(row.SidlCRead);
            Assert.IsFalse(row.IsUnused);
            StringAssert.Contains(row.Note, "SIDL_C");
            Assert.IsFalse(model.NeverNames.Contains("SIDL_LWC"));
        }

        /// <summary>Picked as the SIDL, a SIDL_C pattern is taken off the SIDL_C list - it cannot be both.</summary>
        [TestMethod]
        public void PickingASidlCPatternAsTheSidlTakesItOffTheSidlCList()
        {
            LoadPatternsViewModel model = Patterns("SIDL_LWC");
            model.SidlCNames = "SIDL_C, SIDL_LWC";

            Row(model, "SIDL_LWC").AreaChoice = model.Choices.Single(c => c.Category == LoadCategory.Sdl);

            Assert.AreEqual("SIDL_C", model.SidlCNames);
            Assert.AreEqual(LoadCategory.Sdl, Row(model, "SIDL_LWC").AreaChoice.Category);
            Assert.IsFalse(Row(model, "SIDL_LWC").SidlCRead);
        }

        [TestMethod]
        public void TickingALinePatternReadsIt()
        {
            LoadPatternsViewModel model = Patterns("SDL", "SIDL");

            Row(model, "SDL").LineRead = true;

            Assert.IsTrue(Row(model, "SDL").LineRead);
            StringAssert.Contains(model.LineNames, "SDL");

            Row(model, "SDL").LineRead = false;
            Assert.IsFalse(Row(model, "SDL").LineRead);
            Assert.IsTrue(Row(model, "SIDL").LineRead);
        }

        [TestMethod]
        public void OkWritesTheNamesOnBothSidesIntoTheSettings()
        {
            PrefrencesData p = PrefrencesData.getPrefrencesData();

            LoadPatternsViewModel model = Patterns();
            model.SdlNames = "SUPERDEAD";
            model.SidlHeading = "SDL";
            model.Apply(p);

            // Put back by the clean up, with every other name Apply wrote.
            Assert.AreEqual("SUPERDEAD", p.SdlLoadPatternNames);
            Assert.AreEqual("SDL", p.ScheduleSidlHeadingNames);
        }

        #endregion

        #region the sheets

        /// <summary>An area schedule: number, description, SIDL band, total, live load and its marking.</summary>
        private static Sheet AreaSheet(string sidlHeading, string totalHeading)
        {
            Sheet sheet = new Sheet();
            sheet.Put(4, 1, "SL. NO.");
            sheet.Put(4, 2, "DESCRIPTION");
            sheet.Put(4, 3, sidlHeading);
            sheet.Put(4, 4, totalHeading);
            sheet.Put(4, 5, "LIVE LOAD (kN/m2)");
            sheet.Put(4, 6, "LL");
            sheet.Put(5, 1, "1");
            sheet.Put(5, 2, "RESIDENTIAL");
            sheet.Put(5, 4, "3");
            sheet.Put(5, 5, "2");
            sheet.Put(5, 6, "LLR");
            return sheet;
        }

        private static LoadSchedule ReadArea(Sheet sheet)
        {
            LoadSchedule schedule = new LoadSchedule(Workbook, "Area");
            LoadScheduleReader.Read(sheet.Build("Area"), schedule);
            return schedule;
        }

        private static LineLoadSchedule ReadLine(Sheet sheet)
        {
            LineLoadSchedule schedule = new LineLoadSchedule(Workbook, "Line");
            LineLoadScheduleReader.Read(sheet.Build("Line"), schedule);
            return schedule;
        }

        private sealed class Sheet
        {
            private readonly SortedDictionary<int, SortedDictionary<int, string>> rows =
                new SortedDictionary<int, SortedDictionary<int, string>>();

            public void Put(int row, int column, string text)
            {
                SortedDictionary<int, string> cells;
                if (!rows.TryGetValue(row, out cells))
                {
                    cells = new SortedDictionary<int, string>();
                    rows[row] = cells;
                }
                cells[column] = text;
            }

            public XlsxSheet Build(string name)
            {
                StringBuilder xml = new StringBuilder(
                    "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

                foreach (var row in rows)
                {
                    xml.Append($"<row r=\"{row.Key}\">");
                    foreach (var cell in row.Value)
                    {
                        xml.Append($"<c r=\"{XlsxSheet.Reference(row.Key, cell.Key)}\" t=\"inlineStr\"><is><t>")
                           .Append(System.Security.SecurityElement.Escape(cell.Value))
                           .Append("</t></is></c>");
                    }
                    xml.Append("</row>");
                }

                xml.Append("</sheetData></worksheet>");
                return XlsxSheet.Read(name, XDocument.Parse(xml.ToString()), new List<string>());
            }
        }

        #endregion
    }
}
