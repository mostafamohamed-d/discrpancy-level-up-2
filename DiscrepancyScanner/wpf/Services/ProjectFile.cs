using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// A file carrying everything decided by hand on one project - the alignment, the SAFE
    /// level, the loading plan pairings, the pairings set by hand, the reviews, acceptances and
    /// notes, and the height zones - so it can go to a colleague, to another machine, or into
    /// the job folder beside the models.
    ///
    /// Made of the settings file's own branches, cut down to the one project: each is written
    /// and read back by the store it belongs to, so nothing here knows how any of them is
    /// shaped, and a thing added to one of those stores later travels in the file without this
    /// being touched.
    ///
    /// Read into whatever project is open, which need not be where it was made: the same model
    /// saved somewhere else is still the same model, and what was decided about it still holds.
    /// </summary>
    internal static class ProjectFile
    {
        /// <summary>What the file says it is, so a settings file or anything else is not taken for one.</summary>
        public const string Format = "Discrepancy Scanner project";

        public const string Extension = ".dsproject";

        public const string AlignmentBranch = "alignments";

        /// <summary>The branches of the settings file kept per project - which is what a project is.</summary>
        public static readonly string[] Branches =
        {
            AlignmentBranch,
            LoadingPlanLinks.Branch,
            PairingOverrides.Branch,
            PairingOverrides.PlacesBranch,
            ReviewNotes.Branch,
            HeightZones.Branch,
            ScheduleColumnOverrides.Branch
        };

        /// <summary>One project's share of every branch, as a file.</summary>
        public static Dictionary<string, object> Export(IDictionary<string, object> view, string project,
                                                        string name, DateTime when)
        {
            Dictionary<string, object> data = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string branch in Branches)
            {
                object value = ProjectIn(Json.Branch(view, branch), project);
                if (value != null)
                {
                    data[branch] = value;
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "format", Format },
                { "version", 1d },
                { "project", name ?? "" },
                { "exported", when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) },
                { "data", data }
            };
        }

        /// <summary>What a project file holds, read and counted for the question asked before it goes in.</summary>
        public sealed class Contents
        {
            public string Project = "";
            public string Exported = "";
            public Dictionary<string, object> Data = new Dictionary<string, object>(StringComparer.Ordinal);

            public bool HasAlignment => Data.ContainsKey(AlignmentBranch);
            public int LoadingPlans => Json.Branch(Data, LoadingPlanLinks.Branch).Count;
            public int HandPairings => Json.Branch(Data, PairingOverrides.Branch).Count;
            public int Decisions => Json.Branch(Data, ReviewNotes.Branch).Count;

            public int HeightZones
            {
                get
                {
                    object zones;
                    return Data.TryGetValue(DiscrepancyScanner.HeightZones.Branch, out zones)
                        ? (zones as List<object>)?.Count ?? 0
                        : 0;
                }
            }

            /// <summary>What is in it, a line a thing, for the question asked before it goes in.</summary>
            public IList<string> Lines()
            {
                List<string> lines = new List<string>();
                if (HasAlignment) lines.Add("the alignment of the Revit model");
                lines.Add(Count(LoadingPlans, "level") + " paired with a loading plan");
                lines.Add(Count(HandPairings, "pairing") + " set by hand");
                lines.Add(Count(Decisions, "element") + " reviewed, accepted or noted");
                lines.Add(Count(HeightZones, "height zone"));
                return lines;
            }

            private static string Count(int count, string thing)
            {
                return count + " " + thing + (count == 1 ? "" : "s");
            }
        }

        /// <summary>Reads a project file; false, with why, for anything that is not one.</summary>
        public static bool TryRead(IDictionary<string, object> file, out Contents contents, out string problem)
        {
            contents = null;
            problem = null;

            if (file == null || !string.Equals(Json.GetString(file, "format", ""), Format, StringComparison.Ordinal))
            {
                problem = "This is not a project file exported from the discrepancy check.";
                return false;
            }

            contents = new Contents
            {
                Project = Json.GetString(file, "project", ""),
                Exported = Json.GetString(file, "exported", ""),
                Data = new Dictionary<string, object>(Json.Branch(file, "data"), StringComparer.Ordinal)
            };
            return true;
        }

        /// <summary>
        /// Puts a file's project into a settings view as the given project, in place of whatever
        /// that project had there - so a pairing, a note or a zone the file does not have is gone
        /// afterwards, and the project reads exactly as it was exported. Only the alignment is
        /// left as it was where the file has none: a project nobody aligned is not a project
        /// whose alignment is to be undone.
        /// </summary>
        public static void Import(IDictionary<string, object> view, string project, IDictionary<string, object> data)
        {
            foreach (string branch in Branches)
            {
                object value;
                bool has = data.TryGetValue(branch, out value) && value != null;
                if (branch == AlignmentBranch && !has)
                {
                    continue;
                }

                Dictionary<string, object> replaced =
                    new Dictionary<string, object>(Json.Branch(view, branch), StringComparer.Ordinal);

                foreach (string key in replaced.Keys
                             .Where(key => string.Equals(key, project, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    replaced.Remove(key);
                }

                if (has)
                {
                    replaced[project] = value;
                }

                view[branch] = replaced;
            }
        }

        /// <summary>A project's entry in a branch, matched the way the stores match it - ignoring case.</summary>
        private static object ProjectIn(Dictionary<string, object> branch, string project)
        {
            foreach (KeyValuePair<string, object> entry in branch)
            {
                if (string.Equals(entry.Key, project, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Value;
                }
            }
            return null;
        }
    }
}
