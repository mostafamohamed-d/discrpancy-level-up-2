using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reads one sheet of line loads into the numbers the line load check looks up.
    ///
    /// A far plainer sheet than the area schedule: one row per numbered line load, a value, a
    /// description, and a legend column holding a drawn swatch that nothing reads. So it is
    /// read the same way for the same reason - an entry is every row with a whole number in
    /// the serial column, and the columns are found by their headings rather than by their
    /// letters - and the work that is left is all in the value.
    ///
    /// The value column is headed "SIDL (kN/m2) LOAD ON ELEVATION" and holds two different
    /// things. A bare number is what the heading says: kN per square metre of elevation, to be
    /// multiplied by the height of the wall. A number written "1.50 kN/m'" is a load per metre
    /// run already, and multiplying it by anything would be wrong. The prime is the whole of
    /// the difference, and it is the reason the value is read as text rather than as a number.
    ///
    /// The other sheet shape in these workbooks - "Schedule for BIM-Line Load D+W", with Fz,
    /// Mx and Fy against dead and wind - is not read here. It is a different schedule that
    /// happens to live in the same file, and reading it means comparing moments on the ETABS
    /// frames too. A sheet laid out that way is reported as not being one of these rather than
    /// half read.
    /// </summary>
    public static class LineLoadScheduleReader
    {
        /// <summary>The sheets a workbook has, in tab order. Throws if the file is not readable.</summary>
        public static IList<string> SheetNames(string path)
        {
            return XlsxWorkbook.Open(path).SheetNames;
        }

        /// <summary>
        /// Reads one sheet. A sheet that is not a line load schedule comes back empty with its
        /// issues filled in rather than throwing: the user picked the wrong tab, and the answer
        /// to that is to say so and let them pick again.
        /// </summary>
        public static LineLoadSchedule Read(string path, string sheetName)
        {
            XlsxWorkbook workbook = XlsxWorkbook.Open(path);

            if (string.IsNullOrEmpty(sheetName))
            {
                sheetName = workbook.SheetNames.FirstOrDefault() ?? "";
            }

            LineLoadSchedule schedule = new LineLoadSchedule(path, sheetName);

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
        internal static void Read(XlsxSheet sheet, LineLoadSchedule schedule)
        {
            ScheduleNames names = ScheduleNames.Current;
            IDictionary<ScheduleRole, int> byHand =
                ScheduleColumnOverrides.Current.For(schedule.FilePath, schedule.SheetName);

            Columns columns = new Columns();
            columns.Override(byHand);
            List<string> unreadable = new List<string>();

            for (int row = 1; row <= sheet.RowCount; row++)
            {
                if (IsHeaderRow(sheet, row, columns, names))
                {
                    columns.Override(byHand);
                    continue;
                }

                if (!columns.Found)
                {
                    // Before the heading everything is a title, and the first of it is what
                    // names the part of the building this sheet is for.
                    if (schedule.Title.Length == 0)
                    {
                        schedule.Title = FirstTextOnRow(sheet, row);
                    }
                    continue;
                }

                int number;
                if (!TryNumberOnRow(sheet, row, columns, out number))
                {
                    continue;
                }

                string valueText = sheet.Text(row, columns.Value);
                string description = columns.Description > 0 ? sheet.Text(row, columns.Description) : "";

                double value;
                bool heightIndependent;
                if (!TryValue(valueText, out value, out heightIndependent))
                {
                    // Named rather than counted: a row whose value did not read is the one row
                    // somebody has to go and look at, and its number is what finds it.
                    unreadable.Add(valueText.Length == 0
                        ? $"{number} (the value cell is empty)"
                        : $"{number} (\"{Shortened(valueText)}\")");
                    continue;
                }

                schedule.Add(new LineLoadEntry(number, description, value, heightIndependent, valueText));
            }

            if (columns.Found)
            {
                schedule.ColumnRoles[columns.Number] = LoadScheduleReader.RoleNumber;
                if (columns.Description > 0 && columns.Description != columns.Number)
                {
                    schedule.ColumnRoles[columns.Description] = LoadScheduleReader.RoleDescription;
                }
                schedule.ColumnRoles[columns.Value] = LoadScheduleReader.RoleSidl;
            }

            Report(sheet, schedule, columns, unreadable, names);
        }

        /// <summary>
        /// What the read could not do, said once at the end. Kept together so a sheet that was
        /// not a line load schedule at all is told apart from one that was and had bad rows on
        /// it - they are quite different things to go and fix.
        /// </summary>
        private static void Report(XlsxSheet sheet, LineLoadSchedule schedule, Columns columns,
                                   List<string> unreadable, ScheduleNames names)
        {
            if (!columns.Found)
            {
                schedule.AddIssue(columns.LooksLikeAreaSchedule
                    ? $"\"{sheet.Name}\" is an area loading schedule - it has {names.TotalDisplay} and "
                      + $"{names.LiveDisplay} columns on it - rather than a line load schedule. Pick the "
                      + "sheet the line loads are on."
                    : $"\"{sheet.Name}\" has no line load heading on it: a serial number column and a "
                      + $"value column headed {names.SidlDisplay} or LOAD ON ELEVATION. Either this is not "
                      + "the line load sheet, or it calls its SIDL something else - say what under Load "
                      + "patterns.");
                return;
            }

            if (unreadable.Count > 0)
            {
                schedule.AddIssue($"{unreadable.Count} row(s) on \"{sheet.Name}\" carry no value that "
                                  + $"reads as a number, so a line drawn for them cannot be checked: "
                                  + string.Join("; ", unreadable.Take(12))
                                  + (unreadable.Count > 12 ? $"; and {unreadable.Count - 12} more" : ""));
            }
        }

        private static string Shortened(string text)
        {
            string oneLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length > 40 ? oneLine.Substring(0, 37) + "..." : oneLine;
        }

        #region the value

        /// <summary>
        /// The number in a value cell, and whether the sheet wrote it per metre run.
        ///
        /// Height independent when the units carry a prime - kN/m' - which is how this schedule
        /// marks a load that is already a load per metre. Anything else, a bare number or a
        /// number written with kN/m2 after it, is per square metre of elevation.
        ///
        /// The number itself is whatever the cell starts with, so the units after it do not
        /// have to be written any particular way to be got past.
        /// </summary>
        internal static bool TryValue(string text, out double value, out bool heightIndependent)
        {
            value = 0;
            heightIndependent = false;

            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            heightIndependent = IsPerMetreRun(text);

            return XlsxSheet.TryNumber(text, out value) || TryLeadingNumber(text, out value);
        }

        /// <summary>
        /// True when the cell marks its value as a load per metre run: "kN/m" followed by a
        /// prime, however the prime was typed. Word processors and Excel both turn a typed
        /// apostrophe into a curly one given the chance, and the sheet has to read the same
        /// either way.
        /// </summary>
        internal static bool IsPerMetreRun(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string squeezed = new string(text.ToUpperInvariant()
                                             .Where(letter => !char.IsWhiteSpace(letter))
                                             .Select(Straightened)
                                             .ToArray());

            return squeezed.Contains("KN/M'") || squeezed.Contains("KNPERM'");
        }

        /// <summary>Every kind of prime and apostrophe written as the plain one.</summary>
        private static char Straightened(char letter)
        {
            switch (letter)
            {
                case '‘': // left single quote
                case '’': // right single quote
                case 'ʹ': // modifier prime
                case 'ʼ': // modifier apostrophe
                case '´': // acute accent
                case '`': // grave accent
                case '′': // prime
                    return '\'';
                default:
                    return letter;
            }
        }

        /// <summary>
        /// The number a cell starts with, for the cells that carry their units after it.
        /// Stops at the first character that cannot be part of a number, so "1.50 kN/m'"
        /// answers 1.5 and "SEE NOTE" answers nothing.
        /// </summary>
        private static bool TryLeadingNumber(string text, out double value)
        {
            value = 0;

            int at = 0;
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }

            int from = at;

            if (at < text.Length && (text[at] == '+' || text[at] == '-'))
            {
                at++;
            }

            while (at < text.Length && (char.IsDigit(text[at]) || text[at] == '.' || text[at] == ','))
            {
                at++;
            }

            return at > from && XlsxSheet.TryNumber(text.Substring(from, at - from), out value);
        }

        #endregion

        #region finding the columns

        private sealed class Columns
        {
            /// <summary>
            /// The serial numbers, which is not always the column the heading is over - the
            /// area schedule in the same workbook heads column B and types into column A. So
            /// this starts as the heading's own column and moves to whichever column at or
            /// before it turns out to hold the numbers, the first time a row answers.
            /// </summary>
            public int Number;

            /// <summary>The rightmost column the numbers could be in, which is the heading's own.</summary>
            public int NumberHeading;

            /// <summary>Set once a row has said which of them it really is.</summary>
            public bool NumberSettled;

            public int Description;

            /// <summary>The column the load on elevation is in.</summary>
            public int Value;

            /// <summary>
            /// Set when a row looked like the heading of an area loading schedule instead. The
            /// two live in the same workbook, so picking the wrong tab is the likely mistake
            /// and worth saying out loud rather than reporting as an unreadable sheet.
            /// </summary>
            public bool LooksLikeAreaSchedule;

            public bool Found { get { return Number > 0 && Value > 0; } }

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
                    Value = column;
                }
            }
        }

        /// <summary>
        /// True when this row is the heading, in which case the columns are read off it.
        ///
        /// Matched on the headings themselves rather than on their positions, and only when
        /// both the ones the check cannot work without are there at once: the serial number and
        /// the value. A row that happens to say SIDL somewhere is not a heading.
        /// </summary>
        private static bool IsHeaderRow(XlsxSheet sheet, int row, Columns columns, ScheduleNames names)
        {
            int number = 0, description = 0, value = 0;
            bool total = false, live = false;

            for (int column = 1; column <= sheet.ColumnCount; column++)
            {
                string text = LoadScheduleReader.Normalised(sheet.Text(row, column));
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
                else if (value == 0 && (names.IsSidlHeading(text) || text.Contains("LOADONELEVATION")))
                {
                    value = column;
                }

                if (names.IsTotalHeading(text))
                {
                    total = true;
                }
                else if (names.IsLiveHeading(text))
                {
                    live = true;
                }
            }

            // An area schedule's heading carries SL NO. and SIDL too, so on its own the value
            // column is not enough to tell the two apart. Its TOTAL and LIVE LOAD columns are.
            if (number > 0 && total && live)
            {
                columns.LooksLikeAreaSchedule = true;
                return false;
            }

            if (number == 0 || value == 0)
            {
                return false;
            }

            columns.Number = number;
            columns.NumberHeading = number;
            columns.NumberSettled = false;
            columns.Description = description;
            columns.Value = value;
            columns.LooksLikeAreaSchedule = false;

            return true;
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
        /// The serial number this row starts an entry with, if it starts one. Until a row has
        /// answered, every column up to the heading's own is tried, leftmost first, and the one
        /// that answers is the column for the rest of the sheet.
        /// </summary>
        private static bool TryNumberOnRow(XlsxSheet sheet, int row, Columns columns, out int number)
        {
            if (columns.NumberSettled)
            {
                return LoadScheduleReader.TryWholeNumber(sheet.Text(row, columns.Number), out number);
            }

            for (int column = 1; column <= columns.NumberHeading; column++)
            {
                if (LoadScheduleReader.TryWholeNumber(sheet.Text(row, column), out number))
                {
                    columns.Number = column;
                    columns.NumberSettled = true;
                    return true;
                }
            }

            number = 0;
            return false;
        }

        #endregion
    }
}
