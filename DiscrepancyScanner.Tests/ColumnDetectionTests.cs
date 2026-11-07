using DiscrepancyScanner;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Which frames the ETABS import hands to the column check and which it hands to the beam
    /// check. Every frame goes to one side or the other, so getting this wrong costs twice
    /// over: the frame is checked as the wrong kind of thing, and it is missing from the check
    /// it belonged in. A beam read as a column is reported as a column Revit has not got and
    /// as a beam ETABS has not got, both at once, and neither line says why.
    ///
    /// The rule used to be the rise alone - ends more than 50mm apart in level - which called
    /// every inclined beam in the model a column. What is pinned here is the angle that
    /// replaced it, at the cases the two kinds of member actually sit at.
    /// </summary>
    [TestClass]
    public class ColumnDetectionTests
    {
        private const double Default = ModelIndex.DefaultColumnLeanFromVertical;

        /// <summary>A frame from one end to the other, judged at the shipped lean.</summary>
        private static bool IsColumn(double x1, double y1, double z1,
                                     double x2, double y2, double z2)
        {
            return ModelIndex.IsColumn(x1, y1, z1, x2, y2, z2, Default);
        }

        // ------------------------------------------------------------- what is a column

        [TestMethod]
        public void APlumbColumnIsAColumn()
        {
            Assert.IsTrue(IsColumn(0, 0, 0, 0, 0, 3000));
        }

        [TestMethod]
        public void APlumbColumnIsAColumnDrawnDownwards()
        {
            // ETABS names the ends in whatever order the frame was drawn in, so the test has
            // to reach the same verdict either way round.
            Assert.IsTrue(IsColumn(0, 0, 3000, 0, 0, 0));
        }

        [TestMethod]
        public void AShortStubColumnIsStillAColumn()
        {
            Assert.IsTrue(IsColumn(0, 0, 0, 0, 0, 300));
        }

        [TestMethod]
        public void ARakingColumnLeaningTenDegreesIsAColumn()
        {
            // 3000 up and 529 across is ten degrees off plumb.
            Assert.IsTrue(IsColumn(0, 0, 0, 529, 0, 3000));
        }

        [TestMethod]
        public void ARakingColumnLeaningTwentyDegreesIsAColumn()
        {
            // 3000 up and 1092 across is twenty degrees off plumb.
            Assert.IsTrue(IsColumn(0, 0, 0, 1092, 0, 3000));
        }

        [TestMethod]
        public void TheLeanIsMeasuredInPlanAndNotAlongOneAxis()
        {
            // Twenty degrees of lean taken on the diagonal: 772 on each of X and Y is 1092 in
            // plan.
            Assert.IsTrue(IsColumn(0, 0, 0, 772, 772, 3000));

            // 1543 on each axis reads as twenty seven degrees if X and Y are looked at one at
            // a time, and would pass. Taken together they are thirty six, and out.
            Assert.IsFalse(IsColumn(0, 0, 0, 1543, 1543, 3000));
        }

        // --------------------------------------------------------------- what is a beam

        [TestMethod]
        public void ALevelBeamIsABeam()
        {
            Assert.IsFalse(IsColumn(0, 0, 0, 6000, 0, 0));
        }

        [TestMethod]
        public void ABeamFallingToADrainIsABeam()
        {
            // Six metres long with sixty millimetres of fall. Under the old rise rule this was
            // a column; it is half a degree off level.
            Assert.IsFalse(IsColumn(0, 0, 0, 6000, 0, 60));
        }

        [TestMethod]
        public void ARampBeamIsABeam()
        {
            // A car ramp at ten per cent: 6000 along, 600 up.
            Assert.IsFalse(IsColumn(0, 0, 0, 6000, 0, 600));
        }

        [TestMethod]
        public void AStairFlightBeamIsABeam()
        {
            // Thirty degrees off level, which is sixty off plumb: 3000 along, 1732 up.
            Assert.IsFalse(IsColumn(0, 0, 0, 3000, 0, 1732));
        }

        [TestMethod]
        public void ASteepRoofBeamIsABeam()
        {
            // Forty five degrees, about the steepest a beam is drawn at.
            Assert.IsFalse(IsColumn(0, 0, 0, 3000, 0, 3000));
        }

        [TestMethod]
        public void ABeamSteppedDownAFewMillimetresIsABeam()
        {
            Assert.IsFalse(IsColumn(0, 0, 0, 4000, 0, 25));
        }

        // -------------------------------------------------------------------- the edges

        [TestMethod]
        public void AFrameWithNoLengthAtAllIsNotAColumn()
        {
            // Both ends on one point leaves no direction to measure, and an arc tangent of
            // nothing over nothing answers nought rather than refusing, which would have read
            // as perfectly plumb. The rise floor catches it before any angle is taken.
            Assert.IsFalse(IsColumn(0, 0, 0, 0, 0, 0));
        }

        [TestMethod]
        public void APlumbFrameShorterThanTheRiseFloorIsNotAColumn()
        {
            Assert.IsFalse(IsColumn(0, 0, 0, 0, 0, ModelIndex.ColumnRiseFloor));
            Assert.IsTrue(IsColumn(0, 0, 0, 0, 0, ModelIndex.ColumnRiseFloor + 1));
        }

        [TestMethod]
        public void TheLeanIsWhereTheAnswerTurnsOver()
        {
            // Thirty degrees off plumb over a 3000 rise is 1732 across. Just inside it is a
            // column and just outside it is a beam, so the preference means what it says.
            Assert.IsTrue(IsColumn(0, 0, 0, 1700, 0, 3000));
            Assert.IsFalse(IsColumn(0, 0, 0, 1760, 0, 3000));
        }

        // --------------------------------------------------------------- the preference

        [TestMethod]
        public void RaisingTheLeanTakesInASteeperFrame()
        {
            // Forty degrees off plumb: out at the shipped lean, in once it is widened.
            Assert.IsFalse(ModelIndex.IsColumn(0, 0, 0, 2517, 0, 3000, Default));
            Assert.IsTrue(ModelIndex.IsColumn(0, 0, 0, 2517, 0, 3000, 45));
        }

        [TestMethod]
        public void LoweringTheLeanToNoughtLeavesOnlyPlumbFrames()
        {
            Assert.IsTrue(ModelIndex.IsColumn(0, 0, 0, 0, 0, 3000, 0));
            Assert.IsFalse(ModelIndex.IsColumn(0, 0, 0, 10, 0, 3000, 0));
        }

        [TestMethod]
        public void ANonsenseLeanCannotTurnTheWholeModelIntoColumns()
        {
            // Tangent runs away near ninety. A settings file edited by hand can hold anything,
            // and ninety degrees of lean would call every beam in the model a column, which is
            // the one mistake this test must not be able to make.
            Assert.IsFalse(ModelIndex.IsColumn(0, 0, 0, 6000, 0, 600, 90));
            Assert.IsFalse(ModelIndex.IsColumn(0, 0, 0, 6000, 0, 600, 1e9));
            Assert.IsFalse(ModelIndex.IsColumn(0, 0, 0, 6000, 0, 600, double.NaN));
        }

        [TestMethod]
        public void ALeanOutsideTheRangeIsBroughtBackIntoIt()
        {
            Assert.AreEqual(0d, ModelIndex.ColumnLeanInRange(-10), 1e-9);
            Assert.AreEqual(ModelIndex.MaximumColumnLeanFromVertical,
                            ModelIndex.ColumnLeanInRange(1000), 1e-9);
            Assert.AreEqual(30d, ModelIndex.ColumnLeanInRange(30), 1e-9);
        }

        [TestMethod]
        public void TheShippedLeanSitsBetweenARakingColumnAndAStairBeam()
        {
            // The number itself is not worth arguing over, but it does have to sit in the gap:
            // wider than any column rakes, narrower than any beam climbs.
            Assert.IsTrue(Default > 20);
            Assert.IsTrue(Default < 60);
            Assert.IsTrue(Default <= ModelIndex.MaximumColumnLeanFromVertical);
        }
    }
}
