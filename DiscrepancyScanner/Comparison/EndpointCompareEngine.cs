using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Pairs beams on where their two ends sit, rather than on where their midpoints sit.
    ///
    /// An ETABS frame and a Revit beam pair when both ends line up, in either order: a beam
    /// drawn from the far end is the same beam. Two ends already fix the direction and the
    /// length between them, so this engine has no angle or length tolerance of its own - a
    /// beam running at a different angle, or running longer, is one whose end is simply too
    /// far away to match.
    ///
    /// Where several beams could pair, only the nearest one does. Candidates are scored across
    /// the whole model and taken best first, so the result does not depend on the order the
    /// beams were read in, and a well placed beam is never taken by a worse placed neighbour.
    ///
    /// Nothing is merged: a Revit beam that spans several ETABS frames leaves all of them
    /// unpaired rather than joining them up, which is where this differs from
    /// <see cref="CompareEngine"/> beyond the search itself.
    /// </summary>
    public class EndpointCompareEngine
    {
        private readonly List<PairedFrame> pairedFrames = new List<PairedFrame>();
        private readonly List<FrameData_e> unmatchedEtabs = new List<FrameData_e>();
        private readonly List<FrameData_r> unmatchedRevit = new List<FrameData_r>();

        public List<PairedFrame> PairedFrames { get => pairedFrames; }
        public List<FrameData_e> UnmatchedEtabs { get => unmatchedEtabs; }
        public List<FrameData_r> UnmatchedRevit { get => unmatchedRevit; }

        /// <summary>One beam that could pair with one other, and how far off its ends are.</summary>
        private struct Candidate
        {
            public FrameData_e EtabsFrame;
            public FrameData_r RevitFrame;
            public double EndOffset;
        }

        /// <summary>
        /// The pairs a person set by hand, and which project to read them from. Left unset the
        /// engine pairs entirely by end distance, as it always did.
        /// </summary>
        public PairingOverrides Overrides { get; set; }
        public string ProjectKey { get; set; }

        public void StartComparison(IList<FrameData_r> revitFrames, IList<FrameData_e> etabsFrames)
        {
            pairedFrames.Clear();
            unmatchedEtabs.Clear();
            unmatchedRevit.Clear();

            if (revitFrames == null || etabsFrames == null)
            {
                return;
            }

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            double planTolerance = Math.Max(0, pref.BeamPlanTolerance);
            double verticalTolerance = Math.Max(0, pref.BeamVerticalTolerance);

            // A pairing whose beams have moved since it was set is dropped before any is read.
            // An ETABS model renumbered since hands the name to another beam altogether.
            if (Overrides != null)
            {
                Overrides.Check(ProjectKey, ElementKindKey.Beam,
                    PairPlace.ByName(etabsFrames, frame => frame.EtabsUname, frame => PairPlace.Of(frame)),
                    PairPlace.ById(revitFrames, frame => frame.RevitID, frame => PairPlace.Of(frame)));
            }

            List<Candidate> candidates = CollectCandidates(revitFrames, etabsFrames,
                                                           planTolerance, verticalTolerance);
            // Best first, so the nearest pairing of each beam is the one that gets taken.
            candidates.Sort((left, right) => left.EndOffset.CompareTo(right.EndOffset));

            HashSet<FrameData_e> pairedEtabs = new HashSet<FrameData_e>();
            HashSet<FrameData_r> pairedRevit = new HashSet<FrameData_r>();

            // Beams said to pair with nothing. Held apart from pairedEtabs because they are not
            // paired - they have to be kept out of the candidate loop and still come out
            // unmatched at the end, which is the answer that was asked for.
            HashSet<FrameData_e> blockedEtabs = new HashSet<FrameData_e>();

            PairByHand(revitFrames, etabsFrames, pairedEtabs, pairedRevit, blockedEtabs);

            foreach (Candidate candidate in candidates)
            {
                if (pairedEtabs.Contains(candidate.EtabsFrame) || pairedRevit.Contains(candidate.RevitFrame)
                    || blockedEtabs.Contains(candidate.EtabsFrame))
                {
                    continue;
                }

                pairedEtabs.Add(candidate.EtabsFrame);
                pairedRevit.Add(candidate.RevitFrame);
                pairedFrames.Add(new PairedFrame(candidate.EtabsFrame, candidate.RevitFrame));
            }

            unmatchedEtabs.AddRange(etabsFrames.Where(frame => !pairedEtabs.Contains(frame)));
            unmatchedRevit.AddRange(revitFrames.Where(frame => !pairedRevit.Contains(frame)));

            MarkUnmatched();
        }

        /// <summary>
        /// Takes the pairs someone set by hand before any distance is measured, so the beam
        /// they chose is out of the pool by the time the candidates are walked. A pairing naming
        /// a beam that is not in the model this time is skipped and left in the store.
        /// </summary>
        private void PairByHand(IList<FrameData_r> revitFrames, IList<FrameData_e> etabsFrames,
                                HashSet<FrameData_e> pairedEtabs, HashSet<FrameData_r> pairedRevit,
                                HashSet<FrameData_e> blockedEtabs)
        {
            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return;
            }

            foreach (FrameData_e etabsFrame in etabsFrames)
            {
                long? revitId = Overrides.Find(ProjectKey,
                    PairingOverrides.KeyOf(ElementKindKey.Beam, etabsFrame?.EtabsUname));
                if (revitId == null)
                {
                    continue;
                }

                if (revitId.Value == PairingOverrides.Unpaired)
                {
                    blockedEtabs.Add(etabsFrame);
                    continue;
                }

                FrameData_r match = revitFrames.FirstOrDefault(
                    frame => frame != null && frame.RevitID == revitId.Value
                             && !pairedRevit.Contains(frame));
                if (match == null)
                {
                    continue;
                }

                pairedEtabs.Add(etabsFrame);
                pairedRevit.Add(match);
                pairedFrames.Add(new PairedFrame(etabsFrame, match));
            }
        }

        /// <summary>
        /// Every ETABS and Revit beam whose ends line up, found without trying every ETABS beam
        /// against every Revit beam.
        ///
        /// It used to try them all, which on a building is the square of a large number: forty
        /// seven floors of three hundred and fifty beams is some two hundred and seventy million
        /// tries, five seconds of them, and they ran again after every pairing set by hand. Now
        /// each Revit beam is filed under the grid cell each of its ends falls in, a cell the
        /// size of the tolerance, and an ETABS beam is only tried against the Revit beams with
        /// an end in or beside the cell its own start is in. Nothing that could pair is missed:
        /// whichever way round two beams line up, the ETABS start is within the tolerance of one
        /// of the Revit ends, and a point within the tolerance of another is at most one cell
        /// away from it on every axis.
        ///
        /// The Revit beams are tried in the order they were read, the same order the full search
        /// tried them in, so the list that comes back is the same list and the sort after it
        /// settles a tie between two equally good beams the same way it always did.
        /// </summary>
        private static List<Candidate> CollectCandidates(IList<FrameData_r> revitFrames,
                                                         IList<FrameData_e> etabsFrames,
                                                         double planTolerance, double verticalTolerance)
        {
            List<Candidate> candidates = new List<Candidate>();

            EndGrid grid = new EndGrid(planTolerance, verticalTolerance, revitFrames.Count);
            for (int index = 0; index < revitFrames.Count; index++)
            {
                FrameData_r revitFrame = revitFrames[index];
                if (revitFrame?.Start == null || revitFrame.End == null)
                {
                    continue;
                }

                grid.Add(revitFrame.Start, index);
                grid.Add(revitFrame.End, index);
            }

            List<int> near = new List<int>();

            foreach (FrameData_e etabsFrame in etabsFrames)
            {
                if (etabsFrame?.Start == null || etabsFrame.End == null)
                {
                    continue;
                }

                grid.Near(etabsFrame.Start, near);

                foreach (int index in near)
                {
                    FrameData_r revitFrame = revitFrames[index];

                    double endOffset;
                    if (EndsLineUp(etabsFrame, revitFrame, planTolerance, verticalTolerance, out endOffset))
                    {
                        candidates.Add(new Candidate
                        {
                            EtabsFrame = etabsFrame,
                            RevitFrame = revitFrame,
                            EndOffset = endOffset
                        });
                    }
                }
            }

            return candidates;
        }

        /// <summary>
        /// True when the two beams have both ends within the tolerances, taking them either
        /// way round. <paramref name="endOffset"/> comes back as how far the two ends are off
        /// in total, which is what decides the nearest beam when several could pair; where a
        /// beam lines up both ways round, the closer reading of it is the one reported.
        /// </summary>
        private static bool EndsLineUp(FrameData_e etabsFrame, FrameData_r revitFrame,
                                       double planTolerance, double verticalTolerance,
                                       out double endOffset)
        {
            endOffset = 0;

            bool sameWayRound = IsCloseEnough(etabsFrame.Start, revitFrame.Start, planTolerance, verticalTolerance)
                                && IsCloseEnough(etabsFrame.End, revitFrame.End, planTolerance, verticalTolerance);

            bool otherWayRound = IsCloseEnough(etabsFrame.Start, revitFrame.End, planTolerance, verticalTolerance)
                                 && IsCloseEnough(etabsFrame.End, revitFrame.Start, planTolerance, verticalTolerance);

            if (!sameWayRound && !otherWayRound)
            {
                return false;
            }

            double best = double.MaxValue;
            if (sameWayRound)
            {
                best = etabsFrame.Start.DistanceTo(revitFrame.Start) + etabsFrame.End.DistanceTo(revitFrame.End);
            }
            if (otherWayRound)
            {
                best = Math.Min(best, etabsFrame.Start.DistanceTo(revitFrame.End)
                                      + etabsFrame.End.DistanceTo(revitFrame.Start));
            }

            endOffset = best;
            return true;
        }

        /// <summary>
        /// Two ends are the same end when they sit within the plan buffer of each other in
        /// plan, measured as a radius rather than per axis so the buffer reads the same in
        /// every direction, and within the vertical buffer in Z. Plan and level are kept apart
        /// because a model is normally far better placed in plan than it is in level.
        /// </summary>
        private static bool IsCloseEnough(MyPoint first, MyPoint second,
                                          double planTolerance, double verticalTolerance)
        {
            if (Math.Abs(first.Z - second.Z) > verticalTolerance)
            {
                return false;
            }

            double dx = first.X - second.X;
            double dy = first.Y - second.Y;
            return dx * dx + dy * dy <= planTolerance * planTolerance;
        }

        /// <summary>
        /// Beam ends filed by the grid cell they fall in, for finding the beams with an end near
        /// a point without looking at every beam.
        ///
        /// A cell is the tolerance and a millimetre across. The millimetre is what makes "at most
        /// one cell away" hold for two points exactly the tolerance apart, whatever the rounding
        /// of the division does to them; and it keeps a tolerance of nought from being a cell of
        /// no size.
        /// </summary>
        private sealed class EndGrid
        {
            private readonly Dictionary<Cell, List<int>> cells = new Dictionary<Cell, List<int>>();
            private readonly double planCell;
            private readonly double verticalCell;

            // Which ETABS beam each Revit beam was last found for, so one with both ends near
            // the point is tried once.
            private readonly int[] seen;
            private int stamp;

            public EndGrid(double planTolerance, double verticalTolerance, int count)
            {
                planCell = Math.Max(0, planTolerance) + 1;
                verticalCell = Math.Max(0, verticalTolerance) + 1;
                seen = new int[count];
            }

            public void Add(MyPoint end, int index)
            {
                Cell cell = CellOf(end);

                List<int> filed;
                if (!cells.TryGetValue(cell, out filed))
                {
                    filed = new List<int>();
                    cells[cell] = filed;
                }
                filed.Add(index);
            }

            /// <summary>
            /// The beams with an end in the cell of <paramref name="point"/> or one beside it,
            /// each once and in the order they were added.
            /// </summary>
            public void Near(MyPoint point, List<int> found)
            {
                found.Clear();
                stamp++;

                Cell centre = CellOf(point);
                for (long x = centre.X - 1; x <= centre.X + 1; x++)
                {
                    for (long y = centre.Y - 1; y <= centre.Y + 1; y++)
                    {
                        for (long z = centre.Z - 1; z <= centre.Z + 1; z++)
                        {
                            List<int> filed;
                            if (!cells.TryGetValue(new Cell(x, y, z), out filed))
                            {
                                continue;
                            }

                            foreach (int index in filed)
                            {
                                if (seen[index] != stamp)
                                {
                                    seen[index] = stamp;
                                    found.Add(index);
                                }
                            }
                        }
                    }
                }

                found.Sort();
            }

            private Cell CellOf(MyPoint point)
            {
                return new Cell((long)Math.Floor(point.X / planCell),
                                (long)Math.Floor(point.Y / planCell),
                                (long)Math.Floor(point.Z / verticalCell));
            }

            private struct Cell : IEquatable<Cell>
            {
                public readonly long X;
                public readonly long Y;
                public readonly long Z;

                public Cell(long x, long y, long z)
                {
                    X = x;
                    Y = y;
                    Z = z;
                }

                public bool Equals(Cell other)
                {
                    return X == other.X && Y == other.Y && Z == other.Z;
                }

                public override bool Equals(object obj)
                {
                    return obj is Cell && Equals((Cell)obj);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        long hash = X * 73856093L ^ Y * 19349663L ^ Z * 83492791L;
                        return (int)(hash ^ (hash >> 32));
                    }
                }
            }
        }

        private void MarkUnmatched()
        {
            foreach (FrameData_e frame in unmatchedEtabs)
            {
                frame.AddMessage(frame.MessagePoint, "Un Mached Etabs Frame", MessageType.State);
            }

            foreach (FrameData_r frame in unmatchedRevit)
            {
                frame.AddMessage(frame.MessagePoint, "Un Mached Revit Frame", MessageType.State);
            }
        }
    }
}
