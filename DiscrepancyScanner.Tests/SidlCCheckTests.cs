using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The SIDL_C check: the walls and fill strips the wall and slab checks leave out, weighed
    /// as line loads and read against the SIDL_C loads on the frames.
    ///
    /// The comparison itself is the line load check's and has its own tests. What is pinned
    /// here is what is different: where the value comes from, the band that lets a load
    /// through heavy far more readily than light, the pattern read, and a tab of its own that
    /// is never up beside the line loads.
    /// </summary>
    [TestClass]
    public class SidlCCheckTests
    {
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadSearchRadius = 1500;
            pref.LineLoadLocationTolerance = 400;
            pref.LineLoadAngleTolerance = 20;
            pref.LineLoadMinimumIssueLength = 250;
            pref.LineLoadTolerance = 0.2;
            pref.LineLoadCheckInSteps = true;
            pref.LineLoadStepLength = 250;
            pref.RunLineLoadCheck = true;
            pref.RunSidlCCheck = true;
            pref.SidlCToleranceBelow = 0.3;
            pref.SidlCToleranceAbove = 3;
        }

        #region what a wall weighs

        /// <summary>
        /// A 200 thick upstand 1.2 m high is 0.2 x 1.2 x 25 = 6 kN/m, and the card says how.
        /// </summary>
        [TestMethod]
        public void AWallWeighsItsThicknessTimesItsHeightTimesTheDensity()
        {
            LineLoad_R line = Upstand(thickness: 200, height: 1200).ToLineLoad(25, 0.1);

            Assert.AreEqual(6.0, line.OwnPerMetre, 1e-9);
            Assert.IsTrue(line.HasOwnValue);
            StringAssert.Contains(line.OwnHow, "200 mm thick");
            StringAssert.Contains(line.OwnHow, "1.20 m high");
            StringAssert.Contains(line.OwnHow, "25 kN/m3");
        }

        /// <summary>Rounded up the way a plan line load is, so a 5.04 is checked as 5.1.</summary>
        [TestMethod]
        public void TheWeightIsRoundedUpLikeAPlanLineLoad()
        {
            // 0.2 x 1.008 x 25 = 5.04
            LineLoad_R line = Upstand(thickness: 200, height: 1008).ToLineLoad(25, 0.1);

            Assert.AreEqual(5.1, line.OwnPerMetre, 1e-9);
            StringAssert.Contains(line.OwnHow, "rounded up to 5.1");
        }

        /// <summary>
        /// A fill strip weighs its depth times its width, and the width is the strip's short
        /// side whichever way it runs.
        /// </summary>
        [TestMethod]
        public void AFillStripWeighsItsDepthTimesItsWidth()
        {
            SidlCSource_R strip = new SidlCSource_R(SidlCSourceKind.FillSlab, 77,
                new[] { At(0, 0), At(5000, 0) }, 150, 400, 0) { TypeName = "Fill SLAB-150" };

            // 0.15 x 0.4 x 25 = 1.5
            Assert.AreEqual(1.5, strip.ToLineLoad(25, 0).OwnPerMetre, 1e-9);
            Assert.AreEqual("fill strip Fill SLAB-150", strip.Description);
        }

        /// <summary>
        /// A wall standing on L03 and rising to L04 is a load on L03's frames, whatever it rises
        /// to - which is the other way round from the wall check, which names a wall after the
        /// story it rises to.
        /// </summary>
        [TestMethod]
        public void AWallLoadsTheStoryItStandsOn()
        {
            SidlCSource_R wall = Upstand(thickness: 200, height: 2800, baseElevation: 3010);
            wall.AssignStory(new List<Story_E>
            {
                new Story_E("L03", 3000, Form1.eLength.mm),
                new Story_E("L04", 6000, Form1.eLength.mm)
            });

            Assert.AreEqual("L03", wall.Story);
        }

        #endregion

        #region a strip of fill read as a line

        /// <summary>
        /// The line down a strip is its long axis, and its width the short side - for a strip
        /// drawn on the skew as much as for one square to the grid.
        /// </summary>
        [TestMethod]
        public void AStripsLineRunsDownItsMiddle()
        {
            // 300 wide, 4000 long, at 30 degrees.
            double c = System.Math.Cos(System.Math.PI / 6), s = System.Math.Sin(System.Math.PI / 6);
            List<MyPoint> outline = new List<MyPoint>
            {
                Rotated(0, 0, c, s), Rotated(4000, 0, c, s), Rotated(4000, 300, c, s), Rotated(0, 300, c, s)
            };

            MyPoint start, end;
            double width;
            Assert.IsTrue(SidlCSource_R.StripAxis(outline, 300 * 4000, out start, out end, out width));

            Assert.AreEqual(300, width, 1e-6);
            Assert.AreEqual(4000, WallGeometry.PlanLength(start, end), 1e-6);

            MyPoint middle = Rotated(2000, 150, c, s);
            Assert.AreEqual(middle.X, (start.X + end.X) / 2, 1e-6);
            Assert.AreEqual(middle.Y, (start.Y + end.Y) / 2, 1e-6);
        }

        /// <summary>
        /// An L of fill has no one line down it, and a guess at one would put its weight where
        /// it is not. It is turned away instead, and the read says so.
        /// </summary>
        [TestMethod]
        public void AnLShapedStripHasNoLine()
        {
            List<MyPoint> outline = new List<MyPoint>
            {
                At(0, 0), At(3000, 0), At(3000, 300), At(300, 300), At(300, 3000), At(0, 3000)
            };
            double area = 3000 * 300 + 2700 * 300;

            MyPoint start, end;
            double width;
            Assert.IsFalse(SidlCSource_R.StripAxis(outline, area, out start, out end, out width));
        }

        #endregion

        #region stepped up to the intervals

        /// <summary>With 2, 6, 10, 14, 18: 3 is checked as 6, 6 as 6, 1 as 2, and 20 as 20.</summary>
        [TestMethod]
        public void TheWeightIsSteppedUpToTheNextInterval()
        {
            List<double> intervals = SidlCSource_R.ParseIntervals("2,6,10,14,18");

            Assert.AreEqual(6, SidlCSource_R.StepUp(3, intervals), 1e-9);
            Assert.AreEqual(6, SidlCSource_R.StepUp(6, intervals), 1e-9, "on an interval stays on it");
            Assert.AreEqual(2, SidlCSource_R.StepUp(1, intervals), 1e-9);
            Assert.AreEqual(20, SidlCSource_R.StepUp(20, intervals), 1e-9, "past the last, as it is");
            Assert.AreEqual(3, SidlCSource_R.StepUp(3, new List<double>()), 1e-9, "no intervals, as it is");
        }

        [TestMethod]
        public void TheIntervalsAreReadHoweverTheyAreTyped()
        {
            CollectionAssert.AreEqual(new[] { 2.0, 6, 10, 14, 18 },
                                      SidlCSource_R.ParseIntervals(" 10, 2;6  18,14, x, -3, 0 ").ToArray());
        }

        /// <summary>A 200 upstand 600 high is 3 kN/m, checked as 6, and the card says so.</summary>
        [TestMethod]
        public void AWallIsCheckedAtItsInterval()
        {
            LineLoad_R line = Upstand(thickness: 200, height: 600)
                .ToLineLoad(25, 0.1, SidlCSource_R.ParseIntervals("2,6,10,14,18"), false);

            Assert.AreEqual(6, line.OwnPerMetre, 1e-9);
            StringAssert.Contains(line.OwnHow, "= 3 kN/m, taken as the next interval, 6");
        }

        [TestMethod]
        public void AWallPastTheLastIntervalIsCheckedAsItIs()
        {
            // 0.3 x 3 x 25 = 22.5
            LineLoad_R line = Upstand(thickness: 300, height: 3000)
                .ToLineLoad(25, 0.1, SidlCSource_R.ParseIntervals("2,6,10,14,18"), false);

            Assert.AreEqual(22.5, line.OwnPerMetre, 1e-9);
            StringAssert.Contains(line.OwnHow, "above the last interval so taken as it is");
        }

        #endregion

        #region taken from the instance comment

        [TestMethod]
        public void ACommentStatesALoadOnlyWhenItIsPlainlyOne()
        {
            Assert.AreEqual(6, Stated("6"), 1e-9);
            Assert.AreEqual(6.5, Stated(" 6.5 "), 1e-9);
            Assert.AreEqual(6.5, Stated("6,5"), 1e-9);
            Assert.AreEqual(7, Stated("7 kN/m"), 1e-9);
            Assert.AreEqual(4.2, Stated("SIDL_C 4.2kN/m upstand"), 1e-9);

            Assert.AreEqual(0, Stated(""), "nothing");
            Assert.AreEqual(0, Stated("cast later"), "words");
            Assert.AreEqual(0, Stated("level 2 upstand"), "a number that is not a load");
            Assert.AreEqual(0, Stated("5 kN/m2"), "an area load is not a line load");
            Assert.AreEqual(0, Stated("0"), "nought is no load");
        }

        /// <summary>The comment's load, as it is - neither rounded nor stepped.</summary>
        [TestMethod]
        public void TheCommentsLoadIsTakenAsItIs()
        {
            SidlCSource_R wall = Upstand(thickness: 200, height: 600);
            wall.Comment = "7.3 kN/m";

            LineLoad_R line = wall.ToLineLoad(25, 0.1, SidlCSource_R.ParseIntervals("2,6,10,14,18"), true);

            Assert.AreEqual(7.3, line.OwnPerMetre, 1e-9);
            StringAssert.Contains(line.OwnHow, "from its instance comment \"7.3 kN/m\"");
            Assert.AreEqual(7.3, wall.Weighs(25, true), 1e-9);
        }

        /// <summary>No load in the comment: the geometry, stepped up as usual, and the card says why.</summary>
        [TestMethod]
        public void WithNoLoadInTheCommentTheGeometryIsUsed()
        {
            SidlCSource_R wall = Upstand(thickness: 200, height: 600);
            wall.Comment = "cast later";

            LineLoad_R line = wall.ToLineLoad(25, 0.1, SidlCSource_R.ParseIntervals("2,6,10,14,18"), true);

            Assert.AreEqual(6, line.OwnPerMetre, 1e-9);
            StringAssert.Contains(line.OwnHow, "its instance comment states no load");
            Assert.AreEqual(3, wall.Weighs(25, true), 1e-9);
        }

        /// <summary>With the comment path off, a comment's load is not used.</summary>
        [TestMethod]
        public void TheCommentIsOnlyReadWhenAskedFor()
        {
            SidlCSource_R wall = Upstand(thickness: 200, height: 600);
            wall.Comment = "9";

            Assert.AreEqual(3, wall.ToLineLoad(25, 0.1, null, false).OwnPerMetre, 1e-9);
        }

        private static double Stated(string comment)
        {
            double load;
            return SidlCSource_R.TryReadLoad(comment, out load) ? load : 0;
        }

        #endregion

        #region the band

        /// <summary>
        /// ETABS may carry up to three over the weight and still agree - a load modelled heavy is
        /// on the safe side.
        /// </summary>
        [TestMethod]
        public void ALoadUpToThreeHeavyAgrees()
        {
            Assert.AreEqual(LineLoadState.Matched, RunOne(6.0, 8.9).State);
            Assert.AreEqual(LineLoadState.Matched, RunOne(6.0, 9.0).State, "the edge of the band is in it");
        }

        [TestMethod]
        public void ALoadMoreThanThreeHeavyDoesNot()
        {
            Assert.AreEqual(LineLoadState.ValueMismatch, RunOne(6.0, 9.2).State);
        }

        /// <summary>
        /// And only up to three tenths under it - a load short of the weight is a load short.
        /// </summary>
        [TestMethod]
        public void ALoadUpToThreeTenthsLightAgrees()
        {
            Assert.AreEqual(LineLoadState.Matched, RunOne(6.0, 5.8).State);
            Assert.AreEqual(LineLoadState.Matched, RunOne(6.0, 5.7).State, "the edge of the band is in it");
        }

        [TestMethod]
        public void ALoadMoreThanThreeTenthsLightDoesNot()
        {
            Assert.AreEqual(LineLoadState.ValueMismatch, RunOne(6.0, 5.5).State);
        }

        /// <summary>The band is the settings', not a number written into the check.</summary>
        [TestMethod]
        public void TheBandIsTheSettings()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.SidlCToleranceAbove = 1;

            Assert.AreEqual(LineLoadState.ValueMismatch, RunOne(6.0, 7.5).State);
        }

        /// <summary>
        /// Left unset the band is the line load buffer either way, so the loading plan's line
        /// loads read exactly as they always did.
        /// </summary>
        [TestMethod]
        public void WithNoBandTheLineLoadBufferIsUsedBothWays()
        {
            LoadBand band = LoadBand.Symmetric(0.2);
            Assert.IsTrue(band.Agrees(6.0, 6.2));
            Assert.IsTrue(band.Agrees(6.0, 5.8));
            Assert.IsFalse(band.Agrees(6.0, 6.5));
            Assert.IsFalse(band.Agrees(6.0, 5.5));
        }

        #endregion

        #region which pattern

        /// <summary>
        /// The SIDL_C check reads SIDL_C and nothing else: a frame carrying a SIDL block wall
        /// beside the SIDL_C upstand is not carrying the upstand twice over.
        /// </summary>
        [TestMethod]
        public void OnlyTheSidlCPatternIsRead()
        {
            LineLoadResult result = RunOne(6.0,
                Load("SIDL_C", 6.0), Load("SIDL", 10.0));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(6.0, result.Actual, 1e-9);
        }

        /// <summary>Every pattern named is read: SIDL_LWC is as much the wall's load as SIDL_C.</summary>
        [TestMethod]
        public void EveryNamedPatternIsRead()
        {
            LineLoadResult result = RunOne(6.0, Load("SIDL_LWC", 6.0));

            Assert.AreEqual(LineLoadState.Matched, result.State);
            Assert.AreEqual(6.0, result.Actual, 1e-9);
        }

        /// <summary>
        /// Named outright, so matched whole: SIDL_C does not catch SIDL_C2, the way SIDL catches
        /// SIDL-FINISHES in the line load check.
        /// </summary>
        [TestMethod]
        public void ThePatternsAreMatchedWholeOnly()
        {
            LineLoadPatterns patterns = LineLoadPatterns.Exactly("SIDL_C, SIDL_LWC");

            Assert.IsTrue(patterns.Matches("SIDL_C"));
            Assert.IsTrue(patterns.Matches("sidl-c"));
            Assert.IsTrue(patterns.Matches("SIDL_LWC"));
            Assert.IsTrue(patterns.Matches("Sidl Lwc"));
            Assert.IsFalse(patterns.Matches("SIDL_C2"));
            Assert.IsFalse(patterns.Matches("SIDL_LWC_OLD"));
            Assert.IsFalse(patterns.Matches("SIDL"));

            Assert.AreEqual(LineLoadState.NoEtabsLoad, RunOne(6.0, Load("SIDL_C2", 6.0)).State);
        }

        /// <summary>
        /// A pattern the SIDL_C check reads is never the SIDL, renamed or not and on the never
        /// list or not: the area load check does not count it and the line load check does not
        /// read it, although SIDL sits inside its name.
        /// </summary>
        [TestMethod]
        public void ASidlCPatternIsNeverReadAsTheSidl()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            string sidlC = pref.SidlCLoadPatternNames, never = pref.ExcludedLoadPatternNames,
                   sdl = pref.SdlLoadPatternNames, line = pref.LineLoadPatternNames;
            try
            {
                pref.SidlCLoadPatternNames = "SIDL_WALLS, SIDL_LWC";
                pref.ExcludedLoadPatternNames = "";
                pref.SdlLoadPatternNames = "SIDL";
                pref.LineLoadPatternNames = "SIDL";

                LoadPatternMap.Mapping mapping = LoadPatternMap.FromPreferences(pref);
                Assert.AreEqual(LoadCategory.None, mapping.Categorise("SIDL_WALLS"));
                Assert.AreEqual(LoadCategory.None, mapping.Categorise("SIDL_LWC"));
                Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SIDL"));
                Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SIDL_C"), "no longer named, so the SIDL again");

                LineLoadPatterns lines = LineLoadPatterns.From(pref);
                Assert.IsFalse(lines.Matches("SIDL_WALLS"));
                Assert.IsFalse(lines.Matches("SIDL_LWC"));
                Assert.IsTrue(lines.Matches("SIDL-FINISHES"));

                Assert.IsTrue(LineLoadPatterns.SidlC(pref).Matches("sidl walls"));
            }
            finally
            {
                pref.SidlCLoadPatternNames = sidlC;
                pref.ExcludedLoadPatternNames = never;
                pref.SdlLoadPatternNames = sdl;
                pref.LineLoadPatternNames = line;
            }
        }

        /// <summary>
        /// A model that keeps wall weights and drawn line loads in one pattern (SDLP), and fill
        /// in another (SDLF): each check that names a pattern outright still reads it.
        /// </summary>
        [TestMethod]
        public void APatternNamedOutrightIsReadEvenWhenSidlCReadsItToo()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            string sidlC = pref.SidlCLoadPatternNames, area = pref.SidlCAreaLoadPatternNames,
                   never = pref.ExcludedLoadPatternNames, sdl = pref.SdlLoadPatternNames,
                   line = pref.LineLoadPatternNames;
            try
            {
                pref.SidlCLoadPatternNames = "SDLP";
                pref.SidlCAreaLoadPatternNames = "SDLF";
                pref.ExcludedLoadPatternNames = "SIDL_C";
                pref.SdlLoadPatternNames = "SDLP, SDLF, SIDL";
                pref.LineLoadPatternNames = "SDLP";

                LoadPatternMap.Mapping mapping = LoadPatternMap.FromPreferences(pref);
                Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SDLP"));
                Assert.AreEqual(LoadCategory.Sdl, mapping.Categorise("SDLF"));
                Assert.AreEqual(LoadCategory.None, mapping.Categorise("SIDL_C"), "still kept out");

                Assert.IsTrue(LineLoadPatterns.From(pref).Matches("SDLP"));
                Assert.IsTrue(LineLoadPatterns.SidlC(pref).Matches("SDLP"));
                Assert.IsTrue(LineLoadPatterns.SidlCArea(pref).Matches("SDLF"));
                Assert.IsFalse(LineLoadPatterns.SidlCArea(pref).Matches("SDLP"));
            }
            finally
            {
                pref.SidlCLoadPatternNames = sidlC;
                pref.SidlCAreaLoadPatternNames = area;
                pref.ExcludedLoadPatternNames = never;
                pref.SdlLoadPatternNames = sdl;
                pref.LineLoadPatternNames = line;
            }
        }

        /// <summary>A frame with SIDL on it and no SIDL_C has nothing under the wall.</summary>
        [TestMethod]
        public void ASidlLoadIsNotASidlCLoad()
        {
            LineLoadResult result = RunOne(6.0, Load("SIDL", 6.0));

            Assert.AreEqual(LineLoadState.NoEtabsLoad, result.State);
        }

        /// <summary>A SIDL_C load on a frame no wall stands over is reported as a load nothing accounts for.</summary>
        [TestMethod]
        public void ASidlCLoadWithNoWallIsUnclaimed()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetStoriesToCheck(new[] { "L03" });
            engine.StartComparison(new List<LineLoad_R>(),
                new[] { Frame("B1", 0, 0, 6000, 0, Load("SIDL_C", 4.0)) });

            Assert.AreEqual(1, engine.Unclaimed.Count);
            StringAssert.Contains(engine.Unclaimed[0].SidlCSummary, "no wall or fill strip");
        }

        #endregion

        #region on the plan

        /// <summary>
        /// Its elements answer to the SIDL_C tab, report the SIDL_C check and are filed under
        /// names of their own - so accepting a SIDL_C finding on a frame never accepts the
        /// loading plan's finding on the same frame.
        /// </summary>
        [TestMethod]
        public void ItsElementsAreFiledOnTheSidlCTab()
        {
            LineLoadResult result = RunOne(6.0, 9.5);

            ElementViewModel element = ElementViewModelFactory.FromLineLoadResult(result, true).Single();
            ElementViewModel plain = ElementViewModelFactory.FromLineLoadResult(result).Single();

            Assert.AreEqual(ViewMode.SidlC, element.Tab);
            Assert.AreEqual(CheckKind.SidlC, element.Outcomes.Single().Kind);
            StringAssert.StartsWith(element.ReviewKey, ElementViewModelFactory.SidlCKeyPrefix);

            Assert.AreEqual(ViewMode.LineLoads, plain.Tab);
            Assert.AreNotEqual(plain.ReviewKey, element.ReviewKey);
        }

        [TestMethod]
        public void AnUnclaimedSidlCLoadIsFiledOnTheSidlCTab()
        {
            LineLoadCompareEngine engine = Engine();
            engine.SetStoriesToCheck(new[] { "L03" });
            engine.StartComparison(new List<LineLoad_R>(),
                new[] { Frame("B1", 0, 0, 6000, 0, Load("SIDL_C", 4.0)) });

            ElementViewModel element = ElementViewModelFactory.FromUnclaimedLineLoad(engine.Unclaimed[0], true);

            Assert.AreEqual(ViewMode.SidlC, element.Tab);
            Assert.AreEqual(CheckKind.SidlCMissingInRevit, element.Outcomes.Single().Kind);
            Assert.AreNotEqual(ElementViewModelFactory.FromUnclaimedLineLoad(engine.Unclaimed[0]).ReviewKey,
                               element.ReviewKey);
        }

        /// <summary>The card says it is the wall's weight being asked about, not a schedule.</summary>
        [TestMethod]
        public void TheVerdictSpeaksOfTheWallsWeight()
        {
            PrefrencesData.getPrefrencesData().LineLoadCheckInSteps = false;

            LineLoadResult result = RunOne(6.0, 2.0);

            Assert.AreEqual(LineLoadState.ValueMismatch, result.State);
            StringAssert.Contains(result.Summary, "wall Upstand-200");
            StringAssert.Contains(result.Summary, "its weight is 6");
        }

        /// <summary>
        /// The SIDL_C tab and the line loads tab are never both up: the two are different
        /// questions about the same frames, and read off one plan they would be taken for one.
        /// </summary>
        [TestMethod]
        public void TheSidlCTabAndTheLineLoadsTabAreNeverBothUp()
        {
            FindingTally tally = new FindingTally(() => { });
            Assert.IsFalse(tally.IsActive(ViewMode.SidlC), "off to begin with, like the line loads");

            tally.ToggleTab(ViewMode.LineLoads);
            Assert.IsTrue(tally.IsActive(ViewMode.LineLoads));

            tally.ToggleTab(ViewMode.SidlC);
            Assert.IsTrue(tally.IsActive(ViewMode.SidlC));
            Assert.IsFalse(tally.IsActive(ViewMode.LineLoads), "switched off by the SIDL_C tab");

            tally.ToggleTab(ViewMode.LineLoads);
            Assert.IsFalse(tally.IsActive(ViewMode.SidlC), "and the other way round");
        }

        /// <summary>The names it starts with are the ones the wall check leaves out.</summary>
        [TestMethod]
        public void TheWallNamesStartAsTheWallChecksLeaveOutList()
        {
            // A fresh instance, for the compiled-in defaults: the shared one is whatever the
            // tests before this left it as.
            PrefrencesData defaults = (PrefrencesData)System.Activator.CreateInstance(typeof(PrefrencesData), true);

            Assert.AreEqual(defaults.WallTypeNamesToIgnore, defaults.SidlCWallNames);
            Assert.AreEqual("SIDL_C, SIDL_LWC", defaults.SidlCLoadPatternNames);
            Assert.AreEqual(25, defaults.SidlCConcreteDensity);
            Assert.AreEqual(0.3, defaults.SidlCToleranceBelow);
            Assert.AreEqual(3, defaults.SidlCToleranceAbove);
            Assert.AreEqual(500, defaults.SidlCFillSlabMaximumWidth);
            StringAssert.Contains(defaults.SidlCFillSlabNames, "Fill SLAB");
            Assert.AreEqual(700, defaults.SidlCLocationTolerance);
            Assert.AreEqual(1, defaults.SidlCMinimumLoad);
        }

        /// <summary>
        /// The SIDL_C check finds a wall's frame by its own tolerance, not the line loads
        /// tab's search radius (1500 here): nothing past it, however near the radius is.
        /// </summary>
        [TestMethod]
        public void TheLocationToleranceIsItsOwn()
        {
            Assert.AreEqual(LineLoadState.Matched, RunOffset(1200, null).State, "inside the line loads' radius");
            Assert.AreEqual(LineLoadState.NoEtabsFrame, RunOffset(1200, 800).State, "past its own 800");
            Assert.AreEqual(LineLoadState.Matched, RunOffset(600, 800).State, "inside its own 800");
        }

        /// <summary>And a tolerance past the line loads' search radius reaches as far as it says.</summary>
        [TestMethod]
        public void TheSearchReachesAsFarAsTheTolerance()
        {
            Assert.AreEqual(LineLoadState.NoEtabsFrame, RunOffset(1800, null).State, "past the line loads' 1.5 m");
            Assert.AreEqual(LineLoadState.Matched, RunOffset(1800, 2000).State, "inside its own 2 m");
        }

        private static LineLoadResult RunOffset(double offset, double? tolerance)
        {
            LineLoad_R wall = LineLoad_R.Weighed(501, "wall Upstand-200",
                new[] { At(0, offset), At(6000, offset) }, 6, "worked out", "L03");

            LineLoadCompareEngine engine = Engine();
            engine.LocationTolerance = tolerance;
            engine.StartComparison(new[] { wall }, new[] { Frame("B1", 0, 0, 6000, 0, Load("SIDL_C", 6)) });

            Assert.AreEqual(1, engine.Results.Count, "one wall in, one result out");
            return engine.Results[0];
        }

        #endregion

        #region building the model under test

        private static SidlCSource_R Upstand(double thickness, double height, double baseElevation = 0)
        {
            return new SidlCSource_R(SidlCSourceKind.Wall, 501, new[] { At(0, 0), At(6000, 0) },
                                     thickness, height, baseElevation)
            {
                TypeName = "Upstand-200",
                Story = "L03"
            };
        }

        private static LineLoadCompareEngine Engine()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            return new LineLoadCompareEngine
            {
                Patterns = LineLoadPatterns.Exactly("SIDL_C, SIDL_LWC"),
                Band = new LoadBand(pref.SidlCToleranceBelow, pref.SidlCToleranceAbove)
            };
        }

        /// <summary>A 6 m upstand weighing <paramref name="weight"/> over a frame carrying <paramref name="carried"/> SIDL_C.</summary>
        private static LineLoadResult RunOne(double weight, double carried)
        {
            return RunOne(weight, Load("SIDL_C", carried));
        }

        private static LineLoadResult RunOne(double weight, params LineLoad_E[] loads)
        {
            LineLoad_R wall = LineLoad_R.Weighed(501, "wall Upstand-200",
                new[] { At(0, 150), At(6000, 150) }, weight, "worked out", "L03");

            LineLoadCompareEngine engine = Engine();
            engine.StartComparison(new[] { wall }, new[] { Frame("B1", 0, 0, 6000, 0, loads) });

            Assert.AreEqual(1, engine.Results.Count, "one wall in, one result out");
            return engine.Results[0];
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

        private static LineLoad_E Load(string pattern, double perMetre)
        {
            return new LineLoad_E(pattern, perMetre, perMetre, 0, 1, 10, true, false);
        }

        private static MyPoint At(double x, double y)
        {
            return new MyPoint(x, y, 0);
        }

        private static MyPoint Rotated(double x, double y, double c, double s)
        {
            return new MyPoint(x * c - y * s, x * s + y * c, 0);
        }

        #endregion
    }
}
