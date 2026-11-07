using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One numbered row of the line loading schedule: what a load line of that number carries.
    ///
    /// The number is the whole of the tie between the drawing and the sheet. A load line is
    /// drawn in a line style named for its number - "MRG_STR-Line Load-TL1" is number one -
    /// and the number is what this is looked up by, exactly as a filled region's area load
    /// number looks up a <see cref="LoadScheduleEntry"/>.
    ///
    /// The value is written two ways on the same sheet and they do not mean the same thing:
    ///
    ///   4.8            kN per square metre of elevation, so the load on the slab is this
    ///                  times the height of the wall standing on it
    ///   1.50 kN/m'     kN per metre run, already, whatever the wall height is
    ///
    /// Which of the two it is cannot be guessed from the number, so it is read off the cell
    /// and carried here rather than worked out later.
    /// </summary>
    public sealed class LineLoadEntry
    {
        public LineLoadEntry(int number, string description, double value,
                             bool heightIndependent, string valueText)
        {
            Number = number;
            Description = description ?? "";
            Value = value;
            HeightIndependent = heightIndependent;
            ValueText = valueText ?? "";
        }

        /// <summary>The serial number the line style is named for.</summary>
        public int Number { get; }

        /// <summary>
        /// What the schedule calls this line load. Shown so a wrong number reads as a wrong
        /// thing rather than as a wrong figure, and read by the height rule - it is the only
        /// place the sheet says whether this is a facade.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The number off the sheet: kN/m2 of elevation, or kN/m run when
        /// <see cref="HeightIndependent"/>.
        /// </summary>
        public double Value { get; }

        /// <summary>
        /// True when the sheet wrote the value as kN/m run, which is the load itself and is
        /// not multiplied by anything.
        /// </summary>
        public bool HeightIndependent { get; }

        /// <summary>Whatever the value cell actually held, for when it did not read.</summary>
        public string ValueText { get; }

        /// <summary>One line naming this entry, for the results panel.</summary>
        public string Summary
        {
            get
            {
                string name = Description.Length > 60 ? Description.Substring(0, 57) + "..." : Description;
                return $"{Number} {name}".Trim();
            }
        }

        /// <summary>The value as the sheet wrote it, units and all.</summary>
        public string ValueSummary
        {
            get
            {
                return HeightIndependent
                    ? LoadSet.Text(Value) + " kN/m"
                    : LoadSet.Text(Value) + " kN/m2 on elevation";
            }
        }

        public override string ToString()
        {
            return $"{Summary}: {ValueSummary}";
        }
    }

    /// <summary>
    /// One sheet of line loads, as the numbers the line load check looks up.
    ///
    /// The same shape as <see cref="LoadSchedule"/> and for the same reason: a workbook
    /// carries a sheet per part of the building - "Line Load D (TOWER)" beside "Line Load D
    /// (PODIUM)" - the numbering starts again at one on each, and which sheet a floor reads
    /// against is the user's to say.
    /// </summary>
    public sealed class LineLoadSchedule
    {
        private readonly Dictionary<int, LineLoadEntry> byNumber = new Dictionary<int, LineLoadEntry>();
        private readonly List<string> issues = new List<string>();

        public LineLoadSchedule(string filePath, string sheetName)
        {
            FilePath = filePath ?? "";
            SheetName = sheetName ?? "";
        }

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

        public IEnumerable<LineLoadEntry> Entries
        {
            get { return byNumber.Values.OrderBy(entry => entry.Number); }
        }

        public int Count { get { return byNumber.Count; } }

        /// <summary>Anything the sheet said that could not be made sense of.</summary>
        public IList<string> Issues { get { return issues; } }

        public void Add(LineLoadEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (byNumber.ContainsKey(entry.Number))
            {
                // A schedule fault rather than a reading fault, and the check cannot pick
                // between them. The first is kept so the rest of the sheet still reads.
                issues.Add($"{SheetName}: number {entry.Number} appears more than once " +
                           $"(\"{byNumber[entry.Number].Description}\" and \"{entry.Description}\"). " +
                           "The first one is used.");
                return;
            }

            byNumber[entry.Number] = entry;
        }

        public void AddIssue(string issue)
        {
            issues.Add(issue);
        }

        /// <summary>The entry for one line load number, or null when the sheet has no such number.</summary>
        public LineLoadEntry Find(int number)
        {
            LineLoadEntry entry;
            return byNumber.TryGetValue(number, out entry) ? entry : null;
        }

        /// <summary>One line saying what was read, for the panel.</summary>
        public string Description
        {
            get
            {
                if (Count == 0)
                {
                    return $"{SheetName}: no numbered line loads were found on this sheet.";
                }

                int lowest = byNumber.Keys.Min();
                int highest = byNumber.Keys.Max();
                return $"{SheetName}: {Count} numbered line load(s), {lowest} to {highest}.";
            }
        }

        public override string ToString()
        {
            return Description;
        }
    }
}
