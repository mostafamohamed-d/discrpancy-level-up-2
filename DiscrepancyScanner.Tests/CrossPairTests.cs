using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A column in one model paired with a wall in the other.
    ///
    /// The two models are allowed to disagree about what a short length of wall is - a blade
    /// column in ETABS can be a stub of wall in Revit and the other way round - and where they
    /// do, the element is the same element and should not read as missing from both.
    /// </summary>
    [TestClass]
    public class CrossPairTests
    {
        private const string Story = "L1";
        private const double FeetToMM = 304.8d;

        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.WallLocationTolerance = 100;
            pref.WallThicknessTolerance = 5;
            pref.WallSearchRadius = 1000;
            pref.WallAngleTolerance = 15;
            pref.WallMinimumOverlap = 300;
            pref.WallLengthTolerance = 500;
            pref.SwapRevitColumnWidthDepth = false;
            pref.RunColumnLocationCheck = true;
            pref.RunColumnDimensionsCheck = true;
            pref.RunWallLocationCheck = true;
            pref.RunWallThicknessCheck = true;
            pref.RunWallLengthCheck = true;
        }

        [TestMethod]
        public void AnEtabsColumnStandingOnARevitWallIsPairedWithIt()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 0, 600, 0, 250));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count);
            CrossPair pair = engine.Pairs[0];
            Assert.IsTrue(pair.EtabsIsColumn);
            Assert.AreEqual(10L, pair.RevitID);
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.Location));
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.Dimensions));

            Assert.AreEqual(0, lists.EtabsColumns.Count, "the column is no longer unpaired");
            Assert.AreEqual(0, lists.RevitWalls.Count, "nor is the wall");
        }

        [TestMethod]
        public void AnEtabsWallWithARevitColumnOnItIsPairedWithIt()
        {
            Lists lists = new Lists();
            // Running up Y, so the column has to be turned to lie along it.
            lists.EtabsWalls.Add(EtabsWall("W1", 0, -600, 0, 600, 250));
            lists.RevitColumns.Add(RevitColumn(20, 0, 0, 1200, 250, 90));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count);
            CrossPair pair = engine.Pairs[0];
            Assert.IsFalse(pair.EtabsIsColumn);
            Assert.AreEqual(20L, pair.RevitID);
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.WallLocation));
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.WallThickness));
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.WallLength));
            Assert.AreEqual(0, lists.EtabsWalls.Count);
            Assert.AreEqual(0, lists.RevitColumns.Count);
        }

        [TestMethod]
        public void ADifferentThicknessIsPairedAndReportedRatherThanLeftUnpaired()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 0, 600, 0, 300));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count);
            Assert.AreEqual(ResultLevel.Failed, LevelOf(engine.Pairs[0], CheckKind.Dimensions));
        }

        [TestMethod]
        public void ARevitWallDrawnInTwoPiecesIsOnePairWithTheWholeLength()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 0, 0, 0, 250));
            lists.RevitWalls.Add(RevitWall(11, 0, 0, 600, 0, 250));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count, "one ETABS column, one element on the plan");
            CollectionAssert.AreEquivalent(new[] { 10L, 11L },
                engine.Pairs[0].RevitWalls.Select(wall => wall.RevitID).ToList());
            Assert.AreEqual(ResultLevel.Passed, LevelOf(engine.Pairs[0], CheckKind.Dimensions),
                "the two halves together are as long as the column");
            Assert.AreEqual(0, lists.RevitWalls.Count, "neither half is left unpaired");
        }

        [TestMethod]
        public void StackedEtabsPanelsAllStandOnTheOneRevitColumn()
        {
            Lists lists = new Lists();
            lists.EtabsWalls.Add(EtabsWall("W1", -600, 0, 600, 0, 250));
            lists.EtabsWalls.Add(EtabsWall("W2", -600, 0, 600, 0, 250));
            lists.RevitColumns.Add(RevitColumn(20, 0, 0, 1200, 250, 0));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(2, engine.Pairs.Count);
            Assert.IsTrue(engine.Pairs.All(pair => pair.RevitID == 20));
            Assert.AreEqual(0, lists.EtabsWalls.Count);
        }

        [TestMethod]
        public void EtabsPanelsSideBySideAlongARevitColumnAreAddedUp()
        {
            Lists lists = new Lists();
            lists.EtabsWalls.Add(EtabsWall("W1", -600, 0, 0, 0, 250));
            lists.EtabsWalls.Add(EtabsWall("W2", 0, 0, 600, 0, 250));
            lists.RevitColumns.Add(RevitColumn(20, 0, 0, 1200, 250, 0));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(2, engine.Pairs.Count);
            foreach (CrossPair pair in engine.Pairs)
            {
                Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.WallLength),
                    "the two panels together run the length of the column");
                Assert.AreEqual(0, pair.LengthDelta, 1);
            }
        }

        [TestMethod]
        public void OnePanelAlongHalfARevitColumnIsShort()
        {
            Lists lists = new Lists();
            lists.EtabsWalls.Add(EtabsWall("W1", -600, 0, 0, 0, 250));
            lists.RevitColumns.Add(RevitColumn(20, 0, 0, 1200, 250, 0));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count);
            Assert.AreEqual(ResultLevel.Review, LevelOf(engine.Pairs[0], CheckKind.WallLength));
            Assert.AreEqual(600, engine.Pairs[0].LengthDelta, 1);
        }

        [TestMethod]
        public void EtabsColumnsSideBySideAlongARevitWallAreAddedUp()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", -600, 0, 1200, 250));
            lists.EtabsColumns.Add(EtabsColumn("C2", 600, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -1200, 0, 1200, 0, 250));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(2, engine.Pairs.Count);
            foreach (CrossPair pair in engine.Pairs)
            {
                Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, CheckKind.Dimensions),
                    "the two columns together run the length of the wall");
            }
        }

        [TestMethod]
        public void AnEtabsColumnRunningPastTheRevitWallIsStillTooLong()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1800, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 0, 600, 0, 250));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(1, engine.Pairs.Count);
            Assert.AreEqual(ResultLevel.Failed, LevelOf(engine.Pairs[0], CheckKind.Dimensions));
            Assert.AreEqual(600, engine.Pairs[0].LengthDelta, 1);
        }

        [TestMethod]
        public void NothingNearbyIsLeftUnpaired()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 3000, 600, 3000, 250));

            CrossPairEngine engine = Run(lists);

            Assert.AreEqual(0, engine.Pairs.Count);
            Assert.AreEqual(1, lists.EtabsColumns.Count);
            Assert.AreEqual(1, lists.RevitWalls.Count);
        }

        [TestMethod]
        public void AWallCrossingTheColumnIsNotTheSameElement()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, 0, -600, 0, 600, 250));

            Assert.AreEqual(0, Run(lists).Pairs.Count);
        }

        [TestMethod]
        public void AColumnSetByHandToPairWithNothingStaysThatWay()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(EtabsColumn("C1", 0, 0, 1200, 250));
            lists.RevitWalls.Add(RevitWall(10, -600, 0, 600, 0, 250));

            CrossPairEngine engine = new CrossPairEngine();
            engine.StartComparison(lists.EtabsColumns, lists.RevitColumns, lists.EtabsWalls,
                                   lists.RevitWalls, new HashSet<string> { "C1" });

            Assert.AreEqual(0, engine.Pairs.Count);
            Assert.AreEqual(1, lists.EtabsColumns.Count);
        }

        [TestMethod]
        public void ARoundColumnHasNoLineToBeAWall()
        {
            Lists lists = new Lists();
            lists.EtabsColumns.Add(RoundEtabsColumn("C1", 0, 0, 600));
            lists.RevitWalls.Add(RevitWall(10, -300, 0, 300, 0, 250));

            Assert.AreEqual(0, Run(lists).Pairs.Count);
        }

        [TestMethod]
        public void TheColumnAndWallChecksLeaveTheirLeftoversForIt()
        {
            List<Column_e> etabsColumns = new List<Column_e> { EtabsColumn("C1", 0, 0, 1200, 250) };
            List<Wall_R> revitWalls = new List<Wall_R> { RevitWall(10, -600, 0, 600, 0, 250) };

            ColumnCompareEngine columns = new ColumnCompareEngine();
            columns.StartComparison(etabsColumns, new List<Column_r>());
            WallCompareEngine walls = new WallCompareEngine();
            walls.StartComparison(new List<Wall_E>(), revitWalls);

            CrossPairEngine cross = new CrossPairEngine();
            cross.StartComparison(columns.UnmatchedEtabsColumns, columns.UnmatchedRevitColumns,
                                  walls.UnmatchedEtabsWalls, walls.UnmatchedRevitWalls);

            Assert.AreEqual(1, cross.Pairs.Count);
            Assert.AreEqual(0, columns.UnmatchedEtabsColumns.Count);
            Assert.AreEqual(0, walls.UnmatchedRevitWalls.Count);
        }

        #region building the inputs

        private sealed class Lists
        {
            public readonly List<Column_e> EtabsColumns = new List<Column_e>();
            public readonly List<Column_r> RevitColumns = new List<Column_r>();
            public readonly List<Wall_E> EtabsWalls = new List<Wall_E>();
            public readonly List<Wall_R> RevitWalls = new List<Wall_R>();
        }

        private static CrossPairEngine Run(Lists lists)
        {
            CrossPairEngine engine = new CrossPairEngine();
            engine.StartComparison(lists.EtabsColumns, lists.RevitColumns, lists.EtabsWalls, lists.RevitWalls);
            return engine;
        }

        private static ResultLevel LevelOf(CrossPair pair, CheckKind kind)
        {
            CheckOutcome outcome = pair.Outcomes.FirstOrDefault(o => o.Kind == kind);
            Assert.IsNotNull(outcome, $"the pair publishes a {kind} result");
            return outcome.Level;
        }

        /// <summary>An ETABS column, its depth along X before it is turned.</summary>
        private static Column_e EtabsColumn(string name, double xMM, double yMM, double depth, double width,
                                            double angleDegrees = 0)
        {
            FrameData_e frame = new FrameData_e(name)
            {
                Start = new MyPoint { X = xMM, Y = yMM, Z = 0 },
                End = new MyPoint { X = xMM, Y = yMM, Z = 3000 },
                Story = Story,
                Rotaion = angleDegrees
            };
            frame._width = width;
            frame._depth = depth;
            frame.Shape = SectionShape.Rectangular;
            return new Column_e(frame);
        }

        private static Column_e RoundEtabsColumn(string name, double xMM, double yMM, double diameter)
        {
            FrameData_e frame = new FrameData_e(name)
            {
                Start = new MyPoint { X = xMM, Y = yMM, Z = 0 },
                End = new MyPoint { X = xMM, Y = yMM, Z = 3000 },
                Story = Story
            };
            frame.setDiameter_e(diameter, Form1.eLength.mm);
            return new Column_e(frame);
        }

        /// <summary>A Revit column, its depth along X before it is turned, handed over in feet.</summary>
        private static Column_r RevitColumn(long id, double xMM, double yMM, double depth, double width,
                                            double angleDegrees)
        {
            Column_r column = new Column_r(id, new MyPoint { X = xMM / FeetToMM, Y = yMM / FeetToMM, Z = 0 },
                                           angleDegrees * Math.PI / 180d);
            column.SetDimensions(SectionShape.Rectangular, width / FeetToMM, depth / FeetToMM, 0);
            column.Story = Story;
            return column;
        }

        private static Wall_E EtabsWall(string name, double x1, double y1, double x2, double y2, double thickness)
        {
            Joint[] joints =
            {
                At(x1, y1, 0),
                At(x2, y2, 0),
                At(x2, y2, 3000),
                At(x1, y1, 3000)
            };
            return new Wall_E(name, joints, Story, thickness, "W" + thickness);
        }

        private static Joint At(double x, double y, double z)
        {
            return new Joint("p", new MyPoint(x, y, z), Form1.eLength.mm);
        }

        private static Wall_R RevitWall(long id, double x1, double y1, double x2, double y2, double thickness)
        {
            Wall_R wall = new Wall_R(id, new MyPoint(x1 / FeetToMM, y1 / FeetToMM), new MyPoint(x2 / FeetToMM, y2 / FeetToMM));
            wall.SetThickness(thickness);
            wall.Story = Story;
            wall.TypeName = "RC Wall " + thickness;
            return wall;
        }

        #endregion
    }
}
