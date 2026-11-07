using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// One ETABS shell cut apart inside itself, which is what happens wherever a thickness
    /// changes or a loading region ends part way across a shell.
    ///
    /// This is not the same as a shell being one of many in a mesh: the pieces here are
    /// different answers by construction - that is why the shell was cut at all - so nothing
    /// that reasons about a neighbour carrying the same answer can help them. Each piece has to
    /// come back on its own merits.
    /// </summary>
    [TestClass]
    public class SlabShellCutTests
    {
        private const string Story = "L03";
        private const double Size = 6000;

        [TestMethod]
        public void AThicknessChangeAcrossAShellReportsBothPieces()
        {
            // The whole shell has a floor under it at 200; a triangle of it has a thicker floor
            // as well, so the shell agrees outside the triangle and disagrees inside it.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, Rectangle(0, 0, Size, Size), 200));
            coverage.Add(Floor(501, Triangle(), 300));

            Check(coverage, "a thickness change", SlabZoneState.ThicknessMismatch, TriangleArea);
        }

        [TestMethod]
        public void AThicknessChangeThatEndsOnTheShellCornerReportsBothPieces()
        {
            // The same, except the cut runs corner to corner, so its ends land exactly on the
            // shell's own outline rather than crossing it. A crossing exactly at an end point
            // is not counted as a crossing (see SlabMeshCompare.Cross), so this is the case
            // where the cut may not be tied into the outline at all.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, Rectangle(0, 0, Size, Size), 200));
            coverage.Add(Floor(501, CornerToCornerTriangle(), 300));

            Check(coverage, "a corner to corner thickness change", SlabZoneState.ThicknessMismatch,
                  Size * Size / 2d);
        }

        [TestMethod]
        public void ALoadingRegionEndingPartWayAcrossAShellReportsBothPieces()
        {
            // No thickness change anywhere - one floor, one thickness, agreeing everywhere. The
            // only thing that changes across the shell is which loading region covers it, which
            // is on its own a reason to cut the shell: one shell carries one uniform load, so a
            // shell spanning two regions that ask for different loads is wrong by construction.
            //
            // That cut belongs to the load mesh, not the slab one. The slab mesh is cut along
            // the Revit floors and would - rightly - hand this back as a single zone, because
            // nothing about its thickness changes anywhere across it.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, Rectangle(0, 0, Size, Size), 200));

            List<SlabZone> zones = Compare(coverage, WithLoadRegions());

            double covered = zones.Sum(zone => zone.Area);

            Assert.AreEqual(Size * Size, covered, Size * Size * 0.01,
                "the shell was cut at the loading region boundary and the pieces do not add " +
                "back up to it: " + Describe(zones));

            Assert.IsTrue(zones.Count >= 2,
                "the shell spans two loading regions asking for different loads, so it has to " +
                "come back as more than one zone: " + Describe(zones));
        }

        private static void Check(SlabCoverage coverage, string what, SlabZoneState expected,
                                  double expectedArea)
        {
            List<SlabZone> zones = Compare(coverage, null);

            double covered = zones.Sum(zone => zone.Area);
            Assert.AreEqual(Size * Size, covered, Size * Size * 0.01,
                $"the shell was cut by {what} and the pieces do not add back up to it: " +
                Describe(zones));

            double reported = zones.Where(zone => zone.State == expected).Sum(zone => zone.Area);
            Assert.AreEqual(expectedArea, reported, expectedArea * 0.02,
                $"the piece {what} cut off is not reported: " + Describe(zones));
        }

        private static string Describe(List<SlabZone> zones)
        {
            return zones.Count == 0
                ? "no zones at all"
                : string.Join(", ", zones.Select(z =>
                    $"{z.State}={z.Area / 1e6:0.###} m2 " +
                    $"[{z.Plan.Bounds.MinX:0}..{z.Plan.Bounds.MaxX:0} x " +
                    $"{z.Plan.Bounds.MinY:0}..{z.Plan.Bounds.MaxY:0}]"));
        }

        private static List<SlabZone> Compare(SlabCoverage coverage, LoadCoverage loads)
        {
            List<SlabZone> zones = new List<SlabZone>();

            EtabsFace face = new EtabsFace
            {
                Source = Shell(),
                Plan = Rectangle(0, 0, Size, Size),
                IsWholeArea = true
            };

            SlabMeshCompare compare = new SlabMeshCompare(zones, new List<string>())
            {
                // A pass answers one question and is cut for that question alone, so a test
                // that hands over a loading plan is asking for the load mesh.
                Purpose = loads == null ? SlabMeshPurpose.Slab : SlabMeshPurpose.Loads,
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25,
                Loads = loads,
                LoadTolerance = 0.05,
                LoadPatterns = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData())
            };

            compare.CompareFaces(Story, new List<EtabsFace> { face }, coverage);

            return zones;
        }

        #region the shapes

        /// <summary>The triangle in the picture: it hangs off a grid line and ends inside.</summary>
        private static readonly double TriangleArea = 0.5 * 3000 * 3000;

        private static PlanPolygon Triangle()
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(3000, 3000),
                new MyPoint(6000, 3000),
                new MyPoint(3000, 6000)
            });
        }

        private static PlanPolygon CornerToCornerTriangle()
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(0, 0),
                new MyPoint(Size, 0),
                new MyPoint(Size, Size)
            });
        }

        private static SlabCoverage.Region Floor(long id, PlanPolygon plan, double thickness)
        {
            return new SlabCoverage.Region
            {
                Plan = plan,
                Thickness = thickness,
                RevitId = id,
                TypeName = "SLAB " + thickness,
                TopElevation = 0,
                BottomElevation = -thickness,
                AtStoryPlane = true
            };
        }

        /// <summary>
        /// Two loading regions meeting on a line half way across the shell, asking for
        /// different loads.
        /// </summary>
        private static LoadCoverage WithLoadRegions()
        {
            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Tower-Area Load");
            schedule.Add(new LoadScheduleEntry("1", "TYPE 1", new LoadSet { Sdl = 3.0, Lr = 2.0 }, "LLR"));
            schedule.Add(new LoadScheduleEntry("2", "TYPE 2", new LoadSet { Sdl = 4.0, Lr = 2.0 }, "LLR"));

            return LoadCoverage.Build(
                new List<LoadRegion_R>
                {
                    new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, 3000) }, "1"),
                    new LoadRegion_R(2, new[] { Rectangle(0, 3000, Size, Size) }, "2")
                },
                schedule, new List<string>());
        }

        private static Slab_E Shell()
        {
            List<Joint> joints = new[]
            {
                new MyPoint(0, 0, 0),
                new MyPoint(Size, 0, 0),
                new MyPoint(Size, Size, 0),
                new MyPoint(0, Size, 0)
            }.Select(point => new Joint("", point, Form1.eLength.mm)).ToList();

            Slab_E shell = new Slab_E("A1", joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
            shell.SetLoads(new List<AreaLoad_E>
            {
                new AreaLoad_E("SDL", 3.0, 10, true, false),
                new AreaLoad_E("LLR", 2.0, 10, true, false)
            });
            return shell;
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(minX, minY, 0),
                new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0),
                new MyPoint(minX, maxY, 0)
            });
        }

        #endregion
    }
}
