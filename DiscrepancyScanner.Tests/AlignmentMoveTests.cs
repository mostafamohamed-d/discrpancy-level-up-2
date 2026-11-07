using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Changing the alignment of a model that has already been read.
    ///
    /// The alignment is baked into each element as it is built, so a new one used to mean
    /// reading the whole Revit model again. It does not have to: the difference between the
    /// old alignment and the new one is itself a rigid move, and applying that to geometry
    /// already placed has to land it exactly where the re-read would have. That "exactly" is
    /// what these tests are about - a move that is nearly right would drift a little further
    /// every time somebody nudged the alignment.
    /// </summary>
    [TestClass]
    public class AlignmentMoveTests
    {
        private const double Tolerance = 1e-6;

        [TestMethod]
        public void TheDifferenceLandsAPointWhereTheNewAlignmentWould()
        {
            MyTransformation from = Alignment(1200, -800, 250, 30, false);
            MyTransformation to = Alignment(-4000, 9000, 250, 105, false);

            AssertMovingMatchesRereading(from, to);
        }

        [TestMethod]
        public void ADifferenceWithNoTurnInItIsAPlainMove()
        {
            MyTransformation from = Alignment(1200, -800, 0, 42, false);
            MyTransformation to = Alignment(3200, -300, 0, 42, false);

            MyTransformation move = MyTransformation.Difference(to, from);

            Assert.AreEqual(0, move.Rotation, Tolerance, "the two alignments face the same way");
            Assert.IsFalse(move.IsMirroredAboutX);
            AssertMovingMatchesRereading(from, to);
        }

        [TestMethod]
        public void TheDifferenceFlipsAModelThatHasToBeFlipped()
        {
            MyTransformation from = Alignment(500, 500, 0, 15, false);
            MyTransformation to = Alignment(-2000, 700, 0, -40, true);

            MyTransformation move = MyTransformation.Difference(to, from);

            Assert.IsTrue(move.IsMirroredAboutX, "one alignment flips the model and the other does not");
            AssertMovingMatchesRereading(from, to);
        }

        [TestMethod]
        public void TheDifferenceUnflipsAModelThatWasFlippedByMistake()
        {
            MyTransformation from = Alignment(500, 500, 0, 15, true);
            MyTransformation to = Alignment(500, 500, 0, 15, false);

            AssertMovingMatchesRereading(from, to);
        }

        [TestMethod]
        public void TwoAlignmentsThatAgreeAskForNoMoveAtAll()
        {
            MyTransformation alignment = Alignment(7000, -1500, 300, 62, true);

            MyTransformation move = MyTransformation.Difference(alignment, alignment.Copy());

            Assert.AreEqual(0, move.Rotation, Tolerance);
            Assert.IsFalse(move.IsMirroredAboutX);
            Assert.AreEqual(0, move.Translation.X, Tolerance);
            Assert.AreEqual(0, move.Translation.Y, Tolerance);
            Assert.AreEqual(0, move.Translation.Z, Tolerance);
        }

        [TestMethod]
        public void NudgingTheAlignmentRepeatedlyDoesNotDriftTheModel()
        {
            MyTransformation placed = Alignment(0, 0, 0, 0, false);
            MyPoint raw = new MyPoint(12345, -6789, 3000);
            MyPoint moving = placed.ApplyToPointInMM(raw);

            // Twenty small corrections, one after another, of the kind somebody lining two
            // models up by eye actually makes.
            for (int step = 1; step <= 20; step++)
            {
                MyTransformation next = Alignment(step * 13.5, step * -7.25, step * 0.5, step * 1.75, false);
                moving = MyTransformation.Difference(next, placed).ApplyToPointInMM(moving);
                placed = next;
            }

            MyPoint reread = placed.ApplyToPointInMM(raw);

            Assert.AreEqual(reread.X, moving.X, 1e-6, "X after twenty nudges");
            Assert.AreEqual(reread.Y, moving.Y, 1e-6, "Y after twenty nudges");
            Assert.AreEqual(reread.Z, moving.Z, 1e-6, "Z after twenty nudges");
        }

        [TestMethod]
        public void AMovedWallEndsWhereARereadWallWouldHave()
        {
            MyTransformation before = DataContainer.ModelTransformation;
            try
            {
                MyPoint startInFeet = new MyPoint(10, 20, 12);
                MyPoint endInFeet = new MyPoint(40, 20, 12);

                MyTransformation from = Alignment(1200, -800, 250, 30, false);
                MyTransformation to = Alignment(-4000, 9000, 600, 105, true);

                DataContainer.ModelTransformation = from;
                Wall_R moved = new Wall_R(1, startInFeet, endInFeet);
                moved.SetThickness(250);

                DataContainer.ModelTransformation = to;
                Wall_R reread = new Wall_R(1, startInFeet, endInFeet);
                reread.SetThickness(250);

                moved.MoveBy(MyTransformation.Difference(to, from));

                Assert.AreEqual(reread.PlanStart.X, moved.PlanStart.X, Tolerance, "start X");
                Assert.AreEqual(reread.PlanStart.Y, moved.PlanStart.Y, Tolerance, "start Y");
                Assert.AreEqual(reread.PlanStart.Z, moved.PlanStart.Z, Tolerance, "start Z");
                Assert.AreEqual(reread.PlanEnd.X, moved.PlanEnd.X, Tolerance, "end X");
                Assert.AreEqual(reread.PlanEnd.Y, moved.PlanEnd.Y, Tolerance, "end Y");
            }
            finally
            {
                DataContainer.ModelTransformation = before;
            }
        }

        [TestMethod]
        public void AMovedColumnKeepsFacingTheWayARereadColumnWould()
        {
            MyTransformation before = DataContainer.ModelTransformation;
            try
            {
                MyPoint pointInFeet = new MyPoint(30, -15, 0);
                double rotationRadians = Math.PI / 6;

                MyTransformation from = Alignment(1200, -800, 0, 30, false);
                MyTransformation to = Alignment(-4000, 9000, 0, 105, true);

                DataContainer.ModelTransformation = from;
                Column_r moved = new Column_r(1, pointInFeet, rotationRadians);

                DataContainer.ModelTransformation = to;
                Column_r reread = new Column_r(1, pointInFeet, rotationRadians);

                moved.MoveBy(MyTransformation.Difference(to, from));

                Assert.AreEqual(reread.Start.X, moved.Start.X, Tolerance, "X");
                Assert.AreEqual(reread.Start.Y, moved.Start.Y, Tolerance, "Y");
                Assert.AreEqual(MyTransformation.NormalizeDegrees(reread.Rotaion),
                                MyTransformation.NormalizeDegrees(moved.Rotaion),
                                Tolerance, "the section faces the same way");
            }
            finally
            {
                DataContainer.ModelTransformation = before;
            }
        }

        [TestMethod]
        public void AStoryPairedWithALevelPutsTheLevelOnTheStory()
        {
            // ETABS story at 9.000 m; the Revit level at 9.150 m, with the slab 150 mm below it.
            double dz = MyTransformation.HeightFromStoryAndLevel(9000, 9150, -150);
            Assert.AreEqual(0, dz, Tolerance, "the slab under the level is already at the story");

            // A Revit model modelled from nought against an ETABS model standing on its base.
            dz = MyTransformation.HeightFromStoryAndLevel(12000, 9000, 0);
            Assert.AreEqual(3000, dz, Tolerance);

            MyTransformation placed = Alignment(0, 0, 0, 0, false).WithHeight(dz, null);
            MyPoint onTheLevel = placed.ApplyToPointInMM(new MyPoint(0, 0, 9000));
            Assert.AreEqual(12000, onTheLevel.Z, Tolerance, "the level lands on the story");
        }

        [TestMethod]
        public void SettingTheHeightLeavesThePlanAlone()
        {
            MyTransformation before = Alignment(1200, -800, 250, 37, true);
            before.IsMirroredAboutY = true;
            MyTransformation after = before.WithHeight(-4500, "story L3 is level Level 3");

            Assert.AreEqual(-4500, after.Translation.Z, Tolerance);
            Assert.AreEqual(before.Rotation, after.Rotation, Tolerance);
            Assert.IsTrue(after.IsMirroredAboutX);
            Assert.IsTrue(after.IsMirroredAboutY);
            StringAssert.Contains(after.Diagnostics, "story L3 is level Level 3");

            MyPoint point = new MyPoint(12345, -6789, 3000);
            MyPoint a = before.ApplyToPointInMM(point);
            MyPoint b = after.ApplyToPointInMM(point);
            Assert.AreEqual(a.X, b.X, Tolerance);
            Assert.AreEqual(a.Y, b.Y, Tolerance);
            Assert.AreEqual(a.Z - 250 - 4500, b.Z, Tolerance);

            // And the model already placed moves only up or down to get there.
            AssertMovingMatchesRereading(before, after);
        }

        /// <summary>
        /// Moving a placed point by the difference has to give the same answer as placing the
        /// raw point under the new alignment, wherever the point is.
        /// </summary>
        private static void AssertMovingMatchesRereading(MyTransformation from, MyTransformation to)
        {
            MyTransformation move = MyTransformation.Difference(to, from);

            MyPoint[] raw =
            {
                new MyPoint(0, 0, 0),
                new MyPoint(12345, -6789, 3000),
                new MyPoint(-50000, 25000, -1500),
                new MyPoint(1, 1, 1)
            };

            foreach (MyPoint point in raw)
            {
                MyPoint moved = move.ApplyToPointInMM(from.ApplyToPointInMM(point));
                MyPoint reread = to.ApplyToPointInMM(point);

                Assert.AreEqual(reread.X, moved.X, Tolerance, "X of " + point);
                Assert.AreEqual(reread.Y, moved.Y, Tolerance, "Y of " + point);
                Assert.AreEqual(reread.Z, moved.Z, Tolerance, "Z of " + point);
            }
        }

        private static MyTransformation Alignment(double dx, double dy, double dz,
                                                  double rotationInDegrees, bool mirrored)
        {
            MyTransformation alignment = new MyTransformation();
            alignment.SetAlignment(new MyPoint(dx, dy, dz), rotationInDegrees, mirrored);
            return alignment;
        }
    }
}
