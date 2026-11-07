using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>One cell of a schedule sheet and what it should say.</summary>
    public sealed class XlsxCellEdit
    {
        public XlsxCellEdit(int row, int column, string text)
        {
            Row = row;
            Column = column;
            Text = (text ?? "").Trim();
        }

        public int Row { get; }

        public int Column { get; }

        /// <summary>What the cell says. Empty is a cell emptied.</summary>
        public string Text { get; }

        public string Reference { get { return XlsxSheet.Reference(Row, Column); } }

        public override string ToString()
        {
            return $"{Reference} = \"{Text}\"";
        }
    }

    /// <summary>
    /// Loading schedule cells changed in the tool and kept there rather than written into the
    /// workbook.
    ///
    /// The workbook is somebody else's document as often as not - the loads engineer's, on a
    /// shared drive - and correcting a number in it is not always the checker's call to make.
    /// So a change made in the schedule window can be kept here instead: the file is left as
    /// it is, and every read of the sheet the check makes sees the changed value. Each run
    /// says so, so a schedule that has drifted from its file is never mistaken for the file.
    ///
    /// Held by workbook path and sheet name, because that is all a cell belongs to - which
    /// project pairs its levels with the sheet does not change what the cell says. Written
    /// into the settings file beside the pairings.
    /// </summary>
    public sealed class ScheduleEdits
    {
        /// <summary>Where the edits sit in the settings file.</summary>
        public const string Branch = "scheduleEdits";

        /// <summary>
        /// The one set the tool reads with. One, rather than handed about, because every read
        /// of a schedule goes through <see cref="XlsxWorkbook.Open(string)"/> and each of them
        /// has to see the same thing.
        /// </summary>
        public static ScheduleEdits Current { get; } = new ScheduleEdits();

        // workbook -> sheet -> cell reference -> text
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, XlsxCellEdit>>> byWorkbook =
            new Dictionary<string, Dictionary<string, Dictionary<string, XlsxCellEdit>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The edits kept for one sheet, in reading order.</summary>
        public IList<XlsxCellEdit> For(string workbook, string sheet)
        {
            Dictionary<string, XlsxCellEdit> cells = Cells(workbook, sheet, false);

            return cells == null
                ? new List<XlsxCellEdit>()
                : cells.Values.OrderBy(edit => edit.Row).ThenBy(edit => edit.Column).ToList();
        }

        /// <summary>How many cells of one sheet are kept changed here.</summary>
        public int Count(string workbook, string sheet)
        {
            Dictionary<string, XlsxCellEdit> cells = Cells(workbook, sheet, false);
            return cells == null ? 0 : cells.Count;
        }

        /// <summary>Keeps one cell changed, replacing whatever was kept for it before.</summary>
        public void Set(string workbook, string sheet, XlsxCellEdit edit)
        {
            if (edit == null || edit.Row <= 0 || edit.Column <= 0)
            {
                return;
            }

            Dictionary<string, XlsxCellEdit> cells = Cells(workbook, sheet, true);
            if (cells != null)
            {
                cells[edit.Reference] = edit;
            }
        }

        /// <summary>Stops keeping one cell changed, so it reads as the file has it again.</summary>
        public void Remove(string workbook, string sheet, int row, int column)
        {
            Dictionary<string, XlsxCellEdit> cells = Cells(workbook, sheet, false);
            if (cells == null)
            {
                return;
            }

            cells.Remove(XlsxSheet.Reference(row, column));
            Tidy(workbook, sheet);
        }

        /// <summary>Forgets every edit kept for one sheet, and answers how many there were.</summary>
        public int ClearSheet(string workbook, string sheet)
        {
            Dictionary<string, XlsxCellEdit> cells = Cells(workbook, sheet, false);
            if (cells == null)
            {
                return 0;
            }

            int count = cells.Count;
            cells.Clear();
            Tidy(workbook, sheet);
            return count;
        }

        /// <summary>
        /// Lays the edits kept for a sheet over it as read from the file, and answers how many
        /// cells it changed.
        /// </summary>
        internal int ApplyTo(string workbook, XlsxSheet sheet)
        {
            if (sheet == null)
            {
                return 0;
            }

            int applied = 0;

            foreach (XlsxCellEdit edit in For(workbook, sheet.Name))
            {
                // A formula cell is never edited from the tool, and an edit kept from before
                // the cell became one would throw the formula's answer away without a word.
                if (sheet.IsFormula(edit.Row, edit.Column))
                {
                    continue;
                }

                sheet.Set(edit.Row, edit.Column, edit.Text);
                applied++;
            }

            return applied;
        }

        #region the settings file

        /// <summary>Reads the edits out of the view branch of the settings file.</summary>
        public void ReadFrom(IDictionary<string, object> view)
        {
            foreach (KeyValuePair<string, object> workbook in Json.Branch(view, Branch))
            {
                Dictionary<string, object> sheets = workbook.Value as Dictionary<string, object>;
                if (sheets == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, object> sheet in sheets)
                {
                    Dictionary<string, object> cells = sheet.Value as Dictionary<string, object>;
                    if (cells == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, object> cell in cells)
                    {
                        int row = 0;
                        int column = 0;
                        XlsxSheet.SplitReference(cell.Key ?? "", ref row, ref column);

                        if (row > 0 && column > 0)
                        {
                            Set(workbook.Key, sheet.Key, new XlsxCellEdit(row, column, cell.Value as string));
                        }
                    }
                }
            }
        }

        /// <summary>The edits as they go into the settings file.</summary>
        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> workbooks = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, XlsxCellEdit>>> workbook in
                     byWorkbook.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                Dictionary<string, object> sheets = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (KeyValuePair<string, Dictionary<string, XlsxCellEdit>> sheet in workbook.Value)
                {
                    if (sheet.Value.Count == 0)
                    {
                        continue;
                    }

                    Dictionary<string, object> cells = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (XlsxCellEdit edit in sheet.Value.Values.OrderBy(e => e.Row).ThenBy(e => e.Column))
                    {
                        cells[edit.Reference] = edit.Text;
                    }

                    sheets[sheet.Key] = cells;
                }

                if (sheets.Count > 0)
                {
                    workbooks[workbook.Key] = sheets;
                }
            }

            return workbooks;
        }

        #endregion

        private Dictionary<string, XlsxCellEdit> Cells(string workbook, string sheet, bool create)
        {
            string book = WorkbookKey(workbook);
            string tab = (sheet ?? "").Trim();

            if (book.Length == 0 || tab.Length == 0)
            {
                return null;
            }

            Dictionary<string, Dictionary<string, XlsxCellEdit>> sheets;
            if (!byWorkbook.TryGetValue(book, out sheets))
            {
                if (!create)
                {
                    return null;
                }

                sheets = new Dictionary<string, Dictionary<string, XlsxCellEdit>>(StringComparer.OrdinalIgnoreCase);
                byWorkbook[book] = sheets;
            }

            Dictionary<string, XlsxCellEdit> cells;
            if (!sheets.TryGetValue(tab, out cells))
            {
                if (!create)
                {
                    return null;
                }

                cells = new Dictionary<string, XlsxCellEdit>(StringComparer.OrdinalIgnoreCase);
                sheets[tab] = cells;
            }

            return cells;
        }

        /// <summary>Drops a sheet, and then a workbook, left with nothing kept for it.</summary>
        private void Tidy(string workbook, string sheet)
        {
            string book = WorkbookKey(workbook);

            Dictionary<string, Dictionary<string, XlsxCellEdit>> sheets;
            if (!byWorkbook.TryGetValue(book, out sheets))
            {
                return;
            }

            Dictionary<string, XlsxCellEdit> cells;
            if (sheets.TryGetValue((sheet ?? "").Trim(), out cells) && cells.Count == 0)
            {
                sheets.Remove((sheet ?? "").Trim());
            }

            if (sheets.Count == 0)
            {
                byWorkbook.Remove(book);
            }
        }

        /// <summary>
        /// The workbook's full path, so the same file reached by two spellings of its path is
        /// one workbook rather than two.
        /// </summary>
        private static string WorkbookKey(string workbook)
        {
            string path = (workbook ?? "").Trim();
            if (path.Length == 0)
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
