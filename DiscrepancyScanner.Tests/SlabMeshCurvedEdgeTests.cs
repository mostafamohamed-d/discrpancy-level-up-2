using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A mismatch along a curved slab edge, and what the ETABS mesh under it is allowed to do
    /// to the answer.
    ///
    /// Revit lets a slab edge curve and ETABS does not, so the two outlines disagree along a
    /// long thin crescent that tapers to nothing at each end. Everything the check works out
    /// is worked out inside one ETABS face and never merged across faces, so a subdivided mesh
    /// cuts that crescent into a piece per cell - and the pieces at its tapering ends are small
    /// however large the crescent is. Measured one at a time against the smallest-zone sizes
    /// they were dropped, which left the plan blank exactly where the mismatch ran out and made
    /// the answer depend on how finely the mesh happened to be subdivided.
    ///
    /// So the sizes have to be measured against the whole disagreement rather than against one
    /// cell's share of it, and the test of that is here: the same curve and the same Revit
    /// floor, over ETABS meshes from one area to four hundred, have to report the same area.
    /// </summary>
    [TestClass]
    public class SlabMeshCurvedEdgeTests
    {
        private const string Story = "L03";
        private const double W = 10000;
        private const double H = 10000;
        private const double Sagitta = 150;
        private const int Segments = 60;

        private const double MinimumZoneArea = 10000;
        private const double MinimumZoneWidth = 20;

        /// <summary>
        /// How far short of each end of the crescent the reported mismatch may stop.
        ///
        /// The crescent tapers to nothing, so its last few millimetres are less than a
        /// millimetre deep and are lost to the shaving
        /// <see cref="AHairlineIsShavedOffEveryCutEdge"/> is about - measured, the reported
        /// mismatch reaches x 6 at one end and x 9994 at the other. Ten millimetres covers that
        /// and is two orders of magnitude short of the 500 mm end cell that used to disappear
        /// whole, which is what this test has to keep catching.
        /// </summary>
        private const double EndReach = 10;

        /// <summary>
        /// The reported mismatch has to cover the crescent from end to end, with no cell-sized
        /// hole in it. This is the white wedge itself, stated as a measurement: before the fix
        /// the end cells at 500 mm were dropped and the reported pieces started 500 mm in from
        /// each end of a crescent that runs the full width.
        ///
        /// It is put as coverage rather than as area because the two are not equally telling.
        /// A separate defect - see <see cref="AHairlineIsShavedOffEveryCutEdge"/> - shaves a
        /// fraction of a millimetre off every cut edge, which costs about 1.3% of this crescent
        /// once the mesh is fine enough, and that is more than a dropped end piece costs. Area
        /// alone therefore cannot tell the two apart; where the pieces are can.
        /// </summary>
        [TestMethod]
        public void SubdividingTheEtabsMeshLeavesNoGapInTheMismatch()
        {
            foreach (double cell in new[] { 2000d, 1000d, 500d })
            {
                List<PlanBounds> pieces = Run(cell)
                    .Where(zone => zone.State == SlabZoneState.MissingInRevit)
                    .Select(zone => zone.Plan.Bounds)
                    .OrderBy(bounds => bounds.MinX)
                    .ToList();

                Assert.IsTrue(pieces.Count > 0, $"nothing reported at all at {cell} mm cells");

                string where = $"at {cell} mm cells the reported mismatch runs " +
                               string.Join(", ", pieces.Select(b => $"{b.MinX:0}..{b.MaxX:0}"));

                Assert.IsTrue(pieces.First().MinX <= EndReach,
                    "the crescent starts at x 0 - " + where);
                Assert.IsTrue(pieces.Last().MaxX >= W - EndReach,
                    $"the crescent runs to x {W} - " + where);

                for (int i = 1; i < pieces.Count; i++)
                {
                    Assert.IsTrue(pieces[i].MinX <= pieces[i - 1].MaxX + EndReach,
                        $"a piece of the crescent between x {pieces[i - 1].MaxX:0} and " +
                        $"x {pieces[i].MinX:0} was not reported - " + where);
                }
            }
        }

        /// <summary>
        /// Two meshes fine enough to shave the same amount off have to agree on the area, which
        /// is the area test with the shaving held constant instead of allowed for. Before the
        /// fix these differed by the 11 752 mm2 of end pieces that 500 mm cells dropped and
        /// 1000 mm cells did not.
        /// </summary>
        [TestMethod]
        public void TwoFineEtabsMeshesReportTheSameAmountOfMismatch()
        {
            double atThousand = MissingAreaFor(1000);
            double atFiveHundred = MissingAreaFor(500);

            Assert.AreEqual(atThousand, atFiveHundred, atThousand * 0.001,
                $"1000 mm cells reported {atThousand:0.#} mm2 and 500 mm cells " +
                $"{atFiveHundred:0.#} mm2 of the same mismatch");
        }

        /// <summary>
        /// The pieces at the tapering ends, named. At 500 mm cells the crescent's two end cells
        /// hold about 5 800 mm2 each at about 11 mm wide - under both sizes, so both were
        /// dropped before. They are the white wedges the plan used to show.
        /// </summary>
        [TestMethod]
        public void TheTaperingEndsOfAMismatchAreReportedRatherThanDropped()
        {
            List<SlabZone> zones = Run(500);

            List<SlabZone> missing = zones
                .Where(zone => zone.State == SlabZoneState.MissingInRevit)
                .OrderBy(zone => zone.Plan.Bounds.MinX)
                .ToList();

            Assert.IsTrue(missing.Count >= 2, "the crescent should come back as many pieces");

            SlabZone first = missing.First();
            SlabZone last = missing.Last();

            Assert.IsTrue(first.Plan.Bounds.MinX < 500,
                $"the piece at the start of the crescent is missing - the leftmost reported one " +
                $"starts at x {first.Plan.Bounds.MinX:0}");
            Assert.IsTrue(last.Plan.Bounds.MaxX > W - 500,
                $"the piece at the end of the crescent is missing - the rightmost reported one " +
                $"ends at x {last.Plan.Bounds.MaxX:0}");

            Assert.IsTrue(first.Area < MinimumZoneArea || first.Plan.TypicalWidth < MinimumZoneWidth,
                "this test is only meaningful while the end piece is one the sizes would " +
                "otherwise have dropped");
        }

        /// <summary>
        /// The other half of it: a piece that really is a hairline, with nothing beside it to
        /// belong to, is still dropped. Two outlines a fraction of a millimetre apart is what
        /// the smallest-zone sizes were written for, and keeping every one of those would put a
        /// mismatch on every edge in the model.
        /// </summary>
        [TestMethod]
        public void AHairlineOnItsOwnIsStillDropped()
        {
            // One ETABS area a hair wider than the Revit floor under it, all the way round.
            // Nothing else is near it, so the sliver that leaves belongs to nothing.
            PlanPolygon etabsPlan = Rectangle(0, 0, W, H);
            PlanPolygon revitPlan = Rectangle(0, 0, W - 0.4, H);

            List<SlabZone> zones = Compare(
                new List<EtabsFace>
                {
                    new EtabsFace { Source = EtabsCell(0, 0, W, H, "A1"), Plan = etabsPlan, IsWholeArea = true }
                },
                Coverage(revitPlan));

            Assert.IsFalse(zones.Any(zone => zone.State == SlabZoneState.MissingInRevit),
                "a hairline down one edge is what the smallest-zone sizes exist to swallow: " +
                string.Join(", ", zones.Select(z => $"{z.State}={z.Area:0.###} mm2")));
        }

        /// <summary>
        /// A known, separate defect, pinned here so it is not mistaken for the one above and so
        /// it is noticed if it moves.
        ///
        /// A slab edge goes into the triangulation as a pair of constraints 0.3 mm either side
        /// of where it really runs (SlabMeshCompare.BoundaryOffset), which is what makes the
        /// thickness step at the edge instead of ramping across the area. PlanRing.FromPoints
        /// then treats two points closer together than PlanRing.PointTolerance - 1 mm - as one,
        /// which is wider than the strip those constraints make, so the step across it is
        /// cleaned away and the outline corner-cuts. The reported mismatch comes out about 1.3%
        /// light once the mesh is fine enough to cut the crescent at all.
        ///
        /// It is a hairline and it is not what left holes in the plan. Fixing it means changing
        /// a tolerance that the loading plan and the Revit import also read, so it is recorded
        /// rather than quietly absorbed. If this test starts failing low, that is a regression;
        /// if it starts failing high, the tolerance has been fixed and this test should go.
        /// </summary>
        [TestMethod]
        public void AHairlineIsShavedOffEveryCutEdge()
        {
            double whole = MissingAreaFor(W);
            double subdivided = MissingAreaFor(1000);
            double shortfall = (whole - subdivided) / whole;

            Assert.IsTrue(shortfall > 0.005 && shortfall < 0.02,
                $"the known shaving was {shortfall:P2} of the mismatch ({whole - subdivided:0.#} mm2 " +
                $"of {whole:0.#}); it has been about 1.3% - a change either way is worth knowing " +
                "about, and if it has gone to nought the tolerance has been fixed");
        }

        private static double MissingAreaFor(double cell)
        {
            return Run(cell)
                .Where(zone => zone.State == SlabZoneState.MissingInRevit)
                .Sum(zone => zone.Area);
        }

        /// <summary>
        /// The check over one curved Revit floor and an ETABS mesh of the given cell size.
        ///
        /// <see cref="SlabMeshCompare"/> is driven directly rather than through
        /// <see cref="SlabCompareEngine"/>, because building the base mesh cuts overlapping
        /// ETABS areas apart through the Revit geometry kernel and these tests run without
        /// Revit. A subdivided mesh is exactly the case where no cutting is called for - the
        /// cells abut, they do not overlap - so a face per cell is the same base mesh the
        /// engine would have built, minus the step that cannot run here.
        /// </summary>
        private static List<SlabZone> Run(double cell)
        {
            int n = (int)Math.Round(W / cell);
            List<EtabsFace> faces = new List<EtabsFace>();

            for (int ix = 0; ix < n; ix++)
            {
                for (int iy = 0; iy < n; iy++)
                {
                    double minX = ix * cell, minY = iy * cell;
                    double maxX = (ix + 1) * cell, maxY = (iy + 1) * cell;

                    faces.Add(new EtabsFace
                    {
                        Source = EtabsCell(minX, minY, maxX, maxY, $"A{ix}-{iy}"),
                        Plan = Rectangle(minX, minY, maxX, maxY),
                        IsWholeArea = true
                    });
                }
            }

            return Compare(faces, Coverage(CurvedTopFloor()));
        }

        private static List<SlabZone> Compare(List<EtabsFace> faces, SlabCoverage coverage)
        {
            List<SlabZone> zones = new List<SlabZone>();

            new SlabMeshCompare(zones, new List<string>())
            {
                ThicknessTolerance = 5,
                MinimumZoneArea = MinimumZoneArea,
                MinimumZoneWidth = MinimumZoneWidth,
                EdgeTolerance = 25
            }.CompareFaces(Story, faces, coverage);

            return zones;
        }

        #region building the inputs

        private static SlabCoverage Coverage(PlanPolygon plan)
        {
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(new SlabCoverage.Region
            {
                Plan = plan,
                Thickness = 200,
                RevitId = 500,
                TypeName = "SLAB 200",
                TopElevation = 0,
                BottomElevation = -200,
                AtStoryPlane = true
            });
            return coverage;
        }

        private static Slab_E EtabsCell(double minX, double minY, double maxX, double maxY, string name)
        {
            List<Joint> joints = Corners(minX, minY, maxX, maxY)
                .Select(point => new Joint("", point, Form1.eLength.mm))
                .ToList();

            return new Slab_E(name, joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
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

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        /// <summary>
        /// The full rectangle, except the top edge dips into a shallow sine curve - the
        /// tessellated arc a curved Revit slab edge comes in as. It meets the straight edge at
        /// both corners, so the crescent left over tapers to nothing at each end, which is
        /// where the plan used to go blank.
        /// </summary>
        private static PlanPolygon CurvedTopFloor()
        {
            List<MyPoint> points = new List<MyPoint>
            {
                new MyPoint(0, 0),
                new MyPoint(W, 0)
            };

            for (int i = 0; i <= Segments; i++)
            {
                double x = W - W * i / (double)Segments;
                points.Add(new MyPoint(x, H - Sagitta * Math.Sin(Math.PI * x / W)));
            }

            return PlanPolygon.FromPoints(points);
        }

        #endregion
    }
}
