using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A line load drawn twice in the same place is weighed once: the same style over exactly
    /// the same line is a copy, and anything that differs is a line of its own.
    /// </summary>
    [TestClass]
    public class LineLoadDuplicateTests
    {
        private const string Story = "L03";
        private const string TL1 = "MRG_STR-Line Load-TL1";
        private const string TL2 = "MRG_STR-Line Load-TL2";

        private static LineLoad_R Line(long id, string style, params double[] xy)
        {
            List<MyPoint> points = new List<MyPoint>();
            for (int i = 0; i < xy.Length; i += 2)
            {
                points.Add(new MyPoint(xy[i], xy[i + 1]));
            }

            return new LineLoad_R(id, style, points, LineLoad_R.NumberFromStyleName(style), Story);
        }

        private static List<LineLoad_R> Kept(out List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped,
                                             params LineLoad_R[] lines)
        {
            return LineLoad_R.WithoutDuplicates(lines, out dropped);
        }

        [TestMethod]
        public void TheSameLineDrawnTwiceIsKeptOnce()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(902, TL1, 0, 0, 5000, 0),
                                         Line(901, TL1, 0, 0, 5000, 0));

            Assert.AreEqual(1, kept.Count);
            Assert.AreEqual(901, kept[0].RevitID, "the lowest id is the one kept");
            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual(902, dropped[0].Key.RevitID);
            Assert.AreEqual(901, dropped[0].Value.RevitID);
        }

        [TestMethod]
        public void ThreeCopiesLeaveOne()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 5000, 0),
                                         Line(2, TL1, 0, 0, 5000, 0),
                                         Line(3, TL1, 0, 0, 5000, 0));

            Assert.AreEqual(1, kept.Count);
            Assert.IsTrue(dropped.All(copy => copy.Value.RevitID == 1));
        }

        [TestMethod]
        public void ALineDrawnTheOtherWayRoundIsStillACopy()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 3000, 0, 3000, 4000),
                                         Line(2, TL1, 3000, 4000, 3000, 0, 0, 0));

            Assert.AreEqual(1, kept.Count);
        }

        [TestMethod]
        public void LessThanAMillimetreApartIsTheSamePlace()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 5000, 0),
                                         Line(2, TL1, 0.3, 0.2, 5000.4, -0.1));

            Assert.AreEqual(1, kept.Count);
        }

        [TestMethod]
        public void ADifferentLengthIsTwoLines()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 5000, 0),
                                         Line(2, TL1, 0, 0, 4000, 0));

            Assert.AreEqual(2, kept.Count);
            Assert.AreEqual(0, dropped.Count);
        }

        [TestMethod]
        public void TheSameLengthSomewhereElseIsTwoLines()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 5000, 0),
                                         Line(2, TL1, 0, 10, 5000, 10));

            Assert.AreEqual(2, kept.Count);
        }

        [TestMethod]
        public void TheSameEndsBentDifferentlyIsTwoLines()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 2500, 0, 5000, 0),
                                         Line(2, TL1, 0, 0, 2500, 1000, 5000, 0));

            Assert.AreEqual(2, kept.Count);
        }

        [TestMethod]
        public void ADifferentLoadInTheSamePlaceIsKept()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(1, TL1, 0, 0, 5000, 0),
                                         Line(2, TL2, 0, 0, 5000, 0));

            Assert.AreEqual(2, kept.Count);
        }

        [TestMethod]
        public void TheOrderOfWhatIsKeptIsUnchanged()
        {
            List<KeyValuePair<LineLoad_R, LineLoad_R>> dropped;
            List<LineLoad_R> kept = Kept(out dropped,
                                         Line(30, TL2, 0, 5000, 5000, 5000),
                                         Line(20, TL1, 0, 0, 5000, 0),
                                         Line(10, TL1, 0, 0, 5000, 0),
                                         Line(40, TL1, 0, 9000, 5000, 9000));

            CollectionAssert.AreEqual(new long[] { 30, 10, 40 }, kept.Select(line => line.RevitID).ToArray());
        }
    }
}
