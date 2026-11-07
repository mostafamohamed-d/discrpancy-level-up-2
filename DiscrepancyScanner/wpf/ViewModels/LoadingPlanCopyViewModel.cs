using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>One level the pairing could be copied to, and whether it is.</summary>
    public sealed class LoadingPlanCopyRow : ViewModelBase
    {
        private bool isChosen;

        public LoadingPlanCopyRow(string story, string currentState, bool isSimilar)
        {
            Story = story ?? "";
            CurrentState = currentState ?? "";
            IsSimilar = isSimilar;
        }

        public string Story { get; }

        /// <summary>What the level is paired with today, so overwriting it is a decision made in sight.</summary>
        public string CurrentState { get; }

        /// <summary>Built to the same typical floor in ETABS as the level being copied from.</summary>
        public bool IsSimilar { get; }

        public bool IsChosen
        {
            get => isChosen;
            set => SetProperty(ref isChosen, value);
        }
    }

    /// <summary>
    /// "Copy to", as a window of its own: one level's pairing - the view, the workbook and its
    /// sheets - given to every level ticked.
    ///
    /// A typical floor is one loading plan for a run of levels, all read off the same view
    /// and the same sheets, so pairing it once and ticking the rest is the job rather than
    /// twenty pairings that are each the same. Nothing is written until Apply.
    /// </summary>
    public sealed class LoadingPlanCopyViewModel : ViewModelBase
    {
        private bool copyView = true;
        private bool copySheets = true;

        public LoadingPlanCopyViewModel(LoadingPlanLink source, IEnumerable<LoadingPlanCopyRow> rows)
        {
            Source = source ?? new LoadingPlanLink();

            foreach (LoadingPlanCopyRow row in rows ?? new LoadingPlanCopyRow[0])
            {
                Rows.Add(row);
                row.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(LoadingPlanCopyRow.IsChosen))
                    {
                        OnPropertyChanged(nameof(Summary));
                        CommandManager.InvalidateRequerySuggested();
                    }
                };
            }

            SelectAllCommand = new RelayCommand(_ => Choose(row => true));
            SelectNoneCommand = new RelayCommand(_ => Choose(row => false));
            SelectUnpairedCommand = new RelayCommand(_ => Choose(row => row.CurrentState.Length == 0));
            SelectSimilarCommand = new RelayCommand(_ => Choose(row => row.IsSimilar),
                                                    _ => Rows.Any(row => row.IsSimilar));
            ApplyCommand = new RelayCommand(_ => Close?.Invoke(true),
                                            _ => (copyView || copySheets) && Rows.Any(row => row.IsChosen));
        }

        /// <summary>Set by the window: closes it, true for Apply.</summary>
        public Action<bool> Close { get; set; }

        /// <summary>The pairing being copied.</summary>
        public LoadingPlanLink Source { get; }

        public string Heading => "Copy the loading plan of " + Source.Story;

        public string SourceView => Source.ViewName.Length == 0 ? "(no view)" : Source.ViewName;

        public string SourceSheets
        {
            get
            {
                if (Source.WorkbookPath.Length == 0)
                {
                    return "(no loading schedule)";
                }
                return Path.GetFileName(Source.WorkbookPath)
                       + "  -  area: " + (Source.SheetName.Length == 0 ? "(none)" : Source.SheetName)
                       + ",  line: " + (Source.LineLoadSheetName.Length == 0 ? "(none)" : Source.LineLoadSheetName);
            }
        }

        public ObservableCollection<LoadingPlanCopyRow> Rows { get; } =
            new ObservableCollection<LoadingPlanCopyRow>();

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectUnpairedCommand { get; }
        public ICommand SelectSimilarCommand { get; }
        public ICommand ApplyCommand { get; }

        /// <summary>Whether the ticked levels get the view.</summary>
        public bool CopyView
        {
            get => copyView;
            set
            {
                if (SetProperty(ref copyView, value))
                {
                    OnPropertyChanged(nameof(Summary));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>Whether the ticked levels get the workbook, the area load sheet and the line load sheet.</summary>
        public bool CopySheets
        {
            get => copySheets;
            set
            {
                if (SetProperty(ref copySheets, value))
                {
                    OnPropertyChanged(nameof(Summary));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string Summary
        {
            get
            {
                int chosen = Rows.Count(row => row.IsChosen);
                if (!copyView && !copySheets)
                {
                    return "Tick the view, the sheets, or both.";
                }
                if (!copyView)
                {
                    return $"{chosen} level(s) chosen. Only the sheets are copied, so a level with no view of its own is left as it is.";
                }
                return $"{chosen} level(s) chosen.";
            }
        }

        /// <summary>
        /// What a level's pairing becomes: the source's view and sheets where they are copied,
        /// its own where they are not. Null when that leaves it with no view, since a level is
        /// only ever paired once a view has been said for it.
        /// </summary>
        public static LoadingPlanLink Merge(LoadingPlanLink source, LoadingPlanLink existing, string story,
                                            bool copyView, bool copySheets)
        {
            LoadingPlanLink link = existing == null ? new LoadingPlanLink() : existing.Copy();
            link.Story = story ?? "";

            if (copyView)
            {
                link.ViewId = source.ViewId;
                link.ViewName = source.ViewName;
            }
            if (copySheets)
            {
                link.WorkbookPath = source.WorkbookPath;
                link.SheetName = source.SheetName;
                link.LineLoadSheetName = source.LineLoadSheetName;
            }

            return link.ViewId == 0 ? null : link;
        }

        private void Choose(Func<LoadingPlanCopyRow, bool> rule)
        {
            foreach (LoadingPlanCopyRow row in Rows)
            {
                row.IsChosen = rule(row);
            }
        }
    }
}
