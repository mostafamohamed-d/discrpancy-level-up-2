using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The SIDL_C area check: a light weight fill floor, weighed, against the SIDL_C area load
    /// on the ETABS slab under it.
    ///
    /// What is pinned here is the point of it: a ramp averaged whole, in halves or in five
    /// strips all carry the same total, and all of them read back as the same average.
    /// </summary>
    [TestClass]
    public class SidlCAreaCheckTests
    {
        private const string Story = "L01";

        #region what a fill weighs

        /// <summary>The depth is read off the faces place by place: a quarter of the way up the ramp, 125.</summary>
        [TestMethod]
        public void TheDepthIsReadPlaceByPlace()
        {
            SidlCAreaSource_R ramp = Ramp(0, 0, 10000, 4000, 0, 500);

            Assert.AreEqual(125, ramp.DepthAt(2500, 2000).Value, 1e-6);
            Assert.AreEqual(450, ramp.DepthAt(9000, 2000).Value, 1e-6);
            Assert.IsNull(ramp.DepthAt(20000, 2000), "off the fill");
        }

        /// <summary>
        /// A ramp from 0 to 500 mm, LWC at 10 and 100 mm of topping at 25:
        /// (0 + 0.5) / 2 x 10 + 0.1 x 15 = 4 kN/m2.
        /// </summary>
        [TestMethod]
        public void ARampWeighsItsAverageDepthPlusTheToppingDifference()
        {
            SidlCAreaSource_R ramp = Ramp(0, 0, 10000, 4000, 0, 500);

            Assert.AreEqual(250, ramp.AverageThickness, 1e-6);
            Assert.AreEqual(4.0, ramp.PerSquareMetre(10, 25, 100), 1e-9);
            StringAssert.Contains(ramp.How(10, 25, 100), "0 mm to 500 mm, 250 mm on average");
            StringAssert.Contains(ramp.How(10, 25, 100), "= 4 kN/m2");
        }

        /// <summary>A flat fill is its depth, the same formula with the two ends the same.</summary>
        [TestMethod]
        public void AFlatFillWeighsItsDepthPlusTheToppingDifference()
        {
            // 0.3 x 10 + 0.1 x 15 = 4.5
            Assert.AreEqual(4.5, Ramp(0, 0, 5000, 5000, 300, 300).PerSquareMetre(10, 25, 100), 1e-9);
        }

        /// <summary>
        /// 306 mm on average: 0.306 x 10 + 0.1 x 15 = 4.56, typed into ETABS as 5 - rounded up to
        /// the next 0.5, and said so. A weight already on a step stays where it is.
        /// </summary>
        [TestMethod]
        public void TheWeightIsRoundedUpToTheNextStep()
        {
            SidlCAreaSource_R ramp = Ramp(0, 0, 10000, 4000, 0, 612);

            Assert.AreEqual(4.56, ramp.PerSquareMetre(10, 25, 100), 1e-9);
            Assert.AreEqual(5.0, ramp.PerSquareMetre(10, 25, 100, 0.5), 1e-9);
            StringAssert.Contains(ramp.How(10, 25, 100, 0.5), "= 4.56 kN/m2, rounded up to 5 kN/m2");
            Assert.IsFalse(ramp.How(10, 25, 100).Contains("rounded"), "nought leaves it as it is");

            SidlCAreaSource_R flat = Ramp(0, 0, 5000, 5000, 300, 300);
            Assert.AreEqual(4.5, flat.PerSquareMetre(10, 25, 100, 0.5), 1e-9);
            Assert.IsFalse(flat.How(10, 25, 100, 0.5).Contains("rounded"), "4.5 is already on a step");
        }

        /// <summary>
        /// ETABS carrying the rounded 5 matches a fill weighing 4.56 under a tight tolerance only
        /// once the fill's weight is rounded - the whole fill's and its band's alike.
        /// </summary>
        [TestMethod]
        public void TheRoundedWeightIsWhatIsCompared()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.Band = new LoadBand(0.1, 0.1);
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 612) }, new[] { Shell("A1", 0, 0, 10000, 4000, 5.0) });
            Assert.AreEqual(SidlCAreaState.ValueMismatch, engine.Results.Single().State, "unrounded, 4.56 against 5");

            engine.RoundUpTo = 0.5;
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 612) }, new[] { Shell("A1", 0, 0, 10000, 4000, 5.0) });
            SidlCAreaResult result = engine.Results.Single();

            Assert.AreEqual(SidlCAreaState.Matched, result.State, result.Summary);
            Assert.AreEqual(5.0, result.Expected, 1e-9);
            Assert.AreEqual(5.0, result.Bands.Single().Weighs, 1e-9);
        }

        #endregion

        #region however the engineer averaged it

        /// <summary>The ramp averaged whole: one load of 4 over all of it.</summary>
        [TestMethod]
        public void ARampAveragedWholeMatches()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 10000, 4000, 4.0));

            Assert.AreEqual(SidlCAreaState.Matched, result.State);
            Assert.AreEqual(4.0, result.Actual, 1e-6);
            Assert.AreEqual(1, result.Bands.Count);
        }

        /// <summary>
        /// The same ramp in two halves, each its own average: 0-250 is 2.75, 250-500 is 5.25.
        /// Neither is 4, and together they are.
        /// </summary>
        [TestMethod]
        public void ARampAveragedInHalvesMatches()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 5000, 4000, 2.75),
                                           Shell("A2", 5000, 0, 10000, 4000, 5.25));

            Assert.AreEqual(SidlCAreaState.Matched, result.State, result.Summary);
            Assert.AreEqual(4.0, result.Actual, 0.05);
            Assert.AreEqual(2, result.Bands.Count);
            StringAssert.Contains(result.Summary, "matches band by band");
            Assert.IsTrue(result.Bands.All(band => band.Agrees));
        }

        /// <summary>In five strips, 2, 3, 4, 5 and 6: still 4 on average.</summary>
        [TestMethod]
        public void ARampAveragedInFiveStripsMatches()
        {
            List<Slab_E> strips = Enumerable.Range(0, 5)
                .Select(i => Shell("S" + i, i * 2000, 0, (i + 1) * 2000, 4000, 2 + i))
                .ToList();

            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), strips.ToArray());

            Assert.AreEqual(SidlCAreaState.Matched, result.State, result.Summary);
            Assert.AreEqual(4.0, result.Actual, 0.01);
            Assert.AreEqual(5, result.Bands.Count);
        }

        /// <summary>
        /// The ramp's peak load put over all of it is a heavy load, not an average, and is
        /// caught; so is the thin end's put over all of it.
        /// </summary>
        [TestMethod]
        public void TheWrongLoadOverTheWholeRampDoesNotMatch()
        {
            Assert.AreEqual(SidlCAreaState.ValueMismatch,
                            Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("A1", 0, 0, 10000, 4000, 6.5)).State);
            Assert.AreEqual(SidlCAreaState.ValueMismatch,
                            Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("A1", 0, 0, 10000, 4000, 1.5)).State);
        }

        /// <summary>
        /// Half the ramp averaged and the other half left out comes back as half the load,
        /// and the card says how much of the fill carries nothing.
        /// </summary>
        [TestMethod]
        public void HalfARampLoadedIsHalfTheLoad()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 5000, 4000, 2.75),
                                           Shell("A2", 5000, 0, 10000, 4000, 0));

            Assert.AreEqual(SidlCAreaState.ValueMismatch, result.State);
            Assert.AreEqual(1.375, result.Actual, 0.05);
            StringAssert.Contains(result.Summary, "ETABS carries no SIDL_C over 20 m2 where the fill weighs 5.25 kN/m2");
        }

        #endregion

        #region the band

        /// <summary>ETABS may carry up to 1 more than the fill weighs, and up to 0.5 less.</summary>
        [TestMethod]
        public void HeavyIsLetThroughFurtherThanLight()
        {
            SidlCAreaSource_R ramp = Ramp(0, 0, 10000, 4000, 0, 500);

            Assert.AreEqual(SidlCAreaState.Matched, Check(ramp, Shell("A", 0, 0, 10000, 4000, 5.0)).State, "1 over");
            Assert.AreEqual(SidlCAreaState.ValueMismatch, Check(ramp, Shell("A", 0, 0, 10000, 4000, 5.1)).State, "1.1 over");
            Assert.AreEqual(SidlCAreaState.Matched, Check(ramp, Shell("A", 0, 0, 10000, 4000, 3.5)).State, "0.5 under");
            Assert.AreEqual(SidlCAreaState.ValueMismatch, Check(ramp, Shell("A", 0, 0, 10000, 4000, 3.4)).State, "0.6 under");
        }

        #endregion

        #region what ETABS has under it

        /// <summary>Only SIDL_C is read: a load in another pattern on the same slab is not the fill.</summary>
        [TestMethod]
        public void OnlyTheSidlCPatternIsRead()
        {
            Slab_E slab = Shell("A1", 0, 0, 10000, 4000, 0);
            slab.SetLoads(new[]
            {
                new AreaLoad_E("SIDL", 1.5, 10, true, false),
                new AreaLoad_E("SIDL_C", 4.0, 10, true, false)
            });

            Assert.AreEqual(4.0, Check(Ramp(0, 0, 10000, 4000, 0, 500), slab).Actual, 1e-6);
        }

        /// <summary>The fill read off SIDL_LWC as well as SIDL_C: both are named, so both are its load.</summary>
        [TestMethod]
        public void EveryNamedPatternIsTheFillsLoad()
        {
            Slab_E slab = Shell("A1", 0, 0, 10000, 4000, 0);
            slab.SetLoads(new[]
            {
                new AreaLoad_E("SIDL", 1.5, 10, true, false),
                new AreaLoad_E("SIDL_LWC", 4.0, 10, true, false)
            });

            Assert.AreEqual(4.0, Check(Ramp(0, 0, 10000, 4000, 0, 500), slab).Actual, 1e-6);
        }

        /// <summary>
        /// A slab under the fill with no SIDL_C on it is one model having the fill and the other
        /// not, reported the way an unpaired element is.
        /// </summary>
        [TestMethod]
        public void NoSidlCUnderTheFillIsMissingInEtabs()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("A1", 0, 0, 10000, 4000, 0));

            Assert.AreEqual(SidlCAreaState.NoEtabsLoad, result.State);
            Assert.IsTrue(result.IsMissingInEtabs);
            Assert.AreEqual(ResultLevel.Failed, result.Level);
        }

        /// <summary>
        /// No slab under the fill at all is the load forgotten: averaged over the whole fill it
        /// is nothing, and it is reported as the load not being there.
        /// </summary>
        [TestMethod]
        public void NoSlabUnderTheFillIsTheLoadNotThere()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("FAR", 50000, 0, 60000, 4000, 4));

            Assert.AreEqual(SidlCAreaState.NoEtabsLoad, result.State);
            Assert.IsTrue(result.IsMissingInEtabs);
            Assert.IsTrue(result.AveragedOverWholeFill);
            Assert.AreEqual(0, result.Actual, 1e-9);
            StringAssert.Contains(result.Summary, "is not in ETABS");
        }

        /// <summary>
        /// Most of the fill with no slab under it is averaged over the whole fill, the bare part
        /// as nothing - and judged on that like any other.
        /// </summary>
        [TestMethod]
        public void MostlyBareIsAveragedOverTheWholeFill()
        {
            // A flat 4.5 kN/m2 fill with slab under 3 m of its 10.
            SidlCAreaSource_R fill = Ramp(0, 0, 10000, 4000, 300, 300);

            SidlCAreaResult light = Check(fill, Shell("A1", 0, 0, 3000, 4000, 4.5));
            Assert.IsTrue(light.AveragedOverWholeFill);
            Assert.AreEqual(1.35, light.Actual, 0.05);
            Assert.AreEqual(SidlCAreaState.ValueMismatch, light.State);
            StringAssert.Contains(light.Summary, "averaged over the whole fill");

            // The whole fill's load heaped on the 3 m of slab there is: the same total, but every
            // band is wrong where it lies, and all of it lies on the fill - so it fails.
            SidlCAreaResult whole = Check(fill, Shell("A1", 0, 0, 3000, 4000, 15));
            Assert.AreEqual(SidlCAreaState.ValueMismatch, whole.State, whole.Summary);
        }

        /// <summary>
        /// A fill running on past the slab edge is averaged over the slab it stands on: the
        /// engineer averaged over the slab, and past its edge there is nothing to load.
        /// </summary>
        [TestMethod]
        public void TheAverageIsOverTheSlabTheFillStandsOn()
        {
            // A flat fill 4.5 kN/m2, 10 m long, with the slab under 7 m of it.
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 300, 300), Shell("A1", 0, 0, 7000, 4000, 4.5));

            Assert.AreEqual(SidlCAreaState.Matched, result.State, result.Summary);
            Assert.AreEqual(0.7, result.CoveredShare, 0.02);
        }

        /// <summary>
        /// A null area drawn over the slab for the load adds to the slab under it, as on the
        /// slab load check.
        /// </summary>
        [TestMethod]
        public void ALoadAreaOverTheSlabAdds()
        {
            Slab_E slab = Shell("SLAB", -1000, -1000, 11000, 5000, 0);
            Slab_E loadArea = Shell("LOAD", 0, 0, 10000, 4000, 4.0, SlabKind.LoadOnly);

            Assert.AreEqual(SidlCAreaState.Matched, Check(Ramp(0, 0, 10000, 4000, 0, 500), slab, loadArea).State);
        }

        /// <summary>
        /// SIDL_C on an area with no fill over any of it is reported; one with fill over part of
        /// it - a ramp averaged over the whole panel - is not.
        /// </summary>
        [TestMethod]
        public void SidlCWithNoFillOverItIsReported()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 500) },
                                   new[]
                                   {
                                       Shell("UNDER", -2000, -2000, 12000, 6000, 3.0),
                                       Shell("ELSEWHERE", 30000, 0, 36000, 6000, 2.0),
                                       Shell("BARE", 40000, 0, 46000, 6000, 0)
                                   });

            CollectionAssert.AreEqual(new[] { "ELSEWHERE" },
                                      engine.Unclaimed.Select(one => one.Shell.UniqueName).ToList());
        }

        /// <summary>A fill on another story is not read against this story's slab.</summary>
        [TestMethod]
        public void AFillIsReadAgainstItsOwnStory()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.SetStoriesToCheck(new[] { Story });
            SidlCAreaSource_R upstairs = Ramp(0, 0, 10000, 4000, 0, 500, "L02");

            engine.StartComparison(new[] { upstairs }, new[] { Shell("A1", 0, 0, 10000, 4000, 4.0) });

            Assert.AreEqual(0, engine.Results.Count, "L02 is not checked");
            Assert.AreEqual(1, engine.Unclaimed.Count, "and nothing on L01 claims the SIDL_C there");
        }

        #endregion

        #region when the ETABS load area is not the fill's

        /// <summary>
        /// The fill's 160 kN spread over a panel twice its size reads half the load under the
        /// fill - but the total is all there, so it is up for review, not failed.
        /// </summary>
        [TestMethod]
        public void TheSameTotalOverABiggerAreaIsForReview()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("PANEL", 0, 0, 10000, 8000, 2.0));

            Assert.AreEqual(SidlCAreaState.EquivalentTotal, result.State, result.Summary);
            Assert.AreEqual(ResultLevel.Review, result.Level);
            Assert.AreEqual(160, result.RevitForce, 1e-6);
            Assert.AreEqual(160, result.EtabsForce, 1e-6);
            StringAssert.Contains(result.Summary, "the same total load, put on a different area");
        }

        /// <summary>The fill's own load over a bigger panel matches, and the card says how much lies outside it.</summary>
        [TestMethod]
        public void TheRightLoadOverABiggerAreaMatchesAndSaysSo()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("PANEL", 0, 0, 10000, 8000, 4.0));

            Assert.AreEqual(SidlCAreaState.Matched, result.State);
            StringAssert.Contains(result.Summary, "40 m2 of the SIDL_C area under it lies outside the fill");
        }

        /// <summary>
        /// The load drawn 3 m along from where the fill is reads 70% of it under the fill, but
        /// the whole of it on an area the fill stands on: the same total, for review.
        /// </summary>
        [TestMethod]
        public void TheLoadShiftedOffTheFillIsTheSameTotal()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("SLAB", -5000, -5000, 20000, 9000, 0),
                                           Shell("SHIFTED", 3000, 0, 13000, 4000, 4.0, SlabKind.LoadOnly));

            Assert.AreEqual(2.8, result.Actual, 0.05);
            Assert.AreEqual(SidlCAreaState.EquivalentTotal, result.State, result.Summary);
        }

        /// <summary>A wrong total is still wrong, however the area is drawn.</summary>
        [TestMethod]
        public void ATotalThatIsShortStillFails()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("PANEL", 0, 0, 10000, 8000, 1.0));

            Assert.AreEqual(SidlCAreaState.ValueMismatch, result.State);
            StringAssert.Contains(result.Summary, "in all it weighs 160 kN and ETABS carries 80 kN");
        }

        /// <summary>
        /// One panel under two fills is shared between them by how much of each stands on it,
        /// so its force is not counted twice.
        /// </summary>
        [TestMethod]
        public void APanelUnderTwoFillsIsSharedBetweenThem()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 500), Ramp(20000, 0, 30000, 4000, 0, 500) },
                                   new[] { Shell("PANEL", 0, 0, 30000, 4000, 320.0 / 120) });

            foreach (SidlCAreaResult result in engine.Results)
            {
                Assert.AreEqual(160, result.EtabsForce, 1, "half the panel's 320 kN each");
                Assert.AreEqual(SidlCAreaState.EquivalentTotal, result.State, result.Summary);
            }
        }

        /// <summary>
        /// No SIDL_C under the fill and SIDL_C a metre beside it with no fill over it is one load
        /// in the wrong place: reported on the fill, and not again on its own.
        /// </summary>
        [TestMethod]
        public void SidlCBesideABareFillIsItsLoadMisplaced()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 500) },
                                   new[] { Shell("BESIDE", 11000, 0, 21000, 4000, 4.0) });

            SidlCAreaResult result = engine.Results.Single();
            Assert.AreEqual(SidlCAreaState.Misplaced, result.State);
            Assert.AreEqual(ResultLevel.Failed, result.Level);
            Assert.IsFalse(result.IsMissingInEtabs);
            CollectionAssert.AreEqual(new[] { "BESIDE" }, result.Misplaced.ToList());
            StringAssert.Contains(result.Summary, "is not under it");
            Assert.AreEqual(0, engine.Unclaimed.Count);
        }

        /// <summary>Further off than the setting, the two are reported apart.</summary>
        [TestMethod]
        public void SidlCFarFromABareFillIsReportedApart()
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.StartComparison(new[] { Ramp(0, 0, 10000, 4000, 0, 500) },
                                   new[] { Shell("AWAY", 15000, 0, 25000, 4000, 4.0) });

            Assert.AreEqual(SidlCAreaState.NoEtabsLoad, engine.Results.Single().State);
            Assert.AreEqual(1, engine.Unclaimed.Count);
        }

        #endregion

        #region cut into pieces

        /// <summary>
        /// The fill's load on a panel twice its size is two pieces: the fill, in its verdict,
        /// and the half of the panel outside it, in the ETABS only colour.
        /// </summary>
        [TestMethod]
        public void LoadOutsideTheFillIsAPieceOfItsOwn()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("PANEL", 0, 0, 10000, 8000, 4.0));

            AssertPieces(result,
                         Tuple.Create(SidlCAreaPieceKind.OnFill, 4.0, 40.0),
                         Tuple.Create(SidlCAreaPieceKind.OutsideFill, 4.0, 40.0));

            ElementViewModel outside = ElementViewModelFactory.FromSidlCAreaPiece(
                result.Pieces.Single(piece => piece.Kind == SidlCAreaPieceKind.OutsideFill));
            Assert.AreEqual(ElementSource.UnmatchedEtabs, outside.Source);
            Assert.AreEqual(CheckState.Unpaired, outside.State);
            Assert.AreEqual(CheckKind.SidlCAreaMissingInRevit, outside.Outcomes.Single().Kind);
        }

        /// <summary>
        /// The load shifted 3 m along: the fill with load, the fill without (Revit only), and
        /// the load past the fill (ETABS only).
        /// </summary>
        [TestMethod]
        public void AShiftedLoadIsCutIntoThreePieces()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("SLAB", -5000, -5000, 20000, 9000, 0),
                                           Shell("SHIFTED", 3000, 0, 13000, 4000, 4.0, SlabKind.LoadOnly));

            AssertPieces(result,
                         Tuple.Create(SidlCAreaPieceKind.OnFill, 4.0, 28.0),
                         Tuple.Create(SidlCAreaPieceKind.FillBare, 0.0, 12.0),
                         Tuple.Create(SidlCAreaPieceKind.OutsideFill, 4.0, 12.0));

            ElementViewModel bare = ElementViewModelFactory.FromSidlCAreaPiece(
                result.Pieces.Single(piece => piece.Kind == SidlCAreaPieceKind.FillBare));
            Assert.AreEqual(ElementSource.UnmatchedRevit, bare.Source);
            Assert.AreEqual(CheckKind.SidlCAreaMissingInEtabs, bare.Outcomes.Single().Kind);
        }

        /// <summary>A ramp in two halves is two pieces, each with its own load and its own verdict.</summary>
        [TestMethod]
        public void EachBandIsAPieceWithItsOwnVerdict()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 5000, 4000, 2.75),
                                           Shell("A2", 5000, 0, 10000, 4000, 5.25));

            AssertPieces(result,
                         Tuple.Create(SidlCAreaPieceKind.OnFill, 2.75, 20.0),
                         Tuple.Create(SidlCAreaPieceKind.OnFill, 5.25, 20.0));
            Assert.IsTrue(result.Pieces.All(piece => piece.Level == ResultLevel.Passed));
        }

        /// <summary>
        /// The bands the wrong way round - the deep half's load on the thin half - add up to the
        /// right total and the right average, and both halves fail.
        /// </summary>
        [TestMethod]
        public void BandsTheWrongWayRoundFail()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 5000, 4000, 5.25),
                                           Shell("A2", 5000, 0, 10000, 4000, 2.75));

            Assert.AreEqual(4.0, result.Actual, 0.05, "the average is right");
            Assert.AreEqual(SidlCAreaState.ValueMismatch, result.State, result.Summary);
            Assert.IsTrue(result.Pieces.All(piece => piece.Level == ResultLevel.Failed));
            StringAssert.Contains(result.Summary, "ETABS carries 5.25 kN/m2 over 20 m2 where the fill weighs 2.75 kN/m2");
        }

        /// <summary>One band right and one wrong: the plan is half green and half red.</summary>
        [TestMethod]
        public void OneBandWrongIsOnlyThatBand()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500),
                                           Shell("A1", 0, 0, 5000, 4000, 2.75),
                                           Shell("A2", 5000, 0, 10000, 4000, 3.5));

            Assert.AreEqual(SidlCAreaState.ValueMismatch, result.State);
            Assert.AreEqual(ResultLevel.Passed, result.Pieces.Single(piece => piece.Load == 2.75).Level);
            Assert.AreEqual(ResultLevel.Failed, result.Pieces.Single(piece => piece.Load == 3.5).Level);
        }

        /// <summary>A misplaced load is the bare fill and the area beside it, both drawn.</summary>
        [TestMethod]
        public void AMisplacedLoadIsTheBareFillAndTheAreaBesideIt()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("BESIDE", 11000, 0, 21000, 4000, 4.0));

            AssertPieces(result,
                         Tuple.Create(SidlCAreaPieceKind.FillBare, 0.0, 40.0),
                         Tuple.Create(SidlCAreaPieceKind.OutsideFill, 4.0, 40.0));
            StringAssert.Contains(result.Pieces.Single(piece => piece.Kind == SidlCAreaPieceKind.OutsideFill).Summary,
                                  "most likely its load drawn in the wrong place");
        }

        /// <summary>The kind, load and area (m2) of each piece, in any order.</summary>
        private static void AssertPieces(SidlCAreaResult result, params Tuple<SidlCAreaPieceKind, double, double>[] expected)
        {
            List<string> actual = result.Pieces
                .Select(piece => $"{piece.Kind} {piece.Load:0.##} {piece.Plan.Area / 1e6:0.#}")
                .OrderBy(text => text)
                .ToList();
            List<string> wanted = expected
                .Select(one => $"{one.Item1} {one.Item2:0.##} {one.Item3:0.#}")
                .OrderBy(text => text)
                .ToList();

            CollectionAssert.AreEqual(wanted, actual, string.Join("; ", actual));
        }

        #endregion

        #region the tab

        /// <summary>Off to begin with, and never up with either slab tab: all three are regions of floor.</summary>
        [TestMethod]
        public void TheAreaTabIsNeverUpWithTheSlabTabs()
        {
            FindingTally tally = new FindingTally(() => { });
            Assert.IsFalse(tally.IsActive(ViewMode.SidlCArea));
            Assert.IsTrue(tally.IsActive(ViewMode.Slabs));

            tally.ToggleTab(ViewMode.SidlCArea);
            Assert.IsTrue(tally.IsActive(ViewMode.SidlCArea));
            Assert.IsFalse(tally.IsActive(ViewMode.Slabs));
            Assert.IsFalse(tally.IsActive(ViewMode.SlabLoads));

            tally.ToggleTab(ViewMode.SlabLoads);
            Assert.IsFalse(tally.IsActive(ViewMode.SidlCArea), "and the other way round");
        }

        /// <summary>A fill's element is counted on the SIDL_C area tab, not on a slab tab.</summary>
        [TestMethod]
        public void AFillIsCountedOnTheAreaTab()
        {
            SidlCAreaResult result = Check(Ramp(0, 0, 10000, 4000, 0, 500), Shell("A1", 0, 0, 10000, 4000, 6.5));
            List<ElementViewModel> elements = ElementViewModelFactory.FromSidlCAreaResult(result).ToList();
            foreach (ElementViewModel element in elements)
            {
                element.DisplayState = element.State;
            }

            FindingTally tally = new FindingTally(() => { });
            tally.Update(elements, Story);

            Assert.AreEqual(1, tally.Tabs.Single(tab => tab.Mode == ViewMode.SidlCArea).Open);
            Assert.AreEqual(0, tally.Tabs.Single(tab => tab.Mode == ViewMode.Slabs).Open);
            Assert.AreEqual(CheckKind.SidlCArea, elements[0].Outcomes.Single().Kind);
        }

        #endregion

        #region helpers

        private static SidlCAreaCompareEngine Engine()
        {
            return new SidlCAreaCompareEngine
            {
                Patterns = LineLoadPatterns.Exactly("SIDL_C, SIDL_LWC"),
                Band = new LoadBand(0.5, 1),
                LightweightDensity = 10,
                ToppingDensity = 25,
                ToppingThickness = 100
            };
        }

        private static SidlCAreaResult Check(SidlCAreaSource_R fill, params Slab_E[] shells)
        {
            SidlCAreaCompareEngine engine = Engine();
            engine.StartComparison(new[] { fill }, shells);
            return engine.Results.Single();
        }

        /// <summary>A rectangle of fill, its depth running from one end to the other in X.</summary>
        private static SidlCAreaSource_R Ramp(double x0, double y0, double x1, double y1,
                                              double thinMM, double deepMM, string story = Story)
        {
            PlanPolygon plan = Rectangle(x0, y0, x1, y1);
            SidlCAreaSource_R fill = new SidlCAreaSource_R(1234, new[] { plan }, plan.Area * (thinMM + deepMM) / 2,
                                                           thinMM, deepMM, 0)
            {
                TypeName = "MRG_STR_CIP_RC Light Weight Fill Variable-350",
                Story = story
            };

            // A flat underside, and a top sloping up from the thin end at x0 to the deep at x1.
            fill.SetFaces(
                new[] { new List<MyPoint> { new MyPoint(x0, y0, thinMM), new MyPoint(x1, y0, deepMM),
                                            new MyPoint(x1, y1, deepMM), new MyPoint(x0, y1, thinMM) } },
                new[] { new List<MyPoint> { new MyPoint(x0, y0, 0), new MyPoint(x0, y1, 0),
                                            new MyPoint(x1, y1, 0), new MyPoint(x1, y0, 0) } });
            return fill;
        }

        private static Slab_E Shell(string name, double x0, double y0, double x1, double y1,
                                    double sidlC, SlabKind kind = SlabKind.Slab)
        {
            List<Joint> joints = new[] { new MyPoint(x0, y0, 0), new MyPoint(x1, y0, 0),
                                         new MyPoint(x1, y1, 0), new MyPoint(x0, y1, 0) }
                .Select(point => new Joint("", point, Form1.eLength.mm))
                .ToList();

            Slab_E shell = new Slab_E(name, joints, Story, kind == SlabKind.LoadOnly ? "" : "SLAB200",
                                      kind, 200, 200, false);
            if (sidlC != 0)
            {
                shell.SetLoads(new[] { new AreaLoad_E("SIDL_C", sidlC, 10, true, false) });
            }
            return shell;
        }

        private static PlanPolygon Rectangle(double x0, double y0, double x1, double y1)
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(x0, y0, 0), new MyPoint(x1, y0, 0), new MyPoint(x1, y1, 0), new MyPoint(x0, y1, 0)
            });
        }

        #endregion
    }
}
