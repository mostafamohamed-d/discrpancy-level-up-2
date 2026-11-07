using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Compares the line loads drawn on a loading plan against the distributed loads on the
    /// ETABS frames.
    ///
    /// Not a pairing problem, which is what makes it tractable. A drawn line and a frame are
    /// not two versions of one element the way a column is: one frame can carry what the
    /// drawing shows as three line loads, one drawn line can run past four frames, and neither
    /// side promises the other anything about where it starts or stops. So nothing is paired.
    /// Each frame is read on its own - never chained to its neighbours - and a drawn line is
    /// cut where one frame under it stops and the next starts, each part projected onto its
    /// frame's axis as a chainage. The question then becomes the only one that has a well
    /// defined answer at every point: how much load does the plan ask for here, and how much
    /// does this frame carry here.
    ///
    /// Two verdicts come out of that, and the second is the reason the first is allowed to be
    /// strict:
    ///
    ///   stretch by stretch  what the plan asks for at each chainage against what is there
    ///   as a whole          the total force on the run, both sides
    ///
    /// Where the stretches disagree and the totals agree, the answer is not that the model is
    /// wrong. It is that the same load has been put on differently - which is exactly what
    /// happens when a stepped or curving run of drawn lines is replaced in ETABS by one
    /// straight frame, with the value scaled up to make up the length lost. That is a real and
    /// deliberate piece of modelling, and it comes out of these two numbers without anyone
    /// having to declare it: the check says the totals agree, says what the two lengths were,
    /// and puts it up for review rather than calling it a fault.
    /// </summary>
    public sealed class LineLoadCompareEngine
    {
        private readonly List<LineLoadResult> results = new List<LineLoadResult>();
        private readonly List<EtabsLineLoad> unclaimed = new List<EtabsLineLoad>();
        private readonly List<EtabsLineLoad> etabsStretches = new List<EtabsLineLoad>();
        private readonly List<LineLoadRun> runs = new List<LineLoadRun>();
        private readonly List<string> issues = new List<string>();

        private readonly Dictionary<string, LineLoadSchedule> schedules =
            new Dictionary<string, LineLoadSchedule>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> storyAboveHeights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> inScope =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One result per drawn load line, which is what the plan is coloured from.</summary>
        public List<LineLoadResult> Results { get { return results; } }

        /// <summary>The stretches of ETABS frame carrying a load the plan never asked for.</summary>
        public List<EtabsLineLoad> Unclaimed { get { return unclaimed; } }

        /// <summary>
        /// Every stretch of ETABS frame carrying a line load, claimed or not.
        ///
        /// A superset of <see cref="Unclaimed"/>. The check answers a claimed stretch through
        /// the drawn line over it and reports only the unclaimed ones, but the claimed ones are
        /// still wanted: an acceptance filed on a stretch while nothing claimed it has to be
        /// found and taken back once something does.
        /// </summary>
        public List<EtabsLineLoad> EtabsStretches { get { return etabsStretches; } }

        /// <summary>The runs the lines were read against - one per frame - kept for the viewer.</summary>
        public List<LineLoadRun> Runs { get { return runs; } }

        /// <summary>Anything the check could not do, collected so it can be shown at the end.</summary>
        public IList<string> Issues { get { return issues; } }

        /// <summary>
        /// Which ETABS load patterns are read. The line load sheets these are checked against
        /// are headed SIDL and hold nothing else, so a frame's live load and its wind load are
        /// not what the drawing is asking about - see <see cref="LineLoadPatterns"/>.
        ///
        /// Left unset it reads every downward distributed load, which is what a check with no
        /// settings behind it should do rather than silently reading none.
        /// </summary>
        public LineLoadPatterns Patterns { get; set; }

        /// <summary>Where <see cref="Patterns"/> is set, for the message that says to change it.</summary>
        public string PatternsSetting { get; set; } = "\"ETABS load patterns to read\" under Settings, Line loads";

        /// <summary>
        /// The pairs a person set by hand, and which project to read them from. Left unset the
        /// engine decides entirely by geometry, as it does for everything nobody has corrected.
        ///
        /// A pairing here is written against an ETABS frame, and what it pins is the whole run
        /// that frame belongs to - the same bargain the wall check makes with a panel and its
        /// stack. A run has no name that survives to the next check; a frame does.
        /// </summary>
        public PairingOverrides Overrides { get; set; }

        /// <summary>
        /// The patches of a level drawn by hand where the lines take a height of their own
        /// instead of the floor to floor height - see <see cref="HeightZone"/>. Read from the
        /// same project as <see cref="Overrides"/>. Left unset, every line takes its level's.
        /// </summary>
        public HeightZones HeightZones { get; set; }

        public string ProjectKey { get; set; }

        /// <summary>
        /// How far what ETABS carries may sit from what is asked for, below and above. Left
        /// unset it is the line load buffer either way, which is how the loads off the plan
        /// are read; the SIDL_C check sets one that lets a load through heavy far more readily
        /// than light - see <see cref="LoadBand"/>.
        /// </summary>
        public LoadBand Band { get; set; }

        private LoadBand band = LoadBand.Symmetric(0);

        /// <summary>
        /// How far a line may sit off its frame, in mm, with nothing looked for past it. Left
        /// unset, the line load settings: a buffer the line is expected inside, and a search
        /// radius past it. The SIDL_C check sets its own, which is both.
        /// </summary>
        public double? LocationTolerance { get; set; }

        /// <summary>How far off its frame a line is expected to be drawn, in mm.</summary>
        private double reach;

        /// <summary>How far to look for a frame under a line, in mm; never short of <see cref="reach"/>.</summary>
        private double radius;

        /// <summary>
        /// The stories the check covers, whether or not anything was drawn on them.
        ///
        /// Set from the levels the user paired, and needed because the two halves of this check
        /// do not arrive together: a story can have ETABS line loads and no drawn lines at all,
        /// and that story's loads are exactly the ones worth reporting as asked for by nobody.
        /// Without this the check would only ever look where the drawing already said to look,
        /// which is the half of the question that cannot find anything missing.
        /// </summary>
        public void SetStoriesToCheck(IEnumerable<string> stories)
        {
            if (stories == null)
            {
                return;
            }

            foreach (string story in stories.Where(name => !string.IsNullOrEmpty(name)))
            {
                inScope.Add(story);
            }
        }

        /// <summary>The schedule sheet one story's line load numbers are looked up in.</summary>
        public void SetSchedule(string story, LineLoadSchedule schedule)
        {
            if (!string.IsNullOrEmpty(story) && schedule != null)
            {
                schedules[story] = schedule;
                inScope.Add(story);
            }
        }

        /// <summary>
        /// How far it is from one story to the one above it, in mm, which is the height a wall
        /// standing on that floor rises through. Not set for a story means the topmost one, or
        /// one whose neighbour is not in the model.
        /// </summary>
        public void SetStoryAboveHeight(string story, double heightMM)
        {
            if (!string.IsNullOrEmpty(story))
            {
                storyAboveHeights[story] = heightMM;
            }
        }

        /// <summary>
        /// The story heights worked out from a model's stories: each one's gap to the next one
        /// up. The topmost is left out rather than guessed at.
        /// </summary>
        public void SetStoryHeights(IEnumerable<Story_E> stories)
        {
            if (stories == null)
            {
                return;
            }

            List<Story_E> byElevation = stories.Where(story => story != null)
                                               .OrderBy(story => story.Elevation)
                                               .ToList();

            for (int i = 0; i < byElevation.Count - 1; i++)
            {
                SetStoryAboveHeight(byElevation[i].Name,
                                    byElevation[i + 1].Elevation - byElevation[i].Elevation);
            }
        }

        public void StartComparison(IList<LineLoad_R> drawnLines, IList<LineLoadFrame_E> frames)
        {
            results.Clear();
            unclaimed.Clear();
            etabsStretches.Clear();
            runs.Clear();
            issues.Clear();

            if (drawnLines == null)
            {
                drawnLines = new List<LineLoad_R>();
            }

            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            LineLoadPatterns patterns = Patterns ?? LineLoadPatterns.From(pref);
            band = Band ?? LoadBand.Symmetric(pref.LineLoadTolerance);
            reach = Math.Max(0, LocationTolerance ?? pref.LineLoadLocationTolerance);
            radius = LocationTolerance.HasValue ? reach : Math.Max(pref.LineLoadSearchRadius, reach);

            // Dropped first where the frame or the drawn line has moved since the pairing was
            // set, so every lookup of the pairings below reads only the ones that still hold.
            if (Overrides != null)
            {
                Overrides.Check(ProjectKey, ElementKindKey.LineLoad,
                    PairPlace.ByName(frames, frame => frame.UniqueName, frame => PairPlace.Of(frame)),
                    PairPlace.ById(drawnLines, line => line.RevitID, line => PairPlace.Of(line)));
            }

            runs.AddRange(LineLoadRun.Build(frames));

            foreach (LineLoadRun run in runs)
            {
                run.Carries = Describe(run.GravityProfile(patterns), run.Length);
            }

            ReportLoadsThatCannotBeCompared(frames, patterns);

            Dictionary<string, List<LineLoad_R>> byStory = drawnLines
                .Where(line => line != null)
                .GroupBy(line => line.Story ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(),
                              StringComparer.OrdinalIgnoreCase);

            // Every story in scope, not only the ones something was drawn on. A story with
            // ETABS line loads and no drawn lines is not a story with nothing to say - it is
            // the clearest finding this check can make, and walking the drawing's stories alone
            // would be asking only the half of the question that cannot find anything missing.
            HashSet<string> stories = new HashSet<string>(byStory.Keys, StringComparer.OrdinalIgnoreCase);
            stories.UnionWith(inScope);

            foreach (string story in stories)
            {
                List<LineLoad_R> onStory;
                if (!byStory.TryGetValue(story, out onStory))
                {
                    onStory = new List<LineLoad_R>();
                }

                CompareOneStory(story, onStory, pref, patterns);
            }
        }

        #region one story

        private void CompareOneStory(string story, List<LineLoad_R> drawnLines,
                                     PrefrencesData pref, LineLoadPatterns patterns)
        {
            List<LineLoadRun> onStory = runs
                .Where(run => run.Length > 0
                              && (run.Story.Length == 0 || story.Length == 0
                                  || string.Equals(run.Story, story, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // The runs somebody pinned a drawn line to, resolved before anything is measured,
            // so the line is out of the geometry's hands by the time it would have been placed.
            Dictionary<long, LineLoadRun> pinned = PinnedRuns(onStory);
            HashSet<LineLoadRun> spokenFor = RunsPinnedToNothing(onStory);

            LineLoadSchedule schedule;
            schedules.TryGetValue(story ?? "", out schedule);

            double storyAbove;
            if (!storyAboveHeights.TryGetValue(story ?? "", out storyAbove))
            {
                storyAbove = 0;
            }

            // What each line is worth and which run it belongs to, worked out before anything
            // is compared: a run's planned profile is made of every line on it, so no one line
            // can be judged until they have all been placed.
            //
            // Every line is split over the frames it runs along, each part on its own frame -
            // see AssignAlongFrames.
            Dictionary<long, List<LineLoadRun>> pinnedFrames = PinnedFrames(onStory);
            Dictionary<LineLoadRun, LoadProfile> profiles = new Dictionary<LineLoadRun, LoadProfile>();
            Func<LineLoadRun, LoadProfile> profileOf = run =>
            {
                LoadProfile profile;
                if (!profiles.TryGetValue(run, out profile))
                {
                    profile = run.GravityProfile(patterns);
                    profiles[run] = profile;
                }
                return profile;
            };

            List<Assignment> assignments = new List<Assignment>();
            foreach (LineLoad_R line in drawnLines)
            {
                if (line.HasLine)
                {
                    assignments.AddRange(AssignAlongFrames(line, onStory, schedule, storyAbove, pref,
                                                           pinned, pinnedFrames, spokenFor, profileOf));
                }
                else
                {
                    assignments.Add(Assign(line, onStory, schedule, storyAbove, pref, pinned, spokenFor));
                }
            }

            foreach (IGrouping<LineLoadRun, Assignment> onRun in assignments
                         .Where(assignment => assignment.Run != null)
                         .GroupBy(assignment => assignment.Run))
            {
                CompareOneRun(onRun.Key, onRun.ToList(), pref, patterns);
            }

            // The parts of lines whose ETABS load another line took, placed on the next run
            // along them - and those runs judged again, now that there is one more line on
            // each. Once: a part taken a second time is reported as taken.
            if (pref.LineLoadCheckInSteps)
            {
                List<Assignment> handedOn = HandOnWhatWasTaken(assignments, onStory, spokenFor, pref);
                if (handedOn.Count > 0)
                {
                    assignments.AddRange(handedOn);

                    foreach (LineLoadRun run in handedOn.Select(one => one.Run).Distinct().ToList())
                    {
                        List<Assignment> onRun = assignments.Where(one => one.Run == run).ToList();
                        foreach (Assignment assignment in onRun.Where(one => one.HasExpected))
                        {
                            assignment.Result.State = LineLoadState.NotChecked;
                        }
                        CompareOneRun(run, onRun, pref, patterns);
                    }
                }
            }

            Publish(assignments, pref);

            ReportUnclaimed(onStory, assignments, pref, patterns);
            CollectEtabsStretches(onStory, assignments, pref, patterns);
        }

        /// <summary>
        /// One drawn line placed against the run it belongs to, with what the schedule says it
        /// should be worth already looked up.
        /// </summary>
        private sealed class Assignment
        {
            public LineLoadResult Result;
            public LineLoadRun Run;

            /// <summary>Where the line's pieces sit on the run, as chainages.</summary>
            public readonly List<double[]> Spans = new List<double[]>();

            /// <summary>The piece of the line each span came from, in the same order.</summary>
            public readonly List<LineLoad_R.Piece> SpanPieces = new List<LineLoad_R.Piece>();

            /// <summary>How much of the line comes before each span's piece, in mm.</summary>
            public readonly List<double> SpanBefore = new List<double>();

            /// <summary>True when the line could not be laid along the run to be compared.</summary>
            public bool Bundled;

            /// <summary>What the schedule asks for, in kN/m, when it could be worked out.</summary>
            public double Expected;

            public bool HasExpected;

            /// <summary>How long the line - or this part of it - is, in mm, measured along it.</summary>
            public double Length;

            /// <summary>
            /// Set on one part of a curved line placed a piece at a time: the line's own result,
            /// which the parts are put back together into once every run has judged its own.
            /// Null on a line placed whole, whose result is <see cref="Result"/>.
            /// </summary>
            public LineLoadResult Owner;

            /// <summary>
            /// True on the part of a line handed on to another run because another line took
            /// the ETABS load under it on the run it was first placed on.
            /// </summary>
            public bool IsHandedOn;

            /// <summary>True on a part of a line laid on a frame somebody paired it with by hand.</summary>
            public bool ByHand;
        }

        /// <summary>
        /// One line's claim on a stretch of a run: what it asks for there and how far it sits
        /// from the frames. The distance is what decides, where several lines lie over the same
        /// stretch and ETABS carries what only one of them asks for, which of them it is.
        /// </summary>
        private sealed class Contribution
        {
            public Assignment Assignment;
            public double From;
            public double To;
            public double Offset;
            public double Value;
            public long RevitId;

            /// <summary>The whole line this is a part of, which is what can take a load.</summary>
            public LineLoadResult Line;
        }

        /// <summary>
        /// Finds the run one drawn line belongs to and what it should be worth, settling
        /// everything about the line that can be settled without looking at its neighbours.
        ///
        /// A line is put on the run its pieces run along for longest, with the same preference
        /// the wall check pairs by: a run inside the location buffer beats one outside it
        /// however much of the line lies along it, and outside the buffer the nearest wins -
        /// so a load line in the wrong place is reported as being in the wrong place rather
        /// than as having no frame at all.
        /// </summary>
        private Assignment Assign(LineLoad_R line, List<LineLoadRun> onStory,
                                  LineLoadSchedule schedule, double storyAboveHeight,
                                  PrefrencesData pref,
                                  Dictionary<long, LineLoadRun> pinned,
                                  HashSet<LineLoadRun> spokenFor)
        {
            Assignment assignment = new Assignment { Result = new LineLoadResult(line), Length = line.Length };

            LookUpValue(assignment, line, schedule, storyAboveHeight, pref);

            if (!line.HasLine)
            {
                assignment.Result.State = LineLoadState.NoEtabsFrame;
                assignment.Result.Note = "the detail line has no length in plan";
                return assignment;
            }

            LineLoadRun byHand;
            if (pinned.TryGetValue(line.RevitID, out byHand))
            {
                Place(assignment, line, byHand, pref, 0, byHand: true);
                assignment.Result.Note = Join(assignment.Result.Note, "paired by hand");
                return assignment;
            }

            LineLoadRun best = null;
            double bestOverlap = 0;
            bool bestInsideBuffer = false;
            double bestOffset = double.MaxValue;

            // The nearest run the search turned away, kept only so a line that paired with
            // nothing can say what it nearly paired with. A line only Revit has beside one only ETABS
            // has, with nothing to explain the gap is the one picture this check can produce that
            // cannot be read at all - the two look adjacent on screen and the reason they are
            // not together is a number nobody can see.
            double nearestOffset = double.MaxValue;
            double nearestOverlap = 0;

            foreach (LineLoadRun run in onStory)
            {
                // Said by hand to go with no drawn line at all. Left out before any distance is
                // measured, because measuring again would only reproduce the answer somebody
                // was correcting - and the run still comes back as a load nothing asked for,
                // which is what saying so meant.
                if (spokenFor.Contains(run))
                {
                    continue;
                }

                double overlap = 0;
                double offset = double.MaxValue;

                foreach (LineLoad_R.Piece piece in line.Pieces)
                {
                    // Measured whatever it comes to, so a run past the search radius can still
                    // be named as the nearest thing there was. The radius is applied below.
                    offset = Math.Min(offset, WallGeometry.Offset(piece, run));
                    overlap += WallGeometry.OverlapLength(run, piece);
                }

                if (offset < nearestOffset)
                {
                    nearestOffset = offset;
                    nearestOverlap = overlap;
                }

                // Alongside for less than the shortest stretch worth reporting is not alongside.
                // A return drawn square across the frame lies along it for nothing at all, and
                // one a hair off square - which is what a drawn line and an ETABS frame are once
                // aligned - lies along it for a fraction of a millimetre. Placed whole for that,
                // it put every line on the frame on the total with it.
                if (offset > radius || overlap <= ShortestAlongside(pref))
                {
                    continue;
                }

                bool insideBuffer = offset <= reach;

                if (best != null && !IsBetter(insideBuffer, offset, overlap,
                                              bestInsideBuffer, bestOffset, bestOverlap))
                {
                    continue;
                }

                best = run;
                bestOverlap = overlap;
                bestOffset = offset;
                bestInsideBuffer = insideBuffer;
            }

            if (best == null)
            {
                assignment.Result.State = LineLoadState.NoEtabsFrame;
                assignment.Result.Note = Join(assignment.Result.Note,
                    WhyNothingWasFound(onStory, nearestOffset, nearestOverlap, pref));
                return assignment;
            }

            Place(assignment, line, best, pref, bestOffset, byHand: false);

            return assignment;
        }

        /// <summary>
        /// Lays one drawn line along the run it belongs to: whether it can be read stretch by
        /// stretch at all, and which stretches of the run its pieces cover.
        ///
        /// Shared by the two ways a line arrives at a run - measured, or pinned by hand -
        /// because everything after the choice is the same. A line somebody pinned is still a
        /// line that might be bent, and it still has to be laid along the run to be read.
        /// </summary>
        private static void Place(Assignment assignment, LineLoad_R line, LineLoadRun run,
                                  PrefrencesData pref, double offset, bool byHand)
        {
            assignment.Run = run;
            assignment.Result.Run = run;

            // A line that does not run along the frame cannot be compared stretch by stretch:
            // there is no stretch of the frame that is the same stretch of the line. Its load
            // still has to go somewhere, so it goes onto the total.
            //
            // How far off the run a line sits is not part of that. It decided which run the
            // line went to; once there, a straight line parallel to the frame has a stretch of
            // it for every stretch of itself, however far across it was drawn - a facade line
            // on the outer face of the slab edge beside the wall line on the beam. Put on the
            // total for being outside the buffer, it took the whole run with it, and every
            // line on that frame was then asked for its own value against a frame carrying
            // all of them added together. What is asked is whether the line is bent or runs
            // across the run, because neither of those can be walked along a straight frame.
            assignment.Bundled = line.IsBent(pref.LineLoadAngleTolerance)
                                 || WallGeometry.AngleBetween(line.Pieces[0], run) > pref.LineLoadAngleTolerance;

            assignment.Result.Bundled = assignment.Bundled;

            double before = 0;
            foreach (LineLoad_R.Piece piece in line.Pieces)
            {
                double from, to;
                if (WallGeometry.OverlapSpan(run, piece, out from, out to))
                {
                    assignment.Spans.Add(new[] { from, to });
                    assignment.SpanPieces.Add(piece);
                    assignment.SpanBefore.Add(before);
                }
                before += piece.Length;
            }
        }

        /// <summary>
        /// Which run each hand-paired line belongs to, looked up from the frames.
        ///
        /// The store names an ETABS frame and the Revit line it goes with, so this turns it
        /// round: for every frame on every run, is there a line pinned to it? What comes back
        /// is keyed by the line, which is the way the assignment reads it.
        /// </summary>
        private Dictionary<long, LineLoadRun> PinnedRuns(List<LineLoadRun> onStory)
        {
            Dictionary<long, LineLoadRun> pinned = new Dictionary<long, LineLoadRun>();

            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return pinned;
            }

            foreach (LineLoadRun run in onStory)
            {
                foreach (string frame in run.FrameNames)
                {
                    long? revitId = Overrides.Find(ProjectKey,
                        PairingOverrides.KeyOf(ElementKindKey.LineLoad, frame));

                    if (revitId != null && revitId.Value != PairingOverrides.Unpaired
                        && !pinned.ContainsKey(revitId.Value))
                    {
                        pinned[revitId.Value] = run;
                    }
                }
            }

            return pinned;
        }

        /// <summary>
        /// Every run each hand-paired line was pinned to, rather than the first of them. A line
        /// running over three frames can be paired with any of them, and each of those pairings
        /// still holds.
        /// </summary>
        private Dictionary<long, List<LineLoadRun>> PinnedFrames(List<LineLoadRun> onStory)
        {
            Dictionary<long, List<LineLoadRun>> pinned = new Dictionary<long, List<LineLoadRun>>();

            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return pinned;
            }

            foreach (LineLoadRun run in onStory)
            {
                foreach (string frame in run.FrameNames)
                {
                    long? revitId = Overrides.Find(ProjectKey,
                        PairingOverrides.KeyOf(ElementKindKey.LineLoad, frame));

                    if (revitId == null || revitId.Value == PairingOverrides.Unpaired)
                    {
                        continue;
                    }

                    List<LineLoadRun> onLine;
                    if (!pinned.TryGetValue(revitId.Value, out onLine))
                    {
                        onLine = new List<LineLoadRun>();
                        pinned[revitId.Value] = onLine;
                    }

                    if (!onLine.Contains(run))
                    {
                        onLine.Add(run);
                    }
                }
            }

            return pinned;
        }

        /// <summary>
        /// The runs somebody said go with no drawn line at all.
        ///
        /// Kept as runs rather than as lines, and that is the whole of what the store can say:
        /// a pairing is written from the ETABS side, so "unpaired" is a thing said about a
        /// frame, not about a drawing. It means the load on this frame is deliberately not the
        /// one drawn near it - and the run then comes back as a load nothing asked for, which
        /// is what saying so was for.
        /// </summary>
        private HashSet<LineLoadRun> RunsPinnedToNothing(List<LineLoadRun> onStory)
        {
            HashSet<LineLoadRun> spokenFor = new HashSet<LineLoadRun>();

            if (Overrides == null || string.IsNullOrEmpty(ProjectKey))
            {
                return spokenFor;
            }

            foreach (LineLoadRun run in onStory)
            {
                foreach (string frame in run.FrameNames)
                {
                    long? revitId = Overrides.Find(ProjectKey,
                        PairingOverrides.KeyOf(ElementKindKey.LineLoad, frame));

                    if (revitId != null && revitId.Value == PairingOverrides.Unpaired)
                    {
                        spokenFor.Add(run);
                        break;
                    }
                }
            }

            return spokenFor;
        }

        /// <summary>
        /// Why a drawn line ended up with no run, in the terms somebody looking at the plan can
        /// act on: how far the nearest one was against how far the check looks.
        ///
        /// Worth saying rather than leaving blank. Two lines side by side on screen, one in
        /// each model's colour, with no word about the gap between them, is the one output of this
        /// check that cannot be argued with or acted on - and the gap is usually either a
        /// search radius set too tight or a frame drawn somewhere other than the line.
        /// </summary>
        private string WhyNothingWasFound(List<LineLoadRun> onStory, double nearestOffset,
                                                 double nearestOverlap, PrefrencesData pref)
        {
            if (onStory.Count == 0)
            {
                return "this level has no ETABS frames on it at all - check that the loading plan "
                       + "is paired with the story the model calls this floor";
            }

            if (nearestOffset >= double.MaxValue)
            {
                return "no ETABS frame on this level runs anywhere near it";
            }

            string howFar = Math.Round(nearestOffset / 1000d, 2).ToString("0.##");
            string looks = Math.Round(radius / 1000d, 2).ToString("0.##");

            if (nearestOffset > radius)
            {
                return $"the nearest ETABS frame is {howFar} m off this line and the check looks "
                       + $"{looks} m - widen \"How far to look for a frame under a line\" under "
                       + "Settings, Line loads, if that frame is the one it belongs to";
            }

            return $"the nearest ETABS frame is {howFar} m off this line and does not run "
                   + "alongside it - they cross rather than lie together";
        }

        /// <summary>
        /// How far, in mm, a line placed whole has to lie along a frame to be placed on it: the
        /// shortest difference the check reports, and never less than a millimetre.
        /// </summary>
        private static double ShortestAlongside(PrefrencesData pref)
        {
            return Math.Max(1, pref.LineLoadMinimumIssueLength);
        }

        private static bool IsBetter(bool insideBuffer, double offset, double overlap,
                                     bool bestInsideBuffer, double bestOffset, double bestOverlap)
        {
            if (insideBuffer != bestInsideBuffer)
            {
                return insideBuffer;
            }

            return insideBuffer ? overlap > bestOverlap : offset < bestOffset;
        }

        /// <summary>
        /// What the schedule says this line is worth, and the reasons it might say nothing:
        /// a style with no number on it, a number the sheet does not have, or a load written
        /// per square metre of elevation on a story with nothing above it to give a height.
        /// </summary>
        private void LookUpValue(Assignment assignment, LineLoad_R line, LineLoadSchedule schedule,
                                 double storyAboveHeight, PrefrencesData pref)
        {
            LineLoadResult result = assignment.Result;

            // A line that is worth what it weighs needs no schedule, no number and no height:
            // the value came with it.
            if (line.HasOwnValue)
            {
                assignment.HasExpected = true;
                assignment.Expected = line.OwnPerMetre;

                result.Expected = line.OwnPerMetre;
                result.How = line.OwnHow;
                return;
            }

            if (!line.HasNumber)
            {
                result.State = LineLoadState.NoNumber;
                return;
            }

            LineLoadEntry entry = schedule == null ? null : schedule.Find(line.Number);
            if (entry == null)
            {
                result.State = LineLoadState.NotInSchedule;
                return;
            }

            result.Entry = entry;

            // A zone drawn by hand around most of the line stands in for the level's height.
            // Only for a load that depends on the height at all: one off the schedule per metre
            // run is the same whatever the storey is.
            HeightZone zone = entry.HeightIndependent || HeightZones == null
                ? null
                : HeightZones.ZoneFor(ProjectKey, line.Story, line);
            result.HeightZone = zone == null ? "" : zone.Description;
            result.HeightZoneId = zone == null ? "" : zone.Id;

            ExpectedLineLoad expected = LineLoadHeight.Expected(entry,
                                                                zone == null ? storyAboveHeight : zone.Height,
                                                                pref.LineLoadHeightReduction,
                                                                pref.FullHeightLineLoadNames,
                                                                pref.LineLoadRoundUpTo,
                                                                zone == null ? null : zone.Label);

            if (!expected.Known)
            {
                result.State = LineLoadState.NoExpectedValue;
                result.Note = expected.Why;
                return;
            }

            assignment.HasExpected = true;
            assignment.Expected = expected.PerMetre;

            result.Expected = expected.PerMetre;
            result.How = expected.How;
        }

        #endregion

        #region one run

        /// <summary>
        /// Compares every line on one run against the frames under it.
        ///
        /// Together rather than one at a time, because what the plan asks for at a chainage is
        /// the sum of every line covering it - two loads drawn along the same beam is two loads
        /// on that beam - and no single line knows what its neighbours are asking for.
        /// </summary>
        private void CompareOneRun(LineLoadRun run, List<Assignment> assignments,
                                   PrefrencesData pref, LineLoadPatterns patterns)
        {
            LoadProfile planned = new LoadProfile();

            foreach (Assignment assignment in assignments.Where(one => one.HasExpected && !one.Bundled))
            {
                foreach (double[] span in assignment.Spans)
                {
                    planned.Add(span[0], span[1], assignment.Expected, assignment.Expected);
                }
            }

            LoadProfile actual = run.GravityProfile(patterns);

            // Every line's claim on the run, stretch by stretch, for deciding which of several
            // lines over the same stretch the ETABS load belongs to.
            List<Contribution> contributions = new List<Contribution>();
            foreach (Assignment assignment in assignments.Where(one => one.HasExpected && !one.Bundled))
            {
                for (int i = 0; i < assignment.Spans.Count; i++)
                {
                    contributions.Add(new Contribution
                    {
                        Assignment = assignment,
                        From = assignment.Spans[i][0],
                        To = assignment.Spans[i][1],
                        Offset = i < assignment.SpanPieces.Count
                            ? WallGeometry.Offset(assignment.SpanPieces[i], run)
                            : double.MaxValue,
                        Value = assignment.Expected,
                        RevitId = (assignment.Owner ?? assignment.Result).Line.RevitID,
                        Line = assignment.Owner ?? assignment.Result
                    });
                }
            }

            // Where a bundled line lies the run cannot be read stretch by stretch: part of what
            // the plan asks for there is not laid along it, so the profile is short by that much
            // and every piece of it would read as ETABS carrying too much. So that line, and
            // every line over the same stretch as it, is judged whole - on the values ETABS
            // carries under it, as they are.
            //
            // Only those. A line further along the frame has nothing to do with it, and put on
            // the total with it was asked for its own value against the most the frame carried
            // anywhere - a glazing line over a 5.6 stretch failing against the 31.6 of a recess
            // two metres away.
            List<Assignment> bundled = assignments.Where(one => one.Bundled).ToList();
            Func<Assignment, bool> byTotalAlone = one => one.Bundled
                || bundled.Any(other => SharedLength(other, one) > pref.LineLoadMinimumIssueLength);

            foreach (Assignment assignment in assignments)
            {
                // Already settled on its own terms - no number, not in the schedule, no
                // height - and none of those is made truer or falser by the frames under it.
                if (assignment.Result.State != LineLoadState.NotChecked || !assignment.HasExpected)
                {
                    continue;
                }

                if (byTotalAlone(assignment))
                {
                    JudgeWhole(assignment, assignments, run, actual, pref);
                    continue;
                }

                // Never reconciled on the total force. Each ETABS load is read as it is, where it
                // is: a load spread or averaged differently from the plan is a difference like
                // any other, however well the totals agree.
                if (pref.LineLoadCheckInSteps)
                {
                    JudgeInSteps(assignment, run, planned, actual, contributions, pref);
                }
                else
                {
                    Judge(assignment, run, planned, actual, pref);
                }
            }

            // Nothing is published here. A run can be judged twice - once more when a line is
            // handed on to it - and a line can be judged on several runs; Publish puts each
            // line out once, after all of that.
        }

        /// <summary>
        /// A line that could not be laid along the run, judged whole.
        ///
        /// Never on the total force. ETABS has to carry the value the plan asks for, one value
        /// under the line, and every value it carries there is read as it is - each end of a
        /// tapered load, not its middle - and has to agree.
        ///
        /// That value is the line's own, or every line drawn over the same stretch of the run
        /// added together - the same sum a run read stretch by stretch compares against. One
        /// line lying outside the buffer is what puts a run on its total, and it is typically
        /// the second of two loads on one edge: a facade on the outer face beside the wall on
        /// the beam. Asked for its own value alone, each of the two failed against a frame
        /// carrying exactly both.
        /// </summary>
        private void JudgeWhole(Assignment assignment, List<Assignment> onRun, LineLoadRun run,
                                LoadProfile actual, PrefrencesData pref)
        {
            LineLoadResult result = assignment.Result;

            result.Note = Join(result.Note,
                "this line does not run along the frame, so it was compared as a whole rather "
                + "than stretch by stretch");

            // What ETABS carries under this line, piece by piece - one value if the frame carries
            // one load there, and a spread if it does not. Under the line rather than along the
            // whole frame: a frame loaded differently further along says nothing about this
            // line. A line with no stretch of the frame under it - paired by hand with one it
            // runs square across - is read against the whole frame, as it was paired with.
            List<double[]> under = assignment.Spans
                .Select(span => new[] { Math.Max(0, Math.Min(span[0], span[1])),
                                        Math.Min(run.Length, Math.Max(span[0], span[1])) })
                .Where(span => span[1] > span[0])
                .ToList();
            if (under.Count == 0)
            {
                under.Add(new[] { 0d, run.Length });
            }

            List<double> values = under
                .SelectMany(span => LoadProfile.Pieces(span[0], span[1], 0, actual))
                .SelectMany(piece => actual.AtEnds(piece[0], piece[1]))
                .Where(value => value > 0)
                .ToList();

            if (values.Count == 0)
            {
                result.Actual = 0;
                result.State = LineLoadState.NoEtabsLoad;
                return;
            }

            double lowest = values.Min();
            double highest = values.Max();
            result.Actual = highest;

            result.Carried.Clear();
            foreach (double value in values)
            {
                result.AddCarried(run.Identifier, value);
            }

            if (!band.Agrees(lowest, highest))
            {
                // The load that is wrong, not the highest: on a line asking for the highest, the
                // highest read as the same number on both sides of a failure.
                result.Actual = values.OrderByDescending(value => Math.Abs(value - assignment.Expected)).First();
                result.State = LineLoadState.ValueMismatch;
                result.Note = Join(result.Note,
                    $"ETABS carries different loads under it, {LoadSet.Text(lowest)} to "
                    + $"{LoadSet.Text(highest)} kN/m, and one line asks for one value");
                return;
            }

            // The highest is what the symmetric buffer was always asked about. A band that lets
            // a load through heavier than light has the lowest to answer for as well: under it
            // is where the load is short.
            bool alone = band.Agrees(assignment.Expected, highest)
                         && (band.IsSymmetric || band.Agrees(assignment.Expected, lowest));

            // Every line over the same stretch of this run, this one included, each counted
            // once however many parts of it lie there.
            List<Assignment> alongside = onRun
                .Where(other => other.HasExpected && SharedLength(other, assignment) > pref.LineLoadMinimumIssueLength)
                .GroupBy(other => other.Owner ?? other.Result)
                .Select(line => line.First())
                .ToList();
            double together = alongside.Sum(other => other.Expected);
            bool added = alongside.Count > 1 && band.Agrees(together, highest)
                         && (band.IsSymmetric || band.Agrees(together, lowest));

            // On the value alone. The totals are the frame's, over the whole of it, against every
            // line on it: a frame loaded past the end of this line, or another line on it that is
            // wrong, made them disagree and failed a line whose own value was right - read on the
            // card as the plan and ETABS both asking 1.5. Whether the totals agree is the
            // averaging question, and averaging is off.
            result.State = alone || added
                ? LineLoadState.Matched
                : LineLoadState.ValueMismatch;

            if (result.State == LineLoadState.Matched && !alone)
            {
                result.Note = Join(result.Note,
                    $"the frame carries this line and {alongside.Count - 1} other(s) drawn over the same "
                    + $"stretch added together, {LoadSet.Text(together)} kN/m");
            }
        }

        /// <summary>How far two lines placed on the same run lie over the same stretch of it, in mm.</summary>
        private static double SharedLength(Assignment first, Assignment second)
        {
            double shared = 0;
            foreach (double[] one in first.Spans)
            {
                foreach (double[] other in second.Spans)
                {
                    double from = Math.Max(Math.Min(one[0], one[1]), Math.Min(other[0], other[1]));
                    double to = Math.Min(Math.Max(one[0], one[1]), Math.Max(other[0], other[1]));
                    shared += Math.Max(0, to - from);
                }
            }
            return shared;
        }

        /// <summary>
        /// One line against the frames under it, stretch by stretch.
        ///
        /// Walked piece by piece between every break on either side, so a line running over
        /// two frames loaded differently is read as the two of them rather than as one average
        /// that matches neither.
        /// </summary>
        private void Judge(Assignment assignment, LineLoadRun run,
                                  LoadProfile planned, LoadProfile actual, PrefrencesData pref)
        {
            LineLoadResult result = assignment.Result;

            double covered = 0;
            double loaded = 0;
            bool mismatched = false;
            double mismatchLength = 0;
            double worstGap = -1;
            List<double> read = new List<double>();

            result.Carried.Clear();

            foreach (double[] span in assignment.Spans)
            {
                foreach (double[] piece in LoadProfile.Pieces(span[0], span[1],
                                                              pref.LineLoadMinimumIssueLength,
                                                              planned, actual))
                {
                    double at = (piece[0] + piece[1]) / 2d;
                    double width = piece[1] - piece[0];

                    double wantedHere = planned.At(at);
                    double thereHere = actual.At(at);

                    loaded += width;

                    if (thereHere > 0)
                    {
                        covered += width;
                        read.Add(thereHere);
                        result.AddCarried(run.Identifier, thereHere);
                    }

                    double gap = Math.Abs(wantedHere - thereHere);
                    if (!band.Agrees(wantedHere, thereHere))
                    {
                        mismatched = true;
                        mismatchLength += width;

                        // The stretch that failed it, kept so the card can say where. The
                        // average shown beside it can sit right on the schedule value while
                        // one stretch of the line is well off it.
                        if (gap > worstGap)
                        {
                            worstGap = gap;
                            result.MismatchWanted = wantedHere;
                            result.MismatchCarried = thereHere;
                        }
                    }
                }
            }

            result.CoveredLength = covered;
            result.Actual = AsRead(read, assignment.Expected);
            result.MismatchLength = mismatchLength;

            if (covered <= 0)
            {
                // Nothing under any part of it. Whether the run carries a load somewhere else
                // does not change that for this line, but it changes what to go and look at:
                // a frame with nothing on it at all is a load somebody forgot, and a frame
                // loaded either side of this line is a load somebody put in the wrong place.
                result.State = LineLoadState.NoEtabsLoad;

                if (run.HasGravityLoad)
                {
                    result.Note = Join(result.Note,
                        "the run does carry a load elsewhere along it, so this looks like a load "
                        + "put on the wrong stretch rather than one left off");
                }

                return;
            }

            if (loaded - covered > pref.LineLoadMinimumIssueLength)
            {
                result.State = LineLoadState.PartlyCovered;
                return;
            }

            result.State = mismatched ? LineLoadState.ValueMismatch : LineLoadState.Matched;
        }

        /// <summary>
        /// One line against the frames under it, walked in steps from its start to its end.
        ///
        /// The steps fall every <see cref="PrefrencesData.LineLoadStepLength"/> from the start
        /// of the line, at its end, and - whatever the spacing says - wherever an ETABS frame
        /// starts or ends and wherever a load on either side starts or stops. Each step is read
        /// at its middle, which lies inside one frame and one load on each side, so what it
        /// reads is what ETABS carries at that place rather than an average over the line.
        ///
        /// Steps that read alike are merged into stretches, and a failing stretch too short to
        /// be worth reporting is folded into the one beside it, the same allowance the whole
        /// line check makes for two drawn lines whose ends do not quite meet.
        /// </summary>
        private void JudgeInSteps(Assignment assignment, LineLoadRun run,
                                         LoadProfile planned, LoadProfile actual,
                                         List<Contribution> contributions, PrefrencesData pref)
        {
            LineLoadResult result = assignment.Result;
            double spacing = pref.LineLoadStepLength > 0 ? pref.LineLoadStepLength : 250;

            // Frame ends and load ends, as chainages along the run: the places a step is forced.
            List<double> forced = new List<double>();
            foreach (LineLoadFrame_E frame in run.Frames)
            {
                forced.Add(run.ChainageOf(frame.PlanStart));
                forced.Add(run.ChainageOf(frame.PlanEnd));
            }
            forced.AddRange(actual.Breaks(0));
            forced.AddRange(planned.Breaks(0));

            List<LineLoadStretch> steps = new List<LineLoadStretch>();

            for (int i = 0; i < assignment.Spans.Count; i++)
            {
                double from = assignment.Spans[i][0];
                double to = assignment.Spans[i][1];
                LineLoad_R.Piece piece = assignment.SpanPieces[i];
                double before = assignment.SpanBefore[i];

                double atStart = run.ChainageOf(piece.PlanStart);
                double atEnd = run.ChainageOf(piece.PlanEnd);
                if (Math.Abs(atEnd - atStart) < 1e-6)
                {
                    continue;
                }

                // Counted from the end of the span nearest where the line was drawn from, so
                // the steps fall where somebody walking the drawn line would put them.
                bool forwards = atEnd >= atStart;
                double origin = forwards ? from : to;

                List<double> stations = new List<double> { from, to };
                for (double along = spacing; along < to - from; along += spacing)
                {
                    stations.Add(forwards ? origin + along : origin - along);
                }
                stations.AddRange(forced.Where(at => at > from && at < to));
                stations.Sort();

                double previous = double.NaN;
                foreach (double station in stations)
                {
                    if (!double.IsNaN(previous) && station - previous > 1)
                    {
                        double middle = (previous + station) / 2d;
                        double wanted = planned.At(middle);
                        double carried = actual.At(middle);

                        LineLoadState state = carried <= 0
                            ? LineLoadState.NoEtabsLoad
                            : !band.Agrees(wanted, carried)
                                ? LineLoadState.ValueMismatch
                                : LineLoadState.Matched;
                        string takenBy = "";

                        // Several lines over this step, and ETABS carries what one of them asks
                        // for on its own rather than what they add up to: that one takes it, and
                        // the others are left to find their own load on another frame. Where
                        // ETABS carries the sum, they are two loads and both count.
                        if (state == LineLoadState.ValueMismatch)
                        {
                            Contribution taker = TakerAt(contributions, middle, carried, band);
                            if (taker != null)
                            {
                                wanted = assignment.Expected;
                                // A line never takes a load from itself, whichever of its parts
                                // the load was read through.
                                if (taker.Assignment == assignment || taker.Line == (assignment.Owner ?? assignment.Result))
                                {
                                    state = LineLoadState.Matched;
                                }
                                else
                                {
                                    state = LineLoadState.TakenByOther;
                                    takenBy = "line load " + taker.RevitId;
                                }
                            }
                        }

                        steps.Add(new LineLoadStretch
                        {
                            From = before + piece.Length * (previous - atStart) / (atEnd - atStart),
                            To = before + piece.Length * (station - atStart) / (atEnd - atStart),
                            Start = PointOn(piece, (previous - atStart) / (atEnd - atStart)),
                            End = PointOn(piece, (station - atStart) / (atEnd - atStart)),
                            Wanted = wanted,
                            Carried = carried,
                            State = state,
                            TakenBy = takenBy,
                            Frames = run.Identifier
                        });
                    }

                    if (double.IsNaN(previous) || station - previous > 1)
                    {
                        previous = station;
                    }
                }
            }

            // Along the drawn line, whichever way round the frame was drawn.
            foreach (LineLoadStretch step in steps.Where(one => one.From > one.To))
            {
                double swap = step.From; step.From = step.To; step.To = swap;
                MyPoint swapPoint = step.Start; step.Start = step.End; step.End = swapPoint;
            }
            steps.Sort((a, b) => a.From.CompareTo(b.From));

            List<LineLoadStretch> stretches = Merge(steps);
            FoldShortFailures(stretches, pref.LineLoadMinimumIssueLength);

            Summarise(result, stretches, run.HasGravityLoad, assignment.Expected);
        }

        /// <summary>
        /// The one figure for what ETABS carries under a line, taken from the values read and
        /// never made up out of them: the value, where there is one, and otherwise the one
        /// furthest from what the line asks for. An average of 1.5 and 3 is 2.25, which no frame
        /// carries and which can sit on the schedule value while both frames are wrong.
        /// </summary>
        private static double AsRead(IEnumerable<double> values, double wanted)
        {
            List<double> read = values.Where(value => value > 0).ToList();
            if (read.Count == 0)
            {
                return 0;
            }

            return read.OrderByDescending(value => Math.Abs(value - wanted)).First();
        }

        /// <summary>
        /// Of the lines lying over one chainage, the one the ETABS load there belongs to: a
        /// line that asks for that load on its own, and of several that do, the one nearest
        /// the frames. Null where fewer than two lines lie there, or where none of them asks
        /// for what ETABS carries.
        ///
        /// Not simply the nearest. A balustrade and a block wall drawn along one slab edge,
        /// with the balustrade's 1.5 on the edge beam and the wall's 26.1 on the beam beside
        /// it: the wall line may well lie nearer the edge beam, and the load there is still
        /// the balustrade's. A tie on distance goes to the lower Revit id, so the answer does
        /// not depend on which order the lines were read in.
        /// </summary>
        private static Contribution TakerAt(List<Contribution> contributions, double chainage,
                                            double carried, LoadBand band)
        {
            List<Contribution> here = contributions
                .Where(one => chainage > one.From && chainage < one.To)
                .ToList();

            if (here.Select(one => one.Line).Distinct().Count() < 2)
            {
                return null;
            }

            return here.Where(one => band.Agrees(one.Value, carried))
                       .OrderBy(one => one.Offset)
                       .ThenBy(one => one.RevitId)
                       .FirstOrDefault();
        }

        /// <summary>
        /// What a line checked in steps comes to as a whole, read off its stretches: the state
        /// the counts and the drawer go by, the value for the whole line, and the stretch
        /// furthest off.
        /// </summary>
        private static void Summarise(LineLoadResult result, List<LineLoadStretch> stretches,
                                      bool runCarriesLoad, double wanted)
        {
            result.Stretches.Clear();
            result.Stretches.AddRange(stretches);

            // A stretch another line took carries a load - just not this line's.
            List<LineLoadStretch> own = stretches.Where(one => one.State != LineLoadState.TakenByOther).ToList();

            double covered = own.Where(one => one.Carried > 0).Sum(one => one.Length);

            result.CoveredLength = covered;

            // What ETABS carries, as read on each stretch - never averaged across them.
            result.Carried.Clear();
            foreach (LineLoadStretch stretch in own)
            {
                result.AddCarried(stretch.Frames, stretch.Carried);
            }

            result.Actual = stretches.Count == 1
                ? stretches[0].Carried
                : AsRead(own.Select(one => one.Carried), wanted);

            List<LineLoadStretch> mismatched =
                stretches.Where(one => one.State == LineLoadState.ValueMismatch).ToList();
            result.MismatchLength = mismatched.Sum(one => one.Length);
            LineLoadStretch worst = mismatched
                .OrderByDescending(one => Math.Abs(one.Wanted - one.Carried))
                .FirstOrDefault();
            if (worst != null)
            {
                result.MismatchWanted = worst.Wanted;
                result.MismatchCarried = worst.Carried;
            }

            if (stretches.Count > 0 && stretches.All(one => one.State == LineLoadState.NoEtabsFrame))
            {
                result.State = LineLoadState.NoEtabsFrame;
                return;
            }

            if (stretches.Count > 0 && stretches.All(one => one.State == LineLoadState.TakenByOther))
            {
                result.State = LineLoadState.TakenByOther;
                return;
            }

            if (covered <= 0)
            {
                result.State = LineLoadState.NoEtabsLoad;

                if (runCarriesLoad)
                {
                    result.Note = Join(result.Note,
                        "the run does carry a load elsewhere along it, so this looks like a load "
                        + "put on the wrong stretch rather than one left off");
                }
                return;
            }

            result.State = stretches.Any(one => one.State == LineLoadState.NoEtabsLoad
                                                || one.State == LineLoadState.NoEtabsFrame
                                                || one.State == LineLoadState.TakenByOther)
                ? LineLoadState.PartlyCovered
                : mismatched.Count > 0
                    ? LineLoadState.ValueMismatch
                    : LineLoadState.Matched;
        }

        #region a line over frames read one by one

        /// <summary>
        /// One frame a straight piece of a drawn line runs along, and where along the piece.
        /// </summary>
        private sealed class Beside
        {
            public LineLoadRun Run;

            /// <summary>Where along the piece the frame starts and stops alongside it, in mm.</summary>
            public double From;
            public double To;

            public double Offset;
            public bool ByHand;

            // Scored afresh for each stretch of the piece being decided.
            public double Matching;
            public double Loaded;
        }

        /// <summary>
        /// A drawn line laid over the ETABS frames under it with the frames read one by one:
        /// every straight piece of it cut where one frame stops and the next starts, and each
        /// part given to the frame it lies along.
        ///
        /// Where two frames lie along the same part - a beam and the one beside it, both inside
        /// the location buffer - the part goes to the one that carries what the line asks for
        /// there, then to one carrying any load at all, then to the nearer. That is the question
        /// a person looking at the two would ask: which of these is the load drawn here on. A
        /// frame paired with the line by hand comes before any of that, however far off it is.
        ///
        /// A part no frame lies along is reported as that. A line no frame lies along anywhere
        /// inside the buffer - off the frames, or running across them - is placed whole and
        /// judged on its total, the way it always was.
        /// </summary>
        private List<Assignment> AssignAlongFrames(LineLoad_R line, List<LineLoadRun> onStory,
                                                  LineLoadSchedule schedule, double storyAboveHeight,
                                                  PrefrencesData pref,
                                                  Dictionary<long, LineLoadRun> pinned,
                                                  Dictionary<long, List<LineLoadRun>> pinnedFrames,
                                                  HashSet<LineLoadRun> spokenFor,
                                                  Func<LineLoadRun, LoadProfile> profileOf)
        {
            LineLoadResult owner = new LineLoadResult(line);
            Assignment valued = new Assignment { Result = owner, Length = line.Length };
            LookUpValue(valued, line, schedule, storyAboveHeight, pref);

            List<LineLoadRun> byHand;
            if (!pinnedFrames.TryGetValue(line.RevitID, out byHand))
            {
                byHand = new List<LineLoadRun>();
            }

            List<Assignment> portions = LayAlongFrames(line, owner, valued, onStory, spokenFor, byHand,
                                                       reach, pref, profileOf);

            // Nothing within the buffer, so looked for again as far as the search radius
            // before the line is given up as one that cannot be walked. The buffer is where a
            // line is expected to be drawn, not a limit on reading it: ETABS frames are straight
            // and only subdivided, so every straight piece of a line - bent or not, drawn on the
            // outer face or the inner - lies along one of them or along none, and each piece
            // that lies along one is read in steps like any other. Only a line running across
            // the frames is left to be judged on its total.
            if (!portions.Any(one => one.Run != null) && radius > reach)
            {
                portions = LayAlongFrames(line, owner, valued, onStory, spokenFor, byHand,
                                          radius, pref, profileOf);
            }

            // Paired by hand with a frame it cannot be laid along - it runs across it - so the
            // pairing is honoured the only way it can be: whole, on the total.
            if (byHand.Count > 0 && !portions.Any(one => one.ByHand))
            {
                return new List<Assignment> { Assign(line, onStory, schedule, storyAboveHeight, pref, pinned, spokenFor) };
            }

            // No frame along any part of it inside the buffer: a line in the wrong place, or the
            // stepped run modelled as one straight frame down the middle of it. Either way it is
            // not following any frame, and is placed whole and judged on its total.
            if (!portions.Any(one => one.Run != null))
            {
                return new List<Assignment> { Assign(line, onStory, schedule, storyAboveHeight, pref, pinned, spokenFor) };
            }

            foreach (Assignment portion in portions)
            {
            }

            return portions;
        }

        /// <summary>
        /// The cutting itself, for one reach: every frame within it of each piece, the piece cut
        /// at every place one of them starts or stops, and each cut given to the best of the
        /// frames covering it - or to none.
        /// </summary>
        private List<Assignment> LayAlongFrames(LineLoad_R line, LineLoadResult owner, Assignment valued,
                                                       List<LineLoadRun> onStory, HashSet<LineLoadRun> spokenFor,
                                                       List<LineLoadRun> byHand, double reach,
                                                       PrefrencesData pref,
                                                       Func<LineLoadRun, LoadProfile> profileOf)
        {
            Dictionary<LineLoadRun, Assignment> byRun = new Dictionary<LineLoadRun, Assignment>();
            List<Assignment> portions = new List<Assignment>();
            Assignment nowhere = null;
            double before = 0;

            foreach (LineLoad_R.Piece piece in line.Pieces)
            {
                double length = piece.Length;
                if (length <= 0)
                {
                    continue;
                }

                List<Beside> candidates = new List<Beside>();
                foreach (LineLoadRun run in onStory)
                {
                    bool pinnedHere = byHand.Contains(run);
                    if ((!pinnedHere && spokenFor.Contains(run))
                        || WallGeometry.AngleBetween(piece, run) > pref.LineLoadAngleTolerance)
                    {
                        continue;
                    }

                    double from, to;
                    if (!WallGeometry.OverlapSpan(piece, run, out from, out to) || to - from <= 1)
                    {
                        continue;
                    }

                    // Measured on the part alongside, so a long piece a degree or two off the
                    // frames is judged frame by frame on where it actually is over each.
                    double offset = WallGeometry.Offset(Cut(piece, from / length, to / length), run);
                    if (!pinnedHere && offset > reach)
                    {
                        continue;
                    }

                    candidates.Add(new Beside { Run = run, From = from, To = to, Offset = offset, ByHand = pinnedHere });
                }

                List<double> cuts = new List<double> { 0, length };
                foreach (Beside candidate in candidates)
                {
                    cuts.Add(candidate.From);
                    cuts.Add(candidate.To);
                }
                cuts = cuts.Distinct().OrderBy(at => at).ToList();

                // Each cut to its frame, with neighbours that went to the same frame joined up.
                List<KeyValuePair<double[], Beside>> chosen = new List<KeyValuePair<double[], Beside>>();
                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    double a = cuts[i];
                    double b = cuts[i + 1];
                    if (b - a <= 1e-6)
                    {
                        continue;
                    }

                    double middle = (a + b) / 2d;
                    Beside best = Best(candidates.Where(one => one.From <= middle && one.To >= middle).ToList(),
                                       piece, a, b, valued, pref, profileOf);

                    if (chosen.Count > 0)
                    {
                        KeyValuePair<double[], Beside> last = chosen[chosen.Count - 1];
                        if (last.Value == best && Math.Abs(last.Key[1] - a) < 1e-6)
                        {
                            last.Key[1] = b;
                            continue;
                        }
                    }

                    chosen.Add(new KeyValuePair<double[], Beside>(new[] { a, b }, best));
                }

                foreach (KeyValuePair<double[], Beside> one in chosen)
                {
                    double a = one.Key[0];
                    double b = one.Key[1];
                    LineLoad_R.Piece part = Cut(piece, a / length, b / length);

                    if (one.Value == null)
                    {
                        if (nowhere == null)
                        {
                            nowhere = Portion(owner, valued, null);
                        }
                        nowhere.SpanPieces.Add(part);
                        nowhere.SpanBefore.Add(before + a);
                        nowhere.Length += b - a;
                        continue;
                    }

                    LineLoadRun run = one.Value.Run;
                    Assignment portion;
                    if (!byRun.TryGetValue(run, out portion))
                    {
                        portion = Portion(owner, valued, run);
                        byRun[run] = portion;
                        portions.Add(portion);
                    }

                    portion.ByHand |= one.Value.ByHand;

                    double from, to;
                    if (WallGeometry.OverlapSpan(run, part, out from, out to))
                    {
                        portion.Spans.Add(new[] { from, to });
                        portion.SpanPieces.Add(part);
                        portion.SpanBefore.Add(before + a);
                    }
                    portion.Length += b - a;
                }

                before += length;
            }

            if (nowhere != null)
            {
                portions.Add(nowhere);
            }

            return portions;
        }

        /// <summary>
        /// Of the frames lying along one cut of a piece, the one it goes to: paired by hand
        /// first, then the one carrying what the line asks for over the most of it, then the one
        /// carrying anything over the most of it, then the nearest. Null where there are none.
        /// </summary>
        private Beside Best(List<Beside> covering, LineLoad_R.Piece piece, double a, double b,
                                   Assignment valued, PrefrencesData pref,
                                   Func<LineLoadRun, LoadProfile> profileOf)
        {
            if (covering.Count <= 1)
            {
                return covering.FirstOrDefault();
            }

            double length = piece.Length;
            MyPoint start = PointOn(piece, a / length);
            MyPoint end = PointOn(piece, b / length);

            foreach (Beside one in covering)
            {
                double at0 = one.Run.ChainageOf(start);
                double at1 = one.Run.ChainageOf(end);
                double lo = Math.Max(0, Math.Min(at0, at1));
                double hi = Math.Min(one.Run.Length, Math.Max(at0, at1));

                LoadProfile profile = profileOf(one.Run);
                one.Matching = 0;
                one.Loaded = 0;

                foreach (double[] stretch in LoadProfile.Pieces(lo, hi, 0, profile))
                {
                    double value = profile.At((stretch[0] + stretch[1]) / 2d);
                    double width = stretch[1] - stretch[0];

                    if (value > 0)
                    {
                        one.Loaded += width;

                        if (valued.HasExpected && band.Agrees(valued.Expected, value))
                        {
                            one.Matching += width;
                        }
                    }
                }
            }

            return covering.OrderByDescending(one => one.ByHand)
                           .ThenByDescending(one => Math.Round(one.Matching))
                           .ThenByDescending(one => Math.Round(one.Loaded))
                           .ThenBy(one => one.Offset)
                           .ThenBy(one => one.Run.Identifier, StringComparer.OrdinalIgnoreCase)
                           .First();
        }

        /// <summary>The part of a straight piece between two fractions along it.</summary>
        private static LineLoad_R.Piece Cut(LineLoad_R.Piece piece, double t0, double t1)
        {
            return new LineLoad_R.Piece(piece.Line, PointOn(piece, t0), PointOn(piece, t1));
        }

        /// <summary>
        /// What a run carries, in words: one value where it is loaded all the way along, and
        /// otherwise each value with where it is.
        /// </summary>
        private static string Describe(LoadProfile profile, double length)
        {
            List<double[]> loaded = new List<double[]>();

            foreach (double[] piece in LoadProfile.Pieces(0, length, 0, profile))
            {
                double value = profile.At((piece[0] + piece[1]) / 2d);
                if (value <= 0)
                {
                    continue;
                }

                double[] last = loaded.Count == 0 ? null : loaded[loaded.Count - 1];
                if (last != null && Math.Abs(last[1] - piece[0]) < 1 && Math.Abs(last[2] - value) < 1e-6)
                {
                    last[1] = piece[1];
                    continue;
                }

                loaded.Add(new[] { piece[0], piece[1], value });
            }

            if (loaded.Count == 0)
            {
                return "nothing";
            }

            if (loaded.Count == 1 && loaded[0][0] < 1 && loaded[0][1] > length - 1)
            {
                return LoadSet.Text(loaded[0][2]) + " kN/m";
            }

            return string.Join("; ", loaded.Select(one =>
                $"{LoadSet.Text(one[2])} kN/m from {Math.Round(one[0] / 1000d, 2).ToString("0.##")} "
                + $"to {Math.Round(one[1] / 1000d, 2).ToString("0.##")} m"));
        }

        #endregion

        #region the parts of a line

        /// <summary>One part of a line, to be laid on one run and put back together afterwards.</summary>
        private static Assignment Portion(LineLoadResult owner, Assignment valued, LineLoadRun run)
        {
            Assignment portion = new Assignment
            {
                Result = new LineLoadResult(owner.Line),
                Owner = owner,
                Run = run,
                Expected = valued.Expected,
                HasExpected = valued.HasExpected
            };
            portion.Result.Run = run;
            return portion;
        }

        /// <summary>
        /// The run one straight piece runs along: inside the location buffer, within the angle,
        /// alongside it for some length - and of those, the one it lies along for longest.
        /// </summary>
        private LineLoadRun RunAlong(LineLoad_R.Piece piece, List<LineLoadRun> onStory,
                                            HashSet<LineLoadRun> spokenFor, PrefrencesData pref,
                                            LineLoadRun except = null)
        {
            LineLoadRun best = null;
            double bestOverlap = 0;
            double bestOffset = double.MaxValue;

            foreach (LineLoadRun run in onStory)
            {
                if (run == except || spokenFor.Contains(run)
                    || WallGeometry.AngleBetween(piece, run) > pref.LineLoadAngleTolerance)
                {
                    continue;
                }

                double offset = WallGeometry.Offset(piece, run);
                if (offset > reach)
                {
                    continue;
                }

                double overlap = WallGeometry.OverlapLength(run, piece);
                if (overlap <= 0)
                {
                    continue;
                }

                if (best == null || overlap > bestOverlap + 1e-6
                    || (Math.Abs(overlap - bestOverlap) <= 1e-6 && offset < bestOffset))
                {
                    best = run;
                    bestOverlap = overlap;
                    bestOffset = offset;
                }
            }

            return best;
        }

        #endregion

        #region a load another line takes

        /// <summary>
        /// The parts of lines whose ETABS load another line took, placed on the next run along
        /// each of them - the parallel beam a few hundred millimetres over, most often, which
        /// is the frame this line was drawn for all along. A part with no other run along it
        /// stays where it was, reported as taken.
        /// </summary>
        private List<Assignment> HandOnWhatWasTaken(List<Assignment> assignments,
                                                           List<LineLoadRun> onStory,
                                                           HashSet<LineLoadRun> spokenFor,
                                                           PrefrencesData pref)
        {
            List<Assignment> made = new List<Assignment>();

            foreach (Assignment assignment in assignments.Where(one => one.Run != null && !one.IsHandedOn).ToList())
            {
                List<LineLoadStretch> taken = assignment.Result.Stretches
                    .Where(one => one.State == LineLoadState.TakenByOther)
                    .ToList();
                if (taken.Count == 0)
                {
                    continue;
                }

                LineLoadResult owner = assignment.Owner ?? assignment.Result;
                Dictionary<LineLoadRun, Assignment> byRun = new Dictionary<LineLoadRun, Assignment>();

                foreach (LineLoadStretch stretch in taken)
                {
                    foreach (KeyValuePair<LineLoad_R.Piece, double> part in PiecesBetween(owner.Line, stretch.From, stretch.To))
                    {
                        LineLoadRun other = RunAlong(part.Key, onStory, spokenFor, pref, assignment.Run);

                        double from, to;
                        if (other == null || !WallGeometry.OverlapSpan(other, part.Key, out from, out to))
                        {
                            continue;
                        }

                        Assignment handed;
                        if (!byRun.TryGetValue(other, out handed))
                        {
                            handed = new Assignment
                            {
                                Result = new LineLoadResult(owner.Line),
                                Owner = owner,
                                Run = other,
                                IsHandedOn = true,
                                Expected = assignment.Expected,
                                HasExpected = assignment.HasExpected
                            };
                            handed.Result.Run = other;
                            byRun[other] = handed;
                        }

                        handed.Spans.Add(new[] { from, to });
                        handed.SpanPieces.Add(part.Key);
                        handed.SpanBefore.Add(part.Value);
                        handed.Length += part.Key.Length;
                    }
                }

                made.AddRange(byRun.Values);
            }

            return made;
        }

        /// <summary>
        /// The straight pieces of a line between two distances along it, each with how far
        /// along the line it starts.
        /// </summary>
        private static List<KeyValuePair<LineLoad_R.Piece, double>> PiecesBetween(LineLoad_R line,
                                                                                  double from, double to)
        {
            List<KeyValuePair<LineLoad_R.Piece, double>> parts = new List<KeyValuePair<LineLoad_R.Piece, double>>();
            double walked = 0;

            foreach (LineLoad_R.Piece piece in line.Pieces)
            {
                double length = piece.Length;
                double start = walked;
                walked += length;

                if (length <= 0 || walked <= from || start >= to)
                {
                    continue;
                }

                double t0 = Math.Max(0, (from - start) / length);
                double t1 = Math.Min(1, (to - start) / length);
                if (t1 - t0 <= 1e-9)
                {
                    continue;
                }

                parts.Add(new KeyValuePair<LineLoad_R.Piece, double>(
                    new LineLoad_R.Piece(line, PointOn(piece, t0), PointOn(piece, t1)),
                    start + t0 * length));
            }

            return parts;
        }

        /// <summary>The point a distance along a line, in mm from its start.</summary>
        private static MyPoint PointAlong(LineLoad_R line, double along)
        {
            double walked = 0;
            for (int i = 0; i < line.Pieces.Count; i++)
            {
                LineLoad_R.Piece piece = line.Pieces[i];
                double length = piece.Length;
                if (along <= walked + length || i == line.Pieces.Count - 1)
                {
                    double t = length > 0 ? Math.Max(0, Math.Min(1, (along - walked) / length)) : 0;
                    return PointOn(piece, t);
                }
                walked += length;
            }
            return line.Pieces.Count > 0 ? line.Pieces[0].PlanStart : MyPoint.Zero;
        }

        #endregion

        #region putting each line out once

        /// <summary>
        /// Every line's result, once. A line placed whole and judged on one run is published as
        /// it stands; a curve placed a piece at a time, or a line part of which was handed on
        /// to another run, is put back together first.
        /// </summary>
        private void Publish(List<Assignment> assignments, PrefrencesData pref)
        {
            foreach (IGrouping<LineLoadResult, Assignment> line in assignments.GroupBy(one => one.Owner ?? one.Result))
            {
                List<Assignment> sources = line.ToList();

                if (sources.Count > 1 || sources[0].Owner != null)
                {
                    Assemble(line.Key, sources, pref);
                }

                // Every run the line was read against, in order along it.
                line.Key.Runs.Clear();
                line.Key.Runs.AddRange(sources
                    .Where(one => one.Run != null)
                    .OrderBy(one => one.SpanBefore.Count > 0 ? one.SpanBefore.Min() : 0)
                    .Select(one => one.Run)
                    .Distinct());

                // Where on those frames it lies, for writing it back. Only the parts placed by
                // the line itself: a part handed on to another run went looking for its load
                // somewhere else, and putting it there as well would put the line in twice.
                line.Key.Placements.Clear();
                foreach (Assignment one in sources.Where(one => one.Run != null && !one.IsHandedOn))
                {
                    line.Key.Placements.AddRange(PlacementsOf(one));
                }

                results.Add(line.Key);
            }
        }

        /// <summary>
        /// The stretches of each frame on a run that one placed line covers, as fractions of the
        /// frame from its I end, with spans that touch or overlap joined into one.
        /// </summary>
        private static IEnumerable<LineLoadPlacement> PlacementsOf(Assignment assignment)
        {
            double value = assignment.HasExpected ? assignment.Expected : 0;

            foreach (LineLoadFrame_E frame in assignment.Run.Frames)
            {
                double atI = assignment.Run.ChainageOf(frame.PlanStart);
                double atJ = assignment.Run.ChainageOf(frame.PlanEnd);
                double length = atJ - atI;

                if (Math.Abs(length) < 1e-9 || frame.UniqueName.Length == 0)
                {
                    continue;
                }

                List<double[]> covered = new List<double[]>();
                foreach (double[] span in assignment.Spans)
                {
                    double from = Math.Max(0, Math.Min(1, (span[0] - atI) / length));
                    double to = Math.Max(0, Math.Min(1, (span[1] - atI) / length));
                    if (Math.Abs(to - from) * Math.Abs(length) > 1)
                    {
                        covered.Add(new[] { Math.Min(from, to), Math.Max(from, to) });
                    }
                }

                covered.Sort((a, b) => a[0].CompareTo(b[0]));

                double touching = 1 / Math.Abs(length);
                double[] open = null;
                foreach (double[] span in covered)
                {
                    if (open != null && span[0] <= open[1] + touching)
                    {
                        open[1] = Math.Max(open[1], span[1]);
                        continue;
                    }

                    if (open != null)
                    {
                        yield return new LineLoadPlacement(frame.UniqueName, frame.Label, open[0], open[1],
                                                           value, frame.PlanLength);
                    }
                    open = new[] { span[0], span[1] };
                }

                if (open != null)
                {
                    yield return new LineLoadPlacement(frame.UniqueName, frame.Label, open[0], open[1],
                                                       value, frame.PlanLength);
                }
            }
        }

        /// <summary>
        /// Puts one line back together out of the parts the runs judged: its stretches in
        /// order along it, the parts nothing in ETABS runs along reported as that, and the
        /// parts another line took replaced by what the run they were handed on to said.
        /// </summary>
        private static void Assemble(LineLoadResult owner, List<Assignment> sources, PrefrencesData pref)
        {
            List<Assignment> primary = sources.Where(one => !one.IsHandedOn).ToList();
            List<Assignment> handedOn = sources.Where(one => one.IsHandedOn).ToList();

            List<Assignment> placed = primary.Where(one => one.Run != null).ToList();
            Assignment main = placed.OrderByDescending(one => one.Length).FirstOrDefault();

            // A line split over several frames is named after the one most of it lies along;
            // the card lists every one of them.
            if (main != null && main.Owner != null)
            {
                owner.Run = main.Run;

                if (placed.Any(one => one.ByHand))
                {
                    owner.Note = Join(owner.Note, "paired by hand");
                }
            }

            if (handedOn.Count > 0)
            {
                owner.Note = Join(owner.Note,
                    "another line drawn over it takes the ETABS load under part of it, so that part was read against ETABS "
                    + string.Join(", ", handedOn.Select(one => one.Run.Identifier).Distinct()));
            }

            // Settled before anything was compared - no number, not on the sheet, no height.
            if (!sources.Any(one => one.HasExpected))
            {
                return;
            }

            List<LineLoadStretch> all = primary.SelectMany(StretchesOf).ToList();
            List<LineLoadStretch> instead = handedOn.SelectMany(StretchesOf).ToList();
            if (instead.Count > 0)
            {
                all = Substitute(all, instead, owner.Line);
            }

            if (all.Count == 0)
            {
                owner.State = LineLoadState.NoEtabsFrame;
                owner.Note = Join(owner.Note, "no ETABS frame runs along any part of this line");
                return;
            }

            // What each part read on its own frame, before the parts are put together: a part
            // judged whole carries one figure into its stretch, and the values behind it would
            // be lost.
            double wanted = sources.First(one => one.HasExpected).Expected;
            List<KeyValuePair<string, double>> read = sources
                .Where(one => one.Run != null)
                .OrderBy(one => one.SpanBefore.Count > 0 ? one.SpanBefore.Min() : 0)
                .SelectMany(one => one.Result.Carried)
                .ToList();

            all.Sort((a, b) => a.From.CompareTo(b.From));
            List<LineLoadStretch> stretches = Merge(all);
            FoldShortFailures(stretches, pref.LineLoadMinimumIssueLength);

            Summarise(owner, stretches, main != null && main.Run.HasGravityLoad, wanted);

            if (read.Count > 0)
            {
                owner.Carried.Clear();
                foreach (KeyValuePair<string, double> one in read)
                {
                    owner.AddCarried(one.Key, one.Value);
                }
                owner.Actual = AsRead(read.Select(one => one.Value), wanted);
            }

            // Checked whole, with steps off: the verdict is the parts', and the line is still
            // drawn as one.
            if (!pref.LineLoadCheckInSteps)
            {
                owner.Stretches.Clear();
            }
        }

        /// <summary>
        /// What one part of a line said, as stretches along the line. A part judged in steps
        /// has them already; a part with no run, or on a run that could only be judged on its
        /// total, is one stretch per piece carrying what its run said about it.
        /// </summary>
        private static List<LineLoadStretch> StretchesOf(Assignment part)
        {
            if (part.Run != null && part.Result.Stretches.Count > 0)
            {
                return part.Result.Stretches.ToList();
            }

            LineLoadState state = part.Run == null
                ? LineLoadState.NoEtabsFrame
                : part.Result.State == LineLoadState.NotChecked
                    ? LineLoadState.Matched
                    : part.Result.State == LineLoadState.PartlyCovered
                        ? LineLoadState.NoEtabsLoad
                        : part.Result.State;

            List<LineLoadStretch> stretches = new List<LineLoadStretch>();
            for (int i = 0; i < part.SpanPieces.Count; i++)
            {
                LineLoad_R.Piece piece = part.SpanPieces[i];
                stretches.Add(new LineLoadStretch
                {
                    From = part.SpanBefore[i],
                    To = part.SpanBefore[i] + piece.Length,
                    Start = piece.PlanStart,
                    End = piece.PlanEnd,
                    Wanted = part.Expected,
                    Carried = part.Run == null ? 0 : part.Result.Actual,
                    State = state,
                    Frames = part.Run == null ? "" : part.Run.Identifier
                });
            }
            return stretches;
        }

        /// <summary>
        /// The stretches another line took, replaced by what the run they were handed on to
        /// said about them. Whatever of a taken stretch no other run picked up stays taken.
        /// </summary>
        private static List<LineLoadStretch> Substitute(List<LineLoadStretch> all,
                                                        List<LineLoadStretch> instead,
                                                        LineLoad_R line)
        {
            List<LineLoadStretch> kept = new List<LineLoadStretch>();

            foreach (LineLoadStretch stretch in all)
            {
                if (stretch.State != LineLoadState.TakenByOther)
                {
                    kept.Add(stretch);
                    continue;
                }

                List<double[]> left = new List<double[]> { new[] { stretch.From, stretch.To } };
                foreach (LineLoadStretch replacement in instead)
                {
                    left = Subtract(left, replacement.From, replacement.To);
                }

                foreach (double[] range in left.Where(one => one[1] - one[0] > 1))
                {
                    kept.Add(new LineLoadStretch
                    {
                        From = range[0],
                        To = range[1],
                        Start = PointAlong(line, range[0]),
                        End = PointAlong(line, range[1]),
                        Wanted = stretch.Wanted,
                        Carried = stretch.Carried,
                        State = stretch.State,
                        TakenBy = stretch.TakenBy,
                        Frames = stretch.Frames
                    });
                }
            }

            kept.AddRange(instead);
            return kept;
        }

        private static List<double[]> Subtract(List<double[]> ranges, double from, double to)
        {
            List<double[]> left = new List<double[]>();
            foreach (double[] range in ranges)
            {
                if (to <= range[0] || from >= range[1])
                {
                    left.Add(range);
                    continue;
                }
                if (from > range[0])
                {
                    left.Add(new[] { range[0], from });
                }
                if (to < range[1])
                {
                    left.Add(new[] { to, range[1] });
                }
            }
            return left;
        }

        #endregion

        private static MyPoint PointOn(LineLoad_R.Piece piece, double t)
        {
            return new MyPoint(piece.PlanStart.X + (piece.PlanEnd.X - piece.PlanStart.X) * t,
                               piece.PlanStart.Y + (piece.PlanEnd.Y - piece.PlanStart.Y) * t,
                               piece.PlanStart.Z + (piece.PlanEnd.Z - piece.PlanStart.Z) * t);
        }

        /// <summary>
        /// Consecutive steps that read alike, as one stretch - only where they carry the same
        /// numbers, so a stretch never prints a value it does not carry and two loads are never
        /// averaged into one.
        ///
        /// Never across two frames: a stretch is read against one frame, and the card names it.
        /// </summary>
        private static List<LineLoadStretch> Merge(List<LineLoadStretch> steps)
        {
            List<LineLoadStretch> merged = new List<LineLoadStretch>();

            foreach (LineLoadStretch step in steps)
            {
                LineLoadStretch last = merged.Count == 0 ? null : merged[merged.Count - 1];

                bool joins = last != null
                             && Math.Abs(last.To - step.From) < 1
                             && last.State == step.State
                             && last.TakenBy == step.TakenBy
                             && last.Frames == step.Frames
                             && Math.Abs(last.Wanted - step.Wanted) < 1e-6
                             && Math.Abs(last.Carried - step.Carried) < 1e-6;

                if (!joins)
                {
                    merged.Add(step);
                    continue;
                }

                last.To = step.To;
                last.End = step.End;
            }

            return merged;
        }

        /// <summary>
        /// A failing stretch shorter than the shortest difference worth reporting, taken into
        /// the stretch before it - or after it, at the start of the line. A line on its own is
        /// never folded away: a short line that is wrong is still wrong.
        /// </summary>
        private static void FoldShortFailures(List<LineLoadStretch> stretches, double shortest)
        {
            bool folded = true;
            while (folded && stretches.Count > 1)
            {
                folded = false;

                for (int i = 0; i < stretches.Count; i++)
                {
                    LineLoadStretch one = stretches[i];
                    if (!one.IsFailure || one.Length >= shortest)
                    {
                        continue;
                    }

                    // Only into a stretch it runs on from, give or take a gap too short to report.
                    // Two stretches of one part of a line can have the rest of the line between
                    // them, on another frame - its two ends, where it overhangs the beam it sits
                    // on - and folding one into the other would paint the whole length between
                    // them with what the ends read.
                    LineLoadStretch before = i > 0 && one.From - stretches[i - 1].To < shortest ? stretches[i - 1] : null;
                    LineLoadStretch after = i + 1 < stretches.Count && stretches[i + 1].From - one.To < shortest ? stretches[i + 1] : null;
                    if (before == null && after == null)
                    {
                        continue;
                    }

                    LineLoadStretch into = before ?? after;
                    if (into == before)
                    {
                        into.To = one.To;
                        into.End = one.End;
                    }
                    else
                    {
                        into.From = one.From;
                        into.Start = one.Start;
                    }
                    stretches.RemoveAt(i);

                    // Folding can leave two neighbours that now read alike side by side.
                    List<LineLoadStretch> again = Merge(stretches);
                    stretches.Clear();
                    stretches.AddRange(again);

                    folded = true;
                    break;
                }
            }
        }

        #endregion

        #region what the plan never asked for

        /// <summary>
        /// The stretches of frame carrying a load that no drawn line covers.
        ///
        /// The check's other direction, and the one nothing on the loading plan can show: a
        /// line nobody drew leaves nothing there to colour. A run with a bundled line on it is
        /// left out, because its plan side was never laid along the frame in the first place
        /// and every metre of it would read as unclaimed.
        /// </summary>
        private void ReportUnclaimed(List<LineLoadRun> onStory, List<Assignment> assignments,
                                     PrefrencesData pref, LineLoadPatterns patterns)
        {
            Dictionary<LineLoadRun, List<Assignment>> byRun = assignments
                .Where(assignment => assignment.Run != null)
                .GroupBy(assignment => assignment.Run)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (LineLoadRun run in onStory)
            {
                List<Assignment> onThisRun;
                if (byRun.TryGetValue(run, out onThisRun) && onThisRun.Any(one => one.Bundled))
                {
                    continue;
                }

                LoadProfile actual = run.GravityProfile(patterns);
                if (actual.IsEmpty)
                {
                    continue;
                }

                LoadProfile claimed = new LoadProfile();

                if (onThisRun != null)
                {
                    foreach (Assignment assignment in onThisRun)
                    {
                        foreach (double[] span in assignment.Spans)
                        {
                            claimed.Add(span[0], span[1], 1, 1);
                        }
                    }
                }

                foreach (double[] piece in LoadProfile.Pieces(0, run.Length,
                                                              pref.LineLoadMinimumIssueLength,
                                                              actual, claimed))
                {
                    double at = (piece[0] + piece[1]) / 2d;

                    if (claimed.At(at) > 0 || actual.At(at) <= 0)
                    {
                        continue;
                    }

                    if (piece[1] - piece[0] < pref.LineLoadMinimumIssueLength)
                    {
                        continue;
                    }

                    EtabsLineLoad stretch = new EtabsLineLoad(run, run.PointAt(piece[0]), run.PointAt(piece[1]),
                                                              actual.At(at),
                                                              actual.TotalForce(piece[0], piece[1]));
                    NoteLinesAlongside(stretch, run, piece[0], piece[1], assignments, pref);
                    unclaimed.Add(stretch);
                }
            }
        }

        /// <summary>
        /// Ties a stretch nothing claims to a drawn line lying right beside it that was read
        /// against a different run, and says so on both.
        ///
        /// Two ETABS frames side by side a few centimetres apart - a beam and the edge of a
        /// wall, a frame modelled twice - both within reach of the same drawn line. The line
        /// goes to one of them and the other's load is left over, and on the plan that is a
        /// line and an ETABS-only frame lying together with nothing to say why they are not one.
        /// Either the load is on both frames, or the line was read against the wrong one of
        /// the two; which it is only somebody looking can say, but they can only look if told.
        /// </summary>
        private void NoteLinesAlongside(EtabsLineLoad stretch, LineLoadRun run,
                                               double from, double to,
                                               List<Assignment> assignments, PrefrencesData pref)
        {
            HashSet<LineLoadResult> noted = new HashSet<LineLoadResult>();

            // A line part of which was read against this run already knows about it.
            HashSet<LineLoadResult> onThisRun = new HashSet<LineLoadResult>(
                assignments.Where(one => one.Run == run).Select(one => one.Owner ?? one.Result));

            foreach (Assignment assignment in assignments)
            {
                if (assignment.Run == null || assignment.Run == run)
                {
                    continue;
                }

                LineLoadResult target = assignment.Owner ?? assignment.Result;
                if (noted.Contains(target) || onThisRun.Contains(target))
                {
                    continue;
                }

                LineLoad_R line = target.Line;

                // The part of the line read against the other run, where it is only a part.
                IEnumerable<LineLoad_R.Piece> pieces = assignment.SpanPieces.Count > 0
                    ? (IEnumerable<LineLoad_R.Piece>)assignment.SpanPieces
                    : line.Pieces;

                bool alongside = pieces.Any(piece =>
                {
                    if (WallGeometry.Offset(piece, run) > reach
                        || WallGeometry.AngleBetween(piece, run) > pref.LineLoadAngleTolerance)
                    {
                        return false;
                    }

                    double start, end;
                    return WallGeometry.OverlapSpan(run, piece, out start, out end)
                           && Math.Min(end, to) - Math.Max(start, from) > 0;
                });

                if (!alongside)
                {
                    continue;
                }

                noted.Add(target);
                string metres = Math.Round(stretch.Length / 1000d, 2).ToString("0.##");

                stretch.Alongside = Join(stretch.Alongside,
                    $"line load {line.RevitID} lies alongside, but was read against ETABS "
                    + $"{assignment.Run.Identifier}. The load may be on both frames, or the line "
                    + "belongs to this one");

                target.Alongside = Join(target.Alongside,
                    $"ETABS {run.Identifier} also runs along this line and carries "
                    + $"{LoadSet.Text(stretch.PerMetre)} kN/m over {metres} m that no drawn line "
                    + "claims. The load may be on both frames, or this line belongs to that one");
            }
        }

        /// <summary>
        /// Every stretch of every run that carries a load, with whether the plan claims it.
        ///
        /// Kept apart from the unclaimed pass rather than folded into it, because the two are
        /// asked for different reasons and answer differently. The unclaimed pass is a finding
        /// and steps around the runs it cannot speak for - one carrying a bundled line, where
        /// the plan side was never laid along the frame. This is not a finding at all, it is
        /// the ETABS model drawn as it is, so it steps around nothing.
        /// </summary>
        private void CollectEtabsStretches(List<LineLoadRun> onStory, List<Assignment> assignments,
                                           PrefrencesData pref, LineLoadPatterns patterns)
        {
            Dictionary<LineLoadRun, List<Assignment>> byRun = assignments
                .Where(assignment => assignment.Run != null)
                .GroupBy(assignment => assignment.Run)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (LineLoadRun run in onStory)
            {
                LoadProfile actual = run.GravityProfile(patterns);
                if (actual.IsEmpty)
                {
                    continue;
                }

                LoadProfile claimed = new LoadProfile();

                List<Assignment> onThisRun;
                if (byRun.TryGetValue(run, out onThisRun))
                {
                    foreach (Assignment assignment in onThisRun)
                    {
                        foreach (double[] span in assignment.Spans)
                        {
                            claimed.Add(span[0], span[1], 1, 1);
                        }
                    }
                }

                foreach (double[] piece in LoadProfile.Pieces(0, run.Length,
                                                              pref.LineLoadMinimumIssueLength,
                                                              actual, claimed))
                {
                    double at = (piece[0] + piece[1]) / 2d;

                    if (actual.At(at) <= 0)
                    {
                        continue;
                    }

                    etabsStretches.Add(new EtabsLineLoad(run, run.PointAt(piece[0]),
                                                         run.PointAt(piece[1]),
                                                         actual.At(at),
                                                         actual.TotalForce(piece[0], piece[1]))
                    {
                        Claimed = claimed.At(at) > 0
                    });
                }
            }
        }

        #endregion

        /// <summary>
        /// Names the frame loads the check read and could not use, so a line that reads as
        /// right is not quietly right about half of what is on the frame.
        ///
        /// A load assigned in one of the frame's own local directions is the usual one: which
        /// way local 2 points depends on that frame's rotation, so the check will not guess a
        /// sign for it. A distributed moment is not a line load at all.
        /// </summary>
        private void ReportLoadsThatCannotBeCompared(IList<LineLoadFrame_E> frames,
                                                     LineLoadPatterns patterns)
        {
            if (frames == null)
            {
                return;
            }

            List<string> sideways = new List<string>();
            List<string> moments = new List<string>();

            // Which patterns the frames actually carry, and which of them the settings read.
            // The same trap the line style setting sets: a model whose patterns are named
            // something the setting does not say gives a check that finds nothing and says
            // nothing about why. Counting them here means the answer is always in the report.
            Dictionary<string, int> read = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> passedOver = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (LineLoadFrame_E frame in frames)
            {
                foreach (LineLoad_E load in frame.Loads)
                {
                    if (load == null)
                    {
                        continue;
                    }

                    if (load.IsMoment)
                    {
                        moments.Add(frame.Identifier);
                        continue;
                    }

                    if (!load.IsGravity)
                    {
                        sideways.Add($"{frame.Identifier} (direction {load.Direction})");
                        continue;
                    }

                    Tally(patterns != null && patterns.Matches(load.Pattern) ? read : passedOver,
                          load.Pattern);
                }
            }

            ReportPatterns(read, passedOver, patterns);

            if (sideways.Count > 0)
            {
                issues.Add($"{sideways.Count} distributed load(s) are assigned in a direction this "
                           + "check cannot read as downwards, so they are left out of it: "
                           + Some(sideways) + ". A frame's local axes turn with the frame, so "
                           + "which of them points down is not something the check will guess at - "
                           + "assign the load in Gravity or in global Z to have it compared.");
            }

            if (moments.Count > 0)
            {
                issues.Add($"{moments.Count} distributed moment(s) are on the frames and are not "
                           + "line loads, so they are left out of the check: " + Some(moments) + ".");
            }
        }

        /// <summary>
        /// What the frames carry, sorted into the patterns this check read and the patterns it
        /// passed over, so a setting naming a pattern the model has not got says so outright.
        /// </summary>
        private void ReportPatterns(Dictionary<string, int> read, Dictionary<string, int> passedOver,
                                    LineLoadPatterns patterns)
        {
            if (read.Count == 0 && passedOver.Count == 0)
            {
                return;
            }

            string wanted = patterns == null ? "every pattern" : patterns.Description;

            if (read.Count == 0)
            {
                issues.Add($"None of the distributed loads on the frames is in a pattern this check "
                           + $"reads. It is set to read {wanted}, and what the frames actually carry "
                           + $"is {Tallied(passedOver)}. Put the pattern you want into {PatternsSetting}.");
                return;
            }

            issues.Add($"Read the distributed loads in {Tallied(read)}"
                       + (passedOver.Count == 0
                           ? "."
                           : $", and passed over {Tallied(passedOver)} - the setting reads {wanted}."));
        }

        private static void Tally(Dictionary<string, int> counts, string pattern)
        {
            string name = string.IsNullOrEmpty(pattern) ? "(no pattern)" : pattern;

            int already;
            counts.TryGetValue(name, out already);
            counts[name] = already + 1;
        }

        /// <summary>Pattern names with how many loads each carries, busiest first.</summary>
        private static string Tallied(Dictionary<string, int> counts)
        {
            return string.Join(", ", counts.OrderByDescending(entry => entry.Value)
                                           .Take(12)
                                           .Select(entry => $"{entry.Key} ({entry.Value})"));
        }

        private static string Some(List<string> items)
        {
            const int most = 12;

            List<string> distinct = items.Distinct().ToList();

            return distinct.Count <= most
                ? string.Join("; ", distinct)
                : string.Join("; ", distinct.Take(most)) + $"; and {distinct.Count - most} more";
        }

        private static string Join(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }

            // A run can be judged twice, and a note already said is not said again.
            if (string.IsNullOrEmpty(second) || first.Contains(second))
            {
                return first;
            }

            return first + "; " + second;
        }
    }
}
