using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// An upstand's height read off its elevation rather than off its bounding box: a wall
    /// whose profile was edited is as high as it is at each place, and is cut into a load per
    /// length at one height.
    /// </summary>
    [TestClass]
    public class WallProfileTests
    {
        private const double Shortest = 250;

        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadSearchRadius = 1500;
            pref.LineLoadLocationTolerance = 400;
            pref.LineLoadAngleTolerance = 20;
            pref.LineLoadMinimumIssueLength = 250;
            pref.LineLoadCheckInSteps = true;
            pref.LineLoadStepLength = 250;
            pref.RunSidlCCheck = true;
            pref.SidlCToleranceBelow = 0.3;
            pref.SidlCToleranceAbove = 3;
        }

        [TestMethod]
        public void AWallOfOneHeightIsOneLength()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(new[] { Loop(0, 0, 6000, 0, 6000, 1200, 0, 1200) }, 6000, Shortest);

            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual(0, parts[0].From, 1e-6);
            Assert.AreEqual(6000, parts[0].To, 1e-6);
            Assert.AreEqual(1200, parts[0].Height, 1e-6);
            Assert.IsFalse(parts[0].Varies);
        }

        /// <summary>1.2 m high for 3 m, stepped down to 0.3 m for the last 2 m: two loads, not 1.2 m all along.</summary>
        [TestMethod]
        public void AStepDownIsTwoLengths()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(
                new[] { Loop(0, 0, 5000, 0, 5000, 300, 3000, 300, 3000, 1200, 0, 1200) }, 5000, Shortest);

            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(3000, parts[0].To, 1e-6);
            Assert.AreEqual(1200, parts[0].Height, 1e-6);
            Assert.AreEqual(3000, parts[1].From, 1e-6);
            Assert.AreEqual(300, parts[1].Height, 1e-6);
        }

        /// <summary>A top sloping from 0.3 m to 1.2 m is one length at its average, 0.75 m, and says it is an average.</summary>
        [TestMethod]
        public void ASlopeIsItsAverage()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(
                new[] { Loop(0, 0, 4000, 0, 4000, 1200, 0, 300) }, 4000, Shortest);

            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual(750, parts[0].Height, 1e-6);
            Assert.IsTrue(parts[0].Varies);
        }

        /// <summary>A door cut right through it carries nothing: the wall either side of it, and nothing between.</summary>
        [TestMethod]
        public void ADoorThroughItIsAGap()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(new[]
            {
                Loop(0, 0, 2000, 0, 2000, 1200, 0, 1200),
                Loop(3000, 0, 6000, 0, 6000, 1200, 3000, 1200)
            }, 6000, Shortest);

            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(2000, parts[0].To, 1e-6);
            Assert.AreEqual(3000, parts[1].From, 1e-6);
            Assert.IsTrue(parts.All(part => part.Height == 1200));
        }

        /// <summary>A window in it is a lighter length: 1.2 m less the 0.6 m of opening.</summary>
        [TestMethod]
        public void AnOpeningInItIsALighterLength()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(new[]
            {
                Loop(0, 0, 6000, 0, 6000, 1200, 0, 1200),
                Loop(1000, 300, 1000, 900, 2000, 900, 2000, 300)
            }, 6000, Shortest);

            Assert.AreEqual(3, parts.Count);
            Assert.AreEqual(1200, parts[0].Height, 1e-6);
            Assert.AreEqual(600, parts[1].Height, 1e-6);
            Assert.AreEqual(1200, parts[2].Height, 1e-6);
        }

        /// <summary>A notch too short to be a load of its own is weighed with what it sits in.</summary>
        [TestMethod]
        public void AShortNotchIsFoldedIn()
        {
            List<WallProfile.Part> parts = WallProfile.Parts(
                new[] { Loop(0, 0, 6000, 0, 6000, 1200, 3100, 1200, 3100, 600, 3000, 600, 3000, 1200, 0, 1200) },
                6000, Shortest);

            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual(0, parts[0].From, 1e-6);
            Assert.AreEqual(6000, parts[0].To, 1e-6);
            Assert.AreEqual(1190, parts[0].Height, 1);
        }

        /// <summary>
        /// A wall butting into one face cuts a hole in that face only: the other face is whole,
        /// and the upstand is no lower there.
        /// </summary>
        [TestMethod]
        public void AWallButtingIntoOneFaceIsNoGap()
        {
            List<IList<double[]>> exterior = new List<IList<double[]>>
            {
                Loop(0, 0, 2000, 0, 2000, 1200, 0, 1200),
                Loop(2200, 0, 6000, 0, 6000, 1200, 2200, 1200)
            };
            List<IList<double[]>> interior = new List<IList<double[]>> { Loop(0, 0, 6000, 0, 6000, 1200, 0, 1200) };

            List<WallProfile.Part> parts = WallProfile.Parts(new[] { exterior, interior }, 6000, Shortest);

            Assert.AreEqual(1, parts.Count);
            Assert.AreEqual(1200, parts[0].Height, 1e-6);
        }

        [TestMethod]
        public void ALineIsCutBetweenTwoDistancesAlongIt()
        {
            List<MyPoint> straight = WallProfile.Cut(new[] { At(0, 0), At(6000, 0) }, 1000, 2500);
            Assert.AreEqual(2, straight.Count);
            Assert.AreEqual(1000, straight[0].X, 1e-6);
            Assert.AreEqual(2500, straight[1].X, 1e-6);

            // Across the corner of an L: the corner is kept.
            List<MyPoint> bent = WallProfile.Cut(new[] { At(0, 0), At(3000, 0), At(3000, 3000) }, 2000, 4000);
            Assert.AreEqual(3, bent.Count);
            Assert.AreEqual(2000, bent[0].X, 1e-6);
            Assert.AreEqual(3000, bent[1].X, 1e-6);
            Assert.AreEqual(1000, bent[2].Y, 1e-6);
        }

        /// <summary>
        /// The stepped wall checked against a frame carrying 6 over its first 3 m and 1.5 over
        /// the rest: both lengths match, each says which part it is, and each is filed apart -
        /// accepting one is not accepting the other.
        /// </summary>
        [TestMethod]
        public void EachLengthIsCheckedAndFiledOnItsOwn()
        {
            List<LineLoad_R> lines = new[]
            {
                Length(1, At(0, 150), At(3000, 150), 1200),
                Length(2, At(3000, 150), At(5000, 150), 300)
            }.Select(wall => wall.ToLineLoad(25, 0.1)).ToList();

            Assert.AreEqual(6.0, lines[0].OwnPerMetre, 1e-9);
            Assert.AreEqual(1.5, lines[1].OwnPerMetre, 1e-9);
            StringAssert.Contains(lines[1].StyleName, "part 2 of 2");

            LineLoadFrame_E frame = new LineLoadFrame_E("B1", "B1", "L03", At(0, 0), At(5000, 0));
            frame.Add(new LineLoad_E("SIDL_C", 6.0, 6.0, 0, 0.6, 10, true, false));
            frame.Add(new LineLoad_E("SIDL_C", 1.5, 1.5, 0.6, 1, 10, true, false));

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                Patterns = LineLoadPatterns.From("SIDL_C"),
                Band = new LoadBand(pref.SidlCToleranceBelow, pref.SidlCToleranceAbove)
            };
            engine.StartComparison(lines, new[] { frame });

            Assert.AreEqual(2, engine.Results.Count);
            Assert.IsTrue(engine.Results.All(result => result.State == LineLoadState.Matched),
                          string.Join("; ", engine.Results.Select(result => result.Summary)));

            List<string> keys = engine.Results
                .SelectMany(result => ElementViewModelFactory.FromLineLoadResult(result, true))
                .Select(element => element.ReviewKey)
                .ToList();
            Assert.AreEqual(2, keys.Count);
            Assert.AreNotEqual(keys[0], keys[1]);
        }

        private static SidlCSource_R Length(int part, MyPoint from, MyPoint to, double height)
        {
            return new SidlCSource_R(SidlCSourceKind.Wall, 501, new[] { from, to }, 200, height, 0)
            {
                TypeName = "Upstand-200",
                Story = "L03",
                Part = part,
                Parts = 2
            };
        }

        /// <summary>A loop of the elevation off (s, z) pairs, in mm.</summary>
        private static IList<double[]> Loop(params double[] sz)
        {
            List<double[]> points = new List<double[]>();
            for (int i = 0; i + 1 < sz.Length; i += 2)
            {
                points.Add(new[] { sz[i], sz[i + 1] });
            }
            return points;
        }

        private static MyPoint At(double x, double y)
        {
            return new MyPoint(x, y, 0);
        }
    }
}
