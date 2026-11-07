using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The local thickening detector: what it reads as a thickening of a floor, what it turns
    /// away, what it leaves visible as ambiguous, the edges between them, and what the slab check
    /// makes of the result. The detector is judged from geometry alone, so these bodies carry
    /// no names that matter.
    /// </summary>
    [TestClass]
    public class ThickeningDetectionTests
    {
        // A 300 floor, 20 m square, top at 0.
        private const double HostTop = 0, HostBottom = -300;

        private static readonly ThickeningOptions Options = new ThickeningOptions();

        // --- positive ------------------------------------------------------------------------

        [TestMethod]
        public void ADropPanelFlushWithTheTopAndReachingUnderTheUndersideIsDetected()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 7000, 8500, 0, -600));

            Assert.AreEqual(ThickeningOutcome.Detected, patch.Outcome, patch.Reason);
            Assert.AreEqual(ThickeningConfidence.High, patch.Confidence);
            Assert.AreEqual(600, patch.Thickness, 1e-9, "the body's own depth, not the floor's plus the body's");
            Assert.AreEqual(0, patch.TopElevation, 1e-9);
            Assert.AreEqual(-600, patch.BottomElevation, 1e-9);
            Assert.AreEqual(1.0, patch.OverHostRatio, 1e-9);
            Assert.AreEqual(1, patch.Plans.Count);
        }

        [TestMethod]
        public void ADetectedThickeningHasNoPtOrRcClassOfItsOwn()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 7000, 8500, 0, -600));
            Assert.AreEqual("Unknown", patch.Classification);
        }

        [TestMethod]
        public void ALShapedThickeningIsDetectedWithLessConfidence()
        {
            // 3 m x 3 m with a 1.5 m corner taken out: 6.75 m2, but it fills only 3/4 of its rectangle.
            ThickeningCandidate body = new ThickeningCandidate { ElementId = 1, Solids = 1 };
            body.TopFaces.Add(new List<List<MyPoint>> { Loop(
                new[] { 4000.0, 4000 }, new[] { 7000.0, 4000 }, new[] { 7000.0, 5500 },
                new[] { 5500.0, 5500 }, new[] { 5500.0, 7000 }, new[] { 4000.0, 7000 }) });
            Level(body, 0, -600);

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Detected, patch.Outcome, patch.Reason);
            Assert.AreEqual(ThickeningConfidence.Medium, patch.Confidence);
            Assert.IsTrue(patch.Evidence.Any(line => line.StartsWith("fail: compactness")), string.Join("\n", patch.Evidence));
        }

        [TestMethod]
        public void ABodyOfSeveralSolidsAtOneHeightIsOneThickening()
        {
            ThickeningCandidate body = new ThickeningCandidate { ElementId = 7, Solids = 2 };
            body.TopFaces.Add(Rect(4000, 4000, 6000, 6000));
            body.TopFaces.Add(Rect(6000, 4000, 8000, 6000));
            Level(body, 0, -600);

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Detected, patch.Outcome, patch.Reason);
            Assert.AreEqual(2, patch.Plans.Count, "one region per face looking up");
        }

        [TestMethod]
        public void InclinedSideFacesDoNotRejectABodyWithALevelTopAndBottom()
        {
            ThickeningCandidate body = Box(5000, 5000, 7000, 8500, 0, -600);
            body.InclinedUpFaces = 4;
            body.InclinedUpArea = 0.1 * 2000 * 3500;

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Detected, patch.Outcome, patch.Reason);
        }

        // --- negative ------------------------------------------------------------------------

        [TestMethod]
        public void ANarrowStripFlushWithTheTopIsABeam()
        {
            ThickeningPatch patch = Judge(Box(2000, 5000, 2400, 11000, 0, -700));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "beam");
        }

        [TestMethod]
        public void AColumnPassingThroughTheFloorIsRejected()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 5600, 5600, 3000, -3000));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "passes through");
        }

        [TestMethod]
        public void AWallRisingFromTheTopIsRejected()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 5200, 9000, 3000, -150));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void AToppingOnTheFloorIsRejected()
        {
            ThickeningPatch patch = Judge(Box(3000, 3000, 9000, 9000, 60, 0));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "stands on the floor");
        }

        [TestMethod]
        public void AFoundationWellBelowTheFloorIsRejected()
        {
            ThickeningPatch patch = Judge(Box(3000, 3000, 9000, 9000, -3000, -4500));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void ABodyInsideTheFloorsOwnDepthAddsNothing()
        {
            ThickeningPatch patch = Judge(Box(3000, 3000, 9000, 9000, 0, -200));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void ABeamHangingFromTheUndersideIsRejected()
        {
            ThickeningPatch patch = Judge(Box(2000, 5000, 2400, 13000, HostBottom, -800));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "hangs from the underside");
        }

        [TestMethod]
        public void ABodyMostlyOutsideTheFloorIsRejectedAndSaysSo()
        {
            // A landing next to the floor: 4% of it over the floor's edge.
            ThickeningPatch patch = Judge(Box(-3920, 5000, 80, 8000, 0, -600));
            Assert.AreEqual(ThickeningOutcome.Rejected, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "over this floor");
        }

        // --- ambiguous -----------------------------------------------------------------------

        [TestMethod]
        public void ARampHostIsNotJudgedAndEverythingAgainstItIsAmbiguous()
        {
            ThickeningHost host = Host();
            host.Flat = false;

            ThickeningPatch patch = ThickeningClassifier.Classify(host, Box(5000, 5000, 7000, 8500, 0, -600), Options);

            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "no single top and bottom");
        }

        [TestMethod]
        public void AWideBandIsAmbiguousNotRejected()
        {
            ThickeningPatch patch = Judge(Box(2000, 5000, 3400, 15000, 0, -600));
            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void ASmallPadStaysVisibleAsAmbiguousWithLowConfidence()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 6000, 6000, 0, -600));
            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
            Assert.AreEqual(ThickeningConfidence.Low, patch.Confidence);
            Assert.AreEqual(2, patch.Evidence.Count(line => line.StartsWith("fail:")), string.Join("\n", patch.Evidence));
        }

        [TestMethod]
        public void ACompactPadHangingFromTheUndersideIsAmbiguous()
        {
            // Could be a column capital. It is not flush with the top, so it is not called a thickening.
            ThickeningPatch patch = Judge(Box(5000, 5000, 6500, 6500, HostBottom, -800));
            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void ABodyWithNoLevelTopIsAmbiguous()
        {
            ThickeningCandidate body = new ThickeningCandidate { ElementId = 3, Solids = 1, InclinedUpFaces = 1, InclinedUpArea = 5e6 };
            body.HasLevelBottom = true; body.BottomHigh = body.BottomLow = -600;

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
            StringAssert.Contains(patch.Reason, "inclined");
        }

        [TestMethod]
        public void ASteppedTopIsAmbiguous()
        {
            ThickeningCandidate body = new ThickeningCandidate { ElementId = 4, Solids = 2 };
            body.TopFaces.Add(Rect(4000, 4000, 6000, 6000));
            body.TopFaces.Add(Rect(6000, 4000, 8000, 6000));
            body.HasLevelTop = true; body.TopHigh = 0; body.TopLow = -150;
            body.HasLevelBottom = true; body.BottomHigh = body.BottomLow = -600;

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void ABodyThatCouldNotBeFullyReadIsAmbiguous()
        {
            ThickeningCandidate body = Box(5000, 5000, 7000, 8500, 0, -600);
            body.Unread = "face failed";

            ThickeningPatch patch = Judge(body);

            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
        }

        // --- boundaries ----------------------------------------------------------------------

        [TestMethod]
        public void OverlapJustUnderAndJustOverTheLimitFallEitherSideOfIt()
        {
            ThickeningHost host = Host();
            ThickeningCandidate body = Box(5000, 5000, 7000, 8500, 0, -600);

            Assert.AreEqual(ThickeningOutcome.Ambiguous, ThickeningClassifier.Classify(host, body, Options, 0.89).Outcome);
            Assert.AreEqual(ThickeningOutcome.Detected, ThickeningClassifier.Classify(host, body, Options, 0.91).Outcome);
            Assert.AreEqual(ThickeningOutcome.Detected, ThickeningClassifier.Classify(host, body, Options, 0.90).Outcome, "the limit itself is in");
            Assert.AreEqual(ThickeningOutcome.Rejected, ThickeningClassifier.Classify(host, body, Options, 0.05).Outcome);
            Assert.AreEqual(ThickeningOutcome.Ambiguous, ThickeningClassifier.Classify(host, body, Options, 0.10).Outcome,
                            "a body half over the floor is listed, not dropped");
        }

        [TestMethod]
        public void ABodyHalfOverTheFloorIsMeasuredAsAboutHalfAndIsAmbiguous()
        {
            // The floor runs 0..20000; this panel runs 19000..21000.
            ThickeningPatch patch = Judge(Box(19000, 5000, 21000, 8500, 0, -600));

            Assert.AreEqual(0.5, patch.OverHostRatio, 0.05);
            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, patch.Reason);
        }

        [TestMethod]
        public void TheReachUnderTheUndersideIsAnEdge()
        {
            Assert.AreEqual(ThickeningOutcome.Ambiguous, Judge(Box(5000, 5000, 7000, 8500, 0, HostBottom - 49)).Outcome);
            Assert.AreEqual(ThickeningOutcome.Detected, Judge(Box(5000, 5000, 7000, 8500, 0, HostBottom - 51)).Outcome);
        }

        [TestMethod]
        public void TheFlushToleranceIsAnEdge()
        {
            Assert.AreEqual(ThickeningOutcome.Detected, Judge(Box(5000, 5000, 7000, 8500, 9, -600)).Outcome, "9 above the top is flush");
            Assert.AreEqual(ThickeningOutcome.Rejected, Judge(Box(5000, 5000, 7000, 8500, 11, -600)).Outcome, "11 above the top is not");
        }

        [TestMethod]
        public void AreaAndShortSideAreEvidenceAtTheirLimitsAndNotCutOffs()
        {
            // 1500 x 2000 meets both limits exactly.
            Assert.AreEqual(ThickeningConfidence.High, Judge(Box(5000, 5000, 6500, 7000, 0, -600)).Confidence);

            // 1499 x 2000 misses both and is listed, not dropped.
            ThickeningPatch under = Judge(Box(5000, 5000, 6499, 7000, 0, -600));
            Assert.AreEqual(ThickeningOutcome.Ambiguous, under.Outcome, under.Reason);

            // 1400 x 3000 misses the short side only: still a thickening, with less confidence.
            ThickeningPatch narrow = Judge(Box(5000, 5000, 6400, 8000, 0, -600));
            Assert.AreEqual(ThickeningOutcome.Detected, narrow.Outcome, narrow.Reason);
            Assert.AreEqual(ThickeningConfidence.Medium, narrow.Confidence);
        }

        [TestMethod]
        public void TheThresholdsAreConfigurable()
        {
            ThickeningOptions strict = new ThickeningOptions { MinProjection = 400 };
            ThickeningPatch patch = ThickeningClassifier.Classify(Host(), Box(5000, 5000, 7000, 8500, 0, -600), strict);
            Assert.AreEqual(ThickeningOutcome.Ambiguous, patch.Outcome, "300 of reach is under 400 asked for");
        }

        // --- datum ---------------------------------------------------------------------------

        [TestMethod]
        public void AVerticalShiftOfFloorAndBodiesTogetherChangesNoOutcome()
        {
            ThickeningCandidate[] bodies =
            {
                Box(5000, 5000, 7000, 8500, 0, -600),
                Box(2000, 5000, 2400, 11000, 0, -700),
                Box(5000, 5000, 5600, 5600, 3000, -3000),
                Box(3000, 3000, 9000, 9000, 60, 0),
                Box(5000, 5000, 6500, 6500, HostBottom, -800),
                Box(5000, 5000, 6000, 6000, 0, -600)
            };

            foreach (double shift in new[] { 100.0, -100.0, 21100.0 })
            {
                foreach (ThickeningCandidate body in bodies)
                {
                    ThickeningPatch plain = ThickeningClassifier.Classify(Host(), body, Options);
                    ThickeningPatch shifted = ThickeningClassifier.Classify(Host(shift), Shifted(body, shift), Options);

                    Assert.AreEqual(plain.Outcome, shifted.Outcome, $"shift {shift}: {plain.Reason} / {shifted.Reason}");
                    Assert.AreEqual(plain.Confidence, shifted.Confidence);
                    Assert.AreEqual(plain.Reason, shifted.Reason);
                    Assert.AreEqual(plain.Thickness, shifted.Thickness, 1e-9);
                    Assert.AreEqual(plain.TopElevation + shift, shifted.TopElevation, 1e-9);
                }
            }
        }

        [TestMethod]
        public void MovingAFloorMovesItsPatchesWithIt()
        {
            ThickeningPatch patch = Judge(Box(5000, 5000, 7000, 8500, 0, -600));
            Slab_R floor = new Slab_R(10, Rect(0, 0, 20000, 20000).Select(loop => PlanPolygon.FromPoints(loop)).First(), 0, -300);
            floor.Patches.Add(patch);

            MyTransformation from = Alignment(0, 0, 0);
            MyTransformation to = Alignment(500, 300, 100);
            MyTransformation move = MyTransformation.Difference(to, from);

            MyPoint corner = move.ApplyToPointInMM(new MyPoint(5000, 5000, 0));
            double movedTop = move.ApplyToPointInMM(new MyPoint(0, 0, patch.TopElevation)).Z;
            double movedBottom = move.ApplyToPointInMM(new MyPoint(0, 0, patch.BottomElevation)).Z;
            double floorTop = move.ApplyToPointInMM(new MyPoint(0, 0, floor.TopElevation)).Z;

            floor.MoveBy(move);

            ThickeningPatch moved = floor.Patches.Single();
            Assert.AreEqual(movedTop, moved.TopElevation, 1e-6);
            Assert.AreEqual(movedBottom, moved.BottomElevation, 1e-6);
            Assert.AreEqual(floorTop, floor.TopElevation, 1e-6, "the floor and its patch move by the same height");
            Assert.AreEqual(patch.Thickness, moved.Thickness, 1e-6, "a move changes no thickness");
            Assert.AreEqual(corner.X, moved.Plans[0].Outer.Points.Min(p => p.X), 1e-6);
            Assert.AreEqual(corner.Y, moved.Plans[0].Outer.Points.Min(p => p.Y), 1e-6);
            CollectionAssert.AreEqual(patch.SourceElementIds, moved.SourceElementIds);
        }

        // --- overlapping and identical patches ----------------------------------------------

        [TestMethod]
        public void IdenticalBodiesFoldIntoOneKeepingEverySourceId()
        {
            ThickeningCandidate a = Box(5000, 5000, 7000, 8500, 0, -600); a.ElementId = 11; a.Description = "[Structural Framing] A";
            ThickeningCandidate b = Box(5000, 5000, 7000, 8500, 0, -600); b.ElementId = 12; b.Description = "[Generic Models] B";

            List<ThickeningPatch> kept = ThickeningClassifier.Deduplicate(new[] { Judge(a), Judge(b) });

            Assert.AreEqual(1, kept.Count);
            CollectionAssert.AreEquivalent(new long[] { 11, 12 }, kept[0].SourceElementIds);
            Assert.AreEqual(2, kept[0].Provenance.Count);
            Assert.AreEqual(10, kept[0].HostFloorId);
        }

        [TestMethod]
        public void OverlappingBodiesAreNeverMergedOrDropped()
        {
            ThickeningCandidate small = Box(5000, 5000, 7000, 8500, 0, -600); small.ElementId = 21;
            ThickeningCandidate large = Box(4000, 4000, 9000, 10000, 0, -450); large.ElementId = 22;

            List<ThickeningPatch> kept = ThickeningClassifier.Deduplicate(new[] { Judge(small), Judge(large) });

            Assert.AreEqual(2, kept.Count, "the larger does not swallow the smaller");
            Assert.AreEqual(600, kept.Single(p => p.SourceElementIds.Contains(21)).Thickness, 1e-9);
            Assert.AreEqual(450, kept.Single(p => p.SourceElementIds.Contains(22)).Thickness, 1e-9);
        }

        // --- in the slab check ---------------------------------------------------------------

        [TestMethod]
        public void ADetectedThickeningStopsTheFalseThicknessMismatch()
        {
            Slab_R without = Floor();
            Slab_R with = Floor();
            with.Patches.Add(Judge(DropBody()));

            List<SlabZone> before = Run(without);
            List<SlabZone> after = Run(with);

            Assert.IsTrue(before.Any(z => z.EtabsName == "D1" && z.State == SlabZoneState.ThicknessMismatch),
                          "without it the 600 drop reads against 250:\n" + Dump(before));
            Assert.IsFalse(after.Any(z => z.State == SlabZoneState.ThicknessMismatch),
                           "with it the drop reads 600 against 600:\n" + Dump(after));
            Assert.IsTrue(after.Any(z => z.EtabsName == "D1" && z.State == SlabZoneState.Matched), Dump(after));
        }

        [TestMethod]
        public void ADetectedThickeningRaisesNoPtOrRcMismatch()
        {
            Slab_R with = Floor();
            with.TypeName = "PT SLAB 250";
            with.Patches.Add(Judge(DropBody()));

            List<SlabZone> zones = Run(with);

            SlabZone drop = zones.First(z => z.EtabsName == "D1");
            Assert.AreEqual(ConcreteSystem.Reinforced, drop.EtabsSystem, "ETABS says RC");
            Assert.AreEqual(ConcreteSystem.Unknown, drop.RevitSystem, "the thickening says nothing, and takes nothing from the PT floor");
            Assert.AreEqual(ResultLevel.NotChecked, drop.SystemLevel);
        }

        [TestMethod]
        public void AmbiguousAndRejectedPatchesChangeNothingInTheCheck()
        {
            Slab_R plain = Floor();
            Slab_R ambiguous = Floor();
            ambiguous.Patches.Add(Judge(Box(5000, 5000, 6000, 6000, 0, -600)));
            ThickeningPatch rejected = Judge(Box(2000, 5000, 2400, 11000, 0, -700));
            ambiguous.Patches.Add(rejected);

            CollectionAssert.AreEqual(Describe(Run(plain)), Describe(Run(ambiguous)));
        }

        [TestMethod]
        public void AFloorWithNoPatchesChecksExactlyAsBefore()
        {
            Slab_R floor = Floor();
            Assert.AreEqual(0, floor.Patches.Count, "empty unless the detection fills it");
            Assert.IsTrue(Run(floor).Any(z => z.EtabsName == "A1"));
        }

        [TestMethod]
        public void PatchesSurviveTheSavedInputsFileAndRunTheSame()
        {
            Slab_R with = Floor();
            with.Patches.Add(Judge(DropBody()));
            with.Patches.Add(Judge(Box(5000, 5000, 6000, 6000, 0, -600)));

            List<Story_E> stories = Stories();
            List<Slab_E> shells = Shells();
            List<string> before = Describe(Run(with));

            string path = Path.Combine(Path.GetTempPath(), "slab-inputs-patch-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                SlabCheckInputs.Collect("L1", stories, shells, new[] { with }, new SlabSupport[0]).Write(path);
                SlabCheckInputs read = SlabCheckInputs.Read(path);

                Slab_R back = read.ToRevitFloors().Single();
                Assert.AreEqual(2, back.Patches.Count);
                CollectionAssert.AreEqual(with.Patches.Select(p => p.Outcome).ToList(), back.Patches.Select(p => p.Outcome).ToList());
                CollectionAssert.AreEqual(with.Patches[0].SourceElementIds, back.Patches[0].SourceElementIds);
                Assert.AreEqual(with.Patches[0].Thickness, back.Patches[0].Thickness, 1e-9);
                Assert.AreEqual(with.Patches[0].Reason, back.Patches[0].Reason);

                CollectionAssert.AreEqual(before, Describe(Run(read.ToRevitFloors(), read.EtabsSlabs)));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void AnOldSavedFileWithNoPatchesStillReads()
        {
            Slab_R plain = Floor();
            string path = Path.Combine(Path.GetTempPath(), "slab-inputs-old-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                SlabCheckInputs.Collect("L1", Stories(), Shells(), new[] { plain }, new SlabSupport[0]).Write(path);
                string text = File.ReadAllText(path);
                text = System.Text.RegularExpressions.Regex.Replace(text, "\"Patches\":\\[\\],?", "");
                File.WriteAllText(path, text);

                Slab_R back = SlabCheckInputs.Read(path).ToRevitFloors().Single();
                Assert.AreEqual(0, back.Patches.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void TheDetectionIsOffByDefaultAndItsDefaultsAreThoseAgreed()
        {
            PrefrencesData fresh = PrefrencesData.getPrefrencesData();
            Assert.IsFalse(fresh.DetectLocalThickenings);

            ThickeningOptions options = ThickeningOptions.FromPreferences(fresh);
            Assert.AreEqual(0.90, options.MinOverlap, 1e-9);
            Assert.AreEqual(10, options.FlushTolerance, 1e-9);
            Assert.AreEqual(50, options.MinProjection, 1e-9);
            Assert.AreEqual(3e6, options.MinArea, 1e-9);
            Assert.AreEqual(1500, options.MinShortSide, 1e-9);
            Assert.AreEqual(0.85, options.MinCompactness, 1e-9);
        }

        // --- helpers -------------------------------------------------------------------------

        private static ThickeningHost Host(double shift = 0)
        {
            return new ThickeningHost
            {
                Id = 10,
                Top = HostTop + shift,
                Bottom = HostBottom + shift,
                Flat = true,
                Parts = new List<PlanPolygon> { PlanPolygon.FromPoints(Pts(0, 0, 20000, 20000)) }
            };
        }

        private static ThickeningPatch Judge(ThickeningCandidate body)
        {
            return ThickeningClassifier.Classify(Host(), body, Options);
        }

        private static ThickeningCandidate Box(double minX, double minY, double maxX, double maxY, double top, double bottom)
        {
            ThickeningCandidate body = new ThickeningCandidate { ElementId = 1, Solids = 1, Description = "[any] family 'any' type 'any'" };
            body.TopFaces.Add(Rect(minX, minY, maxX, maxY));
            Level(body, top, bottom);
            return body;
        }

        private static void Level(ThickeningCandidate body, double top, double bottom)
        {
            body.HasLevelTop = true; body.TopHigh = body.TopLow = top;
            body.HasLevelBottom = true; body.BottomHigh = body.BottomLow = bottom;
        }

        private static ThickeningCandidate Shifted(ThickeningCandidate body, double dz)
        {
            ThickeningCandidate copy = new ThickeningCandidate
            {
                ElementId = body.ElementId, Solids = body.Solids, Description = body.Description,
                HasLevelTop = body.HasLevelTop, HasLevelBottom = body.HasLevelBottom,
                TopHigh = body.TopHigh + dz, TopLow = body.TopLow + dz,
                BottomHigh = body.BottomHigh + dz, BottomLow = body.BottomLow + dz,
                InclinedUpFaces = body.InclinedUpFaces, InclinedUpArea = body.InclinedUpArea
            };
            copy.TopFaces = body.TopFaces;
            return copy;
        }

        private static List<List<MyPoint>> Rect(double minX, double minY, double maxX, double maxY)
        {
            return new List<List<MyPoint>> { Pts(minX, minY, maxX, maxY) };
        }

        private static List<MyPoint> Pts(double minX, double minY, double maxX, double maxY)
        {
            return new List<MyPoint>
            {
                new MyPoint(minX, minY, 0), new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0), new MyPoint(minX, maxY, 0)
            };
        }

        private static List<MyPoint> Loop(params double[][] xy)
        {
            return xy.Select(p => new MyPoint(p[0], p[1], 0)).ToList();
        }

        private static MyTransformation Alignment(double dx, double dy, double dz)
        {
            MyTransformation alignment = new MyTransformation();
            alignment.SetAlignment(new MyPoint(dx, dy, dz), 0, false);
            return alignment;
        }

        // The slab check over a 12 m square, ETABS 250 with a 600 drop on it, Revit 250 with whatever patches it is given.

        private static ThickeningCandidate DropBody()
        {
            return Box(6000, 0, 8000, 3500, 0, -600);
        }

        private static List<Story_E> Stories()
        {
            return new List<Story_E> { new Story_E("L1", 0, Form1.eLength.mm), new Story_E("L2", 3000, Form1.eLength.mm) };
        }

        private static List<Slab_E> Shells()
        {
            return new List<Slab_E>
            {
                Shell("A1", 0, 0, 12000, 12000, "S250", SlabKind.Slab, 250),
                Shell("D1", 6000, 0, 8000, 3500, "Rc Drop 600", SlabKind.Drop, 600)
            };
        }

        private static Slab_E Shell(string name, double minX, double minY, double maxX, double maxY,
                                    string property, SlabKind kind, double thickness)
        {
            List<Joint> joints = new[] { new[] { minX, minY }, new[] { maxX, minY }, new[] { maxX, maxY }, new[] { minX, maxY } }
                .Select(p => new Joint("", new MyPoint(p[0], p[1], 0), Form1.eLength.mm))
                .ToList();
            return new Slab_E(name, joints, "L1", property, kind, thickness, thickness, false);
        }

        private static Slab_R Floor()
        {
            PlanPolygon plan = PlanPolygon.FromPoints(Pts(0, 0, 12000, 12000));
            return new Slab_R(500, plan, 0, -250)
            {
                Story = "L1",
                Parts = new List<PlanPolygon> { plan },
                TypeName = "SLAB 250",
                StructuralThickness = 250
            };
        }

        private static List<SlabZone> Run(Slab_R floor)
        {
            return Run(new[] { floor }, Shells());
        }

        private static List<SlabZone> Run(IList<Slab_R> floors, IList<Slab_E> shells)
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = "L1" };
            engine.SetColumnsAndWalls(new List<SlabSupport>());
            engine.StartComparison(shells, floors, Stories());
            return engine.Zones.ToList();
        }

        private static List<string> Describe(IEnumerable<SlabZone> zones)
        {
            return zones.Select(zone => $"{zone.State} {zone.EtabsName} {Math.Round(zone.Area)} {zone.RevitType}")
                        .OrderBy(line => line, StringComparer.Ordinal)
                        .ToList();
        }

        private static string Dump(IEnumerable<SlabZone> zones)
        {
            return string.Join("\n", Describe(zones));
        }
    }
}
