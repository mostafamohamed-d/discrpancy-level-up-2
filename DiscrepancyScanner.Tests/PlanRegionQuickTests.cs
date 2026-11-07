using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The two questions about a pair of regions that are settled without the geometry kernel,
    /// and the arrangement that used to get one of them badly wrong: two areas that share an
    /// edge exactly.
    ///
    /// That arrangement is not unusual, it is what cutting shells apart produces - along a
    /// loading plan boundary, along a line where the thickness changes - so a whole basement of
    /// them lands on this test at once. Answered from a corner, which sits exactly on the
    /// neighbour's edge, "is one inside the other" came back yes for areas merely beside each
    /// other, and PlanRegionMath.Subtract then took the whole of the area away. Thirteen ETABS
    /// areas were dropped that way on one story, silently, and the plan was blank where they
    /// had been.
    /// </summary>
    [TestClass]
    public class PlanRegionQuickTests
    {
        [TestMethod]
        public void AnAreaBesideAnotherIsNotInsideIt()
        {
            // Two squares meeting on x = 1000. Every corner of the right hand one that lies on
            // that line is on the left hand one's edge.
            PlanPolygon left = Rectangle(0, 0, 1000, 1000);
            PlanPolygon right = Rectangle(1000, 0, 2000, 1000);

            Assert.IsFalse(PlanRegionQuick.Covers(left, right),
                "an area beside another is not covered by it");
            Assert.IsFalse(PlanRegionQuick.Covers(right, left),
                "and the same the other way round");
        }

        [TestMethod]
        public void ATrianglePutInTheCornerOfAnotherAreaIsNotInsideIt()
        {
            // The shape in the model this came from: a triangle cut off a shell, sitting in the
            // corner of the area beside it and sharing both of the edges it touches. Its
            // bounding box is inside the other's, nothing crosses, and every one of its corners
            // is on the other's outline - which is every way this could be got wrong at once.
            PlanPolygon shell = Rectangle(0, 0, 6000, 6000);
            PlanPolygon triangle = PlanPolygon.FromPoints(new[]
            {
                new MyPoint(6000, 0),
                new MyPoint(9000, 0),
                new MyPoint(6000, 3000)
            });

            Assert.IsFalse(PlanRegionQuick.Covers(shell, triangle),
                "the triangle is beside the shell, not inside it");
            Assert.IsTrue(PlanRegionQuick.Disjoint(shell, triangle),
                "sharing an edge is not overlapping, so there is nothing for the kernel to do");
        }

        /// <summary>
        /// A bay split corner to corner, so the two halves share a diagonal, at the coordinates
        /// a real site sits on rather than near the origin.
        ///
        /// Honest note on what this test is and is not: on a real basement thirteen ETABS areas
        /// were wrongly judged covered and deleted, and asking at an interior point instead of
        /// at a corner stopped it. This does not reproduce that - it passes either way. Neither
        /// does any other shape tried here. Whether an even-odd crossing count calls a point
        /// exactly on an outline inside it is undefined rather than wrong, so which arrangements
        /// come out badly depends on the coordinates, and the ones that did have not been
        /// captured. What these tests do is hold the contract still: they say what the answers
        /// have to be for areas beside, inside, and overlapping each other, so a later change
        /// cannot quietly move them.
        /// </summary>
        [TestMethod]
        public void ABaySplitCornerToCornerIsTwoAreasBesideEachOtherNotOneInsideTheOther()
        {
            // Near where the model actually sits: six figure millimetres, so the 1 mm2 slack in
            // the crossing test is a far smaller share of the numbers than it is near zero.
            const double x0 = 120272.644, y0 = 92621.677;
            const double x1 = 126378.837, y1 = 93997.750;

            PlanPolygon lower = PlanPolygon.FromPoints(new[]
            {
                new MyPoint(x0, y0),
                new MyPoint(x1, y0),
                new MyPoint(x0, y1)
            });

            PlanPolygon upper = PlanPolygon.FromPoints(new[]
            {
                new MyPoint(x1, y0),
                new MyPoint(x1, y1),
                new MyPoint(x0, y1)
            });

            Assert.IsFalse(PlanRegionQuick.Covers(lower, upper),
                "the two halves of a bay split corner to corner sit beside each other; neither " +
                "covers the other, and saying otherwise deletes one of them outright");
            Assert.IsFalse(PlanRegionQuick.Covers(upper, lower),
                "and the same the other way round");
            Assert.IsTrue(PlanRegionQuick.Disjoint(lower, upper),
                "they share the diagonal and nothing else, so there is nothing to subtract");
        }

        [TestMethod]
        public void AnAreaGenuinelyInsideAnotherIsStillFound()
        {
            // The saving this test exists to protect: a region wholly within another still has
            // to be answered here rather than sent to the kernel.
            PlanPolygon outer = Rectangle(0, 0, 1000, 1000);
            PlanPolygon inner = Rectangle(200, 200, 800, 800);

            Assert.IsTrue(PlanRegionQuick.Covers(outer, inner));
            Assert.IsFalse(PlanRegionQuick.Covers(inner, outer));
            Assert.IsFalse(PlanRegionQuick.Disjoint(outer, inner));
        }

        [TestMethod]
        public void AnAreaInsideAHoleIsNotCovered()
        {
            // Covered means covered by concrete, so a region sitting in an opening is not.
            PlanPolygon ring = PlanPolygon.Create(
                Ring(0, 0, 1000, 1000),
                new List<PlanRing> { Ring(200, 200, 800, 800) });

            PlanPolygon inTheHole = Rectangle(300, 300, 700, 700);

            Assert.IsFalse(PlanRegionQuick.Covers(ring, inTheHole),
                "the region is in the opening, so nothing covers it");
        }

        [TestMethod]
        public void AreasThatMissEachOtherEntirelyAreDisjoint()
        {
            Assert.IsTrue(PlanRegionQuick.Disjoint(
                Rectangle(0, 0, 1000, 1000),
                Rectangle(5000, 5000, 6000, 6000)));
        }

        [TestMethod]
        public void AreasThatGenuinelyOverlapAreNotDisjoint()
        {
            Assert.IsFalse(PlanRegionQuick.Disjoint(
                Rectangle(0, 0, 1000, 1000),
                Rectangle(500, 500, 1500, 1500)));
        }

        /// <summary>
        /// An L shape whose centroid falls outside it, put round the outside of a square that
        /// touches it. Nothing here may be decided by where the centroid happens to land.
        /// </summary>
        [TestMethod]
        public void AnLShapedAreaIsJudgedByAPointThatIsActuallyInsideIt()
        {
            PlanPolygon shape = PlanPolygon.FromPoints(new[]
            {
                new MyPoint(0, 0),
                new MyPoint(3000, 0),
                new MyPoint(3000, 1000),
                new MyPoint(1000, 1000),
                new MyPoint(1000, 3000),
                new MyPoint(0, 3000)
            });

            // Sits in the notch of the L, sharing both edges of it.
            PlanPolygon inTheNotch = Rectangle(1000, 1000, 3000, 3000);

            Assert.IsFalse(PlanRegionQuick.Covers(shape, inTheNotch),
                "the notch of an L is outside it, however the centroid falls");
            Assert.IsTrue(PlanRegionQuick.Disjoint(shape, inTheNotch));
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        private static PlanRing Ring(double minX, double minY, double maxX, double maxY)
        {
            return PlanRing.FromPoints(Corners(minX, minY, maxX, maxY));
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
    }
}
