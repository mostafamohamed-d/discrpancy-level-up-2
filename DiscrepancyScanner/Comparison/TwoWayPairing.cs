using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>One decision to write: this ETABS element goes with this Revit element.</summary>
    public sealed class PairWrite
    {
        public PairWrite(string etabsUniqueName, long revitId)
        {
            EtabsUniqueName = etabsUniqueName ?? "";
            RevitId = revitId;
        }

        public string EtabsUniqueName { get; }
        public long RevitId { get; }
    }

    /// <summary>
    /// What it means to mark exactly two elements on the plan and say "these go together".
    ///
    /// No distance is measured here, and that is the whole point. The automatic pass already
    /// decides by distance, and this exists for the cases where it decided wrongly - so a person
    /// who has pointed at two elements has said something the geometry cannot: that these two,
    /// whatever the measurements suggest, are the same element in two models. Measuring again
    /// would only reproduce the answer being corrected.
    ///
    /// Which of the two was marked first is never asked. A pairing is written from the ETABS
    /// side, so the only question is which of the pair can take that side, and that is a
    /// property of the elements rather than of the order somebody clicked them in.
    /// </summary>
    public static class TwoWayPairing
    {
        /// <summary>
        /// The decisions that follow from marking these two, or an empty list when they cannot
        /// pair at all.
        ///
        /// An element with an ETABS name can take the ETABS side, one with a Revit id the Revit
        /// side, and one that is already paired can take either. Both assignments are tried, so
        /// the answer does not depend on which argument is which:
        ///
        /// - exactly one way round works - one write, the obvious one
        /// - both ways round work - both are paired already, and a pair is not paired with
        ///   another pair
        /// - neither works - two elements from the same model, and there is nothing to pair
        ///
        /// A paired element marked with one that has no counterpart is allowed either way: the
        /// paired ETABS element moves onto a spare Revit element, or a spare ETABS element takes
        /// over the Revit element the pair was holding.
        ///
        /// Two pairs are not, though it used to swap their counterparts. Two failing pairs
        /// side by side are far more often two beams that are simply wrong than two whose
        /// partners the automatic pass crossed, and a swap put down two pairings nobody had
        /// looked at on the strength of one gesture. Crossed pairs are put right one at a time
        /// from the card, where the element being changed is the only one on screen.
        /// </summary>
        public static IList<PairWrite> Between(string nameA, long idA, string nameB, long idB)
        {
            List<PairWrite> writes = new List<PairWrite>();

            bool aOntoB = CanOwn(nameA) && idB > 0;
            bool bOntoA = CanOwn(nameB) && idA > 0;

            if (aOntoB && bOntoA)
            {
                return writes;
            }

            if (aOntoB)
            {
                writes.Add(new PairWrite(nameA, idB));
            }
            else if (bOntoA)
            {
                writes.Add(new PairWrite(nameB, idA));
            }

            return writes;
        }

        /// <summary>
        /// Why a marked pair cannot be paired, said in the terms the plan is drawn in rather
        /// than as a refusal. Empty when there is nothing wrong with them.
        /// </summary>
        public static string WhyNot(string nameA, long idA, string nameB, long idB)
        {
            if (Between(nameA, idA, nameB, idB).Count > 0)
            {
                return "";
            }

            if (IsPaired(nameA, idA) && IsPaired(nameB, idB))
            {
                return "Both of these are paired already, and a pair cannot be paired with another "
                       + "pair. To change what one of them goes with, select it on its own and use "
                       + "Change under Paired with on its card.";
            }

            if (!CanOwn(nameA) && !CanOwn(nameB))
            {
                return "Neither of these has an ETABS name. A pairing is written from the ETABS "
                       + "side, so one of the two has to be the ETABS element.";
            }

            return "Neither of these has a Revit id, so there is nothing here to pair with.";
        }

        private static bool CanOwn(string etabsUniqueName)
        {
            return !string.IsNullOrEmpty(etabsUniqueName);
        }

        /// <summary>An element carrying both an ETABS name and a Revit id is a pair already.</summary>
        private static bool IsPaired(string etabsUniqueName, long revitId)
        {
            return CanOwn(etabsUniqueName) && revitId > 0;
        }
    }
}
