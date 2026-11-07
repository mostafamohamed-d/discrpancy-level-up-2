using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A stiff area drawn over the slab, which is how SAFE marks a column or a wall standing in
    /// it and how an ETABS model can carry one too: a small, very thick shell - a thousand
    /// millimetres is usual - laid over the slab and the drop panel around the column.
    ///
    /// It is not slab. It is the column, and the Revit floor has a hole there because the
    /// column cuts it. Checked as slab it read as ETABS slab over a Revit hole at every column
    /// in the building, and every drop panel on the plan came out in the ETABS only colour,
    /// with the corners of the square reading a thousand against the drop.
    ///
    /// Only a stiff area stacked on another shell is read this way. One standing on its own
    /// is checked exactly as it always was: nothing says what it stands in for.
    ///
    /// The base mesh cuts overlapping shells apart with Revit's geometry kernel, which a test
    /// cannot load, so the faces here are cut by hand into the pieces it would make: the drop
    /// with the column's square taken out of it, and the slab with the drop taken out of it.
    /// </summary>
    [TestClass]
    public class StackedStiffAreaTests
    {
        private const string Story = "GROUND";

        // A 900 round column in a 3500 square drop in a 250 slab, drawn the way SAFE draws it:
        // the stiff area is the square of the same area as the column, so its corners stand
        // out past the round hole the column cuts in the Revit floor and the round hole
        // reaches past its sides.
        private const double Centre = 6000;
        private const double ColumnDiameter = 900;
        private const double StiffSide = 797;

        #region which stiff areas are supports

        [TestMethod]
        public void AStiffAreaOverTheSlabIsASupport()
        {
            Slab_E stiff = Stiff();
            HashSet<Slab_E> supports = SlabCompareEngine.StackedStiffAreas(
                new List<Slab_E> { MainSlab(), Drop(), stiff });

            CollectionAssert.AreEquivalent(new[] { stiff }, supports.ToList(),
                                           "the stiff area and nothing else - a drop over the slab is still slab");
        }

        [TestMethod]
        public void AStiffAreaStandingOnItsOwnIsNotASupport()
        {
            Assert.AreEqual(0, SlabCompareEngine.StackedStiffAreas(new List<Slab_E> { Stiff() }).Count);
        }

        [TestMethod]
        public void AStiffAreaOnlyTouchingTheSlabIsNotASupport()
        {
            // Beside the slab, sharing its edge: nothing is drawn on top of anything.
            Slab_E beside = Shell("K2", "STIFF", SlabKind.Stiff, 1000, 12000, 5000, 12800, 5800);

            Assert.AreEqual(0, SlabCompareEngine.StackedStiffAreas(new List<Slab_E> { MainSlab(), beside }).Count);
        }

        [TestMethod]
        public void AStiffAreaOverAnOpeningOnlyIsNotASupport()
        {
            Slab_E opening = new Slab_E("O1", Corners(5000, 5000, 7000, 7000).Select(Joint).ToList(),
                                        Story, "", SlabKind.Opening, 0, 0, true);

            Assert.AreEqual(0, SlabCompareEngine.StackedStiffAreas(new List<Slab_E> { opening, Stiff() }).Count);
        }

        #endregion

        #region the hole the column cuts

        [TestMethod]
        public void TheRoundHoleAroundASquareStiffAreaIsFilled()
        {
            PlanPolygon floor = WithColumnHole(Rectangle(0, 0, 12000, 12000));

            PlanPolygon filled = SlabCompareEngine.WithoutSupportHoles(floor, Supports());

            Assert.AreEqual(0, filled.Holes.Count);
            Assert.AreEqual(12000d * 12000d, filled.Area, 1);
        }

        [TestMethod]
        public void AShaftWithTheColumnInItsCornerIsLeftOpen()
        {
            // Two metres by one and a half, with the column standing in its corner: far bigger
            // than the column, so it is an opening ETABS has not got and is still reported.
            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                                   new[] { Shaft().Outer });

            PlanPolygon kept = SlabCompareEngine.WithoutSupportHoles(floor, Supports());

            Assert.AreEqual(1, kept.Holes.Count);
            Assert.AreSame(floor, kept, "nothing filled, so the floor is handed back as it was");
        }

        [TestMethod]
        public void TheHoleAWallCutsIsFilled()
        {
            // A 250 wall three metres long, and the floor cut to its outline.
            PlanPolygon wall = Rectangle(2000, 5000, 5000, 5250);
            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                                   new[] { wall.Outer });

            Assert.AreEqual(0, SlabCompareEngine.WithoutSupportHoles(floor, new[] { wall }).Holes.Count);
        }

        [TestMethod]
        public void AnOpeningRunningOnPastTheEndOfAWallIsLeftOpen()
        {
            // The same wall, with the hole carrying on a metre and a half past its end: half
            // again the wall's area, so it used to be filled whole and the opening went missing.
            PlanPolygon wall = Rectangle(2000, 5000, 5000, 5250);
            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                                   new[] { Rectangle(500, 5000, 5000, 5250).Outer });

            Assert.AreEqual(1, SlabCompareEngine.WithoutSupportHoles(floor, new[] { wall }).Holes.Count);
        }

        [TestMethod]
        public void AHoleAWallAndAColumnTakeUpBetweenThemIsFilled()
        {
            // The wall, and a column standing at its end: the hole is cut round the two of them.
            PlanPolygon wall = Rectangle(2000, 5000, 5000, 5250);
            PlanPolygon column = Rectangle(5000, 4850, 5550, 5400);
            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer, new[]
            {
                PlanRing.FromPoints(new[]
                {
                    new MyPoint(2000, 5000, 0), new MyPoint(5000, 5000, 0), new MyPoint(5000, 4850, 0),
                    new MyPoint(5550, 4850, 0), new MyPoint(5550, 5400, 0), new MyPoint(5000, 5400, 0),
                    new MyPoint(5000, 5250, 0), new MyPoint(2000, 5250, 0)
                })
            });

            Assert.AreEqual(0, SlabCompareEngine.WithoutSupportHoles(floor, new[] { wall, column }).Holes.Count);
        }

        [TestMethod]
        public void AHoleNoStiffAreaStandsInIsLeftOpen()
        {
            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                                   new[] { Rectangle(1000, 1000, 1800, 1800).Outer });

            Assert.AreEqual(1, SlabCompareEngine.WithoutSupportHoles(floor, Supports()).Holes.Count);
        }

        #endregion

        #region the check

        [TestMethod]
        public void AColumnStiffAreaInADropLeavesOnlyAgreement()
        {
            List<SlabZone> zones = CheckSlab(Faces(support: true), Coverage(Supports()));

            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched),
                          "the slab and the drop agree, and the column is not slab: " + Describe(zones));

            Assert.IsFalse(zones.Any(zone => zone.EtabsName == "K1"),
                           "the stiff area stands in for the column, so it carries no verdict: " + Describe(zones));

            double drop = zones.Where(zone => zone.EtabsName == "D1").Sum(zone => zone.Area);
            Assert.AreEqual(3500d * 3500d - StiffSide * StiffSide, drop, 1000,
                            "the drop is checked all the way to the column: " + Describe(zones));
        }

        [TestMethod]
        public void AStiffAreaNotStackedIsStillCheckedAsSlab()
        {
            // The same faces, with the stiff area read as the shell it is. This is what every
            // column in a drop used to look like: ETABS slab over the hole the column cuts, the
            // corners of the square a thousand against the drop, and slivers of the drop over
            // the round hole - all in the ETABS only colour but the corners.
            List<SlabZone> zones = CheckSlab(Faces(support: false), Coverage(new List<PlanPolygon>()));

            // Over the hole, and the hole is inside the floor's outline, so it reads as the
            // opening Revit has there rather than as slab past the floor's edge.
            Assert.IsTrue(zones.Any(zone => zone.EtabsName == "K1" && zone.State == SlabZoneState.OpeningInRevitOnly),
                          "checked as slab, it stands over the hole: " + Describe(zones));
            Assert.IsTrue(zones.Any(zone => zone.EtabsName == "D1" && zone.State == SlabZoneState.OpeningInRevitOnly),
                          "and the drop reaches into the round hole past the square: " + Describe(zones));
        }

        [TestMethod]
        public void AnOpeningBesideTheColumnIsStillReported()
        {
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, SlabCompareEngine.WithoutSupportHoles(
                PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer, new[] { Shaft().Outer }), Supports()), 250));

            List<EtabsFace> faces = new List<EtabsFace>
            {
                new EtabsFace { Source = Stiff(), Plan = StiffPlan(), IsSupport = true },
                new EtabsFace
                {
                    Source = MainSlab(),
                    Plan = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer, new[] { StiffPlan().Outer })
                }
            };

            List<SlabZone> zones = CheckSlab(faces, coverage);

            double opening = zones.Where(zone => zone.State == SlabZoneState.OpeningInRevitOnly)
                                  .Sum(zone => zone.Area);

            Assert.AreEqual(Shaft().Area - StiffSide * StiffSide, opening, 5000,
                            "the shaft is reported, less the column standing in it: " + Describe(zones));
        }

        [TestMethod]
        public void TheLoadPassStillReadsTheColumn()
        {
            // Loads on stacked shells add up, and that is left exactly as it was: the piece
            // under the stiff area is still a piece of the load mesh, carrying the slab's load.
            Slab_E slab = MainSlab();
            slab.SetLoads(new List<AreaLoad_E> { new AreaLoad_E("SDL", 3.0, 10, true, false) });
            Slab_E stiff = Stiff();

            List<SlabZone> zones = new List<SlabZone>();
            SlabMeshCompare compare = new SlabMeshCompare(zones, new List<string>())
            {
                Purpose = SlabMeshPurpose.Loads,
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25,
                Loads = OneLoadRegion(),
                LoadTolerance = 0.05,
                LoadPatterns = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData()),
                ShellsOnStory = new List<Slab_E> { slab, stiff }
            };

            compare.CompareFaces(Story, new List<EtabsFace>
            {
                new EtabsFace { Source = stiff, Plan = StiffPlan(), IsSupport = true, IsWholeArea = true }
            }, new SlabCoverage());

            SlabZone column = zones.Single(zone => zone.EtabsName == "K1");
            Assert.AreEqual(3.0, column.EtabsLoads[LoadCategory.Sdl], 1e-9,
                            "the slab's load is still counted under the column");
        }

        #endregion

        #region the model

        private static List<SlabZone> CheckSlab(List<EtabsFace> faces, SlabCoverage coverage)
        {
            List<SlabZone> zones = new List<SlabZone>();
            SlabMeshCompare compare = new SlabMeshCompare(zones, new List<string>())
            {
                Purpose = SlabMeshPurpose.Slab,
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25
            };

            compare.CompareFaces(Story, faces, coverage);
            compare.ReportRevitOnly(Story, faces, coverage);
            return zones;
        }

        /// <summary>The slab, the drop and the column, as the base mesh cuts them.</summary>
        private static List<EtabsFace> Faces(bool support)
        {
            return new List<EtabsFace>
            {
                new EtabsFace { Source = Stiff(), Plan = StiffPlan(), IsWholeArea = true, IsSupport = support },
                new EtabsFace
                {
                    Source = Drop(),
                    Plan = PlanPolygon.Create(Rectangle(4250, 4250, 7750, 7750).Outer, new[] { StiffPlan().Outer })
                },
                new EtabsFace
                {
                    Source = MainSlab(),
                    Plan = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                              new[] { Rectangle(4250, 4250, 7750, 7750).Outer })
                }
            };
        }

        /// <summary>The Revit slab and drop, both cut by the column, built the way the check builds them.</summary>
        private static SlabCoverage Coverage(IList<PlanPolygon> supports)
        {
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, SlabCompareEngine.WithoutSupportHoles(
                WithColumnHole(Rectangle(0, 0, 12000, 12000)), supports), 250));
            coverage.Add(Floor(501, SlabCompareEngine.WithoutSupportHoles(
                WithColumnHole(Rectangle(4250, 4250, 7750, 7750)), supports), 350));
            return coverage;
        }

        private static SlabCoverage.Region Floor(long revitId, PlanPolygon plan, double thickness)
        {
            return new SlabCoverage.Region
            {
                Plan = plan,
                Thickness = thickness,
                RevitId = revitId,
                TypeName = "SLAB " + thickness,
                TopElevation = 0,
                BottomElevation = -thickness,
                AtStoryPlane = true
            };
        }

        private static IList<PlanPolygon> Supports()
        {
            return new List<PlanPolygon> { StiffPlan() };
        }

        private static LoadCoverage OneLoadRegion()
        {
            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Tower-Area Load");
            schedule.Add(new LoadScheduleEntry("1", "TYPE 1", new LoadSet { Sdl = 3.0 }, "LL"));

            return LoadCoverage.Build(
                new List<LoadRegion_R> { new LoadRegion_R(1, new[] { Rectangle(0, 0, 12000, 12000) }, "1") },
                schedule, new List<string>());
        }

        private static Slab_E MainSlab()
        {
            return Shell("A1", "S250", SlabKind.Slab, 250, 0, 0, 12000, 12000);
        }

        private static Slab_E Drop()
        {
            return Shell("D1", "DP350", SlabKind.Drop, 350, 4250, 4250, 7750, 7750);
        }

        private static Slab_E Stiff()
        {
            double half = StiffSide / 2;
            return Shell("K1", "STIFF", SlabKind.Stiff, 1000,
                         Centre - half, Centre - half, Centre + half, Centre + half);
        }

        private static PlanPolygon StiffPlan()
        {
            return Stiff().Plan;
        }

        private static PlanPolygon Shaft()
        {
            return Rectangle(Centre - 450, Centre - 450, Centre + 1550, Centre + 1050);
        }

        private static Slab_E Shell(string name, string property, SlabKind kind, double thickness,
                                    double minX, double minY, double maxX, double maxY)
        {
            return new Slab_E(name, Corners(minX, minY, maxX, maxY).Select(Joint).ToList(),
                              Story, property, kind, thickness, thickness, false);
        }

        private static Joint Joint(MyPoint point)
        {
            return new Joint("", point, Form1.eLength.mm);
        }

        /// <summary>A floor with the round hole the column cuts through it.</summary>
        private static PlanPolygon WithColumnHole(PlanPolygon floor)
        {
            PlanRing hole = PlanRing.FromPoints(Enumerable.Range(0, 64).Select(i =>
            {
                double angle = 2 * Math.PI * i / 64;
                return new MyPoint(Centre + ColumnDiameter / 2 * Math.Cos(angle),
                                   Centre + ColumnDiameter / 2 * Math.Sin(angle), 0);
            }));

            return PlanPolygon.Create(floor.Outer, new[] { hole });
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        private static MyPoint[] Corners(double minX, double minY, double maxX, double maxY)
        {
            return new[]
            {
                new MyPoint(minX, minY, 0),
                new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0),
                new MyPoint(minX, maxY, 0)
            };
        }

        private static string Describe(List<SlabZone> zones)
        {
            return zones.Count == 0
                ? "no zones at all"
                : string.Join(", ", zones.Select(z =>
                    $"{z.EtabsName}{(z.RevitId > 0 ? "/R" + z.RevitId : "")}:{z.State}={z.Area / 1e6:0.###} m2"));
        }

        #endregion
    }
}
