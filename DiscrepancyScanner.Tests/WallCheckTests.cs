using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// What the wall check decides, and why.
    ///
    /// Two things are tested here. The plan geometry a wall is reduced to - the line through a
    /// shell drawn as a panel, how far one wall stands off another's line, how much of the two
    /// run alongside - and the pairing that geometry feeds, which is the part that has to cope
    /// with the two models splitting a wall run in different places.
    ///
    /// None of it needs a model open: an ETABS wall is its corner joints, and a Revit wall is
    /// its location line, so both can be built here.
    /// </summary>
    [TestClass]
    public class WallCheckTests
    {
        private const string Story = "L1";
        private const double FeetToMM = 304.8d;

        /// <summary>
        /// The buffers the tests are written against. The preferences are a singleton the whole
        /// tool shares, so each test states the ones it depends on rather than trusting whatever
        /// ran before it to have left them alone.
        /// </summary>
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.WallLocationTolerance = 100;
            pref.WallThicknessTolerance = 5;
            pref.WallSearchRadius = 1000;
            pref.WallAngleTolerance = 15;
            pref.WallMinimumOverlap = 300;
            pref.WallMinimumLength = 200;
            pref.WallLengthTolerance = 500;
            pref.WallUncoveredDrawLength = 1000;

            // The per-check switches are on the same shared preferences, so they are put back
            // here too - a test that turns one off must not decide what the next one sees.
            pref.RunWallLocationCheck = true;
            pref.RunWallThicknessCheck = true;
        }

        #region the geometry a wall is reduced to

        /// <summary>
        /// The centre line is the two corners furthest apart in plan, which is what makes the
        /// reading independent of how the panel was drawn. This one is stepped at the top and
        /// its corners are in no useful order, and it still reads as the line it stands on.
        /// </summary>
        [TestMethod]
        public void CentreLine_IsTheTwoCornersFurthestApart()
        {
            Wall_E wall = new Wall_E("w1", new[]
            {
                At(5000, 0, 3000),
                At(0, 0, 0),
                At(2500, 0, 4000),
                At(5000, 0, 0),
                At(0, 0, 3000)
            }, Story, 200);

            Assert.IsTrue(wall.HasCentreLine, "a panel with two distinct corners has a line");
            Assert.AreEqual(5000, wall.Length, 0.001);
            Assert.AreEqual(4000, wall.TopElevation, 0.001, "the highest corner is the one it rises to");
            Assert.AreEqual(0, wall.BaseElevation, 0.001);
        }

        /// <summary>
        /// A panel whose corners are all at one place in plan - which is what a wall modelled
        /// by mistake as a zero length shell looks like - has no line, and says so rather than
        /// reading as a wall at the origin.
        /// </summary>
        [TestMethod]
        public void CentreLine_IsAbsentWhenEveryCornerIsAtOnePlace()
        {
            Wall_E wall = new Wall_E("w1", new[] { At(1000, 1000, 0), At(1000, 1000, 3000) }, Story, 200);

            Assert.IsFalse(wall.HasCentreLine);
            Assert.AreEqual(0, wall.Length, 0.001);
        }

        /// <summary>
        /// The offset is measured at both ends and reported as the worse of the two, so a wall
        /// that meets the other in the middle and splays away does not read as a match.
        /// </summary>
        [TestMethod]
        public void Offset_IsTheWorseOfTheTwoEnds()
        {
            Wall_E etabs = EtabsWall("w1", 0, 0, 5000, 0, 200);
            Wall_R revit = RevitWall(1, 0, 50, 5000, 150, 200);

            Assert.AreEqual(150, WallGeometry.Offset(etabs, revit), 1);
        }

        [TestMethod]
        public void Overlap_IsNoughtWhenTwoWallsOnlyPassEachOtherEndToEnd()
        {
            Wall_E etabs = EtabsWall("w1", 0, 0, 5000, 0, 200);
            Wall_R apart = RevitWall(1, 6000, 0, 9000, 0, 200);
            Wall_R sharing = RevitWall(2, 4000, 0, 9000, 0, 200);

            Assert.AreEqual(0, WallGeometry.OverlapLength(etabs, apart), 0.001);
            Assert.AreEqual(1000, WallGeometry.OverlapLength(etabs, sharing), 0.001);
        }

        /// <summary>A wall drawn from the other end is the same wall, not one turned round.</summary>
        [TestMethod]
        public void Angle_FoldsAWallDrawnTheOtherWayRoundToNoDifference()
        {
            Wall_E etabs = EtabsWall("w1", 0, 0, 5000, 0, 200);
            Wall_R revit = RevitWall(1, 5000, 0, 0, 0, 200);

            Assert.AreEqual(0, WallGeometry.AngleBetween(etabs, revit), 0.001);
        }

        #endregion

        #region pairing

        /// <summary>
        /// The case the claim based pairing was written for. ETABS meshes one wall run into
        /// three panels; Revit has it as one wall. Each panel is checked against that wall, and
        /// nothing is reported as missing from either model.
        /// </summary>
        [TestMethod]
        public void SeveralEtabsPanelsMayStandAlongOneRevitWall()
        {
            List<Wall_E> etabs = new List<Wall_E>
            {
                EtabsWall("w1", 0, 0, 3000, 0, 200),
                EtabsWall("w2", 3000, 0, 6000, 0, 200),
                EtabsWall("w3", 6000, 0, 9000, 0, 200)
            };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 0, 9000, 0, 200) };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(3, engine.PairedWalls.Count);
            Assert.AreEqual(0, engine.UnmatchedEtabsWalls.Count);
            Assert.AreEqual(0, engine.UnmatchedRevitWalls.Count);
            Assert.IsTrue(engine.PairedWalls.All(wall => wall.State == WallCheckState.Matched),
                          "panels lying on the wall at the same thickness all match");
        }

        /// <summary>
        /// And the other way round: Revit has the run in two walls where ETABS has one panel.
        /// The panel pairs with one of them, and the second pass gives the other a verdict
        /// rather than leaving it to read as a wall ETABS has not got.
        /// </summary>
        [TestMethod]
        public void ARevitWallNoPanelPickedIsStillCheckedAgainstThePanelItStandsOn()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 0, 0, 6000, 0, 200) };
            List<Wall_R> revit = new List<Wall_R>
            {
                RevitWall(1, 0, 0, 3000, 0, 200),
                RevitWall(2, 3000, 0, 6000, 0, 200)
            };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(2, engine.PairedWalls.Count, "both halves get a verdict");
            Assert.AreEqual(0, engine.UnmatchedRevitWalls.Count);
            Assert.AreEqual(0, engine.UnmatchedEtabsWalls.Count);
            CollectionAssert.AreEquivalent(new long[] { 1, 2 },
                engine.PairedWalls.Select(wall => wall.revitWall.RevitID).ToArray());
        }

        /// <summary>
        /// The wall on the other side of the corridor runs exactly parallel and is nothing to
        /// do with this one. Out of reach is out of reach: both are reported as unpaired rather
        /// than paired to each other and reported as three metres out of place.
        /// </summary>
        [TestMethod]
        public void AParallelWallOutOfReachIsNotAPartner()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 0, 0, 5000, 0, 200) };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 3000, 5000, 3000, 200) };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(0, engine.PairedWalls.Count);
            Assert.AreEqual(1, engine.UnmatchedEtabsWalls.Count);
            Assert.AreEqual(1, engine.UnmatchedRevitWalls.Count);
        }

        /// <summary>
        /// A wall in the wrong place, but near enough to be the same wall, is a location
        /// mismatch. Reporting it as unpaired would say the wall is missing when it is there.
        /// </summary>
        [TestMethod]
        public void AWallPastTheBufferButInsideTheSearchRadiusIsAMismatch()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 0, 0, 5000, 0, 200) };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 400, 5000, 400, 200) };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(1, engine.PairedWalls.Count);
            Assert.AreEqual(0, engine.UnmatchedEtabsWalls.Count);

            PairedWall pair = engine.PairedWalls[0];
            Assert.IsFalse(pair.LocationMatched);
            Assert.IsTrue(pair.ThicknessMatched);
            Assert.AreEqual(WallCheckState.Mismatched, pair.State);
            Assert.AreEqual(400, pair.LocationDelta, 1);
        }

        /// <summary>
        /// Two walls on different stories are two walls, however exactly one lies over the
        /// other - which, in a building, is what every floor above looks like.
        /// </summary>
        [TestMethod]
        public void WallsOnDifferentStoriesAreNotPaired()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 0, 0, 5000, 0, 200) };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 0, 5000, 0, 200, "L2") };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(0, engine.PairedWalls.Count);
            Assert.AreEqual(1, engine.UnmatchedEtabsWalls.Count);
            Assert.AreEqual(1, engine.UnmatchedRevitWalls.Count);
        }

        /// <summary>
        /// A short panel - the piece of wall over a door, say - is not held to the full overlap
        /// a long one is, or it could never be paired with anything.
        /// </summary>
        [TestMethod]
        public void AShortPanelIsNotHeldToAnOverlapItCannotHave()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 1000, 0, 1200, 0, 200) };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 0, 9000, 0, 200) };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(1, engine.PairedWalls.Count);
            Assert.IsTrue(engine.PairedWalls[0].LocationMatched);
            Assert.IsTrue(engine.PairedWalls[0].ThicknessMatched);

            // It is paired and both checks pass; what it is warned about is its length, since
            // 200mm of panel stands for 9m of Revit wall.
            Assert.IsFalse(engine.PairedWalls[0].LengthMatched);
        }

        /// <summary>
        /// A panel with no line in plan cannot be checked. It is counted and said so rather
        /// than being quietly dropped or, worse, compared as a wall at the origin.
        /// </summary>
        [TestMethod]
        public void APanelWithNoLineIsReportedRatherThanChecked()
        {
            List<Wall_E> etabs = new List<Wall_E>
            {
                new Wall_E("w1", new[] { At(1000, 1000, 0), At(1000, 1000, 3000) }, Story, 200)
            };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 0, 5000, 0, 200) };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(0, engine.PairedWalls.Count);
            Assert.AreEqual(0, engine.UnmatchedEtabsWalls.Count, "it never reached the pairing");
            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("no line in plan")),
                          "and it is named in the issues");
        }

        /// <summary>
        /// A story one model has walls on and the other has none at all is named, because every
        /// wall on it is about to be reported as unpaired and a tally of those says nothing
        /// about why. Two models naming the level differently is the usual reason.
        /// </summary>
        [TestMethod]
        public void AStoryWithWallsInOneModelOnlyIsNamed()
        {
            List<Wall_E> etabs = new List<Wall_E> { EtabsWall("w1", 0, 0, 5000, 0, 200, "L1") };
            List<Wall_R> revit = new List<Wall_R> { RevitWall(1, 0, 0, 5000, 0, 200, "LEVEL 1") };

            WallCompareEngine engine = Compare(etabs, revit);

            Assert.AreEqual(0, engine.PairedWalls.Count, "the two never meet on a story");
            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("L1")),
                          "the ETABS level nothing was found on is named");
            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("LEVEL 1")),
                          "and so is the Revit one");
        }

        #endregion

        #region how far ETABS runs against how far Revit runs

        /// <summary>
        /// A Revit wall the panels cover end to end has nothing to warn about, however many
        /// panels it took.
        /// </summary>
        [TestMethod]
        public void PanelsCoveringTheWholeWallRaiseNoWarning()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("w1", 0, 0, 3000, 0, 200),
                                   EtabsWall("w2", 3000, 0, 6000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(2, engine.PairedWalls.Count);
            Assert.IsTrue(engine.PairedWalls.All(wall => wall.LengthMatched));
            Assert.IsTrue(engine.PairedWalls.All(wall => wall.State == WallCheckState.Matched));
        }

        /// <summary>
        /// A wall with a stub of ETABS against one end is warned about rather than drawn twice.
        /// The plan shows the panel; the warning is what says the Revit wall runs on for
        /// another five and a half metres.
        /// </summary>
        [TestMethod]
        public void AWallEtabsStopsShortOfIsWarnedAbout()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("stub", 0, 0, 500, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(1, engine.PairedWalls.Count);

            PairedWall pair = engine.PairedWalls[0];
            Assert.IsFalse(pair.LengthMatched);
            Assert.AreEqual(500, pair.CoveredLength, 1);
            Assert.AreEqual(5500, pair.LengthShortfall, 1);
            Assert.AreEqual(WallCheckState.Review, pair.State);
        }

        /// <summary>The warning is a warning: amber, not a failure.</summary>
        [TestMethod]
        public void TheLengthWarningIsAReviewRatherThanAFailure()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("stub", 0, 0, 500, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            CheckOutcome length = engine.PairedWalls[0].Outcomes
                .First(outcome => outcome.Kind == CheckKind.WallLength);

            Assert.AreEqual(ResultLevel.Review, length.Level);
        }

        /// <summary>
        /// Panels stacked up the height of a wall cover the same stretch of it, so they count
        /// once. Added up instead, three half metre panels would read as a metre and a half
        /// covered and the wall would go unwarned.
        /// </summary>
        [TestMethod]
        public void StackedPanelsCoverTheirStretchOnceRatherThanOnceEach()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("lower", 0, 0, 500, 0, 200),
                                   EtabsWall("middle", 0, 0, 500, 0, 200),
                                   EtabsWall("upper", 0, 0, 500, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(3, engine.PairedWalls.Count);
            Assert.IsTrue(engine.PairedWalls.All(wall => wall.CoveredLength <= 501));
            Assert.IsTrue(engine.PairedWalls.All(wall => !wall.LengthMatched));
        }

        /// <summary>
        /// Every panel on the wall carries the warning, not only the first of them, because any
        /// one of them is what somebody will click on.
        /// </summary>
        [TestMethod]
        public void EveryPanelOnTheWallCarriesTheSameLengthResult()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("w1", 0, 0, 400, 0, 200),
                                   EtabsWall("w2", 400, 0, 800, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(2, engine.PairedWalls.Count);
            Assert.IsTrue(engine.PairedWalls.All(wall => !wall.LengthMatched));
            Assert.IsTrue(engine.PairedWalls.All(wall => Math.Abs(wall.CoveredLength - 800) < 1));
        }

        /// <summary>
        /// The note says both things once each. The checks are run again when the covered
        /// length arrives, and a note appended to twice would say everything twice.
        /// </summary>
        [TestMethod]
        public void TheNoteIsNotWrittenTwiceWhenTheLengthArrives()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("stub", 0, 400, 500, 400, 250) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            string note = engine.PairedWalls[0].Note;

            Assert.AreEqual(1, Occurrences(note, "displaced"));
            Assert.AreEqual(1, Occurrences(note, "ETABS covers"));
        }

        private static int Occurrences(string text, string word)
        {
            int count = 0;
            int at = text.IndexOf(word, StringComparison.Ordinal);

            while (at >= 0)
            {
                count++;
                at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>
        /// A panel standing off its Revit wall line is reported as displaced. The plan draws it
        /// where ETABS has it, so without this there is nothing to say Revit has it elsewhere.
        /// </summary>
        [TestMethod]
        public void APanelOffTheRevitLineIsReportedAsDisplaced()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 400, 6000, 400, 200),
                                             RevitWall(1, 0, 0, 6000, 0, 200));

            Assert.IsTrue(pair.Displaced);
            Assert.AreEqual(400, pair.LocationDelta, 1);

            CheckOutcome location = pair.Outcomes.First(outcome => outcome.Kind == CheckKind.WallLocation);
            Assert.AreEqual(ResultLevel.Failed, location.Level);
            StringAssert.Contains(location.Summary, "displaced");
        }

        /// <summary>A panel on the line is not, and says how far off it is anyway.</summary>
        [TestMethod]
        public void APanelOnTheRevitLineIsNotDisplaced()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 40, 6000, 40, 200),
                                             RevitWall(1, 0, 0, 6000, 0, 200));

            Assert.IsFalse(pair.Displaced);
            Assert.AreEqual(WallCheckState.Matched, pair.State);
        }

        /// <summary>
        /// Two panels with a gap between them cover what they cover and not the gap, so a wall
        /// they leave a hole in the middle of is measured on the two ends alone.
        /// </summary>
        [TestMethod]
        public void AGapBetweenPanelsIsNotCounted()
        {
            List<IWall> panels = new List<IWall>
            {
                EtabsWall("left", 0, 0, 400, 0, 200),
                EtabsWall("right", 5600, 0, 6000, 0, 200)
            };

            double covered = WallGeometry.CoveredLength(RevitWall(1, 0, 0, 6000, 0, 200), panels);

            Assert.AreEqual(800, covered, 1);
        }

        /// <summary>
        /// A wall nothing was ever paired with stays where it was, in the unpaired list - that
        /// one is still drawn, because there is no panel anywhere to stand for it.
        /// </summary>
        [TestMethod]
        public void ARevitWallNothingStoodAlongIsStillReportedUnpaired()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E>(),
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(1, engine.UnmatchedRevitWalls.Count);
            Assert.AreEqual(0, engine.PairedWalls.Count);
        }

        /// <summary>
        /// A pair is drawn where ETABS has it, at the ETABS thickness - the plan being the
        /// ETABS model is the whole point of drawing it that way round.
        /// </summary>
        [TestMethod]
        public void APairIsDrawnOnTheEtabsPanel()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 300, 3000, 300, 250),
                                             RevitWall(1, 0, 0, 6000, 0, 200));

            Assert.AreEqual(300, pair.PlanStart.Y, 0.001);
            Assert.AreEqual(3000, pair.PlanEnd.X, 0.001);
            Assert.AreEqual(250, pair.Thickness, 0.001);
        }

        /// <summary>
        /// Except its thickness where ETABS has none to give: a panel whose property would not
        /// read is drawn at the Revit thickness rather than as a hairline, and its thickness
        /// result says outright that nobody knows.
        /// </summary>
        [TestMethod]
        public void AnUnreadableEtabsThicknessIsDrawnAtTheRevitOne()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 0, 3000, 0, 0),
                                             RevitWall(1, 0, 0, 6000, 0, 200));

            Assert.AreEqual(200, pair.Thickness, 0.001);
        }

        #endregion

        #region the stretches ETABS misses altogether

        /// <summary>
        /// Past a metre of Revit wall with nothing along it, a warning is not enough: the
        /// stretch ETABS misses is drawn as the piece of wall it is.
        /// </summary>
        [TestMethod]
        public void AStretchOverTheBufferIsDrawnAsAPieceOfRevitWall()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("panel", 0, 0, 2000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(1, engine.UncoveredRevitWalls.Count);

            UncoveredWallPiece piece = engine.UncoveredRevitWalls[0];
            Assert.AreEqual(4000, piece.Length, 1);
            Assert.AreEqual(4000, piece.Uncovered, 1);
            Assert.AreEqual(2000, piece.Covered, 1);
            Assert.AreEqual(1, piece.Wall.RevitID);
        }

        /// <summary>
        /// And the piece drawn is the missing stretch alone, starting where ETABS leaves off -
        /// not the whole wall, which would be laid over the panel already on the plan.
        /// </summary>
        [TestMethod]
        public void TheDrawnPieceIsTheMissingStretchAndNotTheWholeWall()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("panel", 0, 0, 2000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            UncoveredWallPiece piece = engine.UncoveredRevitWalls[0];

            Assert.AreEqual(2000, piece.Start.X, 1);
            Assert.AreEqual(6000, piece.End.X, 1);
            Assert.AreEqual(0, piece.Start.Y, 1);
        }

        /// <summary>
        /// Under the buffer nothing is drawn. The wall is still warned about - the two buffers
        /// are different questions, one about saying so and one about showing it.
        /// </summary>
        [TestMethod]
        public void AStretchUnderTheBufferIsWarnedAboutButNotDrawn()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("panel", 0, 0, 5200, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(0, engine.UncoveredRevitWalls.Count, "800mm is under the metre");
            Assert.IsFalse(engine.PairedWalls[0].LengthMatched, "but over the 500mm warning");
        }

        /// <summary>A wall ETABS covers end to end has no missing stretch to draw.</summary>
        [TestMethod]
        public void AWallCoveredEndToEndDrawsNothingFromTheRevitSide()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("w1", 0, 0, 3000, 0, 200),
                                   EtabsWall("w2", 3000, 0, 6000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(0, engine.UncoveredRevitWalls.Count);
        }

        /// <summary>
        /// A wall covered at both ends and open in the middle gives one piece, in the middle,
        /// which is where the hole in the design model actually is.
        /// </summary>
        [TestMethod]
        public void AHoleInTheMiddleIsDrawnInTheMiddle()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("left", 0, 0, 2000, 0, 200),
                                   EtabsWall("right", 4000, 0, 6000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(1, engine.UncoveredRevitWalls.Count);

            UncoveredWallPiece piece = engine.UncoveredRevitWalls[0];
            Assert.AreEqual(2000, piece.Start.X, 1);
            Assert.AreEqual(4000, piece.End.X, 1);
        }

        /// <summary>
        /// Panels stacked up the height of a wall lie along the same stretch of it, so they
        /// leave no gaps between them to be drawn as holes.
        /// </summary>
        [TestMethod]
        public void StackedPanelsLeaveNoPhantomHoles()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("lower", 0, 0, 6000, 0, 200),
                                   EtabsWall("middle", 0, 0, 6000, 0, 200),
                                   EtabsWall("upper", 0, 0, 6000, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(3, engine.PairedWalls.Count);
            Assert.AreEqual(0, engine.UncoveredRevitWalls.Count);
        }

        /// <summary>
        /// Two gaps of six hundred leave a wall as badly served as one of twelve hundred, so
        /// the wall is judged on the whole of what is missing and then every piece is drawn.
        /// </summary>
        [TestMethod]
        public void TwoGapsAreJudgedTogetherAndBothDrawn()
        {
            WallCompareEngine engine = new WallCompareEngine();

            engine.StartComparison(
                new List<Wall_E> { EtabsWall("left", 600, 0, 3000, 0, 200),
                                   EtabsWall("right", 3000, 0, 5400, 0, 200) },
                new List<Wall_R> { RevitWall(1, 0, 0, 6000, 0, 200) });

            Assert.AreEqual(2, engine.UncoveredRevitWalls.Count);
            Assert.AreEqual(1200, engine.UncoveredRevitWalls[0].Uncovered, 1);
        }

        /// <summary>
        /// The sliver two walls leave where they meet is not a stretch of wall nobody modelled,
        /// so pieces shorter than the shortest wall the check bothers with are left alone.
        /// </summary>
        [TestMethod]
        public void SliversTooShortToBeWallsAreNotDrawn()
        {
            List<IWall> panels = new List<IWall>
            {
                EtabsWall("left", 0, 0, 2000, 0, 200),
                EtabsWall("right", 2050, 0, 6000, 0, 200)
            };

            List<MyPoint[]> pieces = WallGeometry.UncoveredSpans(
                RevitWall(1, 0, 0, 6000, 0, 200), panels, 200);

            Assert.AreEqual(0, pieces.Count, "a 50mm sliver is not a piece of wall");
        }

        #endregion

        #region a wall meshed up its height

        /// <summary>
        /// The other way a wall is subdivided: panels stacked one above another between two
        /// floors. In plan they land on top of each other exactly, so they are gathered into
        /// the one wall they stand on before anything is drawn.
        /// </summary>
        [TestMethod]
        public void PanelsStackedUpAWallAreGatheredIntoOneWall()
        {
            List<Wall_E> panels = new List<Wall_E>
            {
                EtabsWall("lower", 0, 0, 5000, 0, 200),
                EtabsWall("middle", 0, 0, 5000, 0, 200),
                EtabsWall("upper", 0, 0, 5000, 0, 200)
            };

            List<List<Wall_E>> stacks = WallStacks.Group(panels);

            Assert.AreEqual(1, stacks.Count);
            Assert.AreEqual(3, stacks[0].Count);
        }

        /// <summary>
        /// And the panels beside each other along the run are not: they are different pieces of
        /// wall in different places, and folding them would draw one wall where there are three.
        /// </summary>
        [TestMethod]
        public void PanelsAlongTheRunAreLeftWhereTheyWereModelled()
        {
            List<Wall_E> panels = new List<Wall_E>
            {
                EtabsWall("w1", 0, 0, 3000, 0, 200),
                EtabsWall("w2", 3000, 0, 6000, 0, 200),
                EtabsWall("w3", 6000, 0, 9000, 0, 200)
            };

            Assert.AreEqual(3, WallStacks.Group(panels).Count);
        }

        /// <summary>A panel drawn from the other end is on the same line, so it folds with it.</summary>
        [TestMethod]
        public void APanelDrawnTheOtherWayRoundIsOnTheSameLine()
        {
            List<Wall_E> panels = new List<Wall_E>
            {
                EtabsWall("lower", 0, 0, 5000, 0, 200),
                EtabsWall("upper", 5000, 0, 0, 0, 200)
            };

            Assert.AreEqual(1, WallStacks.Group(panels).Count);
        }

        /// <summary>
        /// The same line on two stories is two walls. Every floor of a tower has a wall exactly
        /// over the one below it, and folding those would leave one floor drawn for the lot.
        /// </summary>
        [TestMethod]
        public void TheSameLineOnTwoStoriesIsTwoWalls()
        {
            List<Wall_E> panels = new List<Wall_E>
            {
                EtabsWall("l1", 0, 0, 5000, 0, 200, "L1"),
                EtabsWall("l2", 0, 0, 5000, 0, 200, "L2")
            };

            Assert.AreEqual(2, WallStacks.Group(panels).Count);
        }

        /// <summary>
        /// Two panels on one line paired with two different Revit walls are two results about
        /// two walls. Folding them would leave one of the Revit walls with nothing drawn for it.
        /// </summary>
        [TestMethod]
        public void PanelsPairedWithDifferentRevitWallsAreNotFolded()
        {
            List<PairedWall> pairs = new List<PairedWall>
            {
                new PairedWall(EtabsWall("lower", 0, 0, 5000, 0, 200), RevitWall(1, 0, 0, 5000, 0, 200)),
                new PairedWall(EtabsWall("upper", 0, 0, 5000, 0, 200), RevitWall(2, 0, 0, 5000, 0, 200))
            };

            Assert.AreEqual(2, WallStacks.Group(pairs).Count);
        }

        /// <summary>
        /// A folded wall is shown as its worst panel. A wall right at the bottom and wrong at
        /// the top is a wall to go and look at, and drawing it in the colour of whichever panel
        /// happened to be drawn last would be a coin toss.
        /// </summary>
        [TestMethod]
        public void AFoldedWallIsShownAsItsWorstPanel()
        {
            Wall_R revit = RevitWall(1, 0, 0, 5000, 0, 200);

            List<PairedWall> stack = new List<PairedWall>
            {
                new PairedWall(EtabsWall("lower", 0, 0, 5000, 0, 200), revit),
                new PairedWall(EtabsWall("upper", 0, 0, 5000, 0, 250), revit)
            };

            PairedWall worst = PairedWall.Worst(stack);

            Assert.AreEqual("upper", worst.etabsWall.UniqueName);
            Assert.AreEqual(WallCheckState.Mismatched, worst.State);
        }

        /// <summary>Among panels that all went the same way, the one furthest off the line.</summary>
        [TestMethod]
        public void AmongEqualsTheWorstIsTheOneFurthestOffTheLine()
        {
            List<PairedWall> stack = new List<PairedWall>
            {
                new PairedWall(EtabsWall("lower", 0, 200, 5000, 200, 200), RevitWall(1, 0, 0, 5000, 0, 200)),
                new PairedWall(EtabsWall("upper", 0, 600, 5000, 600, 200), RevitWall(1, 0, 0, 5000, 0, 200))
            };

            Assert.AreEqual("upper", PairedWall.Worst(stack).etabsWall.UniqueName);
        }

        #endregion

        #region what a pair concludes

        [TestMethod]
        public void ADifferentThicknessFails()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 0, 5000, 0, 200),
                                             RevitWall(1, 0, 0, 5000, 0, 250));

            Assert.IsTrue(pair.LocationMatched);
            Assert.IsTrue(pair.ThicknessFailed);
            Assert.AreEqual(50, pair.ThicknessDelta, 1);
            Assert.AreEqual(WallCheckState.Mismatched, pair.State);
            Assert.AreEqual(ResultLevel.Failed, LevelOf(pair, CheckKind.WallThickness));
        }

        /// <summary>
        /// A thickness one of the models does not give up is neither a pass nor a failure. It
        /// is said outright, in the colour that means somebody should look, because reporting
        /// it as a match would be reporting agreement that was never checked.
        /// </summary>
        [TestMethod]
        public void AThicknessThatCouldNotBeReadIsNeitherAPassNorAFailure()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 0, 5000, 0, 0),
                                             RevitWall(1, 0, 0, 5000, 0, 200));

            Assert.IsFalse(pair.ThicknessFailed, "nothing was compared, so nothing failed");
            Assert.AreEqual(WallCheckState.Review, pair.State);
            Assert.AreEqual(ResultLevel.Review, LevelOf(pair, CheckKind.WallThickness));
        }

        /// <summary>
        /// Both checks publish a result of their own, which is what lets the viewer switch one
        /// off without touching what the other concluded.
        /// </summary>
        [TestMethod]
        public void EachCheckPublishesItsOwnResult()
        {
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 0, 5000, 0, 200),
                                             RevitWall(1, 0, 400, 5000, 400, 250));

            Assert.AreEqual(ResultLevel.Failed, LevelOf(pair, CheckKind.WallLocation));
            Assert.AreEqual(ResultLevel.Failed, LevelOf(pair, CheckKind.WallThickness));
            Assert.AreEqual(2, pair.Outcomes.Count);
        }

        /// <summary>
        /// A check switched off in the settings publishes nothing at all, rather than a passing
        /// result.
        ///
        /// That distinction is the whole point of the switch. A passing result would colour the
        /// wall green and say the two models were compared and agreed, which is exactly what did
        /// not happen; publishing nothing leaves the wall carrying only the checks that did run.
        /// </summary>
        [TestMethod]
        public void ACheckSwitchedOffPublishesNothingRatherThanPassing()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.RunWallLocationCheck = false;

            // Wrong on both counts, so if the location check were merely being ignored rather
            // than not made, it would still be here saying Failed.
            PairedWall pair = new PairedWall(EtabsWall("w1", 0, 0, 5000, 0, 200),
                                             RevitWall(1, 0, 400, 5000, 400, 250));

            Assert.IsFalse(pair.Outcomes.Any(o => o.Kind == CheckKind.WallLocation),
                           "the switched-off check left a result behind");
            Assert.AreEqual(ResultLevel.Failed, LevelOf(pair, CheckKind.WallThickness),
                            "and the check still switched on is untouched");
            Assert.AreEqual(1, pair.Outcomes.Count);
        }

        #endregion

        #region the names a wall is left out by

        /// <summary>
        /// The wall list is separated on commas alone, so a name keeps the space in it.
        ///
        /// This is the whole point of the wall list being split differently from the floor one.
        /// Split on spaces, "Second Cast" becomes "Cast", and a substring match on "Cast" takes
        /// every cast in situ wall in the model out of the check without saying so.
        /// </summary>
        [TestMethod]
        public void AnIgnoredWallNameMayHaveASpaceInIt()
        {
            string[] names = PrefrencesData.SplitTypeNamePhrases("Partition, Second Cast");

            CollectionAssert.AreEqual(new[] { "Partition", "Second Cast" }, names);
        }

        /// <summary>Empty boxes, stray commas and padding do not become names to match on.</summary>
        [TestMethod]
        public void EmptyEntriesAreNotNamesToMatchOn()
        {
            CollectionAssert.AreEqual(new string[0], PrefrencesData.SplitTypeNamePhrases(""));
            CollectionAssert.AreEqual(new string[0], PrefrencesData.SplitTypeNamePhrases(null));
            CollectionAssert.AreEqual(new[] { "Upstand" },
                                      PrefrencesData.SplitTypeNamePhrases("  , Upstand ,, "));
        }

        /// <summary>
        /// The upstands and the second cast pours the request was written for are left out by
        /// what the tool ships with, so a model gets them dropped without anyone typing a name.
        /// </summary>
        [TestMethod]
        public void UpstandAndSecondCastAreIgnoredOutOfTheBox()
        {
            PrefrencesData defaults =
                (PrefrencesData)Activator.CreateInstance(typeof(PrefrencesData), nonPublic: true);

            string[] names = PrefrencesData.SplitTypeNamePhrases(defaults.WallTypeNamesToIgnore);

            CollectionAssert.Contains(names, "Upstand");
            CollectionAssert.Contains(names, "Second Cast");
        }

        #endregion

        #region building the two models

        private static WallCompareEngine Compare(IList<Wall_E> etabs, IList<Wall_R> revit)
        {
            WallCompareEngine engine = new WallCompareEngine();
            engine.StartComparison(etabs, revit);
            return engine;
        }

        private static ResultLevel LevelOf(PairedWall pair, CheckKind kind)
        {
            CheckOutcome outcome = pair.Outcomes.FirstOrDefault(o => o.Kind == kind);
            Assert.IsNotNull(outcome, $"the wall check publishes a {kind} result");
            return outcome.Level;
        }

        /// <summary>An ETABS wall panel: a vertical shell three metres high on the given line.</summary>
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

        /// <summary>
        /// A Revit wall on the given line. The coordinates are given in mm like everything else
        /// in these tests and converted on the way in, because the container takes what Revit
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

        private static MyPoint InFeet(double xInMM, double yInMM)
        {
            return new MyPoint(xInMM / FeetToMM, yInMM / FeetToMM);
        }

        #endregion
    }
}
