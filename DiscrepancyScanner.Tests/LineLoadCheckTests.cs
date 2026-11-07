using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The line load comparison, on geometry rather than on a model.
    ///
    /// Everything awkward about this check is about shape rather than about either program:
    /// one ETABS frame holding what the drawing shows as three loads, one drawn line running
    /// past four frames, a line that sits a few hundred off the frame and a few degrees round
    /// from it, and a stepped run of lines replaced by one straight frame with the value
    /// scaled up. None of that needs Revit or ETABS open to be got wrong, so none of it needs
    /// them to be pinned.
    /// </summary>
    [TestClass]
    public class LineLoadCheckTests
    {
        /// <summary>
        /// The buffers these tests are written against. The preferences are a singleton the
        /// whole tool shares, so each test states what it depends on rather than trusting
        /// whatever ran before it.
        /// </summary>
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadStyleNamePatterns = "Line Load";
            pref.LineLoadHeightReduction = 200;
            // Off, so the values below are the geometry's own; the rounding has tests of its own.
            pref.LineLoadRoundUpTo = 0;
            pref.FullHeightLineLoadNames = "facade, cladding, glazing";
            pref.LineLoadLocationTolerance = 400;
            pref.LineLoadSearchRadius = 1500;
            pref.LineLoadAngleTolerance = 20;
            pref.LineLoadMinimumIssueLength = 250;
            pref.LineLoadTolerance = 0.2;
            pref.LineLoadCheckInSteps = true;
            pref.LineLoadStepLength = 250;

            // The patterns matter as much as the geometry: read the wrong ones and a correctly
            // loaded beam reads as carrying several times what the plan asks for.
            pref.LineLoadPatternNames = "SIDL";
            pref.ExcludedLoadPatternNames = "SIDL_C";

            // The check is off by default, and a check that is off publishes no result at all -
            // so without this the factory hands back elements carrying nothing and every test
            // about what is drawn reads an empty list.
            pref.RunLineLoadCheck = true;
        }

        #region reading the line style

        /// <summary>
        /// The style name is the whole of the tie to the schedule, and the number is the
        /// trailing digits of it. The prefix says which office drew it and which sheet family
        /// it belongs to, and the story is already paired with a sheet, so it decides nothing.
        /// </summary>
        [TestMethod]
        public void TheScheduleNumberIsTheDigitsOnTheEndOfTheStyleName()
        {
            Assert.AreEqual(1, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-TL1"));
            Assert.AreEqual(12, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-TL12"));
            Assert.AreEqual(7, LineLoad_R.NumberFromStyleName("Line Load 7"));
            Assert.AreEqual(0, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-Facade"));
            Assert.AreEqual(0, LineLoad_R.NumberFromStyleName(""));
        }

        /// <summary>
        /// The three shapes these names come in, all of them taken off one real project rather
        /// than imagined. Reading the trailing digits alone gets the first and the third and
        /// calls every podium style unnumbered - which is a whole family of loads sent quietly
        /// to review while the tower ones check fine.
        /// </summary>
        [TestMethod]
        public void TheNumberIsFoundWhateverFollowsItInTheStyleName()
        {
            Assert.AreEqual(1, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-TL1"));
            Assert.AreEqual(1, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-L1 Podium"));
            Assert.AreEqual(10, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-L10 Podium"));
            Assert.AreEqual(1, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-L1Podium"));
            Assert.AreEqual(2, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-STL2"));
            Assert.AreEqual(19, LineLoad_R.NumberFromStyleName("MRG_STR-Line Load-L19 Podium"));
        }

        /// <summary>
        /// Only after the last dash, so a number in the prefix - an office code, a year - is
        /// not read as the load number.
        /// </summary>
        [TestMethod]
        public void ANumberInThePrefixIsNotMistakenForTheLoadNumber()
        {
            Assert.AreEqual(1, LineLoad_R.NumberFromStyleName("MRG_STR_2024-Line Load-TL1"));
            Assert.AreEqual(3, LineLoad_R.NumberFromStyleName("A1-B2-Line Load 3"));
        }

        /// <summary>
        /// A person told to widen this setting types a star long before they think of clearing
        /// the box. Matched literally it looks for an asterisk inside the style name, finds one
        /// nowhere, and reads exactly like the check being broken - which is what happened.
        /// </summary>
        [TestMethod]
        public void AStarMeansEverythingRatherThanBeingLookedForLiterally()
        {
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("MRG_STR-Line Load-TL1", "*"));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("MRG_STR-Void", "*"));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("<Thin Lines>", " * "));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("anything", "**"));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("anything", "%"));
        }

        /// <summary>
        /// The default setting against the style names this project actually uses: the loads
        /// are read and the void lines drawn on the same plan are not.
        /// </summary>
        [TestMethod]
        public void TheDefaultPatternReadsTheProjectsLoadStylesAndNotItsVoidLines()
        {
            const string byDefault = "Line Load";

            foreach (string style in new[]
                     {
                         "MRG_STR-Line Load-TL1", "MRG_STR-Line Load-TL10",
                         "MRG_STR-Line Load-L1 Podium", "MRG_STR-Line Load-STL3"
                     })
            {
                Assert.IsTrue(LineLoad_R.IsLineLoadStyle(style, byDefault), style);
            }

            foreach (string style in new[] { "MRG_STR-Void", "MRG_STR-Grid Line", "<Thin Lines>" })
            {
                Assert.IsFalse(LineLoad_R.IsLineLoadStyle(style, byDefault), style);
            }
        }

        /// <summary>
        /// A loading plan carries ordinary detail lines too, and reading those as loads would
        /// put a finding on every leader and hatch boundary on the sheet.
        /// </summary>
        [TestMethod]
        public void OnlyLineStylesTheSettingsNameAreReadAsLoads()
        {
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("MRG_STR-Line Load-TL1", "Line Load"));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("mrg_str-line load-tl1", "Line Load"));
            Assert.IsFalse(LineLoad_R.IsLineLoadStyle("Thin Lines", "Line Load"));
            Assert.IsFalse(LineLoad_R.IsLineLoadStyle("<Hidden>", "Line Load"));
        }

        /// <summary>
        /// The way out of the trap this setting sets. A project whose styles are named in some
        /// way nobody anticipated gets a plan with nothing on it, and an empty drawing says
        /// nothing about which of the many possible reasons it was. Emptying the setting reads
        /// every line instead: noisy, and an answer.
        /// </summary>
        [TestMethod]
        public void AnEmptyStylePatternReadsEveryLine()
        {
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("Thin Lines", ""));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("MRG_STR-Line Load-TL1", ""));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("", ""));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("Anything At All", "  ,  "));
        }

        /// <summary>
        /// A trailing space on a style name is invisible on the drawing and fatal to the match.
        /// Folding trims, so a style typed with one still reads.
        /// </summary>
        [TestMethod]
        public void AStraySpaceOnTheStyleNameDoesNotStopItBeingRead()
        {
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle(" MRG_STR-Line Load-TL1 ", "Line Load"));
            Assert.IsTrue(LineLoad_R.IsLineLoadStyle("MRG_STR-Line Load-TL1", " Line Load "));
        }

        #endregion

        #region the ordinary case

        [TestMethod]
        public void ALineOverAFrameCarryingWhatTheScheduleAsksForPasses()
        {
            // 4.8 kN/m2 on elevation, 3.0m story above less the 200mm allowance = 2.8m of
            // wall, so the frame should carry 13.44 kN/m.
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(ResultLevel.Passed, result.Level);
            Assert.AreEqual(13.44, result.Expected, 1e-6);
        }

        [TestMethod]
        public void AFrameCarryingADifferentLoadFails()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 9.6))));

            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            Assert.AreEqual(ResultLevel.Failed, result.Level);
            Assert.AreEqual(9.6, result.Actual, 1e-6);
        }

        [TestMethod]
        public void AFrameWithNoLoadOnItAtAllFails()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0)));

            Assert.AreEqual(LineLoadState.NoEtabsLoad, result.State);
        }

        [TestMethod]
        public void ALineWithNothingUnderItAtAllFails()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 20000, 6000, 20000),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NoEtabsFrame, result.State);
        }

        /// <summary>
        /// A load line is drawn on the face of the wall it stands for and the frame runs down
        /// the middle of the beam, so a few hundred between them is the normal case. The check
        /// is asking whether the load is there, not whether the line is in the right place.
        /// </summary>
        [TestMethod]
        public void ALineDrawnOffTheFrameAndSlightlyRoundFromItIsStillOnIt()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 150, 6000, 320),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.Matched, result.State);
        }

        #endregion

        #region the shapes that make this hard

        /// <summary>
        /// ETABS splits a beam line at every frame that lands on it. The line is split to match,
        /// each part read against its own frame, so a line running the length of three of them
        /// is three thirds each carried rather than two thirds missing on each.
        /// </summary>
        [TestMethod]
        public void ALineRunningOverSeveralFramesIsComparedAgainstAllOfThem()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 9000, 0),
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 13.44)),
                       Frame("B3", 6000, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.Matched, result.State);
        }

        /// <summary>
        /// The other way round: one frame holding what the drawing shows as two separate line
        /// loads, each over its own stretch. Both should pass, because ETABS has each of them
        /// where the drawing puts it.
        /// </summary>
        [TestMethod]
        public void OneFrameCanCarrySeveralDrawnLinesEachOverItsOwnStretch()
        {
            // Number 1 is 4.8 on elevation, so 13.44 over 2.8m of wall.
            // Number 3 is 2.4 on elevation, so 6.72.
            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 4000, 0), Line(3, 4000, 0, 9000, 0) },
                Frames(Frame("B1", 0, 0, 9000, 0,
                             Load("SIDL", 13.44, 0, 4000d / 9000d),
                             Load("SIDL", 6.72, 4000d / 9000d, 1))));

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(result => result.State == LineLoadState.Matched),
                          "both stretches carry what the drawing asks for: "
                          + string.Join(" | ", results.Select(result => result.Summary)));
        }

        /// <summary>
        /// Two loads drawn along the same stretch of beam is two loads on that beam, so what
        /// ETABS should carry there is the sum. Comparing either one against the total on its
        /// own would report both of them as too much.
        /// </summary>
        [TestMethod]
        public void TwoLinesOverTheSameStretchAreAddedRatherThanTakenOneAtATime()
        {
            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 6000, 0), Line(3, 0, 0, 6000, 0) },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44 + 6.72))));

            Assert.IsTrue(results.All(result => result.State == LineLoadState.Matched),
                          "the frame carries both of them: "
                          + string.Join(" | ", results.Select(result => result.Summary)));
        }

        /// <summary>
        /// The same two loads, with the second drawn further off the frame than the location
        /// buffer - a facade line on the outer face of the slab edge, with the wall line on the
        /// beam. The second still goes on the nearest frame, but whole rather than laid along
        /// it, and that puts the frame on its total. The frame still carries the two of them,
        /// and each used to be compared against that sum on its own and failed.
        /// </summary>
        [TestMethod]
        public void TwoLinesOnOneFrameAreStillAddedWhenOneOfThemLiesOutsideTheBuffer()
        {
            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 4700, 0), Line(3, 0, -600, 4500, -600) },
                Frames(Frame("B1", 0, 0, 4730, 0, Load("SIDL", 13.44 + 6.72))));

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(result => result.State == LineLoadState.Matched),
                          "the frame carries both of them: "
                          + string.Join(" | ", results.Select(result => result.Summary)));
            Assert.IsFalse(results.Any(result => result.Bundled),
                           "a straight line parallel to the frame is walked in steps however far across it was drawn");
        }

        /// <summary>
        /// A bent line outside the buffer is walked in steps too, piece by piece: ETABS frames
        /// are straight and only subdivided, so the piece along the frame is read against it
        /// and added to the line already there, and the return across it is a part nothing in
        /// ETABS runs along.
        /// </summary>
        [TestMethod]
        public void ABentLineOutsideTheBufferIsWalkedPieceByPieceAndAddedToTheLineAlongTheFrame()
        {
            LineLoad_R bent = new LineLoad_R(903, "MRG_STR-Line Load-TL3",
                                             new[] { At(0, -600), At(4500, -600), At(4500, -1200) }, 3, "L03");

            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 4700, 0), bent },
                Frames(Frame("B1", 0, 0, 4730, 0, Load("SIDL", 13.44 + 6.72))));

            Assert.AreEqual(2, results.Count);
            Assert.IsFalse(results.Any(result => result.Bundled), "neither is put on the total");

            LineLoadResult wall = results.Single(result => result.Line.RevitID == 901);
            Assert.AreEqual(LineLoadState.Matched, wall.State, "the frame carries both: " + wall.Summary);

            LineLoadResult facade = results.Single(result => result.Line.RevitID == 903);
            Assert.IsTrue(facade.Stretches.Where(one => one.State != LineLoadState.NoEtabsFrame)
                                          .All(one => one.State == LineLoadState.Matched),
                          "the piece along the frame is added to the wall line: " + facade.Summary);
            Assert.AreEqual(600, facade.Stretches.Where(one => one.State == LineLoadState.NoEtabsFrame)
                                                 .Sum(one => one.Length), 1,
                            "the return across the frame has nothing along it");
        }

        /// <summary>
        /// A line running across the frame cannot be walked along it and is judged on the
        /// total - and the frame carrying the lines over it added together is still the frame
        /// carrying what they ask for.
        /// </summary>
        [TestMethod]
        public void ALineAcrossTheFrameOnTheTotalIsStillAddedToTheLineAlongIt()
        {
            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 4700, 0), Line(3, 0, -1000, 4000, 1000) },
                Frames(Frame("B1", 0, 0, 4730, 0, Load("SIDL", 13.44 + 6.72))));

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.Any(result => result.Bundled), "the line across is judged on the total");
            Assert.IsTrue(results.All(result => result.State == LineLoadState.Matched),
                          "the frame carries both of them: "
                          + string.Join(" | ", results.Select(result => result.Summary)));
        }

        /// <summary>
        /// Off a real model, frame B3078 on L03: a glazed facade along the slab edge, stepped
        /// back at a recess with a block wall across its mouth, GRC panels at its back, and a
        /// short GRC return down each side. ETABS carries the glazing's 5.6 either side and the
        /// wall and the GRC added together, 31.6, across the recess.
        ///
        /// The returns run across the frame a hair off square, which gave each a fraction of a
        /// millimetre along it. That was enough to place them on it whole, and every line on the
        /// frame went on the total with them - the glazing reported against the recess's 31.6,
        /// and the wall and the GRC each against the 31.6 they make between them.
        /// </summary>
        [TestMethod]
        public void AReturnAHairOffSquareDoesNotPutTheWholeFrameOnItsTotal()
        {
            // The wall's 25.92 rounded up to 26, as the project has it.
            PrefrencesData.getPrefrencesData().LineLoadRoundUpTo = 0.1;

            LineLoadCompareEngine engine = Engine();
            engine.SetSchedule("L03", RecessSchedule());
            engine.SetStoryAboveHeight("L03", 5600);

            LineLoad_R glazingBefore = Drawn(7101, 7, At(850, -140), At(3400, -140));
            LineLoad_R wall = Drawn(7102, 1, At(3400, -140), At(4600, -140));
            LineLoad_R grc = Drawn(7103, 4, At(3400, -470), At(4600, -470));
            LineLoad_R returnOne = Drawn(7104, 4, At(3400, -140), At(3400.001, -470));
            LineLoad_R returnTwo = Drawn(7105, 4, At(4600, -140), At(4599.999, -470));
            LineLoad_R glazingAfter = Drawn(7106, 7, At(4600, -140), At(7150, -140));

            engine.StartComparison(new[] { glazingBefore, wall, grc, returnOne, returnTwo, glazingAfter },
                Frames(Frame("B3078", 0, 0, 8000, 0,
                             Load("SIDL", 5.6, 850 / 8000d, 3400 / 8000d),
                             Load("SIDL", 31.6, 3400 / 8000d, 4600 / 8000d),
                             Load("SIDL", 5.6, 4600 / 8000d, 7150 / 8000d))));

            Func<LineLoad_R, LineLoadResult> resultOf = line => engine.Results.Single(result => result.Line == line);
            string all = string.Join(" | ", engine.Results.Select(result => result.Line.RevitID + ": " + result.Summary));

            Assert.IsFalse(engine.Results.Any(result => result.Bundled), "nothing is put on the total: " + all);

            foreach (LineLoad_R glazing in new[] { glazingBefore, glazingAfter })
            {
                Assert.AreEqual(LineLoadState.Matched, resultOf(glazing).State, all);
                Assert.AreEqual(5.6, resultOf(glazing).Actual, 1e-6, "what the frame carries under the glazing");
            }

            Assert.AreEqual(26, resultOf(wall).Expected, 1e-6);
            Assert.AreEqual(5.4, resultOf(grc).Expected, 1e-6);
            Assert.AreEqual(LineLoadState.Matched, resultOf(wall).State, "the wall and the GRC added: " + all);
            Assert.AreEqual(LineLoadState.Matched, resultOf(grc).State, "the wall and the GRC added: " + all);

            foreach (LineLoad_R across in new[] { returnOne, returnTwo })
            {
                Assert.AreEqual(LineLoadState.NoEtabsFrame, resultOf(across).State,
                                "a return across the frame has nothing along it, as a square one never had: " + all);
            }
        }

        /// <summary>
        /// A line that does run across the frame for a real length is still judged on the total,
        /// with the line it lies over - and nothing else. A line further along the same frame is
        /// walked as it would be without it, and read against what the frame carries under it
        /// rather than against the most the frame carries anywhere.
        /// </summary>
        [TestMethod]
        public void ALineAcrossTheFrameOnlyPutsTheLinesItLiesOverOnTheTotal()
        {
            LineLoad_R wall = Line(1, 0, 0, 4000, 0);
            LineLoad_R across = Line(5, 500, -1000, 3500, 1000);
            LineLoad_R further = Line(3, 5000, 0, 9000, 0);

            List<LineLoadResult> results = Run(new[] { wall, across, further },
                Frames(Frame("B1", 0, 0, 9000, 0,
                             Load("SIDL", 13.44 + 1.5, 0, 4000d / 9000d),
                             Load("SIDL", 6.72, 5000d / 9000d, 1))));

            string all = string.Join(" | ", results.Select(result => result.Line.RevitID + ": " + result.Summary));

            LineLoadResult acrossResult = results.Single(result => result.Line == across);
            Assert.IsTrue(acrossResult.Bundled, "33 degrees off the frame, so judged on the total");
            Assert.AreEqual(LineLoadState.Matched, acrossResult.State, "with the wall it lies over: " + all);
            Assert.AreEqual(LineLoadState.Matched, results.Single(result => result.Line == wall).State, all);

            LineLoadResult furtherResult = results.Single(result => result.Line == further);
            Assert.AreEqual(LineLoadState.Matched, furtherResult.State, all);
            Assert.AreEqual(6.72, furtherResult.Actual, 1e-6, "what the frame carries under it, not the 14.94 at the other end");
        }

        /// <summary>
        /// Line 5 on B227, L04: a 7.97 m balustrade asking for 1.5 on a frame carrying 1.5 under
        /// it, failed as "the schedule asks for 1.5 kN/m and the ETABS frame carries 1.5 kN/m".
        ///
        /// A line across it put it on the total, and with averaging off the total was still
        /// asked - the frame's, over the whole of a frame loaded twice as far as the balustrade
        /// is drawn. That is the averaging question, and with averaging off only the value
        /// under the line is.
        /// </summary>
        [TestMethod]
        public void ALineOnTheTotalIsJudgedOnItsValueNotOnTheWholeFramesTotal()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoad_R balustrade = Drawn(8001, 5, At(0, 0), At(7970, 0));
            LineLoad_R across = Drawn(8002, 5, At(1000, -1000), At(3000, 1000));

            engine.StartComparison(new[] { balustrade, across },
                Frames(Frame("B227", 0, 0, 16000, 0, Load("SIDL", 1.5))));

            string all = string.Join(" | ", engine.Results.Select(result => result.Line.RevitID + ": " + result.Summary));
            LineLoadResult balustradeResult = engine.Results.Single(result => result.Line == balustrade);

            Assert.AreEqual(LineLoadState.Matched, balustradeResult.State, all);
            Assert.AreEqual(LineLoadState.Matched,
                            engine.Results.Single(result => result.Line == across).State, all);
        }

        /// <summary>
        /// Where the frame does carry more than one load under a line on the total, it fails -
        /// and says so with the load that is wrong, not the highest one, which on a line asking
        /// for the highest read as the same number on both sides of a failure.
        /// </summary>
        [TestMethod]
        public void ALineOnTheTotalOverTwoLoadsShowsTheOneThatIsWrong()
        {
            LineLoadResult result = RunOne(Drawn(8003, 5, At(1000, -1000), At(3000, 1000)),
                Frames(Frame("B1", 0, 0, 6000, 0,
                             Load("SIDL", 1.5, 0, 2000d / 6000d),
                             Load("SIDL", 0.8, 2000d / 6000d, 1))));

            Assert.IsTrue(result.Bundled, "45 degrees across the frame, so on the total");
            Assert.AreEqual(LineLoadState.ValueMismatch, result.State, result.Summary);
            Assert.AreEqual(0.8, result.Actual, 1e-6, result.Summary);
        }

        /// <summary>
        /// A buffer of 0.2 lets through a difference of 0.2. As doubles, 26 and 5.4 added are
        /// 0.20000000000000284 short of 31.6, which is the same load on paper and was a finding.
        /// </summary>
        [TestMethod]
        public void LoadsExactlyTheBufferApartAreTheSameLoad()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetSchedule("L03", RecessSchedule());

            engine.StartComparison(new[] { Drawn(7201, 8, At(0, 0), At(3000, 0)),
                                           Drawn(7202, 9, At(0, -300), At(3000, -300)) },
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 31.6))));

            Assert.IsTrue(engine.Results.All(result => result.State == LineLoadState.Matched),
                          string.Join(" | ", engine.Results.Select(result => result.Summary)));
        }

        /// <summary>
        /// Adding them up is not a way round the check: a frame carrying neither line's value
        /// nor the two together is wrong for both.
        /// </summary>
        [TestMethod]
        public void TwoLinesOnOneFrameStillFailWhenItCarriesNeitherOneNorTheirSum()
        {
            List<LineLoadResult> results = Run(
                new[] { Line(1, 0, 0, 4700, 0), Line(3, 0, -600, 4500, -600) },
                Frames(Frame("B1", 0, 0, 4730, 0, Load("SIDL", 25.0))));

            Assert.AreEqual(2, results.Count);
            Assert.IsTrue(results.All(result => result.State == LineLoadState.ValueMismatch),
                          "25 is neither 13.44, 6.72 nor 20.16: "
                          + string.Join(" | ", results.Select(result => result.Summary)));
        }

        /// <summary>
        /// The ends of a drawn line and the ends of the frame under it never agree exactly -
        /// one was drawn on a plan and the other meshed by ETABS. So the last few centimetres
        /// of a line commonly have no load under them, and that sliver must not read as a
        /// difference: it would put a finding on most of the lines on the plan.
        /// </summary>
        [TestMethod]
        public void ASliverOfLineRunningPastTheEndOfTheLoadIsNotAFailure()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44, 0, 0.99))));

            Assert.AreEqual(LineLoadState.Matched, result.State,
                            "60mm of line past the end of the load is not a load difference: "
                            + result.Summary);
        }

        [TestMethod]
        public void ALineOnlyHalfOfWhichHasALoadUnderItIsReportedAsPartlyCovered()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 9000, 0),
                Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44, 0, 0.5))));

            Assert.AreEqual(LineLoadState.PartlyCovered, result.State);
            Assert.AreEqual(4500, result.CoveredLength, 1);
        }

        /// <summary>
        /// The line load sheets are headed SIDL, so a live load on the same frame is not part
        /// of what the schedule asks for, and is not read as part of what the frame carries.
        /// </summary>
        [TestMethod]
        public void ALiveLoadOnTheSameFrameIsLeftOut()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0,
                             Load("SIDL", 13.44),
                             Load("LL", 7.5))));

            Assert.AreEqual(LineLoadState.Matched, result.State,
                            "the live load is not what the line load schedule asks about: "
                            + result.Summary);
            Assert.AreEqual("13.44 kN/m", result.CarriedText, "the SDL alone, as it is");
        }

        #endregion

        #region which load patterns are read

        /// <summary>
        /// The one that would be quietly wrong in the other direction. A frame carrying a SIDL
        /// this check reads and a wind load it does not must come back as matching - counting
        /// the wind would report a correctly loaded beam as carrying far too much.
        /// </summary>
        [TestMethod]
        public void OnlyTheNamedPatternsAreRead()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0,
                             Load("SIDL", 13.44),
                             Load("WIND", 9.0),
                             Load("LL", 7.5))));

            Assert.AreEqual(LineLoadState.Matched, result.State, result.Summary);
            Assert.AreEqual(13.44, result.Actual, 1e-6);
        }

        /// <summary>
        /// SIDL_C is a pattern of its own with SIDL sitting inside its name. The exclusion list
        /// is asked before the pattern names are, exactly as the area load check does it.
        /// </summary>
        [TestMethod]
        public void AnExcludedPatternIsNotReadEvenThoughItsNameContainsTheWantedOne()
        {
            LineLoadPatterns patterns = LineLoadPatterns.From("SIDL", "SIDL_C");

            Assert.IsTrue(patterns.Matches("SIDL"));
            Assert.IsTrue(patterns.Matches("sidl"));
            Assert.IsTrue(patterns.Matches("SIDL-WALL"));
            Assert.IsFalse(patterns.Matches("SIDL_C"));
            Assert.IsFalse(patterns.Matches("LL"));
            Assert.IsFalse(patterns.Matches("WIND"));
        }

        /// <summary>
        /// The escape hatch, matching the one the line style setting has: a model whose patterns
        /// are named in some way nobody anticipated can be looked at rather than guessed at.
        /// </summary>
        [TestMethod]
        public void AnEmptyPatternSettingReadsEveryPattern()
        {
            LineLoadPatterns patterns = LineLoadPatterns.From("");

            Assert.IsTrue(patterns.IsEmpty);
            Assert.IsTrue(patterns.Matches("SIDL"));
            Assert.IsTrue(patterns.Matches("WIND"));
            Assert.IsTrue(patterns.Matches("anything at all"));
        }

        /// <summary>
        /// A setting naming a pattern the model has not got is the pattern-name twin of the
        /// line style trap, so it has to be said out loud rather than come back as silence.
        /// </summary>
        [TestMethod]
        public void APatternNameTheModelHasNotGotIsReported()
        {
            LineLoadCompareEngine engine = Engine();
            engine.Patterns = LineLoadPatterns.From("SIDL");

            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   Frames(Frame("B1", 0, 0, 6000, 0, Load("DEAD-SUPER", 13.44))));

            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("DEAD-SUPER")),
                          "what the frames actually carry should be named: "
                          + string.Join(" | ", engine.Issues));
        }

        #endregion

        #region a load averaged onto a frame is a difference

        /// <summary>
        /// A stepped run of drawn lines - 7.0m in all at 13.44 - modelled in ETABS as one
        /// straight 6.0m frame with the value scaled up to 15.68, so the total force is the
        /// same. ETABS loads are read as they are and never averaged: it is a difference, and
        /// the value shown is the one the frame carries - not the total spread over the run.
        /// </summary>
        [TestMethod]
        public void WithoutAveragingAScaledLoadIsADifference()
        {
            LineLoad_R stepped = new LineLoad_R(901, "MRG_STR-Line Load-TL1",
                new[] { At(0, 0), At(2500, 0), At(2500, 1000), At(6000, 1000) }, 1, "L03");

            LineLoadResult result = RunOne(stepped,
                Frames(Frame("B1", 0, 500, 6000, 500, Load("SIDL", 15.68))));

            // Walked in steps like any line along a frame, the two runs of it either side of
            // the frame read what the frame carries where they are, and the metre across it has
            // no frame along it at all - rather than 94 kN spread over the run.
            Assert.AreEqual(ResultLevel.Failed, result.Level);
            List<LineLoadStretch> along = result.Stretches.Where(one => one.State != LineLoadState.NoEtabsFrame).ToList();
            Assert.IsTrue(along.Count > 0 && along.All(one => one.State == LineLoadState.ValueMismatch
                                                              && Math.Abs(one.Carried - 15.68) < 1e-6),
                          "what the frame carries, not 94 kN over the run");
            Assert.AreEqual(1000, result.Stretches.Where(one => one.State == LineLoadState.NoEtabsFrame)
                                                  .Sum(one => one.Length), 1);

            // The frame reads 15.68 where it is, and the metre of line past its end has no
            // frame under it at all - each said for itself, rather than 94 kN over 7 m.
            LineLoadResult straight = RunOne(
                Line(1, 0, 0, 7000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 15.68))));

            Assert.AreEqual(ResultLevel.Failed, straight.Level);
            Assert.AreEqual(2, straight.Stretches.Count);
            Assert.AreEqual(LineLoadState.ValueMismatch, straight.Stretches[0].State);
            Assert.AreEqual(15.68, straight.Stretches[0].Carried, 1e-6);
            Assert.AreEqual(LineLoadState.NoEtabsFrame, straight.Stretches[1].State);
            Assert.AreEqual(6000, straight.Stretches[1].From, 1);
        }

        /// <summary>
        /// Two frames carrying loads a few hundredths apart, both close enough to the plan to
        /// pass. Each stretch says its own value, rather than one stretch printing a number
        /// neither frame carries.
        /// </summary>
        [TestMethod]
        public void WithoutAveragingAgreeingStretchesKeepTheirOwnValues()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 13.5))));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(2, result.Stretches.Count);
            Assert.AreEqual(13.44, result.Stretches[0].Carried, 1e-6);
            Assert.AreEqual(13.5, result.Stretches[1].Carried, 1e-6);
        }

        /// <summary>
        /// The same shape with the value not scaled: the frame carries the drawn value over a
        /// shorter length, so a metre of load has gone missing. That is a fault.
        /// </summary>
        [TestMethod]
        public void TheSameShapeWithTheValueNotScaledIsStillAFailure()
        {
            LineLoad_R stepped = new LineLoad_R(901, "MRG_STR-Line Load-TL1",
                new[] { At(0, 0), At(2500, 0), At(2500, 1000), At(6000, 1000) }, 1, "L03");

            LineLoadResult result = RunOne(stepped,
                Frames(Frame("B1", 0, 500, 6000, 500, Load("SIDL", 13.44))));

            // The metre that went missing is the metre across the frame, which nothing in ETABS
            // runs along; the rest carries what it asks for.
            Assert.AreEqual(LineLoadState.PartlyCovered, result.State);
            Assert.AreEqual(ResultLevel.Failed, result.Level);
            Assert.AreEqual(1000, result.Stretches.Where(one => one.State == LineLoadState.NoEtabsFrame)
                                                  .Sum(one => one.Length), 1);
            Assert.IsTrue(result.Stretches.Where(one => one.State != LineLoadState.NoEtabsFrame)
                                          .All(one => one.State == LineLoadState.Matched));
        }

        /// <summary>
        /// The commonest real fault this check exists to find: the frame carries a value six
        /// per cent off the schedule over the same length. A value difference, failed.
        /// </summary>
        [TestMethod]
        public void AValueThatIsSimplyWrongOverTheSameLengthFails()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 12.6))));

            // 4.8 x 2.8 = 13.44 asked for, 12.6 carried: 6% out, over exactly the same 6 m.
            Assert.AreEqual(LineLoadState.ValueMismatch, result.State,
                            "the same length carrying a different value is a value difference: "
                            + result.Summary);
            Assert.AreEqual(ResultLevel.Failed, result.Level);
        }

        #endregion

        #region the height rule, end to end

        /// <summary>
        /// A facade runs the full story height with no allowance off it, so the same schedule
        /// value comes out as a different load. This is the rule going through the whole check
        /// rather than through the height helper alone.
        /// </summary>
        [TestMethod]
        public void AFacadeIsCheckedAgainstTheFullStoryHeight()
        {
            // Number 7 is 1.0 kN/m2 and is a facade, so 1.0 x 3.0m = 3.0 kN/m, not 2.8.
            LineLoadResult result = RunOne(
                Line(7, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 3.0))));

            Assert.AreEqual(LineLoadState.Matched, result.State,
                            "a facade takes the full story height: " + result.Summary);
        }

        [TestMethod]
        public void ALoadPerMetreRunIsCheckedAgainstTheScheduleValueItself()
        {
            // Number 5 is 1.50 kN/m', which is the load whatever the story height is.
            LineLoadResult result = RunOne(
                Line(5, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 1.5))));

            Assert.AreEqual(LineLoadState.Matched, result.State, result.Summary);
        }

        [TestMethod]
        public void OnTheTopStoryALoadOnElevationIsPutUpForReviewRatherThanFailed()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetStoryAboveHeight("L03", 0);

            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NoExpectedValue, engine.Results[0].State);
            Assert.AreEqual(ResultLevel.Review, engine.Results[0].Level);
        }

        #endregion

        #region what the plan never asked for

        [TestMethod]
        public void AFrameLoadedWhereNoLineIsDrawnIsReportedAsUnclaimed()
        {
            LineLoadCompareEngine engine = Engine();

            engine.StartComparison(new[] { Line(1, 0, 0, 4000, 0) },
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(1, engine.Unclaimed.Count,
                            "the 4m to 9m stretch carries a load nothing drew");
            Assert.AreEqual(5000, engine.Unclaimed[0].Length, 1);
            Assert.AreEqual(13.44, engine.Unclaimed[0].PerMetre, 1e-6);
        }

        /// <summary>
        /// Where two drawn lines meet end to end their ends do not quite agree, and the sliver
        /// between them is not an unloaded stretch of beam. Reporting it would put a finding
        /// between every pair of lines on the plan.
        /// </summary>
        [TestMethod]
        public void TheSliverWhereTwoDrawnLinesMeetIsNotReported()
        {
            LineLoadCompareEngine engine = Engine();

            engine.StartComparison(
                new[] { Line(1, 0, 0, 4000, 0), Line(1, 4060, 0, 9000, 0) },
                Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(0, engine.Unclaimed.Count,
                            "a 60mm gap between two drawn lines is not a stretch of missing load");
        }

        /// <summary>
        /// The half of the check that can find something missing. A level whose loading plan has
        /// no line loads drawn on it still has ETABS frames carrying them, and those are loads
        /// somebody put in the model that the drawing never asked for.
        ///
        /// This used to report nothing at all: with no drawn lines there was no story to walk,
        /// so the comparison never ran and the quieter half of the answer came back as the whole.
        /// </summary>
        [TestMethod]
        public void WithNothingDrawnAtAllTheEtabsLoadsAreStillReportedAsUnaskedFor()
        {
            LineLoadCompareEngine engine = Engine();

            engine.StartComparison(new LineLoad_R[0],
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(0, engine.Results.Count, "nothing was drawn, so nothing carries a verdict");
            Assert.AreEqual(1, engine.Unclaimed.Count,
                            "but the frame's load is a load the plan does not ask for");
            Assert.AreEqual(9000, engine.Unclaimed[0].Length, 1);
            Assert.AreEqual(13.44, engine.Unclaimed[0].PerMetre, 1e-6);
        }

        /// <summary>
        /// The same on a story the drawing says nothing about while another story does. Walking
        /// only the stories something was drawn on would find the first and miss the second.
        /// </summary>
        [TestMethod]
        public void AStoryWithNoDrawnLinesIsStillCheckedWhenItIsInScope()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetStoriesToCheck(new[] { "L04" });
            engine.SetStoryAboveHeight("L04", 3000);

            LineLoadFrame_E onL04 = new LineLoadFrame_E("B9", "B9", "L04", At(0, 0), At(9000, 0));
            onL04.Add(Load("SIDL", 13.44));

            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   new[] { Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44)), onL04 });

            Assert.AreEqual(LineLoadState.Matched, engine.Results[0].State);
            Assert.IsTrue(engine.Unclaimed.Any(one => one.Story == "L04"),
                          "the load on L04, which nothing was drawn over, should be reported");
        }

        #endregion

        #region what is drawn in which colour

        /// <summary>
        /// A load one model has and the other has not is not a disagreement about a number, and
        /// colouring it red says the two were compared and differed when one of them had
        /// nothing to compare. It goes out under the one-model-only kinds, which is what the
        /// viewer draws in the "Revit only" and "ETABS only" colours - the same treatment an
        /// unpaired beam or a slab edge only one model has already gets.
        /// </summary>
        [TestMethod]
        public void ALineWithNoLoadUnderItIsReportedAsRevitOnlyRatherThanAsAFailure()
        {
            foreach (LineLoadState state in new[]
                     { LineLoadState.NoEtabsFrame, LineLoadState.NoEtabsLoad })
            {
                DiscrepancyScanner.Wpf.ElementViewModel element =
                    DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoad(
                        new LineLoadResult(Line(1, 0, 0, 6000, 0)) { State = state });

                Assert.AreEqual(DiscrepancyScanner.Wpf.ElementSource.UnmatchedRevit,
                                element.Source, state.ToString());
                Assert.AreEqual(CheckKind.LineLoadMissingInEtabs,
                                element.Outcomes[0].Kind, state.ToString());
            }
        }

        /// <summary>The other direction, drawn in the ETABS only colour for the same reason.</summary>
        [TestMethod]
        public void AnEtabsLoadNothingAskedForIsReportedAsEtabsOnly()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new LineLoad_R[0],
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel element =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromUnclaimedLineLoad(
                    engine.Unclaimed[0]);

            Assert.AreEqual(DiscrepancyScanner.Wpf.ElementSource.UnmatchedEtabs, element.Source);
            Assert.AreEqual(CheckKind.LineLoadMissingInRevit, element.Outcomes[0].Kind);
        }

        /// <summary>
        /// Red is kept for the two that really did differ: a load that is there and wrong, and
        /// a load that is there along only part of the line. Both models have a load in those,
        /// and what is being reported is the disagreement between them.
        /// </summary>
        [TestMethod]
        public void ALoadThatIsThereAndWrongIsStillAFailure()
        {
            foreach (LineLoadState state in new[]
                     { LineLoadState.ValueMismatch, LineLoadState.PartlyCovered })
            {
                DiscrepancyScanner.Wpf.ElementViewModel element =
                    DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoad(
                        new LineLoadResult(Line(1, 0, 0, 6000, 0)) { State = state });

                Assert.AreEqual(CheckKind.LineLoads, element.Outcomes[0].Kind, state.ToString());
                Assert.AreEqual(ResultLevel.Failed, element.Outcomes[0].Level, state.ToString());
            }
        }

        /// <summary>
        /// A line that paired with nothing has to say what it nearly paired with. Two lines
        /// side by side on the plan, one in each model's colour, with no word about the gap
        /// between them is the one output of this check that cannot be argued with - the two
        /// look adjacent and the reason they are apart is a number nobody can see.
        /// </summary>
        [TestMethod]
        public void ALineThatFoundNothingSaysHowFarTheNearestFrameWas()
        {
            // 2.5 m off, and the check looks 1.5 m.
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 2500, 6000, 2500, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NoEtabsFrame, result.State);
            Assert.IsTrue(result.Note.Contains("2.5 m"),
                          "it should say how far the nearest frame was: " + result.Note);
            Assert.IsTrue(result.Note.Contains("1.5 m"),
                          "and how far the check looks: " + result.Note);
        }

        [TestMethod]
        public void ALineOnALevelWithNoFramesSaysThatRatherThanMeasuringNothing()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) }, new LineLoadFrame_E[0]);

            Assert.IsTrue(engine.Results[0].Note.Contains("no ETABS frames on it at all"),
                          "an empty level is a pairing problem, not a distance: "
                          + engine.Results[0].Note);
        }

        /// <summary>
        /// The card used to contradict itself: a line that had found frames B3392 read "nothing
        /// in ETABS was close enough to pair with" at the top and named B3392 four rows below.
        /// The generic answer is worked out from the element's source and its Revit id, and a
        /// line load is read against a run of frames, which is neither.
        /// </summary>
        [TestMethod]
        public void TheCardSaysWhichFramesALineWasReadAgainst()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   Frames(Frame("B3392", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel element =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoad(engine.Results[0]);

            StringAssert.Contains(element.PairedWith, "B3392");
            Assert.IsFalse(element.PairedWith.Contains("close enough"),
                           "a line that found its frames must not say it found none: "
                           + element.PairedWith);
        }

        /// <summary>
        /// And a line that really found nothing still says so - with the reason, which is the
        /// thing somebody looking at a Revit-only line beside an ETABS-only one actually needs.
        /// </summary>
        [TestMethod]
        public void TheCardSaysWhyWhenALineFoundNoFramesAtAll()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   Frames(Frame("B1", 0, 2500, 6000, 2500, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel element =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoad(engine.Results[0]);

            StringAssert.Contains(element.PairedWith, "Nothing in ETABS runs under this line");
            StringAssert.Contains(element.PairedWith, "2.5 m");
        }

        /// <summary>The other direction reads as its own sentence rather than the generic one.</summary>
        [TestMethod]
        public void TheCardOnAnUnclaimedEtabsLoadSaysNothingAsksForIt()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new LineLoad_R[0],
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel element =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromUnclaimedLineLoad(
                    engine.Unclaimed[0]);

            StringAssert.Contains(element.PairedWith, "Nothing on the loading plan asks for");
        }

        /// <summary>
        /// A line only Revit has selected with a load only ETABS has - the two a person marks
        /// to pair by hand. The card used to call both paired, because a line load always says
        /// what it was read against even when that is nothing, and to fold the two findings into
        /// one row named "Check" with "Varies" on both sides, because neither check had a name
        /// of its own. Each is its own row now, with its own model's value.
        /// </summary>
        [TestMethod]
        public void TheCardForALineOnlyRevitHasAndALoadOnlyEtabsHasShowsEachSide()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { Line(1, 0, 20000, 6000, 20000) },
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel revitOnly =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadResult(engine.Results.Single()).Single();
            DiscrepancyScanner.Wpf.ElementViewModel etabsOnly =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromUnclaimedLineLoad(engine.Unclaimed.Single());

            DiscrepancyScanner.Wpf.ElementCard card = new DiscrepancyScanner.Wpf.ElementCard(
                new List<DiscrepancyScanner.Wpf.ElementViewModel> { revitOnly, etabsOnly }, _ => null);

            Assert.AreEqual("None of them is paired.", card.PairingNote);

            CollectionAssert.AreEquivalent(new[] { "Line load in Revit only", "Line load in ETABS only" },
                                           card.Rows.Select(row => row.Label).ToList());
            Assert.IsTrue(card.Rows.All(row => row.Etabs != "Varies" && row.Revit != "Varies"),
                          "each row is one element's finding, so it has one element's values");
            StringAssert.Contains(card.Rows.Single(row => row.Label == "Line load in ETABS only").Etabs, "13.44");

            Assert.AreEqual(revitOnly.RevitId.ToString(),
                            card.Facts.Single(fact => fact.Label == "Revit id (1 of 2)").Value,
                            "a field only the drawn line has is shown for the line, not as Varies");
        }

        #endregion

        #region the raw data

        /// <summary>
        /// Every loaded stretch is collected, not only the half the check reports. A stretch the
        /// plan claims is answered by the drawn line over it, so the check says nothing about
        /// it - but an acceptance filed on it while it was unclaimed is found again through it.
        /// </summary>
        [TestMethod]
        public void EveryLoadedStretchIsCollectedClaimedOrNot()
        {
            LineLoadCompareEngine engine = Engine();

            // The line covers the first 4 m of a 9 m frame, so 4 m is claimed and 5 m is not.
            engine.StartComparison(new[] { Line(1, 0, 0, 4000, 0) },
                                   Frames(Frame("B1", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(1, engine.Unclaimed.Count,
                            "the check reports only the stretch nothing asked for");
            Assert.AreEqual(2, engine.EtabsStretches.Count,
                            "both are kept: " + string.Join(" | ",
                                engine.EtabsStretches.Select(one => one.Summary)));

            Assert.AreEqual(1, engine.EtabsStretches.Count(one => one.Claimed));
            Assert.AreEqual(1, engine.EtabsStretches.Count(one => !one.Claimed));
        }

        /// <summary>
        /// In the raw data a drawn line is what was read and nothing more: the line that found
        /// its frames and the line that found none look the same, carry no result, and are
        /// drawn in the Revit colour. Which of them found anything is the check's to say.
        /// </summary>
        [TestMethod]
        public void ADrawnLineIsTheSameRawDataWhateverTheCheckMadeOfIt()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(
                new[] { Line(1, 0, 0, 6000, 0), Line(5, 0, 20000, 6000, 20000) },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            DiscrepancyScanner.Wpf.ElementViewModel found =
                DiscrepancyScanner.Wpf.RawElements.RevitLineLoad(engine.Results.First(r => r.Run != null));
            DiscrepancyScanner.Wpf.ElementViewModel lost =
                DiscrepancyScanner.Wpf.RawElements.RevitLineLoad(engine.Results.First(r => r.Run == null));

            foreach (DiscrepancyScanner.Wpf.ElementViewModel line in new[] { found, lost })
            {
                Assert.IsTrue(line.IsRaw);
                Assert.AreEqual(0, line.Outcomes.Count, "the raw data carries no results");
                Assert.AreEqual(DiscrepancyScanner.Wpf.ElementSource.RevitOnly, line.Source);
                Assert.IsFalse(line.CanBeFiled, "nothing is filed under the raw data");
            }

            Assert.AreEqual("1.5 kN/m", lost.GetFieldValue(DiscrepancyScanner.Wpf.RawResultKeys.LineLoadLoad),
                            "the load the schedule gives the line's number is what Revit asks for");
            Assert.AreEqual("5", lost.GetFieldValue(DiscrepancyScanner.Wpf.RawResultKeys.LineLoadNumber));
        }

        /// <summary>
        /// The ETABS side of the raw data is the frames as they were imported, with every load
        /// on them by pattern - including the ones the check would add together or leave out.
        /// </summary>
        [TestMethod]
        public void AnEtabsFrameIsRawDataWithEveryLoadOnIt()
        {
            LineLoadFrame_E frame = Frame("B7", 0, 0, 9000, 0,
                                          Load("SIDL", 13.44),
                                          new LineLoad_E("WIND", 4.2, 6.3, 0.25, 0.75, 6, false, false));

            Assert.IsTrue(DiscrepancyScanner.Wpf.RawElements.CarriesALineLoad(frame));

            DiscrepancyScanner.Wpf.ElementViewModel element = DiscrepancyScanner.Wpf.RawElements.EtabsLineLoad(frame);

            string loads = element.GetFieldValue(DiscrepancyScanner.Wpf.RawResultKeys.LineLoadLoad);
            StringAssert.Contains(loads, "SIDL 13.44 kN/m");
            StringAssert.Contains(loads, "WIND 4.2 to 6.3 kN/m over 25% to 75%");

            Assert.AreEqual(DiscrepancyScanner.Wpf.ElementSource.EtabsOnly, element.Source);
            Assert.AreEqual(0, element.Outcomes.Count);
            Assert.AreEqual("9", element.GetFieldValue(DiscrepancyScanner.Wpf.RawResultKeys.LineLoadLength));
            Assert.IsFalse(DiscrepancyScanner.Wpf.PairingEditor.CanBeMarked(element),
                           "nothing on the raw data is paired, so nothing on it can be paired by hand");
        }

        /// <summary>A frame with nothing on it has no line load to show.</summary>
        [TestMethod]
        public void AFrameWithNoLoadIsNotLineLoadRawData()
        {
            Assert.IsFalse(DiscrepancyScanner.Wpf.RawElements.CarriesALineLoad(Frame("B8", 0, 0, 9000, 0)));
        }

        #endregion

        #region pairing by hand

        /// <summary>
        /// The automatic pass puts a line on the run it runs along for longest, which is right
        /// nearly always and wrong exactly where two runs lie close together. A pairing set by
        /// hand is taken before any distance is measured, so measuring again cannot reproduce
        /// the answer somebody was correcting.
        /// </summary>
        [TestMethod]
        public void ALineCanBePinnedToARunItWouldNotHaveChosen()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set("P", PairingOverrides.KeyOf(ElementKindKey.LineLoad, "FAR"), 901);

            LineLoadCompareEngine engine = Engine();
            engine.Overrides = overrides;
            engine.ProjectKey = "P";

            // The line sits on NEAR and would pair with it on distance alone. FAR is the one
            // carrying the load the schedule asks for.
            LineLoadResult result = RunOne(engine,
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("NEAR", 0, 100, 6000, 100),
                       Frame("FAR", 0, 1200, 6000, 1200, Load("SIDL", 13.44))));

            Assert.AreEqual("FAR", result.Run.Identifier, "the pinned run should have been used");
            Assert.AreEqual(LineLoadState.Matched, result.State, result.Summary);
        }

        /// <summary>
        /// A pairing is written against one frame and pins that frame alone: the part of the
        /// line over it goes to it however far off it is, and the rest of the line finds its own
        /// frames the ordinary way.
        /// </summary>
        [TestMethod]
        public void PinningAFrameGivesItThePartOfTheLineOverItAndNoMore()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set("P", PairingOverrides.KeyOf(ElementKindKey.LineLoad, "FAR2"), 901);

            LineLoadCompareEngine engine = Engine();
            engine.Overrides = overrides;
            engine.ProjectKey = "P";

            LineLoadResult result = RunOne(engine,
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("NEAR1", 0, 100, 3000, 100, Load("SIDL", 13.44)),
                       Frame("NEAR2", 3000, 100, 6000, 100),
                       Frame("FAR1", 0, 1200, 3000, 1200, Load("SIDL", 13.44)),
                       Frame("FAR2", 3000, 1200, 6000, 1200, Load("SIDL", 13.44))));

            Assert.AreEqual("NEAR1, FAR2", result.Frames);
            Assert.AreEqual(LineLoadState.Matched, result.State, result.Summary);
            StringAssert.Contains(result.Note, "paired by hand");
        }

        /// <summary>
        /// Said from the ETABS side, because that is the only side a pairing can be written
        /// from: this frame's load goes with no drawn line. The line then pairs elsewhere or
        /// with nothing, and the run comes back as a load nobody asked for - which is what
        /// saying so was for.
        /// </summary>
        [TestMethod]
        public void ARunSaidToGoWithNothingIsLeftForTheLineToMissAndReportedAsUnasked()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set("P", PairingOverrides.KeyOf(ElementKindKey.LineLoad, "B1"),
                          PairingOverrides.Unpaired);

            LineLoadCompareEngine engine = Engine();
            engine.Overrides = overrides;
            engine.ProjectKey = "P";

            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) },
                                   Frames(Frame("B1", 0, 100, 6000, 100, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NoEtabsFrame, engine.Results[0].State,
                            "the line may not claim a run somebody said goes with nothing");
            Assert.AreEqual(1, engine.Unclaimed.Count,
                            "and that run is a load nothing asked for");
        }

        /// <summary>
        /// The stretch has to name a frame, because that name is what a pairing is written
        /// against and what a review is filed under - and both have to be found again next run.
        /// </summary>
        [TestMethod]
        public void AnUnclaimedStretchNamesTheFrameItLiesOn()
        {
            LineLoadCompareEngine engine = Engine();

            engine.StartComparison(new LineLoad_R[0],
                                   Frames(Frame("B7", 0, 0, 9000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(1, engine.Unclaimed.Count);
            Assert.AreEqual("B7", engine.Unclaimed[0].FrameName);
            Assert.AreEqual("B7", engine.Unclaimed[0].Key,
                            "the review key and the pairing key are the same frame name");
        }

        /// <summary>
        /// Two frames side by side a few centimetres apart, both loaded, one drawn line. The line
        /// goes to one and the other's load is left over, which on the plan is a line with an
        /// ETABS-only frame lying right beside it and nothing to say why - so both say so.
        /// </summary>
        [TestMethod]
        public void ALoadLeftOnAFrameBesideTheLinesOwnSaysWhichLineLiesAlongIt()
        {
            LineLoadCompareEngine engine = Engine();

            LineLoadResult result = RunOne(engine,
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 1000, 30, 4000, 30, Load("SIDL", 13.44))));

            Assert.AreEqual("B1", result.Run.Identifier, "the frame it runs along for longest");
            Assert.AreEqual(1, engine.Unclaimed.Count);
            Assert.AreEqual("B2", engine.Unclaimed[0].FrameName);

            StringAssert.Contains(engine.Unclaimed[0].Alongside, "line load 901");
            StringAssert.Contains(engine.Unclaimed[0].Alongside, "B1");
            StringAssert.Contains(result.Alongside, "ETABS B2");
        }

        [TestMethod]
        public void AnUnclaimedLoadWithNoLineBesideItSaysNothingAboutOne()
        {
            LineLoadCompareEngine engine = Engine();

            RunOne(engine,
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 1000, 3000, 4000, 3000, Load("SIDL", 13.44))));

            Assert.AreEqual(1, engine.Unclaimed.Count);
            Assert.AreEqual("", engine.Unclaimed[0].Alongside,
                            "three metres away is another wall, not the same one twice");
        }

        #endregion

        #region the loads the check will not guess at

        /// <summary>
        /// Which way a frame's local 2 points depends on that frame's rotation, so the check
        /// will not put a sign on a load assigned in one. Guessing would read a load as twice
        /// its size in the wrong direction, so it is reported and left out instead.
        /// </summary>
        [TestMethod]
        public void ALoadInALocalDirectionIsReportedRatherThanCounted()
        {
            LineLoadCompareEngine engine = Engine();

            LineLoadFrame_E frame = Frame("B1", 0, 0, 6000, 0);
            frame.Add(new LineLoad_E("SIDL", 13.44, 13.44, 0, 1, 2, false, false));

            engine.StartComparison(new[] { Line(1, 0, 0, 6000, 0) }, new[] { frame });

            Assert.AreEqual(LineLoadState.NoEtabsLoad, engine.Results[0].State);
            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("local axes")),
                          "the load that was not counted should be said out loud");
        }

        [TestMethod]
        public void GravityAndGlobalZAreBothReadAsDownwards()
        {
            double sign;

            Assert.IsTrue(LineLoad_E.IsDownwardDirection(10, out sign));
            Assert.AreEqual(1, sign, 1e-9);

            Assert.IsTrue(LineLoad_E.IsDownwardDirection(6, out sign));
            Assert.AreEqual(-1, sign, 1e-9, "global Z is positive upwards");

            Assert.IsFalse(LineLoad_E.IsDownwardDirection(1, out sign));
            Assert.IsFalse(LineLoad_E.IsDownwardDirection(3, out sign));
        }

        #endregion

        #region the schedule number

        [TestMethod]
        public void ALineWhoseNumberIsNotOnTheSheetIsPutUpForReview()
        {
            LineLoadResult result = RunOne(
                Line(99, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NotInSchedule, result.State);
            Assert.AreEqual(ResultLevel.Review, result.Level);
        }

        [TestMethod]
        public void ALineStyleWithNoNumberOnItIsPutUpForReview()
        {
            LineLoad_R unnumbered = new LineLoad_R(902, "MRG_STR-Line Load-Facade",
                                                   new[] { At(0, 0), At(6000, 0) }, 0, "L03");

            LineLoadResult result = RunOne(unnumbered,
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.NoNumber, result.State);
            Assert.AreEqual(ResultLevel.Review, result.Level);
        }

        #endregion

        #region checking a line in steps

        /// <summary>
        /// One line over two frames that carry different loads. Only the stretch over the
        /// wrong frame is a finding, and it starts exactly where that frame does - 3.1 m, which
        /// no 250 mm step lands on, so the frame end has to be a step of its own.
        /// </summary>
        [TestMethod]
        public void OnlyTheStretchThatDiffersIsReportedAndItStartsAtTheFrameEnd()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 3100, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3100, 0, 6000, 0, Load("SIDL", 9.6))));

            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            Assert.AreEqual(2, result.Stretches.Count);

            LineLoadStretch good = result.Stretches[0];
            LineLoadStretch bad = result.Stretches[1];

            Assert.AreEqual(LineLoadState.Matched, good.State);
            Assert.AreEqual(3100, good.To, 1);

            Assert.AreEqual(LineLoadState.ValueMismatch, bad.State);
            Assert.AreEqual(3100, bad.From, 1, "the step is forced at the frame end");
            Assert.AreEqual(6000, bad.To, 1);
            Assert.AreEqual(9.6, bad.Carried, 1e-6, "what ETABS carries there, not an average");
            Assert.AreEqual(13.44, bad.Wanted, 1e-6);
        }

        /// <summary>
        /// The load starts part way along the frame - 1.5 kN/m from 2.7177 m to the end, off a
        /// real model. The plan asks for it along the whole frame, so the first stretch has
        /// nothing under it, and the second carries 1.5: not the 1.5 spread thinner over the
        /// whole line that the average made of it.
        /// </summary>
        [TestMethod]
        public void ALoadStartingPartWayAlongAFrameIsReportedWhereItIsAndAtItsOwnValue()
        {
            LineLoadResult result = RunOne(
                Line(5, 0, 0, 6041, 0),
                Frames(Frame("B1275", 0, 0, 6041, 0, Load("SIDL", 1.5, 2717.7 / 6041, 1))));

            Assert.AreEqual(LineLoadState.PartlyCovered, result.State);
            Assert.AreEqual(2, result.Stretches.Count);

            Assert.AreEqual(LineLoadState.NoEtabsLoad, result.Stretches[0].State);
            Assert.AreEqual(2717.7, result.Stretches[0].To, 1, "the step is forced where the load starts");

            Assert.AreEqual(LineLoadState.Matched, result.Stretches[1].State);
            Assert.AreEqual(1.5, result.Stretches[1].Carried, 1e-6);
        }

        /// <summary>
        /// The same load with a line drawn a little short of where it starts: under the
        /// shortest difference worth reporting, so the line passes - and it reads 1.5, the load
        /// that is there, rather than 1.5 averaged over a length part of which carries nothing.
        /// </summary>
        [TestMethod]
        public void ALinePassingOverTheStartOfALoadReadsTheLoadNotAnAverage()
        {
            LineLoadResult result = RunOne(
                Line(5, 2480, 0, 3860, 0),
                Frames(Frame("B1275", 0, 0, 6041, 0, Load("SIDL", 1.5, 2717.7 / 6041, 1))));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(1, result.Stretches.Count);
            Assert.AreEqual(1.5, result.Actual, 1e-6);
        }

        /// <summary>
        /// A line turning a corner is walked leg by leg, each leg on the beam it runs along:
        /// the leg over the right load passes, the leg over the wrong one is the finding, and
        /// nothing is left over on either beam.
        /// </summary>
        [TestMethod]
        public void ABentLineIsWalkedLegByLegEachOnTheFrameItRunsAlong()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoad_R corner = new LineLoad_R(901, "MRG_STR-Line Load-TL1",
                new[] { At(0, 0), At(3000, 0), At(3000, 3000) }, 1, "L03");

            LineLoadResult result = RunOne(engine, corner,
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 3000, 3000, Load("SIDL", 9.6))));

            Assert.IsFalse(result.Bundled, "walked, not judged on its total");
            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            Assert.AreEqual(2, result.Stretches.Count);

            Assert.AreEqual(LineLoadState.Matched, result.Stretches[0].State);
            Assert.AreEqual(3000, result.Stretches[0].To, 1);

            Assert.AreEqual(LineLoadState.ValueMismatch, result.Stretches[1].State);
            Assert.AreEqual(3000, result.Stretches[1].From, 1);
            Assert.AreEqual(6000, result.Stretches[1].To, 1);
            Assert.AreEqual(9.6, result.Stretches[1].Carried, 1e-6);

            Assert.AreEqual(0, engine.Unclaimed.Count, "both beams are claimed by their own leg");
        }

        [TestMethod]
        public void ALegOfABentLineWithNoFrameAlongItIsReportedAsThat()
        {
            LineLoad_R corner = new LineLoad_R(901, "MRG_STR-Line Load-TL1",
                new[] { At(0, 0), At(3000, 0), At(3000, 3000) }, 1, "L03");

            LineLoadResult result = RunOne(corner,
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.PartlyCovered, result.State);
            Assert.AreEqual(2, result.Stretches.Count);
            Assert.AreEqual(LineLoadState.Matched, result.Stretches[0].State);
            Assert.AreEqual(LineLoadState.NoEtabsFrame, result.Stretches[1].State);
        }

        /// <summary>
        /// A bent line on a run used to take every other line on that run down to a judgement
        /// on the total. Walked leg by leg, the straight line beside it keeps its steps.
        /// </summary>
        [TestMethod]
        public void AStraightLineSharingARunWithABentOneIsStillWalked()
        {
            LineLoad_R corner = new LineLoad_R(901, "MRG_STR-Line Load-TL1",
                new[] { At(0, 0), At(3000, 0), At(3000, 3000) }, 1, "L03");
            LineLoad_R straight = Line(3, 3000, 0, 6000, 0);

            List<LineLoadResult> results = Run(new[] { corner, straight },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44, 0, 0.5), Load("SIDL", 9.6, 0.5, 1)),
                       Frame("B2", 3000, 0, 3000, 3000, Load("SIDL", 13.44))));

            Assert.AreEqual(2, results.Count, "one result per drawn line, however it was placed");

            LineLoadResult second = results.Single(result => result.Line == straight);
            Assert.AreEqual(6.72, second.Expected, 1e-6, "2.4 kN/m2 over 2.8 m of wall");
            Assert.AreEqual(1, second.Stretches.Count, "walked in steps, not judged on the run's total");
            Assert.AreEqual(9.6, second.Stretches[0].Carried, 1e-6);
            Assert.AreEqual(LineLoadState.ValueMismatch, second.State);

            LineLoadResult bent = results.Single(result => result.Line == corner);
            Assert.AreEqual(LineLoadState.Matched, bent.State, "both legs carry the 13.44 asked for");
        }

        /// <summary>
        /// Two lines drawn along one beam, and ETABS carries what one of them asks for rather
        /// than what they add up to. The one nearer the frame takes it; the other has nothing
        /// else to go to and is reported as taken - not the two of them failing against a load
        /// half what they add up to.
        /// </summary>
        [TestMethod]
        public void TheNearerOfTwoLinesTakesALoadThatOnlyOneOfThemAsksFor()
        {
            LineLoad_R near = Line(1, 0, 50, 6000, 50);
            LineLoad_R far = new LineLoad_R(950, "MRG_STR-Line Load-TL1",
                new[] { At(0, 300), At(6000, 300) }, 1, "L03");

            List<LineLoadResult> results = Run(new[] { near, far },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44))));

            Assert.AreEqual(2, results.Count);

            LineLoadResult taker = results.Single(result => result.Line == near);
            Assert.AreEqual(LineLoadState.Matched, taker.State);
            Assert.AreEqual(13.44, taker.Stretches.Single().Wanted, 1e-6, "its own value, not the two added");

            LineLoadResult other = results.Single(result => result.Line == far);
            Assert.AreEqual(LineLoadState.TakenByOther, other.State);
            Assert.AreEqual(ResultLevel.Failed, other.Level);
            Assert.AreEqual("line load 901", other.Stretches.Single().TakenBy);
        }

        /// <summary>
        /// The same two lines with a second beam beside the first, carrying the same load. Each
        /// line goes to the beam nearer it - the one it was drawn for - and passes there.
        /// Nothing is left unclaimed.
        /// </summary>
        [TestMethod]
        public void ALineWhoseLoadANearerLineTookGoesToTheNextBeamAlongIt()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoad_R near = Line(1, 0, 50, 6000, 50);
            LineLoad_R far = new LineLoad_R(950, "MRG_STR-Line Load-TL1",
                new[] { At(0, 300), At(6000, 300) }, 1, "L03");

            engine.StartComparison(new[] { near, far },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 0, 350, 6000, 350, Load("SIDL", 13.44))));

            LineLoadResult taker = engine.Results.Single(result => result.Line == near);
            LineLoadResult handed = engine.Results.Single(result => result.Line == far);

            Assert.AreEqual(LineLoadState.Matched, taker.State);
            Assert.AreEqual(LineLoadState.Matched, handed.State, "carried by the beam beside it");
            Assert.AreEqual("B1", taker.Frames);
            Assert.AreEqual("B2", handed.Frames);
            Assert.AreEqual(0, engine.Unclaimed.Count, "both beams are claimed");
        }

        /// <summary>
        /// Off a real model (L00, line 2211307 on B238): a wall line right over its beam but a
        /// little longer than it, so its two ends - 61 and 69 mm - go to the long beam beside it,
        /// whose own line takes the load there. Those two ends are all that line has on that
        /// beam, with the whole of the wall between them on the other one. Folded into each
        /// other they once read as the whole line taken, it was handed back onto its own beam
        /// on top of itself, and the card said the line took its load from itself.
        /// </summary>
        [TestMethod]
        public void ALineOverhangingItsBeamIsNotTakenByItself()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoad_R wall = Line(1, -61, 0, 3569, 0);
            LineLoad_R balustrade = new LineLoad_R(950, "MRG_STR-Line Load-TL5",
                new[] { At(-1300, 350), At(4800, 350) }, 5, "L03");

            engine.StartComparison(new[] { wall, balustrade },
                Frames(Frame("B1", 0, 0, 3500, 0, Load("SIDL", 13.44)),
                       Frame("B2", -1300, 350, 4800, 350, Load("SIDL", 1.5))));

            LineLoadResult block = engine.Results.Single(result => result.Line == wall);

            Assert.AreEqual(LineLoadState.Matched, block.State,
                string.Join("\n", block.Stretches.Select(one => $"{one.From:0}-{one.To:0} {one.State} {one.TakenBy}")));
            Assert.IsFalse(block.Stretches.Any(one => one.State == LineLoadState.TakenByOther));
            Assert.IsFalse(block.Stretches.Any(one => one.From < block.Stretches[0].To && one != block.Stretches[0]),
                           "no two stretches over the same part of the line");
        }

        /// <summary>
        /// Off a real model: a balustrade (1.5) and a block wall (13.44 here) drawn along one
        /// slab edge, the edge beam carrying the balustrade's 1.5 and the beam beside it the
        /// wall's load. Both lines land on the edge beam, and the wall line lies nearer it -
        /// yet the load there is the balustrade's. The line that asks for what ETABS carries
        /// takes it, whichever is nearer, and the wall line goes to its own beam.
        /// </summary>
        [TestMethod]
        public void TheLineThatAsksForTheLoadTakesItEvenWhenAnotherLiesNearer()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoad_R wall = Line(1, 0, 50, 6000, 50);
            LineLoad_R balustrade = new LineLoad_R(950, "MRG_STR-Line Load-TL5",
                new[] { At(0, 300), At(6000, 300) }, 5, "L03");

            engine.StartComparison(new[] { wall, balustrade },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 1.5)),
                       Frame("B2", 0, 350, 6000, 350, Load("SIDL", 13.44))));

            LineLoadResult rail = engine.Results.Single(result => result.Line == balustrade);
            LineLoadResult block = engine.Results.Single(result => result.Line == wall);

            Assert.AreEqual(LineLoadState.Matched, rail.State, "the 1.5 on the edge beam is the balustrade's");
            Assert.AreEqual(LineLoadState.Matched, block.State, "and the wall goes to the beam that carries it");
            Assert.AreEqual("B2", block.Frames);
            Assert.AreEqual("B1", rail.Frames);
            Assert.AreEqual(0, engine.Unclaimed.Count, "neither beam is left over");
        }

        /// <summary>
        /// Where ETABS carries what they add up to, they are two loads on one beam - a wall and
        /// a facade - and both count, as they always did.
        /// </summary>
        [TestMethod]
        public void TwoLinesWhoseSumETABSCarriesAreStillTwoLoads()
        {
            LineLoad_R near = Line(1, 0, 50, 6000, 50);
            LineLoad_R far = new LineLoad_R(950, "MRG_STR-Line Load-TL1",
                new[] { At(0, 300), At(6000, 300) }, 1, "L03");

            List<LineLoadResult> results = Run(new[] { near, far },
                Frames(Frame("B1", 0, 0, 6000, 0, Load("SIDL", 26.88))));

            Assert.IsTrue(results.All(result => result.State == LineLoadState.Matched));
        }

        [TestMethod]
        public void WithStepsOffTheLineIsJudgedWholeAsItWasBefore()
        {
            PrefrencesData.getPrefrencesData().LineLoadCheckInSteps = false;

            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 3100, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3100, 0, 6000, 0, Load("SIDL", 9.6))));

            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            Assert.AreEqual(0, result.Stretches.Count);
        }

        /// <summary>
        /// On the plan, a line that differs along part of its length is drawn as its stretches,
        /// each filed under its own name, so accepting the bad stretch is not accepting the line.
        /// </summary>
        [TestMethod]
        public void ALineWithStretchesIsDrawnAsThemEachFiledApart()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 3100, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3100, 0, 6000, 0, Load("SIDL", 9.6))));

            List<DiscrepancyScanner.Wpf.ElementViewModel> drawn =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadResult(result).ToList();

            Assert.AreEqual(2, drawn.Count);
            Assert.AreEqual(ResultLevel.Passed, drawn[0].Outcomes.Single().Level);
            Assert.AreEqual(ResultLevel.Failed, drawn[1].Outcomes.Single().Level);
            Assert.AreNotEqual(drawn[0].ReviewKey, drawn[1].ReviewKey);
            Assert.AreEqual(drawn[0].RevitId, drawn[1].RevitId, "both are still the same Revit line");
        }

        #endregion

        #region each ETABS frame on its own

        /// <summary>
        /// A line over three frames is split where they meet and each part is compared with its
        /// own frame - even where two of them carry the same load, they are two frames and two
        /// stretches, each naming its frame.
        /// </summary>
        [TestMethod]
        public void ALineOverThreeFramesIsReadAgainstEachOfThemOnItsOwn()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoadResult result = RunOne(engine,
                Line(1, 0, 0, 9000, 0),
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 13.44)),
                       Frame("B3", 6000, 0, 9000, 0, Load("SIDL", 9.6))));

            Assert.AreEqual(3, engine.Runs.Count, "one run per frame");
            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            CollectionAssert.AreEqual(new[] { "B1", "B2", "B3" },
                                      result.Stretches.Select(one => one.Frames).ToArray());
            CollectionAssert.AreEqual(new[] { 13.44, 13.44, 9.6 },
                                      result.Stretches.Select(one => one.Carried).ToArray());
            Assert.AreEqual(3000, result.Stretches[1].From, 1);
            Assert.AreEqual(6000, result.Stretches[2].From, 1);
            Assert.AreEqual("B1, B2, B3", result.Frames);
            Assert.AreEqual(0, engine.Unclaimed.Count, "every frame is claimed by its part of the line");
        }

        /// <summary>
        /// Never chained. A beam line with a kink in it is two frames, each where it is, rather
        /// than one line straightened across both.
        /// </summary>
        [TestMethod]
        public void FramesAreNeverChainedIntoOneLine()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new LineLoad_R[0],
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 6000, 300, Load("SIDL", 13.44)),
                       Frame("B3", 6000, 300, 9000, 300, Load("SIDL", 13.44))));

            Assert.AreEqual(3, engine.Runs.Count);
            LineLoadRun second = engine.Runs.Single(run => run.Identifier == "B2");
            Assert.AreEqual(300, Math.Max(second.PlanStart.Y, second.PlanEnd.Y), 1e-6);
            Assert.AreEqual(3, engine.Unclaimed.Count, "each frame's load is its own finding");
        }

        /// <summary>
        /// Two beams side by side under one line, both inside the buffer: the line goes to the
        /// one carrying what it asks for, not simply the nearer, and the other's load is left
        /// over for what it is.
        /// </summary>
        [TestMethod]
        public void OfTwoFramesUnderALineItGoesToTheOneCarryingItsLoad()
        {
            LineLoadCompareEngine engine = Engine();
            LineLoadResult result = RunOne(engine,
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("EDGE", 0, 50, 6000, 50, Load("SIDL", 1.5)),
                       Frame("WALL", 0, 300, 6000, 300, Load("SIDL", 13.44))));

            Assert.AreEqual(LineLoadState.Matched, result.State, result.Summary);
            Assert.AreEqual("WALL", result.Frames);
            Assert.AreEqual(1, engine.Unclaimed.Count);
            Assert.AreEqual("EDGE", engine.Unclaimed[0].FrameName);
        }

        /// <summary>
        /// The frame drawn grey under the lines says what it carries, and is named as one frame.
        /// </summary>
        [TestMethod]
        public void TheFrameUnderTheLinesSaysWhatItCarries()
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { Line(5, 0, 0, 6041, 0) },
                Frames(Frame("B1275", 0, 0, 6041, 0, Load("SIDL", 1.5, 2717.7 / 6041, 1)),
                       Frame("B1276", 6041, 0, 9000, 0, Load("SIDL", 1.5))));

            DiscrepancyScanner.Wpf.ElementViewModel partial =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadRun(
                    engine.Runs.Single(run => run.Identifier == "B1275"));
            DiscrepancyScanner.Wpf.ElementViewModel whole =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadRun(
                    engine.Runs.Single(run => run.Identifier == "B1276"));

            StringAssert.StartsWith(partial.Identifier, "ETABS frame B1275");
            Assert.AreEqual("1.5 kN/m from 2.72 to 6.04 m",
                            partial.ResultFields.Single(field => field.Key == LineLoadResultKeys.Actual).Value);
            Assert.AreEqual("1.5 kN/m",
                            whole.ResultFields.Single(field => field.Key == LineLoadResultKeys.Actual).Value);
        }

        /// <summary>
        /// Each stretch's card names the frame under that stretch, not every frame the line
        /// runs over.
        /// </summary>
        [TestMethod]
        public void EachStretchCardNamesItsOwnFrame()
        {
            LineLoadResult result = RunOne(
                Line(1, 0, 0, 6000, 0),
                Frames(Frame("B1", 0, 0, 3000, 0, Load("SIDL", 13.44)),
                       Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 9.6))));

            List<DiscrepancyScanner.Wpf.ElementViewModel> drawn =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadResult(result).ToList();

            Assert.AreEqual(2, drawn.Count);
            Assert.AreEqual("B1", drawn[0].ResultFields.Single(field => field.Key == LineLoadResultKeys.Frames).Value);
            Assert.AreEqual("B2", drawn[1].ResultFields.Single(field => field.Key == LineLoadResultKeys.Frames).Value);
            StringAssert.Contains(drawn[1].PairedWith, "B2");
        }

        #endregion

        #region a loading plan several stories share

        /// <summary>
        /// A typical floor's loading plan paired with two levels. The view is read once, so its
        /// lines come back named after the first level; handed to the check as they were, the
        /// first level was checked twice over and the second had nothing drawn on it, which put
        /// every load on it down as one nobody asked for. Each level gets the lines as its own.
        /// </summary>
        [TestMethod]
        public void AViewPairedWithTwoStoriesIsCheckedOnEachOfThem()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetSchedule("L04", Schedule());
            engine.SetStoryAboveHeight("L04", 3000);

            LineLoad_R read = Line(1, 0, 0, 6000, 0);

            LineLoadFrame_E onL04 = new LineLoadFrame_E("B9", "B9", "L04", At(0, 0), At(6000, 0));
            onL04.Add(Load("SIDL", 13.44));

            engine.StartComparison(new[] { read.OnStory("L03", true), read.OnStory("L04", true) },
                                   new[] { Frame("B1", 0, 0, 6000, 0, Load("SIDL", 13.44)), onL04 });

            Assert.AreEqual(2, engine.Results.Count, "one result for the line on each level");
            CollectionAssert.AreEquivalent(new[] { "L03", "L04" },
                engine.Results.Select(result => result.Line.Story).ToList());
            Assert.IsTrue(engine.Results.All(result => result.State == LineLoadState.Matched));
            Assert.AreEqual("B9", engine.Results.Single(result => result.Line.Story == "L04").Frames,
                            "the line on L04 is read against the frame on L04");
            Assert.AreEqual(0, engine.Unclaimed.Count, "both levels' loads are asked for");
        }

        /// <summary>The copy for another level is the same drawing, only on that level.</summary>
        [TestMethod]
        public void ALineOnAnotherStoryIsTheSameLineThere()
        {
            LineLoad_R read = new LineLoad_R(4711, "MRG_STR-Line Load-TL3",
                                             new[] { At(0, 0), At(0, 0), At(3000, 0), At(3000, 2000) },
                                             3, "L03");

            LineLoad_R copy = read.OnStory("L07", true);

            Assert.AreEqual("L07", copy.Story);
            Assert.IsTrue(copy.IsOnSeveralStories);
            Assert.IsFalse(read.IsOnSeveralStories, "the line it was copied from is left as it was");
            Assert.AreEqual(read.RevitID, copy.RevitID);
            Assert.AreEqual(read.StyleName, copy.StyleName);
            Assert.AreEqual(read.Number, copy.Number);
            Assert.AreEqual(read.Pieces.Count, copy.Pieces.Count);
            Assert.AreEqual(read.Length, copy.Length, 1e-9);
            Assert.AreEqual(3000, copy.Pieces[1].PlanStart.X, 1e-9);
            Assert.AreEqual(2000, copy.Pieces[1].PlanEnd.Y, 1e-9);
        }

        /// <summary>
        /// The same Revit line on two levels is two findings, and each is filed apart: accepting
        /// it on one floor must not accept it on the other, nor be taken back because the other
        /// reads differently. A line on a plan of its own keeps the name it was always filed by.
        /// </summary>
        [TestMethod]
        public void TheSameLineOnTwoStoriesIsFiledOnceForEach()
        {
            LineLoad_R read = Line(1, 0, 0, 6000, 0);

            DiscrepancyScanner.Wpf.ElementViewModel onL03 = DiscrepancyScanner.Wpf.ElementViewModelFactory
                .FromLineLoad(new LineLoadResult(read.OnStory("L03", true)) { State = LineLoadState.Matched });
            DiscrepancyScanner.Wpf.ElementViewModel onL04 = DiscrepancyScanner.Wpf.ElementViewModelFactory
                .FromLineLoad(new LineLoadResult(read.OnStory("L04", true)) { State = LineLoadState.Matched });
            DiscrepancyScanner.Wpf.ElementViewModel alone = DiscrepancyScanner.Wpf.ElementViewModelFactory
                .FromLineLoad(new LineLoadResult(read) { State = LineLoadState.Matched });

            Assert.AreNotEqual(onL03.ReviewKey, onL04.ReviewKey);
            Assert.AreEqual(onL03.RevitId, onL04.RevitId, "both are still the one Revit line");
            Assert.AreEqual("LineLoad|R" + read.RevitID, alone.ReviewKey);
        }

        /// <summary>The same for the stretches of a line checked in steps.</summary>
        [TestMethod]
        public void TheStretchesOfALineOnTwoStoriesAreFiledApartToo()
        {
            LineLoad_R read = Line(1, 0, 0, 6000, 0);
            LineLoadFrame_E[] frames = Frames(Frame("B1", 0, 0, 3100, 0, Load("SIDL", 13.44)),
                                              Frame("B2", 3100, 0, 6000, 0, Load("SIDL", 9.6)));

            List<string> keys = new[] { "L03", "L04" }
                .Select(story =>
                {
                    LineLoad_R onStory = read.OnStory(story, true);
                    LineLoadFrame_E[] there = frames
                        .Select(frame => new LineLoadFrame_E(frame.UniqueName + story, frame.UniqueName, story,
                                                             frame.PlanStart, frame.PlanEnd))
                        .ToArray();
                    there[0].Add(Load("SIDL", 13.44));
                    there[1].Add(Load("SIDL", 9.6));

                    LineLoadCompareEngine engine = Engine();
                    engine.SetSchedule("L04", Schedule());
                    engine.SetStoryAboveHeight("L04", 3000);
                    return RunOne(engine, onStory, there);
                })
                .SelectMany(DiscrepancyScanner.Wpf.ElementViewModelFactory.FromLineLoadResult)
                .Select(element => element.ReviewKey)
                .ToList();

            Assert.AreEqual(4, keys.Count, "two stretches on each of two levels");
            Assert.AreEqual(4, keys.Distinct().Count(), "each filed under a name of its own");
        }

        /// <summary>
        /// A pairing set by hand on the second level a plan is shared by is held against the
        /// line on that level, not against whichever level the view was read for first - which
        /// would read as the pairing being between two different floors, and drop it.
        /// </summary>
        [TestMethod]
        public void ALineOnASharedPlanIsInReachOfEveryStoryItIsOn()
        {
            MyTransformation alignmentBefore = DataContainer.ModelTransformation;
            DataContainer.ModelTransformation = new MyTransformation();

            try
            {
                LineLoad_R read = Line(1, 0, 0, 6000, 0);
                PairPlace frameOnL04 = PairPlace.OfEtabs("L04", At(0, 0), At(6000, 0));

                Assert.IsTrue(PairPlace.OnOneStory(frameOnL04, PairPlace.Of(read.OnStory("L03", true))));
                Assert.IsFalse(PairPlace.OnOneStory(frameOnL04, PairPlace.Of(read)),
                               "a line on a plan of its own is still on its own story");
            }
            finally
            {
                DataContainer.ModelTransformation = alignmentBefore;
            }
        }

        #endregion

        #region building the model under test

        private static LineLoadCompareEngine Engine()
        {
            LineLoadCompareEngine engine = new LineLoadCompareEngine();
            engine.SetSchedule("L03", Schedule());

            // A three metre story above, which leaves 2.8m of wall once the allowance is off.
            engine.SetStoryAboveHeight("L03", 3000);

            return engine;
        }

        /// <summary>The rows of the reference sheet the tests lean on.</summary>
        private static LineLoadSchedule Schedule()
        {
            LineLoadSchedule schedule = new LineLoadSchedule("", "Line Load D (TOWER)");

            schedule.Add(new LineLoadEntry(1, "ALLOWANCE FOR 200mm THICK SOLID BLOCK WALL",
                                           4.8, false, "4.8"));
            schedule.Add(new LineLoadEntry(3, "ALLOWANCE FOR 100mm THICK SOLID BLOCK WALL",
                                           2.4, false, "2.4"));
            schedule.Add(new LineLoadEntry(5, "ALLOWANCE FOR GLASS BALUSTRADE / RAILING",
                                           1.5, true, "1.50 kN/m'"));
            schedule.Add(new LineLoadEntry(7, "ALLOWANCE FOR FULL HEIGHT FAÇADE GLAZING SYSTEM",
                                           1.0, false, "1"));

            return schedule;
        }

        /// <summary>
        /// The reference sheet with the rows of the recess on B3078: the GRC read per square
        /// metre of elevation and not full height, so 5.4 where the glazing is 5.6. And two
        /// rows that are the recess's values as they stand, for testing the buffer on its own.
        /// </summary>
        private static LineLoadSchedule RecessSchedule()
        {
            LineLoadSchedule schedule = Schedule();

            schedule.Add(new LineLoadEntry(4, "ALLOWANCE FOR GRC PANELS, ACP PANELS INCLUDING FIXING DETAILS",
                                           1.0, false, "1"));
            schedule.Add(new LineLoadEntry(8, "BLOCK WALL, AS ROUNDED", 26, true, "26"));
            schedule.Add(new LineLoadEntry(9, "GRC PANELS, AS WORKED OUT", 5.4, true, "5.4"));

            return schedule;
        }

        /// <summary>A drawn line of its own Revit id, for a test with two lines of one number.</summary>
        private static LineLoad_R Drawn(long revitId, int number, params MyPoint[] points)
        {
            return new LineLoad_R(revitId, "MRG_STR-Line Load-TL" + number, points, number, "L03");
        }

        private static List<LineLoadResult> Run(IList<LineLoad_R> lines, IList<LineLoadFrame_E> frames)
        {
            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(lines, frames);
            return engine.Results;
        }

        private static LineLoadResult RunOne(LineLoad_R line, IList<LineLoadFrame_E> frames)
        {
            List<LineLoadResult> results = Run(new[] { line }, frames);
            Assert.AreEqual(1, results.Count, "one line in, one result out");
            return results[0];
        }

        /// <summary>The same, against an engine the test has already set something on.</summary>
        private static LineLoadResult RunOne(LineLoadCompareEngine engine, LineLoad_R line,
                                             IList<LineLoadFrame_E> frames)
        {
            engine.StartComparison(new[] { line }, frames);
            Assert.AreEqual(1, engine.Results.Count, "one line in, one result out");
            return engine.Results[0];
        }

        private static LineLoad_R Line(int number, double x1, double y1, double x2, double y2)
        {
            return new LineLoad_R(900 + number, "MRG_STR-Line Load-TL" + number,
                                  new[] { At(x1, y1), At(x2, y2) }, number, "L03");
        }

        private static LineLoadFrame_E[] Frames(params LineLoadFrame_E[] frames)
        {
            return frames;
        }

        private static LineLoadFrame_E Frame(string name, double x1, double y1, double x2, double y2,
                                             params LineLoad_E[] loads)
        {
            LineLoadFrame_E frame = new LineLoadFrame_E(name, name, "L03", At(x1, y1), At(x2, y2));

            foreach (LineLoad_E load in loads)
            {
                frame.Add(load);
            }

            return frame;
        }

        /// <summary>A uniform downward load in the SDL pattern, over all or part of a frame.</summary>
        private static LineLoad_E Load(string pattern, double perMetre, double from = 0, double to = 1)
        {
            return new LineLoad_E(pattern, perMetre, perMetre, from, to, 10, true, false);
        }

        private static MyPoint At(double x, double y)
        {
            return new MyPoint(x, y, 0);
        }

        #endregion
    }
}
