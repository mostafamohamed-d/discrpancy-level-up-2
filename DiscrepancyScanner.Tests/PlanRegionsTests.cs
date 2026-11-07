using System;
using System.Collections.Generic;
using System.Linq;
using DiscrepancyScanner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading a drafted region - a filled region off a loading plan - into outlines and the
    /// openings cut in them.
    ///
    /// A filled region is a sketch, and its loops come back wound however each of them
    /// happened to be drawn. Sorting them by winding read a clockwise outline as a hole and
    /// then threw it away, because it was not inside anything; worse, and what these tests are
    /// really about, it read an opening wound the same way as its outline as a solid piece of
    /// region sitting on top of the region it was cut out of. A real loading plan has both in
    /// one region at once, so nothing here may depend on the winding of anything.
    /// </summary>
    [TestClass]
    public class PlanRegionsTests
    {
        [TestMethod]
        public void OneRingIsOneRegionWhicheverWayItWasDrawn()
        {
            foreach (bool counterClockwise in new[] { true, false })
            {
                IList<PlanPolygon> regions = PlanRegions.FromDraftedRings(
                    new List<PlanRing> { Ring(counterClockwise, 100) });

                Assert.AreEqual(1, regions.Count);
                Assert.AreEqual(0, regions[0].Holes.Count);
                Assert.AreEqual(100d * 100d, regions[0].Area, 1e-6);
                Assert.IsTrue(regions[0].Outer.IsCounterClockwise,
                              "the outline is handed on wound the way the kernel expects");
            }
        }

        [TestMethod]
        public void AnOpeningIsAnOpeningWhicheverWayItWasDrawn()
        {
            // The bug this class exists for. Both windings of the inner ring have to come out
            // as one region with a hole in it - when the inner ring was read as an outline of
            // its own, the region covered the ground inside its own opening and every patch
            // drawn in that opening was reported as an overlap.
            foreach (bool outerCcw in new[] { true, false })
            {
                foreach (bool innerCcw in new[] { true, false })
                {
                    IList<PlanPolygon> regions = PlanRegions.FromDraftedRings(
                        new List<PlanRing> { Ring(outerCcw, 100), Ring(innerCcw, 40) });

                    string how = $"outline {outerCcw}, opening {innerCcw}";

                    Assert.AreEqual(1, regions.Count, how);
                    Assert.AreEqual(1, regions[0].Holes.Count, how);
                    Assert.AreEqual(100d * 100d - 40d * 40d, regions[0].Area, 1e-6, how);

                    Assert.IsFalse(regions[0].Contains(0, 0),
                                   "the middle of the opening is not part of the region - " + how);
                    Assert.IsTrue(regions[0].Contains(45, 45),
                                  "the region itself still is - " + how);
                }
            }
        }

        [TestMethod]
        public void SeveralIslandsAreSeveralRegions()
        {
            // One filled region drawn as two separate patches, one of them wound against the
            // other. Neither is inside anything, so both are regions in their own right.
            IList<PlanPolygon> regions = PlanRegions.FromDraftedRings(new List<PlanRing>
            {
                Ring(true, 40, atX: 0),
                Ring(false, 30, atX: 500)
            });

            Assert.AreEqual(2, regions.Count);
            Assert.IsTrue(regions.All(region => region.Holes.Count == 0));
            CollectionAssert.AreEquivalent(
                new[] { 40d * 40d, 30d * 30d },
                regions.Select(region => Math.Round(region.Area, 6)).ToArray());
        }

        [TestMethod]
        public void AnIslandInsideAnOpeningIsARegionOfItsOwn()
        {
            // Three rings nested. The middle one is an opening in the biggest; the innermost
            // sits inside that opening, so it is solid region again rather than a hole in a
            // hole - and the winding says nothing about any of it.
            IList<PlanPolygon> regions = PlanRegions.FromDraftedRings(new List<PlanRing>
            {
                Ring(false, 100),
                Ring(false, 60),
                Ring(false, 20)
            });

            Assert.AreEqual(2, regions.Count);

            PlanPolygon outer = regions.Single(region => region.Holes.Count == 1);
            PlanPolygon island = regions.Single(region => region.Holes.Count == 0);

            Assert.AreEqual(100d * 100d - 60d * 60d, outer.Area, 1e-6);
            Assert.AreEqual(20d * 20d, island.Area, 1e-6);

            Assert.IsTrue(island.Contains(0, 0), "the middle is inside the island");
            Assert.IsFalse(outer.Contains(0, 0), "and not inside the region around the opening");
        }

        [TestMethod]
        public void TwoOpeningsWoundAgainstEachOtherAreBothOpenings()
        {
            // Exactly what the loading plan that started this does: one outline with two
            // openings in it, one wound with the outline and one against it.
            IList<PlanPolygon> regions = PlanRegions.FromDraftedRings(new List<PlanRing>
            {
                Ring(true, 200),
                Ring(true, 30, atX: -80),
                Ring(false, 30, atX: 80)
            });

            Assert.AreEqual(1, regions.Count);
            Assert.AreEqual(2, regions[0].Holes.Count);
            Assert.AreEqual(200d * 200d - 2 * 30d * 30d, regions[0].Area, 1e-6);

            Assert.IsFalse(regions[0].Contains(-80, 0), "the first opening is not region");
            Assert.IsFalse(regions[0].Contains(80, 0), "and neither is the second");
        }

        [TestMethod]
        public void NothingIsNoRegions()
        {
            Assert.AreEqual(0, PlanRegions.FromDraftedRings(null).Count);
            Assert.AreEqual(0, PlanRegions.FromDraftedRings(new List<PlanRing>()).Count);
        }

        /// <summary>A square of the given size centred on the given x, wound as asked.</summary>
        private static PlanRing Ring(bool counterClockwise, double size, double atX = 0)
        {
            double half = size / 2d;

            List<MyPoint> points = new List<MyPoint>
            {
                new MyPoint(atX - half, -half, 0),
                new MyPoint(atX + half, -half, 0),
                new MyPoint(atX + half, half, 0),
                new MyPoint(atX - half, half, 0)
            };

            if (!counterClockwise)
            {
                points.Reverse();
            }

            PlanRing ring = PlanRing.FromPoints(points);
            Assert.AreEqual(counterClockwise, ring.IsCounterClockwise, "the test ring is wound as asked");
            return ring;
        }
    }
}
