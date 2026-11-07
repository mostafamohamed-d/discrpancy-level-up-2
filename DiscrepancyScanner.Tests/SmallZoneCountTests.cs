using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The two sizes under which a difference in the slab stops being work.
    ///
    /// They are not a filter and they throw nothing away - that is
    /// <see cref="PrefrencesData.SlabMinimumZoneArea"/>, which is a hundredth of a square metre
    /// and drops the hairline two outlines leave behind when they were drawn to the same line.
    /// These are far larger and do something else entirely: the piece is cut, coloured on the
    /// plan and carded exactly as before, and only leaves the counts and the findings list. A
    /// hand's width of slab reading 5 mm out is worth seeing and is not a job.
    ///
    /// So everything here is one bargain read from both ends. What must stop happening is the
    /// counting; what must keep happening is all the rest of it, because a size that quietened
    /// the plan as well would be hiding differences rather than ranking them.
    /// </summary>
    [TestClass]
    public class SmallZoneCountTests
    {
        private const string Story = "L03";

        /// <summary>Half a square metre and one square metre, in the mm2 a zone is measured in.</summary>
        private const double ThicknessSize = 0.5e6;
        private const double LoadSize = 1e6;

        #region the size itself

        [TestMethod]
        public void APatchUnderTheThicknessSizeIsNotCountedAsWork()
        {
            SlabZone small = ThicknessMismatch(600, 600, ThicknessSize);

            Assert.IsTrue(small.IsTooSmallToCount,
                          "0.36 m2 is under the half metre the thickness check counts from");
            Assert.AreEqual(ResultLevel.Failed, small.Level,
                            "the verdict is untouched: the two models really do differ here");
        }

        [TestMethod]
        public void APatchOverTheThicknessSizeStillCounts()
        {
            SlabZone big = ThicknessMismatch(1000, 1000, ThicknessSize);

            Assert.IsFalse(big.IsTooSmallToCount, "a square metre is over the half metre size");
        }

        /// <summary>
        /// The size is a floor, not a window: a piece exactly on it is counted. Written down
        /// because a size typed as the smallest thing worth chasing should chase the thing it
        /// was typed as.
        /// </summary>
        [TestMethod]
        public void APatchExactlyOnTheSizeCounts()
        {
            SlabZone onIt = ThicknessMismatch(1000, 500, ThicknessSize);

            Assert.AreEqual(ThicknessSize, onIt.Area, 1d, "the patch is exactly the size");
            Assert.IsFalse(onIt.IsTooSmallToCount);
        }

        [TestMethod]
        public void ZeroCountsEveryPieceHoweverSmall()
        {
            SlabZone tiny = ThicknessMismatch(200, 200, 0);

            Assert.IsFalse(tiny.IsTooSmallToCount,
                           "zero is how the check is put back to counting everything");
        }

        /// <summary>
        /// A piece that is not being counted says so on its own verdict line. It is the only
        /// place it can be said: the piece is off the findings list, so the card reached by
        /// clicking the coloured patch is where somebody asks why the number beside it did not
        /// move, and an unexplained gap between the plan and the counts reads as a miscount.
        /// </summary>
        [TestMethod]
        public void APatchThatIsNotCountedSaysSoOnItsVerdict()
        {
            SlabZone small = ThicknessMismatch(600, 600, ThicknessSize);

            StringAssert.Contains(small.Summary, "not counted");
            StringAssert.Contains(small.Summary, "0.36 m2");
            StringAssert.Contains(small.Summary, "0.5 m2");

            // And it still says what it found first, which is the thing actually being reported.
            StringAssert.Contains(small.Summary, "250");
        }

        [TestMethod]
        public void APatchThatIsCountedSaysNothingAboutSizes()
        {
            SlabZone big = ThicknessMismatch(1000, 1000, ThicknessSize);

            Assert.IsFalse(big.Summary.Contains("not counted"), big.Summary);
        }

        #endregion

        #region each size answers for its own check

        [TestMethod]
        public void ThePatchUnderTheLoadSizeIsNotCountedAsWork()
        {
            SlabZone small = LoadMismatch(900, 900, LoadSize);

            Assert.IsTrue(small.IsTooSmallToCount,
                          "0.81 m2 is under the metre the load check counts from");
            Assert.AreEqual(ResultLevel.Failed, small.LoadLevel, "the loads still do not match");
            StringAssert.Contains(small.LoadSummary, "not counted");
        }

        [TestMethod]
        public void APatchOverTheLoadSizeStillCounts()
        {
            SlabZone big = LoadMismatch(1200, 1200, LoadSize);

            Assert.IsFalse(big.IsTooSmallToCount);
            Assert.IsFalse(big.LoadSummary.Contains("not counted"), big.LoadSummary);
        }

        /// <summary>
        /// The two meshes are cut along different edges and asked different questions, so a
        /// piece of one is never measured against the other's size. A piece of the load mesh
        /// also carries a line saying it was not cut for thickness, and that line must not pick
        /// up the sentence about what is not being counted - it is about the wrong check.
        /// </summary>
        [TestMethod]
        public void TheLoadSizeIsSaidOnTheLoadLineAndNowhereElse()
        {
            SlabZone small = LoadMismatch(900, 900, LoadSize);

            Assert.IsFalse(small.Summary.Contains("not counted"),
                           "the slab line on a load zone says nothing about the load size: "
                           + small.Summary);
        }

        /// <summary>
        /// A patch one model has and the other has not is a different question. The slivers
        /// those leave behind are already the smallest-zone size's to throw away, and these
        /// sizes are not to start quietening outlines they were never written for.
        /// </summary>
        [TestMethod]
        public void AnOutlineThatOnlyOneModelHasIsCountedWhateverItsSize()
        {
            SlabZone sliver = Zone(Rectangle(0, 0, 300, 300), SlabZoneState.MissingInRevit,
                                   SlabMeshPurpose.Slab, ThicknessSize);

            Assert.IsFalse(sliver.IsTooSmallToCount,
                           "the thickness size does not answer for the outline check");
        }

        /// <summary>A piece the two models agree on is not a finding, so there is nothing to quieten.</summary>
        [TestMethod]
        public void APieceTheTwoModelsAgreeOnIsNeverMarked()
        {
            SlabZone agreed = Zone(Rectangle(0, 0, 400, 400), SlabZoneState.Matched,
                                   SlabMeshPurpose.Slab, ThicknessSize);

            Assert.IsFalse(agreed.IsTooSmallToCount);
            Assert.IsFalse(agreed.Summary.Contains("not counted"), agreed.Summary);
        }

        #endregion

        #region what the window does with it

        [TestMethod]
        public void ASmallPatchIsNotCountedOnTheChipOrTheRail()
        {
            ElementViewModel small = Element(ThicknessMismatch(600, 600, ThicknessSize));
            ElementViewModel ordinary = Element(ThicknessMismatch(2000, 2000, ThicknessSize));

            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new List<Story_E> { new Story_E(Story, 10000, Form1.eLength.mm) });
            tally.Update(new List<ElementViewModel> { small, ordinary }, Story);

            ScopeTab slabs = tally.Tabs.First(tab => tab.Mode == ViewMode.Slabs);
            Assert.AreEqual(1, slabs.Failed, "only the patch worth going to should be counted");
            Assert.AreEqual(1, tally.Levels[0].Failed, "and the rail says which floor to go to");
        }

        [TestMethod]
        public void ASmallPatchIsLeftOutOfTheFindingsList()
        {
            ElementViewModel small = Element(ThicknessMismatch(600, 600, ThicknessSize));
            ElementViewModel ordinary = Element(ThicknessMismatch(2000, 2000, ThicknessSize));

            FindingsList drawer = new FindingsList();
            drawer.Update(new List<ElementViewModel> { small, ordinary }, null, null);

            Assert.AreEqual(1, drawer.Rows.Count, "the list is the job list, and this is not a job");
            Assert.AreSame(ordinary, drawer.Rows[0].Element);
            Assert.AreEqual(1, drawer.Open);
        }

        /// <summary>
        /// And the half that must not change. The plan is where the difference is still shown,
        /// so a patch that has left the counts is drawn in exactly the colour it always was -
        /// recolouring it would be settling it, which nobody has done.
        /// </summary>
        [TestMethod]
        public void ASmallPatchIsStillDrawnAsWhatItIs()
        {
            ElementViewModel small = Element(ThicknessMismatch(600, 600, ThicknessSize));
            small.DisplayState = CheckState.Failed;

            Assert.AreEqual(ViewPalette.Failed, ViewPalette.ForState(small),
                            "the plan still shows what the counts stopped counting");
        }

        /// <summary>
        /// It can still be looked at, agreed and noted like anything else. The card is reached
        /// by clicking the patch on the plan, and a difference somebody wants to record an
        /// opinion about does not stop having one because it is small.
        /// </summary>
        [TestMethod]
        public void ASmallPatchCanStillBeFiled()
        {
            ElementViewModel small = Element(ThicknessMismatch(600, 600, ThicknessSize));

            Assert.IsTrue(small.CanBeFiled);
            Assert.IsTrue(new ElementCard(small, null).Rows.Count > 0,
                          "the card still says what differs here");
        }

        #endregion

        #region the settings reach the check

        /// <summary>
        /// The whole run, from the number typed in the settings window to the piece it quietens.
        /// Everything above builds a zone with the size handed to it; this is the one test that
        /// the check reads the setting at all, and reads it in the m2 the row is labelled in.
        /// </summary>
        [TestMethod]
        public void TheCheckReadsTheSizesOutOfTheSettingsInSquareMetres()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            double thickness = pref.SlabThicknessMinimumIssueArea;
            try
            {
                pref.SlabThicknessMinimumIssueArea = 4;

                List<SlabZone> zones = Run();
                SlabZone thicker = zones.First(zone => zone.State == SlabZoneState.ThicknessMismatch);

                Assert.AreEqual(4e6, thicker.MinimumIssueArea, 1d,
                                "4 m2 typed in the settings is 4 million mm2 in the check");
                Assert.IsTrue(thicker.IsTooSmallToCount,
                              $"the 2 m by 2 m drop is under 4 m2: {thicker.Summary}");

                pref.SlabThicknessMinimumIssueArea = 1;
                thicker = Run().First(zone => zone.State == SlabZoneState.ThicknessMismatch);
                Assert.IsFalse(thicker.IsTooSmallToCount, "and over 1 m2 it is work again");
            }
            finally
            {
                pref.SlabThicknessMinimumIssueArea = thickness;
            }
        }

        #endregion

        #region building the inputs

        /// <summary>
        /// A piece of the slab mesh whose two thicknesses differ, built at the size that is the
        /// point of the test. 250 of Revit against 200 of ETABS, which is a real difference by
        /// any buffer anybody would set.
        /// </summary>
        private static SlabZone ThicknessMismatch(double width, double height, double size)
        {
            return Zone(Rectangle(0, 0, width, height), SlabZoneState.ThicknessMismatch,
                        SlabMeshPurpose.Slab, size);
        }

        /// <summary>
        /// A piece of the load mesh carrying an SDL the loading plan did not ask for. The load
        /// mesh says nothing about thickness, so the zone's own state is NotChecked, exactly as
        /// the check leaves it.
        /// </summary>
        private static SlabZone LoadMismatch(double width, double height, double size)
        {
            SlabZone zone = new SlabZone(Rectangle(0, 0, width, height),
                                         SlabZoneState.NotChecked, Story)
            {
                Purpose = SlabMeshPurpose.Loads,
                MinimumIssueArea = size,
                EtabsName = "A1",
                EtabsProperty = "SLAB200",
                LoadNumber = "1",
                LoadDescription = "RESIDENTIAL-TYPE 1",
                PlannedLoads = new LoadSet { Sdl = 3.0 },
                EtabsLoads = new LoadSet { Sdl = 4.0 },
                LoadState = SlabLoadState.Mismatch,
                MismatchedLoads = new List<LoadCategory> { LoadCategory.Sdl }
            };
            zone.BuildResults();
            return zone;
        }

        private static SlabZone Zone(PlanPolygon plan, SlabZoneState state,
                                     SlabMeshPurpose purpose, double size)
        {
            SlabZone zone = new SlabZone(plan, state, Story)
            {
                Purpose = purpose,
                MinimumIssueArea = size,
                EtabsName = "A1",
                EtabsProperty = "SLAB200",
                EtabsThickness = 200,
                RevitId = state == SlabZoneState.MissingInRevit ? 0 : 500,
                RevitThickness = state == SlabZoneState.MissingInRevit ? 0 : 250
            };

            if (state == SlabZoneState.Matched)
            {
                zone.RevitThickness = 200;
            }

            zone.BuildResults();
            return zone;
        }

        private static ElementViewModel Element(SlabZone zone)
        {
            ElementViewModel element = ElementViewModelFactory.FromSlabZone(zone);
            element.DisplayState = CheckState.Failed;
            return element;
        }

        /// <summary>
        /// One 6 m ETABS bay of 200 slab with a 2 m by 2 m drop panel modelled in Revit under
        /// the middle of it, run through the real check. The drop is the mismatched piece, and
        /// at 4 m2 it sits either side of the sizes the test moves.
        /// </summary>
        private static List<SlabZone> Run()
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(
                new List<Slab_E> { Shell() },
                new List<Slab_R>
                {
                    Floor(500, Rectangle(0, 0, 6000, 6000), 200),
                    Floor(501, Rectangle(2000, 2000, 4000, 4000), 300)
                },
                new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            return engine.Zones.ToList();
        }

        private static Slab_E Shell()
        {
            List<Joint> joints = Corners(0, 0, 6000, 6000)
                .Select(point => new Joint("", point, Form1.eLength.mm)).ToList();

            return new Slab_E("A1", joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
        }

        /// <summary>
        /// A floor hung from the story plane, so the 300 one reads as the drop panel it is
        /// meant to be rather than as a floor on the level below.
        /// </summary>
        private static Slab_R Floor(long revitId, PlanPolygon plan, double thickness)
        {
            return new Slab_R(revitId, plan, 0, -thickness)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = "SLAB " + thickness,
                StructuralThickness = thickness
            };
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        private static MyPoint[] Corners(double minX, double minY, double maxX, double maxY)
        {
            return new[]
            {
                new MyPoint(minX, minY, 0),
                new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0),
                new MyPoint(minX, maxY, 0)
            };
        }

        #endregion
    }
}
