using System;
using System.Collections.Generic;
using System.Linq;
using DiscrepancyScanner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The import used to ask ETABS about every object one at a time and now works most of it
    /// out from the two whole-model calls instead. That is a speed change and must not be an
    /// answer change, so what is checked here is that the arithmetic replacing those calls
    /// reaches the same verdicts the API was being asked for.
    ///
    /// The verdict that matters is where a run of beams may be merged into one. Getting it
    /// wrong does not throw: it silently merges through a column, or refuses to merge a beam
    /// that should have been merged, and the comparison quietly reports the wrong thing.
    /// </summary>
    [TestClass]
    public class ModelIndexTests
    {
        // ---------------------------------------------------------------- the point index

        [TestMethod]
        public void FramesAreIndexedByBothOfTheirEnds()
        {
            Dictionary<string, List<string>> byPoint = ModelIndex.FramesByPoint(
                2,
                new[] { "B1", "B2" },
                new[] { "P1", "P2" },
                new[] { "P2", "P3" });

            Assert.AreEqual(3, byPoint.Count, "three distinct points across two beams");
            CollectionAssert.AreEquivalent(new[] { "B1" }, byPoint["P1"]);
            CollectionAssert.AreEquivalent(new[] { "B1", "B2" }, byPoint["P2"], "the shared point holds both");
            CollectionAssert.AreEquivalent(new[] { "B2" }, byPoint["P3"]);
        }

        [TestMethod]
        public void TheIndexStopsAtTheCountEvenWhenTheArraysRunOnPastIt()
        {
            // ETABS hands back arrays that can be longer than the count it reports.
            Dictionary<string, List<string>> byPoint = ModelIndex.FramesByPoint(
                1,
                new[] { "B1", "B2" },
                new[] { "P1", "P2" },
                new[] { "P2", "P3" });

            Assert.AreEqual(2, byPoint.Count);
            Assert.IsFalse(byPoint.ContainsKey("P3"), "the frame past the count is not indexed");
        }

        [TestMethod]
        public void TheIndexHoldsFramesTheImportItselfThrowsAway()
        {
            // Dummy and rigid frames are dropped from the comparison, but ETABS would still
            // have named them when asked what meets a joint - and one of them being a column
            // still has to stop a merge running through it. So the index is built from the raw
            // list, not from the frames that survive filtering.
            Dictionary<string, List<string>> byPoint = ModelIndex.FramesByPoint(
                2,
                new[] { "B1", "DUM1" },
                new[] { "P1", "P1" },
                new[] { "P2", "P3" });

            CollectionAssert.AreEquivalent(new[] { "B1", "DUM1" }, byPoint["P1"],
                "an ignored frame is still something that meets the joint");
        }

        // ---------------------------------------------------------------- the wall points

        [TestMethod]
        public void OnlyThePointsOfWallAreasCountAsStandingOnAWall()
        {
            // Two areas end to end in the flat arrays: a floor on P1..P4, a wall on P5..P8.
            HashSet<string> onAWall = ModelIndex.WallPoints(
                2,
                new[] { false, true },
                new[] { 3, 7 },
                new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8" });

            CollectionAssert.AreEquivalent(new[] { "P5", "P6", "P7", "P8" }, onAWall.ToList());
        }

        [TestMethod]
        public void TheFirstAreaStartsAtTheBeginningOfThePointArray()
        {
            // The delimiter holds each area's LAST point, so the first area is the one case
            // that cannot be worked out by looking at the entry before it.
            HashSet<string> onAWall = ModelIndex.WallPoints(
                2,
                new[] { true, false },
                new[] { 3, 7 },
                new[] { "P1", "P2", "P3", "P4", "P5", "P6", "P7", "P8" });

            CollectionAssert.AreEquivalent(new[] { "P1", "P2", "P3", "P4" }, onAWall.ToList(),
                "the first area's points run from nought to its delimiter inclusive");
            Assert.AreEqual(0, ModelIndex.BoundaryStart(0, new[] { 3, 7 }));
            Assert.AreEqual(4, ModelIndex.BoundaryStart(1, new[] { 3, 7 }));
        }

        // ---------------------------------------------------------------- the merge verdict

        private static Dictionary<string, List<string>> Meeting(params string[] frames)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "P1", frames.ToList() }
            };
        }

        private static HashSet<string> NoWalls()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static Func<string, bool> ColumnsAre(params string[] columns)
        {
            HashSet<string> set = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            return name => set.Contains(name);
        }

        [TestMethod]
        public void TwoBeamsMeetingAtAPointCanBeMergedThroughIt()
        {
            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1", "B2"), NoWalls(), ColumnsAre());

            CollectionAssert.AreEqual(new[] { "B2" }, connected,
                "the beam itself is not one of its own neighbours");
        }

        [TestMethod]
        public void AColumnLandingOnTheJointStopsTheMerge()
        {
            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1", "B2", "C1"), NoWalls(), ColumnsAre("C1"));

            Assert.IsNull(connected, "a column at the joint means the run of beams ends there");
        }

        [TestMethod]
        public void ItDoesNotMatterWhereInTheListTheColumnTurnsUp()
        {
            // The API returned connections in its own order and the old code walked them in
            // that order; the index has no order at all. The verdict has to be the same either
            // way, or a merge would depend on how ETABS happened to list a joint.
            List<string> columnFirst = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("C1", "B1", "B2"), NoWalls(), ColumnsAre("C1"));

            List<string> columnLast = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1", "B2", "C1"), NoWalls(), ColumnsAre("C1"));

            Assert.IsNull(columnFirst);
            Assert.IsNull(columnLast);
        }

        [TestMethod]
        public void AWallOnTheJointStopsTheMerge()
        {
            HashSet<string> walls = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "P1" };

            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1", "B2"), walls, ColumnsAre());

            Assert.IsNull(connected, "a wall supporting the joint ends the run of beams");
        }

        [TestMethod]
        public void AFrameThatIsItselfAColumnStopsTheMerge()
        {
            // The old walk checked the design orientation of every frame at the joint
            // including the one it had been asked about, so a column returned null for its own
            // joints. Kept, because a column is not something to merge along.
            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "C1", Meeting("C1", "B2"), NoWalls(), ColumnsAre("C1"));

            Assert.IsNull(connected);
        }

        [TestMethod]
        public void AJointWithNothingElseOnItMergesWithNothingRatherThanStopping()
        {
            // An empty list and a null are read differently by the merge: empty means "carry
            // on, there is just nothing here", null means "do not go through this joint".
            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1"), NoWalls(), ColumnsAre());

            Assert.IsNotNull(connected, "an end with no neighbour is not an obstruction");
            Assert.AreEqual(0, connected.Count);
        }

        [TestMethod]
        public void APointTheModelNeverMentionedIsNotAnObstruction()
        {
            List<string> connected = ModelIndex.ConnectedFrames(
                "P9", "B1", Meeting("B1", "B2"), NoWalls(), ColumnsAre());

            Assert.IsNotNull(connected);
            Assert.AreEqual(0, connected.Count);
        }

        [TestMethod]
        public void EveryOtherFrameAtACrossingIsOfferedToTheMerge()
        {
            // Four beams meeting in a cross: the one being walked from sees the other three,
            // and it is the merge that decides which of them runs in the same direction.
            List<string> connected = ModelIndex.ConnectedFrames(
                "P1", "B1", Meeting("B1", "B2", "B3", "B4"), NoWalls(), ColumnsAre());

            CollectionAssert.AreEquivalent(new[] { "B2", "B3", "B4" }, connected);
        }

        [TestMethod]
        public void AFrameWithNoSectionIsANullLine()
        {
            // SAFE hands a line drawn with the None property back with no section name at all,
            // where ETABS names it; either way it is a line to hang a load on, not a beam.
            Assert.IsTrue(ModelIndex.IsNullFrame(""), "SAFE's null line");
            Assert.IsTrue(ModelIndex.IsNullFrame(null));
            Assert.IsTrue(ModelIndex.IsNullFrame("None"), "ETABS's null line");
            Assert.IsTrue(ModelIndex.IsNullFrame(" none "));

            Assert.IsFalse(ModelIndex.IsNullFrame("B400X800-C50"));
            Assert.IsFalse(ModelIndex.IsNullFrame("NONE-B300"), "only the property itself, not a name carrying it");
        }
    }
}
