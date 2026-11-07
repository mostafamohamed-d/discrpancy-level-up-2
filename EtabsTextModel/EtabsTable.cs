using System;

namespace EtabsTextModel
{
    /// <summary>
    /// The tables this reader knows how to lift out of a model text file.
    ///
    /// One flag per table rather than one call per table, because the assignments live at the
    /// far end of a section holding half a million lines and the file is read once: asking for
    /// two tables must not mean walking 65 MB twice.
    ///
    /// Add a table by adding a flag here, the section header it lives under, and a handler for
    /// its lines. Nothing else in the walk changes.
    /// </summary>
    [Flags]
    public enum EtabsTable
    {
        None = 0,

        /// <summary>
        /// The units the whole file is written in. Always read, whether asked for or not -
        /// every number in the file is in them and means nothing without them.
        /// </summary>
        Controls = 1 << 0,

        /// <summary>What each shell uniform load set is worth: a row per load pattern in it.</summary>
        ShellUniformLoadSets = 1 << 1,

        /// <summary>Which load set each shell carries, by the shell's label and story.</summary>
        ShellLoadSetAssignments = 1 << 2,

        /// <summary>
        /// What is assigned to each area: its section above all, and the pier, spandrel and
        /// diaphragm it belongs to.
        /// </summary>
        AreaAssigns = 1 << 3,
    }

    /// <summary>The section header each table is written under.</summary>
    internal static class Sections
    {
        public const string Controls = "$ CONTROLS";
        public const string ShellUniformLoadSets = "$ SHELL UNIFORM LOAD SETS";
        public const string AreaAssigns = "$ AREA ASSIGNS";

        /// <summary>
        /// Every load on every shell, of every kind - temperature, uniform, and the load set
        /// assignments - in one section. On a large model this is most of the file.
        /// </summary>
        public const string ShellObjectLoads = "$ SHELL OBJECT LOADS";
    }
}
