using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Writing a drawn line load back to the design model: where on which frame the check laid
    /// the line, what the frame is to carry there, and what becomes of the loads it carried
    /// before. All of it is decided before ETABS is touched, so none of it needs ETABS open.
    /// </summary>
    [TestClass]
    public class LineLoadWriteBackTests
    {
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadStyleNamePatterns = "Line Load";
            pref.LineLoadHeightReduction = 200;
            pref.LineLoadRoundUpTo = 0;
            pref.LineLoadLocationTolerance = 400;
            pref.LineLoadSearchRadius = 1500;
            pref.LineLoadAngleTolerance = 20;
            pref.LineLoadMinimumIssueLength = 250;
            pref.LineLoadTolerance = 0.2;
            pref.LineLoadCheckInSteps = true;
            pref.LineLoadStepLength = 250;
            pref.LineLoadPatternNames = "SIDL";
            pref.ExcludedLoadPatternNames = "SIDL_C";
            pref.RunLineLoadCheck = true;
        }

        #region where the check laid the line

        [TestMethod]
        public void ALineOnPartOfAFrameIsPlacedOverThatPartByTheFramesUniqueName()
        {
            LineLoadResult result = RunOne(Line(5, 1000, 0, 4000, 0),
                                           Frame("B1", 0, 0, 6000, 0));

            Assert.IsTrue(result.CanWriteBack);
            LineLoadPlacement placement = result.Placements.Single();
            Assert.AreEqual("B1", placement.FrameUniqueName);
            Assert.AreEqual(1d / 6, placement.RelativeFrom, 1e-6);
            Assert.AreEqual(4d / 6, placement.RelativeTo, 1e-6);
            Assert.AreEqual(1.5, placement.Value, 1e-9);
            Assert.AreEqual(6000, placement.FrameLength, 1e-6);
        }

        /// <summary>
        /// ETABS measures a load from the frame's I end, whichever way the frame was drawn, so a
        /// frame drawn right to left has the same stretch counted from the other end.
        /// </summary>
        [TestMethod]
        public void TheStretchIsMeasuredFromTheFramesIEnd()
        {
            LineLoadResult result = RunOne(Line(5, 1000, 0, 4000, 0),
                                           Frame("B1", 6000, 0, 0, 0));

            LineLoadPlacement placement = result.Placements.Single();
            Assert.AreEqual(2d / 6, placement.RelativeFrom, 1e-6);
            Assert.AreEqual(5d / 6, placement.RelativeTo, 1e-6);
        }

        [TestMethod]
        public void ALineOverTwoFramesIsPlacedOnBoth()
        {
            LineLoadResult result = RunOne(Line(5, 0, 0, 6000, 0),
                                           Frame("B1", 0, 0, 3000, 0),
                                           Frame("B2", 3000, 0, 6000, 0));

            CollectionAssert.AreEquivalent(new[] { "B1", "B2" },
                                           result.Placements.Select(one => one.FrameUniqueName).ToList());
            foreach (LineLoadPlacement placement in result.Placements)
            {
                Assert.AreEqual(0, placement.RelativeFrom, 1e-6);
                Assert.AreEqual(1, placement.RelativeTo, 1e-6);
            }
        }

        /// <summary>Only a paired load can go back: a line with nothing under it has nowhere to go.</summary>
        [TestMethod]
        public void ALineWithNoFrameUnderItCannotBeWrittenBack()
        {
            LineLoadResult result = RunOne(Line(5, 0, 20000, 6000, 20000),
                                           Frame("B1", 0, 0, 6000, 0));

            Assert.AreEqual(0, result.Placements.Count);
            Assert.IsFalse(result.CanWriteBack);
        }

        #endregion

        #region what ETABS carries is read as it is

        /// <summary>
        /// A line over two frames carrying 1.5 and 3 is read as carrying 1.5 and 3 - never as
        /// the 2.25 they average to, which no frame carries. Checked in steps and whole alike.
        /// </summary>
        [TestMethod]
        public void TwoFramesLoadedDifferentlyAreNeverAveraged()
        {
            foreach (bool inSteps in new[] { true, false })
            {
                PrefrencesData.getPrefrencesData().LineLoadCheckInSteps = inSteps;

                LineLoadResult result = RunOne(Line(5, 0, 0, 6000, 0),
                                               Frame("B1", 0, 0, 3000, 0, Load("SIDL", 1.5)),
                                               Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 3)));

                Assert.AreEqual(LineLoadState.ValueMismatch, result.State, "steps " + inSteps);
                Assert.AreEqual(3, result.Actual, 1e-9, "the value that is wrong, not the average; steps " + inSteps);
                Assert.AreEqual("1.5 / 3 kN/m", result.CarriedText, "steps " + inSteps);
                Assert.AreEqual("1.5 kN/m on B1, 3 kN/m on B2", result.CarriedByFrameText, "steps " + inSteps);
                Assert.IsFalse(result.Summary.Contains("2.25"), result.Summary);
            }
        }

        [TestMethod]
        public void TwoLoadsOnOneFrameAreBothReported()
        {
            LineLoadResult result = RunOne(Line(5, 0, 0, 6000, 0),
                                           Frame("B1", 0, 0, 6000, 0, Load("SIDL", 1.5, 0, 0.5), Load("SIDL", 3, 0.5, 1)));

            Assert.AreEqual(3, result.Actual, 1e-9);
            Assert.AreEqual("1.5 / 3 kN/m on B1", result.CarriedByFrameText);
        }

        [TestMethod]
        public void OneValueIsReadAsThatValue()
        {
            LineLoadResult result = RunOne(Line(5, 0, 0, 6000, 0),
                                           Frame("B1", 0, 0, 3000, 0, Load("SIDL", 1.5)),
                                           Frame("B2", 3000, 0, 6000, 0, Load("SIDL", 1.5)));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(1.5, result.Actual, 1e-9);
            Assert.AreEqual("1.5 kN/m", result.CarriedText);
        }

        #endregion

        #region what the frame is to carry

        [TestMethod]
        public void AStretchEndingJustShortOfTheFramesEndIsRunToIt()
        {
            LineLoadResult line = Placed("B1", 6000, 0.02, 0.6, 4.8);

            FrameLoadSegment segment = LineLoadWriteBack.SegmentsOn("B1", line, null, 250).Single();

            Assert.AreEqual(0, segment.From, 1e-9, "120 mm short is inside the 250 mm snap");
            Assert.AreEqual(0.6, segment.To, 1e-9, "2.4 m short is not");
            Assert.AreEqual(4.8, segment.Value, 1e-9);
        }

        /// <summary>
        /// The check adds lines drawn over the same stretch together, so what is written there is
        /// the two together - otherwise writing one would make the other read as wrong.
        /// </summary>
        [TestMethod]
        public void AnotherLineOverTheSameStretchIsAddedIn()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 1, 4.8);
            LineLoadResult other = Placed("B1", 6000, 0.5, 1, 2.4);
            LineLoadResult elsewhere = Placed("B2", 6000, 0, 1, 9);

            List<FrameLoadSegment> segments = LineLoadWriteBack.SegmentsOn(
                "B1", mine, new[] { mine, other, elsewhere }, 0);

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(0, segments[0].From, 1e-9);
            Assert.AreEqual(0.5, segments[0].To, 1e-9);
            Assert.AreEqual(4.8, segments[0].Value, 1e-9);
            Assert.AreEqual(0.5, segments[1].From, 1e-9);
            Assert.AreEqual(1, segments[1].To, 1e-9);
            Assert.AreEqual(7.2, segments[1].Value, 1e-9);
        }

        /// <summary>
        /// Two loads end to end on one frame are never left averaged: writing either one puts
        /// both on, each at its own value over its own stretch.
        /// </summary>
        [TestMethod]
        public void LinesEndToEndAreEachWrittenAtTheirOwnValue()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 0.5, 4.8);
            LineLoadResult other = Placed("B1", 6000, 0.5, 1, 2.4);

            List<FrameLoadSegment> segments = LineLoadWriteBack.SegmentsOn("B1", mine, new[] { mine, other }, 250);

            Assert.AreEqual(2, segments.Count);
            AssertSegment(segments[0], 0, 0.5, 4.8);
            AssertSegment(segments[1], 0.5, 1, 2.4);
        }

        [TestMethod]
        public void PartlyOverlappingLinesAreWrittenOverBothWholeStretches()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 4d / 6, 4.8);
            LineLoadResult other = Placed("B1", 6000, 2d / 6, 1, 2.4);

            List<FrameLoadSegment> segments = LineLoadWriteBack.SegmentsOn("B1", mine, new[] { mine, other }, 250);

            Assert.AreEqual(3, segments.Count);
            AssertSegment(segments[0], 0, 2d / 6, 4.8);
            AssertSegment(segments[1], 2d / 6, 4d / 6, 7.2);
            AssertSegment(segments[2], 4d / 6, 1, 2.4);
        }

        /// <summary>
        /// A gap between two lines too short to report is closed: the averaged load is taken off
        /// it too, and nothing is put there.
        /// </summary>
        [TestMethod]
        public void AGapBetweenLinesShorterThanTheSnapIsClearedAndLeftEmpty()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 0.49, 4.8);
            LineLoadResult other = Placed("B1", 6000, 0.51, 1, 2.4);

            List<FrameLoadSegment> segments = LineLoadWriteBack.SegmentsOn("B1", mine, new[] { mine, other }, 250);

            Assert.AreEqual(3, segments.Count);
            AssertSegment(segments[1], 0.49, 0.51, 0);

            List<FrameDistributedLoad> sidl = LineLoadWriteBack.Rewrite(
                new[] { Gravity("SIDL", 3.6, 0, 1) }, "SIDL", LineLoadPatterns.From("SIDL"), segments, 1)["SIDL"];

            Assert.AreEqual(2, sidl.Count, "the old load is gone from all of it, and the gap gets nothing");
            AssertLoad(sidl[0], 0, 0.49, 4.8, 4.8);
            AssertLoad(sidl[1], 0.51, 1, 2.4, 2.4);
        }

        /// <summary>The frame is written whole, so a line on it with no value has to be settled first.</summary>
        [TestMethod]
        public void ALineWithNoValueOnTheSameFrameIsFound()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 0.5, 4.8);
            LineLoadResult unvalued = Placed("B1", 6000, 0.5, 1, 0);
            LineLoadResult elsewhere = Placed("B2", 6000, 0, 1, 0);

            List<LineLoadResult> found = LineLoadWriteBack.LinesWithoutValueOn(
                "B1", mine, new[] { mine, unvalued, elsewhere });

            Assert.AreSame(unvalued, found.Single());
        }

        [TestMethod]
        public void NothingIsWrittenOnAFrameTheLineIsNotOn()
        {
            LineLoadResult mine = Placed("B1", 6000, 0, 1, 4.8);

            Assert.AreEqual(0, LineLoadWriteBack.SegmentsOn("B2", mine, null, 250).Count);
        }

        #endregion

        #region what becomes of what the frame carried

        [TestMethod]
        public void TheLoadIsReplacedOverTheStretchAndKeptEverywhereElse()
        {
            List<FrameDistributedLoad> before = new List<FrameDistributedLoad>
            {
                Gravity("SIDL", 3, 0, 1),
                Gravity("LIVE", 2, 0, 1),
                new FrameDistributedLoad("SIDL", 2, "Global", 10, 0, 1, 0.5, 0.5),
                new FrameDistributedLoad("SIDL", 1, "Local", 2, 0, 1, 7, 7)
            };

            Dictionary<string, List<FrameDistributedLoad>> after = LineLoadWriteBack.Rewrite(
                before, "SIDL", LineLoadPatterns.From("SIDL"),
                new[] { new FrameLoadSegment(0.25, 0.75, 5) }, 1);

            Assert.IsFalse(after.ContainsKey("LIVE"), "a pattern the check does not read is not touched");

            List<FrameDistributedLoad> sidl = after["SIDL"];
            List<FrameDistributedLoad> down = sidl.Where(load => load.PressesDown).ToList();
            Assert.AreEqual(3, down.Count);
            AssertLoad(down[0], 0, 0.25, 3, 3);
            AssertLoad(down[1], 0.25, 0.75, 5, 5);
            AssertLoad(down[2], 0.75, 1, 3, 3);
            Assert.AreEqual(FrameDistributedLoad.GravityDirection, down[1].Direction);

            Assert.AreEqual(1, sidl.Count(load => load.LoadType == 2), "the moment goes back as it was");
            Assert.AreEqual(1, sidl.Count(load => load.Direction == 2), "so does a load in a local direction");
        }

        /// <summary>
        /// A downward load in another pattern the check reads would still be counted on top of
        /// what was written, so it comes off the same stretch.
        /// </summary>
        [TestMethod]
        public void AnotherPatternTheCheckReadsIsTakenOffTheStretch()
        {
            List<FrameDistributedLoad> before = new List<FrameDistributedLoad>
            {
                Gravity("SIDL", 3, 0, 1),
                new FrameDistributedLoad("SDL", 1, "Global", 6, 0, 1, -1, -1),
                Gravity("SDL2", 1, 0, 0.2)
            };

            Dictionary<string, List<FrameDistributedLoad>> after = LineLoadWriteBack.Rewrite(
                before, "SIDL", LineLoadPatterns.From("SIDL, SDL, SDL2"),
                new[] { new FrameLoadSegment(0.5, 1, 5) }, 1);

            CollectionAssert.AreEquivalent(new[] { "SIDL", "SDL" }, after.Keys.ToList(),
                                           "SDL2 lies wholly outside the stretch and is left alone");

            FrameDistributedLoad sdl = after["SDL"].Single();
            AssertLoad(sdl, 0, 0.5, -1, -1);
            Assert.AreEqual(6, sdl.Direction, "what is kept keeps its own direction and sign");
        }

        [TestMethod]
        public void ATaperedLoadIsCutWhereTheStretchStarts()
        {
            Dictionary<string, List<FrameDistributedLoad>> after = LineLoadWriteBack.Rewrite(
                new[] { new FrameDistributedLoad("SIDL", 1, "Global", 10, 0, 1, 0, 10) },
                "SIDL", LineLoadPatterns.From("SIDL"),
                new[] { new FrameLoadSegment(0.6, 1, 4) }, 1);

            List<FrameDistributedLoad> sidl = after["SIDL"];
            Assert.AreEqual(2, sidl.Count);
            AssertLoad(sidl[0], 0, 0.6, 0, 6);
            AssertLoad(sidl[1], 0.6, 1, 4, 4);
        }

        [TestMethod]
        public void TheValueIsWrittenInTheModelsUnits()
        {
            Dictionary<string, List<FrameDistributedLoad>> after = LineLoadWriteBack.Rewrite(
                new List<FrameDistributedLoad>(), "SIDL", LineLoadPatterns.From("SIDL"),
                new[] { new FrameLoadSegment(0, 1, 5) }, 1000d / 9806.65);

            AssertLoad(after["SIDL"].Single(), 0, 1, 5000d / 9806.65, 5000d / 9806.65);
        }

        #endregion

        #region which pattern

        [TestMethod]
        public void ThePatternTheFrameIsAlreadyLoadedInIsWrittenTo()
        {
            string problem;
            string pattern = LineLoadWriteBack.PatternToWrite(
                new[] { Gravity("LIVE", 9, 0, 1), Gravity("Super Dead", 3, 0, 1) },
                new[] { "DEAD", "SIDL", "Super Dead", "LIVE" },
                LineLoadPatterns.From("SIDL, Super Dead"), out problem);

            Assert.AreEqual("Super Dead", pattern);
            Assert.IsNull(problem);
        }

        [TestMethod]
        public void AnUnloadedFrameTakesTheFirstPatternTheSettingsNameWhole()
        {
            string problem;
            Assert.AreEqual("SIDL", LineLoadWriteBack.PatternToWrite(
                new FrameDistributedLoad[0], new[] { "SIDL_C", "SIDL-FINISHES", "SIDL" },
                LineLoadPatterns.From("SIDL", "SIDL_C"), out problem));

            Assert.AreEqual("SIDL-FINISHES", LineLoadWriteBack.PatternToWrite(
                new FrameDistributedLoad[0], new[] { "SIDL_C", "SIDL-FINISHES" },
                LineLoadPatterns.From("SIDL", "SIDL_C"), out problem),
                "with no whole match, the first the check would read");
        }

        [TestMethod]
        public void AModelWithNoPatternTheCheckReadsIsSaidSo()
        {
            string problem;
            Assert.IsNull(LineLoadWriteBack.PatternToWrite(
                new FrameDistributedLoad[0], new[] { "DEAD", "LIVE" },
                LineLoadPatterns.From("SIDL"), out problem));
            StringAssert.Contains(problem, "SIDL");

            Assert.IsNull(LineLoadWriteBack.PatternToWrite(
                new FrameDistributedLoad[0], new[] { "DEAD", "LIVE" },
                LineLoadPatterns.From(""), out problem));
            Assert.IsNotNull(problem, "with no pattern named there is nothing to say which to use");
        }

        #endregion

        #region helpers

        private static LineLoadResult RunOne(LineLoad_R line, params LineLoadFrame_E[] frames)
        {
            LineLoadCompareEngine engine = new LineLoadCompareEngine();
            LineLoadSchedule schedule = new LineLoadSchedule("", "Line Load D (TOWER)");
            schedule.Add(new LineLoadEntry(5, "ALLOWANCE FOR GLASS BALUSTRADE / RAILING", 1.5, true, "1.50 kN/m'"));
            engine.SetSchedule("L03", schedule);
            engine.SetStoryAboveHeight("L03", 3000);

            engine.StartComparison(new[] { line }, frames);
            Assert.AreEqual(1, engine.Results.Count, "one line in, one result out");
            return engine.Results[0];
        }

        private static LineLoad_R Line(int number, double x1, double y1, double x2, double y2)
        {
            return new LineLoad_R(900 + number, "MRG_STR-Line Load-TL" + number,
                                  new[] { new MyPoint(x1, y1, 0), new MyPoint(x2, y2, 0) }, number, "L03");
        }

        private static LineLoadFrame_E Frame(string name, double x1, double y1, double x2, double y2,
                                             params LineLoad_E[] loads)
        {
            LineLoadFrame_E frame = new LineLoadFrame_E(name, name, "L03", new MyPoint(x1, y1, 0), new MyPoint(x2, y2, 0));
            foreach (LineLoad_E load in loads)
            {
                frame.Add(load);
            }
            return frame;
        }

        private static LineLoad_E Load(string pattern, double perMetre, double from = 0, double to = 1)
        {
            return new LineLoad_E(pattern, perMetre, perMetre, from, to, 10, true, false);
        }

        /// <summary>A result laid on one frame by hand, for the write-back's own arithmetic.</summary>
        private static LineLoadResult Placed(string frame, double frameLength, double from, double to, double value)
        {
            LineLoadResult result = new LineLoadResult(null);
            result.Placements.Add(new LineLoadPlacement(frame, frame, from, to, value, frameLength));
            return result;
        }

        private static FrameDistributedLoad Gravity(string pattern, double value, double from, double to)
        {
            return new FrameDistributedLoad(pattern, 1, "Global", 10, from, to, value, value);
        }

        private static void AssertSegment(FrameLoadSegment segment, double from, double to, double value)
        {
            Assert.AreEqual(from, segment.From, 1e-9, "from");
            Assert.AreEqual(to, segment.To, 1e-9, "to");
            Assert.AreEqual(value, segment.Value, 1e-9, "value");
        }

        private static void AssertLoad(FrameDistributedLoad load, double from, double to,
                                       double valueFrom, double valueTo)
        {
            Assert.AreEqual(from, load.RelativeFrom, 1e-9, "from");
            Assert.AreEqual(to, load.RelativeTo, 1e-9, "to");
            Assert.AreEqual(valueFrom, load.ValueFrom, 1e-9, "value at the start");
            Assert.AreEqual(valueTo, load.ValueTo, 1e-9, "value at the end");
        }

        #endregion
    }
}
