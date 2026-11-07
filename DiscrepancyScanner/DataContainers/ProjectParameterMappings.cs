using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The size parameter mappings, kept per project rather than once for every model.
    ///
    /// One office's columns say b and h, another's Width and Depth, and the two are open in the
    /// same week. Held once, mapping the second project rewrote the first. Here each project -
    /// filed under the same key its alignment is, the document path or a detached copy's name -
    /// keeps its own, and the settings show the mapping of the project that is open.
    ///
    /// A project seen for the first time starts with no mapping at all: nothing is guessed, so
    /// its sizes are measured or reported until they are mapped. The one exception is the very
    /// first time this is used, when no project has a mapping yet: the project open then takes
    /// the mapping the settings already held, so nobody's existing mapping is lost.
    /// </summary>
    public sealed class ProjectParameterMappings
    {
        public const string Branch = "parameterMappings";

        /// <summary>The settings each project keeps for itself.</summary>
        public static readonly string[] Keys =
        {
            nameof(PrefrencesData.ColumnWidthParameterNames),
            nameof(PrefrencesData.ColumnDepthParameterNames),
            nameof(PrefrencesData.ColumnDiameterParameterNames),
            nameof(PrefrencesData.BeamWidthParameterNames),
            nameof(PrefrencesData.BeamDepthParameterNames)
        };

        private readonly Dictionary<string, Dictionary<string, string>> byProject =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>Whether the settings file had the branch at all - false before first use.</summary>
        private bool everStored;

        public void ReadFrom(IDictionary<string, object> branch, bool branchPresent)
        {
            byProject.Clear();
            everStored = branchPresent;
            if (branch == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> project in branch)
            {
                IDictionary<string, object> stored = project.Value as IDictionary<string, object>;
                if (stored == null)
                {
                    continue;
                }

                Dictionary<string, string> mapping = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string key in Keys)
                {
                    object value;
                    mapping[key] = stored.TryGetValue(key, out value) ? value as string ?? "" : "";
                }
                byProject[project.Key] = mapping;
            }
        }

        public bool Has(string project)
        {
            return !string.IsNullOrEmpty(project) && byProject.ContainsKey(project);
        }

        /// <summary>
        /// Puts the project's mapping into the settings: its own if it has one, the settings'
        /// current one if no project has ever had one, and an empty one otherwise.
        /// </summary>
        public void ApplyTo(PrefrencesData preferences, string project)
        {
            if (preferences == null || string.IsNullOrEmpty(project))
            {
                return;
            }

            Dictionary<string, string> mapping;
            if (byProject.TryGetValue(project, out mapping))
            {
                foreach (string key in Keys)
                {
                    Set(preferences, key, mapping[key]);
                }
                return;
            }

            if (!everStored)
            {
                return;
            }

            foreach (string key in Keys)
            {
                Set(preferences, key, "");
            }
        }

        /// <summary>Files the settings' current mapping under the project, leaving every other one alone.</summary>
        public void StoreFrom(PrefrencesData preferences, string project)
        {
            if (preferences == null || string.IsNullOrEmpty(project))
            {
                return;
            }

            Dictionary<string, string> mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in Keys)
            {
                mapping[key] = Get(preferences, key);
            }
            byProject[project] = mapping;
            everStored = true;
        }

        /// <summary>
        /// What to write back: every project's mapping as the file holds it now, with only
        /// <paramref name="project"/>'s replaced by this one's. Another window - a second Revit
        /// session - may have saved its own project since this one was read, and writing back
        /// the whole set as it was loaded would undo that.
        /// </summary>
        public Dictionary<string, object> ToJsonOver(IDictionary<string, object> onDisk, string project)
        {
            ProjectParameterMappings current = new ProjectParameterMappings();
            current.ReadFrom(onDisk, onDisk != null);

            Dictionary<string, string> mine;
            if (!string.IsNullOrEmpty(project) && byProject.TryGetValue(project, out mine))
            {
                current.byProject[project] = mine;
            }
            return current.ToJson();
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> json = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Dictionary<string, string>> project in byProject)
            {
                Dictionary<string, object> mapping = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> entry in project.Value)
                {
                    mapping[entry.Key] = entry.Value;
                }
                json[project.Key] = mapping;
            }
            return json;
        }

        private static string Get(PrefrencesData preferences, string key)
        {
            return typeof(PrefrencesData).GetField(key)?.GetValue(preferences) as string ?? "";
        }

        private static void Set(PrefrencesData preferences, string key, string value)
        {
            typeof(PrefrencesData).GetField(key)?.SetValue(preferences, value ?? "");
        }
    }
}
