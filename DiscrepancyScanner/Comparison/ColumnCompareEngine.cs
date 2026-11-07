using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Pairs ETABS columns with Revit columns and reports the location, orientation and
    /// section differences between them.
    ///
    /// Pairing runs in two passes. The first pass only accepts a Revit column that already
    /// sits inside the location buffer, so a correctly placed column can never be stolen by
    /// a badly placed neighbour. The second pass widens the search to the search radius so
    /// that a column which is simply in the wrong place still gets reported as a location
    /// mismatch instead of disappearing into the unmatched list.
    /// </summary>
    public class ColumnCompareEngine
    {
        private readonly List<PairedColumn> pairedColumns = new List<PairedColumn>();
        private readonly List<Column_e> unmatchedEtabsColumns = new List<Column_e>();
        private readonly List<Column_r> unmatchedRevitColumns = new List<Column_r>();

        public List<PairedColumn> PairedColumns { get => pairedColumns; }
        public List<Column_e> UnmatchedEtabsColumns { get => unmatchedEtabsColumns; }
        public List<Column_r> UnmatchedRevitColumns { get => unmatchedRevitColumns; }

        /// <summary>
        /// The ETABS columns somebody said by hand should pair with nothing, by unique name.
        /// They are in the unmatched list like any other, and this is what keeps the pairing
        /// across columns and walls from pairing them anyway.
        /// </summary>
        public HashSet<string> HeldUnpaired { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The pairs a person set by hand, and which project to read them from. Left unset the
        /// engine pairs entirely by distance, which is what every test that does not care about
        /// overrides relies on.
        /// </summary>
        public PairingOverrides Overrides { get; set; }
        public string ProjectKey { get; set; }

        public void StartComparison(IList<Column_e> etabsColumns, IList<Column_r> revitColumns)
        {
            pairedColumns.Clear();
            unmatchedEtabsColumns.Clear();
            unmatchedRevitColumns.Clear();
            HeldUnpaired.Clear();

            if (etabsColumns == null || revitColumns == null)
            {
                return;
            }

            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            List<Column_e> remainingEtabs = etabsColumns.ToList();
            List<Column_r> remainingRevit = revitColumns.ToList();

            // The pairs set by hand go first and take both sides out of the pool, so the
            // distance passes below never get to see - or steal - either half of one.
            PairByHand(remainingEtabs, remainingRevit);

            Pair(remainingEtabs, remainingRevit, pref.ColumnLocationTolerance);
            Pair(remainingEtabs, remainingRevit, pref.ColumnSearchRadius);

            unmatchedEtabsColumns.AddRange(remainingEtabs);
            unmatchedRevitColumns.AddRange(remainingRevit);

            MarkUnmatched();
        }

        /// <summary>
        /// Consumes the pairings someone set by hand, whatever the distances say. A pairing
        /// naming an element that is no longer in either model is skipped and left in the store:
        /// a column deleted for one run and restored the next should not cost the decision that
        /// was made about it.
        /// </summary>
        private void PairByHand(List<Column_e> remainingEtabs, List<Column_r> remainingRevit)
        {
            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return;
            }

            // Dropped first where either column has moved since the pairing was set.
            Overrides.Check(ProjectKey, ElementKindKey.Column,
                PairPlace.ByName(remainingEtabs, column => column.EtabsUniqueName, column => PairPlace.Of(column)),
                PairPlace.ById(remainingRevit, column => column.RevitID, column => PairPlace.Of(column)));

            foreach (Column_e etabsColumn in remainingEtabs.ToList())
            {
                long? revitId = Overrides.Find(ProjectKey,
                    PairingOverrides.KeyOf(ElementKindKey.Column, etabsColumn.EtabsUniqueName));
                if (revitId == null)
                {
                    continue;
                }

                if (revitId.Value == PairingOverrides.Unpaired)
                {
                    // Said to pair with nothing. Taken out of the pool so no later pass can
                    // pair it anyway, and reported unmatched, which is the answer that was
                    // asked for.
                    remainingEtabs.Remove(etabsColumn);
                    unmatchedEtabsColumns.Add(etabsColumn);
                    HeldUnpaired.Add(etabsColumn.EtabsUniqueName);
                    continue;
                }

                // A column running through several stories is in the pool once per story; the
                // piece on the ETABS column's own story is the one meant.
                Column_r match = remainingRevit
                    .Where(c => c.RevitID == revitId.Value)
                    .OrderBy(c => IsSameStory(etabsColumn, c) ? 0 : 1)
                    .FirstOrDefault();
                if (match == null)
                {
                    continue;
                }

                remainingRevit.Remove(match);
                remainingEtabs.Remove(etabsColumn);
                pairedColumns.Add(new PairedColumn(etabsColumn, match));
            }
        }

        private void Pair(List<Column_e> remainingEtabs, List<Column_r> remainingRevit, double radius)
        {
            List<Column_e> matchedEtabs = new List<Column_e>();

            foreach (Column_e etabsColumn in remainingEtabs)
            {
                Column_r nearest = GetNearestRevitColumn(etabsColumn, remainingRevit, radius);
                if (nearest == null)
                {
                    continue;
                }

                remainingRevit.Remove(nearest);
                matchedEtabs.Add(etabsColumn);
                pairedColumns.Add(new PairedColumn(etabsColumn, nearest));
            }

            foreach (Column_e matched in matchedEtabs)
            {
                remainingEtabs.Remove(matched);
            }
        }

        private Column_r GetNearestRevitColumn(Column_e etabsColumn, List<Column_r> candidates, double radius)
        {
            Column_r nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (Column_r candidate in candidates)
            {
                if (!IsSameStory(etabsColumn, candidate))
                {
                    continue;
                }

                double distance = ColumnGeometry.PlanDistance(etabsColumn.PlanPoint, candidate.PlanPoint);
                if (distance <= radius && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        private static bool IsSameStory(Column_e etabsColumn, Column_r revitColumn)
        {
            // An unassigned story on either side means we cannot rule the pair out on story
            // alone, so fall back to the plan distance test.
            if (string.IsNullOrEmpty(etabsColumn.Story) || string.IsNullOrEmpty(revitColumn.Story))
            {
                return true;
            }
            return string.Equals(etabsColumn.Story, revitColumn.Story, StringComparison.OrdinalIgnoreCase);
        }

        private void MarkUnmatched()
        {
            foreach (Column_e column in unmatchedEtabsColumns)
            {
                column.comparasonState = ComparasonState.EtabsUnmatched;
                column.AddMessage(column.MessagePoint, "Un Matched Etabs Column", MessageType.State);
            }

            foreach (Column_r column in unmatchedRevitColumns)
            {
                column.comparasonState = ComparasonState.RevitUnmatched;
                column.AddMessage(column.MessagePoint, "Un Matched Revit Column", MessageType.State);
            }
        }
    }
}
