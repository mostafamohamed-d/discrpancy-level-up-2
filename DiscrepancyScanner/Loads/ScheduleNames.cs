using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What the loading schedule calls each load: the headings over its columns and the
    /// markings in its narrow live load column.
    ///
    /// The schedule's half of the load pattern mapping - the ETABS half is
    /// <see cref="LoadPatternMap"/>. Both are settings for the same reason: the names are
    /// whoever wrote the document's, and a schedule headed SDL rather than SIDL is the same
    /// schedule written in another office.
    ///
    /// Headings are matched on how they start, the way the reader always matched them, so
    /// "SIDL (kN/m2)" is a SIDL heading. Markings are matched whole first, across all three
    /// at once, and only then on how they start, longest first - so "LL" cannot claim "LLR".
    /// </summary>
    public sealed class ScheduleNames
    {
        private readonly List<string> sidl;
        private readonly List<string> total;
        private readonly List<string> live;
        private readonly Dictionary<string, LoadCategory> markingsWhole =
            new Dictionary<string, LoadCategory>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, LoadCategory>> markingsStart =
            new List<KeyValuePair<string, LoadCategory>>();

        private ScheduleNames(PrefrencesData preferences)
        {
            sidl = Keys(preferences.ScheduleSidlHeadingNames, "SIDL");
            total = Keys(preferences.ScheduleTotalHeadingNames, "TOTAL");
            live = Keys(preferences.ScheduleLiveHeadingNames, "LIVE LOAD");

            // Plain live load first, so where a marking is named twice the more specific rows
            // below take it over.
            AddMarkings(LoadCategory.Ll, preferences.ScheduleLiveMarkings);
            AddMarkings(LoadCategory.Lr, preferences.ScheduleReducibleLiveMarkings);
            AddMarkings(LoadCategory.Mep, preferences.ScheduleMepMarkings);

            markingsStart.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            SidlDisplay = Display(preferences.ScheduleSidlHeadingNames, "SIDL");
            TotalDisplay = Display(preferences.ScheduleTotalHeadingNames, "TOTAL");
            LiveDisplay = Display(preferences.ScheduleLiveHeadingNames, "LIVE LOAD");
        }

        /// <summary>The names the settings currently give.</summary>
        public static ScheduleNames Current
        {
            get { return From(PrefrencesData.getPrefrencesData()); }
        }

        public static ScheduleNames From(PrefrencesData preferences)
        {
            return new ScheduleNames(preferences ?? PrefrencesData.getPrefrencesData());
        }

        /// <summary>The SIDL heading as the settings give it, for saying what was looked for.</summary>
        public string SidlDisplay { get; }

        public string TotalDisplay { get; }

        public string LiveDisplay { get; }

        /// <summary>True for a heading, already normalised, over the superimposed dead load.</summary>
        public bool IsSidlHeading(string normalised)
        {
            return StartsWithAny(normalised, sidl);
        }

        /// <summary>True for a heading, already normalised, over the area schedule's SDL total.</summary>
        public bool IsTotalHeading(string normalised)
        {
            return StartsWithAny(normalised, total);
        }

        /// <summary>True for a heading, already normalised, over the live load.</summary>
        public bool IsLiveHeading(string normalised)
        {
            return StartsWithAny(normalised, live);
        }

        /// <summary>
        /// Which of the three live loads a marking means. Empty, or a marking none of the three
        /// names, is plain live load: the safe way round, and what the schedule means by
        /// leaving the column blank.
        /// </summary>
        public LoadCategory LiveCategory(string marking)
        {
            string key = LoadScheduleReader.Normalised(marking);
            if (key.Length == 0)
            {
                return LoadCategory.Ll;
            }

            LoadCategory category;
            if (markingsWhole.TryGetValue(key, out category))
            {
                return category;
            }

            foreach (KeyValuePair<string, LoadCategory> candidate in markingsStart)
            {
                if (key.StartsWith(candidate.Key, StringComparison.Ordinal))
                {
                    return candidate.Value;
                }
            }

            return LoadCategory.Ll;
        }

        private void AddMarkings(LoadCategory category, string names)
        {
            foreach (string key in Keys(names, ""))
            {
                markingsWhole[key] = category;
                markingsStart.RemoveAll(pair => pair.Key == key);
                markingsStart.Add(new KeyValuePair<string, LoadCategory>(key, category));
            }
        }

        private static bool StartsWithAny(string normalised, List<string> names)
        {
            return !string.IsNullOrEmpty(normalised)
                   && names.Any(name => normalised.StartsWith(name, StringComparison.Ordinal));
        }

        /// <summary>
        /// The names as they are matched. A heading setting left empty falls back to what the
        /// reader has always looked for, because a schedule with no heading to find reads as
        /// no schedule at all and that is not what clearing the box meant.
        /// </summary>
        private static List<string> Keys(string names, string fallback)
        {
            List<string> keys = PrefrencesData.SplitParameterNames(names)
                                              .Select(LoadScheduleReader.Normalised)
                                              .Where(key => key.Length > 0)
                                              .Distinct()
                                              .ToList();

            if (keys.Count == 0 && fallback.Length > 0)
            {
                keys.Add(LoadScheduleReader.Normalised(fallback));
            }

            return keys;
        }

        private static string Display(string names, string fallback)
        {
            string[] split = PrefrencesData.SplitParameterNames(names);
            return split.Length == 0 ? fallback : string.Join(" or ", split.Select(name => name.Trim()));
        }
    }
}
