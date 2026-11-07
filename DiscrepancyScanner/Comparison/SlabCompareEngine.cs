using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Compares the ETABS slabs against the Revit floors and reports where, exactly, the two
    /// models stop agreeing.
    ///
    /// The ETABS mesh is the base mesh and it is left alone. An area object comes out of the
    /// check as the one shape it went in as, whatever its verdict, and is only ever cut when
    /// the answer changes part way across it: a drop panel Revit has and ETABS does not, an
    /// opening on one side only, an edge the two models drew in different places. The cut then
    /// runs exactly along where the answer changes, so what gets coloured on the plan is the
    /// part that is actually wrong rather than the whole shell.
    ///
    /// Openings need no pass of their own. A hole in one model and not the other is a piece of
    /// plan one model covers and the other does not, which is what the region algebra reports
    /// anyway; all that is left is to say whether that piece sits inside the slab, which makes
    /// it an opening, or runs along its edge, which makes it an outline that does not agree.
    /// </summary>
    public class SlabCompareEngine
    {
        private readonly List<SlabZone> zones = new List<SlabZone>();
        private readonly List<SlabZone> loadZones = new List<SlabZone>();
        private readonly List<string> issues = new List<string>();

        private double thicknessTolerance;
        private double minimumZoneArea;
        private double minimumZoneWidth;
        private double edgeTolerance;
        private double topElevationTolerance;

        private double stackSearchDepth;
        private double loadTolerance;
        private LoadPatternMap.Mapping loadPatterns;

        // The smallest patch each check counts a difference as work on, in mm squared. Read in
        // m2, because that is the size of thing an engineer decides this in, and held here in
        // the millimetres everything else in the check is measured in.
        private double thicknessIssueArea;
        private double loadIssueArea;

        private bool readColumnsAndWalls;

        /// <summary>
        /// The columns and walls of both models, which the slab is read as solid through and
        /// whose insides are accepted - see <see cref="SetColumnsAndWalls"/>.
        /// </summary>
        private readonly List<SlabSupport> columnsAndWalls = new List<SlabSupport>();

        /// <summary>The story directly above each one, by name, for the columns rising from it.</summary>
        private readonly Dictionary<string, string> storyAbove =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The columns and walls standing in the slab, from both models.
        ///
        /// Three things come of them, all only while
        /// <see cref="PrefrencesData.SlabAcceptInsideColumnsAndWalls"/> is on. The hole a column
        /// or wall cuts in a Revit floor is filled back in, the same as for a stiff area. An
        /// ETABS shell lying inside one - a rigid zone drawn at the column under a slab or drop
        /// type rather than as a stiff area - is read as the support it is rather than as slab.
        /// And whatever the thickness, outline or opening check still finds inside one is
        /// accepted, since it is concrete belonging to the column or the wall either way.
        ///
        /// Setting none leaves the check exactly as it was.
        /// </summary>
        public void SetColumnsAndWalls(IEnumerable<SlabSupport> supports)
        {
            columnsAndWalls.Clear();
            if (supports != null)
            {
                columnsAndWalls.AddRange(supports.Where(support => support?.Plan != null));
            }
        }


        /// <summary>
        /// Every piece of slab the thickness, outline and opening checks produced, across
        /// every story. Cut along the Revit floor edges.
        /// </summary>
        public IList<SlabZone> Zones { get { return zones; } }

        /// <summary>
        /// Every piece of slab the load check produced, cut along the loading plan's region
        /// edges instead. A separate mesh over the same areas: the two questions are answered
        /// on different shapes, and a piece of one has no counterpart in the other.
        ///
        /// Empty for a story with no loading plan paired to it, which is most of them.
        /// </summary>
        public IList<SlabZone> LoadZones { get { return loadZones; } }

        /// <summary>
        /// Anything the check could not work out, collected so it can be shown once at the
        /// end rather than as a dialog per area.
        /// </summary>
        public IList<string> Issues { get { return issues; } }

        /// <summary>
        /// Called as the check works through the model, with a sentence saying where it has
        /// got to and how far along it is, nought to one. The check runs on the thread Revit
        /// hands it and cannot report from anywhere else, so whoever sets this is the one
        /// that has to let the screen catch up.
        /// </summary>
        public Action<string, double> ReportProgress { get; set; }

        /// <summary>
        /// The one story to check, or null to check every story either model has slabs on.
        ///
        /// Only the stories that get compared are narrowed. The floors are still read from
        /// the whole model, because a drop panel deep enough to be named after the story
        /// below is still part of the slab above it, and leaving it out would report the slab
        /// as thinner than it is.
        /// </summary>
        public string OnlyStory { get; set; }

        /// <summary>
        /// True to run the load pass alone, over the stories a loading plan is set for, and
        /// leave <see cref="Zones"/> empty.
        ///
        /// The loads are the one part of the slab check that answers to something outside the
        /// two models - a view somebody paired and a sheet somebody picked - and the part most
        /// often changed between runs. Nothing the thickness, outline and opening pass works
        /// out depends on either, so re-cutting it over every story to pick up a new pairing
        /// is the slow half of a run spent on an answer that cannot have changed.
        /// </summary>
        public bool LoadsOnly { get; set; }

        /// <summary>
        /// What the loading plan says each floor carries, by story.
        ///
        /// A loading plan is drawn for one floor, so there is one of these per floor rather
        /// than one for the run. A story nothing was set for is checked for thickness and
        /// outline exactly as it always was, which is what makes it safe to check a model
        /// where six levels out of forty-seven have been paired with a plan.
        /// </summary>
        private readonly Dictionary<string, LoadCoverage> loadsByStory =
            new Dictionary<string, LoadCoverage>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Says that this story's loads are to be checked against this plan. Setting nothing
        /// for a story leaves it checked the way it always was.
        /// </summary>
        public void SetLoads(string story, LoadCoverage coverage)
        {
            if (coverage == null || string.IsNullOrEmpty(story))
            {
                return;
            }

            loadsByStory[StoryKey(story)] = coverage;
        }

        /// <summary>
        /// The plan to check one story's loads against, or null when there is none - which an
        /// empty one counts as, so a plan that turned out to hold no regions leaves the story
        /// checked as before rather than checked against nothing.
        /// </summary>
        private LoadCoverage LoadsFor(string storyName)
        {
            LoadCoverage coverage;
            return loadsByStory.TryGetValue(StoryKey(storyName), out coverage)
                   && coverage != null && !coverage.IsEmpty
                ? coverage
                : null;
        }

        /// <summary>
        /// How a story name is matched between the pairings and the slabs.
        ///
        /// Trimmed, because the pairings are stored trimmed and a slab carries the name
        /// exactly as ETABS gave it. A story named with a trailing space would otherwise be
        /// paired in the window and silently unpaired in the check.
        /// </summary>
        private static string StoryKey(string story)
        {
            return (story ?? "").Trim();
        }

        /// <summary>
        /// Asked, between pieces of work, whether the user has asked for the check to stop.
        /// Left unset the check runs to the end, which is what it has always done.
        /// </summary>
        public Func<bool> ShouldStop { get; set; }

        /// <summary>
        /// True when the check gave up early because it was asked to. Whatever it had worked
        /// out by then is still in <see cref="Zones"/> and is correct as far as it goes; it
        /// simply does not cover the whole model.
        /// </summary>
        public bool Stopped { get; private set; }

        /// <summary>
        /// Whether the check should give up now. The answer is only asked for now and then,
        /// because the asking pumps the screen and doing that between every pair of slabs
        /// would cost more than the work being watched.
        /// </summary>
        private bool StopNow()
        {
            if (Stopped)
            {
                return true;
            }
            if (ShouldStop == null)
            {
                return false;
            }

            if (stopWatch.ElapsedMilliseconds - lastStopCheck < StopCheckIntervalMs)
            {
                return false;
            }
            lastStopCheck = stopWatch.ElapsedMilliseconds;

            if (ShouldStop())
            {
                Stopped = true;
            }
            return Stopped;
        }

        private void Report(string message, double fraction)
        {
            ReportProgress?.Invoke(message, Math.Max(0, Math.Min(1, fraction)));
        }

        private const long StopCheckIntervalMs = 120;

        private readonly System.Diagnostics.Stopwatch stopWatch = System.Diagnostics.Stopwatch.StartNew();
        private long lastStopCheck;

        public void StartComparison(IList<Slab_E> etabsSlabs, IList<Slab_R> revitSlabs, IList<Story_E> stories)
        {
            zones.Clear();
            loadZones.Clear();
            issues.Clear();
            Stopped = false;
            lastStopCheck = 0;
            CheckTiming.Reset();

            ReadPreferences();

            if (etabsSlabs == null)
            {
                etabsSlabs = new List<Slab_E>();
            }
            if (revitSlabs == null)
            {
                revitSlabs = new List<Slab_R>();
            }

            List<StoryToCheck> plan = BuildStoryPlan(etabsSlabs, revitSlabs, stories);
            BuildStoryAbove(stories);

            if (plan.Count == 0 && LoadsOnly)
            {
                ReportPairedStoriesNotChecked(plan);
                Report("No loads to check.", 1);
                return;
            }

            if (plan.Count == 0)
            {
                issues.Add(string.IsNullOrEmpty(OnlyStory)
                    ? "Neither model has a slab on any named story, so there was nothing to check."
                    : $"Neither model has a slab on {OnlyStory}, so there was nothing to check.");
                Report("Nothing to check.", 1);
                return;
            }

            ReportPairedStoriesNotChecked(plan);

            for (int i = 0; i < plan.Count; i++)
            {
                if (StopNow())
                {
                    break;
                }

                StoryToCheck story = plan[i];

                Report($"Checking {(LoadsOnly ? "loads" : "slabs")} on {story.Name} " +
                       $"({i + 1} of {plan.Count})...",
                       i / (double)plan.Count);

                System.Diagnostics.Stopwatch storyWatch = System.Diagnostics.Stopwatch.StartNew();

                CompareStory(story.Name, story.Elevation,
                    etabsSlabs.Where(slab => IsOnStory(slab.Story, story.Name)).ToList(),
                    revitSlabs.Where(slab => IsOnStory(slab.Story, story.Name)).ToList(),
                    revitSlabs);

                storyWatch.Stop();
                CheckTiming.Story(story.Name, storyWatch.ElapsedMilliseconds);
            }

            if (Stopped)
            {
                issues.Add("The slab check was stopped before it finished, so the stories it had not " +
                           "reached yet are not in the results. What it did work out is still right; " +
                           "there is simply less of it.");
                Report("Slab check stopped.", 1);
            }
            else
            {
                Report(LoadsOnly
                           ? $"Load check finished: {loadZones.Count} zone(s)."
                           : $"Slab check finished: {zones.Count} zone(s).", 1);
            }

            // Always reported, not only when something went wrong: a run that took ten
            // minutes and a run that took ten seconds look the same afterwards otherwise.
            foreach (string line in CheckTiming.Report())
            {
                issues.Add(line);
            }
        }

        /// <summary>
        /// Names any story that was paired with a loading plan but that the check is not going
        /// to reach - because it was narrowed to one story, or because neither model has a
        /// slab on it, or because the name in the pairing is not the name the slabs carry.
        ///
        /// Worth saying out loud: pairing a level and then seeing no load results on it looks
        /// exactly like the check being broken, and the difference is a sentence.
        /// </summary>
        private void ReportPairedStoriesNotChecked(List<StoryToCheck> plan)
        {
            List<string> missed = loadsByStory.Keys
                .Where(story => !plan.Any(planned => IsOnStory(StoryKey(planned.Name), story)))
                .OrderBy(story => story, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missed.Count == 0)
            {
                return;
            }

            issues.Add($"Paired with a loading plan but not checked: {string.Join(", ", missed)}. " +
                       (string.IsNullOrEmpty(OnlyStory)
                           ? "Neither model has a slab on those levels under that name."
                           : $"This run was narrowed to {OnlyStory}."));
        }

        /// <summary>One story the check is going to compare, and the plane it sits at.</summary>
        private sealed class StoryToCheck
        {
            public string Name;
            public double Elevation;
        }

        /// <summary>
        /// The stories to compare, in the order they will be done, each with the elevation its
        /// slab is drawn at. Every story either model has something on is included, so a Revit
        /// floor sitting at a level ETABS never modelled is still reported rather than quietly
        /// dropped - unless <see cref="OnlyStory"/> narrows it to the one story asked for.
        /// </summary>
        private List<StoryToCheck> BuildStoryPlan(IList<Slab_E> etabsSlabs, IList<Slab_R> revitSlabs,
                                                  IList<Story_E> stories)
        {
            // A load area is no slab, so it does not bring a story into the check on its own.
            IEnumerable<string> names = etabsSlabs.Where(slab => !slab.IsLoadOnly)
                .Select(slab => slab.Story)
                .Concat(revitSlabs.Select(slab => slab.Story))
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(OnlyStory))
            {
                names = names.Where(name => IsOnStory(name, OnlyStory));
            }

            if (LoadsOnly)
            {
                names = names.Where(name => LoadsFor(name) != null);
            }

            List<StoryToCheck> plan = new List<StoryToCheck>();

            foreach (string name in names)
            {
                Story_E story = stories?.FirstOrDefault(
                    s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

                plan.Add(new StoryToCheck
                {
                    Name = name,
                    Elevation = story?.Elevation ?? EstimateStoryElevation(etabsSlabs, revitSlabs, name)
                });
            }

            return plan;
        }

        private void ReadPreferences()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            thicknessTolerance = pref.SlabThicknessTolerance;
            minimumZoneArea = pref.SlabMinimumZoneArea;
            minimumZoneWidth = pref.SlabMinimumZoneWidth;
            edgeTolerance = pref.SlabEdgeTolerance;
            topElevationTolerance = pref.SlabTopElevationTolerance;
            stackSearchDepth = pref.SlabStackSearchDepth;
            loadTolerance = pref.SlabLoadTolerance;
            loadPatterns = LoadPatternMap.FromPreferences(pref);
            thicknessIssueArea = SquareMetres(pref.SlabThicknessMinimumIssueArea);
            loadIssueArea = SquareMetres(pref.SlabLoadMinimumIssueArea);
            readColumnsAndWalls = pref.SlabAcceptInsideColumnsAndWalls;
        }

        private void BuildStoryAbove(IList<Story_E> stories)
        {
            storyAbove.Clear();
            if (stories == null)
            {
                return;
            }

            List<Story_E> upwards = stories.Where(story => story != null && !string.IsNullOrEmpty(story.Name))
                                           .OrderBy(story => story.Elevation)
                                           .ToList();

            for (int i = 0; i + 1 < upwards.Count; i++)
            {
                storyAbove[StoryKey(upwards[i].Name)] = StoryKey(upwards[i + 1].Name);
            }
        }

        /// <summary>
        /// The columns and walls passing through one story's slab: the ones rising to it, which
        /// it sits on, and the ones rising from it to the story above. Both models name a
        /// column or a wall after the story at its top. Empty while the setting is off.
        /// </summary>
        private List<SlabSupport> ColumnsAndWallsAt(string storyName)
        {
            if (!readColumnsAndWalls || columnsAndWalls.Count == 0)
            {
                return new List<SlabSupport>();
            }

            string here = StoryKey(storyName);
            string above;
            storyAbove.TryGetValue(here, out above);

            return columnsAndWalls
                .Where(support => IsOnStory(StoryKey(support.Story), here)
                                  || (above != null && IsOnStory(StoryKey(support.Story), above)))
                .ToList();
        }

        /// <summary>
        /// The ETABS shells lying inside a column or a wall and drawn over thinner slab there: a
        /// rigid zone at the column given a slab type rather than a stiff one. Whatever type it
        /// was given, it is the support standing there rather than slab, the same as a stiff
        /// area stacked on the slab is - see <see cref="StackedStiffAreas"/>.
        ///
        /// Stacked is the condition here too. A shell meshed in with the rest of the slab - a
        /// strip that happens to lie under a wall - is the slab running on under the wall, and
        /// it used to be taken for the wall: it carried no verdict, nothing was drawn for it,
        /// and a floor meshed along its walls came out with a hole along every one of them.
        /// It is checked as slab now, and whatever is found inside the wall is accepted - see
        /// <see cref="AcceptInsideSupports"/>.
        /// </summary>
        internal static IEnumerable<Slab_E> ShellsInsideSupports(IList<Slab_E> onStory, IList<SlabSupport> supports)
        {
            if (onStory == null || supports == null || supports.Count == 0)
            {
                yield break;
            }

            List<Slab_E> slabs = onStory
                .Where(slab => slab != null && !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null)
                .ToList();

            foreach (Slab_E shell in slabs)
            {
                if (SlabSupports.Holding(shell.Plan, supports) == null)
                {
                    continue;
                }

                // Over a thinner shell, as a rigid zone is: a thousand over the slab. A strip of
                // the same slab drawn over it is still slab.
                bool stacked = slabs.Any(other => !ReferenceEquals(other, shell)
                                                  && other.ComparedThickness < shell.ComparedThickness
                                                  && other.Plan.Bounds.IntersectsWith(shell.Plan.Bounds)
                                                  && !PlanRegionQuick.Disjoint(shell.Plan, other.Plan));
                if (stacked)
                {
                    yield return shell;
                }
            }
        }

        /// <summary>
        /// Accepts every finding the slab pass made inside a column or a wall, from
        /// <paramref name="from"/> on in the zone list. A piece that agrees is left alone, and so
        /// is one that could not be worked out: that is not a difference between the models.
        /// </summary>
        private void AcceptInsideSupports(int from, IList<SlabSupport> supports)
        {
            if (supports.Count == 0)
            {
                return;
            }

            for (int i = from; i < zones.Count; i++)
            {
                SlabZone zone = zones[i];
                if (!IsAcceptableInsideSupport(zone.State))
                {
                    continue;
                }

                SlabSupport holding = SlabSupports.Holding(zone.Plan, supports);
                if (holding == null)
                {
                    continue;
                }

                zone.InsideSupport = holding.Name;
                zone.BuildResults();
            }
        }

        private static bool IsAcceptableInsideSupport(SlabZoneState state)
        {
            switch (state)
            {
                case SlabZoneState.ThicknessMismatch:
                case SlabZoneState.ThicknessReview:
                case SlabZoneState.MissingInRevit:
                case SlabZoneState.MissingInEtabs:
                case SlabZoneState.OpeningInRevitOnly:
                case SlabZoneState.OpeningInEtabsOnly:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>An area typed in m2, in the mm squared the rest of the check works in.</summary>
        private static double SquareMetres(double metres)
        {
            return metres > 0 ? metres * 1e6 : 0;
        }

        private static bool IsOnStory(string elementStory, string storyName)
        {
            return string.Equals(elementStory, storyName, StringComparison.OrdinalIgnoreCase);
        }

        private static double EstimateStoryElevation(IList<Slab_E> etabsSlabs, IList<Slab_R> revitSlabs,
                                                     string storyName)
        {
            List<double> elevations = etabsSlabs.Where(s => IsOnStory(s.Story, storyName))
                .Select(s => s.TopElevation)
                .Concat(revitSlabs.Where(s => IsOnStory(s.Story, storyName)).Select(s => s.TopElevation))
                .ToList();

            return elevations.Count == 0 ? 0 : elevations.Average();
        }

        #region one story

        private void CompareStory(string storyName, double storyElevation,
                                  IList<Slab_E> etabsOnStory, IList<Slab_R> revitOnStory,
                                  IList<Slab_R> allRevitSlabs)
        {
            // The load areas are no slab: no thickness to measure and no plan of their own. The
            // slab check never sees them, and the load check reads them as load on whatever
            // slab they are laid over - see BuildBaseMesh.
            List<Slab_E> shells = etabsOnStory.Where(slab => !slab.IsLoadOnly).ToList();
            List<Slab_E> loadAreas = etabsOnStory.Where(slab => slab.IsLoadOnly && slab.Plan != null).ToList();

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            List<SlabSupport> standing = ColumnsAndWallsAt(storyName);
            HashSet<Slab_E> supports = StackedStiffAreas(shells);
            supports.UnionWith(ShellsInsideSupports(shells, standing));

            // The columns and walls fill the holes they cut in the Revit floors the same as a
            // stiff area does: the slab runs on through them in ETABS, and there is no opening.
            List<PlanPolygon> holeFillers = supports.Select(support => support.Plan)
                .Concat(standing.Select(support => support.Plan))
                .ToList();
            SlabCoverage coverage = BuildCoverage(storyName, storyElevation, allRevitSlabs, holeFillers);
            CheckTiming.Add("reading what the Revit floors declare", watch);

            if (!LoadsOnly)
            {
                int firstZone = zones.Count;
                CompareSlab(storyName, shells, supports, coverage);
                AcceptInsideSupports(firstZone, standing);
            }

            // Pass two: the loads. A second mesh over the same areas, cut along the loading
            // plan's regions instead, and only for the levels a plan was paired with. The
            // Revit floors are not consulted at all - a shell with no floor over it can still
            // be carrying the wrong load, and that is a load finding either way.
            LoadCoverage loads = LoadsFor(storyName);
            if (loads == null)
            {
                return;
            }

            SlabMeshCompare loadCompare = new SlabMeshCompare(loadZones, issues)
            {
                Purpose = SlabMeshPurpose.Loads,
                ThicknessTolerance = thicknessTolerance,
                MinimumZoneArea = minimumZoneArea,
                MinimumZoneWidth = minimumZoneWidth,
                MinimumIssueArea = loadIssueArea,
                EdgeTolerance = edgeTolerance,
                ShouldStop = StopNow,
                Loads = loads,
                LoadTolerance = loadTolerance,
                LoadPatterns = loadPatterns,

                // Every shell on the floor, so a piece of plan with two of them over it is
                // read as carrying both. The mesh hands each piece to one shell; what the
                // slab underneath carries is the sum of the shells standing on it - the load
                // areas laid over it included.
                ShellsOnStory = etabsOnStory
            };

            watch = System.Diagnostics.Stopwatch.StartNew();
            loadCompare.CompareFaces(storyName, BuildBaseMesh(storyName, shells, supports, loadAreas), coverage);
            CheckTiming.Add("cutting each ETABS area along the loading plan regions", watch);

            ReportLoadAreasOverNoSlab(storyName, shells, loadAreas);
        }

        /// <summary>
        /// A load area with no slab under any of it carries a load the check has nowhere to put:
        /// the plan under it is not a piece of any slab. Said, so the load is not lost in
        /// silence - which is what reading the load areas at all is for.
        /// </summary>
        private void ReportLoadAreasOverNoSlab(string storyName, IList<Slab_E> shells, IList<Slab_E> loadAreas)
        {
            List<Slab_E> slabs = shells.Where(slab => !slab.IsOpening && slab.Plan != null).ToList();

            List<string> overNothing = loadAreas
                .Where(area => !slabs.Any(slab => slab.Plan.Bounds.IntersectsWith(area.Plan.Bounds)
                                                  && !PlanRegionQuick.Disjoint(slab.Plan, area.Plan)))
                .Select(area => area.UniqueName)
                .ToList();

            if (overNothing.Count > 0)
            {
                issues.Add($"{storyName}: {overNothing.Count} ETABS load area(s) carry load but no slab is " +
                           $"drawn under them, so their load is not checked against the loading plan: " +
                           string.Join(", ", overNothing.Take(20)) + (overNothing.Count > 20 ? ", ..." : "") + ".");
            }
        }

        /// <summary>
        /// Pass one: the thickness, outline and opening zones for one story.
        /// </summary>
        private void CompareSlab(string storyName, IList<Slab_E> etabsOnStory, HashSet<Slab_E> supports,
                                 SlabCoverage coverage)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            List<EtabsFace> baseMesh = BuildBaseMesh(storyName, etabsOnStory, supports);
            CheckTiming.Add("cutting the ETABS areas into the base mesh", watch);

            // Pass one: the slab itself. Cut along the Revit floor edges, and told nothing
            // about the loading plan, so a loading region boundary can no longer cut a slab of
            // one depth into pieces.
            SlabMeshCompare compare = new SlabMeshCompare(zones, issues)
            {
                Purpose = SlabMeshPurpose.Slab,
                ThicknessTolerance = thicknessTolerance,
                MinimumZoneArea = minimumZoneArea,
                MinimumZoneWidth = minimumZoneWidth,
                MinimumIssueArea = thicknessIssueArea,
                EdgeTolerance = edgeTolerance,
                ShouldStop = StopNow,
                LoadTolerance = loadTolerance,
                LoadPatterns = loadPatterns
            };

            watch = System.Diagnostics.Stopwatch.StartNew();
            compare.CompareFaces(storyName, baseMesh, coverage);
            CheckTiming.Add("cutting each ETABS area along the Revit slab edges", watch);

            watch = System.Diagnostics.Stopwatch.StartNew();
            compare.ReportRevitOnly(storyName, baseMesh, coverage);
            CheckTiming.Add("finding Revit floor with no ETABS area over it", watch);
        }

        /// <summary>
        /// The Revit floors that could put concrete at this story, handed over whole.
        ///
        /// Whole is the change. This used to be a depth field: the floors were cut against
        /// each other with the geometry kernel until every patch of plan belonged to exactly
        /// one region carrying one depth, and that cutting was most of what the check did. The
        /// depth at a point does not need it. Floors are allowed to overlap here, and where
        /// they do the point decides, so nothing is cut and nothing has to be.
        ///
        /// A floor is taken when its top sits at the story plane, which is what an ETABS area
        /// is drawn to, or when it sits below one within reach of a drop - those are the floors
        /// a stack can run down into.
        ///
        /// The holes the columns and walls cut in a floor are filled back in - see
        /// <see cref="WithoutSupportHoles"/> - for the supports the ETABS model marks with a
        /// stiff area, which are the only ones it says are there.
        /// </summary>
        private SlabCoverage BuildCoverage(string storyName, double storyElevation, IList<Slab_R> allRevitSlabs,
                                           IList<PlanPolygon> supports)
        {
            SlabCoverage coverage = new SlabCoverage
            {
                AddStackedDepths = !PrefrencesData.getPrefrencesData().ReadSlabThicknessFromType
            };

            foreach (Slab_R slab in allRevitSlabs)
            {
                if (slab == null || slab.Plan == null)
                {
                    continue;
                }

                // Within the buffer of the plane, and not named after another story. A floor
                // between two stories is the slab of the one it is nearer: a band 2.6 m over
                // this plane belongs to the floor above, and read here it stood in for the
                // slab under it - a 600 band where Revit has a 240 slab.
                bool atPlane = Math.Abs(slab.TopElevation - storyElevation) <= topElevationTolerance
                               && (string.IsNullOrEmpty(slab.Story) || IsOnStory(slab.Story, storyName));

                if (!atPlane)
                {
                    if (slab.TopElevation > storyElevation)
                    {
                        continue;
                    }
                    if (storyElevation - slab.TopElevation > stackSearchDepth)
                    {
                        continue;
                    }
                }

                // One floor, but as many regions as it covers. A shape edited floor arrives as
                // a facet per slope and a floor with an opening on its edge as a slit outline
                // that had to be cut apart, and every piece of it is still the floor.
                foreach (PlanPolygon part in slab.Parts)
                {
                    PlanPolygon plan = WithoutSupportHoles(part, supports);

                    // The holes left open are filled back in only where a column or wall stands
                    // in them, so what is left of each is the opening it really is.
                    IEnumerable<PlanPolygon> regions = new[] { plan }.Concat(SupportPatches(plan, supports));

                    foreach (PlanPolygon region in regions)
                    {
                        coverage.Add(new SlabCoverage.Region
                        {
                            Plan = region,
                            Thickness = slab.ComparedThickness,
                            // A floor read off its sketch with something cutting it that could
                            // not be taken out is put up for review, never passed or failed.
                            Variable = slab.ComparedThicknessVaries || slab.OpeningsUncertain,
                            RevitId = slab.RevitID,
                            TypeName = slab.TypeName,
                            Source = slab.GeometrySource,
                            OpeningsUncertain = slab.OpeningsUncertain,
                            TopElevation = slab.TopElevation,
                            BottomElevation = slab.BottomElevation,
                            AtStoryPlane = atPlane
                        });
                    }
                }

                // The local thickenings found against this floor, each its own region at its own
                // measured depth. Nothing is merged: where they overlap, or overlap the floor,
                // the point decides which is deepest.
                foreach (ThickeningPatch patch in slab.Patches)
                {
                    if (patch.Outcome != ThickeningOutcome.Detected)
                    {
                        continue;
                    }

                    foreach (PlanPolygon footprint in patch.Plans)
                    {
                        coverage.Add(new SlabCoverage.Region
                        {
                            Plan = footprint,
                            Thickness = patch.Thickness,
                            Variable = false,
                            RevitId = slab.RevitID,
                            // No type name at all, so the PT/RC class reads as unknown whatever words
                            // the settings list, and the PT/RC check is skipped for it.
                            TypeName = "",
                            Source = slab.GeometrySource,
                            TopElevation = patch.TopElevation,
                            BottomElevation = patch.BottomElevation,
                            AtStoryPlane = atPlane
                        });
                    }
                }
            }

            return coverage;
        }

        /// <summary>
        /// The floor with the holes the columns and walls standing in it cut filled back in.
        ///
        /// Revit cuts a floor where a column passes through it, and an ETABS or SAFE slab runs
        /// on through the column. Left as it is, the hole reads as floor Revit has not got
        /// around every column the model marks, and the ETABS slab and drop panel beside it are
        /// reported as reaching into it. Filled, the floor carries on through the column the
        /// same as the slab does, and the stiff area standing there says the rest.
        ///
        /// A hole is filled only where the columns, walls and stiff areas standing in it take up
        /// the hole between them - see <see cref="IsSupportHole"/>. Every other hole, a column
        /// the model does not mark included, is left exactly as it was.
        /// </summary>
        internal static PlanPolygon WithoutSupportHoles(PlanPolygon floor, IList<PlanPolygon> supports)
        {
            if (floor == null || floor.Holes.Count == 0 || supports == null || supports.Count == 0)
            {
                return floor;
            }

            List<PlanRing> kept = floor.Holes.Where(hole => !IsSupportHole(hole, supports)).ToList();

            return kept.Count == floor.Holes.Count ? floor : PlanPolygon.Create(floor.Outer, kept);
        }

        /// <summary>
        /// How much of a hole the supports standing in it have to take up, within the margin,
        /// for the whole of it to be filled. Near enough all of it: a hole they only partly take
        /// up is filled where they stand and left open beside them - see <see cref="SupportPatches"/>.
        /// </summary>
        internal const double WholeHoleFraction = 0.98;

        /// <summary>
        /// True where the supports standing in a hole take up all of it but slivers - see
        /// <see cref="SlabSupports.Holding"/>, with <see cref="WholeHoleFraction"/>.
        ///
        /// The two are rarely the same shape. SAFE draws a round column as the square of the
        /// same area, so the round hole reaches past the square's sides by a sliver, and the
        /// margin takes that. What it does not take is an opening that merely has a column or
        /// a wall standing in part of it - a shaft with a column in its corner, a slot running
        /// on past the end of a wall. That used to be filled whole wherever the hole was no
        /// more than twice the area of the support, which a wall a couple of metres long easily
        /// is, and the opening beside it went unreported.
        /// </summary>
        private static bool IsSupportHole(PlanRing hole, IList<PlanPolygon> supports)
        {
            List<SlabSupport> standing = supports
                .Where(support => support != null && hole.Bounds.IntersectsWith(support.Bounds, SlabSupports.Margin))
                .Select(support => new SlabSupport { Plan = support })
                .ToList();

            if (standing.Count == 0)
            {
                return false;
            }

            return SlabSupports.Holding(PlanPolygon.Create(hole, null), standing, WholeHoleFraction) != null;
        }

        /// <summary>
        /// The parts of a floor's open holes that a column or wall stands in, each to be read as
        /// floor: the hole clipped to the support, grown by the margin so a support drawn a
        /// little thinner than the cut it made still takes the whole width of it.
        ///
        /// What is left of the hole is then exactly the opening beside the support - the slot
        /// running on past the end of a wall, the shaft around a column in its corner - and the
        /// part under the support reads as the slab running on through it, the same as a hole
        /// the support takes up whole. A support that is not convex is left out: clipped by a
        /// shape it does not fit, the hole would come back wrong, and left open it is at worst
        /// reported where it might have been filled.
        /// </summary>
        internal static IEnumerable<PlanPolygon> SupportPatches(PlanPolygon floor, IList<PlanPolygon> supports)
        {
            if (floor == null || floor.Holes.Count == 0 || supports == null || supports.Count == 0)
            {
                yield break;
            }

            foreach (PlanRing hole in floor.Holes)
            {
                foreach (PlanPolygon support in supports)
                {
                    if (support == null || !hole.Bounds.IntersectsWith(support.Bounds, SlabSupports.Margin))
                    {
                        continue;
                    }

                    List<MyPoint> clip = PlanClip.Grown(support.Outer.Points, SlabSupports.Margin);
                    if (clip == null)
                    {
                        continue;
                    }

                    List<MyPoint> patch = PlanClip.ToConvex(hole.Points, clip);
                    PlanPolygon region = patch.Count < 3 ? null : PlanPolygon.FromPoints(patch);
                    if (region != null && region.Area > 1)
                    {
                        yield return region;
                    }
                }
            }
        }

        #endregion

        #region the ETABS base mesh

        /// <summary>
        /// The stiff areas drawn over another shell of the story: the columns and walls
        /// standing in the slab.
        ///
        /// SAFE marks every column and every wall under a slab this way, with a small shell a
        /// thousand millimetres thick laid over the slab and the drop panel around it, and an
        /// ETABS model can carry them too. It is the support, not slab, and checked as slab it
        /// read as a thousand millimetres of slab over the hole the column cuts in the Revit
        /// floor, at every column in the building.
        ///
        /// Stacked is the condition, and nothing else about the area is. A stiff area standing
        /// on its own, over no other shell, says nothing about what it stands in for, so it is
        /// checked as the shell it is exactly as before. A shell that only touches it along an
        /// edge does not make it stacked either.
        /// </summary>
        internal static HashSet<Slab_E> StackedStiffAreas(IList<Slab_E> onStory)
        {
            HashSet<Slab_E> stacked = new HashSet<Slab_E>();
            if (onStory == null)
            {
                return stacked;
            }

            // A load area is no slab for a column to stand in.
            List<Slab_E> others = onStory
                .Where(slab => slab != null && !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null
                               && slab.Kind != SlabKind.Stiff)
                .ToList();

            foreach (Slab_E stiff in onStory)
            {
                if (stiff == null || stiff.Kind != SlabKind.Stiff || stiff.IsOpening || stiff.Plan == null)
                {
                    continue;
                }

                if (others.Any(other => !PlanRegionQuick.Disjoint(stiff.Plan, other.Plan)))
                {
                    stacked.Add(stiff);
                }
            }

            return stacked;
        }

        /// <summary>
        /// The ETABS areas as the mesh the check works from.
        ///
        /// Two things are taken out of an area, and both are ETABS telling us there is no slab
        /// of its there rather than us deciding to cut it up: the areas flagged as openings,
        /// and any area that outranks it drawn over it - a drop panel, a load shell, a stiff
        /// area. They are carried with the face as outlines and taken out point by point when
        /// it is triangulated, never cut out beforehand. Everything else is passed straight
        /// through, the same object that came out of the model, so an area nothing overlaps
        /// keeps the exact points it was drawn with.
        ///
        /// The <paramref name="supports"/> - stiff areas stacked on the slab, see
        /// <see cref="StackedStiffAreas"/> - go in as faces like any other, so they cut the
        /// shells they stand in and the Revit floor under them is taken as spoken for. They
        /// are marked, and the thickness pass reports nothing on them.
        ///
        /// The <paramref name="loadAreas"/> - null areas laid over the slab to carry a load,
        /// handed in for the load pass only - are never faces: they own no plan. Each face they
        /// reach over carries them with the shells it stands on, so the load pass cuts it along
        /// their outlines and adds their load where they are.
        /// </summary>
        private List<EtabsFace> BuildBaseMesh(string storyName, IList<Slab_E> etabsOnStory,
                                              HashSet<Slab_E> supports, IList<Slab_E> loadAreas = null)
        {
            List<EtabsFace> faces = new List<EtabsFace>();

            List<PlanPolygon> openings = etabsOnStory
                .Where(slab => slab.IsOpening && slab.Plan != null)
                .Select(slab => slab.Plan)
                .ToList();

            // Smallest first, so each area is only ever cut by the ones that outrank it and the
            // inner shell wins the plan it shares with an outer one.
            //
            // Inner rather than thicker. A drop panel is both - smaller than the slab and
            // deeper than it - so the two rules agree on the case that made this ordering
            // necessary. They part company on a shell that is inner and thinner, which is a
            // recess or a lowered panel, and there the thickness at that patch of plan is the
            // recess rather than the slab it was cut out of. Ordering on the plan says the
            // nearer of two overlapping shells wins whatever it is made of, which is what
            // "on top of each other" means in the model.
            //
            // Thickness then name break the tie for two shells of the same size, so the mesh
            // comes out the same on every run.
            //
            // The supports go before all of it, whatever their size. A wall's stiff strip can
            // be longer than the drop panel it crosses, and it is still the wall standing there
            // rather than slab. Only stacked stiff areas are moved, so the order of every other
            // shell, and so every shell nothing overlaps, is exactly what it was.
            //
            // Then the drops before the slabs, whatever their size. A drop drawn as a long strip
            // across several smaller slab shells is still the local thickening where it crosses
            // them, so it is the drop's depth there, not the slab's.
            List<Slab_E> slabs = etabsOnStory
                .Where(slab => !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null)
                .OrderByDescending(slab => supports.Contains(slab))
                .ThenByDescending(slab => slab.Kind == SlabKind.Drop)
                .ThenBy(slab => slab.Plan.Area)
                .ThenByDescending(slab => slab.ComparedThickness)
                .ThenBy(slab => slab.UniqueName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (int i = 0; i < slabs.Count; i++)
            {
                if (StopNow())
                {
                    break;
                }

                Slab_E slab = slabs[i];
                bool support = supports.Contains(slab);

                List<PlanPolygon> cutters = new List<PlanPolygon>();
                for (int j = 0; j < i; j++)
                {
                    cutters.Add(slabs[j].Plan);
                }
                cutters.AddRange(openings);

                List<PlanPolygon> overlapping = cutters
                    .Where(cutter => cutter != null && slab.Plan.Bounds.IntersectsWith(cutter.Bounds))
                    .ToList();

                ReportCoverageJudgedFromACorner(storyName, slab, overlapping);

                // The shells it is drawn over, which own none of its plan and still put their
                // load on it: loads add, whichever shell is on top. See EtabsFace.StandsOn.
                List<Slab_E> standsOn = new List<Slab_E>();
                for (int j = i + 1; j < slabs.Count; j++)
                {
                    if (slab.Plan.Bounds.IntersectsWith(slabs[j].Plan.Bounds)
                        && !PlanRegionQuick.Disjoint(slab.Plan, slabs[j].Plan))
                    {
                        standsOn.Add(slabs[j]);
                    }
                }

                // And the load areas laid over it, which own none of its plan either and put
                // their load on it all the same.
                if (loadAreas != null)
                {
                    foreach (Slab_E area in loadAreas)
                    {
                        if (slab.Plan.Bounds.IntersectsWith(area.Plan.Bounds)
                            && !PlanRegionQuick.Disjoint(slab.Plan, area.Plan))
                        {
                            standsOn.Add(area);
                        }
                    }
                }

                // Only what genuinely overlaps it. Two shells drawn to the same line share an
                // edge without either reaching into the other, which is the commonest
                // arrangement in the model and no overlap at all.
                List<PlanPolygon> over = overlapping
                    .Where(cutter => !PlanRegionQuick.Disjoint(slab.Plan, cutter))
                    .ToList();

                if (over.Count == 0)
                {
                    // Nothing reaches over this area, so it goes through as drawn.
                    faces.Add(new EtabsFace
                    {
                        Source = slab,
                        Plan = slab.Plan,
                        IsWholeArea = true,
                        IsSupport = support,
                        StandsOn = standsOn
                    });
                    continue;
                }

                // Nothing left of it. Sometimes that is the truth - a slab wholly under a drop
                // panel is checked as the panel - but a blank on the plan is worth a sentence.
                if (over.Any(cutter => PlanRegionQuick.Covers(cutter, slab.Plan)))
                {
                    issues.Add($"{storyName}: {slab.Description} is taken to be completely " +
                               $"covered by the area(s) drawn over it, so nothing of it " +
                               $"is checked on its own. If the plan is blank there, this is why.");

                    DumpWhatCoveredIt(storyName, slab, over);
                    continue;
                }

                // The shells over it are not cut out of it here. They go with it as outlines,
                // and the triangulation that cuts it along the Revit floors gives each point to
                // the shell that outranks the rest there - see EtabsFace.CoveredBy. This used
                // to be one boolean in the geometry kernel against everything over the shell,
                // and a slab carrying a few hundred drops, stiff areas and load shells is
                // exactly what the kernel refuses: one refusal and the slab was checked whole,
                // on top of every drop and load shell drawn over it.
                faces.Add(new EtabsFace
                {
                    Source = slab,
                    Plan = slab.Plan,
                    IsWholeArea = false,
                    IsSupport = support,
                    CoveredBy = over,
                    StandsOn = standsOn
                });
            }

            return faces;
        }

        /// <summary>
        /// Records any area that the old corner test would have called covered and the point
        /// properly inside it does not.
        ///
        /// This is what deleted thirteen areas on a real basement, and no shape written by hand
        /// has reproduced it since - a point exactly on an outline is undefined rather than
        /// wrong, so it depends on the coordinates. Writing the outlines out where it actually
        /// happens is the only way to get a case that can be tested against. Costs nothing
        /// unless the diagnostics dump is switched on, and can come out once there is a test.
        /// </summary>
        private void ReportCoverageJudgedFromACorner(string storyName, Slab_E slab,
                                                     IList<PlanPolygon> overlapping)
        {
            if (!GeometryDump.IsOpen || slab.Plan == null)
            {
                return;
            }

            MyPoint corner = slab.Plan.Outer.Points[0];

            foreach (PlanPolygon cutter in overlapping)
            {
                bool fromCorner = cutter.Bounds.Contains(corner.X, corner.Y)
                                  && cutter.Contains(corner.X, corner.Y);

                if (fromCorner && !PlanRegionQuick.Covers(cutter, slab.Plan))
                {
                    GeometryDump.Fault($"{storyName}: {slab.Description}",
                                       "a corner of this area falls inside the area drawn over it " +
                                       "while the area itself is not covered by it - this is the " +
                                       "arrangement that used to delete it",
                                       $"corner tested was {corner.X:0.###} {corner.Y:0.###}; " +
                                       "region 1 is this area, region 2 is the one over it",
                                       slab.Plan, cutter);
                }
            }
        }

        /// <summary>
        /// Writes the area that was dropped as covered, beside the one that was taken to cover
        /// it, so the judgement can be checked against the outlines rather than believed.
        ///
        /// An area under a drop panel really is covered and really should be dropped, and an
        /// area merely beside another really should not; the two are told apart by geometry
        /// nobody can see from the message alone. Only written when the diagnostics dump is
        /// switched on, and only for the areas this actually happened to.
        /// </summary>
        private void DumpWhatCoveredIt(string storyName, Slab_E slab, IList<PlanPolygon> overlapping)
        {
            if (!GeometryDump.IsOpen)
            {
                return;
            }

            foreach (PlanPolygon cutter in overlapping)
            {
                if (!PlanRegionQuick.Covers(cutter, slab.Plan))
                {
                    continue;
                }

                GeometryDump.Fault($"{storyName}: {slab.Description}",
                                   "taking off the areas drawn over it left nothing, because this " +
                                   "one was judged to cover the whole of it",
                                   "region 1 is the area that was dropped, region 2 is what covered it",
                                   slab.Plan, cutter);
                return;
            }

            // Nothing on its own covered it, so it was the several of them together - or the
            // kernel's answer came back empty. Worth telling apart from the case above.
            GeometryDump.Fault($"{storyName}: {slab.Description}",
                               $"taking off the {overlapping.Count} area(s) drawn over it left nothing, " +
                               "and no single one of them covers it on its own",
                               "region 1 is the area that was dropped",
                               slab.Plan);
        }

        #endregion


    }
}
