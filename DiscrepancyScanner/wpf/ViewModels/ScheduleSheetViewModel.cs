using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>Where the changes made in the schedule window are to go.</summary>
    public enum ScheduleSaveTarget
    {
        /// <summary>Not saved: back to the sheet, with the changes still there.</summary>
        Cancel,

        /// <summary>Written into the workbook itself.</summary>
        Workbook,

        /// <summary>Kept in the tool, with the workbook left as it is.</summary>
        ToolOnly
    }

    /// <summary>
    /// One cell of the schedule as the window shows it: what the file says, what the tool
    /// keeps it changed to, and what it has been typed as since.
    /// </summary>
    public sealed class ScheduleCellViewModel : ViewModelBase
    {
        private string text;

        public ScheduleCellViewModel(int row, int column, string fileText, string keptText, bool isFormula)
        {
            Row = row;
            Column = column;
            FileText = fileText ?? "";
            KeptText = keptText;
            IsFormula = isFormula;
            text = keptText ?? FileText;
        }

        public int Row { get; }

        public int Column { get; }

        public string Reference { get { return XlsxSheet.Reference(Row, Column); } }

        /// <summary>What the workbook says.</summary>
        public string FileText { get; }

        /// <summary>What the tool keeps this cell changed to, or null when it keeps nothing.</summary>
        public string KeptText { get; private set; }

        /// <summary>A formula's cell, shown as it last worked out and never edited from here.</summary>
        public bool IsFormula { get; }

        /// <summary>What the cell says now, in the window.</summary>
        public string Text
        {
            get => text;
            set
            {
                if (IsFormula)
                {
                    return;
                }

                if (SetProperty(ref text, (value ?? "").Trim()))
                {
                    Changed?.Invoke();
                    RaiseState();
                }
            }
        }

        /// <summary>Typed here and not yet saved anywhere.</summary>
        public bool IsPending { get { return text != (KeptText ?? FileText); } }

        /// <summary>Kept changed in the tool, and not typed over since.</summary>
        public bool IsKept { get { return KeptText != null && !IsPending; } }

        /// <summary>Says something other than the workbook does, saved or not.</summary>
        public bool DiffersFromFile { get { return text != FileText; } }

        /// <summary>What hovering over the cell says.</summary>
        public string Tip
        {
            get
            {
                if (IsFormula)
                {
                    return Reference + " is worked out by a formula. Change it in Excel.";
                }
                if (!DiffersFromFile)
                {
                    return null;
                }

                string file = FileText.Length == 0 ? "empty" : "\"" + FileText + "\"";
                return IsPending
                    ? $"{Reference}: not saved yet. The workbook says {file}."
                    : $"{Reference}: changed in the tool only. The workbook says {file}.";
            }
        }

        /// <summary>Raised when the text is typed over, for the counts at the foot of the window.</summary>
        internal Action Changed { get; set; }

        /// <summary>Takes back whatever was typed since the last save.</summary>
        internal void Revert()
        {
            Text = KeptText ?? FileText;
        }

        /// <summary>Records the text as kept in the tool, or as not kept when it is what the file says.</summary>
        internal void MarkKept()
        {
            KeptText = text == FileText ? null : text;
            RaiseState();
        }

        private void RaiseState()
        {
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsKept));
            OnPropertyChanged(nameof(DiffersFromFile));
            OnPropertyChanged(nameof(Tip));
        }
    }

    /// <summary>One row of the schedule, as the window's table draws it.</summary>
    public sealed class ScheduleRowViewModel
    {
        public ScheduleRowViewModel(int number, List<ScheduleCellViewModel> cells)
        {
            Number = number;
            Cells = cells;
        }

        /// <summary>The row's number in the workbook, counted from one.</summary>
        public int Number { get; }

        public List<ScheduleCellViewModel> Cells { get; }
    }

    /// <summary>
    /// A sheet of the loading schedule, shown inside the tool and edited there.
    ///
    /// The schedule is what every load on the plan is checked against, and until now the only
    /// way to see what the check was reading was to go and open the file - and the only way to
    /// correct a number was to go and change it there and pick the file again. Here it is the
    /// sheet as the check reads it, with the cells the tool has changed picked out.
    ///
    /// Where a change goes is asked when it is saved, every time, because it is not always the
    /// checker's file to change: written into the workbook, or kept in the tool with the file
    /// left as it is. A change kept in the tool is read by every run, and every run says so.
    /// </summary>
    public sealed class ScheduleSheetViewModel : ViewModelBase
    {
        /// <summary>Room to type beyond what the sheet has in it, for a load that is not there yet.</summary>
        private const int SpareRows = 10;
        private const int SpareColumns = 2;

        private readonly ScheduleEdits edits;
        private readonly ScheduleColumnOverrides columnsByHand = ScheduleColumnOverrides.Current;
        private readonly List<ScheduleCellViewModel> allCells = new List<ScheduleCellViewModel>();

        private string selectedSheet = "";
        private string problem = "";
        private bool loading;

        public ScheduleSheetViewModel(string workbookPath, string sheet, ScheduleEdits store)
        {
            WorkbookPath = workbookPath ?? "";
            edits = store ?? ScheduleEdits.Current;

            SaveCommand = new RelayCommand(_ => Save(), _ => PendingCount > 0 || KeptCount > 0);
            RevertCommand = new RelayCommand(_ => Revert(), _ => PendingCount > 0);
            DiscardKeptCommand = new RelayCommand(_ => DiscardKept(), _ => KeptCount > 0 || PendingKeptCount > 0);
            ReloadCommand = new RelayCommand(_ => Reload());
            OpenInExcelCommand = new RelayCommand(_ => OpenInExcel(), _ => File.Exists(WorkbookPath));
            ResetColumnsCommand = new RelayCommand(_ => ResetColumns(), _ => HandSetColumns.Count > 0);

            try
            {
                foreach (string name in XlsxWorkbook.Open(WorkbookPath, false).SheetNames)
                {
                    Sheets.Add(name);
                }
            }
            catch (Exception e)
            {
                Problem = "The workbook could not be read: " + e.Message;
            }

            selectedSheet = Sheets.FirstOrDefault(
                                name => string.Equals(name, sheet, StringComparison.OrdinalIgnoreCase))
                            ?? Sheets.FirstOrDefault() ?? "";
            Load();
        }

        #region what the window hands in

        /// <summary>Set by the window to ask where a save goes.</summary>
        public Func<int, ScheduleSaveTarget> AskWhereToSave { get; set; }

        /// <summary>Set by the window to ask a yes or no question; true is yes.</summary>
        public Func<string, bool> Confirm { get; set; }

        /// <summary>Set by the window to say how a save went.</summary>
        public Action<string> Report { get; set; }

        /// <summary>Raised when the sheet shown has a different number of columns, so the table is laid out again.</summary>
        public event Action ColumnsChanged;

        #endregion

        public string WorkbookPath { get; }

        public string WorkbookName { get { return Path.GetFileName(WorkbookPath); } }

        public string Heading { get { return WorkbookName + (selectedSheet.Length == 0 ? "" : "  ·  " + selectedSheet); } }

        public ObservableCollection<string> Sheets { get; } = new ObservableCollection<string>();

        /// <summary>
        /// The sheet shown. Changing it with changes not yet saved asks first, because the
        /// changes belong to the sheet they were typed on and would otherwise go without a word.
        /// </summary>
        public string SelectedSheet
        {
            get => selectedSheet;
            set
            {
                string wanted = value ?? "";
                if (wanted == selectedSheet || loading)
                {
                    return;
                }

                if (PendingCount > 0 && Confirm != null
                    && !Confirm($"{PendingCount} change(s) on {selectedSheet} are not saved. Throw them away?"))
                {
                    // Said again so the picker goes back to the sheet still shown.
                    OnPropertyChanged();
                    return;
                }

                selectedSheet = wanted;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Heading));
                Load();
            }
        }

        public ObservableCollection<ScheduleRowViewModel> Rows { get; } = new ObservableCollection<ScheduleRowViewModel>();

        /// <summary>How many columns the table has, spare ones included.</summary>
        public int ColumnCount { get; private set; }

        public string Problem
        {
            get => problem;
            private set
            {
                if (SetProperty(ref problem, value ?? ""))
                {
                    OnPropertyChanged(nameof(HasProblem));
                }
            }
        }

        public bool HasProblem { get { return problem.Length > 0; } }

        /// <summary>
        /// Which column the check takes each value from, by column number - the serial number,
        /// the SIDL, the live load and its marking. What the table colours, so a column picked
        /// wrongly is seen at a glance rather than found out from a wrong result.
        /// </summary>
        public IDictionary<int, string> ColumnRoles { get; private set; } = new Dictionary<int, string>();

        /// <summary>What the check reads this sheet as, and from which columns, in a line.</summary>
        public string ReadAs { get; private set; } = "";

        /// <summary>
        /// Reads the sheet the way the check does - with the tool's own changes laid over it -
        /// through each of the two readers, and keeps the columns of whichever found its heading.
        /// </summary>
        private void FindColumnRoles()
        {
            ColumnRoles = new Dictionary<int, string>();
            ReadAs = "";

            try
            {
                XlsxSheet sheet = XlsxWorkbook.Open(WorkbookPath).Sheet(selectedSheet);
                if (sheet != null)
                {
                    LoadSchedule area = new LoadSchedule(WorkbookPath, selectedSheet);
                    LoadScheduleReader.Read(sheet, area);

                    if (area.ColumnRoles.Count > 0)
                    {
                        ColumnRoles = new Dictionary<int, string>(area.ColumnRoles);
                        ReadAs = $"Read as an area load schedule, {area.Count} numbered load(s): " + DescribeRoles();
                    }
                    else
                    {
                        LineLoadSchedule line = new LineLoadSchedule(WorkbookPath, selectedSheet);
                        LineLoadScheduleReader.Read(sheet, line);

                        if (line.ColumnRoles.Count > 0)
                        {
                            ColumnRoles = new Dictionary<int, string>(line.ColumnRoles);
                            ReadAs = $"Read as a line load schedule, {line.Count} numbered load(s): " + DescribeRoles();
                        }
                        else
                        {
                            ReadAs = "No schedule heading found on this sheet, so the check reads nothing from it. "
                                     + "If its headings are named differently, say what they are under Load patterns.";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ReadAs = "The columns could not be worked out - " + e.Message;
            }

            OnPropertyChanged(nameof(ColumnRoles));
            OnPropertyChanged(nameof(ReadAs));
        }

        private string DescribeRoles()
        {
            return string.Join(", ", ColumnRoles.OrderBy(role => role.Key)
                                                .Select(role => $"{role.Value} in {ColumnName(role.Key)}")) + ".";
        }

        #region the counts

        /// <summary>Cells typed and not yet saved anywhere.</summary>
        public int PendingCount { get { return allCells.Count(cell => cell.IsPending); } }

        /// <summary>Cells kept changed in the tool and not typed over since.</summary>
        public int KeptCount { get { return allCells.Count(cell => cell.IsKept); } }

        /// <summary>Cells kept changed in the tool that have been typed over again since.</summary>
        private int PendingKeptCount { get { return allCells.Count(cell => cell.IsPending && cell.KeptText != null); } }

        /// <summary>Every cell that says something other than the workbook, saved or not.</summary>
        private List<ScheduleCellViewModel> DifferFromFile()
        {
            return allCells.Where(cell => cell.DiffersFromFile).ToList();
        }

        public string Summary
        {
            get
            {
                if (HasProblem)
                {
                    return "";
                }

                int pending = PendingCount;
                int kept = KeptCount;

                if (pending == 0 && kept == 0)
                {
                    return "The sheet as the workbook has it. Double click a cell to change it.";
                }

                List<string> parts = new List<string>();
                if (pending > 0)
                {
                    parts.Add($"{pending} change(s) not saved");
                }
                if (kept > 0)
                {
                    parts.Add($"{kept} cell(s) changed in the tool only - the check reads them, the workbook does not have them");
                }
                return string.Join(" · ", parts) + ".";
            }
        }

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(KeptCount));
            OnPropertyChanged(nameof(Summary));
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region commands

        public ICommand SaveCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand DiscardKeptCommand { get; }
        public ICommand ReloadCommand { get; }
        public ICommand OpenInExcelCommand { get; }
        public ICommand ResetColumnsCommand { get; }

        #region saying by hand what a column holds

        /// <summary>
        /// The roles a column can be said to hold, as the header menu offers them. The three
        /// the line load sheet reads come first; the rest are the area sheet's.
        /// </summary>
        public static readonly KeyValuePair<ScheduleRole, string>[] RoleChoices =
        {
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.Number, "No. - the serial number"),
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.Description, "Description"),
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.Sidl, "SIDL value (area: the total, line: the load)"),
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.SidlBreakdown, "SIDL breakdown (area)"),
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.Live, "LL value (area)"),
            new KeyValuePair<ScheduleRole, string>(ScheduleRole.LiveKind, "LL type (area)")
        };

        /// <summary>
        /// Whether a column can be said by hand here: it is kept per project, so there has to
        /// be a project open to keep it under.
        /// </summary>
        public bool CanSetColumns { get { return columnsByHand.ActiveProject.Length > 0; } }

        /// <summary>The columns said by hand for this sheet in the open project, role by role.</summary>
        public IDictionary<ScheduleRole, int> HandSetColumns
        {
            get { return columnsByHand.For(WorkbookPath, selectedSheet); }
        }

        /// <summary>The role a column has been said by hand to hold, or null when the headings decide it.</summary>
        public ScheduleRole? HandRoleOf(int column)
        {
            foreach (KeyValuePair<ScheduleRole, int> role in HandSetColumns)
            {
                if (role.Value == column)
                {
                    return role.Key;
                }
            }
            return null;
        }

        /// <summary>
        /// Says what a column holds, or with null hands it back to the headings. The sheet is
        /// read again at once, so the colours say straight away what the check will now read.
        /// </summary>
        public void SetColumnRole(int column, ScheduleRole? role)
        {
            if (!CanSetColumns || column <= 0)
            {
                return;
            }

            if (role.HasValue)
            {
                columnsByHand.Set(columnsByHand.ActiveProject, WorkbookPath, selectedSheet, role.Value, column);
            }
            else
            {
                ScheduleRole? held = HandRoleOf(column);
                if (held.HasValue)
                {
                    columnsByHand.Set(columnsByHand.ActiveProject, WorkbookPath, selectedSheet, held.Value, 0);
                }
            }

            FindColumnRoles();
            ColumnsChanged?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }

        private void ResetColumns()
        {
            columnsByHand.ClearSheet(columnsByHand.ActiveProject, WorkbookPath, selectedSheet);
            FindColumnRoles();
            ColumnsChanged?.Invoke();
        }

        #endregion

        /// <summary>
        /// Saves wherever the user says. Written into the workbook, every cell that differs
        /// from it goes in - the ones kept in the tool from before as well as the ones typed
        /// now - and the tool stops keeping any of them, because the file says them now.
        /// Kept in the tool, only what was typed now is added to what it keeps.
        ///
        /// Answers false when nothing was saved, so closing the window can stay open for it.
        /// </summary>
        public bool Save()
        {
            List<ScheduleCellViewModel> differing = DifferFromFile();
            if (PendingCount == 0 && differing.Count == 0)
            {
                return true;
            }

            ScheduleSaveTarget target = AskWhereToSave == null
                ? ScheduleSaveTarget.ToolOnly
                : AskWhereToSave(differing.Count);

            switch (target)
            {
                case ScheduleSaveTarget.Workbook:
                    return WriteIntoWorkbook(differing);

                case ScheduleSaveTarget.ToolOnly:
                    KeepInTool();
                    return true;

                default:
                    return false;
            }
        }

        private bool WriteIntoWorkbook(List<ScheduleCellViewModel> differing)
        {
            if (differing.Count == 0)
            {
                // Typed back to what the file says: nothing to write, only the kept edits to drop.
                edits.ClearSheet(WorkbookPath, selectedSheet);
                Load();
                return true;
            }

            try
            {
                string backup = XlsxCellWriter.Write(
                    WorkbookPath, selectedSheet,
                    differing.Select(cell => new XlsxCellEdit(cell.Row, cell.Column, cell.Text)).ToList());

                edits.ClearSheet(WorkbookPath, selectedSheet);
                Load();

                Report?.Invoke($"{differing.Count} cell(s) written into {WorkbookName}, on {selectedSheet}.\n\n"
                               + "The workbook as it was before is kept at:\n" + backup);
                return true;
            }
            catch (IOException e)
            {
                Report?.Invoke($"{WorkbookName} could not be written - {e.Message}\n\n"
                               + "It is most likely open in Excel. Close it there and save again; "
                               + "nothing typed here has been lost.");
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Report?.Invoke($"{WorkbookName} could not be written - {e.Message}\n\n"
                               + "The file may be read only, or on a drive you cannot write to. "
                               + "Keep the changes in the tool instead, or save a copy of the workbook "
                               + "you can write to and pick that one.");
                return false;
            }
            catch (Exception e)
            {
                Report?.Invoke($"Nothing was written into {WorkbookName} - {e.Message}");
                return false;
            }
        }

        private void KeepInTool()
        {
            foreach (ScheduleCellViewModel cell in allCells.Where(c => c.IsPending))
            {
                if (cell.DiffersFromFile)
                {
                    edits.Set(WorkbookPath, selectedSheet, new XlsxCellEdit(cell.Row, cell.Column, cell.Text));
                }
                else
                {
                    edits.Remove(WorkbookPath, selectedSheet, cell.Row, cell.Column);
                }

                cell.MarkKept();
            }

            RefreshCounts();
        }

        private void Revert()
        {
            foreach (ScheduleCellViewModel cell in allCells.Where(c => c.IsPending))
            {
                cell.Revert();
            }

            RefreshCounts();
        }

        /// <summary>
        /// Forgets the changes kept in the tool for this sheet, so it reads as the workbook has
        /// it again. Asked first: they may be the only record of what was corrected.
        /// </summary>
        private void DiscardKept()
        {
            int kept = edits.Count(WorkbookPath, selectedSheet);
            if (Confirm != null
                && !Confirm($"Forget the {kept} cell(s) changed in the tool on {selectedSheet}, "
                            + "and anything typed and not saved? The sheet will read as the workbook has it."))
            {
                return;
            }

            edits.ClearSheet(WorkbookPath, selectedSheet);
            Load();
        }

        /// <summary>Reads the sheet again, for a workbook changed in Excel while this was open.</summary>
        private void Reload()
        {
            if (PendingCount > 0 && Confirm != null
                && !Confirm($"{PendingCount} change(s) are not saved. Read the workbook again and throw them away?"))
            {
                return;
            }

            Load();
        }

        private void OpenInExcel()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WorkbookPath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception e)
            {
                Report?.Invoke($"{WorkbookName} could not be opened - {e.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Reads the sheet from the file, with what the tool keeps for it laid over. The file
        /// is read on its own so every cell knows what the workbook says as well as what the
        /// check will read.
        /// </summary>
        private void Load()
        {
            loading = true;

            try
            {
                Rows.Clear();
                allCells.Clear();
                Problem = "";

                if (selectedSheet.Length == 0)
                {
                    SetColumns(0);
                    return;
                }

                XlsxSheet sheet;
                try
                {
                    sheet = XlsxWorkbook.Open(WorkbookPath, false).Sheet(selectedSheet);
                }
                catch (Exception e)
                {
                    Problem = "The workbook could not be read: " + e.Message;
                    SetColumns(0);
                    return;
                }

                if (sheet == null)
                {
                    Problem = $"The workbook has no sheet called \"{selectedSheet}\" any more.";
                    SetColumns(0);
                    return;
                }

                Dictionary<string, string> kept = edits.For(WorkbookPath, selectedSheet)
                    .Where(edit => !sheet.IsFormula(edit.Row, edit.Column))
                    .ToDictionary(edit => edit.Reference, edit => edit.Text, StringComparer.OrdinalIgnoreCase);

                int keptRows = kept.Count == 0 ? 0 : edits.For(WorkbookPath, selectedSheet).Max(edit => edit.Row);
                int keptColumns = kept.Count == 0 ? 0 : edits.For(WorkbookPath, selectedSheet).Max(edit => edit.Column);

                int rows = Math.Max(sheet.RowCount, keptRows) + SpareRows;
                int columns = Math.Max(sheet.ColumnCount, keptColumns) + SpareColumns;

                for (int row = 1; row <= rows; row++)
                {
                    List<ScheduleCellViewModel> cells = new List<ScheduleCellViewModel>(columns);

                    for (int column = 1; column <= columns; column++)
                    {
                        string keptText;
                        kept.TryGetValue(XlsxSheet.Reference(row, column), out keptText);

                        ScheduleCellViewModel cell = new ScheduleCellViewModel(
                            row, column, sheet.Text(row, column), keptText, sheet.IsFormula(row, column))
                        {
                            Changed = RefreshCounts
                        };

                        cells.Add(cell);
                        allCells.Add(cell);
                    }

                    Rows.Add(new ScheduleRowViewModel(row, cells));
                }

                FindColumnRoles();
                SetColumns(columns);
            }
            finally
            {
                loading = false;
                RefreshCounts();
            }
        }

        private void SetColumns(int columns)
        {
            ColumnCount = columns;
            OnPropertyChanged(nameof(ColumnCount));
            ColumnsChanged?.Invoke();
        }

        /// <summary>A column's letters, for its heading.</summary>
        public static string ColumnName(int column)
        {
            return XlsxSheet.ColumnName(column);
        }
    }
}
