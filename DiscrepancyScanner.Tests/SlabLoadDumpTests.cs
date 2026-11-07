using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The slab load dump: what the load check read and found on one level, written so it can
    /// be read back and the check run again without Revit. What matters is that the parts that
    /// exist nowhere else - the regions, their outlines and every number parameter each one
    /// carries - come back out exactly, and that the whole thing is json the reader takes.
    /// </summary>
    [TestClass]
    public class SlabLoadDumpTests
    {
        [TestMethod]
        public void TheRegionsTheScheduleAndThePiecesAllGoIn()
        {
            LoadSchedule schedule = new LoadSchedule("plan.xlsx", "Podium-Area Load");
            schedule.Add(new LoadScheduleEntry("47", "GOODS TRUCK ROUTE", new LoadSet { Sdl = 0.5, Ll = 20 }, "LL"));
            schedule.Add(new LoadScheduleEntry("7", "PAVING-TYPE 1", new LoadSet { Sdl = 3.5, Ll = 3.5 }, "LL"));

            LoadRegion_R region = new LoadRegion_R(9001, new[] { Square(0, 0, 4000) }, "7")
            {
                TypeName = "Loading - Paving",
                NumberSources = new List<string>
                {
                    "Loading Type Mark (type) = 7",
                    "Area Load Number (instance) = 47"
                }
            };

            SlabZone piece = new SlabZone(Square(0, 0, 4000), SlabZoneState.NotChecked, "GROUND")
            {
                Purpose = SlabMeshPurpose.Loads,
                EtabsName = "479",
                EtabsProperty = "S250-C50",
                LoadShells = new List<string> { "479" },
                EtabsLoads = new LoadSet { Sdl = 0.5, Ll = 20 },
                PlannedLoads = new LoadSet { Sdl = 3.5, Ll = 3.5 },
                LoadNumber = "7",
                LoadDescription = "PAVING-TYPE 1",
                LoadRegionId = 9001,
                LoadState = SlabLoadState.Mismatch,
                MismatchedLoads = new List<LoadCategory> { LoadCategory.Sdl, LoadCategory.Ll }
            };

            SlabLoadDump.Level level = new SlabLoadDump.Level
            {
                Story = "GROUND",
                ViewId = 1188254,
                ViewName = "GROUND FLOOR LOADING PLAN",
                Workbook = "plan.xlsx",
                Sheet = "Podium-Area Load",
                Schedule = schedule,
                Regions = new List<LoadRegion_R> { region },
                Shells = new List<Slab_E> { LoadArea() }
            };

            string text = Json.Write(SlabLoadDump.Build(level, new[] { piece }));
            Dictionary<string, object> read = Json.Read(text);

            Assert.AreEqual("GROUND FLOOR LOADING PLAN", read["viewName"]);

            List<object> rows = (List<object>)read["schedule"];
            Assert.AreEqual(2, rows.Count, "every schedule row");
            Assert.AreEqual("7", Map(rows[0])["number"], "in load number order");

            Dictionary<string, object> dumped = Map(((List<object>)read["regions"]).Single());
            Assert.AreEqual(9001d, dumped["id"]);
            Assert.AreEqual("7", dumped["number"]);
            CollectionAssert.AreEqual(new object[] { "Loading Type Mark (type) = 7", "Area Load Number (instance) = 47" },
                                      (List<object>)dumped["numberFrom"],
                                      "every number parameter the region carries, the one read first");

            List<object> outer = (List<object>)Map(((List<object>)dumped["parts"]).Single())["outer"];
            Assert.AreEqual(4, outer.Count, "the outline, corner by corner");

            Dictionary<string, object> dumpedPiece = Map(((List<object>)read["pieces"]).Single());
            Assert.AreEqual("479", dumpedPiece["shell"]);
            Assert.AreEqual("Mismatch", dumpedPiece["state"]);
            Assert.AreEqual(20d, Map(dumpedPiece["etabs"])["ll"]);
            Assert.AreEqual(3.5d, Map(dumpedPiece["plan"])["ll"]);
            Assert.AreEqual(9001d, dumpedPiece["region"]);

            // Each shell with what it carries on its own, the null areas laid over for load
            // among them: a piece only says what they add up to.
            Dictionary<string, object> shell = Map(((List<object>)read["shells"]).Single());
            Assert.AreEqual("N1", shell["name"]);
            Assert.AreEqual("LoadOnly", shell["kind"]);
            Dictionary<string, object> load = Map(((List<object>)shell["loads"]).Single());
            Assert.AreEqual("SIDL", load["pattern"]);
            Assert.AreEqual(3d, load["value"]);
            Assert.AreEqual(4, ((List<object>)Map(shell["outline"])["outer"]).Count);
        }

        private static Slab_E LoadArea()
        {
            Slab_E area = new Slab_E("N1", new[]
            {
                new MyPoint(0, 0, 0), new MyPoint(4000, 0, 0), new MyPoint(4000, 4000, 0), new MyPoint(0, 4000, 0)
            }.Select(point => new Joint("", point, Form1.eLength.mm)).ToList(), "GROUND", "None", SlabKind.LoadOnly, 0, 0, false);
            area.SetLoads(new List<AreaLoad_E> { new AreaLoad_E("SIDL", 3.0, 10, true, false) });
            return area;
        }

        private static Dictionary<string, object> Map(object value)
        {
            return (Dictionary<string, object>)value;
        }

        private static PlanPolygon Square(double x, double y, double size)
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(x, y, 0),
                new MyPoint(x + size, y, 0),
                new MyPoint(x + size, y + size, 0),
                new MyPoint(x, y + size, 0)
            });
        }
    }
}
