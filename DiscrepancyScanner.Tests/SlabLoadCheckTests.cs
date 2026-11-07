using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The slab load check, run for real.
    ///
    /// Nothing here needs Revit or ETABS: the loading plan arrives as regions that have
    /// already been read off a view, and the ETABS areas as objects with their loads already
    /// converted, so the whole comparison - cutting the mesh, reading the plan at a point,
    /// and deciding the four numbers - runs on plain objects.
    ///
    /// The case that matters most is the last one. An ETABS shell carries one uniform load, so
    /// a shell lying under two loading regions that ask for different loads is wrong however
    /// it was drawn, and the check is supposed to cut it along the boundary and say so. If
    /// that does not happen the area is answered from one point in its middle and half the
    /// disagreement disappears without a word, which is the failure the design exists to
    /// prevent.
    /// </summary>
    [TestClass]
    public class SlabLoadCheckTests
    {
        private const string Story = "L03";

        // A 10 m by 10 m bay, in mm, and the two halves of it.
        private const double Size = 10000;

        #region the four loads on one bay

        [TestMethod]
        public void LoadsThatMatchTheScheduleComeOutMatched()
        {
            SlabZone zone = OneBay(etabsSdl: 3.0, etabsLr: 2.0);

            Assert.AreEqual(SlabLoadState.Matched, zone.LoadState, zone.LoadSummary);
            Assert.AreEqual("1", zone.LoadNumber);
            Assert.AreEqual(0, zone.MismatchedLoads.Count);

            // The verdict has to reach the viewer as a result of its own, beside the
            // thickness one, or none of the colouring or filtering can see it.
            CheckOutcome outcome = zone.Outcomes.FirstOrDefault(o => o.Kind == CheckKind.SlabLoads);
            Assert.IsNotNull(outcome, "the zone publishes no load outcome");
            Assert.AreEqual(ResultLevel.Passed, outcome.Level);

            // The thickness verdict is no longer beside it: the two checks are answered on
            // two separate meshes, cut along different edges, so a piece of the load mesh has
            // no thickness to report and does not pretend to. The thickness verdict for this
            // area is on the slab mesh, where it was cut for that question.
            Assert.IsFalse(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabThickness),
                           "a load zone claims no thickness verdict - it was not cut for one");
            Assert.IsTrue(LastSlabZones.Any(z => z.Outcomes.Any(o => o.Kind == CheckKind.SlabThickness)),
                          "and the thickness verdict is on the slab mesh instead");
        }

        /// <summary>
        /// A piece of the load cut is never asked the slab questions - its slab check reads "not
        /// checked" - so its verdict has to come from its loads. It used to come from the slab
        /// check alone, and a piece failing its SDL had ACCEPT greyed out, on its own and in a
        /// selection of several, because as far as the verdict knew there was nothing to accept.
        /// </summary>
        [TestMethod]
        public void APieceFailingItsLoadsCanBeAccepted()
        {
            SlabZone wrong = OneBay(etabsSdl: 4.0, etabsLr: 2.0);
            ElementViewModel element = ElementViewModelFactory.FromSlabZone(wrong);

            Assert.AreEqual(CheckState.Failed, element.State, "the load failure is the verdict");
            Assert.IsTrue(CheckNaming.Disagrees(element));
            Assert.IsTrue(new ElementCard(element, null).CanAccept);

            ElementViewModel another = ElementViewModelFactory.FromSlabZone(OneBay(etabsSdl: 4.0, etabsLr: 2.0));
            Assert.IsTrue(new ElementCard(new List<ElementViewModel> { element, another }, e => null).CanAccept,
                          "ACCEPT ALL on a selection of them");

            ElementViewModel right = ElementViewModelFactory.FromSlabZone(OneBay(etabsSdl: 3.0, etabsLr: 2.0));
            Assert.AreEqual(CheckState.Passed, right.State);
            Assert.IsFalse(new ElementCard(right, null).CanAccept, "a piece that agrees has nothing to accept");
        }

        [TestMethod]
        public void ALoadThatDiffersIsNamedRatherThanJustFlagged()
        {
            SlabZone zone = OneBay(etabsSdl: 4.0, etabsLr: 2.0);

            Assert.AreEqual(SlabLoadState.Mismatch, zone.LoadState, zone.LoadSummary);
            CollectionAssert.AreEqual(new[] { LoadCategory.Sdl }, zone.MismatchedLoads.ToArray(),
                                      "only the SDL differs, so only the SDL is named");

            StringAssert.Contains(zone.LoadSummary, "SDL");
            StringAssert.Contains(zone.LoadSummary, "plan 3");
            StringAssert.Contains(zone.LoadSummary, "ETABS 4");

            // And the SDL answers for itself, with the two numbers under it rather than a
            // sentence beside it: four loads reported as one verdict say the zone is wrong
            // without saying which of them to go and change.
            CheckOutcome sdl = zone.Outcomes.Single(
                o => o.Kind == CheckKind.SlabLoads && o.Token == "SDL");
            Assert.AreEqual(ResultLevel.Failed, sdl.Level);
            Assert.AreEqual("4", sdl.Etabs);
            Assert.AreEqual("3", sdl.Revit);

            Assert.IsTrue(
                zone.Outcomes.Where(o => o.Kind == CheckKind.SlabLoads && o.Token != "SDL")
                    .All(o => o.Level == ResultLevel.Passed),
                "the three that agree are not dragged down with the one that does not");
        }

        [TestMethod]
        public void ALoadInsideTheBufferIsStillTheSameLoad()
        {
            // The buffer is 0.05 kN/m2 by default: a schedule written to one decimal place
            // against a load set typed in from it.
            Assert.AreEqual(SlabLoadState.Matched, OneBay(etabsSdl: 3.02, etabsLr: 2.0).LoadState);
            Assert.AreEqual(SlabLoadState.Mismatch, OneBay(etabsSdl: 3.2, etabsLr: 2.0).LoadState);
        }

        [TestMethod]
        public void AnAreaWithNoLoadOnItSaysSoRatherThanReadingAsZero()
        {
            SlabZone zone = OneBay(etabsSdl: 3.0, etabsLr: 2.0, etabsHasLoads: false);

            Assert.AreEqual(SlabLoadState.NoEtabsLoad, zone.LoadState, zone.LoadSummary);
            Assert.AreEqual(ResultLevel.Review,
                            zone.Outcomes.First(o => o.Kind == CheckKind.SlabLoads).Level,
                            "nothing to compare is a look-at-it, not a failure");
        }

        #endregion

        #region which way is down

        [TestMethod]
        public void WhichDirectionsMeanDownAndWhichWayRoundTheyAreWritten()
        {
            // The sign convention itself, because everything else about the load check rests
            // on it: gravity is positive downwards, and every axis direction is positive up.
            double sign;

            Assert.IsTrue(AreaLoad_E.IsDownwardDirection(10, out sign), "gravity");
            Assert.AreEqual(1, sign, 1e-9, "a gravity load is written positive downwards");

            Assert.IsTrue(AreaLoad_E.IsDownwardDirection(11, out sign), "projected gravity");
            Assert.AreEqual(1, sign, 1e-9);

            Assert.IsTrue(AreaLoad_E.IsDownwardDirection(6, out sign), "global Z");
            Assert.AreEqual(-1, sign, 1e-9, "a load along Z is written negative downwards");

            Assert.IsTrue(AreaLoad_E.IsDownwardDirection(9, out sign), "projected global Z");
            Assert.AreEqual(-1, sign, 1e-9);

            Assert.IsTrue(AreaLoad_E.IsDownwardDirection(3, out sign), "local 3");
            Assert.AreEqual(-1, sign, 1e-9);

            // Sideways: not a load this check can compare, and not one it may absorb either.
            Assert.IsFalse(AreaLoad_E.IsDownwardDirection(4, out sign), "global X");
            Assert.IsFalse(AreaLoad_E.IsDownwardDirection(5, out sign), "global Y");
        }

        [TestMethod]
        public void GravityAndGlobalZAssignmentsGiveTheSameVerdict()
        {
            // The same 3 kN/m2 pressing down on the same slab, written the two ways ETABS
            // writes it: +3 assigned in the gravity direction, and -3 assigned along global Z.
            // Reading either the wrong way round would flip every verdict in the model.
            SlabZone gravity = Run(new List<AreaLoad_E>
            {
                RawLoad("SDL", 3.0, 10),
                RawLoad("LLR", 2.0, 10)
            }, planNumber: 1);

            SlabZone globalZ = Run(new List<AreaLoad_E>
            {
                RawLoad("SDL", -3.0, 6),
                RawLoad("LLR", -2.0, 6)
            }, planNumber: 1);

            Assert.AreEqual(SlabLoadState.Matched, gravity.LoadState, gravity.LoadSummary);
            Assert.AreEqual(SlabLoadState.Matched, globalZ.LoadState, globalZ.LoadSummary);
            Assert.AreEqual(3.0, globalZ.EtabsLoads.Sdl, 1e-9,
                            "a load written -3 along Z is 3 pressing down");
        }

        [TestMethod]
        public void AnUpwardLoadIsNotReadAsADownwardOne()
        {
            // +3 along global Z is 3 kN/m2 pulling up, which is not what the schedule asks
            // for and must not be mistaken for it.
            SlabZone zone = Run(new List<AreaLoad_E>
            {
                RawLoad("SDL", 3.0, 6),
                RawLoad("LLR", -2.0, 6)
            }, planNumber: 1);

            Assert.AreEqual(SlabLoadState.Mismatch, zone.LoadState, zone.LoadSummary);
            Assert.AreEqual(-3.0, zone.EtabsLoads.Sdl, 1e-9);
        }

        [TestMethod]
        public void APatternTheSettingsDoNotNameIsReportedRatherThanCountedAsNothing()
        {
            List<AreaLoad_E> loads = new List<AreaLoad_E>
            {
                Load("SDL", 3.0),
                Load("LLR", 2.0),
                // Named by none of the four lists, so its value cannot be counted - which is
                // exactly why the zone has to say it was there.
                Load("SNOW", 1.5)
            };

            SlabZone zone = Run(loads, planNumber: 1);

            Assert.AreEqual(SlabLoadState.Matched, zone.LoadState);
            StringAssert.Contains(zone.LoadSummary, "SNOW",
                                  "a load nobody mapped has to be visible on a zone that reads as right");
        }

        #endregion

        #region the plan and the schedule disagreeing

        [TestMethod]
        public void ANumberTheScheduleDoesNotDefineIsAReviewRatherThanAFailure()
        {
            SlabZone zone = Run(new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                                planNumber: 97);

            Assert.AreEqual(SlabLoadState.NotInSchedule, zone.LoadState, zone.LoadSummary);
            Assert.AreEqual(ResultLevel.Review,
                            zone.Outcomes.First(o => o.Kind == CheckKind.SlabLoads).Level);
        }

        [TestMethod]
        public void ARegionWithNoNumberOnItIsAReviewToo()
        {
            SlabZone zone = Run(new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                                planNumber: 0);

            Assert.AreEqual(SlabLoadState.NoLoadNumber, zone.LoadState, zone.LoadSummary);
        }

        [TestMethod]
        public void ALetteredLabelOnThePlanIsLookedUpInTheSchedule()
        {
            // The schedule writes H1; the region was typed h1. Same load.
            List<SlabZone> zones = RunAll(
                new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                new List<LoadRegion_R> { new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, Size) }, "h1") });

            SlabZone zone = zones.Single(z => z.WasLoadChecked);
            Assert.AreEqual(SlabLoadState.Matched, zone.LoadState, zone.LoadSummary);
            Assert.AreEqual("HOTEL ROOM", zone.LoadDescription);
        }

        #endregion

        #region the case the whole design is for

        [TestMethod]
        public void AnEtabsAreaSpanningTwoLoadingRegionsIsCutBetweenThem()
        {
            // One ETABS shell over the whole bay, and a loading plan that asks for two
            // different loads across it. The shell carries one uniform load, so it cannot be
            // right on both halves - and answering it from a single point would hide that.
            List<SlabZone> zones = RunAll(
                new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                new List<LoadRegion_R>
                {
                    Region(1, 1, Rectangle(0, 0, Size / 2, Size)),
                    Region(2, 2, Rectangle(Size / 2, 0, Size, Size))
                });

            List<SlabZone> loadChecked = zones.Where(zone => zone.WasLoadChecked).ToList();

            Assert.AreEqual(2, loadChecked.Count,
                            "the area should have been cut along the loading region boundary, " +
                            "but came out as " + loadChecked.Count + " zone(s)");

            CollectionAssert.AreEquivalent(new[] { "1", "2" },
                                           loadChecked.Select(zone => zone.LoadNumber).ToArray());

            // Load 1 asks for SDL 3.0 and LR 2.0, which is what the shell carries; load 2 asks
            // for SDL 4.0 and LR 2.0, which it does not. So the two halves have to disagree.
            SlabZone first = loadChecked.Single(zone => zone.LoadNumber == "1");
            SlabZone second = loadChecked.Single(zone => zone.LoadNumber == "2");

            Assert.AreEqual(SlabLoadState.Matched, first.LoadState, first.LoadSummary);
            Assert.AreEqual(SlabLoadState.Mismatch, second.LoadState, second.LoadSummary);

            // And the piece that is wrong has to be the half it is wrong on, not the whole bay.
            Assert.AreEqual(Size * Size / 2, second.Area, Size * Size * 0.02,
                            "the coloured piece should be the half that disagrees");
        }

        [TestMethod]
        public void ARegionOverlappedByAnotherIsNamedByElementIdAndArea()
        {
            // A base region over the whole bay with a patch drawn on top of it. The patch is
            // the smaller one, so it answers - but the disagreement has to say which region it
            // is with, by the id that makes it selectable in Revit, because the number alone
            // is drawn all over the plan and finds nothing.
            List<SlabZone> zones = RunAll(
                new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                new List<LoadRegion_R>
                {
                    Region(4001, 1, Rectangle(0, 0, Size, Size)),
                    Region(4002, 2, Rectangle(0, 0, Size / 4, Size / 4))
                });

            SlabZone conflicted = zones.Single(
                zone => zone.LoadState == SlabLoadState.ConflictingRegions);

            Assert.AreEqual("2", conflicted.LoadNumber, "the smaller region answers");
            Assert.AreEqual(4002, conflicted.LoadRegionId);

            StringAssert.Contains(conflicted.LoadNote, "4001",
                                  "the note has to name the region it disagrees with");
            StringAssert.Contains(conflicted.LoadNote, "load 1");

            // The area of the region it disagrees with, so a region claiming ground it does
            // not cover - a hole that was lost - shows up as an area that is not the one
            // Revit reports for that id.
            StringAssert.Contains(conflicted.LoadNote, "100 m2");

            // And the verdict the viewer shows says both of them.
            StringAssert.Contains(conflicted.LoadSummary, "4001");
            StringAssert.Contains(conflicted.LoadSummary, "4002");
        }

        [TestMethod]
        public void TwoPatchesInConflictWithDifferentRegionsAreNotMergedIntoOneZone()
        {
            // Two patches over one base region, each disagreeing with a different neighbour.
            // They have to stay apart: merged into one zone, the note would confidently name
            // whichever region the sample point that built the zone happened to hit.
            List<SlabZone> zones = RunAll(
                new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                new List<LoadRegion_R>
                {
                    Region(4001, 1, Rectangle(0, 0, Size, Size / 2)),
                    Region(4002, 1, Rectangle(0, Size / 2, Size, Size)),
                    Region(4003, 2, Rectangle(Size / 4, 0, Size / 2, Size / 4)),
                    Region(4004, 2, Rectangle(Size / 4, 3 * Size / 4, Size / 2, Size))
                });

            List<SlabZone> conflicted = zones
                .Where(zone => zone.LoadState == SlabLoadState.ConflictingRegions)
                .ToList();

            Assert.AreEqual(2, conflicted.Count,
                            "the two patches disagree with different regions, so they are two " +
                            "zones, not " + conflicted.Count);

            CollectionAssert.AreEquivalent(
                new long[] { 4003, 4004 },
                conflicted.Select(zone => zone.LoadRegionId).ToArray());

            SlabZone lower = conflicted.Single(zone => zone.LoadRegionId == 4003);
            SlabZone upper = conflicted.Single(zone => zone.LoadRegionId == 4004);

            StringAssert.Contains(lower.LoadNote, "4001");
            Assert.IsFalse(lower.LoadNote.Contains("4002"), lower.LoadNote);

            StringAssert.Contains(upper.LoadNote, "4002");
            Assert.IsFalse(upper.LoadNote.Contains("4001"), upper.LoadNote);
        }

        [TestMethod]
        public void WithNoLoadingPlanTheZonesAreExactlyWhatTheyAlwaysWere()
        {
            // The load check is opt in, and with it off nothing about the slab check may
            // change - not the verdicts, and not how the mesh is cut into zones.
            RunAll(null, null);
            List<SlabZone> withoutLoads = LastSlabZones;

            Assert.AreEqual(1, withoutLoads.Count, "one ETABS area, one zone");
            Assert.IsFalse(withoutLoads[0].WasLoadChecked);
            Assert.IsFalse(withoutLoads[0].Outcomes.Any(o => o.Kind == CheckKind.SlabLoads),
                           "no load result is published at all, so its switches never appear");
        }

        #endregion

        #region every paired level in one run

        [TestMethod]
        public void EachLevelIsCheckedAgainstItsOwnLoadingPlan()
        {
            // Two levels, both carrying SDL 3.0, paired with plans that ask for different
            // things: number 1 asks 3.0, number 2 asks 4.0. So the first level is right and
            // the second is wrong. Applying one level's plan to the other, or one plan to
            // both, gives the wrong verdict rather than an error - which is why this is
            // checked by the numbers each zone came out with, not just by its state.
            List<SlabZone> zones = RunLevels(new LevelSetup("L03", 1), new LevelSetup("L04", 2));

            SlabZone third = OnStory(zones, "L03");
            SlabZone fourth = OnStory(zones, "L04");

            Assert.AreEqual("1", third.LoadNumber, "L03 reads its own plan");
            Assert.AreEqual(SlabLoadState.Matched, third.LoadState, third.LoadSummary);

            Assert.AreEqual("2", fourth.LoadNumber, "L04 reads its own plan");
            Assert.AreEqual(SlabLoadState.Mismatch, fourth.LoadState, fourth.LoadSummary);
        }

        [TestMethod]
        public void ALevelWithNoLoadingPlanIsLeftExactlyAsItAlwaysWas()
        {
            // The point of checking every level at once: the paired ones get their loads
            // checked and the unpaired ones are not touched. Six levels paired out of
            // forty-seven has to leave the other forty-one reading exactly as before.
            List<SlabZone> loadZones = RunLevels(new LevelSetup("L03", 1), new LevelSetup("L04", 0));

            SlabZone paired = OnStory(loadZones, "L03");

            // An unpaired level produces no load mesh at all, so the thing to check is that
            // its slab zone is still there and still says nothing about loads.
            Assert.IsFalse(loadZones.Any(z => string.Equals(z.Story, "L04", StringComparison.OrdinalIgnoreCase)),
                           "an unpaired level is not cut for loads in the first place");
            SlabZone unpaired = OnStory(LastSlabZones, "L04");

            Assert.IsTrue(paired.WasLoadChecked);
            Assert.IsTrue(paired.Outcomes.Any(o => o.Kind == CheckKind.SlabLoads));

            Assert.IsFalse(unpaired.WasLoadChecked, "an unpaired level has no load verdict");
            Assert.IsFalse(unpaired.Outcomes.Any(o => o.Kind == CheckKind.SlabLoads),
                           "and publishes no load result at all, so its switches stay empty");
        }

        [TestMethod]
        public void APairingIsFoundEvenWhenTheStoryNameCarriesStraySpaces()
        {
            // The pairings are stored trimmed and a slab carries the name exactly as ETABS
            // gave it. Without matching on the trimmed name a level would be paired in the
            // window and silently unpaired in the check, which looks like the feature not
            // working at all.
            List<SlabZone> zones = RunLevels(new LevelSetup("L03 ", 1, storyOnSlab: "L03"));

            Assert.AreEqual("1", OnStory(zones, "L03").LoadNumber);
        }

        [TestMethod]
        public void AStrayCharacterOnTheSlabSideIsNotReportedAsAnUncheckedLevel()
        {
            // The same mismatch the other way round: the pairing trimmed, the slab carrying
            // the space. The level does get checked - and the warning that names levels which
            // were paired but never reached must not name it, or the one message whose job is
            // explaining why a level looks unchecked cries wolf about a level that was fine.
            List<SlabZone> zones = RunLevels(new LevelSetup("L03", 1, storyOnSlab: "L03 "));

            Assert.AreEqual("1", OnStory(zones, "L03 ").LoadNumber, "the level is still checked");
            Assert.IsFalse(LastIssues.Any(issue => issue.Contains("not checked")),
                           "and is not reported as unchecked: " + string.Join(" / ", LastIssues));
        }

        [TestMethod]
        public void TheLoadsCheckedOnTheirOwnComeBackAsAWholeRunGivesThem()
        {
            // A level paired with a different view changes nothing the thickness, outline and
            // opening pass reads, so the loads are checked again on their own. They have to
            // come back exactly as a whole run gives them - down to the name a review is filed
            // under, or every acceptance on a load finding would be lost by the re-check.
            List<SlabZone> whole = RunLevels(false, new LevelSetup("L03", 1), new LevelSetup("L04", 0));
            List<SlabZone> alone = RunLevels(true, new LevelSetup("L03", 1), new LevelSetup("L04", 0));

            Assert.AreEqual(0, LastSlabZones.Count, "the slab pass is not run on a load re-check");
            Assert.AreEqual(whole.Count, alone.Count, "the same load zones, no more and no fewer");

            SlabZone before = OnStory(whole, "L03"), after = OnStory(alone, "L03");
            Assert.AreEqual(before.LoadSummary, after.LoadSummary);
            Assert.AreEqual(before.ReviewKey, after.ReviewKey);
        }

        /// <summary>What the last <see cref="RunLevels"/> had to say for itself.</summary>
        private static List<string> LastIssues = new List<string>();

        /// <summary>
        /// The slab mesh from the last run - thickness, outlines and openings, cut along the
        /// Revit floors. The helpers return the load mesh, because most of these tests are
        /// about loads; the few that check the slab side was left alone read this instead.
        /// </summary>
        private static List<SlabZone> LastSlabZones = new List<SlabZone>();

        private static SlabZone OnStory(List<SlabZone> zones, string story)
        {
            List<SlabZone> found = zones
                .Where(zone => string.Equals(zone.Story, story, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.AreEqual(1, found.Count, $"expected one zone on {story}, got {found.Count}");
            return found[0];
        }

        /// <summary>One level in a multi-level run: its name, and the plan it is paired with.</summary>
        private sealed class LevelSetup
        {
            public readonly string Story;
            public readonly string StoryOnSlab;
            public readonly int PlanNumber;

            /// <param name="planNumber">Nought for a level nobody has paired.</param>
            /// <param name="storyOnSlab">
            /// The name the slabs carry, when it is not the name the pairing was filed under.
            /// </param>
            public LevelSetup(string story, int planNumber, string storyOnSlab = null)
            {
                Story = story;
                PlanNumber = planNumber;
                StoryOnSlab = storyOnSlab ?? story;
            }
        }

        /// <summary>
        /// The whole check over several levels at once, with no story narrowing - which is
        /// what "every level" does. Each level is one bay carrying SDL 3.0 and LR 2.0, over
        /// one Revit floor of the same outline and thickness.
        /// </summary>
        private static List<SlabZone> RunLevels(params LevelSetup[] levels)
        {
            return RunLevels(false, levels);
        }

        /// <param name="loadsOnly">The load pass alone, as a re-check of the loads runs it.</param>
        private static List<SlabZone> RunLevels(bool loadsOnly, params LevelSetup[] levels)
        {
            List<Slab_E> areas = new List<Slab_E>();
            List<Slab_R> floors = new List<Slab_R>();
            List<Story_E> stories = new List<Story_E>();

            SlabCompareEngine engine = new SlabCompareEngine { LoadsOnly = loadsOnly };

            double elevation = 0;

            for (int i = 0; i < levels.Length; i++)
            {
                LevelSetup level = levels[i];

                areas.Add(EtabsArea(new List<AreaLoad_E> { Load("SDL", 3.0), Load("LLR", 2.0) },
                                    level.StoryOnSlab, elevation, "A" + i));
                floors.Add(RevitFloor(elevation, 500 + i, level.StoryOnSlab));
                stories.Add(new Story_E(level.StoryOnSlab, elevation, Form1.eLength.mm));

                if (level.PlanNumber > 0)
                {
                    engine.SetLoads(level.Story, LoadCoverage.Build(
                        new List<LoadRegion_R> { Region(1, level.PlanNumber, Rectangle(0, 0, Size, Size)) },
                        Schedule(), new List<string>()));
                }

                // Further apart than the check looks down for a drop panel, so one level's
                // floor is never mistaken for a thickening of the one above it.
                elevation += 4000;
            }

            engine.StartComparison(areas, floors, stories);

            LastIssues = engine.Issues.ToList();
            LastSlabZones = engine.Zones.ToList();

            // The load check reports on its own mesh now: the ETABS areas cut along the
            // loading plan's regions, not along the Revit floors. A level with no plan paired
            // to it produces none, which is what the "left exactly as it always was" test is
            // reading when it expects an empty list back.
            return engine.LoadZones.ToList();
        }

        #endregion

        #region building the inputs

        /// <summary>One ETABS bay under one loading region numbered 1, checked.</summary>
        private static SlabZone OneBay(double etabsSdl, double etabsLr, bool etabsHasLoads = true)
        {
            List<AreaLoad_E> loads = etabsHasLoads
                ? new List<AreaLoad_E> { Load("SDL", etabsSdl), Load("LLR", etabsLr) }
                : new List<AreaLoad_E>();

            return Run(loads, planNumber: 1);
        }

        private static SlabZone Run(List<AreaLoad_E> loads, int planNumber)
        {
            List<LoadRegion_R> regions = new List<LoadRegion_R>
            {
                Region(1, planNumber, Rectangle(0, 0, Size, Size))
            };

            List<SlabZone> zones = RunAll(loads, regions);
            List<SlabZone> loadChecked = zones.Where(zone => zone.WasLoadChecked).ToList();

            Assert.AreEqual(1, loadChecked.Count,
                            "expected the bay to come out as one zone, got " + loadChecked.Count);
            return loadChecked[0];
        }

        /// <summary>
        /// The whole check over one bay: one ETABS area, one Revit floor of the same outline
        /// and the same thickness, and whatever loading plan is handed in.
        /// </summary>
        private static List<SlabZone> RunAll(List<AreaLoad_E> loads, List<LoadRegion_R> regions)
        {
            Slab_E area = EtabsArea(loads);
            Slab_R floor = RevitFloor();

            LoadCoverage coverage = null;
            if (regions != null)
            {
                coverage = LoadCoverage.Build(regions, Schedule(), new List<string>());
            }

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, coverage);

            engine.StartComparison(new List<Slab_E> { area }, new List<Slab_R> { floor },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            LastSlabZones = engine.Zones.ToList();
            return engine.LoadZones.ToList();
        }

        /// <summary>
        /// A schedule with the two numbers the tests use: 1 asks for SDL 3.0 and LR 2.0, and 2
        /// asks for SDL 4.0 and LR 2.0, so an area carrying load 1 over both is wrong on the
        /// second half and only there. H1 asks for the same as 1, under a lettered label.
        /// </summary>
        private static LoadSchedule Schedule()
        {
            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Tower-Area Load");

            schedule.Add(new LoadScheduleEntry("1", "RESIDENTIAL-TYPE 1",
                new LoadSet { Sdl = 3.0, Lr = 2.0 }, "LLR"));
            schedule.Add(new LoadScheduleEntry("2", "RESIDENTIAL-TYPE 2",
                new LoadSet { Sdl = 4.0, Lr = 2.0 }, "LLR"));
            schedule.Add(new LoadScheduleEntry("H1", "HOTEL ROOM",
                new LoadSet { Sdl = 3.0, Lr = 2.0 }, "LLR"));

            return schedule;
        }

        private static Slab_E EtabsArea(List<AreaLoad_E> loads, string story = Story,
                                        double topElevation = 0, string name = "A1")
        {
            List<Joint> joints = Corners(0, 0, Size, Size, topElevation)
                .Select(point => new Joint("", point, Form1.eLength.mm))
                .ToList();

            Slab_E area = new Slab_E(name, joints, story, "SLAB200", SlabKind.Slab, 200, 200, false);

            if (loads != null)
            {
                area.SetLoads(loads);
            }

            return area;
        }

        private static Slab_R RevitFloor(double topElevation = 0, long revitId = 500,
                                         string story = Story)
        {
            PlanPolygon plan = Rectangle(0, 0, Size, Size);

            return new Slab_R(revitId, plan, topElevation, topElevation - 200)
            {
                Story = story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = "SLAB 200",
                StructuralThickness = 200
            };
        }

        /// <summary>A load already pressing down, for the tests that are not about direction.</summary>
        private static AreaLoad_E Load(string pattern, double value)
        {
            return RawLoad(pattern, value, 10);
        }

        /// <summary>
        /// A load built from the value ETABS actually stores against that direction, turned
        /// the right way up exactly as EtabsEngine.buildAreaLoad turns it. Raw rather than
        /// already normalised, so the tests about which way is down have something to test.
        /// </summary>
        private static AreaLoad_E RawLoad(string pattern, double rawValue, int direction)
        {
            double sign;
            bool gravity = AreaLoad_E.IsDownwardDirection(direction, out sign);

            return new AreaLoad_E(pattern, gravity ? sign * rawValue : rawValue,
                                  direction, gravity, false);
        }

        private static LoadRegion_R Region(long revitId, int number, PlanPolygon plan)
        {
            return new LoadRegion_R(revitId, new[] { plan }, number > 0 ? number.ToString() : "");
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        private static MyPoint[] Corners(double minX, double minY, double maxX, double maxY, double z = 0)
        {
            return new[]
            {
                new MyPoint(minX, minY, z),
                new MyPoint(maxX, minY, z),
                new MyPoint(maxX, maxY, z),
                new MyPoint(minX, maxY, z)
            };
        }

        #endregion
    }

    /// <summary>
    /// The pairings surviving a trip through the settings file, which is the whole of what
    /// "kept for you between sessions" means.
    /// </summary>
    [TestClass]
    public class LoadingPlanLinksTests
    {
        private const string Project = @"C:\jobs\Tower A\structure.rvt";

        private static LoadingPlanLinks WithLevels()
        {
            LoadingPlanLinks links = new LoadingPlanLinks();

            links.Set(Project, Paired("L03", "Tower-Area Load"));
            links.Set(Project, Paired("L04", "Tower-Area Load"));
            links.Set(Project, Paired("L05", ""));      // paired with a view, no sheet yet

            return links;
        }

        private static LoadingPlanLink Paired(string story, string sheet)
        {
            return new LoadingPlanLink
            {
                Story = story,
                ViewId = 1000,
                ViewName = story + " - LOADING PLAN",
                WorkbookPath = @"C:\jobs\Tower A\Loading Schedules.xlsx",
                SheetName = sheet
            };
        }

        [TestMethod]
        public void CheckingEveryLevelReadsEveryFinishedPairing()
        {
            // The bug this replaced: running over every level read no pairing at all, so a
            // user who had paired six levels and pressed run saw nothing and had no way to
            // tell that from the check being broken.
            List<LoadingPlanLink> usable = WithLevels().UsableFor(Project, everyStory: true, oneStory: null);

            CollectionAssert.AreEquivalent(new[] { "L03", "L04" },
                usable.Select(link => link.Story).ToList(),
                "every level with both a view and a sheet, and only those");
        }

        [TestMethod]
        public void ALevelPairedWithNoSheetIsNotReadYet()
        {
            List<LoadingPlanLink> usable = WithLevels().UsableFor(Project, everyStory: true, oneStory: null);

            Assert.IsFalse(usable.Any(link => link.Story == "L05"),
                           "half a pairing is somebody mid-way through, not something to check against");
        }

        [TestMethod]
        public void CheckingOneLevelReadsOnlyThatLevelsPairing()
        {
            List<LoadingPlanLink> usable = WithLevels().UsableFor(Project, everyStory: false, oneStory: "L04");

            Assert.AreEqual(1, usable.Count);
            Assert.AreEqual("L04", usable[0].Story);
        }

        [TestMethod]
        public void CheckingOneLevelThatNobodyPairedReadsNothing()
        {
            List<LoadingPlanLink> usable = WithLevels().UsableFor(Project, everyStory: false, oneStory: "L09");

            Assert.AreEqual(0, usable.Count);
        }

        [TestMethod]
        public void AnotherProjectsPairingsAreNeverRead()
        {
            List<LoadingPlanLink> usable = WithLevels()
                .UsableFor(@"C:\jobs\Tower B\structure.rvt", everyStory: true, oneStory: null);

            Assert.AreEqual(0, usable.Count, "the pairings are filed per project and stay there");
        }

        /// <summary>
        /// A typical floor's plan paired with the levels built to it. Only levels that read
        /// their line loads off it count towards sharing it, and every one of them is asked -
        /// not only the ones a run is checking - so a line is filed the same way whether its
        /// level is checked alone or with the rest.
        /// </summary>
        [TestMethod]
        public void AViewIsSharedForLineLoadsWhenMoreThanOneLevelReadsItsLinesOffIt()
        {
            LoadingPlanLinks links = new LoadingPlanLinks();

            links.Set(Project, WithLineSheet("L05", 2000));
            links.Set(Project, WithLineSheet("L06", 2000));
            links.Set(Project, WithLineSheet("L07", 2000));
            links.Set(Project, WithLineSheet("L08", 3000));

            // The same view as L08, for the area loads only: it reads no lines off it.
            LoadingPlanLink areaOnly = WithLineSheet("L09", 3000);
            areaOnly.LineLoadSheetName = "";
            links.Set(Project, areaOnly);

            CollectionAssert.AreEquivalent(new[] { 2000L },
                links.ViewsSharedForLineLoads(Project).ToList());
            Assert.AreEqual(0, links.ViewsSharedForLineLoads(@"C:\jobs\Tower B\structure.rvt").Count);
        }

        private static LoadingPlanLink WithLineSheet(string story, long viewId)
        {
            LoadingPlanLink link = Paired(story, "Tower-Area Load");
            link.ViewId = viewId;
            link.LineLoadSheetName = "Line Load D (TOWER)";
            return link;
        }

        [TestMethod]
        public void APairingSurvivesBeingWrittenOutAndReadBack()
        {
            LoadingPlanLinks saved = new LoadingPlanLinks();

            saved.Set(@"C:\jobs\Tower A\structure.rvt", new LoadingPlanLink
            {
                Story = "L03",
                ViewId = 4198573,
                ViewName = "L03 - LOADING PLAN",
                WorkbookPath = @"C:\jobs\Tower A\Loading Schedules.xlsx",
                SheetName = "Tower-Area Load"
            });

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { LoadingPlanLinks.Branch, saved.ToJson() }
            };

            // Out to text and back, the way the settings file goes.
            LoadingPlanLinks read = LoadingPlanLinks.From(Json.Read(Json.Write(view)));

            LoadingPlanLink link = read.Find(@"C:\jobs\Tower A\structure.rvt", "L03");

            Assert.IsNotNull(link, "the pairing did not survive the settings file");
            Assert.AreEqual(4198573, link.ViewId, "a Revit element id has to come back exact");
            Assert.AreEqual("L03 - LOADING PLAN", link.ViewName);
            Assert.AreEqual(@"C:\jobs\Tower A\Loading Schedules.xlsx", link.WorkbookPath,
                            "the backslashes in a Windows path have to survive being escaped");
            Assert.AreEqual("Tower-Area Load", link.SheetName);
        }

        [TestMethod]
        public void TwoProjectsDoNotReadEachOthersPairings()
        {
            LoadingPlanLinks links = new LoadingPlanLinks();

            links.Set(@"C:\a.rvt", new LoadingPlanLink { Story = "L03", ViewId = 1, ViewName = "A" });
            links.Set(@"C:\b.rvt", new LoadingPlanLink { Story = "L03", ViewId = 2, ViewName = "B" });

            Assert.AreEqual(1, links.Find(@"C:\a.rvt", "L03").ViewId);
            Assert.AreEqual(2, links.Find(@"C:\b.rvt", "L03").ViewId);
            Assert.IsNull(links.Find(@"C:\c.rvt", "L03"));
        }

        [TestMethod]
        public void TheWorkbookCarriesOverFromWhateverTheProjectIsAlreadyUsing()
        {
            LoadingPlanLinks links = new LoadingPlanLinks();

            Assert.AreEqual("", links.AnyWorkbook(@"C:\a.rvt"), "nothing to carry over at first");

            links.Set(@"C:\a.rvt", new LoadingPlanLink
            {
                Story = "L03",
                ViewId = 1,
                WorkbookPath = @"C:\jobs\Loading Schedules.xlsx",
                SheetName = "Tower-Area Load"
            });

            Assert.AreEqual(@"C:\jobs\Loading Schedules.xlsx", links.AnyWorkbook(@"C:\a.rvt"));
            Assert.AreEqual("", links.AnyWorkbook(@"C:\b.rvt"), "and not from another project");
        }
    }
}
