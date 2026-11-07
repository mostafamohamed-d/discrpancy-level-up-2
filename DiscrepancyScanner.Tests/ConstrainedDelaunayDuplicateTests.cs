using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Constraints repeated on the same pair of points must not change the triangulation and
    /// must not make the crossing stage run away.
    /// </summary>
    [TestClass]
    public class ConstrainedDelaunayDuplicateTests
    {
        private static ConstrainedDelaunay Square(int copies, bool reversed)
        {
            var cdt = new ConstrainedDelaunay();
            int a = cdt.AddPoint(0, 0), b = cdt.AddPoint(10, 0), c = cdt.AddPoint(10, 10), d = cdt.AddPoint(0, 10);
            int e = cdt.AddPoint(5, 5);
            for (int i = 0; i < copies; i++)
            {
                cdt.AddConstraint(a, b); cdt.AddConstraint(b, c); cdt.AddConstraint(c, d); cdt.AddConstraint(d, a);
                if (reversed) { cdt.AddConstraint(b, a); cdt.AddConstraint(a, c); }
                else cdt.AddConstraint(a, c);
            }
            return cdt;
        }

        private static List<string> Describe(ConstrainedDelaunay cdt)
        {
            return cdt.Triangles
                      .Select(t => string.Join(",", t.Select(i => $"{cdt.Points[i].X:R}/{cdt.Points[i].Y:R}").OrderBy(x => x)))
                      .OrderBy(x => x).ToList();
        }

        [TestMethod]
        public void RepeatedAndReversedConstraintsGiveTheSameTriangles()
        {
            var one = Square(1, false); one.Triangulate();
            var many = Square(5, true); many.Triangulate();
            CollectionAssert.AreEqual(Describe(one), Describe(many));
            Assert.AreEqual(one.Constraints.Count, many.Constraints.Count);
        }

        [TestMethod]
        public void ZeroLengthConstraintsAreDropped()
        {
            var cdt = Square(1, false);
            cdt.Constraints.Add((2, 2));
            cdt.Triangulate();
            Assert.IsFalse(cdt.Constraints.Any(k => k.A == k.B));
        }

        [TestMethod]
        public void PartialOverlapsAreKept()
        {
            var cdt = new ConstrainedDelaunay();
            int a = cdt.AddPoint(0, 0), b = cdt.AddPoint(10, 0), c = cdt.AddPoint(5, 0), d = cdt.AddPoint(15, 0), e = cdt.AddPoint(5, 5);
            cdt.AddConstraint(a, b); cdt.AddConstraint(c, d); cdt.AddConstraint(a, e); cdt.AddConstraint(d, e);
            cdt.Triangulate();
            Assert.IsFalse(cdt.CrossingStageDidNotConverge);
            Assert.IsTrue(cdt.Constraints.Count >= 4, "nothing real was removed");
        }

        [TestMethod]
        public void ManyCopiesOfCrossingLinesConvergeAndKeepTheCrossing()
        {
            var cdt = new ConstrainedDelaunay();
            int a = cdt.AddPoint(0, 0), b = cdt.AddPoint(1000.123, 777.456), c = cdt.AddPoint(0, 800), d = cdt.AddPoint(1000.123, 3.3);
            for (int i = 0; i < 64; i++) { cdt.AddConstraint(a, b); cdt.AddConstraint(d, c); cdt.AddConstraint(b, a); }
            cdt.Triangulate();
            Assert.IsFalse(cdt.CrossingStageDidNotConverge);
            Assert.AreEqual(5, cdt.RealPointCount, "four ends and the one real crossing");
        }
    }
}
