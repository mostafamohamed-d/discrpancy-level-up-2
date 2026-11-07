using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reads one sheet of a loading schedule workbook into the numbers the slab load check
    /// looks up.
    ///
    /// The sheet is laid out for a person rather than for a program: an entry is a serial
    /// number in the first column against a block of two, three or four rows breaking the
    /// superimposed dead load down into finishes, services and a partition allowance, with the
    /// total and the live load merged down beside them. The heading row repeats part way down
    /// a long sheet, where the schedule was split across two printed pages.
    ///
    /// So the sheet is read on the one thing that is reliable about it: an entry starts on
    /// every row that has a label in the serial column - a whole number, or a short code such
    /// as H1 or L1 on a row that carries a load - and the columns are found by their headings
    /// rather than by their letters, so a sheet with a column inserted still reads.
    ///
    /// Four loads come out of each entry, which is what an ETABS shell carries:
    ///
    ///   SDL   the total superimposed dead load, straight off the TOTAL column
    ///   LL    the live load, when the schedule marks it as not reducible
    ///   LR    the same value, when the schedule marks it reducible
    ///   MEP   the same value again, when the schedule marks it mechanical
    ///
    /// The live load is one number in one column; which of the three it belongs to is what the
    /// narrow column past the remarks says, and the other two are nought. A sheet that leaves
    /// that column empty - which is how the podium schedule in the reference workbook is
    /// written - is read as plain live load.
    /// </summary>
    public static class LoadScheduleReader
    {
        /// <summary>The sheets a workbook has, in tab order. Throws if the file is not readable.</summary>
        public static IList<string> SheetNames(string path)
        {
            return XlsxWorkbook.Open(path).SheetNames;
        }

        /// <summary>
        /// Reads one sheet. A sheet that is not a loading schedule at all comes back empty
        /// with its issues filled in rather than throwing: the user picked the wrong tab, and
        /// the answer to that is to say so and let them pick again.
        /// </summary>
        public static LoadSchedule Read(string path, string sheetName)
        {
            XlsxWorkbook workbook = XlsxWorkbook.Open(path);

            if (string.IsNullOrEmpty(sheetName))
            {
                sheetName = workbook.SheetNames.FirstOrDefault() ?? "";
            }

            LoadSchedule schedule = new LoadSchedule(path, sheetName);

            XlsxSheet sheet = workbook.Sheet(sheetName);
            if (sheet == null)
            {
                schedule.AddIssue($"The workbook has no sheet called \"{sheetName}\". It has: " +
                                  string.Join(", ", workbook.SheetNames) + ".");
                return schedule;
            }

            Read(sheet, schedule);

            // Said on every run, so a schedule that has drifted from its file is never taken
            // for the file itself.
            if (sheet.ToolEditCount > 0)
            {
                schedule.AddIssue($"{sheet.ToolEditCount} cell(s) are read as they were changed in the tool, "
                                  + "not as the workbook has them. Write them into the workbook, or discard "
                                  + "them, from the sheet's View / edit under Loading plans.");
            }

            return schedule;
        }

        /// <summary>The reading itself, kept apart from the file so it can be tested on its own.</summary>
        internal static void Read(XlsxSheet sheet, LoadSchedule schedule)
        {
            ScheduleNames names = ScheduleNames.Current;
            IDictionary<ScheduleRole, int> byHand =
                ScheduleColumnOverrides.Current.For(schedule.FilePath, schedule.SheetName);

            Columns columns = new Columns();
            columns.Override(byHand);
            Pending pending = null;

            for (int row = 1; row <= sheet.RowCount; row++)
            {
                if (IsHeaderRow(sheet, row, columns, names))
                {
                    columns.Override(byHand);

                    // The heading repeats where the schedule was split across printed pages.
                    // Reading it again costs nothing and picks up a sheet whose second half
                    // was laid out with a column more or less than its first.
                    Finish(schedule, ref pending);
                    continue;
                }

                if (!columns.Found)
                {
                    // Before the heading everything is a title, which is worth keeping so the
                    // panel can say which part of the building this sheet is.
                    if (schedule.Title.Length == 0)
                    {
                        schedule.Title = FirstTextOnRow(sheet, row);
                    }
                    continue;
                }

                string number;
                if (TryNumberOnRow(sheet, row, columns, out number))
                {
                    Finish(schedule, ref pending);
                    pending = Start(sheet, row, number, columns, names);
                    continue;
                }

                if (pending != null)
                {
                    // A continuation row of the block above: the second and third lines of the
                    // superimposed dead load breakdown, and the fourth where the schedule adds
                    // one. Only added up at all where the entry's own total was left empty.
                    AddSidlRow(sheet, row, columns, pending);
                }
            }

            Finish(schedule, ref pending);

            if (columns.Found)
            {
                RecordRoles(schedule.ColumnRoles, columns, names);
            }

            if (schedule.Count == 0 && columns.Found)
            {
                schedule.AddIssue($"{schedule.SheetName}: the heading row was found but no row under it " +
                                  "carries a serial number, so there is nothing to look a load number up in.");
            }
            else if (!columns.Found)
            {
                schedule.AddIssue($"{schedule.SheetName}: no heading row with \"SL. NO.\", " +
                                  $"\"{names.TotalDisplay}\" and \"{names.LiveDisplay}\" on it, so this " +
                                  "does not read as an area load schedule. Pick the sheet the area loads " +
                                  "are on, or say what this schedule calls its columns under Load patterns.");
            }
        }

        /// <summary>Says which column each value was read from, for the schedule window.</summary>
        private static void RecordRoles(IDictionary<int, string> roles, Columns columns, ScheduleNames names)
        {
            roles[columns.Number] = RoleNumber;
            if (columns.Description > 0 && !roles.ContainsKey(columns.Description))
            {
                roles[columns.Description] = RoleDescription;
            }
            if (columns.Sidl > 0 && !roles.ContainsKey(columns.Sidl))
            {
                roles[columns.Sidl] = names.SidlDisplay + " breakdown";
            }
            roles[columns.Total] = RoleSidl + " (" + names.TotalDisplay + ")";
            roles[columns.Live] = RoleLive;
            if (columns.LiveKind > 0 && !roles.ContainsKey(columns.LiveKind))
            {
                roles[columns.LiveKind] = RoleLiveKind;
            }
        }

        // What the schedule window calls each column the reader takes a value from.
        public const string RoleNumber = "No.";
        public const string RoleDescription = "Description";
        public const string RoleSidl = "SIDL";
        public const string RoleLive = "LL value";
        public const string RoleLiveKind = "LL type";

        #region one entry

        /// <summary>
        /// The entry being read, while the rows under its serial number are still coming. The
        /// breakdown values are collected as they go and only ever used where the schedule
        /// left the TOTAL cell empty.
        /// </summary>
        private sealed class Pending
        {
            public string Number = "";
            public string Description = "";
            public string LiveKind = "";
            public LoadSet Loads;
            public bool HasTotal;
            public double BreakdownSum;
        }

        /// <summary>
        /// An entry started off its own row. The total and the live load are merged down the
        /// block in the file, which puts both of them on this row.
        /// </summary>
        private static Pending Start(XlsxSheet sheet, int row, string number, Columns columns,
                                     ScheduleNames names)
        {
            double total;
            bool hasTotal = sheet.TryNumber(row, columns.Total, out total);

            double live;
            sheet.TryNumber(row, columns.Live, out live);

            string kind = columns.LiveKind > 0 ? sheet.Text(row, columns.LiveKind) : "";

            LoadSet loads = new LoadSet { Sdl = hasTotal ? total : 0 };
            loads = loads.Add(names.LiveCategory(kind), live);

            Pending pending = new Pending
            {
                Number = number,
                Description = sheet.Text(row, columns.Description),
                LiveKind = kind,
                Loads = loads,
                HasTotal = hasTotal
            };

            AddSidlRow(sheet, row, columns, pending);
            return pending;
        }

        /// <summary>
        /// Puts the entry that was being read into the schedule, taking its superimposed dead
        /// load from the breakdown rows where the sheet left the total empty.
        /// </summary>
        private static void Finish(LoadSchedule schedule, ref Pending pending)
        {
            if (pending == null)
            {
                return;
            }

            LoadSet loads = pending.Loads;
            if (!pending.HasTotal)
            {
                loads.Sdl = pending.BreakdownSum;
            }

            schedule.Add(new LoadScheduleEntry(pending.Number, pending.Description, loads,
                                               pending.LiveKind));
            pending = null;
        }

        private static void AddSidlRow(XlsxSheet sheet, int row, Columns columns, Pending pending)
        {
            double value;
            if (columns.Sidl > 0 && sheet.TryNumber(row, columns.Sidl, out value))
            {
                pending.BreakdownSum += value;
            }
        }

        /// <summary>
        /// Which of the three live loads the schedule's own marking means. The column carries
        /// LL, LLR or MECH in the reference workbook; what each is written as is a setting -
        /// see <see cref="ScheduleNames"/> - and anything none of them names, an empty cell
        /// included, is read as plain live load, which is the safe way round.
        /// </summary>
        internal static LoadCategory LiveCategory(string marking)
        {
            return ScheduleNames.Current.LiveCategory(marking);
        }

        #endregion

        #region finding the columns

        /// <summary>Where each thing the reader wants sits on the sheet, in column numbers.</summary>
        private sealed class Columns
        {
            /// <summary>
            /// The serial numbers, which is not always the column the heading is over.
            ///
            /// The reference workbook heads column B with "SL. NO." and types the numbers into
            /// column A beside it, because column B carries the legend swatch. So this starts
            /// as the heading's own column and moves to whichever column at or before it turns
            /// out to hold the numbers, the first time a row answers.
            /// </summary>
            public int Number;

            /// <summary>The rightmost column the numbers could be in, which is the heading's own.</summary>
            public int NumberHeading;

            /// <summary>Set once a row has said which of them it really is.</summary>
            public bool NumberSettled;

            public int Description;

            /// <summary>The column the superimposed dead load breakdown values are in.</summary>
            public int Sidl;

            public int Total;
            public int Live;

            /// <summary>The narrow column saying which live load it is. Zero when the sheet has none.</summary>
            public int LiveKind;

            public bool Found { get { return Number > 0 && Total > 0 && Live > 0; } }

            /// <summary>
            /// Puts the columns said by hand over the ones the heading gave. Said again after
            /// every heading row, because reading a heading sets every column afresh.
            /// </summary>
            public void Override(IDictionary<ScheduleRole, int> byHand)
            {
                int column;
                if (byHand.TryGetValue(ScheduleRole.Number, out column))
                {
                    Number = column;
                    NumberHeading = column;
                    NumberSettled = true;
                }
                if (byHand.TryGetValue(ScheduleRole.Description, out column))
                {
                    Description = column;
                }
                if (byHand.TryGetValue(ScheduleRole.Sidl, out column))
                {
                    Total = column;
                }
                if (byHand.TryGetValue(ScheduleRole.SidlBreakdown, out column))
                {
                    Sidl = column;
                }
                if (byHand.TryGetValue(ScheduleRole.Live, out column))
                {
                    Live = column;
                }
                if (byHand.TryGetValue(ScheduleRole.LiveKind, out column))
                {
                    LiveKind = column;
                }
            }
        }

        /// <summary>
        /// True when this row is the heading, in which case the columns are read off it.
        ///
        /// Matched on the headings themselves rather than on their positions, and only when
        /// enough of them are there at once: the serial number, the total and the live load.
        /// A row that happens to have the word "total" on it is not a heading.
        /// </summary>
        private static bool IsHeaderRow(XlsxSheet sheet, int row, Columns columns, ScheduleNames names)
        {
            int number = 0, description = 0, total = 0, live = 0, kind = 0, sidlHeading = 0;

            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                string text = Normalised(sheet.Text(row, column));
                if (text.Length == 0)
                {
                    continue;
                }

                if (number == 0 && (text.StartsWith("SLNO") || text == "SNO" || text == "SERIALNO"
                                    || text == "NO" || text == "LOADNO"))
                {
                    number = column;
                }
                else if (description == 0 && text.StartsWith("DESCRIPTION"))
                {
                    description = column;
                }
                else if (sidlHeading == 0 && names.IsSidlHeading(text))
                {
                    sidlHeading = column;
                }
                else if (total == 0 && names.IsTotalHeading(text))
                {
                    total = column;
                }
                else if (live == 0 && names.IsLiveHeading(text))
                {
                    live = column;
                }
                else if (kind == 0 && (text == "LL" || text == "LLTYPE" || text == "TYPE"))
                {
                    kind = column;
                }
            }

            if (number == 0 || total == 0 || live == 0)
            {
                return false;
            }

            columns.NumberHeading = number;
            if (!columns.NumberSettled)
            {
                columns.Number = number;
            }
            columns.Description = description > 0 ? description : number + 3;
            columns.Total = total;
            columns.Live = live;
            columns.LiveKind = kind;

            // The breakdown values sit in the last column of the SIDL band, which is the one
            // just before the total. Only used when an entry's own total was left empty.
            columns.Sidl = total - 1 > sidlHeading ? total - 1 : 0;

            return true;
        }

        /// <summary>
        /// A heading as it is matched: upper case, with the spaces, dots and line breaks a
        /// typed heading collects taken out, so "SL. NO." and "SL NO" read the same.
        /// </summary>
        internal static string Normalised(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            char[] kept = text.ToUpperInvariant()
                              .Where(char.IsLetterOrDigit)
                              .ToArray();

            return new string(kept);
        }

        private static string FirstTextOnRow(XlsxSheet sheet, int row)
        {
            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                string text = sheet.Text(row, column);
                if (text.Length > 0)
                {
                    return text;
                }
            }
            return "";
        }

        /// <summary>
        /// The label this row starts an entry with, if it starts one.
        ///
        /// Until a row has answered, every column from the first up to the one the heading is
        /// over is tried, leftmost first, and the one that answers is the column for the rest
        /// of the sheet. After that only that column is read, so a number that happens to land
        /// in the legend column later on cannot start an entry of its own.
        /// </summary>
        private static bool TryNumberOnRow(XlsxSheet sheet, int row, Columns columns, out string number)
        {
            if (columns.NumberSettled)
            {
                return TryLabel(sheet, row, columns.Number, columns, out number);
            }

            for (int column = 1; column <= columns.NumberHeading; column++)
            {
                if (TryLabel(sheet, row, column, columns, out number))
                {
                    columns.Number = column;
                    columns.NumberSettled = true;
                    return true;
                }
            }

            number = "";
            return false;
        }

        /// <summary>
        /// The label in one cell, if it holds one.
        ///
        /// A whole number always is one, as it always was. A word is one only when it is shaped
        /// like a label - H1, L-2, 3A - and the row also has a total or a live load on it,
        /// because that is what the first row of an entry carries and what a stray note in the
        /// serial column does not.
        /// </summary>
        private static bool TryLabel(XlsxSheet sheet, int row, int column, Columns columns, out string number)
        {
            string text = sheet.Text(row, column);

            int whole;
            if (TryWholeNumber(text, out whole))
            {
                number = whole.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            double value;
            if (LoadLabel.IsLabelShaped(text)
                && (sheet.TryNumber(row, columns.Total, out value) || sheet.TryNumber(row, columns.Live, out value)))
            {
                number = LoadLabel.Read(text);
                return true;
            }

            number = "";
            return false;
        }

        /// <summary>
        /// True when a cell holds a whole number, which is what a serial number is. A cell
        /// holding 1.5, or a word, is not one and does not start an entry.
        /// </summary>
        internal static bool TryWholeNumber(string text, out int number)
        {
            number = 0;

            double value;
            if (!XlsxSheet.TryNumber(text, out value))
            {
                return false;
            }

            if (Math.Abs(value - Math.Round(value)) > 1e-9 || value < 0 || value > int.MaxValue)
            {
                return false;
            }

            number = (int)Math.Round(value);
            return true;
        }

        #endregion
    }
}
