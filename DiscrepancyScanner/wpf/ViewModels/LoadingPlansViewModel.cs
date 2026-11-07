using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>What a level's pairing amounts to, which is not simply paired or not.</summary>
    public enum LoadingPlanState
    {
        /// <summary>No loading plan has been paired with this level.</summary>
        Unpaired,

        /// <summary>Paired with a view and a schedule sheet: ready to check.</summary>
        Ready,

        /// <summary>
        /// Paired, but the view it names is not in this project any more - renamed is fine,
        /// deleted is not, and a pairing saved against another copy of the project lands here
        /// too. Reads as paired until the check runs and finds nothing, so it is called out.
        /// </summary>
        ViewMissing,

        /// <summary>Paired with a view, but no schedule sheet was ever picked for it.</summary>
        NoSchedule
    }

    /// <summary>One ETABS level and the loading plan it is paired with, as the table draws it.</summary>
    public sealed class LoadingPlanRow : ViewModelBase
    {
        public LoadingPlanRow(string story)
        {
            Story = story ?? "";
        }

        public string Story { get; }

        public string ViewName { get; private set; } = "";
        public string WorkbookName { get; private set; } = "";
        public string SheetName { get; private set; } = "";

        /// <summary>
        /// The sheet this level's line loads are on, when it has one.
        ///
        /// Shown in its own column rather than folded into the one beside it, because the two
        /// checks are paired separately and a level set up for one and not the other is the
        /// ordinary case rather than a half finished one. A blank here is a level whose line
        /// loads are simply not checked.
        /// </summary>
        public string LineSheetName { get; private set; } = "";

        public LoadingPlanState State { get; private set; } = LoadingPlanState.Unpaired;

        /// <summary>True for a level that will actually be checked, for the count at the foot.</summary>
        public bool IsReady { get { return State == LoadingPlanState.Ready; } }

        /// <summary>What the row says about itself, in the column beside the level.</summary>
        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case LoadingPlanState.Ready:
                        return "Ready";
                    case LoadingPlanState.ViewMissing:
                        return "View is gone";
                    case LoadingPlanState.NoSchedule:
                        return "No schedule";
                    default:
                        return "Not paired";
                }
            }
        }

        /// <summary>
        /// The colour of the dot on the row, using the same three the plan is drawn in: green
        /// for a level that will be checked, red for a pairing that is broken, amber for one
        /// that is half made, grey for one nobody has made yet.
        /// </summary>
        public string Swatch
        {
            get
            {
                switch (State)
                {
                    case LoadingPlanState.Ready:
                        return "#FF1EA84C";
                    case LoadingPlanState.ViewMissing:
                        return "#FFD61F26";
                    case LoadingPlanState.NoSchedule:
                        return "#FFE8820C";
                    default:
                        return "#FFCCCCCC";
                }
            }
        }

        /// <summary>Re-reads the row from the pairing store and the views the project has now.</summary>
        public void Refresh(LoadingPlanLink link, bool viewResolves)
        {
            if (link == null || link.ViewId == 0)
            {
                ViewName = "";
                WorkbookName = "";
                SheetName = "";
                LineSheetName = "";
                State = LoadingPlanState.Unpaired;
            }
            else
            {
                ViewName = link.ViewName;
                WorkbookName = link.WorkbookPath.Length == 0 ? "" : Path.GetFileName(link.WorkbookPath);
                SheetName = link.SheetName;
                LineSheetName = link.LineLoadSheetName;

                State = !viewResolves
                    ? LoadingPlanState.ViewMissing
                    : link.WorkbookPath.Length == 0 || link.SheetName.Length == 0
                        ? LoadingPlanState.NoSchedule
                        : LoadingPlanState.Ready;
            }

            OnPropertyChanged(nameof(ViewName));
            OnPropertyChanged(nameof(WorkbookName));
            OnPropertyChanged(nameof(SheetName));
            OnPropertyChanged(nameof(LineSheetName));
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Swatch));
            OnPropertyChanged(nameof(IsReady));
        }

        public override string ToString()
        {
            return $"{Story}: {StatusText}";
        }
    }

    /// <summary>
    /// Pairing each ETABS level with the Revit view its loading plan is drawn in and the sheet
    /// of the loading schedule its numbers mean something in.
    ///
    /// A window of its own rather than four controls down the side of the plan, because the
    /// question is not "which plan for this floor" but "which floors have I done and which
    /// have I not" - and that is a table, with the levels that are ready, the ones nobody has
    /// paired, and the ones paired to a view that is not there any more all readable at once.
    ///
    /// Every edit goes straight into the pairing store, which is the only copy of it. There is
    /// nothing to cancel: pairing a level is one visible act, and unpairing it is another.
    /// </summary>
    public sealed class LoadingPlansViewModel : ViewModelBase
    {
        private readonly LoadingPlanLinks links;
        private readonly string projectKey;
        private readonly List<LoadingPlanView> allViews;

        /// <summary>The master story each typical floor's level is similar to in ETABS, by level.</summary>
        private readonly Dictionary<string, string> similarTo =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private LoadingPlanRow selectedRow;
        private string viewSearch = "";
        private LoadingPlanView selectedView;
        private string workbookPath = "";
        private string selectedSheet = "";
        private string selectedLineSheet = "";

        /// <summary>
        /// True while the editor is being filled in from a row that was just selected, so the
        /// filling does not read as the user pairing something and write itself back.
        /// </summary>
        private bool loadingRow;

        public LoadingPlansViewModel(LoadingPlanLinks pairings, string project,
                                     IEnumerable<LoadingPlanView> views, IEnumerable<Story_E> stories)
        {
            links = pairings;
            projectKey = project ?? "";
            allViews = (views ?? new LoadingPlanView[0]).ToList();

            foreach (Story_E story in stories ?? new Story_E[0])
            {
                if (story != null && !string.IsNullOrEmpty(story.Name))
                {
                    Rows.Add(new LoadingPlanRow(story.Name));
                    similarTo[story.Name] = story.SimilarTo;
                }
            }

            PickWorkbookCommand = new RelayCommand(_ => PickWorkbook(), _ => selectedRow != null);
            UnpairCommand = new RelayCommand(_ => Unpair(),
                                             _ => selectedRow != null && selectedRow.State != LoadingPlanState.Unpaired);
            ClearSearchCommand = new RelayCommand(_ => ViewSearch = "", _ => viewSearch.Length > 0);
            GuessCommand = new RelayCommand(_ => GuessByName(), _ => Rows.Count > 0 && allViews.Count > 0);
            CopyToCommand = new RelayCommand(_ => CopyTo(),
                                             _ => selectedRow != null && selectedRow.State != LoadingPlanState.Unpaired
                                                  && Rows.Count > 1);
            ViewAreaSheetCommand = new RelayCommand(_ => ViewSheet(selectedSheet), _ => workbookPath.Length > 0);
            ViewLineSheetCommand = new RelayCommand(_ => ViewSheet(selectedLineSheet), _ => workbookPath.Length > 0);
            LoadPatternsCommand = new RelayCommand(_ => EditLoadPatterns());

            RefreshViews();
            RefreshRows();

            SelectedRow = Rows.FirstOrDefault();
        }

        /// <summary>Set by the window to put the workbook picker up; answers the path, or null.</summary>
        public Func<string> PickWorkbookFile { get; set; }

        /// <summary>Set by the window to say what went wrong reading a workbook.</summary>
        public Action<string> ReportProblem { get; set; }

        #region the table

        /// <summary>One row per ETABS level, whether or not it has ever been paired.</summary>
        public ObservableCollection<LoadingPlanRow> Rows { get; } = new ObservableCollection<LoadingPlanRow>();

        /// <summary>False before an ETABS model has been imported, when there is nothing to pair.</summary>
        public bool HasStories { get { return Rows.Count > 0; } }

        public bool HasNoStories { get { return Rows.Count == 0; } }

        public LoadingPlanRow SelectedRow
        {
            get => selectedRow;
            set
            {
                if (SetProperty(ref selectedRow, value))
                {
                    LoadEditorFromRow();
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(EditorHeading));
                }
            }
        }

        public bool HasSelection { get { return selectedRow != null; } }

        /// <summary>What the right hand side is editing, named so it cannot be mistaken.</summary>
        public string EditorHeading
        {
            get
            {
                return selectedRow == null
                    ? "Pick a level on the left."
                    : "The loading plan for " + selectedRow.Story;
            }
        }

        // How many levels stand in each state, for the strip above the table. A count per
        // state rather than one sentence, because the four say different things: ready is done,
        // a missing view is broken and wants fixing, a missing sheet is half done and wants
        // finishing, and unpaired is simply work not started - which on a forty-seven storey
        // model is the normal condition and not a fault.

        public int ReadyCount => Rows.Count(row => row.State == LoadingPlanState.Ready);
        public int NoScheduleCount => Rows.Count(row => row.State == LoadingPlanState.NoSchedule);
        public int ViewMissingCount => Rows.Count(row => row.State == LoadingPlanState.ViewMissing);
        public int UnpairedCount => Rows.Count(row => row.State == LoadingPlanState.Unpaired);

        public bool HasNoSchedule => NoScheduleCount > 0;
        public bool HasViewMissing => ViewMissingCount > 0;

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(ReadyCount));
            OnPropertyChanged(nameof(NoScheduleCount));
            OnPropertyChanged(nameof(ViewMissingCount));
            OnPropertyChanged(nameof(UnpairedCount));
            OnPropertyChanged(nameof(HasNoSchedule));
            OnPropertyChanged(nameof(HasViewMissing));
            OnPropertyChanged(nameof(GuessSummary));
            CommandManager.InvalidateRequerySuggested();
        }

        public string Summary
        {
            get
            {
                if (Rows.Count == 0)
                {
                    return "Import the ETABS model first: its levels are what a loading plan is paired with.";
                }

                int ready = Rows.Count(row => row.IsReady);
                int broken = Rows.Count(row => row.State == LoadingPlanState.ViewMissing
                                               || row.State == LoadingPlanState.NoSchedule);

                string line = $"{ready} of {Rows.Count} level(s) ready to have their loads checked.";

                return broken == 0
                    ? line
                    : line + $" {broken} more {(broken == 1 ? "is" : "are")} paired but not usable yet.";
            }
        }

        #endregion

        #region the searchable list of views

        /// <summary>The project's views, narrowed to whatever the search box says.</summary>
        public ObservableCollection<LoadingPlanView> Views { get; } =
            new ObservableCollection<LoadingPlanView>();

        /// <summary>
        /// Narrows the view list. Matched on the name without regard to case, and on every word
        /// typed rather than on the phrase, so "l03 load" finds "L03 - LOADING PLAN" without
        /// anyone having to remember which way round the words go or what punctuation is
        /// between them.
        /// </summary>
        public string ViewSearch
        {
            get => viewSearch;
            set
            {
                if (SetProperty(ref viewSearch, value ?? ""))
                {
                    RefreshViews();
                    OnPropertyChanged(nameof(ViewCountText));
                }
            }
        }

        public string ViewCountText
        {
            get
            {
                if (allViews.Count == 0)
                {
                    return "This project has no plan views in it.";
                }
                return viewSearch.Trim().Length == 0
                    ? $"{allViews.Count} plan view(s) in this project."
                    : $"{Views.Count} of {allViews.Count} match.";
            }
        }

        public ICommand ClearSearchCommand { get; }

        private void RefreshViews()
        {
            LoadingPlanView keep = selectedView;

            Views.Clear();
            foreach (LoadingPlanView view in allViews.Where(view => SearchMatches(view.Name, viewSearch)))
            {
                Views.Add(view);
            }

            // A search that no longer contains the paired view would otherwise clear the
            // picker, which would read as the level having been unpaired.
            if (keep != null && !Views.Contains(keep))
            {
                Views.Insert(0, keep);
            }

            OnPropertyChanged(nameof(ViewCountText));
        }

        /// <summary>
        /// Whether a view name answers the search box. Typed with commas, every comma separated
        /// phrase has to be in the name, matched the way "Guess by name" matches - so "TOWER A,
        /// SDL" finds the Tower A SDL plan and not Tower B's, whose name has an A in it too.
        /// Typed without, every word has to be in it, in any order, so "l03 plan" still finds
        /// "L03 - LOADING PLAN".
        /// </summary>
        public static bool SearchMatches(string viewName, string search)
        {
            string name = viewName ?? "";
            string typed = search ?? "";

            if (typed.IndexOf(',') >= 0)
            {
                return CarriesAllOf(name, typed);
            }

            foreach (string word in typed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (name.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The view this level's loading plan is drawn in.</summary>
        public LoadingPlanView SelectedView
        {
            get => selectedView;
            set
            {
                if (SetProperty(ref selectedView, value) && !loadingRow)
                {
                    Pair();
                }
            }
        }

        #endregion

        #region the schedule

        public ICommand PickWorkbookCommand { get; }
        public ICommand UnpairCommand { get; }
        public ICommand GuessCommand { get; }
        public ICommand CopyToCommand { get; }

        /// <summary>What the guess button offers to do, so it is not a surprise.</summary>
        public string GuessSummary => "Choose which levels to pair with the view whose name carries "
              + "both the level's name and \"" + LoadingPlanMarkers + "\""
              + (LoadingPlanRequired.Trim().Length > 0 ? ", and all of \"" + LoadingPlanRequired + "\"" : "")
              + ", and which schedule sheets they read. Every guess is shown before anything is paired.";

        /// <summary>
        /// Text a loading plan view's name must also carry, every comma separated piece of it.
        /// A setting like <see cref="LoadingPlanMarkers"/>, and edited beside it.
        /// </summary>
        public string LoadingPlanRequired
        {
            get { return PrefrencesData.getPrefrencesData().LoadingPlanViewNameRequired ?? ""; }
            set
            {
                string wanted = value ?? "";
                if (wanted == LoadingPlanRequired)
                {
                    return;
                }

                PrefrencesData.getPrefrencesData().LoadingPlanViewNameRequired = wanted;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GuessSummary));
            }
        }

        /// <summary>
        /// What a loading plan view is called on this job. An ordinary setting, edited in the
        /// guess window as well because that is where its effect is seen. It writes straight
        /// into the settings, which are saved when this window is closed.
        /// </summary>
        public string LoadingPlanMarkers
        {
            get { return PrefrencesData.getPrefrencesData().LoadingPlanViewNamePatterns ?? ""; }
            set
            {
                string wanted = value ?? "";
                if (wanted == LoadingPlanMarkers)
                {
                    return;
                }

                PrefrencesData.getPrefrencesData().LoadingPlanViewNamePatterns = wanted;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GuessSummary));
            }
        }

        /// <summary>
        /// Set by the window to put the guess window up; answers true when it was applied.
        /// </summary>
        public Func<LoadingPlanGuessViewModel, bool> ShowGuessWindow { get; set; }

        /// <summary>
        /// Pairs levels with the plan views whose names carry the level's name and the loading
        /// plan phrase, in a window where the levels are chosen and every guess is seen first.
        ///
        /// Forty-seven levels named after their views is the ordinary case, and pairing them one
        /// at a time is an afternoon nobody should spend. The rule stays timid: "L1" cannot take
        /// the view meant for "L10", a view without the phrase is never offered, and where more
        /// than one view qualifies the choice is left to a person.
        ///
        /// Unpaired levels are chosen to start with. An already paired level is only re-paired
        /// when it is ticked, so a decision is never undone without being seen.
        /// </summary>
        private void GuessByName()
        {
            if (ShowGuessWindow == null)
            {
                return;
            }

            List<LoadingPlanGuessRow> rows = Rows
                .Select(row => new LoadingPlanGuessRow(
                    row.Story,
                    row.State == LoadingPlanState.Unpaired ? "" : row.StatusText + ": " + row.ViewName,
                    row.State == LoadingPlanState.Unpaired,
                    SimilarTo(row.Story)))
                .ToList();

            string workbook = workbookPath.Length > 0 ? workbookPath : links.AnyWorkbook(projectKey);

            LoadingPlanGuessViewModel guess = new LoadingPlanGuessViewModel(
                rows, allViews, LoadingPlanMarkers, workbook, selectedSheet, selectedLineSheet)
            {
                RequiredText = LoadingPlanRequired
            };

            if (!ShowGuessWindow(guess))
            {
                return;
            }

            LoadingPlanMarkers = guess.LoadingPlanMarkers;
            LoadingPlanRequired = guess.RequiredText;

            int paired = 0;
            foreach (LoadingPlanGuessRow row in guess.Rows.Where(r => r.IsChosen && r.PickedView != null))
            {
                links.Set(projectKey, new LoadingPlanLink
                {
                    Story = row.Story,
                    ViewId = row.PickedView.Id,
                    ViewName = row.PickedView.Name,
                    WorkbookPath = guess.WorkbookPath,
                    SheetName = guess.SelectedAreaSheet,
                    LineLoadSheetName = guess.SelectedLineSheet
                });
                paired++;
            }

            RefreshRows();
            LoadEditorFromRow();

            ReportProblem?.Invoke(paired + " level(s) paired by name."
                                  + (guess.WorkbookPath.Length == 0 || guess.SelectedAreaSheet.Length == 0
                                     ? " They still need an area load sheet."
                                     : ""));
        }

        /// <summary>
        /// Set by the window to put the copy window up; answers true when it was applied.
        /// </summary>
        public Func<LoadingPlanCopyViewModel, bool> ShowCopyWindow { get; set; }

        /// <summary>
        /// Gives the selected level's pairing - its view, workbook and sheets - to the levels
        /// ticked in a window of their own. A typical floor is one loading plan read by a run
        /// of levels, so it is paired once and copied, rather than paired twenty times over.
        ///
        /// Nothing is ticked to start with, and every level says what it is paired with now,
        /// so a pairing is never overwritten without being seen.
        /// </summary>
        private void CopyTo()
        {
            if (ShowCopyWindow == null || selectedRow == null)
            {
                return;
            }

            LoadingPlanLink source = links.Find(projectKey, selectedRow.Story);
            if (source == null || source.ViewId == 0)
            {
                return;
            }

            string sourceMaster = SimilarTo(source.Story);

            List<LoadingPlanCopyRow> rows = Rows
                .Where(row => !string.Equals(row.Story, source.Story, StringComparison.OrdinalIgnoreCase))
                .Select(row =>
                {
                    string master = SimilarTo(row.Story);
                    bool similar = string.Equals(master, source.Story, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(sourceMaster, row.Story, StringComparison.OrdinalIgnoreCase)
                                   || (master.Length > 0
                                       && string.Equals(master, sourceMaster, StringComparison.OrdinalIgnoreCase));
                    return new LoadingPlanCopyRow(
                        row.Story,
                        row.State == LoadingPlanState.Unpaired ? "" : row.StatusText + ": " + row.ViewName,
                        similar);
                })
                .ToList();

            LoadingPlanCopyViewModel copy = new LoadingPlanCopyViewModel(source, rows);
            if (!ShowCopyWindow(copy))
            {
                return;
            }

            int copied = 0;
            int skipped = 0;
            foreach (LoadingPlanCopyRow row in copy.Rows.Where(r => r.IsChosen))
            {
                LoadingPlanLink link = LoadingPlanCopyViewModel.Merge(
                    source, links.Find(projectKey, row.Story), row.Story, copy.CopyView, copy.CopySheets);
                if (link == null)
                {
                    skipped++;
                    continue;
                }

                links.Set(projectKey, link);
                copied++;
            }

            RefreshRows();
            LoadEditorFromRow();

            ReportProblem?.Invoke(copied + " level(s) given the loading plan of " + source.Story + "."
                                  + (skipped > 0
                                     ? " " + skipped + " had no view of their own to keep the sheets with, and were left as they are."
                                     : ""));
        }

        /// <summary>The master story a level is similar to in ETABS, or empty.</summary>
        private string SimilarTo(string story)
        {
            string master;
            return story != null && similarTo.TryGetValue(story, out master) ? master ?? "" : "";
        }

        /// <summary>
        /// Whether a view name says it is a loading plan, by any of the phrases the settings
        /// list. Matched as plain text anywhere in the name rather than on a word boundary,
        /// because the phrase is the whole point of the name and is written every way a
        /// drawing office writes anything - "L03 LOADING PLAN", "L03_LoadingPlan (2)".
        /// </summary>
        public static bool LooksLikeALoadingPlan(string viewName, string patterns)
        {
            if (string.IsNullOrEmpty(viewName) || string.IsNullOrEmpty(patterns))
            {
                return false;
            }

            string haystack = Squash(viewName);
            foreach (string phrase in patterns.Split(new[] { ',' },
                                                     StringSplitOptions.RemoveEmptyEntries))
            {
                string needle = Squash(phrase);
                if (needle.Length > 0 && haystack.IndexOf(needle, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Whether a view name carries every one of the comma separated phrases, matched the
        /// way <see cref="LooksLikeALoadingPlan"/> matches one. True when there are none: an
        /// empty list asks for nothing.
        /// </summary>
        public static bool CarriesAllOf(string viewName, string phrases)
        {
            string haystack = Squash(viewName ?? "");

            foreach (string phrase in (phrases ?? "").Split(new[] { ',' },
                                                            StringSplitOptions.RemoveEmptyEntries))
            {
                string needle = Squash(phrase);
                if (needle.Length > 0 && haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Upper case with the spaces, dashes and underscores taken out, so "LoadingPlan",
        /// "LOADING_PLAN" and "Loading - Plan" are one phrase rather than three.
        /// </summary>
        private static string Squash(string text)
        {
            char[] kept = new char[text.Length];
            int length = 0;
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    kept[length++] = char.ToUpperInvariant(c);
                }
            }
            return new string(kept, 0, length);
        }

        /// <summary>
        /// Whether a view name carries a level's name as a word of its own.
        ///
        /// Bounded on both sides so "L1" does not match "L10" or "SL1": a level name inside a
        /// longer run of letters and digits is a different name that happens to contain this
        /// one, and pairing on that is how the wrong plan gets read for a whole floor.
        /// </summary>
        public static bool NamesTheLevel(string viewName, string story)
        {
            string haystack = (viewName ?? "").ToUpperInvariant();
            string needle = (story ?? "").Trim().ToUpperInvariant();
            if (needle.Length == 0 || haystack.Length == 0)
            {
                return false;
            }

            int at = haystack.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                bool startsClean = at == 0 || !IsNameCharacter(haystack[at - 1]);
                int after = at + needle.Length;
                bool endsClean = after >= haystack.Length || !IsNameCharacter(haystack[after]);

                if (startsClean && endsClean)
                {
                    return true;
                }
                at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal);
            }
            return false;
        }

        private static bool IsNameCharacter(char c)
        {
            return char.IsLetterOrDigit(c);
        }

        /// <summary>
        /// Whether a view name stands for a level: names it outright, or names the run of
        /// levels it is in.
        ///
        /// A typical floor is one view for a run of levels, and the drawing office names it
        /// after the run - "L05-L20 LOADING PLAN", "LEVEL 5 TO 20 LOADING PLAN" - so the levels
        /// in the middle of it never appear in the name at all. A level numbered like the run
        /// (the same letters in front, a number between the two ends) is in it; a level
        /// numbered with other letters is not, so "B1-L3" is no range of anything. "L5" and
        /// "L05" are the same level here, the number being read as a number.
        /// </summary>
        public static bool CoversTheLevel(string viewName, string story)
        {
            if (NamesTheLevel(viewName, story))
            {
                return true;
            }

            string prefix;
            int number;
            if (!NumberedLevel(story, out prefix, out number))
            {
                return false;
            }

            foreach (Match match in NumberedRun.Matches((viewName ?? "").ToUpperInvariant()))
            {
                if (match.Groups[1].Value != prefix)
                {
                    continue;
                }

                int from;
                if (!int.TryParse(match.Groups[2].Value, out from))
                {
                    continue;
                }

                if (!match.Groups[4].Success)
                {
                    if (from == number)
                    {
                        return true;
                    }
                    continue;
                }

                int to;
                bool samePrefix = !match.Groups[3].Success || match.Groups[3].Value == prefix;
                if (samePrefix && int.TryParse(match.Groups[4].Value, out to)
                    && from < to && number >= from && number <= to)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A level named as letters and a number - "L05", "LEVEL 5", "Story12" - read into its
        /// letters, upper case and without spaces or dashes, and its number.
        /// </summary>
        private static bool NumberedLevel(string story, out string prefix, out int number)
        {
            prefix = "";
            number = 0;

            Match match = NumberedStory.Match(story ?? "");
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out number))
            {
                return false;
            }

            prefix = match.Groups[1].Value.ToUpperInvariant();
            return true;
        }

        private static readonly Regex NumberedStory =
            new Regex(@"^\s*([A-Za-z]+)[\s_.\-]*(\d{1,6})\s*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// A numbered level in a view name, with the other end of its run where there is one:
        /// "L05", "L05-L20", "LEVEL 5 TO 20", "L5~12". Bounded on both sides like
        /// <see cref="NamesTheLevel"/>, so "SL5" is not L5. Run on the name in upper case.
        /// </summary>
        private static readonly Regex NumberedRun = new Regex(
            @"(?<![A-Z0-9])([A-Z]+)[\s_.\-]*(\d{1,6})"
            + @"(?:\s*(?:-|–|—|~|TO|THRU|THROUGH)\s*(?:([A-Z]+)[\s_.\-]*)?(\d{1,6}))?"
            + @"(?![A-Z0-9])",
            RegexOptions.CultureInvariant);

        /// <summary>The sheets in the picked workbook, for the picker.</summary>
        public ObservableCollection<string> ScheduleSheets { get; } = new ObservableCollection<string>();

        public string WorkbookName
        {
            get
            {
                return workbookPath.Length == 0 ? "No loading schedule picked." : Path.GetFileName(workbookPath);
            }
        }

        /// <summary>The sheet this level's area load numbers are on.</summary>
        public string SelectedScheduleSheet
        {
            get => selectedSheet;
            set
            {
                if (SetProperty(ref selectedSheet, value ?? "") && !loadingRow)
                {
                    Pair();
                }
            }
        }

        /// <summary>
        /// The sheet this level's line loads are on, which is a tab of the same workbook.
        ///
        /// The same workbook because that is how these schedules are kept - one file per
        /// project, a tab per part of the building for the area loads and a tab per part for
        /// the line loads - so the file has already been picked and only the tab is left.
        ///
        /// Left empty is a level whose line loads are not checked, which is not a fault: the
        /// area load check stands on its own and always did.
        /// </summary>
        public string SelectedLineLoadSheet
        {
            get => selectedLineSheet;
            set
            {
                if (SetProperty(ref selectedLineSheet, value ?? "") && !loadingRow)
                {
                    Pair();
                }
            }
        }

        public ICommand ViewAreaSheetCommand { get; }
        public ICommand ViewLineSheetCommand { get; }

        public ICommand LoadPatternsCommand { get; }

        /// <summary>
        /// The load patterns the imported ETABS model loads its shells and frames in, for the
        /// load pattern window to map. Empty before a model with loads has been imported.
        /// </summary>
        public IList<ModelLoadPattern> ModelPatterns { get; set; } = new List<ModelLoadPattern>();

        /// <summary>Set by the window to put the load pattern window up; answers true when it was closed on OK.</summary>
        public Func<LoadPatternsViewModel, bool> ShowLoadPatterns { get; set; }

        /// <summary>
        /// Which ETABS pattern is which load of the loading plan, and what the schedule calls
        /// each. Written into the settings only when the window is closed on OK; the settings
        /// file is written when this window closes, the way the pairings are.
        /// </summary>
        private void EditLoadPatterns()
        {
            if (ShowLoadPatterns == null)
            {
                return;
            }

            PrefrencesData preferences = PrefrencesData.getPrefrencesData();
            LoadPatternsViewModel patterns = new LoadPatternsViewModel(preferences, ModelPatterns);

            if (ShowLoadPatterns(patterns))
            {
                patterns.Apply(preferences);
            }
        }

        /// <summary>Set by the window to put a sheet of the schedule up, to be read and edited.</summary>
        public Action<ScheduleSheetViewModel> ShowScheduleSheet { get; set; }

        /// <summary>
        /// Shows a sheet of the schedule the way the check reads it, where it can be corrected.
        /// Opened on the sheet this level is paired with, or on the first one when it has none
        /// yet - the sheet list in the window is how the right one is found.
        /// </summary>
        private void ViewSheet(string sheet)
        {
            if (ShowScheduleSheet == null || workbookPath.Length == 0)
            {
                return;
            }

            ShowScheduleSheet(new ScheduleSheetViewModel(workbookPath, sheet, ScheduleEdits.Current));
        }

        private void PickWorkbook()
        {
            string path = PickWorkbookFile == null ? null : PickWorkbookFile();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            OpenWorkbook(path, null, null);
            Pair();
        }

        /// <summary>
        /// Lists a workbook's sheets, keeping the one asked for where it still has a sheet by
        /// that name. The sheet is never carried over from another level: the numbering starts
        /// again on every sheet, so which one a floor reads is the one thing here that must be
        /// said rather than inherited.
        /// </summary>
        private void OpenWorkbook(string path, string wantedSheet, string wantedLineSheet)
        {
            bool wasLoading = loadingRow;
            loadingRow = true;

            try
            {
                IList<string> sheets = LoadScheduleReader.SheetNames(path);

                workbookPath = path;

                ScheduleSheets.Clear();
                foreach (string name in sheets)
                {
                    ScheduleSheets.Add(name);
                }

                selectedSheet = sheets.FirstOrDefault(
                    name => string.Equals(name, wantedSheet, StringComparison.OrdinalIgnoreCase)) ?? "";
                selectedLineSheet = sheets.FirstOrDefault(
                    name => string.Equals(name, wantedLineSheet, StringComparison.OrdinalIgnoreCase)) ?? "";
            }
            catch (Exception e)
            {
                workbookPath = "";
                ScheduleSheets.Clear();
                selectedSheet = "";
                selectedLineSheet = "";

                ReportProblem?.Invoke("That workbook could not be read: " + e.Message);
            }
            finally
            {
                loadingRow = wasLoading;

                OnPropertyChanged(nameof(WorkbookName));
                OnPropertyChanged(nameof(SelectedScheduleSheet));
                OnPropertyChanged(nameof(SelectedLineLoadSheet));
            }
        }

        #endregion

        #region pairing

        /// <summary>
        /// Writes what the editor says into the pairing store. A level is only ever paired
        /// once a view has been picked for it - a schedule on its own says nothing about which
        /// plan to read - and the sheet may still be missing, which the table then says.
        /// </summary>
        private void Pair()
        {
            if (selectedRow == null || selectedView == null)
            {
                return;
            }

            links.Set(projectKey, new LoadingPlanLink
            {
                Story = selectedRow.Story,
                ViewId = selectedView.Id,
                ViewName = selectedView.Name,
                WorkbookPath = workbookPath,
                SheetName = selectedSheet,
                LineLoadSheetName = selectedLineSheet
            });

            RefreshRows();
        }

        private void Unpair()
        {
            if (selectedRow == null)
            {
                return;
            }

            links.Remove(projectKey, selectedRow.Story);

            loadingRow = true;
            try
            {
                selectedView = null;
                OnPropertyChanged(nameof(SelectedView));
            }
            finally
            {
                loadingRow = false;
            }

            RefreshRows();
        }

        /// <summary>
        /// Fills the editor in from the level just selected.
        ///
        /// The workbook is carried over from whatever the project is already using, because a
        /// project has one loading schedule and picking the same file twenty times is not a
        /// decision anyone is making. The sheet is not carried over, ever.
        /// </summary>
        private void LoadEditorFromRow()
        {
            loadingRow = true;

            try
            {
                LoadingPlanLink link = selectedRow == null
                    ? null
                    : links.Find(projectKey, selectedRow.Story);

                selectedView = link == null
                    ? null
                    : allViews.FirstOrDefault(view => view.Id == link.ViewId);

                string workbook = link != null && link.WorkbookPath.Length > 0
                    ? link.WorkbookPath
                    : links.AnyWorkbook(projectKey);

                if (string.IsNullOrEmpty(workbook))
                {
                    workbookPath = "";
                    ScheduleSheets.Clear();
                    selectedSheet = "";
                    selectedLineSheet = "";
                }
                else
                {
                    OpenWorkbook(workbook, link?.SheetName, link?.LineLoadSheetName);
                }
            }
            finally
            {
                loadingRow = false;
            }

            RefreshViews();

            OnPropertyChanged(nameof(SelectedView));
            OnPropertyChanged(nameof(WorkbookName));
            OnPropertyChanged(nameof(SelectedScheduleSheet));
            OnPropertyChanged(nameof(SelectedLineLoadSheet));
        }

        /// <summary>
        /// Re-reads every row from the store and from the views the project actually has, so a
        /// pairing whose view has since been deleted shows as broken rather than as done.
        /// </summary>
        private void RefreshRows()
        {
            foreach (LoadingPlanRow row in Rows)
            {
                LoadingPlanLink link = links.Find(projectKey, row.Story);
                bool resolves = link != null && allViews.Any(view => view.Id == link.ViewId);
                row.Refresh(link, resolves);
            }

            OnPropertyChanged(nameof(Summary));
            RefreshCounts();
        }

        #endregion
    }
}
