using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Pairs ETABS wall areas with Revit walls and reports the thickness and location
    /// differences between them.
    ///
    /// A wall is not one element to one element the way a column is. ETABS splits a wall run
    /// into panels wherever the mesh wants a node, and Revit splits a wall wherever the person
    /// drawing it stopped, so one wall on the drawings can be five areas in one model and two
    /// walls in the other. Consuming a Revit wall the moment something matched it - which is
    /// what the column pairing does - would leave four of those five areas reported as walls
    /// Revit has not got.
    ///
    /// So a Revit wall may be claimed by as many ETABS areas as stand along it, each checked
    /// against it on its own. A second pass then goes the other way for the Revit walls nothing
    /// claimed, so a Revit wall standing along an ETABS area that already has a partner gets a
    /// verdict of its own rather than reading as unpaired. What is left after both passes is a
    /// wall with nothing on its line at all, which is the thing worth reporting as unpaired.
    /// </summary>
    /// <summary>
    /// A stretch of a Revit wall with no ETABS wall standing along it, long enough to be worth
    /// drawing. Carries the wall it was cut from, so the piece on the plan can still name the
    /// element and be selected back in Revit.
    /// </summary>
    public sealed class UncoveredWallPiece
    {
        public UncoveredWallPiece(Wall_R wall, MyPoint start, MyPoint end,
                                  double covered, double uncovered)
        {
            Wall = wall;
            Start = start;
            End = end;
            Covered = covered;
            Uncovered = uncovered;
        }

        public Wall_R Wall { get; }

        /// <summary>The ends of this piece, in plan.</summary>
        public MyPoint Start { get; }
        public MyPoint End { get; }

        /// <summary>How much of the whole wall has ETABS along it, in mm.</summary>
        public double Covered { get; }

        /// <summary>How much of the whole wall has not, in mm - every piece, not only this one.</summary>
        public double Uncovered { get; }

        /// <summary>The length of this piece, in mm.</summary>
        public double Length { get { return WallGeometry.PlanLength(Start, End); } }
    }

    public class WallCompareEngine
    {
        private readonly List<PairedWall> pairedWalls = new List<PairedWall>();
        private readonly List<Wall_E> unmatchedEtabsWalls = new List<Wall_E>();
        private readonly List<Wall_R> unmatchedRevitWalls = new List<Wall_R>();
        private readonly List<UncoveredWallPiece> uncoveredRevitWalls = new List<UncoveredWallPiece>();
        private readonly List<string> issues = new List<string>();

        public List<PairedWall> PairedWalls { get => pairedWalls; }
        public List<Wall_E> UnmatchedEtabsWalls { get => unmatchedEtabsWalls; }
        public List<Wall_R> UnmatchedRevitWalls { get => unmatchedRevitWalls; }

        /// <summary>
        /// The ETABS panels somebody said by hand should pair with nothing, by unique name, so
        /// the pairing across columns and walls leaves them that way too.
        /// </summary>
        public HashSet<string> HeldUnpaired { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The stretches of paired Revit wall that no ETABS panel stands along, where there is
        /// enough of one to be worth drawing.
        ///
        /// The plan is the ETABS model, and a paired Revit wall is not drawn on it: where ETABS
        /// stops short, the panel says so as a warning. Past
        /// <see cref="PrefrencesData.WallUncoveredDrawLength"/> a warning is not enough - a wall
        /// ETABS has a metre of and Revit has six is a hole in the design model, and the shape
        /// of the hole is the thing to see. What is drawn is the missing stretch alone, so
        /// nothing is laid over the panels that are there.
        /// </summary>
        public List<UncoveredWallPiece> UncoveredRevitWalls { get => uncoveredRevitWalls; }

        /// <summary>Anything the check could not do, collected so it can be shown at the end.</summary>
        public IList<string> Issues { get => issues; }

        /// <summary>
        /// The pairs a person set by hand, and which project to read them from. Left unset the
        /// engine pairs entirely by geometry, as it always did.
        /// </summary>
        public PairingOverrides Overrides { get; set; }
        public string ProjectKey { get; set; }

        public void StartComparison(IList<Wall_E> etabsWalls, IList<Wall_R> revitWalls)
        {
            pairedWalls.Clear();
            unmatchedEtabsWalls.Clear();
            unmatchedRevitWalls.Clear();
            uncoveredRevitWalls.Clear();
            issues.Clear();
            HeldUnpaired.Clear();

            if (etabsWalls == null || revitWalls == null)
            {
                return;
            }

            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            List<Wall_E> etabsToCheck = Checkable(etabsWalls);
            List<Wall_R> revitToCheck = revitWalls.Where(wall => wall != null && wall.Length > 0).ToList();

            WallStoryIndex<Wall_R> revitByStory = new WallStoryIndex<Wall_R>(revitToCheck);
            HashSet<Wall_R> claimedRevit = new HashSet<Wall_R>();

            // One drawn wall is often several ETABS panels stacked on the same line, and the
            // card pairs the wall rather than the panel - so a pairing set on any one panel is
            // read as being about the whole stack. Resolved up front, because a panel lower in
            // the list has to see a decision made on one higher up.
            Dictionary<string, long> byStack = HandPairedStacks(etabsToCheck, revitToCheck);

            foreach (Wall_E etabsWall in etabsToCheck)
            {
                long pinned;
                if (byStack.TryGetValue(StackKey(etabsWall), out pinned))
                {
                    if (pinned == PairingOverrides.Unpaired)
                    {
                        unmatchedEtabsWalls.Add(etabsWall);
                        HeldUnpaired.Add(etabsWall.UniqueName);
                        continue;
                    }

                    Wall_R chosen = revitToCheck.FirstOrDefault(w => w.RevitID == pinned);
                    if (chosen != null)
                    {
                        claimedRevit.Add(chosen);
                        pairedWalls.Add(new PairedWall(etabsWall, chosen));
                        continue;
                    }

                    // Named a wall this model has not got. Left in the store in case it comes
                    // back, and decided by geometry this time round.
                }

                Wall_R match = Best(etabsWall, revitByStory.For(etabsWall.Story), pref);
                if (match == null)
                {
                    unmatchedEtabsWalls.Add(etabsWall);
                    continue;
                }

                claimedRevit.Add(match);
                pairedWalls.Add(new PairedWall(etabsWall, match));
            }

            // The other way round, for the Revit walls no ETABS area picked. A Revit wall run
            // split in two where ETABS has one panel is the case this catches: the panel paired
            // with one half, and without this the other half would read as a wall ETABS has not
            // got, when what is actually there is the same wall drawn in two pieces.
            WallStoryIndex<Wall_E> etabsByStory = new WallStoryIndex<Wall_E>(etabsToCheck);

            foreach (Wall_R revitWall in revitToCheck)
            {
                if (claimedRevit.Contains(revitWall))
                {
                    continue;
                }

                Wall_E match = Best(revitWall, etabsByStory.For(revitWall.Story), pref);
                if (match == null)
                {
                    unmatchedRevitWalls.Add(revitWall);
                    continue;
                }

                pairedWalls.Add(new PairedWall(match, revitWall));
            }

            MeasureCoverage(pref);

            ReportStoriesWithWallsInOneModelOnly(etabsToCheck, revitToCheck);
        }

        /// <summary>
        /// How much ETABS wall stands along each paired Revit wall, told to every pair on it.
        ///
        /// This is what the length warning is made of, and it cannot be worked out one pair at
        /// a time: a wall meshed into five panels is five pairs, and no one of them knows what
        /// the other four cover. It is measured as the stretch of the Revit wall covered rather
        /// than as the panels added up, because panels stacked up the height of a wall all lie
        /// along the same stretch of it and would otherwise count once each - which would leave
        /// every meshed wall reading as covered several times over. See
        /// <see cref="WallGeometry.CoveredLength"/>.
        /// </summary>
        private void MeasureCoverage(PrefrencesData pref)
        {
            Dictionary<Wall_R, List<PairedWall>> pairsByWall = new Dictionary<Wall_R, List<PairedWall>>();

            foreach (PairedWall pair in pairedWalls)
            {
                List<PairedWall> onWall;
                if (!pairsByWall.TryGetValue(pair.revitWall, out onWall))
                {
                    onWall = new List<PairedWall>();
                    pairsByWall[pair.revitWall] = onWall;
                }
                onWall.Add(pair);
            }

            foreach (KeyValuePair<Wall_R, List<PairedWall>> entry in pairsByWall)
            {
                List<IWall> panels = entry.Value.Select(pair => (IWall)pair.etabsWall).ToList();
                double covered = WallGeometry.CoveredLength(entry.Key, panels);

                foreach (PairedWall pair in entry.Value)
                {
                    pair.SetCoveredLength(covered);
                }

                CutTheUncoveredStretches(entry.Key, panels, covered, pref);
            }
        }

        /// <summary>
        /// The pieces of one Revit wall that no panel stands along, kept for the plan where
        /// there is more of them than the buffer allows.
        ///
        /// Judged on the wall as a whole rather than piece by piece: two gaps of six hundred
        /// leave a wall as badly served as one gap of twelve hundred, and it is the wall being
        /// asked about. Once it is worth drawing, every piece of it is drawn, down to the
        /// shortest wall the check bothers with at all - below that a piece is the sliver two
        /// walls leave where they meet rather than a stretch of wall nobody modelled.
        /// </summary>
        private void CutTheUncoveredStretches(Wall_R wall, List<IWall> panels, double covered,
                                              PrefrencesData pref)
        {
            double uncovered = wall.Length - covered;
            if (uncovered <= pref.WallUncoveredDrawLength)
            {
                return;
            }

            foreach (MyPoint[] piece in WallGeometry.UncoveredSpans(wall, panels, pref.WallMinimumLength))
            {
                uncoveredRevitWalls.Add(new UncoveredWallPiece(wall, piece[0], piece[1],
                                                               covered, uncovered));
            }
        }

        /// <summary>
        /// The hand-set pairings on this story's walls, spread across each panel's whole stack.
        /// </summary>
        private Dictionary<string, long> HandPairedStacks(IList<Wall_E> etabsWalls, IList<Wall_R> revitWalls)
        {
            Dictionary<string, long> byStack = new Dictionary<string, long>(StringComparer.Ordinal);
            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return byStack;
            }

            // Dropped first where either wall has moved since the pairing was set.
            Overrides.Check(ProjectKey, ElementKindKey.Wall,
                PairPlace.ByName(etabsWalls, wall => wall.UniqueName, wall => PairPlace.Of(wall)),
                PairPlace.ById(revitWalls, wall => wall.RevitID, wall => PairPlace.Of(wall)));

            foreach (Wall_E wall in etabsWalls)
            {
                long? revitId = Overrides.Find(ProjectKey,
                    PairingOverrides.KeyOf(ElementKindKey.Wall, wall.UniqueName));
                if (revitId != null)
                {
                    byStack[StackKey(wall)] = revitId.Value;
                }
            }
            return byStack;
        }

        /// <summary>
        /// Which drawn wall a panel belongs to: its story and the line it stands on, taken in a
        /// fixed order so the two ends cannot key the same wall two ways. Deliberately the same
        /// rule the viewer groups panels by, so a pairing set on the wall you can see applies to
        /// every panel that wall is drawn from.
        /// </summary>
        private static string StackKey(Wall_E wall)
        {
            long x1 = Rounded(wall.PlanStart == null ? 0 : wall.PlanStart.X);
            long y1 = Rounded(wall.PlanStart == null ? 0 : wall.PlanStart.Y);
            long x2 = Rounded(wall.PlanEnd == null ? 0 : wall.PlanEnd.X);
            long y2 = Rounded(wall.PlanEnd == null ? 0 : wall.PlanEnd.Y);

            bool inOrder = x1 < x2 || (x1 == x2 && y1 <= y2);

            return string.Join("|", (wall.Story ?? "").ToUpperInvariant(),
                               inOrder ? x1 : x2, inOrder ? y1 : y2,
                               inOrder ? x2 : x1, inOrder ? y2 : y1);
        }

        /// <summary>Two ends within this are the same end. Matches WallStacks.</summary>
        private const double SamePlaceInMM = 10;

        private static long Rounded(double value)
        {
            return (long)Math.Round(value / SamePlaceInMM);
        }

        /// <summary>
        /// Names the stories where one model has walls and the other has none at all.
        ///
        /// Every wall on such a story is reported as unpaired, and that reads as two models
        /// that disagree about a whole floor when what has usually happened is that the two
        /// name the story differently, or that a Revit wall's top lands nearer the level above
        /// than the one it belongs to. A tally of unpaired walls cannot be told from either;
        /// the name of the story can, and it is what someone would need to go and look.
        /// </summary>
        private void ReportStoriesWithWallsInOneModelOnly(IList<Wall_E> etabsWalls, IList<Wall_R> revitWalls)
        {
            HashSet<string> etabsStories = NamedStories(etabsWalls.Cast<IWall>());
            HashSet<string> revitStories = NamedStories(revitWalls.Cast<IWall>());

            List<string> etabsOnly = etabsStories.Where(story => !revitStories.Contains(story))
                                                 .OrderBy(story => story, StringComparer.OrdinalIgnoreCase)
                                                 .ToList();
            List<string> revitOnly = revitStories.Where(story => !etabsStories.Contains(story))
                                                 .OrderBy(story => story, StringComparer.OrdinalIgnoreCase)
                                                 .ToList();

            if (etabsOnly.Count > 0)
            {
                issues.Add("ETABS has walls on " + string.Join(", ", etabsOnly) + " and the Revit model "
                           + "has none there at all, so every one of them reads as unpaired. Check that "
                           + "the two models name that level the same way, and that the Revit walls on it "
                           + "rise to it rather than to the level above.");
            }

            if (revitOnly.Count > 0)
            {
                issues.Add("The Revit model has walls on " + string.Join(", ", revitOnly) + " and ETABS "
                           + "has none there at all, so every one of them reads as unpaired.");
            }
        }

        private static HashSet<string> NamedStories(IEnumerable<IWall> walls)
        {
            HashSet<string> stories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (IWall wall in walls)
            {
                // A wall with no story is in reach of every one of them, so it cannot make a
                // story look empty and has no place in this.
                if (!string.IsNullOrEmpty(wall.Story))
                {
                    stories.Add(wall.Story);
                }
            }

            return stories;
        }

        /// <summary>
        /// The ETABS walls there is anything to check. An area whose footprint is a single
        /// point has no line to compare and is counted here rather than passed over in silence.
        /// </summary>
        private List<Wall_E> Checkable(IList<Wall_E> etabsWalls)
        {
            List<Wall_E> checkable = new List<Wall_E>();
            int withoutLine = 0;

            foreach (Wall_E wall in etabsWalls)
            {
                if (wall == null)
                {
                    continue;
                }

                if (!wall.HasCentreLine || wall.Length <= 0)
                {
                    withoutLine++;
                    continue;
                }

                checkable.Add(wall);
            }

            if (withoutLine > 0)
            {
                issues.Add($"{withoutLine} ETABS wall area(s) have no line in plan - every corner of the "
                           + "area is at the same place - so there was nothing to check them against and "
                           + "they were left out of the wall check.");
            }

            return checkable;
        }

        /// <summary>
        /// The best counterpart for one wall, or null when nothing on its line will do.
        ///
        /// Three gates and then a preference. The gates: the two must run the same way, must
        /// stand alongside each other rather than merely cross, and must be within reach at
        /// all. The preference: a wall inside the location buffer beats one outside it however
        /// much of it runs alongside, and among those inside the buffer the one that runs
        /// alongside for longest wins - which is what picks the wall itself over a stub
        /// crossing it. Outside the buffer the nearest one wins, so a wall in the wrong place
        /// is still reported as being in the wrong place instead of as unpaired.
        /// </summary>
        private static TCandidate Best<TCandidate>(IWall wall, IEnumerable<TCandidate> candidates,
                                                   PrefrencesData pref)
            where TCandidate : class, IWall
        {
            TCandidate best = null;
            double bestOffset = double.MaxValue;
            double bestOverlap = 0;
            bool bestInsideBuffer = false;

            double minimumOverlap = MinimumOverlap(wall, pref);

            foreach (TCandidate candidate in candidates)
            {
                if (WallGeometry.AngleBetween(wall, candidate) > pref.WallAngleTolerance)
                {
                    continue;
                }

                double offset = WallGeometry.Offset(wall, candidate);
                if (offset > pref.WallSearchRadius)
                {
                    continue;
                }

                double overlap = WallGeometry.OverlapLength(wall, candidate);
                if (overlap < minimumOverlap)
                {
                    continue;
                }

                bool insideBuffer = offset <= pref.WallLocationTolerance;

                if (best != null && !IsBetter(insideBuffer, offset, overlap,
                                              bestInsideBuffer, bestOffset, bestOverlap))
                {
                    continue;
                }

                best = candidate;
                bestOffset = offset;
                bestOverlap = overlap;
                bestInsideBuffer = insideBuffer;
            }

            return best;
        }

        private static bool IsBetter(bool insideBuffer, double offset, double overlap,
                                     bool bestInsideBuffer, double bestOffset, double bestOverlap)
        {
            if (insideBuffer != bestInsideBuffer)
            {
                return insideBuffer;
            }

            if (insideBuffer)
            {
                return overlap > bestOverlap;
            }

            return offset < bestOffset;
        }

        /// <summary>
        /// How much of a wall has to run alongside a candidate for the two to be the same wall.
        ///
        /// Never more than half the wall's own length, so a short panel - the piece of wall over
        /// a door, or the stub either side of it - is not held to a length it does not have.
        /// </summary>
        private static double MinimumOverlap(IWall wall, PrefrencesData pref)
        {
            double length = WallGeometry.PlanLength(wall.PlanStart, wall.PlanEnd);
            return Math.Min(pref.WallMinimumOverlap, length / 2d);
        }

        /// <summary>
        /// The walls of one model, in reach by story. Walls are compared story by story like
        /// everything else, and going through every wall in the building for each one of them
        /// is work that grows with the square of the model.
        ///
        /// A wall with no story on it is in reach of every story: that is what an unassigned
        /// story means, the same way the column pairing falls back to the plan distance when
        /// either side has no story name.
        /// </summary>
        private sealed class WallStoryIndex<T> where T : class, IWall
        {
            private readonly Dictionary<string, List<T>> byStory =
                new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
            private readonly List<T> withoutStory = new List<T>();
            private readonly List<T> all;

            public WallStoryIndex(IEnumerable<T> walls)
            {
                all = walls.ToList();

                foreach (T wall in all)
                {
                    if (string.IsNullOrEmpty(wall.Story))
                    {
                        withoutStory.Add(wall);
                        continue;
                    }

                    List<T> onStory;
                    if (!byStory.TryGetValue(wall.Story, out onStory))
                    {
                        onStory = new List<T>();
                        byStory[wall.Story] = onStory;
                    }
                    onStory.Add(wall);
                }
            }

            public IEnumerable<T> For(string story)
            {
                if (string.IsNullOrEmpty(story))
                {
                    return all;
                }

                List<T> onStory;
                if (!byStory.TryGetValue(story, out onStory))
                {
                    return withoutStory;
                }

                return withoutStory.Count == 0 ? onStory : onStory.Concat(withoutStory);
            }
        }
    }
}
