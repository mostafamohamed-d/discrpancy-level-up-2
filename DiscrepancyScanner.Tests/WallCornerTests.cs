using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Reading a Revit wall that meets another one as running to the point their centre lines
    /// cross.
    ///
    /// The corner is where the two models are furthest apart for the least reason. Revit takes
    /// whatever the person drawing it did - one wall run the full width and the other butted
    /// into its face, both mitred to meet, one run through to the far face - and ETABS meshes
    /// the joint on the crossing every time. So these are about a difference the check should
    /// never have been reporting, and about the ones next to it that it should: a wall that
    /// stops a metre short of the one it should meet is not a corner drawn a second way, and
    /// has to come through untouched.
    /// </summary>
    [TestClass]
    public class WallCornerTests
    {
        private const string Story = "L1";
        private const double FeetToMM = 304.8d;

        /// <summary>
        /// The buffer these are written against, and an alignment that does nothing - a Revit
        /// wall is built through whatever alignment is in force, and these tests are about the
        /// line rather than about where the model sits.
        /// </summary>
        [TestInitialize]
        public void SetTolerances()
        {
            DataContainer.ModelTransformation = new MyTransformation();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.SnapWallsToCorners = true;
            pref.WallCornerBuffer = 50;
            pref.WallLocationTolerance = 100;
            pref.WallThicknessTolerance = 5;
            pref.WallSearchRadius = 1000;
            pref.WallAngleTolerance = 15;
            pref.WallMinimumOverlap = 300;
            pref.WallLengthTolerance = 500;
            pref.WallUncoveredDrawLength = 1000;
            pref.WallMinimumLength = 200;
            pref.RunWallLocationCheck = true;
            pref.RunWallThicknessCheck = true;
            pref.RunWallLengthCheck = true;
        }

        #region a wall drawn to a face

        /// <summary>
        /// A wall butted into the face of a 300 core stops 150 short of where ETABS meshes the
        /// joint. It is read as running the whole way to it.
        /// </summary>
        [TestMethod]
        public void AWallStoppingAtAFaceIsReadAsRunningToTheCrossing()
        {
            Wall_R butted = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R core = RevitWall(2, 5000, -3000, 5000, 3000, 300);

            Assert.AreEqual(1, Snap(butted, core));

            Assert.AreEqual(5000, butted.PlanEnd.X, 0.01, "the end runs on to the crossing");
            Assert.AreEqual(5000, butted.Length, 0.01);
        }

        /// <summary>
        /// And a wall drawn straight through to the far face of that core is 150 too long. The
        /// same crossing, reached from the other side, so it is cut back to it.
        /// </summary>
        [TestMethod]
        public void AWallRunThroughToTheFarFaceIsCutBackToTheCrossing()
        {
            Wall_R through = RevitWall(1, 0, 0, 5150, 0, 200);
            Wall_R core = RevitWall(2, 5000, -3000, 5000, 3000, 300);

            Assert.AreEqual(1, Snap(through, core));

            Assert.AreEqual(5000, through.PlanEnd.X, 0.01, "the end is cut back to the crossing");
            Assert.AreEqual(5000, through.Length, 0.01);
        }

        /// <summary>
        /// A corner drawn twice over: each wall stopped at the other one's face, so the point
        /// the two centre lines cross is off the end of both of them. Both are still read as
        /// reaching it - that is the corner ETABS has.
        /// </summary>
        [TestMethod]
        public void BothWallsReachACornerNeitherOfThemWasDrawnTo()
        {
            Wall_R along = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R up = RevitWall(2, 5000, 100, 5000, 6000, 300);

            Assert.AreEqual(2, Snap(along, up));

            Assert.AreEqual(5000, along.PlanEnd.X, 0.01);
            Assert.AreEqual(0, up.PlanStart.Y, 0.01);
        }

        /// <summary>
        /// A wall between two cores, drawn face to face, is read as running centre to centre -
        /// which is half of each core longer, and the whole point of doing this.
        /// </summary>
        [TestMethod]
        public void AWallBetweenTwoCoresGrowsByHalfOfEachOfThem()
        {
            Wall_R between = RevitWall(1, 200, 0, 5800, 0, 200);
            Wall_R left = RevitWall(2, 0, -3000, 0, 3000, 400);
            Wall_R right = RevitWall(3, 6000, -3000, 6000, 3000, 400);

            Assert.AreEqual(1, Snap(between, left, right), "only the wall between them moves");

            Assert.AreEqual(0, between.PlanStart.X, 0.01);
            Assert.AreEqual(6000, between.PlanEnd.X, 0.01);
            Assert.AreEqual(6000, between.Length, 0.01);
        }

        /// <summary>
        /// The outline the plan is drawn from follows the line, so a wall read to the corner
        /// is drawn to the corner rather than left short of it.
        /// </summary>
        [TestMethod]
        public void ThePlanOutlineFollowsTheWallToItsCorner()
        {
            Wall_R butted = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R core = RevitWall(2, 5000, -3000, 5000, 3000, 300);

            Snap(butted, core);

            Assert.AreEqual(5000, butted.PlanOutline.Max(corner => corner.X), 0.01);
        }

        #endregion

        #region what is left alone

        /// <summary>
        /// The one that has to come through untouched. A wall stopping a metre short of the
        /// one it should meet is a hole in the model, not a corner drawn a second way, and it
        /// is exactly what the check exists to report.
        /// </summary>
        [TestMethod]
        public void AWallStoppingWellShortOfTheOtherIsLeftWhereItWasDrawn()
        {
            Wall_R short_ = RevitWall(1, 0, 0, 4000, 0, 200);
            Wall_R core = RevitWall(2, 5000, -3000, 5000, 3000, 300);

            Assert.AreEqual(0, Snap(short_, core));

            Assert.AreEqual(4000, short_.Length, 0.01);
        }

        /// <summary>
        /// Half the other wall's thickness and no further. A 300 core accounts for 150, the
        /// buffer for another 50, and a wall 250 short of the crossing is over the line.
        /// </summary>
        [TestMethod]
        public void TheReachStopsAtHalfTheOtherWallPlusTheBuffer()
        {
            Wall_R justInside = RevitWall(1, 0, 0, 4801, 0, 200);
            Wall_R justOutside = RevitWall(2, 0, 1000, 4799, 1000, 200);
            Wall_R core = RevitWall(3, 5000, -3000, 5000, 3000, 300);

            Assert.AreEqual(1, Snap(justInside, justOutside, core));

            Assert.AreEqual(5000, justInside.PlanEnd.X, 0.01, "199 short, inside the reach");
            Assert.AreEqual(4799, justOutside.PlanEnd.X, 0.01, "201 short, outside it");
        }

        /// <summary>
        /// A wall crossed near its middle has no corner at either end, and neither end is
        /// pulled in on it. The T junction is the case: the crossing is one end of the wall
        /// standing on it, and nothing at all to the wall it stands on.
        /// </summary>
        [TestMethod]
        public void AWallCrossedInTheMiddleIsNotPulledInFromEitherEnd()
        {
            Wall_R crossed = RevitWall(1, 0, 0, 10000, 0, 200);
            Wall_R standingOn = RevitWall(2, 5000, 0, 5000, 6000, 300);

            Assert.AreEqual(0, Snap(crossed, standingOn), "neither of them was drawn anywhere else");

            Assert.AreEqual(10000, crossed.Length, 0.01);
            Assert.AreEqual(6000, standingOn.Length, 0.01);
        }

        /// <summary>
        /// Two walls in a line are one wall run split where somebody stopped drawing, not a
        /// corner, and where their lines cross says nothing about either of them.
        /// </summary>
        [TestMethod]
        public void TwoWallsInALineAreNotACorner()
        {
            Wall_R first = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R second = RevitWall(2, 5200, 0, 10000, 0, 200);

            Assert.AreEqual(0, Snap(first, second));

            Assert.AreEqual(4850, first.Length, 0.01);
            Assert.AreEqual(4800, second.Length, 0.01);
        }

        /// <summary>
        /// A wall already drawn to the crossing is not moved, and not counted either: the
        /// import reports how many walls this changed, and every wall in a model drawn the way
        /// ETABS wants it would otherwise be reported as changed.
        /// </summary>
        [TestMethod]
        public void AWallAlreadyDrawnToTheCornerIsNotCounted()
        {
            Wall_R along = RevitWall(1, 0, 0, 5000, 0, 200);
            Wall_R up = RevitWall(2, 5000, 0, 5000, 6000, 300);

            Assert.AreEqual(0, Snap(along, up));
        }

        /// <summary>
        /// Nothing moves twice. Reading a model that has already been read has to land the
        /// walls in the same place, or a second import would take another half thickness off
        /// every corner in the building.
        /// </summary>
        [TestMethod]
        public void SnappingASecondTimeChangesNothing()
        {
            Wall_R between = RevitWall(1, 200, 0, 5800, 0, 200);
            Wall_R left = RevitWall(2, 0, -3000, 0, 3000, 400);
            Wall_R right = RevitWall(3, 6000, -3000, 6000, 3000, 400);

            Snap(between, left, right);
            Assert.AreEqual(0, Snap(between, left, right), "the second pass has nothing to do");

            Assert.AreEqual(6000, between.Length, 0.01);
        }

        /// <summary>
        /// Every end is decided against the lines as they were drawn, so which wall the import
        /// happened to read first cannot change the answer.
        /// </summary>
        [TestMethod]
        public void TheOrderTheWallsWereReadInDoesNotMatter()
        {
            Wall_R along = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R up = RevitWall(2, 5000, 100, 5000, 6000, 300);

            Wall_R alongAgain = RevitWall(1, 0, 0, 4850, 0, 200);
            Wall_R upAgain = RevitWall(2, 5000, 100, 5000, 6000, 300);

            Snap(along, up);
            Snap(upAgain, alongAgain);

            Assert.AreEqual(along.PlanEnd.X, alongAgain.PlanEnd.X, 0.01);
            Assert.AreEqual(up.PlanStart.Y, upAgain.PlanStart.Y, 0.01);
        }

        #endregion

        #region what it does to the check

        /// <summary>
        /// The whole reason for it. A wall run through two 600 cores is 600 longer than the
        /// ETABS panel on it, which is over the length buffer, so the wall is reported as one
        /// ETABS stops short of - and nobody has stopped short of anything. Read to the
        /// crossings, the two models agree.
        /// </summary>
        [TestMethod]
        public void AWallDrawnThroughItsCoresNoLongerReadsAsOneEtabsStopsShortOf()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("panel", 0, 0, 6000, 0, 200) };

            Wall_R drawn = RevitWall(1, -300, 0, 6300, 0, 200);
            Wall_R left = RevitWall(2, 0, -3000, 0, 3000, 600);
            Wall_R right = RevitWall(3, 6000, -3000, 6000, 3000, 600);

            WallCompareEngine asDrawn = new WallCompareEngine();
            asDrawn.StartComparison(etabs, new List<Wall_R> { drawn, left, right });

            PairedWall before = asDrawn.PairedWalls.First(pair => pair.revitWall == drawn);
            Assert.IsFalse(before.LengthMatched, "as drawn the wall reads as 600 longer than ETABS");
            Assert.AreEqual(600, before.LengthShortfall, 1);

            Snap(drawn, left, right);

            WallCompareEngine toTheCorners = new WallCompareEngine();
            toTheCorners.StartComparison(etabs, new List<Wall_R> { drawn, left, right });

            PairedWall after = toTheCorners.PairedWalls.First(pair => pair.revitWall == drawn);
            Assert.IsTrue(after.LengthMatched, "read to the crossings the two models agree");
            Assert.AreEqual(0, after.LengthShortfall, 1);
        }

        /// <summary>
        /// The other half of it. Reading the corners must not quieten a wall ETABS really does
        /// stop short of: the corner is worth half a core, and three metres of missing wall is
        /// still three metres of missing wall afterwards - warned about, and its missing
        /// stretch still drawn on the plan.
        /// </summary>
        [TestMethod]
        public void AWallEtabsReallyDoesStopShortOfIsStillReportedAfterwards()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("panel", 0, 0, 3000, 0, 200) };

            Wall_R drawn = RevitWall(1, -300, 0, 6000, 0, 200);
            Wall_R core = RevitWall(2, 0, -3000, 0, 3000, 600);

            Assert.AreEqual(1, Snap(drawn, core));
            Assert.AreEqual(6000, drawn.Length, 0.01, "the corner is worth 300 of it");

            WallCompareEngine engine = new WallCompareEngine();
            engine.StartComparison(etabs, new List<Wall_R> { drawn, core });

            PairedWall pair = engine.PairedWalls.First(one => one.revitWall == drawn);
            Assert.IsFalse(pair.LengthMatched);
            Assert.AreEqual(3000, pair.LengthShortfall, 1);

            Assert.IsTrue(engine.UncoveredRevitWalls.Any(piece => piece.Wall == drawn),
                          "the stretch nothing stands along is still drawn");
        }

        #endregion

        #region building the walls

        private static int Snap(params Wall_R[] walls)
        {
            return WallCorners.SnapToJunctions(walls,
                                               PrefrencesData.getPrefrencesData().WallCornerBuffer);
        }

        /// <summary>
        /// A Revit wall on the given line. The coordinates are in mm like everything else in
        /// these tests and converted on the way in, because the container takes what Revit
        /// hands over, which is feet.
        /// </summary>
        private static Wall_R RevitWall(long id, double x1, double y1, double x2, double y2,
                                        double thickness, string story = Story)
        {
            Wall_R wall = new Wall_R(id, InFeet(x1, y1), InFeet(x2, y2));
            wall.SetThickness(thickness);
            wall.Story = story;
            wall.TypeName = "RC Wall " + thickness;
            return wall;
        }

        private static Wall_E EtabsWall(string name, double x1, double y1, double x2, double y2,
                                        double thickness, string story = Story)
        {
            Joint[] joints =
            {
                At(x1, y1, 0),
                At(x2, y2, 0),
                At(x2, y2, 3000),
                At(x1, y1, 3000)
            };

            return new Wall_E(name, joints, story, thickness, "W" + thickness);
        }

        private static Joint At(double x, double y, double z)
        {
            return new Joint("p", new MyPoint(x, y, z), Form1.eLength.mm);
        }

        private static MyPoint InFeet(double xInMM, double yInMM)
        {
            return new MyPoint(xInMM / FeetToMM, yInMM / FeetToMM);
        }

        #endregion
    }
}
