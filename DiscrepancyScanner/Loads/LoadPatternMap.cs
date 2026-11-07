using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Which of the four loads an ETABS load pattern is.
    ///
    /// Pattern names are the model author's own - SDL, SIDL, Super Dead; LL, Live; LR, LLR,
    /// Roof Live; MEP, MECH - so the names are a setting rather than a list in the code, and a
    /// pattern the settings do not name is left out of the check rather than guessed at.
    ///
    /// The names are matched whole first, across all four at once, and only then as a word
    /// inside a longer name. Doing it the other way round would let "LL" claim "LLR": one pass
    /// per category in turn cannot be made safe, because whether it is safe depends on the
    /// order the categories happen to be tried in.
    ///
    /// Names the settings exclude are answered before either pass, because the match inside a
    /// longer name is what they are there to stop: SIDL_C is a pattern of its own and not the
    /// SIDL, but "SIDL" is sitting inside its name all the same.
    /// </summary>
    public static class LoadPatternMap
    {
        /// <summary>
        /// The pattern names to look for, ready to be asked about a pattern. Built once per
        /// check rather than per area: a model has thousands of areas and a dozen patterns.
        /// </summary>
        public sealed class Mapping
        {
            private readonly Dictionary<string, LoadCategory> whole =
                new Dictionary<string, LoadCategory>(StringComparer.Ordinal);

            private readonly List<KeyValuePair<string, LoadCategory>> partial =
                new List<KeyValuePair<string, LoadCategory>>();

            private readonly Dictionary<string, LoadCategory> answered =
                new Dictionary<string, LoadCategory>(StringComparer.Ordinal);

            private readonly HashSet<string> excluded = new HashSet<string>(StringComparer.Ordinal);

            internal void Add(LoadCategory category, string names)
            {
                foreach (string name in PrefrencesData.SplitParameterNames(names))
                {
                    string key = Key(name);
                    if (key.Length == 0)
                    {
                        continue;
                    }

                    if (!whole.ContainsKey(key))
                    {
                        whole[key] = category;
                    }
                    partial.Add(new KeyValuePair<string, LoadCategory>(key, category));
                }

                // The longest name first, so "LIVEREDUCIBLE" is tried before "LIVE" when
                // neither matched the pattern whole.
                partial.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            }

            /// <summary>
            /// Names that are none of the four however the rest of the matching reads them.
            /// </summary>
            internal void Never(string names)
            {
                foreach (string name in PrefrencesData.SplitParameterNames(names))
                {
                    // Not a pattern one of the four names outright - see LineLoadPatterns.From.
                    string key = Key(name);
                    if (key.Length != 0 && !whole.ContainsKey(key))
                    {
                        excluded.Add(key);
                    }
                }
            }

            /// <summary>
            /// Which of the four this pattern is, or <see cref="LoadCategory.None"/> when the
            /// settings do not name it. Answers are remembered, because the same dozen names
            /// are asked about once per area.
            /// </summary>
            public LoadCategory Categorise(string patternName)
            {
                string key = Key(patternName);
                if (key.Length == 0)
                {
                    return LoadCategory.None;
                }

                LoadCategory already;
                if (answered.TryGetValue(key, out already))
                {
                    return already;
                }

                if (excluded.Contains(key))
                {
                    answered[key] = LoadCategory.None;
                    return LoadCategory.None;
                }

                LoadCategory category;
                if (!whole.TryGetValue(key, out category))
                {
                    category = LoadCategory.None;
                    foreach (KeyValuePair<string, LoadCategory> candidate in partial)
                    {
                        if (key.IndexOf(candidate.Key, StringComparison.Ordinal) >= 0)
                        {
                            category = candidate.Value;
                            break;
                        }
                    }
                }

                answered[key] = category;
                return category;
            }

            /// <summary>The names each of the four is being matched on, for the settings note.</summary>
            public IEnumerable<string> NamesFor(LoadCategory category)
            {
                return partial.Where(pair => pair.Value == category).Select(pair => pair.Key);
            }
        }

        /// <summary>
        /// The mapping the settings currently describe. The SIDL_C checks' patterns are none of
        /// the four, the same as the excluded ones: they are counted by a check of their own.
        /// </summary>
        public static Mapping FromPreferences(PrefrencesData preferences)
        {
            return From(preferences.SdlLoadPatternNames, preferences.LiveLoadPatternNames,
                        preferences.ReducibleLiveLoadPatternNames, preferences.MepLoadPatternNames,
                        PrefrencesData.NotSidl(preferences.ExcludedLoadPatternNames, preferences.AllSidlCLoadPatternNames));
        }

        /// <summary>
        /// The mapping some lists of names would make, whether or not they are the settings yet
        /// - which is how the load pattern window shows what a change does before it is saved.
        /// </summary>
        public static Mapping From(string sdl, string live, string reducibleLive, string mep, string never)
        {
            Mapping mapping = new Mapping();
            mapping.Add(LoadCategory.Sdl, sdl);
            mapping.Add(LoadCategory.Mep, mep);
            mapping.Add(LoadCategory.Lr, reducibleLive);
            mapping.Add(LoadCategory.Ll, live);
            mapping.Never(never);
            return mapping;
        }

        /// <summary>
        /// A pattern name as it is matched: upper case with the spaces, dashes and dots taken
        /// out, so "Super Dead", "SUPER-DEAD" and "superdead" are all the same name.
        /// </summary>
        private static string Key(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            return new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }
    }
}
