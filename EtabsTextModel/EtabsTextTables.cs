using System.Collections.Generic;

namespace EtabsTextModel
{
    /// <summary>
    /// What one read of a model text file came back with.
    ///
    /// A table that was asked for and is not in <see cref="Found"/> is not in the file at all,
    /// which is a different thing from a table that is there and empty: a model with no load
    /// sets defined has no load sets section, and reading nothing from it is the right answer
    /// rather than a failure.
    /// </summary>
    public sealed class EtabsTextTables
    {
        internal EtabsTextTables(string path, ModelUnits units, EtabsTable found,
                                 IList<ShellUniformLoadSetRow> loadSets,
                                 IList<ShellLoadSetAssignmentRow> assignments,
                                 IList<AreaAssignRow> areaAssigns,
                                 IList<string> issues)
        {
            Path = path;
            Units = units;
            Found = found;
            ShellUniformLoadSets = loadSets;
            ShellLoadSetAssignments = assignments;
            AreaAssigns = areaAssigns;
            Issues = issues;
        }

        /// <summary>The file this was read from.</summary>
        public string Path { get; }

        /// <summary>What every number in here is in.</summary>
        public ModelUnits Units { get; }

        /// <summary>Which of the tables asked for the file actually has a section for.</summary>
        public EtabsTable Found { get; }

        public IList<ShellUniformLoadSetRow> ShellUniformLoadSets { get; }

        public IList<ShellLoadSetAssignmentRow> ShellLoadSetAssignments { get; }

        /// <summary>One row per area in the model: its section, pier, spandrel and diaphragm.</summary>
        public IList<AreaAssignRow> AreaAssigns { get; }

        /// <summary>
        /// Lines the reader could not make sense of, one message each, capped so a file in a
        /// shape this has never seen says so once rather than half a million times.
        /// </summary>
        public IList<string> Issues { get; }

        public bool Has(EtabsTable table)
        {
            return (Found & table) == table;
        }
    }
}
