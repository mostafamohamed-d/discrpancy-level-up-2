using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Which pairs the beam check makes when more than one of them is possible.
    ///
    /// The gates that decide whether an ETABS frame and a Revit frame may be paired at all -
    /// how far apart they sit, which way they run, how long they are - need frames read out of
    /// the two models and are not tested here. What is tested is the step after that: given a
    /// set of pairs that are all allowed, which ones actually get made. That is where the
    /// answer used to depend on the order ETABS handed the frames over, and it is the part
    /// that can be reasoned about without a model.
    /// </summary>
    [TestClass]
    public class BeamPairingTests
    {
        private static CompareEngine.candidatePair Pair(int etabsIndex, int revitIndex, double distance)
        {
            CompareEngine.candidatePair candidate;
            candidate.etabsIndex = etabsIndex;
            candidate.revitIndex = revitIndex;
            candidate.distance = distance;
            return candidate;
        }

        private static string Describe(IEnumerable<CompareEngine.candidatePair> pairs)
        {
            return string.Join(", ", pairs.Select(p => $"e{p.etabsIndex}-r{p.revitIndex}"));
        }

        /// <summary>
        /// The case the change was made for. Both ETABS frames can reach Revit frame 0, and
        /// frame 1 is much the closer of the two. Taking the ETABS frames in turn, frame 0
        /// took it at 100mm and frame 1 - 10mm away - was left with nothing, because the only
        /// other Revit frame in reach is 400mm from frame 0 and not in reach of frame 1 at all.
        /// Taking the closest pair first gives Revit frame 0 to the frame that is nearly on top
        /// of it and leaves the other one the frame it can still reach, so both get paired.
        /// </summary>
        [TestMethod]
        public void ClosestPairIsMadeFirstSoNeitherFrameIsLeftStranded()
        {
            List<CompareEngine.candidatePair> candidates = new List<CompareEngine.candidatePair>
            {
                Pair(0, 0, 100),
                Pair(0, 1, 400),
                Pair(1, 0, 10)
            };

            List<CompareEngine.candidatePair> taken = CompareEngine.TakeClosestPairsFirst(candidates);

            Assert.AreEqual("e1-r0, e0-r1", Describe(taken));
        }

        /// <summary>
        /// The same candidates handed over in every order give the same pairs. This is the
        /// property the change is really about: the pairing is decided by the model rather
        /// than by the order ETABS happened to return the frames in.
        /// </summary>
        [TestMethod]
        public void PairsDoNotDependOnTheOrderTheCandidatesArriveIn()
        {
            List<CompareEngine.candidatePair> candidates = new List<CompareEngine.candidatePair>
            {
                Pair(0, 0, 100),
                Pair(0, 1, 400),
                Pair(1, 0, 10),
                Pair(2, 2, 250),
                Pair(2, 1, 60)
            };

            string expected = Describe(CompareEngine.TakeClosestPairsFirst(candidates));

            // Every ordering of five candidates, not a sample of them: the list is short
            // enough to walk in full and a sample could miss the one that disagrees.
            foreach (List<CompareEngine.candidatePair> ordering in Orderings(candidates))
            {
                Assert.AreEqual(expected, Describe(CompareEngine.TakeClosestPairsFirst(ordering)),
                    "pairing changed when the candidates arrived as " + Describe(ordering));
            }
        }

        /// <summary>
        /// Two Revit frames exactly the same distance from one ETABS frame. Neither is nearer,
        /// so the one that comes first in the model wins whichever way round the candidates
        /// were handed over. <c>getTheNearest</c> settles a tie the same way, and both give
        /// the same answer on every run instead of one that follows the read order.
        /// </summary>
        [TestMethod]
        public void ExactTieKeepsTheEarlierRevitFrame()
        {
            List<CompareEngine.candidatePair> candidates = new List<CompareEngine.candidatePair>
            {
                Pair(0, 1, 250),
                Pair(0, 0, 250)
            };

            List<CompareEngine.candidatePair> taken = CompareEngine.TakeClosestPairsFirst(candidates);

            Assert.AreEqual("e0-r0", Describe(taken));
        }

        /// <summary>A frame is never put in two pairs, however many it could be paired with.</summary>
        [TestMethod]
        public void EachFrameIsPairedAtMostOnce()
        {
            List<CompareEngine.candidatePair> candidates = new List<CompareEngine.candidatePair>
            {
                Pair(0, 0, 10),
                Pair(0, 1, 20),
                Pair(1, 0, 30),
                Pair(1, 1, 40)
            };

            List<CompareEngine.candidatePair> taken = CompareEngine.TakeClosestPairsFirst(candidates);

            CollectionAssert.AllItemsAreUnique(taken.Select(p => p.etabsIndex).ToList());
            CollectionAssert.AllItemsAreUnique(taken.Select(p => p.revitIndex).ToList());
            Assert.AreEqual("e0-r0, e1-r1", Describe(taken));
        }

        [TestMethod]
        public void NoCandidatesMakesNoPairs()
        {
            Assert.AreEqual(0, CompareEngine.TakeClosestPairsFirst(
                new List<CompareEngine.candidatePair>()).Count);
        }

        /// <summary>Every ordering of the list, so nothing is left to a chosen shuffle.</summary>
        private static IEnumerable<List<CompareEngine.candidatePair>> Orderings(
            List<CompareEngine.candidatePair> items)
        {
            if (items.Count <= 1)
            {
                yield return new List<CompareEngine.candidatePair>(items);
                yield break;
            }

            for (int i = 0; i < items.Count; i++)
            {
                List<CompareEngine.candidatePair> rest = new List<CompareEngine.candidatePair>(items);
                rest.RemoveAt(i);

                foreach (List<CompareEngine.candidatePair> tail in Orderings(rest))
                {
                    tail.Insert(0, items[i]);
                    yield return tail;
                }
            }
        }
    }
}
