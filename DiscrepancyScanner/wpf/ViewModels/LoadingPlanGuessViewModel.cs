using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One level in the guess window: whether it is to be guessed, and the view it would get.
    /// </summary>
    public sealed class LoadingPlanGuessRow : ViewModelBase
    {
        private bool isChosen;
        private LoadingPlanView pickedView;

        private string borrowedFrom = "";

        public LoadingPlanGuessRow(string story, string currentState, bool chosen, string similarTo = "")
        {
            Story = story ?? "";
            CurrentState = currentState ?? "";
            isChosen = chosen;
            SimilarTo = similarTo ?? "";
        }

        public string Story { get; }

        /// <summary>The master story ETABS says this level is similar to; empty for none.</summary>
        public string SimilarTo { get; }

        /// <summary>
        /// The master story the candidates were found for, when this level's own name found
        /// none and they are its typical floor's views instead. Empty when they are its own.
        /// </summary>
        public string BorrowedFrom => borrowedFrom;

        /// <summary>What the level is paired with today, so overwriting it is a decision made in sight.</summary>
        public string CurrentState { get; }

        /// <summary>Whether this level is guessed when the window is applied.</summary>
        public bool IsChosen
        {
            get => isChosen;
            set => SetProperty(ref isChosen, value);
        }

        /// <summary>
        /// Every view whose name carries both the level's name and the loading plan phrase.
        /// More than one is offered rather than decided: picking among three is a click, and
        /// picking wrong is a floor read off another floor's plan.
        /// </summary>
        public ObservableCollection<LoadingPlanView> Candidates { get; } =
            new ObservableCollection<LoadingPlanView>();

        public LoadingPlanView PickedView
        {
            get => pickedView;
            set
            {
                if (SetProperty(ref pickedView, value))
                {
                    OnPropertyChanged(nameof(MatchText));
                }
            }
        }

        public bool HasCandidates => Candidates.Count > 0;

        /// <summary>What the guess found, beside the view it would pair.</summary>
        public string MatchText
        {
            get
            {
                if (Candidates.Count == 0)
                {
                    return "No view matches";
                }
                if (borrowedFrom.Length > 0)
                {
                    return "Similar to " + borrowedFrom + " in ETABS"
                           + (Candidates.Count > 1 && pickedView == null ? " - pick one" : "");
                }
                if (Candidates.Count == 1)
                {
                    return "1 match";
                }
                return pickedView == null
                    ? Candidates.Count + " matches - pick one"
                    : Candidates.Count + " matches";
            }
        }

        public void SetCandidates(IEnumerable<LoadingPlanView> views, string from = "")
        {
            LoadingPlanView keep = pickedView;
            borrowedFrom = from ?? "";

            Candidates.Clear();
            foreach (LoadingPlanView view in views)
            {
                Candidates.Add(view);
            }

            // A pick survives a change of phrase as long as the view is still a candidate;
            // otherwise only a single candidate is picked on its own.
            pickedView = keep != null && Candidates.Contains(keep)
                ? keep
                : Candidates.Count == 1 ? Candidates[0] : null;

            OnPropertyChanged(nameof(PickedView));
            OnPropertyChanged(nameof(HasCandidates));
            OnPropertyChanged(nameof(BorrowedFrom));
            OnPropertyChanged(nameof(MatchText));
        }
    }

    /// <summary>
    /// "Guess by name", as a window of its own: which levels to guess, what a loading plan
    /// view is called, and which sheets of the schedule the guessed levels read.
    ///
    /// The guess is shown before it is made. Every level says which view it would get, so a
    /// phrase that misses is seen and corrected here rather than discovered forty-seven rows
    /// later. Nothing is written until Apply; Cancel leaves the pairings as they were.
    /// </summary>
    public sealed class LoadingPlanGuessViewModel : ViewModelBase
    {
        private readonly List<LoadingPlanView> allViews;

        private string markers;
        private string required = "";
        private string workbookPath;
        private string areaSheet = "";
        private string lineSheet = "";

        public LoadingPlanGuessViewModel(IEnumerable<LoadingPlanGuessRow> rows,
                                         IEnumerable<LoadingPlanView> views,
                                         string loadingPlanMarkers,
                                         string workbook, string wantedAreaSheet, string wantedLineSheet)
        {
            allViews = (views ?? new LoadingPlanView[0]).ToList();
            markers = loadingPlanMarkers ?? "";

            foreach (LoadingPlanGuessRow row in rows ?? new LoadingPlanGuessRow[0])
            {
                Rows.Add(row);
                row.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(LoadingPlanGuessRow.IsChosen)
                        || e.PropertyName == nameof(LoadingPlanGuessRow.PickedView))
                    {
                        OnPropertyChanged(nameof(Summary));
                        CommandManager.InvalidateRequerySuggested();
                    }
                };
            }

            SelectAllCommand = new RelayCommand(_ => Choose(row => true));
            SelectNoneCommand = new RelayCommand(_ => Choose(row => false));
            SelectUnpairedCommand = new RelayCommand(_ => Choose(row => row.CurrentState.Length == 0));
            PickWorkbookCommand = new RelayCommand(_ => PickWorkbook());
            ApplyCommand = new RelayCommand(_ => Close?.Invoke(true), _ => ReadyCount > 0);

            workbookPath = "";
            if (!string.IsNullOrEmpty(workbook))
            {
                OpenWorkbook(workbook, wantedAreaSheet, wantedLineSheet);
            }

            RefreshCandidates();
        }

        /// <summary>Set by the window to put the workbook picker up; answers the path, or null.</summary>
        public Func<string> PickWorkbookFile { get; set; }

        /// <summary>Set by the window to say what went wrong reading a workbook.</summary>
        public Action<string> ReportProblem { get; set; }

        /// <summary>Set by the window: closes it, true for Apply.</summary>
        public Action<bool> Close { get; set; }

        public ObservableCollection<LoadingPlanGuessRow> Rows { get; } =
            new ObservableCollection<LoadingPlanGuessRow>();

        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }
        public ICommand SelectUnpairedCommand { get; }
        public ICommand PickWorkbookCommand { get; }
        public ICommand ApplyCommand { get; }

        /// <summary>
        /// What a loading plan view's name contains. Only a view whose name carries this and
        /// the level's name is offered for that level. Comma separated for a job that uses more
        /// than one wording; left empty, the level's name is all that is asked for.
        /// </summary>
        public string LoadingPlanMarkers
        {
            get => markers;
            set
            {
                if (SetProperty(ref markers, value ?? ""))
                {
                    RefreshCandidates();
                }
            }
        }

        /// <summary>
        /// What a loading plan view's name must also carry - every comma separated piece of it,
        /// where <see cref="LoadingPlanMarkers"/> is any one of its pieces. For a floor with
        /// several loading plans of its own: one per tower, or SDL and live load drawn apart.
        /// Left empty, nothing more is asked for.
        /// </summary>
        public string RequiredText
        {
            get => required;
            set
            {
                if (SetProperty(ref required, value ?? ""))
                {
                    RefreshCandidates();
                }
            }
        }

        /// <summary>Levels chosen that have a view to pair with - what Apply will actually pair.</summary>
        public int ReadyCount => Rows.Count(row => row.IsChosen && row.PickedView != null);

        public string Summary
        {
            get
            {
                int chosen = Rows.Count(row => row.IsChosen);
                int ready = ReadyCount;
                string line = $"{ready} of {chosen} chosen level(s) will be paired.";
                if (chosen > ready)
                {
                    line += $" {chosen - ready} have no view picked and will be left as they are.";
                }
                return line;
            }
        }

        #region the schedule

        public ObservableCollection<string> ScheduleSheets { get; } = new ObservableCollection<string>();

        public string WorkbookPath => workbookPath;

        public string WorkbookName => workbookPath.Length == 0
            ? "No loading schedule picked - the levels will be paired with their view only."
            : Path.GetFileName(workbookPath);

        /// <summary>The sheet every guessed level reads its area loads from.</summary>
        public string SelectedAreaSheet
        {
            get => areaSheet;
            set => SetProperty(ref areaSheet, value ?? "");
        }

        /// <summary>The sheet every guessed level reads its line loads from; empty for none.</summary>
        public string SelectedLineSheet
        {
            get => lineSheet;
            set => SetProperty(ref lineSheet, value ?? "");
        }

        private void PickWorkbook()
        {
            string path = PickWorkbookFile == null ? null : PickWorkbookFile();
            if (!string.IsNullOrEmpty(path))
            {
                OpenWorkbook(path, areaSheet, lineSheet);
            }
        }

        private void OpenWorkbook(string path, string wantedAreaSheet, string wantedLineSheet)
        {
            try
            {
                IList<string> sheets = LoadScheduleReader.SheetNames(path);

                workbookPath = path;
                ScheduleSheets.Clear();
                foreach (string name in sheets)
                {
                    ScheduleSheets.Add(name);
                }

                areaSheet = sheets.FirstOrDefault(
                    name => string.Equals(name, wantedAreaSheet, StringComparison.OrdinalIgnoreCase)) ?? "";
                lineSheet = sheets.FirstOrDefault(
                    name => string.Equals(name, wantedLineSheet, StringComparison.OrdinalIgnoreCase)) ?? "";
            }
            catch (Exception e)
            {
                workbookPath = "";
                ScheduleSheets.Clear();
                areaSheet = "";
                lineSheet = "";

                ReportProblem?.Invoke("That workbook could not be read: " + e.Message);
            }

            OnPropertyChanged(nameof(WorkbookPath));
            OnPropertyChanged(nameof(WorkbookName));
            OnPropertyChanged(nameof(SelectedAreaSheet));
            OnPropertyChanged(nameof(SelectedLineSheet));
        }

        #endregion

        private void Choose(Func<LoadingPlanGuessRow, bool> rule)
        {
            foreach (LoadingPlanGuessRow row in Rows)
            {
                row.IsChosen = rule(row);
            }
        }

        private void RefreshCandidates()
        {
            foreach (LoadingPlanGuessRow row in Rows)
            {
                // A typical floor's level whose own name is on no view reads its master's
                // view: one plan drawn for the master, and every similar level built to it.
                List<LoadingPlanView> own = CandidatesFor(allViews, row.Story, markers, required);
                if (own.Count == 0 && row.SimilarTo.Length > 0)
                {
                    List<LoadingPlanView> master = CandidatesFor(allViews, row.SimilarTo, markers, required);
                    if (master.Count > 0)
                    {
                        row.SetCandidates(master, row.SimilarTo);
                        continue;
                    }
                }
                row.SetCandidates(own);
            }

            OnPropertyChanged(nameof(Summary));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// The views a level could be paired with: its name carries the level's name as a word
        /// of its own, or a run of levels it is in ("L05-L20"), and - where a phrase is given -
        /// the phrase as well. The phrase is a
        /// requirement rather than a preference, so a floor with only a general arrangement
        /// plan is left unpaired instead of being read off the wrong drawing.
        ///
        /// ETABS levels are often named "L01_LEVEL 1" where the drawing says "LEVEL 1 LOADING
        /// PLAN". The whole name is tried first; only where no view carries it are the parts
        /// either side of an underscore tried, each still bounded so "LEVEL 1" is not "LEVEL 10".
        ///
        /// <paramref name="required"/> narrows it further: every one of its comma separated
        /// pieces has to be in the name as well.
        /// </summary>
        public static List<LoadingPlanView> CandidatesFor(IEnumerable<LoadingPlanView> views,
                                                          string story, string markers,
                                                          string required = "")
        {
            bool anyPhrase = !string.IsNullOrWhiteSpace((markers ?? "").Replace(",", ""));

            List<LoadingPlanView> plans = (views ?? new LoadingPlanView[0])
                .Where(view => !anyPhrase || LoadingPlansViewModel.LooksLikeALoadingPlan(view.Name, markers))
                .Where(view => LoadingPlansViewModel.CarriesAllOf(view.Name, required))
                .ToList();

            List<LoadingPlanView> whole = plans
                .Where(view => LoadingPlansViewModel.CoversTheLevel(view.Name, story))
                .ToList();
            if (whole.Count > 0)
            {
                return whole;
            }

            string[] parts = (story ?? "")
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToArray();
            if (parts.Length < 2)
            {
                return whole;
            }

            return plans
                .Where(view => parts.Any(part => LoadingPlansViewModel.CoversTheLevel(view.Name, part)))
                .ToList();
        }
    }
}
