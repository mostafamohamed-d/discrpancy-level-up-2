using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The plan geometry the slab check reasons with.
    ///
    /// The boolean operations themselves belong to the Revit kernel and can only run inside
    /// Revit, so they are not tested here. What is tested is everything the check decides for
    /// itself once it has the pieces: whether a piece of slab is a hole in the middle of the
    /// bay or an edge the two models drew differently, and whether it is a real difference or
    /// the hairline two outlines leave when they were drawn to the same grid line and landed a
    /// fraction of a millimetre apart. Those two calls are what the results actually say, and
    /// neither is obvious from reading the code.
    /// </summary>
    [TestClass]
    public class PlanPolygonTests
    {
        // The defaults the slab check runs with; see PrefrencesData.
        private const double MinimumZoneArea = 10000;
        private const double MinimumZoneWidth = 20;
        private const double EdgeTolerance = 25;

        #region rings

        [TestMethod]
        public void TwoPointsCannotEncloseAnArea()
        {
            Assert.IsNull(PlanRing.FromPoints(new[] { Point(0, 0), Point(100, 0) }));
            Assert.IsNull(PlanRing.FromPoints(null));
        }

        [TestMethod]
        public void RepeatedAndClosingPointsAreDropped()
        {
            PlanRing ring = PlanRing.FromPoints(new[]
            {
                Point(0, 0), Point(0, 0.5), Point(1000, 0), Point(1000, 1000), Point(0, 1000), Point(0, 0)
            });

            Assert.IsNotNull(ring);
            Assert.AreEqual(4, ring.Points.Count,
                "a point half a millimetre from the last one, and a closing point repeating the first, "
                + "are both redundant and would leave the kernel an edge it refuses");
        }

        [TestMethod]
        public void OutlineIsWoundCounterClockwiseAndHolesClockwise()
        {
            // Clockwise on the way in.
            PlanPolygon region = PlanPolygon.Create(
                PlanRing.FromPoints(new[] { Point(0, 0), Point(0, 1000), Point(1000, 1000), Point(1000, 0) }),
                new[] { PlanRing.FromPoints(Square(200, 200, 200)) });

            Assert.IsTrue(region.Outer.IsCounterClockwise);
            Assert.AreEqual(1, region.Holes.Count);
            Assert.IsFalse(region.Holes[0].IsCounterClockwise);
        }

        #endregion

        #region area and containment

        [TestMethod]
        public void AreaTakesTheHolesOff()
        {
            PlanPolygon region = SquareWithHole();
            Assert.AreEqual(1000d * 1000 - 400d * 400, region.Area, 1);
        }

        [TestMethod]
        public void APointInAnOpeningIsNotOnTheSlab()
        {
            PlanPolygon region = SquareWithHole();

            Assert.IsTrue(region.Contains(100, 100), "on the slab");
            Assert.IsFalse(region.Contains(400, 400), "in the opening");
            Assert.IsFalse(region.Contains(1500, 500), "off the slab altogether");
        }

        [TestMethod]
        public void DistanceIsZeroOnTheSlabAndMeasuresToTheEdgeOffIt()
        {
            PlanPolygon region = PlanPolygon.FromPoints(Square(0, 0, 1000));

            Assert.AreEqual(0, region.DistanceTo(500, 500), 1e-9);
            Assert.AreEqual(300, region.DistanceTo(1300, 500), 1e-6);
        }

        #endregion

        #region telling a real difference from a hairline

        [TestMethod]
        public void ASubMillimetreHairlineIsNotARegionAtAll()
        {
            // What two outlines drawn to the same grid line leave behind: five metres long,
            // a third of a millimetre wide. It never even becomes a ring.
            PlanPolygon hairline = PlanPolygon.FromPoints(new[]
            {
                Point(0, 0), Point(5000, 0), Point(5000, 0.33), Point(0, 0.33)
            });

            Assert.IsNull(hairline);
        }

        [TestMethod]
        public void TheWidthTestCatchesWhatTheAreaTestDoesNot()
        {
            // 15 mm by a metre. Its area alone clears the floor, so only the width test
            // catches it - which is the whole reason there are two of them.
            PlanPolygon narrow = PlanPolygon.FromPoints(new[]
            {
                Point(0, 0), Point(1000, 0), Point(1000, 15), Point(0, 15)
            });

            Assert.IsTrue(narrow.Area >= MinimumZoneArea, "clears the area floor on its own");
            Assert.IsTrue(narrow.TypicalWidth < MinimumZoneWidth, "but is too thin to mean anything");
        }

        [TestMethod]
        public void ARealMismatchedStripSurvivesBothTests()
        {
            // Five metres of edge where the two models disagree by 150 mm.
            PlanPolygon strip = PlanPolygon.FromPoints(new[]
            {
                Point(0, 0), Point(5000, 0), Point(5000, 150), Point(0, 150)
            });

            Assert.IsTrue(strip.Area >= MinimumZoneArea);
            Assert.IsTrue(strip.TypicalWidth >= MinimumZoneWidth);
        }

        #endregion

        #region telling an opening from an edge

        [TestMethod]
        public void AStripAlongTheSlabEdgeReadsAsAnOutlineThatDoesNotAgree()
        {
            PlanPolygon face = PlanPolygon.FromPoints(Square(0, 0, 5000));
            PlanPolygon strip = PlanPolygon.FromPoints(new[]
            {
                Point(0, 0), Point(150, 0), Point(150, 5000), Point(0, 5000)
            });

            Assert.IsTrue(strip.TouchesOutlineOf(face, EdgeTolerance));
        }

        [TestMethod]
        public void AHoleInTheMiddleReadsAsAnOpening()
        {
            PlanPolygon face = PlanPolygon.FromPoints(Square(0, 0, 5000));
            PlanPolygon hole = PlanPolygon.FromPoints(Square(2000, 2000, 800));

            Assert.IsFalse(hole.TouchesOutlineOf(face, EdgeTolerance),
                "a hole that touches nothing is an opening one model has and the other has not");
        }

        [TestMethod]
        public void ANotchOutOfTheSideIsAnEdgeRatherThanAnOpening()
        {
            PlanPolygon face = PlanPolygon.FromPoints(Square(0, 0, 5000));
            PlanPolygon notch = PlanPolygon.FromPoints(new[]
            {
                Point(0, 2000), Point(800, 2000), Point(800, 2800), Point(0, 2800)
            });

            Assert.IsTrue(notch.TouchesOutlineOf(face, EdgeTolerance));
        }

        #endregion

        #region label anchors

        [TestMethod]
        public void TheLabelAnchorOfAnLShapeLandsInsideIt()
        {
            // The centroid of an L falls in the missing quarter.
            PlanPolygon shape = PlanPolygon.FromPoints(new[]
            {
                Point(0, 0), Point(1000, 0), Point(1000, 300),
                Point(300, 300), Point(300, 1000), Point(0, 1000)
            });

            MyPoint anchor = shape.InteriorPoint();
            Assert.IsTrue(shape.Contains(anchor.X, anchor.Y), "anchor landed at " + anchor);
        }

        [TestMethod]
        public void TheLabelAnchorOfASlabWithABigOpeningLandsOnTheSlab()
        {
            PlanPolygon ring = PlanPolygon.Create(
                PlanRing.FromPoints(Square(0, 0, 1000)),
                new[] { PlanRing.FromPoints(Square(200, 200, 600)) });

            MyPoint anchor = ring.InteriorPoint();
            Assert.IsTrue(ring.Contains(anchor.X, anchor.Y), "anchor landed at " + anchor);
        }

        #endregion

        #region bounds

        [TestMethod]
        public void AreasMeetingOnAGridLineAreStillCompared()
        {
            PlanPolygon left = PlanPolygon.FromPoints(Square(0, 0, 1000));
            PlanPolygon right = PlanPolygon.FromPoints(Square(1000, 0, 1000));
            PlanPolygon far = PlanPolygon.FromPoints(Square(9000, 0, 1000));

            Assert.IsTrue(left.Bounds.IntersectsWith(right.Bounds));
            Assert.IsFalse(left.Bounds.IntersectsWith(far.Bounds));
        }

        #endregion

        #region helpers

        private static MyPoint Point(double x, double y)
        {
            return new MyPoint(x, y);
        }

        private static MyPoint[] Square(double x, double y, double side)
        {
            return new[]
            {
                Point(x, y), Point(x + side, y), Point(x + side, y + side), Point(x, y + side)
            };
        }

        private static PlanPolygon SquareWithHole()
        {
            return PlanPolygon.Create(
                PlanRing.FromPoints(Square(0, 0, 1000)),
                new[] { PlanRing.FromPoints(Square(200, 200, 400)) });
        }

        #endregion
    }
}
