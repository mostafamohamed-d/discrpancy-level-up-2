using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A shell under more loading plan edges than one triangulation used to take.
    ///
    /// The cap was 6,000 points, and a real loading plan is far busier: on one podium the
    /// paving region, drawn as the background with a hole for everything else, had 9,748
    /// points on its own, and the slab under the whole floor needed about 30,000. Past the cap
    /// the whole shell was answered from one point - 3,750 m2 of parking held against paving,
    /// because that is where the one point fell, and nothing on the plan said so but a line in
    /// the issues.
    /// </summary>
    [TestClass]
    public class BusyLoadPlanTests
    {
        private const string Story = "GROUND";
        private const double Size = 12000;

        [TestMethod]
        public void APlanAsBusyAsARealPodiumIsCutAlongItsEdges()
        {
            // The left region's edge is a curve drawn with 4,000 points - 8,000 once it goes in
            // doubled, past the old cap - and the right half has no region on it at all.
            List<MyPoint> outline = new List<MyPoint> { new MyPoint(0, 0, 0) };
            for (int i = 0; i <= 4000; i++)
            {
                double y = Size * i / 4000;
                outline.Add(new MyPoint(Size / 2 + 500 * Math.Sin(2 * Math.PI * y / Size), y, 0));
            }
            outline.Add(new MyPoint(0, Size, 0));

            List<string> issues = new List<string>();
            List<SlabZone> pieces = CutForLoads(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { PlanPolygon.FromPoints(outline) }, "1")
            }, issues, compare => { });

            Assert.IsFalse(issues.Any(issue => issue.Contains("answered")), string.Join(" | ", issues));
            Assert.AreEqual(Size * Size / 2, AreaWhere(pieces, SlabLoadState.Matched), 20000,
                            "the region's half, to its curved edge: " + Describe(pieces));
            Assert.AreEqual(Size * Size / 2, AreaWhere(pieces, SlabLoadState.NoLoadingRegion), 20000,
                            "and the half nothing is drawn on: " + Describe(pieces));
        }

        [TestMethod]
        public void AFaceTooBusyToCutIsSampledRatherThanAnsweredFromOnePoint()
        {
            // The cap set so low that two rectangles are too much. What comes back is still the
            // two halves, each with its own answer, to within the sample spacing - rather than
            // one answer for the whole shell.
            List<string> issues = new List<string>();
            List<SlabZone> pieces = CutForLoads(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size / 2, Size) }, "1"),
                new LoadRegion_R(2, new[] { Rectangle(Size / 2, 0, Size, Size) }, "2")
            }, issues, compare =>
            {
                compare.MaximumPointsPerTriangulation = 4;
                compare.SampleBudget = 2000;
            });

            Assert.IsTrue(issues.Any(issue => issue.Contains("answered at points")), string.Join(" | ", issues));

            // A band one sample spacing wide along the line between them may land either side.
            double band = Size * 300;
            Assert.AreEqual(Size * Size / 2, AreaWhere(pieces, SlabLoadState.Matched), band,
                            "the half asking for SDL 3: " + Describe(pieces));
            Assert.AreEqual(Size * Size / 2, AreaWhere(pieces, SlabLoadState.Mismatch), band,
                            "the half asking for SDL 4: " + Describe(pieces));
        }

        #region the model

        /// <summary>
        /// One shell carrying SDL 3 cut for the load pass under the regions given: load 1 asks
        /// for SDL 3, load 2 for SDL 4.
        /// </summary>
        private static List<SlabZone> CutForLoads(List<LoadRegion_R> regions, List<string> issues,
                                                  Action<SlabMeshCompare> set)
        {
            LoadSchedule schedule = new LoadSchedule("plan.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "ONE", new LoadSet { Sdl = 3.0 }, "LL"));
            schedule.Add(new LoadScheduleEntry("2", "TWO", new LoadSet { Sdl = 4.0 }, "LL"));

            Slab_E slab = new Slab_E("A1", Corners(0, 0, Size, Size).Select(point => new Joint("", point, Form1.eLength.mm)).ToList(),
                                     Story, "S250", SlabKind.Slab, 250, 250, false);
            slab.SetLoads(new List<AreaLoad_E> { new AreaLoad_E("SDL", 3.0, 10, true, false) });

            List<SlabZone> zones = new List<SlabZone>();
            SlabMeshCompare compare = new SlabMeshCompare(zones, issues)
            {
                Purpose = SlabMeshPurpose.Loads,
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25,
                Loads = LoadCoverage.Build(regions, schedule, new List<string>()),
                LoadTolerance = 0.05,
                LoadPatterns = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData()),
                ShellsOnStory = new List<Slab_E> { slab }
            };
            set(compare);

            compare.CompareFaces(Story, new List<EtabsFace>
            {
                new EtabsFace { Source = slab, Plan = slab.Plan, IsWholeArea = true }
            }, new SlabCoverage());

            return zones;
        }

        private static double AreaWhere(IEnumerable<SlabZone> zones, SlabLoadState state)
        {
            return zones.Where(zone => zone.LoadState == state).Sum(zone => zone.Area);
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

        private static string Describe(IEnumerable<SlabZone> zones)
        {
            return string.Join(", ", zones.GroupBy(zone => zone.LoadState)
                                          .Select(group => $"{group.Key}={group.Sum(zone => zone.Area) / 1e6:0.##} m2"));
        }

        #endregion
    }
}
