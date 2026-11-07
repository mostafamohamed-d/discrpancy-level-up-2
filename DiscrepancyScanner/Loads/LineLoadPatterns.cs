using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Which ETABS load patterns the line load check reads, and which it leaves alone.
    ///
    /// Said outright rather than inferred. The line load schedules these are checked against
    /// are headed SIDL and hold nothing else, so a frame's live load, its wind load and its
    /// seismic load are not what the drawing is asking about - counting them would report a
    /// correctly loaded beam as carrying several times what the plan asks for. The area load
    /// check answers the same question by sorting every pattern into one of four categories,
    /// which it has to because it compares four numbers; this compares one, so it only needs
    /// to know which patterns are that one.
    ///
    /// Matched the way every pattern setting in the tool is matched: whole first, then as a
    /// word inside a longer name, with case, spaces and dashes ignored. Names the settings
    /// exclude are answered before either, which is what keeps SIDL_C - a pattern of its own,
    /// with SIDL sitting inside its name - from being read as the superimposed dead load.
    ///
    /// Left empty it takes every downward distributed load whatever pattern it is in. That is
    /// the same escape hatch the line style setting has, and it exists for the same reason: a
    /// model whose patterns are named in some way nobody anticipated should be able to be
    /// looked at rather than guessed at.
    /// </summary>
    public sealed class LineLoadPatterns
    {
        private readonly HashSet<string> whole = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> partial = new List<string>();
        private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> answered = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<string> named = new List<string>();
        private bool wholeOnly;

        private LineLoadPatterns()
        {
        }

        /// <summary>
        /// The patterns the settings currently name for the line load check - less the SIDL_C
        /// checks' own, which are never the SIDL however their name reads.
        /// </summary>
        public static LineLoadPatterns From(PrefrencesData preferences)
        {
            return From(preferences.LineLoadPatternNames,
                        PrefrencesData.NotSidl(preferences.ExcludedLoadPatternNames, preferences.AllSidlCLoadPatternNames));
        }

        /// <summary>The patterns the settings currently name for the SIDL_C line check.</summary>
        public static LineLoadPatterns SidlC(PrefrencesData preferences)
        {
            return Exactly(preferences.SidlCLoadPatternNames);
        }

        /// <summary>The SIDL_C area check's patterns: its own when it has some, the line check's otherwise.</summary>
        public static LineLoadPatterns SidlCArea(PrefrencesData preferences)
        {
            return PrefrencesData.SplitParameterNames(preferences.SidlCAreaLoadPatternNames).Length > 0
                ? Exactly(preferences.SidlCAreaLoadPatternNames)
                : SidlC(preferences);
        }

        /// <summary>
        /// Patterns matched whole only: case, spaces, dashes and underscores aside, a pattern is
        /// read when it is one of these names and not when one of them sits inside its name.
        /// For a pattern named outright, where SIDL_C must not also catch SIDL_C2.
        /// </summary>
        public static LineLoadPatterns Exactly(string names)
        {
            LineLoadPatterns patterns = From(names);
            patterns.wholeOnly = true;
            return patterns;
        }

        public static LineLoadPatterns From(string names, string neverNames = "")
        {
            LineLoadPatterns patterns = new LineLoadPatterns();

            foreach (string name in PrefrencesData.SplitParameterNames(names))
            {
                string key = Key(name);
                if (key.Length == 0)
                {
                    continue;
                }

                patterns.named.Add(name.Trim());
                patterns.whole.Add(key);
                patterns.partial.Add(key);
            }

            // The longest first, so a longer name is tried before a shorter one sitting inside it.
            patterns.partial.Sort((a, b) => b.Length.CompareTo(a.Length));

            // A name this list gives outright is read whatever the never list says: the never
            // list is there to stop SIDL_C being found inside a longer name, not to overrule
            // a pattern the settings ask for by name.
            foreach (string name in PrefrencesData.SplitParameterNames(neverNames))
            {
                string key = Key(name);
                if (key.Length != 0 && !patterns.whole.Contains(key))
                {
                    patterns.excluded.Add(key);
                }
            }

            return patterns;
        }

        /// <summary>
        /// True when the settings name no pattern at all, in which case every downward
        /// distributed load is read whatever pattern it is in.
        /// </summary>
        public bool IsEmpty { get { return named.Count == 0; } }

        /// <summary>The names being matched on, as they were typed, for reporting.</summary>
        public IList<string> Names { get { return named; } }

        /// <summary>True when a load in this pattern is one the check reads.</summary>
        public bool Matches(string patternName)
        {
            if (IsEmpty)
            {
                return true;
            }

            string key = Key(patternName);
            if (key.Length == 0)
            {
                return false;
            }

            bool already;
            if (answered.TryGetValue(key, out already))
            {
                return already;
            }

            bool matches;

            if (excluded.Contains(key))
            {
                matches = false;
            }
            else if (whole.Contains(key))
            {
                matches = true;
            }
            else if (wholeOnly)
            {
                matches = false;
            }
            else
            {
                matches = partial.Any(name => key.IndexOf(name, StringComparison.Ordinal) >= 0);
            }

            answered[key] = matches;
            return matches;
        }

        /// <summary>One line naming what is being read, for the report.</summary>
        public string Description
        {
            get
            {
                return IsEmpty
                    ? "every load pattern, because none is named in the settings"
                    : string.Join(", ", named);
            }
        }

        /// <summary>
        /// A pattern name as it is matched: upper case with the spaces, dashes and dots taken
        /// out, so "Super Dead", "SUPER-DEAD" and "superdead" are all the same name.
        /// </summary>
        internal static string Key(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
