using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The four loads a slab is designed for, in kN/m2. One set is what the loading schedule
    /// says the area should carry and the other is what the ETABS shell actually has on it,
    /// and the check is the two of them side by side.
    ///
    /// Four rather than two because that is how the schedule and the ETABS model both split
    /// them, and because a total that comes out right by luck - live load put where the
    /// superimposed dead load belongs - is exactly the mistake worth catching.
    /// </summary>
    public struct LoadSet
    {
        /// <summary>Superimposed dead load: finishes, services, partition allowance.</summary>
        public double Sdl;

        /// <summary>Live load that is not reducible.</summary>
        public double Ll;

        /// <summary>Reducible live load.</summary>
        public double Lr;

        /// <summary>Mechanical and plant live load.</summary>
        public double Mep;

        public double this[LoadCategory category]
        {
            get
            {
                switch (category)
                {
                    case LoadCategory.Sdl: return Sdl;
                    case LoadCategory.Ll: return Ll;
                    case LoadCategory.Lr: return Lr;
                    case LoadCategory.Mep: return Mep;
                    default: return 0;
                }
            }
        }

        public LoadSet Add(LoadCategory category, double value)
        {
            LoadSet sum = this;
            switch (category)
            {
                case LoadCategory.Sdl: sum.Sdl += value; break;
                case LoadCategory.Ll: sum.Ll += value; break;
                case LoadCategory.Lr: sum.Lr += value; break;
                case LoadCategory.Mep: sum.Mep += value; break;
            }
            return sum;
        }

        /// <summary>The four in the order they are always written, for walking them.</summary>
        public static readonly LoadCategory[] Categories =
        {
            LoadCategory.Sdl, LoadCategory.Ll, LoadCategory.Lr, LoadCategory.Mep
        };

        public static string NameOf(LoadCategory category)
        {
            switch (category)
            {
                case LoadCategory.Sdl: return "SDL";
                case LoadCategory.Ll: return "LL";
                case LoadCategory.Lr: return "LR";
                case LoadCategory.Mep: return "MEP";
                default: return "";
            }
        }

        public override string ToString()
        {
            LoadSet loads = this;
            return string.Join(", ", Categories.Select(
                category => $"{NameOf(category)} {Text(loads[category])}"));
        }

        /// <summary>A load in kN/m2, to two places and without the trailing zeroes.</summary>
        public static string Text(double value)
        {
            return Math.Round(value, 2).ToString("0.##", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>Which of the four loads a value belongs to.</summary>
    public enum LoadCategory
    {
        /// <summary>Not one of the four the check knows about.</summary>
        None,
        Sdl,
        Ll,
        Lr,
        Mep
    }

    /// <summary>One labelled row of the loading schedule: what an area of that label carries.</summary>
    public sealed class LoadScheduleEntry
    {
        public LoadScheduleEntry(string number, string description, LoadSet loads, string liveLoadKind)
        {
            Number = LoadLabel.Read(number);
            Description = description ?? "";
            Loads = loads;
            LiveLoadKind = liveLoadKind ?? "";
        }

        /// <summary>
        /// The label the loading plan tags an area with - 7, or H1, or L-2 - as
        /// <see cref="LoadLabel.Read"/> keeps it.
        /// </summary>
        public string Number { get; }

        /// <summary>What the schedule calls this area, shown so a wrong number is readable.</summary>
        public string Description { get; }

        public LoadSet Loads { get; }

        /// <summary>
        /// What the schedule's own live load column said this one was - LL, LLR or MECH - kept
        /// as it was written so a mapping that went wrong can be seen rather than guessed at.
        /// </summary>
        public string LiveLoadKind { get; }

        /// <summary>One line naming this entry, for the results panel.</summary>
        public string Summary
        {
            get
            {
                string name = Description.Length > 60 ? Description.Substring(0, 57) + "..." : Description;
                return $"{Number} {name}".Trim();
            }
        }

        public override string ToString()
        {
            return $"{Summary}: {Loads}";
        }
    }

    /// <summary>
    /// One sheet of the loading schedule workbook, as the numbers the check looks up.
    ///
    /// A workbook usually carries a sheet per part of the building - a tower, a podium, the
    /// landscape over each - and the numbering starts again at one on every sheet, so which
    /// sheet a floor is read against is the user's to say and cannot be guessed at.
    /// </summary>
    public sealed class LoadSchedule
    {
        // Keyed on LoadLabel.Key, so h1 on the plan finds H1 in the schedule.
        private readonly Dictionary<string, LoadScheduleEntry> byNumber = new Dictionary<string, LoadScheduleEntry>();
        private readonly List<string> issues = new List<string>();

        public LoadSchedule(string filePath, string sheetName)
        {
            FilePath = filePath ?? "";
            SheetName = sheetName ?? "";
        }

        /// <summary>The workbook this was read from, so the panel can say which file is loaded.</summary>
        public string FilePath { get; }

        public string SheetName { get; }

        /// <summary>The title row of the sheet, which is what names the part of the building.</summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Which column of the sheet the reader took each value from, by column number counted
        /// from one - "No.", "SIDL" and so on. Empty when no heading was found. What the
        /// schedule window colours, so a column picked wrongly is seen rather than guessed at.
        /// </summary>
        public IDictionary<int, string> ColumnRoles { get; } = new SortedDictionary<int, string>();

        public IEnumerable<LoadScheduleEntry> Entries
        {
            get { return byNumber.Values.OrderBy(entry => entry.Number, LoadLabel.Order); }
        }

        public int Count { get { return byNumber.Count; } }

        /// <summary>Anything the sheet said that could not be made sense of.</summary>
        public IList<string> Issues { get { return issues; } }

        public void Add(LoadScheduleEntry entry)
        {
            if (entry == null || entry.Number.Length == 0)
            {
                return;
            }

            string key = LoadLabel.Key(entry.Number);
            if (byNumber.ContainsKey(key))
            {
                // Two rows with the same serial number is a schedule fault, not a reading
                // fault, and the check cannot pick between them. The first is kept so the rest
                // of the sheet is still usable, and the clash is reported.
                issues.Add($"{SheetName}: number {entry.Number} appears more than once " +
                           $"(\"{byNumber[key].Description}\" and \"{entry.Description}\"). " +
                           "The first one is used.");
                return;
            }

            byNumber[key] = entry;
        }

        public void AddIssue(string issue)
        {
            issues.Add(issue);
        }

        /// <summary>The entry for one area load label, or null when the sheet has no such label.</summary>
        public LoadScheduleEntry Find(string number)
        {
            LoadScheduleEntry entry;
            return byNumber.TryGetValue(LoadLabel.Key(number), out entry) ? entry : null;
        }

        /// <summary>One line saying what was read, for the panel.</summary>
        public string Description
        {
            get
            {
                if (Count == 0)
                {
                    return $"{SheetName}: no numbered rows were found on this sheet.";
                }

                List<LoadScheduleEntry> inOrder = Entries.ToList();
                return $"{SheetName}: {Count} numbered load(s), {inOrder.First().Number} to {inOrder.Last().Number}.";
            }
        }

        public override string ToString()
        {
            return Description;
        }
    }
}
