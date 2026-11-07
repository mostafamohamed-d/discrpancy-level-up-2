using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A hole the ETABS model has and the Revit floor does not, which is the Revit-only half
    /// of the slab check: the floor is walked for the patches no ETABS shell stands over.
    ///
    /// Both cases here used to come back with nothing at all - not a wrong verdict, no zone -
    /// so the opening was simply missing from the plan.
    /// </summary>
    [TestClass]
    public class SlabEtabsOpeningTests
    {
        private const string Story = "L03";

        [TestMethod]
        public void AHoleInAFinelyMeshedSlabIsReported()
        {
            // A 50 m floor meshed at 500 is ten thousand shells, and every one of their corners
            // used to go into the one triangulation - past the most it takes, so the whole floor
            // was given up on and the hole in the middle of it never looked for.
            const double size = 50000;
            PlanBounds hole = Box(24000, 24000, 26000, 26000);

            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(700, Rectangle(0, 0, size, size), 200, atPlane: true));

            List<string> issues = new List<string>();
            List<SlabZone> zones = ReportRevitOnly(coverage, Mesh(0, 0, size, size, 500, hole), issues);

            AssertOpening(zones, 2000 * 2000);
            Assert.AreEqual(0, zones.Count(zone => zone.State == SlabZoneState.MissingInEtabs),
                "the mesh reaches every edge of the floor, so no outline differs: " + Describe(zones));
            Assert.IsFalse(issues.Any(issue => issue.Contains("cut apart")),
                "the floor was given up on: " + string.Join(" | ", issues));
        }

        [TestMethod]
        public void AHoleOverARevitDropPanelIsReported()
        {
            // A 400 panel hung under the 200 slab, and the ETABS hole over it. The panel is the
            // thicker of the two, so it was the floor the point reading named - and a floor
            // below the story plane never reports a bare patch, while the slab at the plane
            // stood aside for it. Nobody reported the hole.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(700, Rectangle(0, 0, 12000, 12000), 200, atPlane: true));
            coverage.Add(Floor(701, Rectangle(4000, 4000, 8000, 8000), 400, atPlane: false));

            List<SlabZone> zones = ReportRevitOnly(coverage,
                Mesh(0, 0, 12000, 12000, 1000, Box(5000, 5000, 7000, 7000)), new List<string>());

            AssertOpening(zones, 2000 * 2000);
            Assert.IsTrue(zones.All(zone => zone.RevitId == 700),
                "the hole is in the slab at the plane, so that is the floor it names: " + Describe(zones));
        }

        [TestMethod]
        public void TwoMeshSizesMeetingAddNothingAlongTheSeam()
        {
            // Left half meshed at 1000, right half at 500, so the shells along the seam do not
            // share edges one for one. Nothing is missing there, and nothing may be reported.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(700, Rectangle(0, 0, 12000, 12000), 200, atPlane: true));

            List<EtabsFace> faces = Mesh(0, 0, 6000, 12000, 1000, Box(2000, 5000, 4000, 7000));
            faces.AddRange(Mesh(6000, 0, 12000, 12000, 500, null));

            List<SlabZone> zones = ReportRevitOnly(coverage, faces, new List<string>());

            AssertOpening(zones, 2000 * 2000);
            Assert.AreEqual(1, zones.Count, "only the hole differs: " + Describe(zones));
        }

        [TestMethod]
        public void AMeshStoppingShortOfTheFloorEdgeIsAnOutlineNotAnOpening()
        {
            // The mesh stops a metre short of one edge of the floor, and has no hole.
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(700, Rectangle(0, 0, 12000, 12000), 200, atPlane: true));

            List<SlabZone> zones = ReportRevitOnly(coverage, Mesh(0, 0, 11000, 12000, 500, null),
                                                   new List<string>());

            double outline = zones.Where(zone => zone.State == SlabZoneState.MissingInEtabs)
                                  .Sum(zone => zone.Area);
            Assert.AreEqual(1000d * 12000d, outline, 1000d * 12000d * 0.02,
                "the strip past the mesh is Revit floor ETABS does not reach: " + Describe(zones));
            Assert.AreEqual(0, zones.Count(zone => zone.State == SlabZoneState.OpeningInEtabsOnly),
                Describe(zones));
        }

        private static void AssertOpening(List<SlabZone> zones, double expectedArea)
        {
            double opening = zones.Where(zone => zone.State == SlabZoneState.OpeningInEtabsOnly)
                                  .Sum(zone => zone.Area);
            Assert.AreEqual(expectedArea, opening, expectedArea * 0.02,
                "the hole ETABS has and Revit has not is not reported as an opening: " + Describe(zones));
        }

        private static List<SlabZone> ReportRevitOnly(SlabCoverage coverage, List<EtabsFace> faces,
                                                      List<string> issues)
        {
            List<SlabZone> zones = new List<SlabZone>();

            SlabMeshCompare compare = new SlabMeshCompare(zones, issues)
            {
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25
            };

            compare.ReportRevitOnly(Story, faces, coverage);
            return zones;
        }

        /// <summary>
        /// A square mesh of shells over a rectangle, sharing corners the way a divided ETABS
        /// slab does, with the shells inside <paramref name="hole"/> left out.
        /// </summary>
        private static List<EtabsFace> Mesh(double minX, double minY, double maxX, double maxY,
                                            double cell, PlanBounds hole)
        {
            Slab_E source = new Slab_E("A1", new List<Joint>(), Story, "SLAB200", SlabKind.Slab,
                                       200, 200, false);
            List<EtabsFace> faces = new List<EtabsFace>();

            for (double x = minX; x < maxX - 1e-6; x += cell)
            {
                for (double y = minY; y < maxY - 1e-6; y += cell)
                {
                    double cx = x + cell / 2d, cy = y + cell / 2d;
                    if (hole != null && hole.Contains(cx, cy))
                    {
                        continue;
                    }

                    faces.Add(new EtabsFace
                    {
                        Source = source,
                        Plan = Rectangle(x, y, x + cell, y + cell),
                        IsWholeArea = true
                    });
                }
            }

            return faces;
        }

        private static SlabCoverage.Region Floor(long id, PlanPolygon plan, double thickness, bool atPlane)
        {
            double top = atPlane ? 0 : -200;
            return new SlabCoverage.Region
            {
                Plan = plan,
                Thickness = thickness,
                RevitId = id,
                TypeName = "SLAB " + thickness,
                TopElevation = top,
                BottomElevation = top - thickness,
                AtStoryPlane = atPlane
            };
        }

        private static PlanBounds Box(double minX, double minY, double maxX, double maxY)
        {
            PlanBounds bounds = new PlanBounds();
            bounds.Include(minX, minY);
            bounds.Include(maxX, maxY);
            return bounds;
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

        private static string Describe(List<SlabZone> zones)
        {
            return zones.Count == 0
                ? "no zones at all"
                : string.Join(", ", zones.Select(z =>
                    $"{z.State}={z.Area / 1e6:0.###} m2 R{z.RevitId} " +
                    $"[{z.Plan.Bounds.MinX:0}..{z.Plan.Bounds.MaxX:0} x " +
                    $"{z.Plan.Bounds.MinY:0}..{z.Plan.Bounds.MaxY:0}]"));
        }
    }
}
