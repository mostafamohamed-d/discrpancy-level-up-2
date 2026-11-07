using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One ETABS story paired with the Revit view its loading plan is drawn in and the
    /// schedule sheet its numbers are looked up in.
    ///
    /// The view is held by element id and by name at once. The id is what actually finds it,
    /// and survives the view being renamed; the name is what the panel can say when the id no
    /// longer resolves, which is what happens when the pairing was saved against a different
    /// copy of the project.
    /// </summary>
    public sealed class LoadingPlanLink
    {
        public string Story = "";
        public long ViewId;
        public string ViewName = "";
        public string WorkbookPath = "";
        public string SheetName = "";

        /// <summary>
        /// The sheet of the same workbook the line loads are on, when the project has one.
        ///
        /// A second sheet rather than a second workbook, because that is how these schedules
        /// are kept: one file per project with a tab per part of the building for the area
        /// loads and a tab per part for the line loads. So the file is already named by
        /// <see cref="WorkbookPath"/> and only the tab is left to say.
        ///
        /// Empty is a level whose line loads are not checked, which is not a fault - the area
        /// load check stands on its own and always did.
        /// </summary>
        public string LineLoadSheetName = "";

        public LoadingPlanLink Copy()
        {
            return new LoadingPlanLink
            {
                Story = Story,
                ViewId = ViewId,
                ViewName = ViewName,
                WorkbookPath = WorkbookPath,
                SheetName = SheetName,
                LineLoadSheetName = LineLoadSheetName
            };
        }

        public override string ToString()
        {
            return $"{Story} -> {ViewName} / {SheetName}";
        }
    }

    /// <summary>
    /// Which loading plan goes with which floor, kept between sessions.
    ///
    /// Pairing a floor with a plan is a decision about the project rather than about the run,
    /// and there are as many of them as there are floors, so making it again every session
    /// would be most of the work of using the check at all. It is written into the settings
    /// file under the user's profile, keyed by project first so two projects open in the same
    /// Revit do not read each other's pairings.
    ///
    /// The pairing is the user's to make and to change: nothing here ever guesses one. Working
    /// it out from the names is a later job, and this is the shape it will fill in.
    /// </summary>
    public sealed class LoadingPlanLinks
    {
        private readonly Dictionary<string, Dictionary<string, LoadingPlanLink>> byProject =
            new Dictionary<string, Dictionary<string, LoadingPlanLink>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Reads the pairings out of the view branch of the settings file.</summary>
        public static LoadingPlanLinks From(IDictionary<string, object> view)
        {
            LoadingPlanLinks links = new LoadingPlanLinks();
            links.ReadFrom(view);
            return links;
        }

        /// <summary>
        /// Adds whatever the settings file holds to what is here already. A pairing in the
        /// file replaces one made this session for the same floor, which only happens on the
        /// one read at start up, before anything has been paired.
        /// </summary>
        public void ReadFrom(IDictionary<string, object> view)
        {
            LoadingPlanLinks links = this;

            Dictionary<string, object> projects = Json.Branch(view, Branch);

            foreach (KeyValuePair<string, object> project in projects)
            {
                Dictionary<string, object> stories = project.Value as Dictionary<string, object>;
                if (stories == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> story in stories)
                {
                    Dictionary<string, object> stored = story.Value as Dictionary<string, object>;
                    if (stored == null)
                    {
                        continue;
                    }

                    links.Set(project.Key, new LoadingPlanLink
                    {
                        Story = story.Key,
                        ViewId = (long)Json.GetDouble(stored, "viewId", 0),
                        ViewName = Json.GetString(stored, "viewName", ""),
                        WorkbookPath = Json.GetString(stored, "workbook", ""),
                        SheetName = Json.GetString(stored, "sheet", ""),
                        LineLoadSheetName = Json.GetString(stored, "lineSheet", "")
                    });
                }
            }
        }

        /// <summary>The pairings as they go into the settings file.</summary>
        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> projects = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, LoadingPlanLink>> project in
                     byProject.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Dictionary<string, object> stories = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (LoadingPlanLink link in project.Value.Values
                             .OrderBy(link => link.Story, StringComparer.Ordinal))
                {
                    stories[link.Story] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "viewId", (double)link.ViewId },
                        { "viewName", link.ViewName ?? "" },
                        { "workbook", link.WorkbookPath ?? "" },
                        { "sheet", link.SheetName ?? "" },
                        { "lineSheet", link.LineLoadSheetName ?? "" }
                    };
                }

                if (stories.Count > 0)
                {
                    projects[project.Key] = stories;
                }
            }

            return projects;
        }

        /// <summary>The pairing saved for one floor of one project, or null when there is none.</summary>
        public LoadingPlanLink Find(string project, string story)
        {
            Dictionary<string, LoadingPlanLink> stories;
            LoadingPlanLink link;

            if (Key(project).Length > 0 && Key(story).Length > 0
                && byProject.TryGetValue(Key(project), out stories)
                && stories.TryGetValue(Key(story), out link))
            {
                return link.Copy();
            }

            return null;
        }

        /// <summary>Remembers a pairing, replacing whatever that floor was paired with before.</summary>
        public void Set(string project, LoadingPlanLink link)
        {
            if (link == null || Key(project).Length == 0 || Key(link.Story).Length == 0)
            {
                return;
            }

            Dictionary<string, LoadingPlanLink> stories;
            if (!byProject.TryGetValue(Key(project), out stories))
            {
                stories = new Dictionary<string, LoadingPlanLink>(StringComparer.OrdinalIgnoreCase);
                byProject[Key(project)] = stories;
            }

            stories[Key(link.Story)] = link.Copy();
        }

        /// <summary>Forgets one floor's pairing.</summary>
        public void Remove(string project, string story)
        {
            Dictionary<string, LoadingPlanLink> stories;
            if (Key(project).Length > 0 && byProject.TryGetValue(Key(project), out stories))
            {
                stories.Remove(Key(story));
            }
        }

        /// <summary>
        /// Forgets every level's pairing on one project, and answers how many levels were
        /// paired. Other projects are left alone.
        /// </summary>
        public int ClearProject(string project)
        {
            Dictionary<string, LoadingPlanLink> stories;
            if (Key(project).Length == 0 || !byProject.TryGetValue(Key(project), out stories))
            {
                return 0;
            }

            int count = stories.Count;
            byProject.Remove(Key(project));
            return count;
        }

        /// <summary>Every pairing made for one project, in level order.</summary>
        public IEnumerable<LoadingPlanLink> ForProject(string project)
        {
            Dictionary<string, LoadingPlanLink> stories;
            if (Key(project).Length == 0 || !byProject.TryGetValue(Key(project), out stories))
            {
                return new LoadingPlanLink[0];
            }

            return stories.Values.OrderBy(link => link.Story, StringComparer.Ordinal)
                                 .Select(link => link.Copy())
                                 .ToList();
        }

        /// <summary>
        /// A loading schedule workbook this project is already using, or an empty string when
        /// none of its levels has one yet.
        ///
        /// Read out of the store rather than remembered as the last file picked, so it cannot
        /// drift from what the pairings actually say. A project has one loading schedule, so
        /// this saves picking the same file once per floor; the sheet inside it is another
        /// matter and is never carried over.
        /// </summary>
        public string AnyWorkbook(string project)
        {
            foreach (LoadingPlanLink link in ForProject(project))
            {
                if (link.WorkbookPath.Length > 0)
                {
                    return link.WorkbookPath;
                }
            }

            return "";
        }

        /// <summary>
        /// Whether both halves of a pairing have been said: which view the loading plan is
        /// drawn in, and which sheet of the schedule its numbers mean something on. Half a
        /// pairing is not a fault, it is a level somebody has not finished pairing yet.
        /// </summary>
        public static bool IsUsable(LoadingPlanLink link)
        {
            return link != null && link.ViewId != 0
                   && (link.WorkbookPath ?? "").Length > 0
                   && (link.SheetName ?? "").Length > 0;
        }

        /// <summary>
        /// Whether this pairing says enough to check the line loads: the view they are drawn
        /// in and the sheet their numbers mean something on.
        ///
        /// Held apart from <see cref="IsUsable"/> because the two checks are paired
        /// separately and a level is commonly set up for one and not the other. Asking one
        /// question for both would either switch the line loads on for levels nobody gave a
        /// sheet, or switch the area loads off for levels that have one.
        /// </summary>
        public static bool IsUsableForLineLoads(LoadingPlanLink link)
        {
            return link != null && link.ViewId != 0
                   && (link.WorkbookPath ?? "").Length > 0
                   && (link.LineLoadSheetName ?? "").Length > 0;
        }

        /// <summary>
        /// The pairings a run will actually read: every usable one when the check runs over
        /// every level, and the one checked level's otherwise.
        ///
        /// This is what the check running over every level means, so it is worth being able
        /// to say plainly: a run over every level reads every pairing that has been finished,
        /// and levels nobody paired are checked for thickness and outline the way they always
        /// were. It used to read none of them, which looked exactly like the load check being
        /// broken to anyone who had paired six levels and pressed run.
        /// </summary>
        public List<LoadingPlanLink> UsableFor(string project, bool everyStory, string oneStory)
        {
            return Candidates(project, everyStory, oneStory).Where(IsUsable).ToList();
        }

        /// <summary>The pairings a run will read for the line load check, the same way.</summary>
        public List<LoadingPlanLink> UsableForLineLoads(string project, bool everyStory, string oneStory)
        {
            return Candidates(project, everyStory, oneStory).Where(IsUsableForLineLoads).ToList();
        }

        /// <summary>
        /// The views more than one level of this project reads its line loads off: a typical
        /// floor's loading plan, paired with every level built to it.
        ///
        /// Asked of every pairing the project has rather than of the ones a run reads, because
        /// it decides what a finding is filed under. Asked of the run, the same line on the
        /// same level would be filed under one name when that level is checked on its own and
        /// under another when every level is, and a review made one way would not be found
        /// the other.
        /// </summary>
        public HashSet<long> ViewsSharedForLineLoads(string project)
        {
            return new HashSet<long>(ForProject(project)
                .Where(IsUsableForLineLoads)
                .GroupBy(link => link.ViewId)
                .Where(levels => levels.Count() > 1)
                .Select(levels => levels.Key));
        }

        private IEnumerable<LoadingPlanLink> Candidates(string project, bool everyStory, string oneStory)
        {
            return everyStory
                ? ForProject(project)
                : new[] { string.IsNullOrEmpty(oneStory) ? null : Find(project, oneStory) };
        }

        /// <summary>How many floors of this project have a loading plan paired with them.</summary>
        public int CountFor(string project)
        {
            Dictionary<string, LoadingPlanLink> stories;
            return Key(project).Length > 0 && byProject.TryGetValue(Key(project), out stories)
                ? stories.Count
                : 0;
        }

        private static string Key(string value)
        {
            return (value ?? "").Trim();
        }

        /// <summary>Where the pairings sit in the settings file.</summary>
        public const string Branch = "loadingPlans";
    }
}
