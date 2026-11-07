using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Pairing beams on their two ends, which the check does for the whole building and again
    /// after every pairing set by hand.
    ///
    /// It used to try every ETABS beam against every Revit beam, and now only tries the ones
    /// with an end nearby. That is only worth having if it finds exactly what trying them all
    /// found - the same pairs, the same beams left over, and the same winner where two beams
    /// are equally good - so that is what is held here, against a search that tries them all.
    /// </summary>
    [TestClass]
    public class EndpointPairingTests
    {
        private const double FeetToMM = 304.8;
        private const double PlanTolerance = 300;
        private const double VerticalTolerance = 500;

        private MyTransformation alignmentBefore;
        private double planBefore;
        private double verticalBefore;

        [TestInitialize]
        public void SetTolerances()
        {
            alignmentBefore = DataContainer.ModelTransformation;
            DataContainer.ModelTransformation = new MyTransformation();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            planBefore = pref.BeamPlanTolerance;
            verticalBefore = pref.BeamVerticalTolerance;
            pref.BeamPlanTolerance = PlanTolerance;
            pref.BeamVerticalTolerance = VerticalTolerance;
        }

        [TestCleanup]
        public void PutThemBack()
        {
            DataContainer.ModelTransformation = alignmentBefore;
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.BeamPlanTolerance = planBefore;
            pref.BeamVerticalTolerance = verticalBefore;
        }

        /// <summary>
        /// A crowded, awkward model: beams packed closer together than the tolerance so most
        /// have several candidates, ends placed right on the edge of the tolerance, beams drawn
        /// from the other end, exact duplicates that tie, and several floors.
        /// </summary>
        [TestMethod]
        public void OnlyTryingNearbyBeamsFindsWhatTryingEveryBeamFinds()
        {
            for (int seed = 1; seed <= 5; seed++)
            {
                Random random = new Random(seed);
                List<FrameData_e> etabs = new List<FrameData_e>();
                List<FrameData_r> revit = new List<FrameData_r>();

                for (int i = 0; i < 400; i++)
                {
                    double z = random.Next(0, 3) * 3500 + Jitter(random, VerticalTolerance * 1.2);
                    double x = random.Next(0, 12) * 400;
                    double y = random.Next(0, 12) * 400;
                    double length = random.Next(1, 4) * 2000;
                    bool across = random.Next(2) == 0;
                    double x2 = across ? x + length : x;
                    double y2 = across ? y : y + length;

                    etabs.Add(Etabs("E" + i, x, y, x2, y2, z));

                    double dx = Jitter(random, PlanTolerance * 0.9);
                    double dy = Jitter(random, PlanTolerance * 0.9);
                    double dz = Jitter(random, VerticalTolerance * 1.1);
                    bool reversed = random.Next(3) == 0;
                    revit.Add(reversed
                        ? Revit(1000 + i, x2 + dx, y2 + dy, x + dx, y + dy, z + dz)
                        : Revit(1000 + i, x + dx, y + dy, x2 + dx, y2 + dy, z + dz));

                    // Some exact twins, which tie with the beam above and have to lose to it
                    // the same way they always did.
                    if (random.Next(10) == 0)
                    {
                        revit.Add(Revit(5000 + i, x + dx, y + dy, x2 + dx, y2 + dy, z + dz));
                    }
                }

                // Ends exactly the tolerance away, in plan and in level, on the cell edges.
                etabs.Add(Etabs("Edge", 0, 0, 6000, 0, 0));
                revit.Add(Revit(9001, PlanTolerance, 0, 6000 + PlanTolerance, 0, VerticalTolerance));

                EndpointCompareEngine engine = new EndpointCompareEngine();
                engine.StartComparison(revit, etabs);

                Assert.AreEqual(Spell(TryingEveryPair(revit, etabs)), Spell(engine.PairedFrames),
                                "seed " + seed + " paired differently");
                CollectionAssert.AreEquivalent(
                    etabs.Except(engine.PairedFrames.Select(pair => pair.etabsFrame)).ToList(),
                    engine.UnmatchedEtabs, "seed " + seed + " left different ETABS beams over");
            }
        }

        /// <summary>A building's worth of beams, every one of which has its twin in the other model.</summary>
        [TestMethod]
        public void EveryBeamOfABuildingPairsWithItsTwin()
        {
            List<FrameData_e> etabs = new List<FrameData_e>();
            List<FrameData_r> revit = new List<FrameData_r>();

            for (int level = 0; level < 47; level++)
            {
                for (int i = 0; i < 350; i++)
                {
                    double x = (i % 20) * 6000, y = (i / 20) * 6000, z = level * 3500;
                    bool across = i % 2 == 0;
                    etabs.Add(Etabs(level + "-" + i, x, y, across ? x + 6000 : x, across ? y : y + 6000, z));
                    revit.Add(Revit(level * 1000 + i, x + 40, y, (across ? x + 6000 : x) + 40,
                                    across ? y : y + 6000, z));
                }
            }

            EndpointCompareEngine engine = new EndpointCompareEngine();
            engine.StartComparison(revit, etabs);

            Assert.AreEqual(etabs.Count, engine.PairedFrames.Count);
            Assert.IsTrue(engine.PairedFrames.All(pair =>
                pair.etabsFrame.etabsUname == (pair.revitFrame.RevitID / 1000) + "-" + (pair.revitFrame.RevitID % 1000)));
        }

        /// <summary>
        /// Unpairing a beam from its card: its ETABS name is recorded as pairing with nothing.
        /// It stays apart even though its ends line up exactly, its Revit counterpart is free,
        /// and marking the two on the plan afterwards puts them back together.
        /// </summary>
        [TestMethod]
        public void AnUnpairedBeamStaysApartUntilItIsPairedAgain()
        {
            const string project = "tower.rvt";
            FrameData_e etabs = Etabs("B-1", 0, 0, 6000, 0, 0);
            FrameData_r revit = Revit(5501, 0, 0, 6000, 0, 0);

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(project, PairingOverrides.KeyOf(ElementKindKey.Beam, "B-1"), PairingOverrides.Unpaired);

            EndpointCompareEngine engine = new EndpointCompareEngine { Overrides = overrides, ProjectKey = project };
            engine.StartComparison(new List<FrameData_r> { revit }, new List<FrameData_e> { etabs });

            Assert.AreEqual(0, engine.PairedFrames.Count, "unpaired, and lined up exactly is still unpaired");
            CollectionAssert.AreEqual(new[] { etabs }, engine.UnmatchedEtabs);
            CollectionAssert.AreEqual(new[] { revit }, engine.UnmatchedRevit, "and the Revit beam is free");

            // Marked together on the plan: the pair box writes the pairing over "nothing".
            foreach (PairWrite write in TwoWayPairing.Between("B-1", 0, "", 5501))
            {
                overrides.Set(project, PairingOverrides.KeyOf(ElementKindKey.Beam, write.EtabsUniqueName), write.RevitId);
            }
            engine.StartComparison(new List<FrameData_r> { revit }, new List<FrameData_e> { etabs });

            Assert.AreEqual(1, engine.PairedFrames.Count, "and pairing them again puts them back");
        }

        #region what trying every pair finds

        /// <summary>
        /// The search as it was: every ETABS beam against every Revit beam, in the order they
        /// were read, the nearest taken first.
        /// </summary>
        private static List<KeyValuePair<FrameData_e, FrameData_r>> TryingEveryPair(
            IList<FrameData_r> revit, IList<FrameData_e> etabs)
        {
            List<Tuple<FrameData_e, FrameData_r, double>> candidates = new List<Tuple<FrameData_e, FrameData_r, double>>();
            foreach (FrameData_e e in etabs)
            {
                foreach (FrameData_r r in revit)
                {
                    bool same = Close(e.Start, r.Start) && Close(e.End, r.End);
                    bool other = Close(e.Start, r.End) && Close(e.End, r.Start);
                    if (!same && !other)
                    {
                        continue;
                    }

                    double best = double.MaxValue;
                    if (same) best = e.Start.DistanceTo(r.Start) + e.End.DistanceTo(r.End);
                    if (other) best = Math.Min(best, e.Start.DistanceTo(r.End) + e.End.DistanceTo(r.Start));
                    candidates.Add(Tuple.Create(e, r, best));
                }
            }

            // The same sort the engine uses, on the same list, so a tie falls the same way.
            candidates.Sort((left, right) => left.Item3.CompareTo(right.Item3));

            HashSet<FrameData_e> takenE = new HashSet<FrameData_e>();
            HashSet<FrameData_r> takenR = new HashSet<FrameData_r>();
            List<KeyValuePair<FrameData_e, FrameData_r>> pairs = new List<KeyValuePair<FrameData_e, FrameData_r>>();
            foreach (Tuple<FrameData_e, FrameData_r, double> candidate in candidates)
            {
                if (takenE.Contains(candidate.Item1) || takenR.Contains(candidate.Item2))
                {
                    continue;
                }
                takenE.Add(candidate.Item1);
                takenR.Add(candidate.Item2);
                pairs.Add(new KeyValuePair<FrameData_e, FrameData_r>(candidate.Item1, candidate.Item2));
            }
            return pairs;
        }

        private static bool Close(MyPoint a, MyPoint b)
        {
            if (Math.Abs(a.Z - b.Z) > VerticalTolerance)
            {
                return false;
            }
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy <= PlanTolerance * PlanTolerance;
        }

        private static string Spell(IEnumerable<KeyValuePair<FrameData_e, FrameData_r>> pairs)
        {
            return string.Join(",", pairs.Select(pair => pair.Key.etabsUname + "=" + pair.Value.RevitID)
                                         .OrderBy(text => text, StringComparer.Ordinal));
        }

        private static string Spell(IEnumerable<PairedFrame> pairs)
        {
            return Spell(pairs.Select(pair => new KeyValuePair<FrameData_e, FrameData_r>(pair.etabsFrame, pair.revitFrame)));
        }

        #endregion

        #region building the model

        private static double Jitter(Random random, double most)
        {
            return (random.NextDouble() * 2 - 1) * most;
        }

        private static FrameData_e Etabs(string name, double x1, double y1, double x2, double y2, double z)
        {
            FrameData_e frame = new FrameData_e(name) { Story = "L" + Math.Round(z / 3500) };
            frame.Start_e = new MyPoint(x1, y1, z);
            frame.END_e = new MyPoint(x2, y2, z);
            frame._width = 300;
            frame._depth = 600;
            return frame;
        }

        /// <summary>In mm here, handed over in feet the way the importer hands it over.</summary>
        private static FrameData_r Revit(long id, double x1, double y1, double x2, double y2, double z)
        {
            MyPoint start = new MyPoint(x1 / FeetToMM, y1 / FeetToMM, z / FeetToMM);
            MyPoint end = new MyPoint(x2 / FeetToMM, y2 / FeetToMM, z / FeetToMM);

            FrameData_r frame = new FrameData_r(id, start, end, start.DistanceTo(end), new MyPoint(1, 0, 0))
            {
                Story = "L" + Math.Round(z / 3500)
            };
            frame._width = 300;
            frame._depth = 600;
            return frame;
        }

        #endregion
    }
}
