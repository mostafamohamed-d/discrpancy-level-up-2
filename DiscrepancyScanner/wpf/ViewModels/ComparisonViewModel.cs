using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The whole check, as state a view can bind to. It owns the import, the beam comparison
    /// and the column comparison, then projects whatever survives the visibility filters into
    /// <see cref="Elements"/> for the canvas to draw.
    /// </summary>
    public class ComparisonViewModel : ViewModelBase
    {
        private readonly ExternalCommandData commandData;
        private readonly DataContainer data = DataContainer.getDataContainer();

        private EtabsEngine etabsEngine;
        private RevitEngine revitEngine;

        private Story_E selectedStory;
        private ElementViewModel selectedElement;

        /// <summary>
        /// Everything selected on the plan, in the order it was picked. One element is the
        /// ordinary case and is also <see cref="selectedElement"/>; several come from a window
        /// dragged over the plan, and leave that one null.
        /// </summary>
        private readonly List<ElementViewModel> selection = new List<ElementViewModel>();
        private string status = "Import an ETABS model and a Revit model, then run the check.";
        private bool hasCompared;
        private bool isChecking;
        private bool isImporting;

        /// <summary>
        /// True from the first line of a Revit read to the last. Letting the screen draw
        /// during the read is what stops the window looking hung, and it is also what lets
        /// the read's own button be pressed again while it is running - so this keeps the
        /// second press out rather than starting a second read inside the first.
        /// </summary>
        private bool revitReadRunning;
        private bool stopRequested;
        private double checkProgress;
        private bool slabCheckStopped;
        private bool checkEveryStory = true;
        private Story_E slabCheckStory;

        private bool importFromFile;

        // Which loading plan each ETABS level is checked against: a Revit view of filled
        // regions with an area load number on each, and the sheet of the loading schedule
        // those numbers mean something in. Made in the loading plans window and kept between
        // sessions; this is the only copy, and it is read when the check runs rather than
        // mirrored onto the panel, so there is nothing here that can go stale against it.
        /// <summary>
        /// The pairs set by hand, per project. Read in with the settings and handed to every
        /// compare engine, so a decision made once is made once.
        /// </summary>
        private readonly PairingOverrides pairingOverrides = new PairingOverrides();

        /// <summary>
        /// What people have said about elements: reviewed, and any note. Read only by the
        /// viewer - unlike a pairing, none of this changes what the check computes.
        /// </summary>
        private readonly ReviewNotes reviewNotes = new ReviewNotes();

        /// <summary>
        /// The patches of each level drawn by hand where the line loads take a height of their
        /// own, per project. Handed to the line load check, which is the only thing that reads them.
        /// </summary>
        private readonly HeightZones heightZones = new HeightZones();

        /// <summary>
        /// What the last run found, for saying what has changed since. Read lazily on the first
        /// rebuild that needs it, because the project is not known until a model is open.
        /// </summary>
        private readonly RunHistory runHistory = new RunHistory();

        private readonly LoadingPlanLinks loadingPlanLinks = new LoadingPlanLinks();
        private bool checkSlabLoads;

        /// <summary>
        /// Every project's alignment, keyed the same way <see cref="ProjectKey"/> keys the
        /// loading plans, so re-opening a project puts the Revit model back on top of ETABS
        /// without picking the points again. Held whole, loaded and not otherwise touched,
        /// rather than just this project's entry, so saving does not drop what another open
        /// project has already put in the same settings file.
        /// </summary>
        private readonly Dictionary<string, object> alignmentsByProject =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Each project's size parameter mapping - see <see cref="ProjectParameterMappings"/>.</summary>
        private readonly ProjectParameterMappings parameterMappings = new ProjectParameterMappings();

        /// <summary>The project whose mapping is in the settings, as it was when the window opened.</summary>
        private string mappingProjectKey = "";

        /// <summary>
        /// The label fields the user has ticked, kept by key rather than by row. The picker is
        /// rebuilt from whatever the visible elements publish, so it is empty before the first
        /// import and loses whichever fields the other mode had; holding the keys here is what
        /// lets a tick survive both of those, and the settings file.
        /// </summary>
        private readonly HashSet<string> rememberedLabelKeys = new HashSet<string>(StringComparer.Ordinal);

        private bool showUnpairedEtabs = true;
        private bool showUnpairedRevit = true;
        private bool showNotChecked = true;
        private bool showAccepted = true;

        private bool showContext = true;

        public ComparisonViewModel(ExternalCommandData commandData, RevitRequestHandler requestHandler)
        {
            this.commandData = commandData;
            Alignment = new AlignmentViewModel(requestHandler, OnAlignmentApplied);
            // The alignment pairs a story with a level from the same list the single level link
            // uses, read fresh each time the alignment window asks, so a level added since shows.
            Alignment.LevelsSource = () =>
            {
                RefreshRevitLevels();
                return revitLevels;
            };
            CheckFilters = new CheckFilters(RebuildElements);
            Findings = new FindingTally(OnScopeChanged);
            // A row in the drawer picks the element on the plan, which is the whole point of
            // having the list beside the drawing rather than in a window of its own.
            FindingsList.OnRowPicked = element => SelectedElement = element;
            ClearSelectionCommand = new RelayCommand(_ => SelectedElement = null);
            Pairing = new PairingEditor(FindPairCandidates, ApplyPairing);
            PairSelectionCommand = new RelayCommand(_ => PairSelection(), _ => CanPairSelection);
            UnpairSelectionCommand = new RelayCommand(_ => UnpairSelection(), _ => CanUnpairSelection);
            ForgetDecisionsCommand = new RelayCommand(_ => ForgetDecisions(), _ => HasDecisions);

            ToggleReviewedCommand = new RelayCommand(_ => ToggleVerdict(HumanVerdict.Reviewed),
                                                     _ => Card != null && Card.CanReview);
            MarkPickedReviewedCommand = new RelayCommand(_ => MarkPicked(HumanVerdict.Reviewed),
                                                         _ => FindingsList.HasPicked);
            AcceptPickedCommand = new RelayCommand(_ => MarkPicked(HumanVerdict.Accepted),
                                                   _ => FindingsList.HasPicked);
            ToggleAcceptedCommand = new RelayCommand(_ => ToggleVerdict(HumanVerdict.Accepted),
                                                     _ => Card != null && Card.CanAccept);
            AddNoteToPickedCommand = new RelayCommand(_ => AddNoteToPicked(),
                                                      _ => FindingsList.HasPicked);
            AddNoteCommand = new RelayCommand(_ => AddNote(),
                                              _ => Card != null && Card.CanReview);
            RefreshVisibleCheckGroups();

            ImportEtabsCommand = new RelayCommand(_ => ImportEtabs());
            ImportRevitCommand = new RelayCommand(_ => ImportRevit(chooseLinks: true), _ => !revitReadRunning);
            ExportEtabsCommand = new RelayCommand(_ => ExportEtabs(), _ => CanExportEtabs);
            CompareCommand = new RelayCommand(_ => Compare(), _ => CanCompare);
            // Enabled whether or not a run could say anything, because pressing it when nothing
            // is ready is exactly when the sheet below has something to say.
            RunCheckCommand = new RelayCommand(_ => RunCheck(), _ => !IsChecking);
            SelectInRevitCommand = new RelayCommand(_ => SelectInRevit(),
                                                    _ => selection.Any(element => element.RevitId > 0));
            WriteLoadBackCommand = new RelayCommand(_ => WriteLoadBack(), _ => CanWriteLoadBack);
            ExportBeamMarksCommand = new RelayCommand(_ => ExportBeamMarks(), _ => CanExportBeamMarks);
            ClearLabelsCommand = new RelayCommand(_ => ClearLabels());
            ShowAllResultsCommand = new RelayCommand(_ => CheckFilters.SetAll(true));
            HideAllResultsCommand = new RelayCommand(_ => CheckFilters.SetAll(false));
            StopCheckCommand = new RelayCommand(_ => stopRequested = true, _ => IsChecking && !stopRequested);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            OpenAlignmentCommand = new RelayCommand(_ => ShowAlignment?.Invoke());
            StoryUpCommand = new RelayCommand(_ => StepStory(1), _ => StoryStepFrom(1) != null);
            StoryDownCommand = new RelayCommand(_ => StepStory(-1), _ => StoryStepFrom(-1) != null);
            OpenLoadingPlansCommand = new RelayCommand(_ => OpenLoadingPlans());
            RecheckLoadsCommand = new RelayCommand(_ => RecheckLoads(null, lineLoads: true),
                                                   _ => hasCompared && !IsChecking);

            LoadUserSettings();
        }

        #region bound state

        /// <summary>Lines the Revit model up with the ETABS model from two matched points.</summary>
        public AlignmentViewModel Alignment { get; }

        /// <summary>
        /// Redraws the plan without rebuilding it. Set by the window, because the canvas is
        /// created there rather than in the XAML.
        ///
        /// Needed because the canvas only redraws when the collection it is bound to changes,
        /// and accepting a difference changes a property on an element that is already in it.
        /// Rebuilding instead would work and would also drop the selection, which is the one
        /// thing that must survive pressing a button on the selected element's own card.
        /// </summary>
        public Action RedrawPlan { get; set; }

        /// <summary>
        /// Puts the preflight sheet up. Returns true where the run should go ahead anyway.
        /// Set by the window; a null hook means no sheet and the run goes straight through,
        /// which is what keeps this testable without a window.
        /// </summary>
        public Func<PreflightReport, bool> ShowPreflight { get; set; }

        /// <summary>
        /// Every element the last rebuild produced, on every level.
        ///
        /// Kept because the chip counts and the level rail are counted off the whole model
        /// rather than off the level on show, so a verdict changed on one element has to be
        /// re-counted against all of them - and rebuilding the lot to do it would throw away
        /// the selection the verdict was just set on.
        /// </summary>
        private List<ElementViewModel> allElements = new List<ElementViewModel>();

        /// <summary>Set by the view to show a collected list of import problems in one go.</summary>
        public Action<string, IList<string>> ShowIssues { get; set; }

        #region settings

        /// <summary>
        /// Set by the view to put the settings window up. Answers true when it was closed on
        /// OK, which is when the settings are worth writing out.
        /// </summary>
        public Func<SettingsViewModel, bool> ShowSettings { get; set; }

        /// <summary>Set by the view: opens the alignment window beside the plan.</summary>
        public Action ShowAlignment { get; set; }

        /// <summary>Set by the view to add its own size and position to what gets saved.</summary>
        public Func<Dictionary<string, object>> CollectWindowState { get; set; }

        /// <summary>Where the settings are kept, for the tooltip on the settings button.</summary>
        public string SettingsLocation => "Saved for you alone, in " + UserSettings.FilePath;

        private void OpenSettings()
        {
            SettingsViewModel settings = new SettingsViewModel(OnSettingChanged, () => GeometryDumpDescription)
            {
                // The models and alignment pages are built out of this window's own commands,
                // so the settings window is handed the window it was opened from.
                Owner = this
            };

            if (ShowSettings != null && ShowSettings(settings))
            {
                SaveUserSettings();
            }

            // Whichever way it closed, the panel behind may be describing a value that has
            // moved, and the plan may be drawn from one.
            OnSettingChanged();

            // A loading plan paired afresh from inside the settings window. Done now rather
            // than as the pairing window closed, so the bar and the stop button are on the
            // window in front rather than behind the one the pairing was made from.
            RecheckPendingLoads();

            // Asked for from inside the settings window, and only doable once that window has
            // gone: aligning has to be able to reach Revit, and a dialog on this thread
            // disables Revit's window along with ours.
            if (settings.AlignmentRequested)
            {
                ShowAlignment?.Invoke();
            }
        }

        /// <summary>
        /// Called as a setting is edited. Nearly none of them change what has already been
        /// imported or compared - they are read again on the next import or the next run - so
        /// mostly the only thing to do here is let the panel re-read the lines that quote one.
        ///
        /// The exception is a setting that says what the plan draws out of results already
        /// worked out. There is nothing to re-run for one of those and nothing to wait for, so
        /// the plan is built again on the spot.
        /// </summary>
        private void OnSettingChanged()
        {
            OnPropertyChanged(nameof(GeometryDumpDescription));
            OnPropertyChanged(nameof(ShowsLoadRecheck));

            // The keys are read afresh on every press, so only the tooltips naming them need
            // telling.
            Findings.ShowKeys();
            OnPropertyChanged(nameof(RawDataToolTip));
            OnPropertyChanged(nameof(StoryUpToolTip));
            OnPropertyChanged(nameof(StoryDownToolTip));
            OnPropertyChanged(nameof(AcceptToolTip));
            OnPropertyChanged(nameof(PairToolTip));

            // Only when it actually moved: this is called on every keystroke in every box in
            // the settings window, and rebuilding a floor's worth of elements on each one
            // would make the window type badly on a large model.
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            if (pref.DrawBothColumnsOnMismatch != drawingBothColumns
                || pref.LineLoadMinimumCountedLength != countingLineLoadsFrom)
            {
                RebuildElements();
            }
        }

        /// <summary>
        /// The shortest line load finding the plan as it stands counts, for the same reason as
        /// <see cref="drawingBothColumns"/>: what is counted is settled as the elements are
        /// built, so moving the setting rebuilds them and the counts follow while it is typed.
        /// </summary>
        private double countingLineLoadsFrom;

        /// <summary>
        /// Whether the plan as it stands was built with the second column outline on. Written
        /// by the rebuild itself, so it says what is actually drawn rather than what was wanted
        /// when the window opened; see <see cref="OnSettingChanged"/>.
        /// </summary>
        private bool drawingBothColumns;

        /// <summary>
        /// What was under the view branch of the settings file when the window opened, for the
        /// view to take its own size and position out of. Read after the constructor has run,
        /// which is the earliest the view can have hooked itself up.
        /// </summary>
        public IDictionary<string, object> SavedWindowState { get; private set; }

        /// <summary>
        /// Reads the settings file over the preferences and puts the panel back the way it was
        /// left. Nothing here can fail loudly: an absent file is the ordinary first run, and an
        /// unreadable one is no worse than an absent one.
        /// </summary>
        private void LoadUserSettings()
        {
            IDictionary<string, object> view = UserSettings.Load(PrefrencesData.getPrefrencesData());

            ShowContext = Json.GetBool(view, "showContext", showContext);
            VerdictColours.ReadFrom(view);
            // Only the chips that were switched off are written, so a chip added by a later
            // version comes back on rather than off - the safer way round for something that
            // decides whether a check is read at all.
            List<string> offChecks = Json.GetStrings(view, "switchedOffChecks") ?? new List<string>();
            foreach (ScopeTab tab in Findings.Tabs)
            {
                tab.IsActive = !offChecks.Contains(tab.Mode.ToString(), StringComparer.OrdinalIgnoreCase);
            }
            ShowUnpairedEtabs = Json.GetBool(view, "showUnpairedEtabs", showUnpairedEtabs);
            ShowUnpairedRevit = Json.GetBool(view, "showUnpairedRevit", showUnpairedRevit);
            ShowNotChecked = Json.GetBool(view, "showNotChecked", showNotChecked);
            ShowAccepted = Json.GetBool(view, "showAccepted", showAccepted);
            ImportFromFile = Json.GetBool(view, "importFromFile", importFromFile);
            CheckEveryStory = Json.GetBool(view, "checkEveryStory", checkEveryStory);

            CsiProduct = string.Equals(Json.GetString(view, "csiProduct", "Etabs"), "Safe",
                                       StringComparison.OrdinalIgnoreCase)
                       ? CsiProduct.Safe
                       : CsiProduct.Etabs;

            CheckSlabLoads = Json.GetBool(view, "checkSlabLoads", checkSlabLoads);

            // Read whole here, and the one for the floor being checked is put on the panel as
            // soon as a floor is picked.
            loadingPlanLinks.ReadFrom(view);
            pairingOverrides.ReadFrom(view);
            reviewNotes.ReadFrom(view);
            heightZones.ReadFrom(view);
            ScheduleEdits.Current.ReadFrom(view);
            ScheduleColumnOverrides.Current.ReadFrom(view);

            foreach (KeyValuePair<string, object> entry in Json.Branch(view, "alignments"))
            {
                alignmentsByProject[entry.Key] = entry.Value;
            }
            LoadAlignmentForCurrentProject();

            // Each project's own size parameter mapping, filed under the same key - the key of
            // the project open now, held for the save: switching documents while the window is
            // open must not file this project's mapping under another.
            mappingProjectKey = ProjectKey;
            parameterMappings.ReadFrom(Json.Branch(view, ProjectParameterMappings.Branch),
                                       view != null && view.ContainsKey(ProjectParameterMappings.Branch));
            parameterMappings.ApplyTo(PrefrencesData.getPrefrencesData(), mappingProjectKey);

            CheckFilters.ApplySwitchedOff(Json.GetStrings(view, "switchedOffResults"));

            List<string> labels = Json.GetStrings(view, "labelKeys");
            if (labels != null)
            {
                // The picker is empty until something has been imported, so the ticks are held
                // here and put on the rows as they appear.
                foreach (string key in labels)
                {
                    rememberedLabelKeys.Add(key);
                }
            }

            SavedWindowState = Json.Branch(view, "window");
        }

        /// <summary>
        /// Writes the settings file. Called when the settings window is closed on OK and again
        /// when the check window closes, so the panel is found as it was left.
        /// </summary>
        public void SaveUserSettings()
        {
            Dictionary<string, object> view = CollectView();

            Dictionary<string, object> window = CollectWindowState == null ? null : CollectWindowState();
            if (window != null)
            {
                view["window"] = window;
            }

            if (!UserSettings.Save(PrefrencesData.getPrefrencesData(), view))
            {
                Status = UserSettings.LastError;
            }
        }

        /// <summary>
        /// Everything the panel keeps in the settings file, as it stands - every project's
        /// pairings, notes, zones and alignment among it. What a project file is cut out of,
        /// and put back into.
        /// </summary>
        private Dictionary<string, object> CollectView()
        {
            StoreAlignmentForCurrentProject();
            parameterMappings.StoreFrom(PrefrencesData.getPrefrencesData(), mappingProjectKey);

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // Over what the file holds now, so another window's project is not undone.
                { ProjectParameterMappings.Branch, parameterMappings.ToJsonOver(
                      Json.Branch(UserSettings.ViewBranchOnDisk(), ProjectParameterMappings.Branch), mappingProjectKey) },
                { "showContext", showContext },
                { VerdictColours.Branch, VerdictColours.ToJson() },
                { "switchedOffChecks", Findings.Tabs.Where(t => !t.IsActive)
                                                   .Select(t => t.Mode.ToString()).ToList() },
                { "showUnpairedEtabs", showUnpairedEtabs },
                { "showUnpairedRevit", showUnpairedRevit },
                { "showNotChecked", showNotChecked },
                { "showAccepted", showAccepted },
                { "importFromFile", importFromFile },
                { "checkEveryStory", checkEveryStory },
                { "csiProduct", csiProduct.ToString() },
                { "checkSlabLoads", checkSlabLoads },
                { LoadingPlanLinks.Branch, loadingPlanLinks.ToJson() },
                { PairingOverrides.Branch, pairingOverrides.ToJson() },
                { PairingOverrides.PlacesBranch, pairingOverrides.PlacesToJson() },
                { ReviewNotes.Branch, reviewNotes.ToJson() },
                { HeightZones.Branch, heightZones.ToJson() },
                { ScheduleEdits.Branch, ScheduleEdits.Current.ToJson() },
                { ScheduleColumnOverrides.Branch, ScheduleColumnOverrides.Current.ToJson() },
                { "alignments", alignmentsByProject },
                { "switchedOffResults", CheckFilters.SwitchedOffKeys() },
                { "labelKeys", rememberedLabelKeys.OrderBy(key => key, StringComparer.Ordinal).ToList() }
            };
        }

        #endregion


        #region view mode

        /// <summary>
        /// Draw the kinds whose chip is off in grey behind the ones that are on, rather than
        /// leaving them off the plan. On, a beam can be read against the floor it sits in; off,
        /// one kind can be looked at with nothing else on the drawing at all.
        /// </summary>
        public bool ShowContext
        {
            get => showContext;
            set { if (SetProperty(ref showContext, value)) RebuildElements(); }
        }

        public string ContextDescription =>
            "Whatever is switched off up top is still drawn, in grey, so the floor reads as a floor. "
            + "Grey elements carry no result and cannot be selected. Switch this off to see one kind "
            + "of check on its own.";

        private bool showRawData;

        /// <summary>
        /// Draw each model as it was read instead of what the check made of them: every ETABS
        /// element in the ETABS colour and every Revit one in the Revit colour, on top of each
        /// other, with nothing paired and nothing judged. The tabs still say which kinds are
        /// drawn, and the labels offer what each element was read as - its name, section, size,
        /// thickness, loads - so the two can be read against each other by eye.
        ///
        /// For the questions the check cannot answer about itself. A pairing that went wrong, a
        /// level paired with the wrong view, a size read off the wrong parameter: each comes out
        /// of the check as a finding about something else, and each is plain the moment both
        /// models are laid over each other and labelled with what was read.
        ///
        /// Nothing is counted, listed, reviewed, accepted or paired while it is on - there are
        /// no findings to do any of it to. It is not remembered between sessions either: the
        /// window always opens on the check, which is what somebody opening it has come for.
        /// </summary>
        public bool ShowRawData
        {
            get => showRawData;
            set
            {
                if (SetProperty(ref showRawData, value))
                {
                    OnPropertyChanged(nameof(ShownVerdictKey));
                    RebuildElements();
                    Status = value
                        ? "Raw data: both models as they were read. Nothing is paired or compared - "
                          + "tick LABEL to write what each element was read as on the plan."
                        : "Back to the check.";
                }
            }
        }

        public string RawDataToolTip =>
            "Draw both models as they were read, with nothing paired or compared: ETABS in the ETABS "
            + "colour, Revit in the Revit colour. The tabs still choose what is drawn, and LABEL writes "
            + "what each element was read as - name, section, size, loads - beside it."
            + KeyHint(ShortcutAction.RawData, " Press {0} to switch it on or off.");

        /// <summary>The level arrows' tooltips, with the keys they are set to.</summary>
        public string StoryUpToolTip =>
            "The level above" + KeyHint(ShortcutAction.LevelUp, " ({0})")
            + ". Steps by height, which is not always the next row down the rail.";

        public string StoryDownToolTip =>
            "The level below" + KeyHint(ShortcutAction.LevelDown, " ({0})") + ".";

        /// <summary>The card's ACCEPT button and the PAIR THESE button, with their keys.</summary>
        public string AcceptToolTip =>
            "Say the two models differ here and that is agreed - a screed the analysis leaves out, a mark "
            + "the design team has confirmed. It stays on the plan and stops counting as open work."
            + KeyHint(ShortcutAction.Accept, " Press {0} to accept, or to take it back.");

        public string PairToolTip =>
            "Records that these two are the same element in both models, whatever the distance between "
            + "them suggests, and pairs that type again. The answer is kept and survives the next run."
            + KeyHint(ShortcutAction.Pair, " Press {0} to pair them.");

        /// <summary>
        /// A key written into a tooltip as it is set now, or nothing where the action has no
        /// key - a tooltip naming a key that does nothing is worse than one naming none.
        /// </summary>
        private static string KeyHint(ShortcutAction action, string format)
        {
            string key = Shortcuts.Describe(Shortcuts.KeyOf(action));
            return key.Length == 0 ? "" : string.Format(format, key);
        }

        public string GeometryDumpDescription
        {
            get
            {
                if (!PrefrencesData.getPrefrencesData().WriteGeometryDump)
                {
                    return "Off. Turn it on and re-import to record what the kernel refused.";
                }

                return GeometryDump.FilePath == null
                    ? "Written to " + GeometryDump.DefaultFolder() + " on the next import."
                    : $"{GeometryDump.Entries} fault(s) in {GeometryDump.FilePath}";
            }
        }

        #endregion

        public ObservableCollection<ElementViewModel> Elements { get; } = new ObservableCollection<ElementViewModel>();

        public ObservableCollection<Story_E> Stories { get; } = new ObservableCollection<Story_E>();

        /// <summary>Every result field the current elements publish, tickable for display.</summary>
        /// <summary>
        /// What can be stamped on the plan, one group per kind of element. Grouped rather than
        /// listed, because the kinds name their results the same things: a column and a beam
        /// both publish "Check state", and in one flat list those are two identical lines.
        /// </summary>
        public ObservableCollection<LabelFieldGroup> LabelGroups { get; }
            = new ObservableCollection<LabelFieldGroup>();

        /// <summary>True once there is anything to stamp, which is once something is imported.</summary>
        public bool HasLabelFields => LabelGroups.Count > 0;

        /// <summary>
        /// How many results are being stamped, for the face of the button. It counts what is
        /// actually drawn rather than what is remembered: a tick belonging to a kind whose chip
        /// is down is kept for when it comes back up, but nothing on the plan is carrying it.
        /// </summary>
        public string LabelCountText => SelectedLabelKeys.Count == 0
            ? "" : SelectedLabelKeys.Count.ToString();

        public bool HasLabels => SelectedLabelKeys.Count > 0;

        /// <summary>Keys of the ticked fields; the canvas stamps these on top of each element.</summary>
        public ObservableCollection<string> SelectedLabelKeys { get; } = new ObservableCollection<string>();

        public Story_E SelectedStory
        {
            get => selectedStory;
            set
            {
                if (SetProperty(ref selectedStory, value))
                {
                    // Nothing about any element changes with the level, so what was built is
                    // shown again rather than built again - see ShowLevel.
                    if (!hasBuilt)
                    {
                        RebuildElements();
                    }
                    else
                    {
                        ShowLevel();
                    }
                    RefreshStoryBanner();
                    OnPropertyChanged(nameof(SelectedLevel));
                }
            }
        }

        /// <summary>
        /// The level rail's selection. It is derived from <see cref="SelectedStory"/> rather
        /// than held, so the rail and the picker across the top of the plan can never disagree
        /// about which floor is up, and so the tally stays the only thing that ever writes
        /// <see cref="LevelRow.IsSelected"/> - a rail that wrote it too would have its click
        /// overwritten by the rebuild that click sets off.
        /// </summary>
        public LevelRow SelectedLevel
        {
            get => Findings.Levels.FirstOrDefault(row => row.Story == selectedStory);
            set
            {
                if (value?.Story != null)
                {
                    SelectedStory = value.Story;
                }
            }
        }

        #region which level is on screen

        // The plan is one level at a time and every colour on it belongs to that level, so
        // which one it is has to be readable without going looking for the picker. These feed
        // the band across the top of the drawing.

        /// <summary>The level's own name, drawn large above the plan.</summary>
        public string StoryTitle => selectedStory == null ? "No level" : selectedStory.Name;

        /// <summary>The level's height, in meters, beside the name.</summary>
        public string StoryElevation => selectedStory == null
            ? string.Empty
            : (selectedStory.Elevation / 1000d).ToString("+0.000;-0.000;0.000") + " m";

        /// <summary>Where this level sits in the building, so the top and bottom are obvious.</summary>
        public string StoryPosition
        {
            get
            {
                if (selectedStory == null)
                {
                    return Stories.Count == 0 ? "Nothing imported yet" : "Pick a level";
                }

                int at = StoriesByElevation().IndexOf(selectedStory);
                return at < 0 ? string.Empty : $"{at + 1} of {Stories.Count} from the bottom";
            }
        }

        public bool HasStories => Stories.Count > 0;

        /// <summary>
        /// The levels in height order rather than the order ETABS listed them, so stepping up
        /// and down means what it says whichever way round the model was written.
        /// </summary>
        private List<Story_E> StoriesByElevation()
        {
            return Stories.OrderBy(story => story.Elevation).ToList();
        }

        private Story_E StoryStepFrom(int direction)
        {
            if (selectedStory == null)
            {
                return null;
            }

            List<Story_E> ordered = StoriesByElevation();
            int next = ordered.IndexOf(selectedStory) + direction;

            return next >= 0 && next < ordered.Count ? ordered[next] : null;
        }

        private void StepStory(int direction)
        {
            Story_E next = StoryStepFrom(direction);
            if (next != null)
            {
                SelectedStory = next;
            }
        }

        private void RefreshStoryBanner()
        {
            OnPropertyChanged(nameof(StoryTitle));
            OnPropertyChanged(nameof(StoryElevation));
            OnPropertyChanged(nameof(StoryPosition));
            OnPropertyChanged(nameof(HasStories));
        }

        #endregion

        /// <summary>
        /// The selected element when exactly one is selected, and null otherwise. Setting it
        /// replaces the whole selection with that one element, or clears it.
        /// </summary>
        public ElementViewModel SelectedElement
        {
            get => selectedElement;
            set => SetSelection(value == null ? new ElementViewModel[0] : new[] { value });
        }

        /// <summary>Everything selected, one element or many.</summary>
        public IReadOnlyList<ElementViewModel> SelectedElements => selection;

        /// <summary>A plain click on the plan: that element alone, or nothing.</summary>
        public void SelectOnly(ElementViewModel element)
        {
            SelectedElement = element;
        }

        /// <summary>A window dragged over the plan: what it caught joins the selection.</summary>
        public void AddToSelection(IList<ElementViewModel> elements)
        {
            if (elements == null || elements.Count == 0)
            {
                return;
            }
            SetSelection(selection.Concat(elements));
        }

        /// <summary>
        /// Shift+click or shift+drag: these leave the selection. Matched on what is drawn, so
        /// a panel picked out of a folded wall comes out when the wall it is drawn as does.
        /// </summary>
        public void RemoveFromSelection(IList<ElementViewModel> elements)
        {
            if (elements == null || elements.Count == 0 || selection.Count == 0)
            {
                return;
            }

            HashSet<ElementViewModel> drawn = new HashSet<ElementViewModel>(
                elements.Where(element => element != null).Select(element => element.Drawn));
            SetSelection(selection.Where(element => !drawn.Contains(element.Drawn)));
        }

        /// <summary>
        /// The one place the selection changes. Everything that reads it - the halo on the
        /// plan, the card, the pair panel, the buttons - is brought up to date from here.
        /// </summary>
        private void SetSelection(IEnumerable<ElementViewModel> wanted)
        {
            List<ElementViewModel> next = new List<ElementViewModel>();
            foreach (ElementViewModel element in wanted)
            {
                if (element != null && !next.Contains(element))
                {
                    next.Add(element);
                }
            }

            if (next.SequenceEqual(selection))
            {
                return;
            }

            foreach (ElementViewModel element in selection)
            {
                element.Drawn.IsSelected = false;
            }
            selection.Clear();
            selection.AddRange(next);

            ElementViewModel single = selection.Count == 1 ? selection[0] : null;
            if (!ReferenceEquals(single, selectedElement))
            {
                selectedElement = single;
                OnPropertyChanged(nameof(SelectedElement));
            }

            // After the one element property has gone out, because the plan clears the flag on
            // whatever it was showing as selected before when that property changes.
            foreach (ElementViewModel element in selection)
            {
                element.Drawn.IsSelected = true;
            }

            // The pairing editor is about one element, and the card it sits in is no longer
            // about one element.
            if (selection.Count != 1 && Pairing.IsOpen)
            {
                Pairing.CancelCommand.Execute(null);
            }

            BuildCard();
            OnPropertyChanged(nameof(Card));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(SelectedElements));
            OnPropertyChanged(nameof(CanPairSelection));
            OnPropertyChanged(nameof(PairSelectionText));
            OnPropertyChanged(nameof(PairSelectionNote));
            OnPropertyChanged(nameof(CanUnpairSelection));
            OnPropertyChanged(nameof(UnpairSelectionText));
            OnPropertyChanged(nameof(ShowsWriteLoadBack));
            OnPropertyChanged(nameof(WriteLoadBackText));
            OnPropertyChanged(nameof(WriteLoadBackToolTip));
            RedrawPlan?.Invoke();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Rebuilt rather than kept in step: an element's verdicts do not change while it is
        /// selected, and a run replaces the element object outright.
        /// </summary>
        private void BuildCard()
        {
            if (selection.Count == 0)
            {
                Card = null;
            }
            else if (selection.Count == 1)
            {
                Card = new ElementCard(selection[0], SaidAbout(selection[0], ProjectKey));
            }
            else
            {
                string project = ProjectKey;
                Card = new ElementCard(selection, element => SaidAbout(element, project));
            }
        }

        /// <summary>
        /// What has been said about an element, for its card - nothing for one that cannot have
        /// anything said about it. A beam in the raw data still goes by its Revit id, but a
        /// review filed under that id was about what the check found, and on an element that
        /// carries no findings it could only ever read as gone stale.
        /// </summary>
        private ReviewNote SaidAbout(ElementViewModel element, string project)
        {
            return element.CanBeFiled ? reviewNotes.Find(project, element.ReviewKey) : null;
        }

        /// <summary>
        /// The card over the plan for the element that is picked, or null while nothing is.
        /// </summary>
        public ElementCard Card { get; private set; }

        public bool HasSelection => Card != null;

        /// <summary>Clears the selection, which is what the card's close button does.</summary>
        public ICommand ClearSelectionCommand { get; }

        public ICommand ToggleReviewedCommand { get; }
        public ICommand AddNoteCommand { get; }
        public ICommand MarkPickedReviewedCommand { get; }
        public ICommand AcceptPickedCommand { get; }
        public ICommand ToggleAcceptedCommand { get; }
        public ICommand AddNoteToPickedCommand { get; }

        /// <summary>
        /// Marks every ticked row's element reviewed or accepted, each against its own verdicts.
        ///
        /// Working down a list is what the drawer is for, and a floor of the same wrong thing -
        /// forty beams whose marks were never filled in, every piece of a slab that is thicker
        /// in Revit by the depth of one screed - is exactly the case where saying so once per
        /// element is not worth anyone's afternoon.
        /// </summary>
        private void MarkPicked(HumanVerdict verdict)
        {
            IList<ElementViewModel> picked = FindingsList.PickedElements;
            int done = 0;
            int agreeing = 0;

            foreach (ElementViewModel element in picked)
            {
                if (!element.CanBeFiled)
                {
                    continue;
                }

                // Nothing to accept on an element that agrees.
                if (verdict == HumanVerdict.Accepted && !CheckNaming.Disagrees(element))
                {
                    agreeing++;
                    continue;
                }

                reviewNotes.SetVerdict(ProjectKey, element.ReviewKey, verdict,
                                       element.VerdictFingerprint);
                done++;
            }

            AfterBulkEdit(done, picked.Count - agreeing,
                          verdict == HumanVerdict.Accepted ? "accepted" : "marked reviewed");

            if (agreeing > 0)
            {
                Status += " " + agreeing + " left alone - "
                          + (agreeing == 1 ? "it agrees" : "they agree") + ", so there is nothing to accept.";
            }
        }

        private void AddNoteToPicked()
        {
            IList<ElementViewModel> picked = FindingsList.PickedElements;
            if (picked.Count == 0 || AskForNote == null)
            {
                return;
            }

            string about = picked.Count == 1
                ? picked[0].Description
                : picked.Count + " elements";
            string typed = AskForNote(about, "");
            if (typed == null)
            {
                return;
            }

            int done = 0;
            foreach (ElementViewModel element in picked)
            {
                if (!element.CanBeFiled)
                {
                    continue;
                }
                reviewNotes.SetNote(ProjectKey, element.ReviewKey, typed);
                done++;
            }

            AfterBulkEdit(done, picked.Count, "given a note");
        }

        /// <summary>
        /// Saves, redraws the drawer so the rows read back what was written, and says what was
        /// skipped. Being told is better than wondering why the tick did not appear - and what
        /// gets skipped is rare enough that it would be a puzzle rather than a habit.
        /// </summary>
        private void AfterBulkEdit(int done, int asked, string what)
        {
            SaveUserSettings();
            RefreshAfterVerdictChange();

            int skipped = asked - done;
            Status = done + (done == 1 ? " element " : " elements ") + what
                     + (skipped > 0
                        ? ". " + skipped + " skipped - nothing to file "
                          + (skipped == 1 ? "it" : "them") + " under."
                        : ".");
        }

        /// <summary>Puts a note editor up. Set by the window, which owns the dialog.</summary>
        public Func<string, string, string> AskForNote { get; set; }

        /// <summary>
        /// Marks the selected element reviewed, or takes the review back.
        ///
        /// A review is recorded against the verdicts the element is carrying at that moment, so
        /// that a later run which changes any of them shows the review as stale rather than as
        /// still good. Marking a stale one again is how it is renewed.
        /// </summary>
        /// <summary>
        /// Sets or takes back a decision on the selected element. Pressing the button for the
        /// decision it already carries takes it back, so one control both makes and unmakes the
        /// statement rather than needing a second one to undo it.
        /// </summary>
        /// <summary>
        /// On several selected elements it is one statement about all of them: set on every
        /// one, or - where every one already carries it - taken back from every one.
        /// </summary>
        private void ToggleVerdict(HumanVerdict wanted)
        {
            List<ElementViewModel> targets = selection.Where(element => element.CanBeFiled).ToList();
            if (targets.Count == 0)
            {
                return;
            }

            bool alreadySaid = Card != null && Card.Verdict == wanted;
            HumanVerdict now = alreadySaid ? HumanVerdict.None : wanted;
            int unfiled = selection.Count - targets.Count;

            // Accepting is for differences, so the ones in the selection that agree are left out.
            int agreeing = 0;
            if (now == HumanVerdict.Accepted)
            {
                List<ElementViewModel> differing = targets.Where(CheckNaming.Disagrees).ToList();
                agreeing = targets.Count - differing.Count;
                targets = differing;
                if (targets.Count == 0)
                {
                    Status = "Nothing to accept - " + (agreeing == 1 ? "it agrees." : "they all agree.");
                    return;
                }
            }

            foreach (ElementViewModel element in targets)
            {
                reviewNotes.SetVerdict(ProjectKey, element.ReviewKey, now, element.VerdictFingerprint);
            }
            SaveUserSettings();
            RefreshAfterVerdictChange();

            string which = targets.Count == 1 ? targets[0].Description : targets.Count + " elements";
            Status = (now == HumanVerdict.None
                         ? "Taken back on " + which + "."
                         : which + (now == HumanVerdict.Accepted
                             ? " accepted - still a difference, but agreed."
                             : " marked as reviewed."))
                     + Skipped(unfiled)
                     + (agreeing > 0
                         ? " " + agreeing + " left alone - " + (agreeing == 1 ? "it agrees" : "they agree")
                           + ", so there is nothing to accept."
                         : "");
        }

        /// <summary>The same note on every selected element, replacing what each one had.</summary>
        private void AddNote()
        {
            List<ElementViewModel> targets = selection.Where(element => element.CanBeFiled).ToList();
            if (targets.Count == 0 || AskForNote == null)
            {
                return;
            }

            string existing = Card == null ? "" : Card.NoteToEdit;
            string about = targets.Count == 1 ? targets[0].Description : targets.Count + " elements";
            string typed = AskForNote(about, existing);
            if (typed == null)
            {
                return;
            }

            foreach (ElementViewModel element in targets)
            {
                reviewNotes.SetNote(ProjectKey, element.ReviewKey, typed);
            }
            SaveUserSettings();

            if (targets.Count == 1)
            {
                RefreshCard();
                return;
            }

            RefreshFindingsList();
            RefreshCard();
            Status = "The same note put on " + targets.Count + " elements."
                     + Skipped(selection.Count - targets.Count);
        }

        private static string Skipped(int skipped)
        {
            return skipped > 0
                ? " " + skipped + " skipped - nothing to file " + (skipped == 1 ? "it" : "them") + " under."
                : "";
        }

        /// <summary>
        /// Refills the drawer from what is on the plan, with what people have said about each
        /// element and what the last run found.
        /// </summary>
        private void RefreshFindingsList()
        {
            string project = ProjectKey;

            if (!runHistory.IsLoaded)
            {
                runHistory.Load(project);
            }

            FindingsList.Update(Elements,
                                element => reviewNotes.Find(project, element.ReviewKey),
                                runHistory);
        }

        /// <summary>
        /// Records what this run found, so the next one can say what changed.
        ///
        /// Written after the results are on the plan rather than as the check finishes, because
        /// what counts as a finding is decided by the switches - and a run recorded with a check
        /// switched off would make every one of its findings look new the moment it came back on.
        /// Slab zones are left out - see <see cref="FindingRow.HistoryKey"/>, which is where a
        /// row decides whether it has a name steady enough to be counted against last time.
        /// </summary>
        private void RecordThisRun()
        {
            HashSet<string> current = new HashSet<string>(StringComparer.Ordinal);

            foreach (FindingRow row in FindingsList.Rows)
            {
                if (row.HistoryKey.Length > 0)
                {
                    current.Add(row.HistoryKey);
                }
            }

            runHistory.Save(ProjectKey, current);
        }

        /// <summary>
        /// Puts what somebody has said about an element onto the element itself, so the plan
        /// and the counts can read it without going back to the store for every draw.
        ///
        /// Only acceptance is carried across. A review says somebody has looked, which leaves
        /// the finding open and leaves the colour alone; accepting says the difference is real
        /// and agreed, which takes it out of the work. A verdict recorded against a different
        /// set of results counts as neither - <see cref="CheckNaming.VerdictOn"/> is where that
        /// is decided, once, for the plan and the card and the drawer alike.
        /// </summary>
        /// <param name="project">
        /// Which project the verdicts are filed under, asked of Revit once by the caller rather
        /// than once per element: it is a walk through the Revit API every time, and this is
        /// run over every element in the model.
        /// </param>
        private void MarkVerdict(ElementViewModel element, string project)
        {
            if (element == null || !element.CanBeFiled)
            {
                return;
            }

            ReviewNote said = reviewNotes.Find(project, element.ReviewKey);

            string why = CheckNaming.WhyAcceptanceIsOvertaken(said, element);
            if (why != null)
            {
                TakeBackAcceptance(element.ReviewKey, element.Description, why);
                said = reviewNotes.Find(project, element.ReviewKey);
            }

            TakePlaceIntoOlderVerdict(said, element);

            element.IsAccepted = element.IsAutoAccepted
                                 || CheckNaming.VerdictOn(said, element) == HumanVerdict.Accepted;
        }

        /// <summary>
        /// Removes an acceptance outright, keeping any note that was written with it - the note
        /// is what somebody said, and is still worth reading when the element is looked at again.
        /// </summary>
        private void TakeBackAcceptance(string key, string description, string why)
        {
            reviewNotes.SetVerdict(ProjectKey, key, HumanVerdict.None, null);
            acceptancesTakenBack.Add(description + ": " + why + ".");
            reviewsChanged = true;
        }

        /// <summary>
        /// The acceptances filed while an element had no counterpart, under a name it no longer
        /// goes by now it has one.
        ///
        /// An unpaired Revit element is filed under its Revit id and a paired one under the
        /// ETABS name, and an ETABS line load nothing asked for stops being drawn at all once a
        /// line claims it - so the acceptance made while it was unpaired is left under a name
        /// nothing on the plan answers to. Left there it would settle nothing now and come back
        /// the moment the element lost its counterpart again, however different it was by then.
        ///
        /// A name something on the plan still goes by is left to <see cref="MarkVerdict"/>.
        /// </summary>
        private void TakeBackAcceptancesFiledWhileUnpaired(IList<ElementViewModel> everything)
        {
            List<ElementViewModel> all = everything
                .Concat(everything.SelectMany(element => element.Parts))
                .ToList();

            HashSet<string> inUse = new HashSet<string>(
                all.Where(element => element.CanBeFiled).Select(element => element.ReviewKey),
                StringComparer.OrdinalIgnoreCase);

            // Every name something paired now went by while it was unpaired, with what to call it.
            List<KeyValuePair<string, string>> formerNames = new List<KeyValuePair<string, string>>();
            foreach (ElementViewModel element in all.Where(element => element.State != CheckState.NotChecked
                                                                      && element.State != CheckState.Unpaired))
            {
                if (element.RevitId > 0)
                {
                    formerNames.Add(new KeyValuePair<string, string>(
                        element.Kind + "|R" + element.RevitId, element.Description));
                }
                if (element.EtabsUniqueName.Length > 0)
                {
                    formerNames.Add(new KeyValuePair<string, string>(
                        element.Kind + "|" + element.EtabsUniqueName, element.Description));
                }
            }
            foreach (EtabsLineLoad stretch in data.EtabsLineLoads.Where(stretch => stretch.Claimed))
            {
                formerNames.Add(new KeyValuePair<string, string>(
                    ElementKind.LineLoad + "|" + stretch.Key, "ETABS line load on " + stretch.FrameName));
            }

            string project = ProjectKey;
            foreach (KeyValuePair<string, string> name in formerNames)
            {
                if (inUse.Contains(name.Key))
                {
                    continue;
                }

                ReviewNote said = reviewNotes.Find(project, name.Key);
                if (said != null && said.Verdict == HumanVerdict.Accepted
                    && CheckNaming.SaidUnpaired(said.Fingerprint))
                {
                    TakeBackAcceptance(name.Key, name.Value,
                        "accepted with no counterpart, and paired now - its value needs checking");
                }
            }
        }

        /// <summary>
        /// Saves what marking the verdicts changed in the store, and says which acceptances were
        /// taken back - once, however many elements it was, and only when there were any.
        /// </summary>
        private void SettleReviews()
        {
            if (!reviewsChanged)
            {
                return;
            }

            reviewsChanged = false;
            SaveUserSettings();
            OnPropertyChanged(nameof(HasDecisions));
            OnPropertyChanged(nameof(DecisionsDescription));

            if (acceptancesTakenBack.Count > 0)
            {
                List<string> lines = new List<string>
                {
                    "These were accepted, and what was accepted is no longer what is there. The "
                    + "acceptance has been removed - any note is kept - so they count as open work "
                    + "until they are looked at again."
                };
                lines.AddRange(acceptancesTakenBack);
                acceptancesTakenBack.Clear();
                ShowIssues?.Invoke("Acceptances removed", lines);
            }
        }

        /// <summary>Acceptances taken back since the last time it was said.</summary>
        private readonly List<string> acceptancesTakenBack = new List<string>();

        /// <summary>
        /// A verdict recorded before where an element stands was part of what it is held to.
        ///
        /// It still holds if the results it was made on are the results the element carries now
        /// - that was the whole test when it was made - so where the element stands now is
        /// written into it, and from here on moving or resizing the element leaves it stale.
        /// Without this, every beam accepted before the change would come back open at once,
        /// for nothing more than the tool having learned to ask a new question.
        /// </summary>
        private void TakePlaceIntoOlderVerdict(ReviewNote said, ElementViewModel element)
        {
            if (said == null || !said.Reviewed || element.PlaceFingerprint.Length == 0
                || said.Fingerprint.Contains(ElementViewModel.PlaceMarker)
                || said.Fingerprint != element.ResultFingerprint)
            {
                return;
            }

            said.Fingerprint = element.VerdictFingerprint;
            reviewsChanged = true;
        }

        /// <summary>
        /// Set when marking the verdicts has changed the store - an older verdict given a place,
        /// or an acceptance taken back - so the file is saved once rather than per element.
        /// </summary>
        private bool reviewsChanged;

        /// <summary>
        /// Takes a changed verdict through everything that reads one: the colour on the plan,
        /// the chip counts, the level rail, the drawer and the card.
        ///
        /// Deliberately not a rebuild. Nothing about the model has changed - only what is filed
        /// against it - and a rebuild would clear the selection, so accepting a difference from
        /// the selected element's own card would close the card that was just used.
        /// </summary>
        private void RefreshAfterVerdictChange()
        {
            OnPropertyChanged(nameof(HasDecisions));
            OnPropertyChanged(nameof(DecisionsDescription));

            string project = ProjectKey;
            foreach (ElementViewModel element in allElements)
            {
                MarkVerdict(element, project);
                foreach (ElementViewModel part in element.Parts)
                {
                    MarkVerdict(part, project);
                }
            }

            // With accepted elements switched off, accepting one or taking an acceptance back
            // changes what the switches hold back, so it greys or comes back straight away.
            if (!ShowAccepted)
            {
                foreach (ElementViewModel element in allElements)
                {
                    ApplyResultFilters(element);
                    foreach (ElementViewModel part in element.Parts)
                    {
                        ApplyResultFilters(part);
                    }
                }
                OnPropertyChanged(nameof(IsFiltered));
            }

            SettleReviews();

            Findings.Update(allElements, SelectedStory?.Name);
            RefreshFindingsList();
            RefreshCard();
            RedrawPlan?.Invoke();
        }

        /// <summary>
        /// Rebuilds the card so it reads back what was just written. The element itself has not
        /// changed - only what is filed against it - so nothing else needs redrawing.
        /// </summary>
        private void RefreshCard()
        {
            if (selection.Count == 0)
            {
                return;
            }

            BuildCard();
            OnPropertyChanged(nameof(Card));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// The pairing editor behind the card's Change button: which Revit element the selected
        /// one really goes with.
        /// </summary>
        public PairingEditor Pairing { get; }

        public string Status
        {
            get => status;
            set => SetProperty(ref status, value);
        }

        /// <summary>
        /// True while a comparison is running. The view uses it to put up the progress bar
        /// and the stop button, and to keep the run button from being pressed twice.
        /// </summary>
        public bool IsChecking
        {
            get => isChecking;
            private set
            {
                if (SetProperty(ref isChecking, value))
                {
                    OnPropertyChanged(nameof(CanCompare));
                    OnPropertyChanged(nameof(IsIdle));
                }
            }
        }

        /// <summary>
        /// The complement, so a thing that stands down for a run needs no inverting converter.
        /// </summary>
        public bool IsIdle => !isChecking;

        /// <summary>
        /// True while the Revit read is the thing running, rather than a comparison. Both
        /// put the bar and the stop button up; this is what makes the button say which of
        /// them it would be stopping, because the two stop very differently - a run keeps
        /// what it has checked, a read keeps nothing.
        /// </summary>
        public bool IsImporting
        {
            get { return isImporting; }
            private set
            {
                if (SetProperty(ref isImporting, value))
                {
                    OnPropertyChanged(nameof(StopButtonText));
                    OnPropertyChanged(nameof(StopButtonHint));
                }
            }
        }

        public string StopButtonText
        {
            get { return isImporting ? "STOP THE IMPORT" : "STOP THE CHECK"; }
        }

        public string StopButtonHint
        {
            get
            {
                return isImporting
                    ? "Stops the read at the next element. Nothing is kept from a stopped "
                      + "read - whatever was imported before it still stands."
                    : "Stops at the next story. What has been checked so far is kept.";
            }
        }

        /// <summary>How far the running check has got, nought to one.</summary>
        public double CheckProgress
        {
            get => checkProgress;
            private set => SetProperty(ref checkProgress, value);
        }

        /// <summary>
        /// Whether the slab check covers the whole building. Turning it off and picking a
        /// single level is the quickest way to get an answer out of a large model, and to try
        /// a tolerance out before committing to the full run.
        /// </summary>
        public bool CheckEveryStory
        {
            get => checkEveryStory;
            set
            {
                if (SetProperty(ref checkEveryStory, value))
                {
                    OnPropertyChanged(nameof(CheckOneStoryOnly));
                    OnPropertyChanged(nameof(SlabCheckScopeDescription));
                    OnPropertyChanged(nameof(LoadingPlanDescription));
                    OnPropertyChanged(nameof(CanCompare));
                }
            }
        }

        /// <summary>The other side of <see cref="CheckEveryStory"/>, so the view can enable the picker.</summary>
        public bool CheckOneStoryOnly => !checkEveryStory;

        /// <summary>The one level the slab check runs on when it is not running on all of them.</summary>
        public Story_E SlabCheckStory
        {
            get => slabCheckStory;
            set
            {
                if (SetProperty(ref slabCheckStory, value))
                {
                    OnPropertyChanged(nameof(LoadingPlanDescription));
                    OnPropertyChanged(nameof(SlabCheckScopeDescription));
                    OnPropertyChanged(nameof(CanCompare));
                }
            }
        }

        public string SlabCheckScopeDescription
        {
            get
            {
                if (checkEveryStory)
                {
                    return $"Every level with a slab on it, {data.Slabs_r.Count} Revit floor(s) in all.";
                }

                return slabCheckStory == null
                    ? "Pick the level to check."
                    : $"Only {slabCheckStory.Name}. The floors below it are still read, so a drop " +
                      "panel modelled as a separate floor is still measured.";
            }
        }

        #region the loading plan

        /// <summary>
        /// Set by the view to put the loading plans window up. It edits the pairings in place,
        /// so there is nothing to answer: what comes back is written to the settings file
        /// either way.
        /// </summary>
        public Action<LoadingPlansViewModel> ShowLoadingPlans { get; set; }

        /// <summary>Opens the window where a level is paired with its loading plan.</summary>
        public ICommand OpenLoadingPlansCommand { get; }

        /// <summary>
        /// Whether the slab check also compares the area loads on each ETABS shell against the
        /// loading plan. Off leaves the check exactly as it was: thickness, outline, openings.
        /// </summary>
        public bool CheckSlabLoads
        {
            get => checkSlabLoads;
            set
            {
                if (SetProperty(ref checkSlabLoads, value))
                {
                    OnPropertyChanged(nameof(LoadingPlanDescription));
                    OnPropertyChanged(nameof(ShowsLoadRecheck));
                    RefreshVisibleCheckGroups();
                }
            }
        }

        private void OpenLoadingPlans()
        {
            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }

                // The schedule window reads and says columns for the project open now.
                ScheduleColumnOverrides.Current.ActiveProject = ProjectKey;

                LoadingPlansViewModel pairings = new LoadingPlansViewModel(
                    loadingPlanLinks, ProjectKey, revitEngine.getLoadingPlanViews(), data.Stories)
                {
                    ModelPatterns = ModelLoadPattern.From(data.Slabs, data.LineLoadFrames)
                };

                List<LoadingPlanLink> before = loadingPlanLinks.ForProject(ProjectKey).ToList();

                ShowLoadingPlans?.Invoke(pairings);

                // Written out whatever way the window was closed. The pairings were edited in
                // place as they were made, so a user who pairs eight levels and then loses
                // Revit should not lose the eight along with it.
                SaveUserSettings();

                NoteChangedPairings(before);

                OnPropertyChanged(nameof(LoadingPlanDescription));
                OnPropertyChanged(nameof(ShowsLoadRecheck));

                // The pairings are among what the forget button offers to forget.
                OnPropertyChanged(nameof(HasDecisions));
                OnPropertyChanged(nameof(DecisionsDescription));
            }
            catch (Exception e)
            {
                Status = "The loading plans could not be opened: " + e.Message;
            }
        }

        /// <summary>The pairing for the level the slab check is about to run on, or null.</summary>
        private LoadingPlanLink LinkForCheckedStory
        {
            get
            {
                return checkEveryStory || slabCheckStory == null
                    ? null
                    : loadingPlanLinks.Find(ProjectKey, slabCheckStory.Name);
            }
        }

        /// <summary>What the panel says about the loads as things currently stand.</summary>
        public string LoadingPlanDescription
        {
            get
            {
                if (!checkSlabLoads)
                {
                    return "The loads on each ETABS shell are not being checked.";
                }

                int ready = UsableLinks().Count;

                if (checkEveryStory)
                {
                    int paired = loadingPlanLinks.CountFor(ProjectKey);

                    if (ready == 0)
                    {
                        return paired == 0
                            ? "No level is paired with a loading plan yet, so there are no loads to " +
                              "check. Open \"Loading plans...\" and pair the levels you want checked."
                            : $"{paired} level(s) are paired but none of them has both a view and a " +
                              "schedule sheet yet, so there are no loads to check.";
                    }

                    return $"Every paired level is checked: {ready} of them. The rest are checked for " +
                           "thickness and outline as before.";
                }

                if (slabCheckStory == null)
                {
                    return "Pick the level to check.";
                }

                int elsewhereCount = loadingPlanLinks.CountFor(ProjectKey);
                string elsewhere = elsewhereCount == 0 ? "" : $" {elsewhereCount} level(s) paired in all.";

                LoadingPlanLink link = LinkForCheckedStory;

                if (link == null)
                {
                    return $"{slabCheckStory.Name} is not paired with a loading plan yet." + elsewhere;
                }
                if (link.WorkbookPath.Length == 0 || link.SheetName.Length == 0)
                {
                    return $"{slabCheckStory.Name} is paired with \"{link.ViewName}\" but has no " +
                           "schedule sheet picked, so its loads cannot be checked." + elsewhere;
                }

                return $"{slabCheckStory.Name} reads its loads from \"{link.ViewName}\", against " +
                       $"{link.SheetName} in {System.IO.Path.GetFileName(link.WorkbookPath)}." + elsewhere;
            }
        }

        /// <summary>
        /// The pairings this run will actually read. The choosing is
        /// <see cref="LoadingPlanLinks.UsableFor"/>; what belongs here is only which of the
        /// panel's switches it is asked with.
        /// </summary>
        private List<LoadingPlanLink> UsableLinks()
        {
            ScheduleColumnOverrides.Current.ActiveProject = ProjectKey;

            return checkSlabLoads
                ? loadingPlanLinks.UsableFor(ProjectKey, checkEveryStory,
                                             slabCheckStory == null ? null : slabCheckStory.Name)
                : new List<LoadingPlanLink>();
        }

        /// <summary>
        /// Which project the pairings are filed under. The path rather than the title, because
        /// two projects can carry the same title and only one of them is this one.
        /// </summary>
        private string ProjectKey
        {
            get
            {
                try
                {
                    Document document = commandData.Application.ActiveUIDocument.Document;
                    string path = document.PathName ?? "";
                    return path.Length > 0 ? path : document.Title ?? "";
                }
                catch (Exception)
                {
                    return "";
                }
            }
        }

        /// <summary>
        /// Puts this project's saved alignment, if there is one, onto <see cref="DataContainer"/>
        /// and the alignment panel. Called once at start up; a project nobody has aligned yet
        /// leaves the identity transform already in place.
        /// </summary>
        private void LoadAlignmentForCurrentProject()
        {
            object rawAlignment;
            Dictionary<string, object> savedAlignment;
            if (ProjectKey.Length == 0 || !alignmentsByProject.TryGetValue(ProjectKey, out rawAlignment)
                || (savedAlignment = rawAlignment as Dictionary<string, object>) == null)
            {
                return;
            }

            MyPoint translationInMM = new MyPoint(
                Json.GetDouble(savedAlignment, "dx", 0),
                Json.GetDouble(savedAlignment, "dy", 0),
                Json.GetDouble(savedAlignment, "dz", 0));

            MyTransformation transformation = new MyTransformation();
            transformation.SetAlignment(translationInMM, Json.GetDouble(savedAlignment, "rotation", 0),
                Json.GetBool(savedAlignment, "mirroredX", false));
            transformation.IsMirroredAboutY = Json.GetBool(savedAlignment, "mirroredY", false);

            DataContainer.ModelTransformation = transformation;
            Alignment.LoadSaved(transformation);
            Alignment.ReadHeightPairing(savedAlignment);
        }

        /// <summary>Set the moment an alignment is applied in this session.</summary>
        private bool alignmentApplied;

        /// <summary>
        /// The alignment the Revit elements in hand were built under, or null when none have
        /// been read yet.
        ///
        /// Kept so a changed alignment can move the model that is already here rather than
        /// read the whole thing out of Revit again: the difference between this and the new
        /// alignment is the move. See <see cref="MyTransformation.Difference"/>.
        /// </summary>
        private MyTransformation placedUnder;

        /// <summary>
        /// The SAFE level the Revit elements in hand were narrowed to, as <see cref="NarrowingKey"/>
        /// gives it, or null when they are the whole model. A move cannot put back what the
        /// narrowing threw away, so a pairing that has changed since means reading again.
        /// </summary>
        private string narrowedTo;

        /// <summary>
        /// The leave-out lists the Revit elements in hand have had applied to them, or null
        /// when none have been read yet: the lists the import ran under, and every name a
        /// check has taken out since. A name added to a list is applied by the next check;
        /// a name taken out of one needs the model read again, and this is how that is told.
        /// </summary>
        private RevitLeaveOut revitLeftOut;

        /// <summary>This project's stored alignment, or an empty one where it has none.</summary>
        private Dictionary<string, object> SavedAlignment
        {
            get
            {
                object raw;
                Dictionary<string, object> saved;
                if (ProjectKey.Length > 0 && alignmentsByProject.TryGetValue(ProjectKey, out raw)
                    && (saved = raw as Dictionary<string, object>) != null)
                {
                    return saved;
                }
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        /// <summary>Remembers the alignment now in force against <see cref="ProjectKey"/>.</summary>
        private void StoreAlignmentForCurrentProject()
        {
            if (ProjectKey.Length == 0)
            {
                return;
            }

            MyTransformation transformation = DataContainer.ModelTransformation;
            var stored = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                // Whether anybody has ever aligned this project, as against the transform being
                // the identity - which is also what two models that already share coordinates
                // have. Without it the preflight cannot tell "aligned, nothing to move" from
                // "never aligned", and would nag a correctly set up project on every run.
                { "applied", alignmentApplied || Json.GetBool(SavedAlignment, "applied", false) },
                { "dx", transformation.Translation.X },
                { "dy", transformation.Translation.Y },
                { "dz", transformation.Translation.Z },
                { "rotation", transformation.Rotation },
                { "mirroredX", transformation.IsMirroredAboutX },
                { "mirroredY", transformation.IsMirroredAboutY }
            };

            // The story and level the height came from, so it comes back paired.
            Alignment.WriteHeightPairing(stored);
            alignmentsByProject[ProjectKey] = stored;
        }

        /// <summary>
        /// The loading plan regions each level's load check was last run against, by level, for
        /// the raw data to draw. The check reads them for itself and throws them away; the raw
        /// data has no other way of seeing what the loading plan said.
        /// </summary>
        private readonly Dictionary<string, LoadCoverage> loadingPlansRead =
            new Dictionary<string, LoadCoverage>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Keeps what a load pass read, for the raw data. A pass over some levels replaces what
        /// those levels had - a level just unpaired loses its plan - and leaves the rest alone;
        /// a pass over all of them replaces the lot.
        /// </summary>
        private void KeepLoadingPlans(Dictionary<string, LoadCoverage> read, HashSet<string> onlyStories)
        {
            if (onlyStories == null)
            {
                loadingPlansRead.Clear();
            }
            else
            {
                foreach (string story in loadingPlansRead.Keys
                             .Where(story => onlyStories.Contains(StoryKey(story))).ToList())
                {
                    loadingPlansRead.Remove(story);
                }
            }

            foreach (KeyValuePair<string, LoadCoverage> level in read)
            {
                loadingPlansRead[level.Key] = level.Value;
            }
        }

        /// <summary>
        /// The loading plan read off the paired view and looked up in the schedule, or null
        /// when the level being checked has no usable pairing.
        ///
        /// Everything is read here, when the check runs, rather than held on the panel: the
        /// pairing is the only copy of what was decided, and reading it at the last moment is
        /// what makes it impossible for the check to run against a workbook or a view the
        /// panel merely used to be showing.
        ///
        /// <paramref name="onlyStories"/> narrows it to those levels, for a re-check of the
        /// ones whose pairing changed; null reads every level the run would.
        /// </summary>
        private Dictionary<string, LoadCoverage> BuildLoadCoverages(IList<string> issues,
                                                                    HashSet<string> onlyStories = null)
        {
            Dictionary<string, LoadCoverage> byStory =
                new Dictionary<string, LoadCoverage>(StringComparer.OrdinalIgnoreCase);

            // A fresh read starts with nothing noted for the dump.
            slabLoadReads.Clear();

            if (!checkSlabLoads)
            {
                return byStory;
            }

            List<LoadingPlanLink> links = UsableLinks()
                .Where(link => onlyStories == null || onlyStories.Contains(StoryKey(link.Story)))
                .ToList();

            if (links.Count == 0)
            {
                // Narrowed, nothing left is a level that was unpaired, which is not a fault.
                if (onlyStories == null)
                {
                    issues.Add("The loads were not checked: " + LoadingPlanDescription);
                }
                return byStory;
            }

            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }
            }
            catch (Exception e)
            {
                issues.Add("The loading plans could not be read, so the loads were not checked - " + e.Message);
                return byStory;
            }

            // One schedule per sheet, not per level. A project has one workbook and levels
            // share sheets - five podium levels off one podium sheet is the normal shape - so
            // without this the same file is opened and parsed once per level, and the same
            // complaints about it are reported that many times over.
            Dictionary<string, LoadSchedule> schedules = new Dictionary<string, LoadSchedule>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadingPlanLink link in links)
            {
                Status = $"Reading the loading plan for {link.Story}...";
                Pump();

                LoadCoverage coverage = BuildOneLoadCoverage(link, schedules, issues);

                if (coverage != null)
                {
                    byStory[link.Story] = coverage;
                }
            }

            if (byStory.Count > 0)
            {
                issues.Add($"Loads read for {byStory.Count} level(s): " +
                           string.Join(", ", byStory.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));

                if (!data.EtabsReadsNullLoadAreas)
                {
                    issues.Add(StaleModelFileNote);
                }
            }

            return byStory;
        }

        /// <summary>
        /// What the slab load check read on each level this run, kept until the pieces are
        /// back so each level's dump can hold both - see <see cref="SlabLoadDump"/>. Keyed by
        /// the level name as the pairing trims it.
        /// </summary>
        private readonly Dictionary<string, SlabLoadDump.Level> slabLoadReads =
            new Dictionary<string, SlabLoadDump.Level>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Writes down what the slab load check read and found on each level it read a loading
        /// plan for, and says where each file went.
        /// </summary>
        private void DumpSlabLoads(IEnumerable<SlabZone> pieces, IList<string> issues)
        {
            if (slabLoadReads.Count == 0)
            {
                return;
            }

            List<SlabZone> all = (pieces ?? Enumerable.Empty<SlabZone>()).ToList();

            foreach (SlabLoadDump.Level level in slabLoadReads.Values)
            {
                try
                {
                    string key = StoryKey(level.Story);
                    level.Shells = data.Slabs.Where(slab => string.Equals(StoryKey(slab.Story), key,
                                                                          StringComparison.OrdinalIgnoreCase))
                                             .ToList();
                    string path = SlabLoadDump.Write(GeometryDump.DefaultFolder(), level,
                        all.Where(zone => string.Equals(StoryKey(zone.Story), key,
                                                        StringComparison.OrdinalIgnoreCase)));

                    issues.Add($"{level.Story}: what the slab load check read and found is written "
                               + $"down in {path}");
                }
                catch (Exception e)
                {
                    issues.Add($"{level.Story}: what the slab load check read could not be written "
                               + "down - " + e.Message);
                }
            }

            slabLoadReads.Clear();
        }

        /// <summary>
        /// The loading plan for one level: the filled regions off its view, looked up in its
        /// sheet of the schedule. Null when that level cannot be checked, with the reason
        /// added to the run's issues rather than raised as a dialog - one level being wrong is
        /// not a reason to stop checking the other forty-six.
        /// </summary>
        private LoadCoverage BuildOneLoadCoverage(LoadingPlanLink link,
                                                  IDictionary<string, LoadSchedule> schedules,
                                                  IList<string> issues)
        {
            try
            {
                string sheetKey = link.WorkbookPath + "|" + link.SheetName;

                LoadSchedule schedule;
                if (!schedules.TryGetValue(sheetKey, out schedule))
                {
                    schedule = LoadScheduleReader.Read(link.WorkbookPath, link.SheetName);
                    schedules[sheetKey] = schedule;

                    // Reported once for the sheet, not once per level that reads it.
                    foreach (string issue in schedule.Issues)
                    {
                        issues.Add($"{link.SheetName}: {issue}");
                    }
                }

                if (schedule.Count == 0)
                {
                    issues.Add($"{link.Story}: {link.SheetName} in " +
                               $"{System.IO.Path.GetFileName(link.WorkbookPath)} has no numbered loads " +
                               "on it, so there is nothing to check its loads against.");
                    return null;
                }

                // Anything the read complains about goes onto the engine's own list, which is
                // only shown at import time - so what it added just now is taken across to the
                // list this check is about to show.
                int alreadyReported = revitEngine.ImportIssues.Count;

                IList<LoadRegion_R> regions = revitEngine.getLoadRegions(link.ViewId);

                for (int i = alreadyReported; i < revitEngine.ImportIssues.Count; i++)
                {
                    issues.Add($"{link.Story}: {revitEngine.ImportIssues[i]}");
                }

                // Noted for the dump whatever comes of it: a view with no regions on it is as
                // much worth writing down as one with four hundred.
                if (PrefrencesData.getPrefrencesData().WriteSlabLoadDump)
                {
                    slabLoadReads[StoryKey(link.Story)] = new SlabLoadDump.Level
                    {
                        Story = link.Story,
                        ViewId = link.ViewId,
                        ViewName = link.ViewName ?? "",
                        Workbook = link.WorkbookPath ?? "",
                        Sheet = link.SheetName ?? "",
                        Schedule = schedule,
                        Regions = regions
                    };
                }

                if (regions.Count == 0)
                {
                    issues.Add($"{link.Story}: \"{link.ViewName}\" has no filled regions on it, so there " +
                               "is no loading plan to check its loads against. Check that this is the " +
                               "right view.");
                    return null;
                }

                LoadCoverage coverage = LoadCoverage.Build(regions, schedule, issues);

                issues.Add($"{link.Story}: {regions.Count} region(s) off \"{link.ViewName}\" against " +
                           $"{schedule.Count} numbered load(s) on {schedule.SheetName}.");

                return coverage;
            }
            catch (Exception e)
            {
                issues.Add($"{link.Story}: the loading plan could not be read, so its loads were not " +
                           "checked - " + e.Message);
                return null;
            }
        }

        #endregion

        /// <summary>
        /// What is said of a design model read off a file saved before the None areas carrying
        /// load were kept: every one of them is missing from it, and only a fresh read of the
        /// model can put them back.
        /// </summary>
        private string StaleModelFileNote =>
            $"The {DesignProductName} model was read from \"{data.EtabsSourceName}\", a file saved before None " +
            "areas carrying load were read. Every such area is missing from it, so the slab under one reads " +
            $"as carrying its own load alone. Import the model from {DesignProductName} again, or save the file " +
            "again with this version, before trusting the slab load check.";

        /// <summary>Read the ETABS model from a saved json file instead of a running instance.</summary>
        public bool ImportFromFile
        {
            get => importFromFile;
            set => SetProperty(ref importFromFile, value);
        }

        #region which program the design model is in

        /// <summary>
        /// Asks which of the open models to read, and returns null when the user cancels. Set
        /// by the window, because putting a dialog up is the view's job and not this one's.
        /// </summary>
        public Func<CsiInstance> PickModel { get; set; }

        private CsiProduct csiProduct = CsiProduct.Etabs;

        /// <summary>The two programs a design model can be read out of, for the picker.</summary>
        public IList<CsiProduct> CsiProductChoices
        {
            get { return new List<CsiProduct> { CsiProduct.Etabs, CsiProduct.Safe }; }
        }

        /// <summary>
        /// Which program the design model is open in. Changing it does not re-import: the
        /// model already read stays on the panel until the next import, the same as changing
        /// any other import setting.
        /// </summary>
        public CsiProduct CsiProduct
        {
            get { return csiProduct; }
            set
            {
                if (SetProperty(ref csiProduct, value))
                {
                    OnPropertyChanged(nameof(IsSingleLevelProduct));
                    OnPropertyChanged(nameof(DesignProductName));
                    OnPropertyChanged(nameof(ImportDesignModelText));
                    OnPropertyChanged(nameof(ExportDesignModelText));

                    // A SAFE run keeps only the floor it was paired with, so the Revit model
                    // read for one product is the wrong model for the other - too little of it
                    // going back to ETABS, all of it going to SAFE. Marked stale rather than
                    // re-read, because re-reading a big model is not something to do to
                    // somebody who only clicked a picker.
                    Models.Revit.IsStale = true;
                    SaveUserSettings();
                }
            }
        }

        /// <summary>"ETABS" or "SAFE", for every label that names the program.</summary>
        public string DesignProductName { get { return CsiProducts.Name(csiProduct); } }

        public string ImportDesignModelText { get { return "Import " + DesignProductName + " model"; } }

        /// <summary>True for SAFE, which is the one that needs a level pairing.</summary>
        public bool IsSingleLevelProduct { get { return CsiProducts.IsSingleLevel(csiProduct); } }

        private IList<RevitLevel> revitLevels = new List<RevitLevel>();

        /// <summary>
        /// The levels in the Revit project, read the first time they are asked for rather than
        /// at start up: it needs a Revit document, and the window opens before one has
        /// necessarily been asked for.
        /// </summary>
        public IList<RevitLevel> RevitLevels
        {
            get
            {
                if (revitLevels.Count == 0)
                {
                    RefreshRevitLevels();
                }
                return revitLevels;
            }
        }

        public void RefreshRevitLevels()
        {
            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }
                revitLevels = revitEngine.getLevels();
                OnPropertyChanged(nameof(RevitLevels));
            }
            catch (Exception e)
            {
                Status = "The Revit levels could not be read: " + e.Message;
            }
        }

        /// <summary>
        /// The level this project's SAFE model stands for, or null where it has none: the
        /// Revit level the alignment paired a story with to get the height in force. A SAFE
        /// model has one story, so that pairing is the whole of which floor it is - there is
        /// no second place to say it, and so nothing for the two to disagree about.
        ///
        /// Read from the alignment each time rather than kept, so applying a new pairing or
        /// clearing the alignment is the end of the old level, with nothing left to forget.
        /// </summary>
        private SafeLevelLink SafeLevelForCurrentProject
        {
            get
            {
                string name = Alignment.AppliedLevelName;
                if (!IsSingleLevelProduct || string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                if (revitLevels.Count == 0)
                {
                    RefreshRevitLevels();
                }

                long id = Alignment.AppliedLevelId;
                RevitLevel level = revitLevels.FirstOrDefault(l => l.Id == id)
                                   ?? revitLevels.FirstOrDefault(
                                          l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
                if (level == null)
                {
                    // Deleted from the Revit model since it was paired. Narrowing to where it
                    // used to be would be a guess, so it is treated as not paired.
                    return null;
                }

                return new SafeLevelLink
                {
                    LevelName = level.Name,
                    LevelId = level.Id,
                    // The frame the alignment's height was worked out in, so the floor is
                    // found exactly where the alignment put it.
                    LevelElevationMM = level.ProjectElevationMM + Alignment.AppliedLevelOffsetMM
                };
            }
        }

        /// <summary>
        /// Keeps only the Revit elements standing at the paired level, for a run against a
        /// model that holds one floor. Does nothing for ETABS, which says what floor
        /// everything is on and needs the whole building.
        ///
        /// Said out loud rather than done quietly. Dropping most of the Revit model is the
        /// right thing to do here and an alarming thing to have happened without being told,
        /// and the count is what tells whoever is looking that the band is set sensibly.
        /// </summary>
        private void narrowRevitToSingleLevel()
        {
            SafeLevelLink level = SafeLevelForCurrentProject;
            if (level == null || !level.IsPaired)
            {
                return;
            }

            string dropped = data.NarrowRevitToLevel(level.AlignedElevationMM,
                                                     PrefrencesData.getPrefrencesData().SingleLevelBand);
            narrowedTo = NarrowingKey(level);
            Status = $"Narrowed to {level.LevelName}: {dropped}";
        }

        /// <summary>Which level, and where on it, a narrowing kept - null for none.</summary>
        private static string NarrowingKey(SafeLevelLink level)
        {
            return level == null || !level.IsPaired
                ? null
                : level.LevelId + "|" + level.LevelName + "|"
                  + Math.Round(level.LevelElevationMM, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when the Revit elements in hand were narrowed to the level the alignment pairs
        /// the SAFE model with now - or, for ETABS or an unpaired SAFE model, not narrowed.
        /// </summary>
        private bool RevitNarrowingIsCurrent
        {
            get { return narrowedTo == NarrowingKey(SafeLevelForCurrentProject); }
        }

        #endregion

        public bool ShowUnpairedEtabs
        {
            get => showUnpairedEtabs;
            set { if (SetProperty(ref showUnpairedEtabs, value)) RebuildElements(); }
        }

        public bool ShowUnpairedRevit
        {
            get => showUnpairedRevit;
            set { if (SetProperty(ref showUnpairedRevit, value)) RebuildElements(); }
        }

        /// <summary>Elements imported but not compared yet, which is everything before a run.</summary>
        public bool ShowNotChecked
        {
            get => showNotChecked;
            set { if (SetProperty(ref showNotChecked, value)) RebuildElements(); }
        }

        /// <summary>
        /// Elements accepted as a real and agreed difference, by hand or by the check. Switched
        /// off, every result on one is held back and it is drawn grey, the same as an element
        /// whose results are all switched off - it stays on the plan, so the floor still reads.
        /// </summary>
        public bool ShowAccepted
        {
            get => showAccepted;
            set { if (SetProperty(ref showAccepted, value)) RebuildElements(); }
        }

        /// <summary>
        /// A switch per check per verdict. These control which results are drawn, never which
        /// elements are: every column stays on the plan whatever is switched off here.
        /// </summary>
        public CheckFilters CheckFilters { get; }

        /// <summary>
        /// True while something on the plan is being held back. These switches used to sit in a
        /// panel that was always open, where an unticked box was its own warning; folded behind
        /// a button they are out of sight, and a plan reading clean because half of it is
        /// switched off is the one misreading this window could cause.
        ///
        /// Never while the raw data is up, which has no results for the switches to hold back.
        /// </summary>
        public bool IsFiltered => !showRawData
                                  && (!CheckFilters.AllShown
                                      || !ShowUnpairedEtabs || !ShowUnpairedRevit || !ShowNotChecked
                                      || !ShowAccepted);

        /// <summary>
        /// The counts on the tab strip and down the level rail. Refreshed by every rebuild, so
        /// a switch turned off takes its findings out of the numbers as well as off the plan.
        /// </summary>
        public FindingTally Findings { get; }

        /// <summary>Which two models are loaded and when they were last checked, for the header.</summary>
        public ModelStatus Models { get; } = new ModelStatus();

        /// <summary>
        /// The worklist under the plan: every open finding on the level being looked at. The
        /// plan says what is wrong where; this says what is left, which is the question a
        /// drawing cannot answer.
        /// </summary>
        public FindingsList FindingsList { get; } = new FindingsList();

        /// <summary>
        /// The colour key along the right of the scope bar, in the colours the canvas draws.
        /// Right-clicking an entry changes its colour - see <see cref="ChangeVerdictColour"/>.
        /// </summary>
        public IList<VerdictKeyEntry> VerdictKey { get; } =
            VerdictColours.All.Select(colour => new VerdictKeyEntry(colour)).ToList();

        /// <summary>
        /// The key while the raw data is up, when nothing on the plan is a verdict: two
        /// colours, one per model.
        /// </summary>
        public IList<VerdictKeyEntry> RawDataKey { get; } = new List<VerdictKeyEntry>
        {
            new VerdictKeyEntry(VerdictColour.EtabsOnly, "ETABS"),
            new VerdictKeyEntry(VerdictColour.RevitOnly, "Revit")
        };

        /// <summary>
        /// Whichever key says what the plan on show is drawn in - and, while the height zones
        /// are on it, the colour each height is drawn in.
        /// </summary>
        public IList<VerdictKeyEntry> ShownVerdictKey
        {
            get
            {
                IList<VerdictKeyEntry> key = showRawData ? RawDataKey : VerdictKey;
                IList<VerdictKeyEntry> heights = HeightZoneKey;
                return heights.Count == 0 ? key : key.Concat(heights).ToList();
            }
        }

        /// <summary>
        /// The heights of the zones on the level on show, lowest first, each in its colour. The
        /// colour is the height's, so this is the whole of what the zones' colours mean.
        /// </summary>
        private IList<VerdictKeyEntry> HeightZoneKey
        {
            get
            {
                if (Findings == null || !Findings.IsKindActive(ElementKind.HeightZone))
                {
                    return new List<VerdictKeyEntry>();
                }

                string story = SelectedStory?.Name;
                IList<HeightZone> shown = story == null
                    ? heightZones.ForProject(ProjectKey)
                    : heightZones.OnStory(ProjectKey, story);

                return shown.Select(zone => Math.Round(zone.Height))
                            .Distinct()
                            .OrderBy(height => height)
                            .Select(height => new VerdictKeyEntry(
                                HeightZone.Metres(height),
                                heightZones.ColourOf(ProjectKey, height),
                                "Height zones of " + HeightZone.Metres(height) + " floor to floor. Each height has "
                                + "a colour of its own; where zones overlap, only the one in control is coloured in."))
                            .ToList();
            }
        }

        /// <summary>
        /// Tells the window a verdict colour has changed, so the brushes its own chrome draws
        /// verdicts with - the tab counts, the level bars, the pills on the card - follow.
        /// </summary>
        public Action VerdictColoursChanged { get; set; }

        /// <summary>
        /// Draws one verdict in a new colour from now on, everywhere it is shown, and keeps it
        /// for the next session.
        ///
        /// A redraw rather than a rebuild: no result has changed, only what it is painted in,
        /// and a rebuild would drop the selection. Two verdicts in the same colour are allowed -
        /// it is somebody's own plan - but said, because on the plan they can no longer be told
        /// apart.
        /// </summary>
        public void ChangeVerdictColour(VerdictColour which, System.Windows.Media.Color colour)
        {
            if (!VerdictColours.Set(which, colour))
            {
                return;
            }

            foreach (VerdictKeyEntry entry in VerdictKey.Concat(RawDataKey))
            {
                entry.Refresh();
            }
            CheckFilters.RefreshSwatches();
            VerdictColoursChanged?.Invoke();
            RefreshFindingsList();
            RefreshCard();
            RedrawPlan?.Invoke();
            SaveUserSettings();

            IList<VerdictColour> sharing = VerdictColours.SharingColourWith(which);
            Status = VerdictColours.NameOf(which)
                     + (VerdictColours.IsDefault(which)
                            ? " is back to its own colour."
                            : " is drawn in the colour you picked. Right-click it again to change it, "
                              + "or to pick its own colour back from the custom colours.")
                     + (sharing.Count > 0
                            ? " It is the same colour as "
                              + string.Join(" and ", sharing.Select(VerdictColours.NameOf))
                              + " now, so the two cannot be told apart on the plan."
                            : "");
        }

        /// <summary>
        /// The switches belonging to the mode being looked at. The rest keep their settings
        /// while they are off screen, so switching back finds the panel as it was left.
        /// </summary>
        public ObservableCollection<CheckFilterGroup> VisibleCheckGroups { get; }
            = new ObservableCollection<CheckFilterGroup>();

        /// <summary>
        /// A chip switched on or off. The rows in the panel follow the chips, and the plan is
        /// redrawn so a kind that has just been switched off goes grey in the same gesture. A
        /// chip only changes which of the built elements are coloured and which are grey, so
        /// what was built is shown again rather than built again.
        /// </summary>
        private void OnScopeChanged()
        {
            RefreshVisibleCheckGroups();

            if (hasBuilt)
            {
                ShowLevel();
            }
            else
            {
                RebuildElements();
            }
        }

        private void RefreshVisibleCheckGroups()
        {
            VisibleCheckGroups.Clear();
            foreach (CheckFilterGroup group in CheckFilters.Groups.Where(g => Findings.IsActive(g.Mode)))
            {
                // The load switches are only worth a row when the load check is on. Left up
                // with it off they would be three switches over results nothing publishes.
                if (group.Kind == CheckKind.SlabLoads && !checkSlabLoads)
                {
                    continue;
                }

                VisibleCheckGroups.Add(group);
            }
        }

        public bool CanCompare => !IsChecking
                                  && (checkEveryStory || slabCheckStory != null)
                                  && data.Frames_e.Count + data.Columns.Count + data.Walls.Count
                                     + data.Slabs.Count > 0
                                  && data.Frames_r.Count + data.Columns_r.Count + data.Walls_r.Count
                                     + data.Slabs_r.Count > 0;

        /// <summary>One model on its own is enough to export; the other half of each row is blank.</summary>
        public bool CanExportBeamMarks => data.Frames_e.Count + data.Frames_r.Count > 0;

        public string Summary
        {
            get
            {
                if (showRawData)
                {
                    return RawDataSummary;
                }

                // With the chips being multi-select there is no single "current check" to
                // summarise, so the line follows what is actually switched on: the frame
                // counts while any frame chip is up, the slab line while only the slabs are.
                bool anyFrame = Findings.IsActive(ViewMode.Beams)
                                || Findings.IsActive(ViewMode.Columns)
                                || Findings.IsActive(ViewMode.Walls);
                if (!anyFrame && Findings.IsKindActive(ElementKind.Slab))
                {
                    return SlabSummary;
                }

                // The load chip is on but nothing came back for it. Said here as well as in the
                // slab line, because with a frame chip up too the slab line is not the one being
                // read - and an empty load plan with no word about why reads as a broken tool.
                if (Findings.IsActive(ViewMode.SlabLoads) && hasCompared
                    && data.SlabLoadZones.Count == 0)
                {
                    return WhyNoLoadZones + "   |   " + FrameSummary;
                }

                // The same for the line load tab, and for the same reason: a plan with nothing
                // drawn on it and no word about why reads as a broken tool rather than as a check
                // nobody has set up yet.
                if (Findings.IsActive(ViewMode.LineLoads)
                    && data.LineLoadResults.Count == 0 && data.EtabsLineLoads.Count == 0)
                {
                    return WhyNoLineLoads + "   |   " + FrameSummary;
                }

                if (Findings.IsActive(ViewMode.SidlC)
                    && data.SidlCResults.Count == 0 && data.EtabsSidlC.Count == 0)
                {
                    return WhyNoSidlC + "   |   " + FrameSummary;
                }

                if (Findings.IsActive(ViewMode.SidlCArea)
                    && data.SidlCAreaResults.Count == 0 && data.UnclaimedSidlCArea.Count == 0)
                {
                    return WhyNoSidlCArea + "   |   " + FrameSummary;
                }

                return FrameSummary;
            }
        }

        /// <summary>
        /// What the raw data has on the level on show, model by model, and - for the two load
        /// tabs - why one side of it may be missing. The loading plans are only read when the
        /// check runs, so before then the loads are ETABS alone, and a plan with half of it
        /// missing and no word about why reads as a Revit model with no loads in it.
        /// </summary>
        private string RawDataSummary
        {
            get
            {
                List<ElementViewModel> shown = Elements.Where(element => element.IsRaw
                                                                        && !element.IsDrawnAsContext)
                                                       .ToList();

                List<string> parts = new List<string>
                {
                    "Raw data - both models as they were read, nothing paired or compared",
                    "ETABS: " + RawCount(shown, ElementSource.EtabsOnly),
                    "Revit: " + RawCount(shown, ElementSource.RevitOnly)
                };

                // Before a run the one reason is that nothing has been read yet. After one, the
                // check's own answer for an empty plan is the right one.
                if (Findings.IsActive(ViewMode.LineLoads) && data.LineLoadResults.Count == 0)
                {
                    parts.Add(hasCompared || data.LineLoadFrames.Count == 0
                        ? WhyNoLineLoads
                        : "Line loads: the loading plans are read when the check runs, so the "
                          + "Revit side is here once RUN CHECK has been pressed");
                }

                if (Findings.IsActive(ViewMode.SlabLoads) && loadingPlansRead.Count == 0)
                {
                    parts.Add(!checkSlabLoads
                        ? "Slab loads: the load check is switched off, so no loading plan has been "
                          + "read - turn it on under Set up, in What is checked"
                        : !hasCompared
                            ? "Slab loads: the loading plans are read when the check runs, so the "
                              + "regions on them are here once RUN CHECK has been pressed"
                            : "Slab loads: no loading plan was read - pair the levels with one under "
                              + "Set up, in Loading plans");
                }

                return string.Join("   |   ", parts);
            }
        }

        /// <summary>"12 beams, 4 columns", for one model's side of what is on show.</summary>
        private static string RawCount(IEnumerable<ElementViewModel> shown, ElementSource source)
        {
            List<string> counts = LabelGrouping.Order
                .Select(kind => new { kind, count = shown.Count(element => element.Kind == kind
                                                                          && element.Source == source) })
                .Where(entry => entry.count > 0)
                .Select(entry => entry.count + " " + RawKindWord(entry.kind, entry.count))
                .ToList();

            return counts.Count == 0 ? "nothing on this level" : string.Join(", ", counts);
        }

        private static string RawKindWord(ElementKind kind, int count)
        {
            string word;
            switch (kind)
            {
                case ElementKind.Beam: word = "beam"; break;
                case ElementKind.Column: word = "column"; break;
                case ElementKind.Wall: word = "wall"; break;
                case ElementKind.LineLoad: word = "line load"; break;
                default: word = "slab"; break;
            }
            return count == 1 ? word : word + "s";
        }

        /// <summary>
        /// Why the load check has nothing to show. There are only two reasons, and neither is
        /// visible from a blank plan, so it says which.
        /// </summary>
        /// <summary>
        /// Why the line load check has nothing to show. Three reasons, all of them invisible
        /// from a blank plan, so it says which one it was.
        /// </summary>
        private string WhyNoLineLoads
        {
            get
            {
                if (!PrefrencesData.getPrefrencesData().RunLineLoadCheck)
                {
                    return "Line loads: the check is switched off. Turn it on under Set up, in "
                           + "Settings, and import the ETABS model again - the frames' distributed "
                           + "loads are only read when it is on.";
                }

                // Asked before anything about the pairing or the drawing, because it is the
                // answer whenever the plan is blank and nobody has pressed the button yet -
                // and the one that would otherwise be reported as a problem with the setup.
                if (!hasCompared)
                {
                    return "Line loads: nothing has been read yet. Press RUN CHECK - the loading "
                           + "plans are read then, and both models' line loads appear here.";
                }

                if (loadingPlanLinks.UsableForLineLoads(ProjectKey, CheckEveryStory,
                                                        SlabCheckStory?.Name).Count == 0)
                {
                    return "Line loads: no level here has a line load sheet paired with it - pair "
                           + "one under Set up, in Loading plans, beside the area load sheet.";
                }

                // Asked before the drawing is, because this is the reason that says nothing
                // about the drawing at all. A design model read from a file written before this
                // check existed carries no distributed loads, and blaming the line styles for
                // that sent somebody to look at the one thing that was not wrong.
                if (data.LineLoadFrames.Count == 0)
                {
                    return "Line loads: the design model carries no distributed loads on its frames. "
                           + "A model saved before this check existed does not hold them - import it "
                           + "from ETABS again and save it over, and they will be in the file.";
                }

                return "Line loads: nothing on the paired loading plans is drawn in a line style "
                       + "the settings name as a line load. Check the style patterns under Set up, "
                       + "in Settings.";
            }
        }

        private string WhyNoLoadZones => checkSlabLoads
            ? "Slab loads: no level is paired with a loading plan yet - pair them under Set up, "
              + "in Loading plans. The shells are drawn uncoloured."
            : "Slab loads: the check is switched off. Turn it on under Set up, in What is checked.";

        private string FrameSummary
        {
            get
            {
                if (!hasCompared)
                {
                    return $"ETABS: {data.Frames_e.Count} beams, {data.Columns.Count} columns, " +
                           $"{data.Walls.Count} wall areas   |   " +
                           $"Revit: {data.Frames_r.Count} beams, {data.Columns_r.Count} columns, " +
                           $"{data.Walls_r.Count} walls";
                }

                int columnsPassed = data.PairedColumns.Count(c => c.State == ColumnCheckState.Matched);
                int columnsReview = data.PairedColumns.Count(c => c.State == ColumnCheckState.Review);
                int columnsFailed = data.PairedColumns.Count - columnsPassed - columnsReview;
                int typeMarkFailed = data.PairedColumns.Count(
                    c => c.GroupCheck.Result == GroupCheckResult.Mismatched);
                int beamsFailed = data.PairedFrames.Count(f => f.MyDesignState != IDrawAble.designState.ignore);
                int beamsPassed = data.PairedFrames.Count - beamsFailed;

                return $"Columns: {columnsPassed} matched, {columnsFailed} mismatched " +
                       $"({typeMarkFailed} on the type mark), {columnsReview} in multiple groups, " +
                       $"{data.UnmatchedEtabsColumns.Count + data.UnmatchedRevitColumns.Count} unpaired" +
                       CrossPairSummary + "   |   " +
                       $"Beams: {beamsPassed} ok, {beamsFailed} flagged, " +
                       $"{data.UnmatchedEtabs.Count + data.UnmatchedRevit.Count} unpaired" +
                       WallSummary;
            }
        }

        /// <summary>
        /// How many columns and walls were paired across the kinds, where any were. Empty
        /// otherwise, so a run with it switched off reads as it always did.
        /// </summary>
        private string CrossPairSummary
        {
            get
            {
                if (data.CrossPairs.Count == 0)
                {
                    return "";
                }

                int columnsAsWalls = data.CrossPairs.Count(pair => pair.EtabsIsColumn);
                int wallsAsColumns = data.CrossPairs.Count - columnsAsWalls;
                int failed = data.CrossPairs.Count(pair => pair.Outcomes.Any(o => o.Level == ResultLevel.Failed));

                return $", {columnsAsWalls} ETABS column(s) paired with Revit walls and " +
                       $"{wallsAsColumns} ETABS wall panel(s) with Revit columns ({failed} mismatched)";
            }
        }

        /// <summary>
        /// What the wall check made of the model, added to the frame summary. Empty where
        /// neither model has a wall, so a job without walls reads exactly as it always did.
        ///
        /// The two models are counted separately on purpose. One Revit wall commonly carries
        /// several ETABS panels, so the pairs are counted from the ETABS side, where each panel
        /// has one verdict, and the Revit walls nothing stood along are counted on their own.
        /// </summary>
        private string WallSummary
        {
            get
            {
                if (data.Walls.Count == 0 && data.Walls_r.Count == 0)
                {
                    return "";
                }

                int matched = data.PairedWalls.Count(wall => wall.State == WallCheckState.Matched);
                int review = data.PairedWalls.Count(wall => wall.State == WallCheckState.Review);
                int failed = data.PairedWalls.Count - matched - review;
                int thicknessFailed = data.PairedWalls.Count(wall => wall.ThicknessFailed);
                int shortOfTheirWall = data.PairedWalls.Count(wall => !wall.LengthMatched);

                return $"   |   Walls: {matched} matched, {failed} mismatched " +
                       $"({thicknessFailed} on the thickness), {review} with no thickness to compare, " +
                       $"{data.UnmatchedEtabsWalls.Count} ETABS panels and " +
                       $"{data.UnmatchedRevitWalls.Count} Revit walls unpaired" +
                       (shortOfTheirWall == 0 ? "" : $", {shortOfTheirWall} not the full length of their wall") +
                       (data.UncoveredRevitWalls.Count == 0
                           ? ""
                           : $", {data.UncoveredRevitWalls.Count} stretch(es) of Revit wall with no ETABS on them");
            }
        }

        /// <summary>
        /// The slab check by area rather than by count, because a slab result is a piece of
        /// floor: ten square metres at the wrong thickness matters whether it came out as one
        /// zone or as five.
        /// </summary>
        private string SlabSummary
        {
            get
            {
                int etabsAreas = data.Slabs.Count(slab => !slab.IsOpening && !slab.IsLoadOnly);
                int etabsOpenings = data.Slabs.Count(slab => slab.IsOpening);
                int loadAreas = data.Slabs.Count(slab => slab.IsLoadOnly);

                if (!hasCompared || data.SlabZones.Count == 0)
                {
                    return $"ETABS: {etabsAreas} slab areas, {etabsOpenings} openings" +
                           (loadAreas > 0 ? $", {loadAreas} load areas" : "") + "   |   " +
                           $"Revit: {data.Slabs_r.Count} floors";
                }

                return $"Slabs: {SquareMetres(SlabZoneState.Matched)} m2 the same, " +
                       $"{SquareMetres(SlabZoneState.ThicknessMismatch)} m2 a different thickness, " +
                       $"{SquareMetres(SlabZoneState.MissingInRevit)} m2 in ETABS only, " +
                       $"{SquareMetres(SlabZoneState.MissingInEtabs)} m2 in Revit only   |   " +
                       $"Openings: {Count(SlabZoneState.OpeningInRevitOnly)} in Revit only, " +
                       $"{Count(SlabZoneState.OpeningInEtabsOnly)} in ETABS only" +
                       LoadSummary;
            }
        }

        /// <summary>
        /// What the load check made of the story, added to the slab summary. Empty when it did
        /// not run, so the line reads as it always did.
        /// </summary>
        private string LoadSummary
        {
            get
            {
                List<SlabZone> checked_ = data.SlabLoadZones.Where(zone => zone.WasLoadChecked).ToList();
                if (checked_.Count == 0)
                {
                    // The load check has a mesh of its own now, so with no level paired to a
                    // loading plan the Slab loads tab is simply empty - a blank plan with no
                    // word about why. Saying so here is the difference between a check that has
                    // nothing to report and a check that looks broken.
                    if (!checkSlabLoads)
                    {
                        return "   |   Loads: not checked. Switch the load check on under Set up, "
                               + "in What is checked.";
                    }
                    return "   |   Loads: nothing to check. No level is paired with a loading "
                           + "plan yet - pair them under Set up, in Loading plans.";
                }

                double matched = LoadSquareMetres(checked_, SlabLoadState.Matched);
                double wrong = LoadSquareMetres(checked_, SlabLoadState.Mismatch);
                double review = Math.Round(
                    checked_.Where(zone => zone.LoadLevel == ResultLevel.Review).Sum(zone => zone.Area) / 1e6, 1);

                return $"   |   Loads: {matched} m2 as the plan asks, {wrong} m2 not, " +
                       $"{review} m2 with nothing to check them against";
            }
        }

        private static double LoadSquareMetres(IEnumerable<SlabZone> zones, SlabLoadState state)
        {
            return Math.Round(zones.Where(zone => zone.LoadState == state).Sum(zone => zone.Area) / 1e6, 1);
        }

        private double SquareMetres(SlabZoneState state)
        {
            return Math.Round(data.SlabZones.Where(zone => zone.State == state).Sum(zone => zone.Area) / 1e6, 1);
        }

        private int Count(SlabZoneState state)
        {
            return data.SlabZones.Count(zone => zone.State == state);
        }

        #endregion

        #region commands

        public ICommand ImportEtabsCommand { get; }
        public ICommand ImportRevitCommand { get; }

        /// <summary>
        /// Writes the design model out as the file the "read from a saved file" import reads.
        /// The ETABS side has always been able to do this; this is the same file, saved from
        /// the side that is more often the one with the model in front of it.
        /// </summary>
        public ICommand ExportEtabsCommand { get; }
        public ICommand CompareCommand { get; }

        /// <summary>
        /// What the RUN CHECK button is wired to. Runs the comparison where everything is
        /// ready, and puts the preflight sheet up where it is not.
        /// </summary>
        public ICommand RunCheckCommand { get; }
        public ICommand SelectInRevitCommand { get; }
        public ICommand ExportBeamMarksCommand { get; }
        public ICommand ClearLabelsCommand { get; }
        public ICommand ShowAllResultsCommand { get; }
        public ICommand HideAllResultsCommand { get; }
        public ICommand StopCheckCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenAlignmentCommand { get; }
        public ICommand StoryUpCommand { get; }
        public ICommand StoryDownCommand { get; }

        /// <summary>
        /// Reads the design model, and tries once more where the CSI API itself would not load.
        ///
        /// That only happens to the Add-in Manager's copy on the development machine, with the
        /// installed add-in loaded in the same Revit session: the API is then hot loaded from
        /// the ETABS install - see <see cref="AddinAssemblies.HotLoadCsiApi"/> - and the import
        /// run again. An installed add-in has the API beside it and never comes here.
        /// </summary>
        private void ImportEtabs()
        {
            try
            {
                ImportEtabsOnce();
            }
            catch (Exception e) when (AddinAssemblies.IsCsiApiLoadFailure(e) && AddinAssemblies.HotLoadCsiApi())
            {
                try
                {
                    ImportEtabsOnce();
                }
                catch (Exception again)
                {
                    EtabsImportFailed(again);
                }
            }
            catch (Exception e)
            {
                EtabsImportFailed(e);
            }
        }

        /// <summary>
        /// Says the import failed. A CSI API that would not load gets the whole exception, since
        /// the one line of it names the assembly and nothing about why.
        /// </summary>
        private void EtabsImportFailed(Exception e)
        {
            Status = "ETABS import failed: " + e.Message;
            string shown = AddinAssemblies.IsCsiApiLoadFailure(e) ? e.ToString() : e.Message;
            MessageBox.Show(shown, "ETABS import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void ImportEtabsOnce()
        {
            if (ImportFromFile)
            {
                data.getEtabsDataFromFile();
                designInstance = null;
            }
            else
            {
                // Which of the open models, asked outright. Cancelling is a decision too:
                // nothing is read, and what is on the panel stays as it was.
                CsiInstance picked = PickModel == null ? null : PickModel();
                if (picked == null)
                {
                    Status = "No model was picked, so nothing was imported.";
                    return;
                }

                // The program comes from the model that was picked rather than from the
                // setting: a picked SAFE model is a SAFE model whatever the picker said.
                CsiProduct = picked.Product;

                etabsEngine = new EtabsEngine(picked, SafeLevelForCurrentProject);
                data.getEtabsDataFromEtabs(ref etabsEngine);
                designInstance = picked;
            }

            hasCompared = false;
            Models.Etabs.MarkRead(data.EtabsSourceName);
            Models.Refresh();
            RefreshStories();

            // Before the stories are assigned, and here as well as on the Revit import,
            // because the Revit model is commonly read first: read it, then pair the level,
            // then read the SAFE model, and the narrowing would never have run. Assigning
            // stories to a building against a single story names every element in it after
            // that one floor, which is the whole failure this guards. Revit elements already
            // narrowed to another level have nothing of this one left in them to keep, so
            // they are marked to be read again rather than narrowed down to nothing.
            if (narrowedTo != null && !RevitNarrowingIsCurrent)
            {
                Models.Revit.IsStale = true;
            }
            else
            {
                narrowRevitToSingleLevel();
            }

            // Revit elements imported earlier were assigned against the old story list.
            data.AssignRevitStories();
            RebuildElements();

            int slabAreas = data.Slabs.Count(slab => !slab.IsOpening && !slab.IsLoadOnly);
            int slabOpenings = data.Slabs.Count(slab => slab.IsOpening);
            int loadAreas = data.Slabs.Count(slab => slab.IsLoadOnly);
            Status = $"{DesignProductName} import: {data.Frames_e.Count} beams, " +
                     $"{data.Columns.Count} columns, {data.Walls.Count} wall areas, " +
                     $"{slabAreas} slab areas, {slabOpenings} openings and {loadAreas} load areas." +
                     (data.EtabsReadsNullLoadAreas ? "" : " Saved before None load areas were read - import again for the slab load check.");

            // A SAFE model read before its level has been picked is the one way this
            // import can succeed and still be useless: it lands on a story nothing in the
            // Revit model is named after, and nothing pairs. Said here rather than left to
            // be discovered in a run with no findings in it.
            if (IsSingleLevelProduct && SafeLevelForCurrentProject == null)
            {
                Status += " No Revit level is paired with this SAFE model yet, so the Revit "
                          + "model has not been narrowed to its floor. Pair its story with a "
                          + "Revit level in the alignment, then import again.";
            }

            // Areas the import could not make sense of - a ramp, one with no slab property,
            // a wall whose section says nothing about its thickness - are left out or left
            // incomplete rather than guessed at, and said so here.
            IList<string> issues = etabsEngine == null ? new List<string>() : etabsEngine.ImportIssues;
            if (issues.Count > 0)
            {
                Status += $" The import raised {issues.Count} issue(s).";
                ShowIssues?.Invoke("ETABS import issues", issues);
            }
        }

        /// <summary>
        /// Whether there is a design model in hand to write out. Counted off what is loaded
        /// rather than off whether an import was run, so a model read and then replaced by an
        /// import that found nothing does not leave the button offering to save it.
        /// </summary>
        public bool CanExportEtabs
        {
            get
            {
                return data.Frames_e.Count > 0 || data.Columns.Count > 0
                       || data.Walls.Count > 0 || data.Slabs.Count > 0;
            }
        }

        /// <summary>What the save button says, which follows the program it would be saving.</summary>
        public string ExportDesignModelText
        {
            get { return "Save the " + DesignProductName + " model to a file..."; }
        }

        /// <summary>
        /// Saves the design model as the file the file import reads back.
        ///
        /// The point of it is a check that can be run without the design model open: the file
        /// is handed on with the Revit model, or kept beside the job as what ETABS said on the
        /// day, and a later run reads it with the "read from a saved file" box ticked. Until
        /// now it could only be written from inside ETABS, which is the one place the model is
        /// certainly already open and so the one place it is least needed.
        ///
        /// What goes out is what is loaded, not what is on the plan: the whole building, every
        /// level, whatever the level rail and the filters happen to be showing.
        /// </summary>
        private void ExportEtabs()
        {
            try
            {
                EtabsDataContainer model = data.ToEtabsData();

                if (EtabsModelFile.IsEmpty(model))
                {
                    Status = "There is no " + DesignProductName
                             + " model to save. Import one first.";
                    return;
                }

                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save the " + DesignProductName + " model",
                    Filter = EtabsModelFile.FileFilter,
                    FileName = SuggestedModelFileName(),
                    DefaultExt = EtabsModelFile.DefaultExtension,
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                EtabsModelFile.Write(dialog.FileName, model);
                Status = "Saved " + EtabsModelFile.Describe(model) + " to " + dialog.FileName
                         + ". Tick \"read from a saved file\" to read it back.";
            }
            catch (Exception e)
            {
                Status = "Saving the " + DesignProductName + " model failed: " + e.Message;
                MessageBox.Show(e.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// A name to open the save dialog on: the model this was read from, so saving a file
        /// read from a file offers the name it came in under rather than a blank box.
        /// </summary>
        private string SuggestedModelFileName()
        {
            string source = data.EtabsSourceName ?? "";
            source = source.Trim();

            if (source.Length == 0)
            {
                return DesignProductName.ToLowerInvariant() + " model"
                       + EtabsModelFile.DefaultExtension;
            }

            foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
            {
                source = source.Replace(bad, ' ');
            }

            return System.IO.Path.GetFileNameWithoutExtension(source.Trim())
                   + EtabsModelFile.DefaultExtension;
        }

        /// <summary>
        /// The alignment has changed, so the Revit model has to end up somewhere else.
        ///
        /// It is moved where it stands rather than read out of Revit again. The alignment is
        /// baked into each element as it is built, which is why this used to be a re-read -
        /// minutes of Revit, to move a model a few metres - but the difference between the
        /// old alignment and the new one is itself a rigid move, and applying that to geometry
        /// already placed lands it exactly where the re-read would have.
        /// </summary>
        private void OnAlignmentApplied()
        {
            alignmentApplied = true;
            SaveUserSettings();

            if (data.Frames_r.Count == 0 && data.Columns_r.Count == 0 && data.Walls_r.Count == 0)
            {
                // Nothing has been read, so there is nothing to move and nothing out of date:
                // the next import builds every element under the new alignment anyway.
                placedUnder = null;
                return;
            }

            if (MoveRevitToAlignment())
            {
                Status += SafeStoryRenameNote();
                return;
            }

            Models.Revit.IsStale = true;
            ImportRevit();
            Status = "Alignment applied and the Revit model read again. Run the comparison again."
                     + SafeStoryRenameNote();
        }

        /// <summary>
        /// A SAFE model's one story is named after its level as it is imported, so a pairing
        /// made since leaves it under the old name until it is imported again. Said, because
        /// the loading plans and the level rail go by that name.
        /// </summary>
        private string SafeStoryRenameNote()
        {
            SafeLevelLink level = SafeLevelForCurrentProject;
            List<Story_E> stories = data.Stories;
            if (level == null || stories == null || stories.Count != 1
                || string.Equals(stories[0].Name, level.LevelName, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return $" Import the SAFE model again to name its floor {level.LevelName}.";
        }

        /// <summary>
        /// Moves the Revit model already in hand onto the alignment now in force. False when
        /// it cannot be done, and nothing has been touched, so the caller reads the model
        /// again instead.
        ///
        /// The one case it cannot: a run narrowed to a single level threw away everything off
        /// that level as it was read, so pairing the SAFE model with another level would be
        /// asking for elements back that are no longer here to be moved. Everything else - any
        /// turn, any flip, any move - is a rigid move of geometry that is already placed.
        /// </summary>
        private bool MoveRevitToAlignment()
        {
            if (placedUnder == null)
            {
                return false;
            }

            MyTransformation move = MyTransformation.Difference(DataContainer.ModelTransformation,
                                                                placedUnder);

            // The level is a fixed floor of the Revit model, so moving the model up or down
            // moves that floor with it and what was kept is still right. A different level, or
            // one paired or unpaired since, is not.
            if (!RevitNarrowingIsCurrent)
            {
                return false;
            }

            data.MoveRevitBy(move);
            placedUnder = DataContainer.ModelTransformation.Copy();

            // Which story an element belongs to follows from where it now stands.
            data.AssignRevitStories();

            Models.Revit.IsStale = false;
            Models.Refresh();
            hasCompared = false;
            RebuildElements();

            Status = "Alignment applied and the Revit model moved with it: " + move
                     + ". Run the comparison again.";
            return true;
        }

        /// <summary>
        /// Reads the open Revit document, saying where it has got to as it goes.
        ///
        /// The read runs on the thread Revit hands the command, which is the thread every
        /// window in the process draws on, so it cannot be put on a thread of its own and
        /// watched from here. It used to run straight through: on a real building that is
        /// minutes in which neither this window nor Revit repaints or answers a click, which
        /// Windows greys over and titles "not responding", and which is indistinguishable
        /// from a crash. It was not hung, but there was no way to tell, and no way to get out
        /// of it either.
        ///
        /// So the engine now says which of the four passes it is in and how far through it
        /// is, and the handler below lets the screen draw that before going back to the read.
        /// The same handler is what lets the stop button be pressed at all.
        ///
        /// The four reads land in local variables and are put on the model together at the
        /// end. A stopped read throws, and what is on the panel is then the last whole read
        /// rather than a Revit model with two of its four kinds of element replaced.
        ///
        /// <paramref name="chooseLinks"/> asks which linked models to read again, starting
        /// from the last choice. An import the user asks for does; a re-read the tool does on
        /// its own, after the alignment changes, keeps the links it was already reading.
        /// </summary>
        private void ImportRevit(bool chooseLinks = false)
        {
            if (revitReadRunning)
            {
                return;
            }

            bool alreadyBusy = IsChecking;
            revitReadRunning = true;

            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }
                else if (chooseLinks)
                {
                    revitEngine.ChooseLinksAgain();
                }

                // Not when a check is already running and reading for itself - the preflight
                // sheet can set this going - because the run owns the bar and the stop button
                // and taking them over would leave them switched off when the read finishes.
                if (!alreadyBusy)
                {
                    stopRequested = false;
                    IsImporting = true;
                    IsChecking = true;
                    CheckProgress = 0;
                }

                revitEngine.ReportProgress = (message, fraction) =>
                {
                    CheckProgress = fraction;
                    // Pump puts the line up and then lets the screen draw it, which is the
                    // whole of what keeps the window alive through the read.
                    Pump(message);
                };
                revitEngine.ShouldStop = () => stopRequested;

                IList<FrameData_r> frames = revitEngine.getFilterdFrames();
                IList<Column_r> columns = revitEngine.getFilterdColumns();
                IList<Wall_R> walls = revitEngine.getFilterdWalls();
                IList<Slab_R> slabs = revitEngine.getFilterdSlabs();
                IList<SidlCSource_R> sidlC = PrefrencesData.getPrefrencesData().RunSidlCCheck
                    ? revitEngine.getSidlCSources()
                    : new List<SidlCSource_R>();
                IList<SidlCAreaSource_R> sidlCArea = PrefrencesData.getPrefrencesData().RunSidlCAreaCheck
                    ? revitEngine.getSidlCAreaSources()
                    : new List<SidlCAreaSource_R>();

                data.Frames_r = frames;
                data.Columns_r = columns;
                data.Walls_r = walls;
                data.Slabs_r = slabs;
                data.SidlCSources = sidlC;
                data.SidlCAreaSources = sidlCArea;
                narrowedTo = null;
                narrowRevitToSingleLevel();
                data.AssignRevitStories();

                // What these elements were built under, so a later change of alignment can be
                // a move rather than another read.
                placedUnder = DataContainer.ModelTransformation.Copy();
                revitLeftOut = RevitLeaveOut.FromPreferences(PrefrencesData.getPrefrencesData());

                Models.Revit.MarkRead(revitEngine.DocumentTitle);
                Models.Refresh();
                hasCompared = false;
                RebuildElements();
                int familyCount = data.Columns_r.Select(c => c.FamilyName).Distinct().Count();
                int openingCount = data.Slabs_r.Sum(slab => slab.Openings.Count);
                Status = $"Revit import: {data.Frames_r.Count} beams and " +
                         $"{data.Columns_r.Count} columns across {familyCount} famil{(familyCount == 1 ? "y" : "ies")}, " +
                         $"{data.Walls_r.Count} walls, " +
                         $"and {data.Slabs_r.Count} floors with {openingCount} opening(s) in them.";

                // The slab panel counts the imported floors, and the floors are only known
                // now - the ETABS import raised it last, when there were none.
                OnPropertyChanged(nameof(SlabCheckScopeDescription));

                // Anything the import could not read is reported once, at the end.
                OnPropertyChanged(nameof(GeometryDumpDescription));

                IList<string> issues = revitEngine.ImportIssues;
                if (issues.Count > 0)
                {
                    Status += $" {issues.Count} element(s) reported a problem.";
                    ShowIssues?.Invoke("Revit import issues", issues);
                }
            }
            catch (ImportStoppedException)
            {
                // Asked for, so not a failure. Nothing was put on the model - see the locals
                // above - so whatever was read before this is still what the panel is showing.
                Status = Models.Revit.IsRead
                    ? "The Revit read was stopped. The model read before it still stands."
                    : "The Revit read was stopped, so no Revit model has been read.";
            }
            catch (Exception e)
            {
                Status = "Revit import failed: " + e.Message;
                MessageBox.Show(e.Message, "Revit import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (revitEngine != null)
                {
                    revitEngine.ReportProgress = null;
                    revitEngine.ShouldStop = null;
                }

                if (!alreadyBusy)
                {
                    IsImporting = false;
                    IsChecking = false;
                    CheckProgress = 0;
                }

                revitReadRunning = false;
            }
        }

        /// <summary>
        /// The run, with a look at whether it is worth doing first.
        ///
        /// Deliberately a sheet rather than a refusal. Everything it raises is something a run
        /// would still produce an answer to - it would just be the wrong answer, or an answer
        /// about a model that has moved on - and the one thing worse than a check that will not
        /// run is a check that runs and quietly reports two thousand unpaired elements.
        /// </summary>
        private void RunCheck()
        {
            PreflightReport report = BuildPreflight();

            if (report.IsClear || ShowPreflight == null)
            {
                if (CanCompare)
                {
                    Compare();
                }
                return;
            }

            if (ShowPreflight(report) && CanCompare)
            {
                Compare();
            }
        }

        /// <summary>
        /// What stands between the button and a run worth reading.
        ///
        /// Every line is something that changes what the run says, and each carries the way to
        /// deal with it. What is already done is listed too: a sheet that names only the faults
        /// reads as a longer list of trouble than it is, and knowing which of the two models is
        /// the one that has not been read is half of dealing with it.
        /// </summary>
        private PreflightReport BuildPreflight()
        {
            List<PreflightItem> items = new List<PreflightItem>();

            // ---- what has to be there at all -------------------------------------------
            if (!Models.Etabs.IsRead)
            {
                items.Add(new PreflightItem(PreflightLevel.Blocker,
                    "The ETABS model has not been read",
                    ImportFromFile
                        ? "Reads the saved file named beside the model."
                        : "Reads the running instance of ETABS.",
                    "READ IT NOW", ImportEtabsCommand, fixesItself: true));
            }

            if (!Models.Revit.IsRead)
            {
                items.Add(new PreflightItem(PreflightLevel.Blocker,
                    "The Revit model has not been read",
                    "Reads the open document. On a large model this takes a moment.",
                    "READ IT NOW", ImportRevitCommand, fixesItself: true));
            }

            if (!checkEveryStory && slabCheckStory == null)
            {
                items.Add(new PreflightItem(PreflightLevel.Blocker,
                    "No level has been picked to check",
                    "The slab check is set to one level rather than all of them, and no level is "
                    + "chosen. Pick one under Set up, or set it back to every level."));
            }

            // ---- what would answer from something out of date --------------------------
            if (Models.Revit.IsRead && Models.Revit.IsStale)
            {
                items.Add(new PreflightItem(PreflightLevel.Warning,
                    "The Revit model is worth reading again",
                    "The alignment has changed since it was read, and the alignment is baked into "
                    + "each element as it is built. Until it is read again the check runs against "
                    + "the old positions.",
                    // The same links as before: it is the alignment that is out of date.
                    "READ IT AGAIN", new RelayCommand(_ => ImportRevit(), _ => !revitReadRunning),
                    fixesItself: true));
            }

            if (Models.Revit.IsRead && revitLeftOut != null)
            {
                IList<string> comeBack = revitLeftOut.NoLongerLeftOutBy(
                    RevitLeaveOut.FromPreferences(PrefrencesData.getPrefrencesData()));
                if (comeBack.Count > 0)
                {
                    items.Add(new PreflightItem(PreflightLevel.Warning,
                        "A name has come off a leave-out list since the Revit model was read",
                        "Whatever " + string.Join(", ", comeBack) + " left out was never read, so "
                        + "a check cannot put it back. Until the model is read again those elements "
                        + "stay out of the check. A name added to a list needs no read: the check "
                        + "applies it.",
                        "READ IT AGAIN", new RelayCommand(_ => ImportRevit(), _ => !revitReadRunning),
                        fixesItself: true));
                }
            }

            if (Models.Etabs.IsRead && Models.Revit.IsRead && !HasBeenAligned)
            {
                items.Add(new PreflightItem(PreflightLevel.Warning,
                    "Nothing has been aligned in this project",
                    "Two picked points is enough. If the two models already share coordinates "
                    + "there is nothing to do; if they do not, every element reads as unpaired "
                    + "and the run says nothing else.",
                    "ALIGN NOW", OpenAlignmentCommand));
            }

            if (checkSlabLoads && loadingPlanLinks.CountFor(ProjectKey) == 0)
            {
                items.Add(new PreflightItem(PreflightLevel.Warning,
                    "No level is paired with a loading plan",
                    "The load check has nothing to read, so it is skipped. Thickness, outlines and "
                    + "openings are checked as usual.",
                    "PAIR THEM", OpenLoadingPlansCommand));
            }

            // ---- what is already dealt with --------------------------------------------
            if (Models.Etabs.IsRead)
            {
                int slabAreas = data.Slabs.Count(slab => !slab.IsOpening && !slab.IsLoadOnly);
                items.Add(new PreflightItem(PreflightLevel.Done,
                    "ETABS model read - " + Models.Etabs.Name,
                    data.Stories.Count + " levels, " + data.Frames_e.Count + " beams, "
                    + data.Columns.Count + " columns, " + data.Walls.Count + " wall areas, "
                    + slabAreas + " slab areas."));
            }

            if (Models.Revit.IsRead && !Models.Revit.IsStale)
            {
                items.Add(new PreflightItem(PreflightLevel.Done,
                    "Revit model read - " + Models.Revit.Name,
                    data.Frames_r.Count + " beams, " + data.Columns_r.Count + " columns, "
                    + data.Walls_r.Count + " walls, " + data.Slabs_r.Count + " floors."));
            }

            if (HasBeenAligned)
            {
                items.Add(new PreflightItem(PreflightLevel.Done,
                    "Revit and ETABS are aligned",
                    DataContainer.ModelTransformation.ToString()));
            }

            PreflightReport report = new PreflightReport(items, CanCompare);
            report.RunAnywayNote = !HasBeenAligned
                ? "Run anyway and, unless the two models already share coordinates, everything "
                  + "will read as unpaired."
                : "Run anyway and everything above is answered from what is already there.";
            return report;
        }

        /// <summary>
        /// Whether anybody has ever aligned this project.
        ///
        /// A transform that is not the identity is proof on its own - there is no way to get one
        /// except by aligning. The identity is not proof of the opposite, because two models
        /// that already share coordinates are aligned and need no transform, so the flag written
        /// when an alignment is applied is what tells that apart from a project nobody has
        /// touched. A project aligned before the flag existed reads as not aligned once, and the
        /// line it raises is a warning rather than a refusal for exactly that reason.
        /// </summary>
        private bool HasBeenAligned
        {
            get
            {
                MyTransformation transformation = DataContainer.ModelTransformation;
                if (transformation.Translation.X != 0 || transformation.Translation.Y != 0
                    || transformation.Translation.Z != 0 || transformation.Rotation != 0
                    || transformation.IsMirroredAboutX || transformation.IsMirroredAboutY)
                {
                    return true;
                }

                object raw;
                Dictionary<string, object> saved;
                if (ProjectKey.Length == 0 || !alignmentsByProject.TryGetValue(ProjectKey, out raw)
                    || (saved = raw as Dictionary<string, object>) == null)
                {
                    return false;
                }
                return Json.GetBool(saved, "applied", false);
            }
        }

        private void Compare()
        {
            IsChecking = true;
            stopRequested = false;
            CheckProgress = 0;

            // A whole run reads every pairing as it stands, so nothing is left waiting on one.
            pendingLoadStories.Clear();
            pendingLineLoads = false;

            try
            {
                string leftOutNote = ApplyRevitLeaveOut();

                Pump("Checking beams...");
                CompareBeams();

                Pump("Checking columns...");
                CompareColumns();

                Pump("Checking walls...");
                CompareWalls();
                CrossPairColumnsAndWalls();

                CompareSlabs();

                Pump("Checking line loads...");
                CompareLineLoads();

                Pump("Checking SIDL_C loads...");
                CompareSidlC();

                Pump("Checking SIDL_C area loads...");
                CompareSidlCArea();

                IList<string> droppedPairings = SettleHandPairings();

                hasCompared = true;
                Models.MarkRun();
                RebuildElements();

                // The drawer is filled by the rebuild above, so this reads what the run actually
                // reported rather than what the engines produced before the switches were applied.
                RecordThisRun();
                Status = (slabCheckStopped
                             ? "Stopped part way through the slab check. " + Summary
                             : Summary)
                         + DroppedPairingsNote(droppedPairings)
                         + leftOutNote
                         + etabsBeamsLeftOutNote;
            }
            catch (Exception e)
            {
                Status = "Comparison failed: " + e.Message;
                MessageBox.Show(e.Message, "Comparison failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsChecking = false;
                CheckProgress = 0;
            }
        }

        /// <summary>
        /// Takes out of the Revit model in hand whatever the leave-out lists now name and the
        /// import did not, so a name added to a list counts from the next check rather than
        /// the next import. Says what it took out, or nothing when it took out nothing.
        ///
        /// Only ever takes out. What a name taken off a list would bring back was never read,
        /// so that waits for an import, and the sheet before the run says so.
        /// </summary>
        private string ApplyRevitLeaveOut()
        {
            if (revitLeftOut == null)
            {
                return "";
            }

            RevitLeaveOut now = RevitLeaveOut.FromPreferences(PrefrencesData.getPrefrencesData());

            int walls, slabs, beams;
            List<Wall_R> keptWalls = RevitLeaveOut.Keep(data.Walls_r, now.LeavesOut, out walls);
            List<Slab_R> keptSlabs = RevitLeaveOut.Keep(data.Slabs_r, now.LeavesOut, out slabs);
            List<FrameData_r> keptBeams = RevitLeaveOut.Keep(data.Frames_r, now.LeavesOut, out beams);

            revitLeftOut = revitLeftOut.With(now);

            if (walls + slabs + beams == 0)
            {
                return "";
            }

            if (walls > 0) data.Walls_r = keptWalls;
            if (slabs > 0) data.Slabs_r = keptSlabs;
            if (beams > 0) data.Frames_r = keptBeams;

            // The slab panel counts the floors, and there are fewer of them now.
            OnPropertyChanged(nameof(SlabCheckScopeDescription));

            List<string> parts = new List<string>();
            if (beams > 0) parts.Add(beams + " beam(s)");
            if (walls > 0) parts.Add(walls + " wall(s)");
            if (slabs > 0) parts.Add(slabs + " floor(s)");
            return " Left out by the names added to the leave-out lists since the import: "
                   + string.Join(", ", parts) + ".";
        }

        /// <summary>
        /// Puts a line up and lets the screen draw it.
        ///
        /// The check has to run on the thread Revit hands the command, so it cannot be moved
        /// off to one side and watched from here. Letting the queued work run instead is what
        /// keeps the window drawing and, more to the point, is what lets the stop button be
        /// pressed at all while the check is going.
        /// </summary>
        private void Pump(string message = null)
        {
            if (message != null)
            {
                Status = message;
            }

            Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
        }

        /// <summary>
        /// Cuts the ETABS mesh where the two models stop agreeing about the slab. Skipped when
        /// neither model has a slab in it, so a job that only ever checked beams and columns
        /// costs nothing for a check it is not using.
        /// </summary>
        private void CompareSlabs()
        {
            slabCheckStopped = false;

            if (data.Slabs.Count == 0 && data.Slabs_r.Count == 0)
            {
                data.SlabZones = new List<SlabZone>();
                data.SlabLoadZones = new List<SlabZone>();
                loadingPlansRead.Clear();
                return;
            }

            // A file of its own for the comparison. The import writes one and closes it, so
            // without this the faults raised while comparing - which are most of them, and the
            // ones worth reading - were recorded nowhere.
            if (PrefrencesData.getPrefrencesData().WriteGeometryDump)
            {
                GeometryDump.Start(GeometryDump.DefaultFolder(), "Slab comparison");
            }

            List<string> issues = new List<string>();
            Dictionary<string, LoadCoverage> loadCoverages = BuildLoadCoverages(issues);
            KeepLoadingPlans(loadCoverages, null);
            SlabCompareEngine slabEngine = NewSlabEngine(loadCoverages);

            SaveSlabCheckInputs(issues);

            slabEngine.StartComparison(data.Slabs, data.Slabs_r, data.Stories);
            data.SlabZones = slabEngine.Zones;
            data.SlabLoadZones = slabEngine.LoadZones;
            slabCheckStopped = slabEngine.Stopped;

            issues.AddRange(slabEngine.Issues);
            DumpSlabLoads(slabEngine.LoadZones, issues);

            if (GeometryDump.IsOpen)
            {
                int faults = GeometryDump.Entries;
                string where = GeometryDump.FilePath;
                GeometryDump.Stop();
                OnPropertyChanged(nameof(GeometryDumpDescription));

                issues.Add(faults == 0
                    ? "The geometry dump recorded nothing: every region was compared without complaint."
                    : $"The geometry dump recorded {faults} fault(s) in {where}");
            }

            if (issues.Count > 0)
            {
                ShowIssues?.Invoke("Slab check issues", issues);
            }
        }

        /// <summary>
        /// Writes what the slab check is about to read for the level on show, where the
        /// diagnostics ask for it - see <see cref="SlabCheckInputs"/>. Failing to is said and
        /// never stops the check.
        /// </summary>
        private void SaveSlabCheckInputs(IList<string> issues)
        {
            if (!PrefrencesData.getPrefrencesData().SaveSlabCheckInputs)
            {
                return;
            }

            string story = (SlabCheckStory ?? SelectedStory)?.Name;
            if (string.IsNullOrEmpty(story))
            {
                issues.Add("The slab check's inputs were not saved: no level is on show.");
                return;
            }

            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                             "DiscrepancyScanner");
                System.IO.Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, SlabCheckInputs.FileNameFor(story));

                SlabCheckInputs inputs = SlabCheckInputs.Collect(story, data.Stories, data.Slabs, data.Slabs_r,
                    SlabSupports.From(data.Columns, data.Columns_r, data.Walls, data.Walls_r));
                inputs.Write(path);

                issues.Add($"The slab check's inputs for {story} were saved to {path} - "
                           + $"{inputs.EtabsSlabs.Count} ETABS area(s), {inputs.RevitFloors.Count} Revit floor(s), "
                           + $"{inputs.Supports.Count} column(s) and wall(s).");
            }
            catch (Exception e)
            {
                issues.Add("The slab check's inputs could not be saved: " + e.Message);
            }
        }

        /// <summary>
        /// The slab engine as this panel sets it up: the level it is narrowed to, the progress
        /// and the stop button wired to the window, and the loading plans it is to check.
        /// </summary>
        private SlabCompareEngine NewSlabEngine(Dictionary<string, LoadCoverage> loadCoverages)
        {
            SlabCompareEngine slabEngine = new SlabCompareEngine
            {
                ReportProgress = (message, fraction) =>
                {
                    Status = message;
                    CheckProgress = fraction;
                },
                OnlyStory = CheckEveryStory ? null : SlabCheckStory?.Name,
                ShouldStop = () =>
                {
                    // Pumping here rather than in the progress callback is deliberate: this
                    // is the one the engine calls on a timer it controls, so the screen is
                    // let through at a steady rate however uneven the work happens to be.
                    Pump();
                    return stopRequested;
                }
            };

            foreach (KeyValuePair<string, LoadCoverage> level in loadCoverages)
            {
                slabEngine.SetLoads(level.Key, level.Value);
            }

            slabEngine.SetColumnsAndWalls(SlabSupports.From(data.Columns, data.Columns_r, data.Walls, data.Walls_r));

            return slabEngine;
        }

        #region checking the loads again on their own

        /// <summary>
        /// The levels whose loading plan pairing has changed since the loads on the plan were
        /// worked out, and whether any line load pairing has. Filled as the pairing window
        /// closes and emptied by whatever checks the loads next - a re-check of these, or a
        /// whole run.
        /// </summary>
        private readonly HashSet<string> pendingLoadStories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool pendingLineLoads;

        /// <summary>
        /// Checks the loads again, and nothing else: the area loads on the slab and the line
        /// loads on the frames. Every pairing is read afresh - the view, what is drawn on it,
        /// the schedule sheet - so it serves a loading plan edited in Revit, a schedule edited
        /// in Excel and a level paired with a different view alike.
        /// </summary>
        public ICommand RecheckLoadsCommand { get; }

        /// <summary>
        /// Whether the panel offers to check the loads on their own: only once there is a
        /// loading plan paired and a load check switched on to read it.
        /// </summary>
        public bool ShowsLoadRecheck =>
            loadingPlanLinks.CountFor(ProjectKey) > 0
            && (checkSlabLoads || PrefrencesData.getPrefrencesData().RunLineLoadCheck);

        /// <summary>How a level is matched between a pairing and a zone.</summary>
        private static string StoryKey(string story)
        {
            return (story ?? "").Trim();
        }

        /// <summary>
        /// Notes which levels the pairing window changed, against what they were paired with
        /// before it opened.
        ///
        /// Only the parts a check reads count: the view and the workbook for both, the area
        /// load sheet for the slab loads and the line load sheet for the line loads. A level
        /// given a line load sheet has not had its area loads changed, and is not cut again
        /// for them.
        /// </summary>
        private void NoteChangedPairings(IEnumerable<LoadingPlanLink> before)
        {
            Dictionary<string, LoadingPlanLink> was = before.ToDictionary(
                link => StoryKey(link.Story), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, LoadingPlanLink> now = loadingPlanLinks.ForProject(ProjectKey).ToDictionary(
                link => StoryKey(link.Story), StringComparer.OrdinalIgnoreCase);

            foreach (string story in was.Keys.Union(now.Keys, StringComparer.OrdinalIgnoreCase))
            {
                LoadingPlanLink a, b;
                was.TryGetValue(story, out a);
                now.TryGetValue(story, out b);

                if (!SamePairing(a, b, link => link.SheetName))
                {
                    pendingLoadStories.Add(story);
                }
                if (!SamePairing(a, b, link => link.LineLoadSheetName))
                {
                    pendingLineLoads = true;
                }
            }
        }

        private static bool SamePairing(LoadingPlanLink a, LoadingPlanLink b, Func<LoadingPlanLink, string> sheet)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            return a.ViewId == b.ViewId
                   && string.Equals(a.WorkbookPath ?? "", b.WorkbookPath ?? "", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(sheet(a) ?? "", sheet(b) ?? "", StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks the loads again on the levels whose pairing changed, if there are results on
        /// the plan for them to be out of date against. Before the first run there are none,
        /// and the run reads every pairing anyway.
        /// </summary>
        private void RecheckPendingLoads()
        {
            if (pendingLoadStories.Count == 0 && !pendingLineLoads)
            {
                return;
            }

            if (!hasCompared || IsChecking)
            {
                if (!hasCompared)
                {
                    pendingLoadStories.Clear();
                    pendingLineLoads = false;
                }
                return;
            }

            RecheckLoads(new HashSet<string>(pendingLoadStories, StringComparer.OrdinalIgnoreCase),
                         pendingLineLoads);
        }

        /// <summary>
        /// Runs the load checks again and puts the results back on the plan, leaving the beams,
        /// columns, walls and the slab thickness, outline and opening zones exactly as the last
        /// run left them - none of those reads a loading plan, so none of them can have moved.
        ///
        /// <paramref name="onlyStories"/> is the levels to cut again for the area loads, null
        /// for every paired one; the zones of every other level are kept. The line loads are
        /// read across every level together, so they are either checked again whole or not.
        ///
        /// Not a run, the same as a pairing corrected by hand is not one: the run history is
        /// not re-recorded and the models are not marked as freshly checked.
        /// </summary>
        private void RecheckLoads(HashSet<string> onlyStories, bool lineLoads)
        {
            pendingLoadStories.Clear();
            pendingLineLoads = false;

            if (!hasCompared || IsChecking)
            {
                return;
            }

            IsChecking = true;
            stopRequested = false;
            CheckProgress = 0;

            List<string> issues = new List<string>();

            try
            {
                if (onlyStories == null || onlyStories.Count > 0)
                {
                    RecheckSlabLoads(onlyStories, issues);
                }

                if (lineLoads)
                {
                    Pump("Checking line loads...");
                    issues.AddRange(CompareLineLoads(reportIssues: false));
                    issues.AddRange(SettleHandPairings(reportThem: false));
                }

                RebuildElements();

                string which = onlyStories == null
                    ? "Loads checked again"
                    : onlyStories.Count == 0
                        ? "Line loads checked again"
                        : "Loads checked again on " + string.Join(", ", onlyStories.OrderBy(
                              story => story, StringComparer.OrdinalIgnoreCase));
                Status = which + (stopRequested ? ", stopped part way through" : "")
                         + ". Everything else is as the last run left it. " + Summary;
            }
            catch (Exception e)
            {
                Status = "Checking the loads again failed: " + e.Message;
                MessageBox.Show(e.Message, "Load check failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsChecking = false;
                CheckProgress = 0;
            }

            if (issues.Count > 0)
            {
                ShowIssues?.Invoke("Load check issues", issues);
            }
        }

        /// <summary>
        /// The load pass of the slab check on its own, over the levels asked for, with the load
        /// zones of every other level kept as they were.
        /// </summary>
        private void RecheckSlabLoads(HashSet<string> onlyStories, List<string> issues)
        {
            Dictionary<string, LoadCoverage> loadCoverages = BuildLoadCoverages(issues, onlyStories);
            KeepLoadingPlans(loadCoverages, onlyStories);

            // Every level asked about loses what it had, whether or not it gets anything back:
            // a level just unpaired has no loads to check, and its old verdicts were read off a
            // plan it is no longer paired with.
            List<SlabZone> kept = onlyStories == null
                ? new List<SlabZone>()
                : data.SlabLoadZones.Where(zone => !onlyStories.Contains(StoryKey(zone.Story))).ToList();

            if (loadCoverages.Count > 0)
            {
                SlabCompareEngine slabEngine = NewSlabEngine(loadCoverages);
                slabEngine.LoadsOnly = true;
                slabEngine.StartComparison(data.Slabs, data.Slabs_r, data.Stories);

                kept.AddRange(slabEngine.LoadZones);
                issues.AddRange(slabEngine.Issues);
                DumpSlabLoads(slabEngine.LoadZones, issues);
            }

            data.SlabLoadZones = kept;
        }

        #endregion

        /// <summary>
        /// Pairs the ETABS columns with the Revit columns and checks each pair. A method of its
        /// own so that a hand-set pairing can run this one pass again without the rest of the
        /// check coming with it.
        /// </summary>
        #region line loads

        /// <summary>
        /// Compares the line loads drawn on each paired loading plan against the distributed
        /// loads on the ETABS frames under them.
        ///
        /// Read at the moment the check runs, like the area loads and for the same reason: the
        /// pairing is the only record of what was decided, so reading it now is what makes it
        /// impossible to check against a view or a sheet the panel merely used to be showing.
        /// </summary>
        /// <param name="readAgain">
        /// False when a pairing set by hand is being put into effect. That changes which line
        /// goes with which frame and nothing about the drawings or the schedules, so the lines
        /// and sheets read by the last run are used again - as long as the levels are still
        /// paired with the same views and sheets. Reading them again read every paired loading
        /// plan out of Revit, opened every schedule workbook and wrote every dump, for each
        /// pairing.
        /// </param>
        private List<string> CompareLineLoads(bool reportIssues = true, bool readAgain = true)
        {
            data.LineLoadResults = new List<LineLoadResult>();
            data.UnclaimedLineLoads = new List<EtabsLineLoad>();
            data.EtabsLineLoads = new List<EtabsLineLoad>();

            if (!PrefrencesData.getPrefrencesData().RunLineLoadCheck)
            {
                return new List<string>();
            }

            List<string> issues = new List<string>();

            ScheduleColumnOverrides.Current.ActiveProject = ProjectKey;

            List<LoadingPlanLink> links = loadingPlanLinks.UsableForLineLoads(
                ProjectKey, CheckEveryStory, SlabCheckStory?.Name);

            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }
            }
            catch (Exception e)
            {
                issues.Add("The loading plans could not be read, so the line loads were not "
                           + "checked - " + e.Message);
                if (reportIssues) ShowIssues?.Invoke("Line load check issues", issues);
                return issues;
            }

            if (links.Count == 0)
            {
                issues.Add("The line loads were not checked: no level here has both a loading plan "
                           + "view and a line load sheet paired with it. Pair them under Loading plans.");

                // Still written down, off whatever views are paired for the area loads. A level
                // with a view and no line load sheet is the likeliest state this check will ever
                // be found in - the sheet is picked in a dropdown that did not exist until now -
                // and it is no reason to withhold the one file that says what is on the drawing.
                if (readAgain)
                {
                    ReadAndDumpEveryView(ViewsWorthDumping(), issues);
                }

                if (reportIssues) ShowIssues?.Invoke("Line load check issues", issues);
                return issues;
            }

            // Written down before anything about the ETABS side is asked, and that ordering is
            // the whole point. What is on the Revit view has nothing to do with whether the
            // ETABS model came back with its loads, so a diagnostic about the one must not be
            // stopped by the other - which is exactly what used to happen, and left the person
            // who most needed the file with no file at all.
            string readingKey = LineLoadReadingKey(links);
            bool reuse = !readAgain && lineLoadsReadFor == readingKey && lineLoadsByView != null;

            Dictionary<long, IList<LineLoad_R>> byView = reuse
                ? lineLoadsByView
                : ReadAndDumpEveryView(links, issues);

            if (data.LineLoadFrames.Count == 0)
            {
                issues.Add("The ETABS model was read without its distributed loads, so there is "
                           + "nothing to check the drawn line loads against. Import the ETABS model "
                           + "again - the lines read off the Revit views are written down either "
                           + "way, above.");
                if (reportIssues) ShowIssues?.Invoke("Line load check issues", issues);
                return issues;
            }

            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                Overrides = pairingOverrides,
                HeightZones = heightZones,
                ProjectKey = ProjectKey
            };
            engine.SetStoryHeights(data.Stories);
            engine.Patterns = LineLoadPatterns.From(PrefrencesData.getPrefrencesData());

            // Every paired level, whether or not anything was drawn on it. A level whose loading
            // plan has no line loads on it still has ETABS frames carrying line loads, and those
            // are the ones nobody asked for - see LineLoadCompareEngine.SetStoriesToCheck.
            engine.SetStoriesToCheck(links.Select(link => link.Story));

            List<LineLoad_R> drawn = new List<LineLoad_R>();

            // One schedule per sheet rather than per level: levels share sheets, and without
            // this the same workbook is opened and parsed once per floor and the same
            // complaints about it reported that many times over.
            Dictionary<string, LineLoadSchedule> schedules = reuse
                ? lineLoadSchedules
                : new Dictionary<string, LineLoadSchedule>(StringComparer.OrdinalIgnoreCase);

            lineLoadsByView = byView;
            lineLoadSchedules = schedules;
            lineLoadsReadFor = readingKey;

            HashSet<long> sharedViews = loadingPlanLinks.ViewsSharedForLineLoads(ProjectKey);
            Dictionary<string, LineLoadSchedule> scheduleOfStory =
                new Dictionary<string, LineLoadSchedule>(StringComparer.OrdinalIgnoreCase);

            foreach (LoadingPlanLink link in links)
            {
                LineLoadSchedule schedule = ReadLineLoadSheet(link, schedules, issues);
                if (schedule == null)
                {
                    continue;
                }

                IList<LineLoad_R> lines;
                if (!byView.TryGetValue(link.ViewId, out lines))
                {
                    continue;
                }

                engine.SetSchedule(link.Story, schedule);
                scheduleOfStory[link.Story] = schedule;

                // Put on this level whichever level the view was read for. A view is read once
                // however many levels share it, so the lines come back named after the first of
                // them - and handed on as they were, that level was checked once per level
                // sharing the view and every other level had nothing drawn on it at all.
                bool shared = sharedViews.Contains(link.ViewId);
                drawn.AddRange(lines.Select(line => line.OnStory(link.Story, shared)));
            }

            if (drawn.Count == 0)
            {
                // Said, and then the check is run anyway. The drawing having nothing on it does
                // not mean ETABS has nothing on it, and what the frames carry with no line drawn
                // over it is the plainest finding this check makes: a load somebody put in the
                // model that the loading plan never asked for. Stopping here used to throw that
                // away and report the quieter half of the answer as the whole of it.
                issues.Add("No line load was found on any of the paired loading plans, so every "
                           + "line load on the ETABS frames is reported as one the plan does not "
                           + "ask for. The lines are read off their line style name - see the line "
                           + "load style patterns under Settings.");
            }

            SaveLineLoadCheckInputs(drawn, scheduleOfStory, issues);

            engine.StartComparison(drawn, data.LineLoadFrames);

            data.LineLoadResults = engine.Results;
            data.UnclaimedLineLoads = engine.Unclaimed;
            data.EtabsLineLoads = engine.EtabsStretches;

            issues.AddRange(engine.Issues);
            issues.Add($"{engine.Results.Count} drawn line load(s) checked against "
                       + $"{engine.Runs.Count} ETABS frame(s), each read on its own.");

            if (issues.Count > 0)
            {
                if (reportIssues) ShowIssues?.Invoke("Line load check issues", issues);
            }

            return issues;
        }

        /// <summary>
        /// Writes what the line load check is about to read for the level on show, where the
        /// diagnostics ask for it - see <see cref="LineLoadCheckInputs"/>. Failing to is said and
        /// never stops the check.
        /// </summary>
        private void SaveLineLoadCheckInputs(List<LineLoad_R> drawn, Dictionary<string, LineLoadSchedule> scheduleOfStory,
                                             IList<string> issues)
        {
            if (!PrefrencesData.getPrefrencesData().SaveLineLoadCheckInputs)
            {
                return;
            }

            string story = (SlabCheckStory ?? SelectedStory)?.Name;
            if (string.IsNullOrEmpty(story))
            {
                issues.Add("The line load check's inputs were not saved: no level is on show.");
                return;
            }

            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                             "DiscrepancyScanner");
                System.IO.Directory.CreateDirectory(folder);
                string path = System.IO.Path.Combine(folder, LineLoadCheckInputs.FileNameFor(story));

                LineLoadSchedule schedule;
                scheduleOfStory.TryGetValue(story, out schedule);

                LineLoadCheckInputs inputs = LineLoadCheckInputs.Collect(story, ProjectKey, data.Stories, drawn,
                    data.LineLoadFrames, schedule, pairingOverrides, heightZones);
                inputs.Write(path);

                issues.Add($"The line load check's inputs for {story} were saved to {path} - "
                           + $"{inputs.RevitLines.Count} drawn line(s), {inputs.EtabsFrames.Count} ETABS frame(s)"
                           + (schedule == null ? ", and no schedule sheet for the level." : "."));
            }
            catch (Exception e)
            {
                issues.Add("The line load check's inputs could not be saved: " + e.Message);
            }
        }

        /// <summary>
        /// Which views and sheets a set of pairings reads, as one string: what the last run read
        /// is only used again for the same pairings.
        /// </summary>
        private static string LineLoadReadingKey(IEnumerable<LoadingPlanLink> links)
        {
            return string.Join(";", links.Select(link => link.Story + "|" + link.ViewId + "|"
                                                      + link.WorkbookPath + "|" + link.LineLoadSheetName));
        }

        // What the last line load pass read off the Revit views and out of the schedules, and
        // for which pairings - see the readAgain parameter of CompareLineLoads.
        private Dictionary<long, IList<LineLoad_R>> lineLoadsByView;
        private Dictionary<string, LineLoadSchedule> lineLoadSchedules;
        private string lineLoadsReadFor;

        /// <summary>
        /// The line load sheet for one level, read once per sheet. Null when that level cannot
        /// be checked, with the reason added to the run's issues rather than raised as a dialog
        /// - one level being wrong is not a reason to stop checking the others.
        /// </summary>
        private LineLoadSchedule ReadLineLoadSheet(LoadingPlanLink link,
                                                   IDictionary<string, LineLoadSchedule> schedules,
                                                   IList<string> issues)
        {
            try
            {
                string key = link.WorkbookPath + "|" + link.LineLoadSheetName;

                LineLoadSchedule schedule;
                if (!schedules.TryGetValue(key, out schedule))
                {
                    schedule = LineLoadScheduleReader.Read(link.WorkbookPath, link.LineLoadSheetName);
                    schedules[key] = schedule;

                    foreach (string issue in schedule.Issues)
                    {
                        issues.Add($"{link.LineLoadSheetName}: {issue}");
                    }
                }

                if (schedule.Count == 0)
                {
                    issues.Add($"{link.Story}: {link.LineLoadSheetName} in "
                               + $"{System.IO.Path.GetFileName(link.WorkbookPath)} has no numbered line "
                               + "loads on it, so there is nothing to check its lines against.");
                    return null;
                }

                return schedule;
            }
            catch (Exception e)
            {
                issues.Add($"{link.Story}: the line load sheet could not be read, so its line loads "
                           + "were not checked - " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// The detail lines off one level's loading plan, with anything the read complained
        /// about carried across to this check's own list.
        /// </summary>
        private IList<LineLoad_R> ReadDrawnLineLoads(LoadingPlanLink link, IList<string> issues)
        {
            try
            {
                int alreadyReported = revitEngine.ImportIssues.Count;

                IList<LineLoad_R> lines = revitEngine.getLineLoads(link.ViewId, link.Story);

                for (int i = alreadyReported; i < revitEngine.ImportIssues.Count; i++)
                {
                    issues.Add($"{link.Story}: {revitEngine.ImportIssues[i]}");
                }

                if (lines.Count == 0)
                {
                    issues.Add($"{link.Story}: no line load is drawn on \"{link.ViewName}\".");
                }

                return lines;
            }
            catch (Exception e)
            {
                issues.Add($"{link.Story}: the line loads could not be read - " + e.Message);
                return new List<LineLoad_R>();
            }
        }

        /// <summary>
        /// The views worth writing down when no level is set up for the line load check yet:
        /// every loading plan this project has paired, whatever it was paired for.
        ///
        /// The area load check only needs a view and a sheet, so a project part way through
        /// being set up has views paired and no line load sheet against any of them. Those are
        /// the views the drawn lines are on, and they are what somebody trying to find out why
        /// the check saw nothing needs to look at.
        /// </summary>
        private List<LoadingPlanLink> ViewsWorthDumping()
        {
            return loadingPlanLinks
                .ForProject(ProjectKey)
                .Where(link => link != null && link.ViewId != 0)
                .Where(link => CheckEveryStory
                               || string.Equals(link.Story, SlabCheckStory?.Name,
                                                StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        /// <summary>
        /// Reads the lines off every paired view and writes each one down, before anything else
        /// is asked of either model.
        ///
        /// One read per view rather than per level, because several levels commonly share a
        /// loading plan and reading the same view five times would write five copies of the
        /// same file.
        /// </summary>
        private Dictionary<long, IList<LineLoad_R>> ReadAndDumpEveryView(List<LoadingPlanLink> links,
                                                                        IList<string> issues)
        {
            Dictionary<long, IList<LineLoad_R>> byView = new Dictionary<long, IList<LineLoad_R>>();

            foreach (LoadingPlanLink link in links)
            {
                if (byView.ContainsKey(link.ViewId))
                {
                    continue;
                }

                Status = $"Reading the line loads for {link.Story}...";
                Pump();

                IList<LineLoad_R> lines = ReadDrawnLineLoads(link, issues);
                byView[link.ViewId] = lines;

                DumpLinesOn(link, lines.Count, issues);
            }

            return byView;
        }

        /// <summary>
        /// Writes down every line on one loading plan, whatever style it is drawn in, and says
        /// where it went.
        ///
        /// Always, not only when nothing was read. A view that gave up three lines when the
        /// drawing has thirty on it is the same question as one that gave up none, and it is
        /// the harder of the two to notice.
        /// </summary>
        private void DumpLinesOn(LoadingPlanLink link, int read, IList<string> issues)
        {
            if (!PrefrencesData.getPrefrencesData().WriteLineLoadDump)
            {
                return;
            }

            try
            {
                string path = System.IO.Path.Combine(
                    GeometryDump.DefaultFolder(),
                    "lines-" + Slug(link.Story) + "-"
                    + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");

                string written = revitEngine.dumpLinesInView(link.ViewId, path);

                issues.Add($"{link.Story}: {read} line load(s) read off \"{link.ViewName}\". "
                           + $"Every line on that view, whatever style it is drawn in, is written "
                           + $"down in {written}");
            }
            catch (Exception e)
            {
                issues.Add($"{link.Story}: the lines on the view could not be written down - "
                           + e.Message);
            }
        }

        /// <summary>A story name as a file name, for the dump beside the other diagnostics.</summary>
        private static string Slug(string name)
        {
            char[] kept = (name ?? "")
                .Select(letter => char.IsLetterOrDigit(letter) ? letter : '-')
                .ToArray();

            string slug = new string(kept).Trim('-');
            return slug.Length == 0 ? "level" : slug;
        }

        #endregion

        #region SIDL_C loads

        /// <summary>
        /// Compares the SIDL_C line loads on the ETABS frames against the weight of the Revit
        /// walls and fill strips the wall and slab checks leave out.
        ///
        /// The line load check over again, frame by frame and stretch by stretch, with two
        /// differences: the Revit side's value is what the wall weighs rather than what a
        /// schedule says, and ETABS may carry more than that by a good margin but less by very
        /// little - see <see cref="LoadBand"/>. No loading plan is needed, so every level the
        /// check covers is read, and a SIDL_C load on a frame with no wall over it is reported
        /// however the levels are paired.
        ///
        /// Quiet unless something went wrong: the line load check already puts a list up after
        /// every run, and a second one saying only how many walls were weighed is noise.
        /// </summary>
        private List<string> CompareSidlC()
        {
            data.SidlCResults = new List<LineLoadResult>();
            data.UnclaimedSidlC = new List<EtabsLineLoad>();
            data.EtabsSidlC = new List<EtabsLineLoad>();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            List<string> issues = new List<string>();

            if (!pref.RunSidlCCheck || data.LineLoadFrames.Count == 0)
            {
                return issues;
            }

            List<string> stories = CheckEveryStory
                ? data.Stories.Select(story => story.Name).ToList()
                : new List<string> { SlabCheckStory?.Name ?? "" };
            stories = stories.Where(name => !string.IsNullOrEmpty(name)).ToList();
            HashSet<string> inScope = new HashSet<string>(stories, StringComparer.OrdinalIgnoreCase);

            List<SidlCSource_R> onStories = data.SidlCSources
                .Where(source => source.Length > 0 && inScope.Contains(source.Story))
                .ToList();
            List<double> intervals = SidlCSource_R.ParseIntervals(pref.SidlCLoadIntervals);
            List<LineLoad_R> weighed = onStories
                .Where(source => source.Weighs(pref.SidlCConcreteDensity, pref.SidlCLoadFromComment) >= pref.SidlCMinimumLoad)
                .Select(source => source.ToLineLoad(pref.SidlCConcreteDensity, pref.LineLoadRoundUpTo,
                                                    intervals, pref.SidlCLoadFromComment))
                .ToList();
            int tooLight = onStories.Count - weighed.Count;

            // No pairings by hand and no height zones: a wall is laid on the frames by where it
            // stands, and weighs what it weighs whatever the storey above it is.
            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                ProjectKey = ProjectKey,
                Patterns = LineLoadPatterns.SidlC(pref),
                PatternsSetting = "\"ETABS / SAFE load patterns to read\" under Settings, SIDL_C Line",
                Band = new LoadBand(pref.SidlCToleranceBelow, pref.SidlCToleranceAbove),
                LocationTolerance = pref.SidlCLocationTolerance
            };
            engine.SetStoriesToCheck(stories);
            engine.StartComparison(weighed, data.LineLoadFrames);

            data.SidlCResults = engine.Results;
            data.UnclaimedSidlC = engine.Unclaimed;
            data.EtabsSidlC = engine.EtabsStretches;

            issues.AddRange(engine.Issues);
            if (issues.Count > 0)
            {
                issues.Add($"{weighed.Count} wall(s) and fill strip(s) weighed against the "
                           + $"{engine.Patterns.Description} loads on {engine.Runs.Count} ETABS frame(s).");
                if (tooLight > 0)
                {
                    issues.Add($"{tooLight} lighter than {pref.SidlCMinimumLoad:0.##} kN/m left out.");
                }
                ShowIssues?.Invoke("SIDL_C check issues", issues);
            }

            return issues;
        }

        /// <summary>
        /// The SIDL_C tab's elements: each wall or fill strip coloured by what the frames under
        /// it carry, the SIDL_C loads nothing in Revit accounts for, and the frames read, grey.
        /// The line load tab's builder over again, filed on the SIDL_C tab.
        /// </summary>
        private List<ElementViewModel> BuildSidlCElements()
        {
            List<ElementViewModel> built = new List<ElementViewModel>();

            if (data.SidlCResults.Count == 0 && data.EtabsSidlC.Count == 0)
            {
                return built;
            }

            built.AddRange(data.SidlCResults.SelectMany(result => ElementViewModelFactory.FromLineLoadResult(result, true)));
            built.AddRange(data.UnclaimedSidlC.Select(stretch => ElementViewModelFactory.FromUnclaimedLineLoad(stretch, true)));

            foreach (LineLoadRun run in data.SidlCResults
                         .SelectMany(result => result.Runs.Count > 0
                                         ? (IEnumerable<LineLoadRun>)result.Runs
                                         : new[] { result.Run })
                         .Where(run => run != null)
                         .Distinct())
            {
                built.Add(ElementViewModelFactory.FromLineLoadRun(run, true));
            }

            return built;
        }

        /// <summary>Why the SIDL_C tab has nothing on it, said rather than left to be guessed.</summary>
        private string WhyNoSidlC
        {
            get
            {
                PrefrencesData pref = PrefrencesData.getPrefrencesData();

                if (!pref.RunSidlCCheck)
                {
                    return "SIDL_C Line: the check is switched off. Turn it on under Set up, in Settings, "
                           + "and import the Revit model again - the walls it weighs are read then.";
                }

                if (!hasCompared)
                {
                    return "SIDL_C Line: nothing has been checked yet. Press RUN CHECK.";
                }

                if (data.LineLoadFrames.Count == 0)
                {
                    return "SIDL_C Line: the design model carries no distributed loads on its frames - import "
                           + "it from ETABS again.";
                }

                if (data.SidlCSources.Count == 0)
                {
                    return "SIDL_C Line: no Revit wall or fill strip is named as a SIDL_C load, and no ETABS frame "
                           + "carries one. The names are under Set up, in Settings, SIDL_C Line - import the "
                           + "Revit model again after changing them.";
                }

                return "SIDL_C Line: nothing to show on this level.";
            }
        }

        /// <summary>
        /// Compares the SIDL_C area loads on the ETABS slabs against the weight of the Revit
        /// light weight fill floors over them. Every level the check covers is read: no loading
        /// plan is needed, the fill says what it weighs. Quiet unless something went wrong.
        /// </summary>
        private List<string> CompareSidlCArea()
        {
            data.SidlCAreaResults = new List<SidlCAreaResult>();
            data.UnclaimedSidlCArea = new List<SidlCAreaUnclaimed>();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            List<string> issues = new List<string>();

            if (!pref.RunSidlCAreaCheck || data.Slabs.Count == 0)
            {
                return issues;
            }

            List<string> stories = CheckEveryStory
                ? data.Stories.Select(story => story.Name).ToList()
                : new List<string> { SlabCheckStory?.Name ?? "" };

            SidlCAreaCompareEngine engine = new SidlCAreaCompareEngine
            {
                Patterns = LineLoadPatterns.SidlCArea(pref),
                Band = new LoadBand(pref.SidlCAreaToleranceBelow, pref.SidlCAreaToleranceAbove),
                LightweightDensity = pref.SidlCAreaLightweightDensity,
                ToppingDensity = pref.SidlCAreaToppingDensity,
                ToppingThickness = pref.SidlCAreaToppingThickness,
                RoundUpTo = pref.SidlCAreaRoundUpTo,
                MisplacedDistance = pref.SidlCAreaMisplacedDistance,
                SmallestPiece = pref.SlabMinimumZoneArea,
                NarrowestPiece = pref.SlabMinimumZoneWidth,
                SmallestIssue = pref.SlabLoadMinimumIssueArea * 1e6
            };
            engine.SetStoriesToCheck(stories);
            engine.StartComparison(data.SidlCAreaSources, data.Slabs);

            data.SidlCAreaResults = engine.Results;
            data.UnclaimedSidlCArea = engine.Unclaimed;

            issues.AddRange(engine.Issues);
            if (issues.Count > 0)
            {
                ShowIssues?.Invoke("SIDL_C area check issues", issues);
            }

            return issues;
        }

        /// <summary>
        /// The SIDL_C area tab's elements: each fill floor coloured by the average SIDL_C under
        /// it, and the ETABS areas carrying SIDL_C that no fill accounts for.
        /// </summary>
        private List<ElementViewModel> BuildSidlCAreaElements()
        {
            List<ElementViewModel> built = new List<ElementViewModel>();
            // Cut into pieces along the edges of the fill and of the SIDL_C areas, the way the
            // slab load check cuts its plan; a fill whose story would not cut is drawn whole.
            foreach (SidlCAreaResult result in data.SidlCAreaResults)
            {
                built.AddRange(result.Pieces.Count > 0
                    ? result.Pieces.Select(ElementViewModelFactory.FromSidlCAreaPiece)
                    : ElementViewModelFactory.FromSidlCAreaResult(result));
            }
            built.AddRange(data.UnclaimedSidlCArea.Select(ElementViewModelFactory.FromSidlCAreaUnclaimed));
            return built;
        }

        /// <summary>Why the SIDL_C area tab has nothing on it.</summary>
        private string WhyNoSidlCArea
        {
            get
            {
                PrefrencesData pref = PrefrencesData.getPrefrencesData();

                if (!pref.RunSidlCAreaCheck)
                {
                    return "SIDL_C Area: the check is switched off. Turn it on under Set up, in Settings, "
                           + "and import the Revit model again - the fill floors it weighs are read then.";
                }

                if (!hasCompared)
                {
                    return "SIDL_C Area: nothing has been checked yet. Press RUN CHECK.";
                }

                if (data.SidlCAreaSources.Count == 0)
                {
                    return "SIDL_C Area: no Revit floor is named as light weight fill, and no ETABS area "
                           + "carries SIDL_C. The names are under Set up, in Settings, SIDL_C Area - import the "
                           + "Revit model again after changing them.";
                }

                return "SIDL_C Area: nothing to show on this level.";
            }
        }

        #endregion

        private void CompareColumns()
        {
            ColumnCompareEngine columnEngine = new ColumnCompareEngine
            {
                Overrides = pairingOverrides,
                ProjectKey = ProjectKey
            };
            // A Revit column running through a floor is one piece per story, as ETABS has it.
            List<Column_r> revitColumns = data.Columns_r
                .Where(column => !IsBelowEtabsModel(column.Story))
                .SelectMany(column => column.SplitAtStories(data.Stories))
                .ToList();
            columnEngine.StartComparison(data.Columns, revitColumns);
            data.PairedColumns = columnEngine.PairedColumns;
            data.UnmatchedEtabsColumns = columnEngine.UnmatchedEtabsColumns;
            data.UnmatchedRevitColumns = columnEngine.UnmatchedRevitColumns;
            columnsHeldUnpaired = columnEngine.HeldUnpaired;
        }

        private Story_E LowestStory()
        {
            return data.Stories.OrderBy(story => story.Elevation).FirstOrDefault();
        }

        /// <summary>
        /// True for a Revit wall or column named after the lowest ETABS story. ETABS names a
        /// wall or column after the story at its top, and nothing stands below the lowest one,
        /// so ETABS can have nothing there to pair with. A one-story model is never this: its
        /// only story is where everything is.
        /// </summary>
        private bool IsBelowEtabsModel(string story)
        {
            return data.Stories.Count > 1
                   && string.Equals(story, LowestStory().Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The ETABS columns and walls set by hand to pair with nothing, from the last pass.</summary>
        private ICollection<string> columnsHeldUnpaired = new HashSet<string>();
        private ICollection<string> wallsHeldUnpaired = new HashSet<string>();

        /// <summary>
        /// Offers what the column and the wall checks left unpaired to the other kind, where
        /// that is switched on: an ETABS column with a Revit wall on it, an ETABS wall with a
        /// Revit column on it. Run after both, since it takes from both of their leftovers.
        /// </summary>
        private void CrossPairColumnsAndWalls()
        {
            if (!PrefrencesData.getPrefrencesData().CrossPairColumnsAndWalls)
            {
                data.CrossPairs = new List<CrossPair>();
                return;
            }

            HashSet<string> held = new HashSet<string>(columnsHeldUnpaired, StringComparer.Ordinal);
            held.UnionWith(wallsHeldUnpaired);

            CrossPairEngine crossEngine = new CrossPairEngine();
            crossEngine.StartComparison(data.UnmatchedEtabsColumns, data.UnmatchedRevitColumns,
                                        data.UnmatchedEtabsWalls, data.UnmatchedRevitWalls, held);
            data.CrossPairs = crossEngine.Pairs;
        }

        /// <summary>
        /// Pairs the ETABS wall panels with the Revit walls and checks each pair on its
        /// thickness and on how far it sits off the other model's line. Skipped when neither
        /// model has a wall in it, so a job that never had walls costs nothing for a check it
        /// is not using.
        /// </summary>
        /// <param name="reportIssues">
        /// False when this is a re-pair rather than a run. What the wall engine reports are
        /// faults in the imported geometry, which a hand-set pairing has not changed - putting
        /// the same sheet up again every time someone corrects a wall would be nagging about
        /// something they have already read and cannot fix from here.
        /// </param>
        private void CompareWalls(bool reportIssues = true)
        {
            if (data.Walls.Count == 0 && data.Walls_r.Count == 0)
            {
                data.PairedWalls = new List<PairedWall>();
                data.UnmatchedEtabsWalls = new List<Wall_E>();
                data.UnmatchedRevitWalls = new List<Wall_R>();
                data.UncoveredRevitWalls = new List<UncoveredWallPiece>();
                wallsHeldUnpaired = new HashSet<string>();
                return;
            }

            WallCompareEngine wallEngine = new WallCompareEngine
            {
                Overrides = pairingOverrides,
                ProjectKey = ProjectKey
            };
            wallEngine.StartComparison(data.Walls, data.Walls_r.Where(wall => !IsBelowEtabsModel(wall.Story)).ToList());

            int wallsBelow = data.Walls_r.Count(wall => IsBelowEtabsModel(wall.Story));
            int columnsBelow = data.Columns_r.Count(column => IsBelowEtabsModel(column.Story));
            if (reportIssues && wallsBelow + columnsBelow > 0)
            {
                wallEngine.Issues.Add($"{wallsBelow} Revit wall(s) and {columnsBelow} column(s) rise only to "
                                      + $"{LowestStory().Name}, the lowest ETABS story, so they stand below the "
                                      + "ETABS model and were left out of the check.");
            }

            data.PairedWalls = wallEngine.PairedWalls;
            data.UnmatchedEtabsWalls = wallEngine.UnmatchedEtabsWalls;
            data.UnmatchedRevitWalls = wallEngine.UnmatchedRevitWalls;
            data.UncoveredRevitWalls = wallEngine.UncoveredRevitWalls;
            wallsHeldUnpaired = wallEngine.HeldUnpaired;

            if (reportIssues && wallEngine.Issues.Count > 0)
            {
                ShowIssues?.Invoke("Wall check issues", wallEngine.Issues);
            }
        }

        /// <summary>
        /// Runs whichever pairing engine is switched on. Both fill the same three containers,
        /// so everything downstream - the drawing, the labels, the export - is unaffected by
        /// which one ran.
        /// </summary>
        private void CompareBeams()
        {
            List<FrameData_e> etabsBeams = EtabsBeamsToCheck();

            if (PrefrencesData.getPrefrencesData().UseEndpointPairing)
            {
                EndpointCompareEngine endpointEngine = new EndpointCompareEngine
                {
                    Overrides = pairingOverrides,
                    ProjectKey = ProjectKey
                };
                endpointEngine.StartComparison(data.Frames_r, etabsBeams);
                data.UnmatchedEtabs = endpointEngine.UnmatchedEtabs;
                data.UnmatchedRevit = endpointEngine.UnmatchedRevit;
                data.PairedFrames = endpointEngine.PairedFrames;
                return;
            }

            CompareEngine beamEngine = CompareEngine.GetCompareEngine();
            beamEngine.startComparison(data.Frames_r, etabsBeams, ref etabsEngine);
            data.UnmatchedEtabs = beamEngine.RemainingEtabsframes;
            data.UnmatchedRevit = beamEngine.UnmatchedRevit;
            data.PairedFrames = beamEngine.PairedFrames;
        }

        /// <summary>
        /// The ETABS beams the check pairs: every frame read, less those whose section is on
        /// the "Leave out ETABS beams whose section contains" list - lines drawn in ETABS only
        /// to carry a line load. Filtered here rather than at the read so a saved model file
        /// obeys the list too, and a name taken off it brings the beams back on the next run.
        /// The line load checks read their own frames and are not touched.
        /// </summary>
        private List<FrameData_e> EtabsBeamsToCheck()
        {
            etabsBeamsLeftOutNote = "";
            string[] sections = PrefrencesData.SplitTypeNamePhrases(
                PrefrencesData.getPrefrencesData().EtabsBeamSectionsToIgnore);

            if (sections.Length == 0)
            {
                return data.Frames_e;
            }

            List<FrameData_e> kept = new List<FrameData_e>(data.Frames_e.Count);
            Dictionary<string, int> byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (FrameData_e frame in data.Frames_e)
            {
                string matched;
                if (PrefrencesData.MatchesAnyPhrase(frame.etabsSecName, sections, out matched))
                {
                    byName[matched] = byName.TryGetValue(matched, out int count) ? count + 1 : 1;
                    continue;
                }
                kept.Add(frame);
            }

            if (byName.Count > 0)
            {
                etabsBeamsLeftOutNote = " Left out " + byName.Values.Sum() + " ETABS beam(s) by section: "
                                        + string.Join(", ", byName.OrderByDescending(e => e.Value)
                                                                  .Select(e => e.Key + " " + e.Value)) + ".";
            }

            return kept;
        }

        private string etabsBeamsLeftOutNote = "";

        private void SelectInRevit()
        {
            List<long> revitIds = selection.Select(element => element.RevitId)
                                           .Where(id => id > 0)
                                           .Distinct()
                                           .ToList();
            if (revitIds.Count == 0)
            {
                return;
            }

            try
            {
                if (revitEngine == null)
                {
                    revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
                }

                string problem;
                if (!revitEngine.SelectInRevit(revitIds, out problem))
                {
                    Status = "Cannot select the element in Revit: " + problem;
                }
                else if (problem.Length > 0)
                {
                    Status = "Selected in Revit, but " + problem;
                }
            }
            catch (Exception e)
            {
                Status = "Cannot select the element in Revit: " + e.Message;
            }
        }

        #region writing a line load back to the design model

        /// <summary>
        /// The running model the design model was imported from, so a load written back goes to
        /// the model that was read rather than to whichever one answers first. Null after an
        /// import from a saved file, when the model to write to is asked for.
        /// </summary>
        private CsiInstance designInstance;

        /// <summary>Puts the selected drawn line load onto the ETABS or SAFE frames it was paired with.</summary>
        public ICommand WriteLoadBackCommand { get; }

        /// <summary>The selected element, when it is one drawn line load - the only thing that can be written back.</summary>
        private LineLoadElementViewModel SelectedLineLoad
        {
            get
            {
                LineLoadElementViewModel line = selectedElement as LineLoadElementViewModel;
                return line != null && line.Result != null ? line : null;
            }
        }

        /// <summary>Whether the card offers to write the load back: on a drawn line load alone.</summary>
        public bool ShowsWriteLoadBack => SelectedLineLoad != null && !showRawData;

        /// <summary>
        /// Whether it can be: only a line paired with design model frames, with a value of its
        /// own to put on them, and not while a check is running over the same frames.
        /// </summary>
        public bool CanWriteLoadBack => ShowsWriteLoadBack && SelectedLineLoad.CanWriteBack && !IsChecking;

        public string WriteLoadBackText => "ASSIGN LOAD IN " + DesignProductName;

        public string WriteLoadBackToolTip
        {
            get
            {
                LineLoadElementViewModel line = SelectedLineLoad;
                if (line != null && !line.CanWriteBack)
                {
                    return line.Result.Placements.Count == 0
                        ? "Only a load paired with " + DesignProductName + " frames can be assigned there, "
                          + "and nothing in " + DesignProductName + " runs under this one."
                        : "There is no value worked out for this load to assign.";
                }

                return "Assign this load to the " + DesignProductName + " frames it is paired with, over the "
                       + "stretch of each it lies on, in the load pattern the check reads. Says what it will "
                       + "write and asks first; the line loads are checked again afterwards.";
            }
        }

        /// <summary>
        /// Assigns the selected drawn line's value to the design model frames it was paired
        /// with, by their unique names, and checks the line loads again with what the model now
        /// carries.
        ///
        /// Each frame is made to carry, over the stretch the line lies on, what the plan asks for
        /// there - see <see cref="LineLoadWriteBack"/> - in the pattern the check reads. What
        /// will be written is said first and nothing is written until that is agreed to; a model
        /// holding analysis results has to be unlocked, which throws them away, and that is
        /// asked too.
        /// </summary>
        private void WriteLoadBack()
        {
            LineLoadElementViewModel element = SelectedLineLoad;
            if (element == null || !element.CanWriteBack)
            {
                return;
            }

            LineLoadResult result = element.Result;
            bool sidlC = element.Tab == ViewMode.SidlC;
            string program = DesignProductName;
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            LineLoadPatterns patterns = sidlC
                ? LineLoadPatterns.SidlC(pref)
                : LineLoadPatterns.From(pref);
            IList<LineLoadResult> everyLine = sidlC ? data.SidlCResults : data.LineLoadResults;

            try
            {
                string problem;
                EtabsEngine model = AttachForWriting(out problem);
                if (model == null)
                {
                    Status = "Nothing was assigned: " + problem;
                    return;
                }

                List<string> modelPatterns = model.LoadPatternNames();
                List<FrameWrite> writes = new List<FrameWrite>();

                foreach (LineLoadPlacement placement in result.Placements
                             .GroupBy(one => one.FrameUniqueName, StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.First()))
                {
                    string frame = placement.FrameUniqueName;

                    // The unique names are what the load goes to, and a model renumbered since the
                    // import - or a different model opened in the same program since - would hand
                    // them to other frames. The label is the cheap way to tell.
                    string label = model.FrameLabelNow(frame);
                    if (placement.FrameLabel.Length > 0
                        && !string.Equals(label, placement.FrameLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        Refuse($"frame {frame} is {(label.Length > 0 ? "labelled " + label : "not")} in the open "
                               + $"{program} model, where it was {placement.FrameLabel} when the model was "
                               + "imported. Import the model again and run the check before assigning.");
                        return;
                    }

                    List<FrameDistributedLoad> before = model.ReadFrameLoads(frame, out problem);
                    if (before == null)
                    {
                        Refuse(problem);
                        return;
                    }

                    // The frame is written whole, so a line on it with no value would have its
                    // stretch cleared and nothing put back.
                    List<LineLoadResult> unvalued = LineLoadWriteBack.LinesWithoutValueOn(frame, result, everyLine);
                    if (unvalued.Count > 0)
                    {
                        Refuse($"frame {placement.FrameIdentifier} also carries "
                               + string.Join(", ", unvalued.Select(other => other.Line.LoadName).Distinct())
                               + ", which has no value worked out, and the frame is written whole. "
                               + "Settle that line first.");
                        return;
                    }

                    List<FrameLoadSegment> segments = LineLoadWriteBack.SegmentsOn(
                        frame, result, everyLine, pref.LineLoadMinimumIssueLength);
                    if (segments.Count == 0)
                    {
                        continue;
                    }

                    string pattern = LineLoadWriteBack.PatternToWrite(before, modelPatterns, patterns, out problem);
                    if (pattern == null)
                    {
                        Refuse(problem);
                        return;
                    }

                    writes.Add(new FrameWrite
                    {
                        Frame = frame,
                        Label = placement.FrameIdentifier,
                        Pattern = pattern,
                        Segments = segments,
                        Before = before,
                        After = LineLoadWriteBack.Rewrite(before, pattern, patterns, segments,
                                                          model.ModelUnitsPerKilonewtonMetre)
                    });
                }

                if (writes.Count == 0)
                {
                    Status = "Nothing to assign: the line covers no stretch of the frames it was paired with.";
                    return;
                }

                bool locked = model.IsModelLocked;
                if (MessageBox.Show(DescribeWrite(result, writes, model.ModelName, locked, program),
                                    "Assign load in " + program, MessageBoxButton.YesNo,
                                    locked ? MessageBoxImage.Warning : MessageBoxImage.Question,
                                    MessageBoxResult.No) != MessageBoxResult.Yes)
                {
                    Status = "Nothing was assigned in " + program + ".";
                    return;
                }

                if (locked && !model.UnlockModel(out problem))
                {
                    Refuse("the model could not be unlocked - " + problem);
                    return;
                }

                List<string> failed = new List<string>();
                List<string> written = new List<string>();
                foreach (FrameWrite write in writes)
                {
                    if (model.WriteFrameLoads(write.Frame, write.After, write.Before, out problem))
                    {
                        written.Add(write.Label);
                    }
                    else
                    {
                        failed.Add(problem);
                    }

                    RefreshFrame(model, write.Frame);
                }

                model.RefreshView();
                RecheckAfterWrite(element, sidlC);

                Status = (written.Count > 0
                             ? $"{result.Line.LoadName} assigned in {program} on frame{(written.Count == 1 ? "" : "s")} "
                               + string.Join(", ", written) + ", and the line loads checked again."
                             : "Nothing was assigned in " + program + ".")
                         + (failed.Count > 0 ? " " + string.Join(" ", failed) : "");

                if (failed.Count > 0)
                {
                    MessageBox.Show(string.Join("\n\n", failed), "Assign load in " + program,
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception e)
            {
                Refuse(e.Message);
            }
        }

        /// <summary>What one frame is given, worked out before anything is written.</summary>
        private sealed class FrameWrite
        {
            public string Frame;
            public string Label;
            public string Pattern;
            public List<FrameLoadSegment> Segments;
            public List<FrameDistributedLoad> Before;
            public Dictionary<string, List<FrameDistributedLoad>> After;
        }

        private void Refuse(string why)
        {
            Status = "Nothing was assigned in " + DesignProductName + ": " + why;
            MessageBox.Show(Status, "Assign load in " + DesignProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// The model to write to: the one the design model was imported from while it is still
        /// open, and otherwise whichever the user picks.
        /// </summary>
        private EtabsEngine AttachForWriting(out string problem)
        {
            problem = null;
            CSiAPIv1.cSapModel sapModel = null;
            CsiInstance instance = designInstance;

            if (instance != null)
            {
                sapModel = CsiProducts.Attach(instance, out problem);
            }

            if (sapModel == null)
            {
                instance = PickModel == null ? null : PickModel();
                if (instance == null)
                {
                    problem = "no model was picked to assign the load in.";
                    return null;
                }

                sapModel = CsiProducts.Attach(instance, out problem);
                if (sapModel == null)
                {
                    return null;
                }
            }

            designInstance = instance;
            return new EtabsEngine(sapModel, instance.Product, SafeLevelForCurrentProject);
        }

        /// <summary>What will be written, said in full, to be agreed to before it is.</summary>
        private static string DescribeWrite(LineLoadResult result, List<FrameWrite> writes,
                                            string modelName, bool locked, string program)
        {
            List<string> lines = new List<string>
            {
                $"Assign {result.Line.LoadName} ({LoadSet.Text(result.Expected)} kN/m) in {program}, {modelName}:",
                ""
            };

            foreach (FrameWrite write in writes)
            {
                lines.Add($"Frame {write.Label} ({write.Frame}), {write.Pattern}: "
                          + string.Join("; ", write.Segments.Select(segment => segment.ToString())));

                List<string> cleared = write.After.Keys
                    .Where(pattern => !string.Equals(pattern, write.Pattern, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (cleared.Count > 0)
                {
                    lines.Add("    and the downward load in " + string.Join(", ", cleared)
                              + " taken off the same stretch");
                }
            }

            lines.Add("");
            lines.Add("Every drawn line on these frames is written at its own value over its own stretch - "
                      + "never averaged - and where lines overlap they are added together, as the check adds "
                      + "them. What each frame carried under the lines in that pattern is replaced; its loads "
                      + "where no line is drawn, and in every other pattern, are left as they are.");

            if (locked)
            {
                lines.Add("");
                lines.Add("The model is locked. Unlocking it deletes its analysis results.");
            }

            lines.Add("");
            lines.Add("Go ahead?");
            return string.Join("\n", lines);
        }

        /// <summary>Puts what one frame carries now into the model in hand, in place of what was imported.</summary>
        private void RefreshFrame(EtabsEngine model, string frameUniqueName)
        {
            string problem;
            List<FrameDistributedLoad> now = model.ReadFrameLoads(frameUniqueName, out problem);
            if (now == null)
            {
                return;
            }

            foreach (LineLoadFrame_E frame in data.LineLoadFrames.Where(frame => string.Equals(
                         frame.UniqueName, frameUniqueName, StringComparison.OrdinalIgnoreCase)))
            {
                frame.ReplaceLoads(model.AsLineLoads(now));
            }
        }

        /// <summary>
        /// The tab the line was written from, checked again with what the frames now carry - off
        /// the loading plans already read, so without going back to Revit - and the line selected
        /// again, which is what somebody who has just written it wants to look at.
        /// </summary>
        private void RecheckAfterWrite(LineLoadElementViewModel written, bool sidlC)
        {
            long revitId = written.RevitId;
            string story = written.Story;
            ViewMode? tab = written.Tab;

            if (sidlC)
            {
                CompareSidlC();
            }
            else
            {
                CompareLineLoads(reportIssues: false, readAgain: false);
                SettleHandPairings(reportThem: false);
            }

            RebuildKind(ElementKind.LineLoad);

            List<ElementViewModel> again = Elements.OfType<LineLoadElementViewModel>()
                .Where(element => element.Result != null && element.Tab == tab && element.RevitId == revitId
                                  && string.Equals(element.Story, story, StringComparison.OrdinalIgnoreCase))
                .Cast<ElementViewModel>()
                .ToList();

            if (again.Count == 1)
            {
                SelectedElement = again[0];
            }
            else if (again.Count > 1)
            {
                SelectOnly(null);
                AddToSelection(again);
            }
            else
            {
                SelectedElement = null;
            }
        }

        #endregion

        /// <summary>
        /// Writes the ETABS names of every beam next to the reinforcement mark its Revit
        /// counterpart carries, as a spreadsheet. The whole model goes out, every story, with
        /// the story in a column of its own: the picker above only chooses what is drawn.
        /// </summary>
        private void ExportBeamMarks()
        {
            try
            {
                IList<string[]> rows = BeamMarkExport.BuildRows(data, hasCompared);
                if (rows.Count <= 1)
                {
                    Status = "Nothing to export. Import a model first.";
                    return;
                }

                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export beam marks",
                    Filter = "Excel workbook (*.xlsx)|*.xlsx",
                    FileName = "beam marks.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                ExcelWorkbookWriter.Write(dialog.FileName, "Beam marks", rows);
                Status = $"Exported {rows.Count - 1} beam(s) to {dialog.FileName}.";
            }
            catch (Exception e)
            {
                Status = "Export failed: " + e.Message;
                MessageBox.Show(e.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearLabels()
        {
            foreach (LabelFieldGroup group in LabelGroups)
            {
                foreach (LabelFieldOption option in group.Options)
                {
                    option.IsSelected = false;
                }
            }
        }

        #endregion

        #region element projection

        private void RefreshStories()
        {
            Stories.Clear();
            foreach (Story_E story in data.Stories)
            {
                Stories.Add(story);
            }
            Findings.SetStories(Stories);

            SelectedStory = Stories.Count > 1 ? Stories[1] : Stories.FirstOrDefault();

            if (slabCheckStory == null || !Stories.Contains(slabCheckStory))
            {
                SlabCheckStory = SelectedStory;
            }

            // The description counts the imported floors, so it goes stale on a re-import.
            OnPropertyChanged(nameof(SlabCheckScopeDescription));
            RefreshStoryBanner();
        }

        /// <summary>
        /// Rebuilds what the canvas draws from the current containers and filters. Before a
        /// comparison has run this is the raw import; afterwards it is the paired results
        /// plus whatever could not be paired.
        /// </summary>
        public void RebuildElements()
        {
            // Read here rather than where it is ticked, so it always names what the plan about
            // to be built actually has on it.
            drawingBothColumns = PrefrencesData.getPrefrencesData().DrawBothColumnsOnMismatch;
            countingLineLoadsFrom = PrefrencesData.getPrefrencesData().LineLoadMinimumCountedLength;

            // The raw data is each model as it was read, which nobody has said anything about:
            // nothing on it can carry an acceptance or a review, so the store is not consulted,
            // and nothing in it is touched either. Marking a verdict can take an acceptance back,
            // and against elements with no results it would take back every one there is.
            if (showRawData)
            {
                allElements = BuildHeightZoneElements();
                allElements.AddRange(BuildRawElements());
                hasBuilt = true;
                slabBackdropByStory.Clear();
                ShowLevel();
                return;
            }

            // Every kind on every level, checked. The plan only ever draws one kind on one
            // level, but the tab counts and the level rail are both about what is NOT on
            // screen - which tab to open next, which floor to go to - so they have to be
            // counted off the whole model. Building it all was already what happened here;
            // the story filter has always come after.
            // The height zones first, so the lines they change are drawn over them.
            List<ElementViewModel> everything = BuildHeightZoneElements();
            everything.AddRange(BuildFrameElements());
            everything.AddRange(BuildSlabElements());
            everything.AddRange(BuildSidlCAreaElements());
            everything.AddRange(BuildLineLoadElements());

            allElements = everything;
            hasBuilt = true;
            slabBackdropByStory.Clear();

            TakeBackAcceptancesFiledWhileUnpaired(everything);
            MarkAndFilter(everything);

            SettleReviews();

            ShowLevel();
        }

        /// <summary>
        /// What people have said about each of these, and which of their results the switches
        /// leave on show.
        /// </summary>
        private void MarkAndFilter(IEnumerable<ElementViewModel> elements)
        {
            string project = ProjectKey;

            foreach (ElementViewModel element in elements)
            {
                MarkVerdict(element, project);
                ApplyResultFilters(element);

                // A folded wall's panels are never drawn, but the card and the chooser read
                // them, so they need the same switches applied or a panel would open showing
                // results the plan is currently hiding.
                foreach (ElementViewModel part in element.Parts)
                {
                    MarkVerdict(part, project);
                    ApplyResultFilters(part);
                }
            }
        }

        /// <summary>
        /// Builds again only the elements of one kind, after that kind has been paired again by
        /// hand, and leaves every other kind as it was.
        ///
        /// Pairing two beams changes the beams and nothing else - not a column, a wall, a piece
        /// of slab or a line load - so building the whole model again for it was most of the
        /// time a hand pairing took. The rebuilt kind goes back where it was in the list, so it
        /// is drawn in the same order against the others as before.
        /// </summary>
        private void RebuildKind(ElementKind kind)
        {
            // The raw data is not what a pairing changes, and is built whole or not at all.
            if (showRawData)
            {
                RebuildElements();
                return;
            }

            List<ElementViewModel> rebuilt;
            switch (kind)
            {
                case ElementKind.Beam: rebuilt = BuildBeamElements(); break;
                case ElementKind.Column: rebuilt = BuildColumnElements(); break;
                case ElementKind.Wall: rebuilt = BuildWallElements(); break;
                case ElementKind.LineLoad: rebuilt = BuildLineLoadElements(); break;
                case ElementKind.HeightZone: rebuilt = BuildHeightZoneElements(); break;
                default:
                    RebuildElements();
                    return;
            }

            int at = allElements.FindIndex(element => element.Kind == kind);
            List<ElementViewModel> everything = allElements.Where(element => element.Kind != kind).ToList();

            // The first zone on a plan that had none goes under everything, where the zones always
            // go, rather than on top of the lines it changes.
            if (at < 0 && kind == ElementKind.HeightZone)
            {
                at = 0;
            }
            everything.InsertRange(at < 0 ? everything.Count : at, rebuilt);
            allElements = everything;

            TakeBackAcceptancesFiledWhileUnpaired(everything);
            MarkAndFilter(rebuilt);

            SettleReviews();

            ShowLevel();
        }

        /// <summary>
        /// Puts the level on show onto the plan, out of what <see cref="RebuildElements"/>
        /// last built for the whole model.
        ///
        /// Apart from the build because a level is changed far more often than the model is,
        /// and a change of level changes nothing about any element - only which of them are on
        /// screen and what the tabs count. It used to build every element of every level again
        /// each time, and the whole of the ETABS slab backdrop with them: on a forty seven
        /// storey model, seconds for every click on the level rail. The elements kept from one
        /// level to the next also keep the shapes already worked out for drawing them.
        /// </summary>
        private void ShowLevel()
        {
            string storyName = SelectedStory?.Name;

            Findings.Update(allElements, storyName);

            // A kind whose chip is off is either drawn grey behind the rest or left off the
            // plan entirely, which is the difference between reading a beam against its floor
            // and reading the beams on their own.
            // The load cut is empty where no level is paired with a loading plan, which is the
            // ordinary state of most of a building. The floor is drawn anyway, out of the slab
            // cut and grey, rather than leaving the load tab blank - a blank plan reads as a
            // broken tool. Grey because the load tab is the one on show, and colouring it by
            // thickness would answer a question nobody asked.
            // Not in the raw data, whose load tab has a floor of its own - the ETABS areas, which
            // are drawn under both slab tabs.
            bool floorUnderAnEmptyLoadCut = !showRawData
                                            && Findings.IsActive(ViewMode.SlabLoads)
                                            && data.SlabLoadZones.Count == 0;

            List<ElementViewModel> onThisStory = new List<ElementViewModel>();
            foreach (ElementViewModel element in allElements)
            {
                bool active = element.Tab != null
                    ? Findings.IsActive(element.Tab.Value)
                    : Findings.IsKindActive(element.Kind);
                element.IsBackdrop = !active;

                if (!active && floorUnderAnEmptyLoadCut && element.Tab == ViewMode.Slabs)
                {
                    if (IsOnStory(element, storyName))
                    {
                        onThisStory.Add(element);
                    }
                    continue;
                }

                if (active)
                {
                    if (IsOnStory(element, storyName))
                    {
                        onThisStory.Add(element);
                    }

                    // The other model's outline goes on the plan with the element it explains,
                    // and only while that element is still saying something: a failure the
                    // switches have turned off greys the column, and an outline left behind
                    // under a grey column is a difference nobody asked to be shown.
                    if (element.Counterpart != null && !element.IsMuted
                        && IsOnStory(element.Counterpart, storyName))
                    {
                        onThisStory.Add(element.Counterpart);
                    }
                }
                // Not the height zones: they are nothing in either model, only a note laid over
                // the plan, and with their tab off they are not wanted on it at all.
                else if (ShowContext && element.Kind != ElementKind.Slab && element.Kind != ElementKind.HeightZone
                         && IsOnStory(element, storyName))
                {
                    onThisStory.Add(element);
                }
            }

            // The slab backdrop is the imported ETABS outlines, not the checked zones, so it
            // reads the same whether the slab check has been run or not and stays one shape per
            // area rather than the patchwork the mesh cuts it into.
            if (ShowContext && !Findings.IsKindActive(ElementKind.Slab))
            {
                onThisStory.AddRange(SlabBackdropOn(storyName));
            }

            foreach (ElementViewModel element in onThisStory)
            {
                ApplyResultFilters(element);
            }

            Elements.Clear();
            // Context first and muted next, so a result that is being shown is never drawn
            // under a grey one.
            foreach (ElementViewModel element in onThisStory.Where(e => e.IsDrawnAsContext))
            {
                Elements.Add(element);
            }
            foreach (ElementViewModel element in onThisStory.Where(e => !e.IsDrawnAsContext && e.IsMuted))
            {
                Elements.Add(element);
            }
            foreach (ElementViewModel element in onThisStory.Where(e => !e.IsDrawnAsContext && !e.IsMuted))
            {
                Elements.Add(element);
            }

            // A selection is of elements on the level that was on show, and none of them is on
            // the plan now.
            SelectedElement = null;
            RefreshFindingsList();
            RefreshLabelFields();
            // Every switch behind the SHOW button comes through here, so this is the one place
            // that has to say whether anything is being held back.
            OnPropertyChanged(nameof(IsFiltered));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(CanCompare));
            OnPropertyChanged(nameof(CanExportBeamMarks));
            OnPropertyChanged(nameof(ShowsHeightZoneTools));
            OnPropertyChanged(nameof(HeightZoneToolsText));
            // The heights in the key are the level's and the tab's, and both may just have changed.
            OnPropertyChanged(nameof(ShownVerdictKey));
        }

        /// <summary>
        /// Every beam, column and wall, checked if the comparison has run. Which of them ends
        /// up coloured on the plan and which ends up grey behind it is decided afterwards, by
        /// the tab; this method's job is only to produce the whole set, because the tab counts
        /// are taken off it too.
        /// </summary>
        private List<ElementViewModel> BuildFrameElements()
        {
            List<ElementViewModel> built = BuildColumnElements();
            built.AddRange(BuildBeamElements());
            built.AddRange(BuildWallElements());
            return built;
        }

        private List<ElementViewModel> BuildColumnElements()
        {
            List<ElementViewModel> built = new List<ElementViewModel>();

            if (hasCompared)
            {
                built.AddRange(data.PairedColumns.Select(ElementViewModelFactory.FromPairedColumn));
                built.AddRange(data.UnmatchedEtabsColumns.Select(c => ElementViewModelFactory.FromEtabsColumn(c, true)));
                built.AddRange(data.UnmatchedRevitColumns.Select(c => ElementViewModelFactory.FromRevitColumn(c, true)));
                // An ETABS column Revit has as a wall is drawn as the column it is in ETABS.
                built.AddRange(data.CrossPairs.Where(pair => pair.EtabsIsColumn)
                                              .Select(ElementViewModelFactory.FromCrossPair));
                return built;
            }

            built.AddRange(data.Columns.Select(c => ElementViewModelFactory.FromEtabsColumn(c, false)));
            built.AddRange(data.Columns_r.Select(c => ElementViewModelFactory.FromRevitColumn(c, false)));
            return built;
        }

        private List<ElementViewModel> BuildBeamElements()
        {
            List<ElementViewModel> built = new List<ElementViewModel>();

            if (hasCompared)
            {
                built.AddRange(data.PairedFrames.Select(ElementViewModelFactory.FromPairedFrame));
                built.AddRange(data.UnmatchedEtabs.Select(f => ElementViewModelFactory.FromEtabsFrame(f, true)));
                built.AddRange(data.UnmatchedRevit.Select(f => ElementViewModelFactory.FromRevitFrame(f, true)));
                return built;
            }

            built.AddRange(data.Frames_e.Select(f => ElementViewModelFactory.FromEtabsFrame(f, false)));
            built.AddRange(data.Frames_r.Select(f => ElementViewModelFactory.FromRevitFrame(f, false)));
            return built;
        }

        private List<ElementViewModel> BuildWallElements()
        {
            List<ElementViewModel> built = new List<ElementViewModel>();

            if (hasCompared)
            {
                // The ETABS panels standing on one line of wall are drawn as that wall
                // rather than as a pile of identical rectangles; see WallStacks.
                built.AddRange(WallStacks.Group(data.PairedWalls)
                                         .Select(ElementViewModelFactory.FromPairedWallStack));
                built.AddRange(WallStacks.Group(data.UnmatchedEtabsWalls)
                                         .Select(stack => ElementViewModelFactory.FromEtabsWallStack(stack, true)));
                built.AddRange(data.UnmatchedRevitWalls.Select(w => ElementViewModelFactory.FromRevitWall(w, true)));

                // A paired Revit wall is not drawn: the plan is the ETABS model, and where
                // ETABS stands off its line or stops a little short, the panel carries a
                // warning saying so rather than the two being drawn over each other. What
                // is drawn from the Revit side is the stretch ETABS misses altogether,
                // once there is more of it than a warning can stand for.
                built.AddRange(data.UncoveredRevitWalls
                                   .Select(ElementViewModelFactory.FromUncoveredWallPiece));

                // An ETABS wall Revit has as a column, folded the same way the paired panels are.
                built.AddRange(ElementViewModelFactory.FromCrossPairWalls(
                    data.CrossPairs.Where(pair => !pair.EtabsIsColumn)));
                return built;
            }

            built.AddRange(WallStacks.Group(DrawableEtabsWalls())
                                     .Select(stack => ElementViewModelFactory.FromEtabsWallStack(stack, false)));
            built.AddRange(data.Walls_r.Select(w => ElementViewModelFactory.FromRevitWall(w, false)));
            return built;
        }

        /// <summary>
        /// The checked slab: the ETABS mesh with a verdict on every piece of it. Before the
        /// comparison has run there is nothing cut yet, so both models' outlines are shown as
        /// they were imported.
        /// </summary>
        private List<ElementViewModel> BuildSlabElements()
        {
            if (hasCompared)
            {
                // Both cuts of the floor, whichever slab chip is up. The two chips draw two
                // different cuts of the same floor, and they used to be built one at a time, so
                // moving between the slab check and the load check built the whole model again.
                // Now the chip only chooses which cut is shown - see ShowLevel - and each piece
                // says which cut it is, so each is counted on its own tab.
                List<ElementViewModel> slabs = new List<ElementViewModel>();
                foreach (SlabZone zone in data.SlabZones)
                {
                    ElementViewModel element = ElementViewModelFactory.FromSlabZone(zone);
                    element.Tab = ViewMode.Slabs;
                    slabs.Add(element);
                }
                foreach (SlabZone zone in data.SlabLoadZones)
                {
                    ElementViewModel element = ElementViewModelFactory.FromSlabZone(zone);
                    element.Tab = ViewMode.SlabLoads;
                    slabs.Add(element);
                }
                return slabs;
            }

            List<ElementViewModel> built = new List<ElementViewModel>();
            built.AddRange(data.Slabs.Where(slab => !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null)
                                     .Select(slab => ElementViewModelFactory.FromEtabsSlab(slab, false)));
            built.AddRange(data.Slabs_r.Where(slab => slab.Plan != null)
                                       .Select(slab => ElementViewModelFactory.FromRevitSlab(slab, false)));
            return built;
        }

        /// <summary>
        /// The line load plan: the detail lines somebody drew, coloured by what the frames
        /// under them had to say, and the stretches of frame carrying a load nothing drew.
        ///
        /// Nothing before the comparison has run. Every other kind has an imported state worth
        /// drawing - a beam is a beam before anybody checks it - but a drawn load line on its
        /// own says nothing the loading plan in Revit does not already say better, and the
        /// frames it would be laid over are not on this plan at all.
        /// </summary>
        private List<ElementViewModel> BuildLineLoadElements()
        {
            // The SIDL_C tab's lines are line loads too, and are built with them so that a
            // rebuild of the kind - after a pairing, a height zone, a setting - keeps both tabs.
            List<ElementViewModel> built = BuildSidlCElements();

            // Whatever the line load pass has read, rather than whether the whole check has
            // run. They amount to the same thing today - the pass runs inside the check.
            if (data.LineLoadResults.Count == 0 && data.EtabsLineLoads.Count == 0)
            {
                return built;
            }

            built.AddRange(data.LineLoadResults.SelectMany(ElementViewModelFactory.FromLineLoadResult));
            built.AddRange(data.UnclaimedLineLoads.Select(ElementViewModelFactory.FromUnclaimedLineLoad));

            // The runs the drawn lines landed on, grey, under them. A run nothing claimed is
            // already drawn as the load nobody asked for, so only the claimed ones are added
            // here - otherwise the stretch would be on the plan twice, once in each colour.
            //
            // Every run a line was read against, not only the one most of it lies along: with
            // the frames read one by one a line over three frames is on all three.
            foreach (LineLoadRun run in data.LineLoadResults
                         .SelectMany(result => result.Runs.Count > 0
                                         ? (IEnumerable<LineLoadRun>)result.Runs
                                         : new[] { result.Run })
                         .Where(run => run != null)
                         .Distinct())
            {
                built.Add(ElementViewModelFactory.FromLineLoadRun(run));
            }

            return built;
        }

        /// <summary>
        /// The ETABS slabs behind the frame checks on one level, in grey. The imported outlines
        /// are used rather than the checked zones so the backdrop reads the same whether the
        /// slab check has been run or not, and stays one shape per area rather than a patchwork.
        ///
        /// One level's worth, made the first time that level is shown and kept until the next
        /// build. An ETABS floor arrives meshed into thousands of shells, so the whole building's
        /// backdrop is tens of thousands of shapes - and it used to be made in full, and then
        /// all but one level of it thrown away, every time the level changed.
        /// </summary>
        private List<ElementViewModel> SlabBackdropOn(string storyName)
        {
            string key = storyName ?? "";

            List<ElementViewModel> backdrop;
            if (!slabBackdropByStory.TryGetValue(key, out backdrop))
            {
                backdrop = data.Slabs
                    .Where(slab => !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null
                                   && (key.Length == 0 || string.IsNullOrEmpty(slab.Story)
                                       || string.Equals(slab.Story, key, StringComparison.OrdinalIgnoreCase)))
                    .Select(slab => ElementViewModelFactory.AsContext(
                        ElementViewModelFactory.FromEtabsSlab(slab, false)))
                    .ToList();
                slabBackdropByStory[key] = backdrop;
            }

            return backdrop;
        }

        /// <summary>The slab backdrop of each level already shown since the last build.</summary>
        private readonly Dictionary<string, List<ElementViewModel>> slabBackdropByStory =
            new Dictionary<string, List<ElementViewModel>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// True once the elements have been built, after which a chip or a level only changes
        /// which of them are shown. No chip changes what is built: each decides only which of
        /// the built elements are coloured and which are grey - the two slab chips included,
        /// since both cuts of the floor are built together.
        /// </summary>
        private bool hasBuilt;

        /// <summary>
        /// Everything the raw data draws, every kind on every level, as each model was read -
        /// see <see cref="RawElements"/>. The tabs choose what of it is shown, as they do for
        /// the check.
        ///
        /// The two load tabs draw what the check last read for them. The ETABS side of both is
        /// in the model from the import, but the loading plans are read off Revit when the
        /// check runs and not before, so until then those tabs have one model on them.
        /// </summary>
        private List<ElementViewModel> BuildRawElements()
        {
            List<ElementViewModel> raw = new List<ElementViewModel>();

            raw.AddRange(data.Columns.Select(RawElements.EtabsColumn));
            raw.AddRange(data.Columns_r.Select(RawElements.RevitColumn));

            raw.AddRange(data.Frames_e.Select(RawElements.EtabsBeam));
            raw.AddRange(data.Frames_r.Select(RawElements.RevitBeam));

            raw.AddRange(WallStacks.Group(DrawableEtabsWalls()).Select(stack => RawElements.EtabsWall(stack)));
            raw.AddRange(data.Walls_r.Select(RawElements.RevitWall));

            raw.AddRange(data.Slabs.Where(slab => !slab.IsOpening && !slab.IsLoadOnly && slab.Plan != null)
                                   .Select(RawElements.EtabsSlab));
            raw.AddRange(data.Slabs.Where(slab => slab.IsLoadOnly && slab.Plan != null)
                                   .Select(RawElements.EtabsLoadArea));
            raw.AddRange(data.Slabs_r.Where(slab => slab.Plan != null)
                                     .Select(RawElements.RevitSlab));

            foreach (KeyValuePair<string, LoadCoverage> level in loadingPlansRead)
            {
                raw.AddRange(level.Value.Regions
                                  .Where(region => region.Plan != null)
                                  .Select(region => RawElements.LoadRegion(region, level.Key)));
            }

            raw.AddRange(data.LineLoadResults.Where(result => result.Line != null && result.Line.HasLine)
                                             .Select(RawElements.RevitLineLoad));
            raw.AddRange(data.LineLoadFrames.Where(RawElements.CarriesALineLoad)
                                            .Select(RawElements.EtabsLineLoad));
            raw.AddRange(data.SidlCSources.Where(source => source.Length > 0)
                                          .Select(RawElements.SidlCSource));
            raw.AddRange(data.SidlCAreaSources.SelectMany(RawElements.SidlCAreaSource));

            return raw;
        }

        /// <summary>
        /// The ETABS wall areas there is anything to draw. An area whose corners are all at one
        /// place in plan has no line and no rectangle; it would be drawn at the origin, which
        /// is worse than not drawing it. The import says how many there were.
        /// </summary>
        private IEnumerable<Wall_E> DrawableEtabsWalls()
        {
            return data.Walls.Where(wall => wall != null && wall.HasCentreLine);
        }

        private static bool IsOnStory(ElementViewModel element, string storyName)
        {
            if (string.IsNullOrEmpty(storyName) || string.IsNullOrEmpty(element.Story))
            {
                return true;
            }
            return string.Equals(element.Story, storyName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Works out what this element draws as, from the results whose check is still
        /// switched on. Every element stays on the plan: one with nothing left on show is
        /// drawn grey and unlabelled rather than taken away, and its other results are
        /// untouched, so switching one check off never costs the answer of another.
        ///
        /// Where more than one result is on show, the most serious of them wins the colour, so
        /// a column that failed one check and passed another still reads as failed until that
        /// failure is the thing being switched off.
        /// </summary>
        private void ApplyResultFilters(ElementViewModel element)
        {
            // Context carries no result of this check, and the raw data no result of any, so
            // there is nothing here for the switches to show or hide. The raw data is never
            // greyed for it either: what it has to say is what it is, not what it was found.
            // A height zone neither: it is drawn by hand and has nothing to be judged on.
            if (element.IsContext || element.IsRaw || element.Kind == ElementKind.HeightZone)
            {
                element.HiddenChecks = new HashSet<CheckKind>();
                element.HiddenResults = new HashSet<string>(StringComparer.Ordinal);
                element.DisplayState = CheckState.NotChecked;
                element.IsMuted = false;
                return;
            }

            HashSet<string> hiddenResults = new HashSet<string>(StringComparer.Ordinal);
            HashSet<CheckKind> shownKinds = new HashSet<CheckKind>();
            HashSet<CheckKind> hidden = new HashSet<CheckKind>();
            List<CheckOutcome> shown = new List<CheckOutcome>();

            // An accepted element with the accepted switch off holds back everything it says.
            bool acceptedHidden = !ShowAccepted && element.IsAccepted;

            foreach (CheckOutcome outcome in element.Outcomes)
            {
                if (!acceptedHidden && IsOutcomeShown(element, outcome))
                {
                    shown.Add(outcome);
                    shownKinds.Add(outcome.Kind);
                }
                else
                {
                    hiddenResults.Add(ElementViewModel.ResultKey(outcome));
                    hidden.Add(outcome.Kind);
                }
            }

            // A check is only hidden once nothing it said is left on show. Where a check
            // reports on several places along one element - the stations of a beam, the four
            // loads on a piece of slab - some of them can be switched off while others are not,
            // and calling the whole check hidden there would take the ones still on show off
            // the plan and out of the findings list with them.
            hidden.ExceptWith(shownKinds);

            element.HiddenChecks = hidden;
            element.HiddenResults = hiddenResults;
            element.DisplayState = MostSerious(shown);
            element.IsMuted = shown.Count == 0;
        }

        private bool IsOutcomeShown(ElementViewModel element, CheckOutcome outcome)
        {
            // Pairing is not one of the per-check switches: an element with no counterpart has
            // this one result and nothing else, so it has its own switch per model.
            if (outcome.Kind == CheckKind.Pairing)
            {
                if (outcome.Level == ResultLevel.NotChecked)
                {
                    return ShowNotChecked;
                }
                return element.Source == ElementSource.UnmatchedRevit
                    ? ShowUnpairedRevit
                    : ShowUnpairedEtabs;
            }

            return CheckFilters.IsShown(outcome.Kind, outcome.Level);
        }

        /// <summary>
        /// Checks whose failure means one model has something the other does not, rather than
        /// the two disagreeing about something they both have. Those are drawn in the colour of
        /// the model that has it, which is what makes an unpaired beam and a patch of slab only
        /// ETABS knows about read the same way.
        /// </summary>
        private static bool IsOneModelOnly(CheckKind kind)
        {
            return kind == CheckKind.Pairing
                || kind == CheckKind.SlabMissingInRevit
                || kind == CheckKind.SlabMissingInEtabs
                || kind == CheckKind.SlabOpeningInRevit
                || kind == CheckKind.SlabOpeningInEtabs
                || kind == CheckKind.LineLoadMissingInEtabs
                || kind == CheckKind.LineLoadMissingInRevit;
        }

        private static CheckState MostSerious(IList<CheckOutcome> shown)
        {
            if (shown.Count == 0)
            {
                return CheckState.NotChecked;
            }

            if (shown.Any(outcome => IsOneModelOnly(outcome.Kind)
                                     && outcome.Level == ResultLevel.Failed))
            {
                return CheckState.Unpaired;
            }
            if (shown.Any(outcome => outcome.Level == ResultLevel.Failed))
            {
                return CheckState.Failed;
            }
            if (shown.Any(outcome => outcome.Level == ResultLevel.Review))
            {
                return CheckState.Review;
            }
            if (shown.Any(outcome => outcome.Level == ResultLevel.Passed))
            {
                return CheckState.Passed;
            }
            return CheckState.NotChecked;
        }

        #region pairing by hand

        /// <summary>
        /// How far the candidate list reaches. The same radius the automatic pass uses, so the
        /// list is exactly the set of elements the check considered and rejected - which is
        /// what makes it an explanation of the pairing rather than a second, different search.
        /// </summary>
        private double CandidateRadius(ElementKind kind)
        {
            return PairingOverrides.PickerRadius(PairKindOf(kind));
        }

        /// <summary>
        /// The Revit elements of the same kind near this one, nearest first. Everything is
        /// offered, paired or not: re-pairing usually means taking a counterpart off the
        /// element that wrongly claimed it, and a list that hid those would hide the answer.
        /// </summary>
        private IList<PairCandidate> FindPairCandidates(ElementViewModel element)
        {
            List<PairCandidate> found = new List<PairCandidate>();
            if (element == null)
            {
                return found;
            }

            double radius = CandidateRadius(element.Kind);
            System.Windows.Point? here = PlanPointOf(element);
            if (here == null)
            {
                return found;
            }

            switch (element.Kind)
            {
                case ElementKind.Column:
                    foreach (Column_r column in data.Columns_r)
                    {
                        Add(found, element, column.RevitID, column.PlanPoint, here.Value, radius,
                            Describe(column.TypeMark, column.TypeName));
                    }
                    break;

                case ElementKind.Beam:
                    foreach (FrameData_r frame in data.Frames_r)
                    {
                        Add(found, element, frame.RevitID, frame.Start, here.Value, radius,
                            Describe(frame.BeamMark, ""));
                    }
                    break;

                case ElementKind.Wall:
                    foreach (Wall_R wall in data.Walls_r)
                    {
                        Add(found, element, wall.RevitID, wall.PlanStart, here.Value, radius,
                            Describe("", Math.Round(wall.Thickness) + " mm"));
                    }
                    break;
            }

            return found.OrderBy(candidate => candidate.DistanceMM).ToList();
        }

        private static string Describe(string first, string second)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(first))
            {
                parts.Add(first.Trim());
            }
            if (!string.IsNullOrWhiteSpace(second))
            {
                parts.Add(second.Trim());
            }
            return string.Join(" \u00B7 ", parts);
        }

        private static void Add(List<PairCandidate> found, ElementViewModel element, long revitId,
                                MyPoint point, System.Windows.Point here, double radius,
                                string description)
        {
            if (point == null)
            {
                return;
            }

            // Both sides are already in the shared ETABS millimetres by the time they get here,
            // so this is a plain plan distance and not a change of coordinate system.
            double dx = point.X - here.X;
            double dy = point.Y - here.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > radius)
            {
                return;
            }

            found.Add(new PairCandidate(revitId, description, distance,
                                        revitId == element.RevitId));
        }

        /// <summary>
        /// Where the selected element sits in plan, for measuring candidates from. A beam and a
        /// wall are measured from the end they start at rather than their middle, because that
        /// is the end the pairing passes compare.
        /// </summary>
        private static System.Windows.Point? PlanPointOf(ElementViewModel element)
        {
            ColumnElementViewModel column = element as ColumnElementViewModel;
            if (column != null)
            {
                return column.Center;
            }

            BeamElementViewModel beam = element as BeamElementViewModel;
            if (beam != null)
            {
                return beam.Start;
            }

            WallElementViewModel wall = element as WallElementViewModel;
            return wall == null ? (System.Windows.Point?)null : wall.Start;
        }

        /// <summary>
        /// Writes the decision down and pairs that one kind again.
        ///
        /// The whole kind rather than the one element: a pairing takes a counterpart away from
        /// whatever had it before, so the element that loses it has to be re-decided too, and so
        /// does anything that then becomes free. Running that kind's pass again is the only way
        /// to get an answer that is consistent with itself.
        ///
        /// But only that kind. Pairing a column cannot change a beam, a wall or a slab - the
        /// three pools are separate and each engine reads its own containers - so the rest of
        /// the run is left standing exactly as it was. That matters because the slab check is
        /// the slow half of a run by a wide margin, and it has nothing to do with the pairing
        /// that was just set: making someone sit through the mesh again to correct two columns
        /// is the reason a correction feels expensive enough not to make.
        /// </summary>
        private void ApplyPairing(ElementViewModel element, long? revitId)
        {
            if (element == null || element.EtabsUniqueName.Length == 0)
            {
                return;
            }

            string key = PairingOverrides.KeyOf(PairKindOf(element.Kind), element.EtabsUniqueName);
            pairingOverrides.Set(ProjectKey, key, revitId ?? PairingOverrides.Unpaired);
            SaveUserSettings();

            string what = revitId == null
                ? "Paired with nothing, by hand."
                : "Paired with Revit " + revitId.Value + ", by hand.";

            PutHandPairingsIntoEffect(what, new[] { element.Kind });
        }

        /// <summary>
        /// Brings the plan up to date with pairings just written by hand: pairs each kind they
        /// were written for again, once, and says what was done.
        ///
        /// The one way every hand pairing is put into effect - the pairing editor on the card,
        /// two elements marked on the plan, and unpairing - so that what each of them does once
        /// the pairing is written, and what each says about it, cannot drift apart.
        /// </summary>
        private void PutHandPairingsIntoEffect(string what, IEnumerable<ElementKind> writtenFor)
        {
            List<ElementKind> kinds = writtenFor.Distinct().ToList();
            SelectedElement = null;

            // Nothing has been checked yet, so there is nothing to bring up to date. Saying so
            // is better than starting a whole run off the back of a pairing edit.
            if (!hasCompared)
            {
                Status = what + " It will be used the next time the check runs.";
                return;
            }

            // The old beam engine pairs on its own and never reads the overrides, so a beam
            // pairing set while it is on is kept but does nothing. Better said out loud than
            // left to look as though the pairing was ignored at random.
            string aside = "";
            if (kinds.Contains(ElementKind.Beam)
                && !PrefrencesData.getPrefrencesData().UseEndpointPairing)
            {
                kinds.Remove(ElementKind.Beam);
                aside = " Beams are being paired on their overlap rather than their end points, and "
                        + "that pass does not read hand-set pairings - switch end point pairing on "
                        + "under Set up for this to take effect.";
            }

            if (kinds.Count == 0)
            {
                Status = what + aside;
                return;
            }

            // Said, and put on the screen, before the pass starts. Columns and beams are back
            // before anyone could read it, but a wall pass on a large model measures every wall
            // against every other and holds the thread while it does - and a window that goes
            // still with the old status still up reads as one that has hung.
            Status = what + " Pairing the " + string.Join(" and ", kinds.Select(KindWord)) + " again...";
            Pump();

            try
            {
                foreach (ElementKind kind in kinds)
                {
                    RePairOneKind(kind);
                }
            }
            catch (Exception e)
            {
                Status = "Pairing again failed: " + e.Message;
                MessageBox.Show(e.Message, "Pairing failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Status = what + aside + " " + Summary
                     + (slabCheckStopped
                            ? " The slab check is still the one that was stopped part way through."
                            : "");
        }

        #region height zones

        /// <summary>Asks for a zone's height and name, and answers whether to go ahead. Set by the window.</summary>
        public Func<HeightZoneRequest, bool> AskForHeightZone { get; set; }

        /// <summary>Asks which levels to copy to, and answers whether to go ahead. Set by the window.</summary>
        public Func<LevelChoices, bool> AskForLevels { get; set; }

        private ICommand editHeightZonesCommand;
        private ICommand copyHeightZonesCommand;
        private ICommand deleteHeightZonesCommand;

        public ICommand EditHeightZonesCommand => editHeightZonesCommand
            ?? (editHeightZonesCommand = new RelayCommand(_ => EditHeightZones(), _ => SelectedHeightZones.Count > 0));

        public ICommand CopyHeightZonesCommand => copyHeightZonesCommand
            ?? (copyHeightZonesCommand = new RelayCommand(_ => CopyHeightZones(), _ => SelectedHeightZones.Count > 0));

        public ICommand DeleteHeightZonesCommand => deleteHeightZonesCommand
            ?? (deleteHeightZonesCommand = new RelayCommand(_ => DeleteHeightZones(), _ => SelectedHeightZones.Count > 0));

        /// <summary>The drawing tools are up while the height zone tab is, on a level.</summary>
        public bool ShowsHeightZoneTools => Findings.IsActive(ViewMode.HeightZones) && SelectedStory != null;

        /// <summary>What the tools say about the level on show.</summary>
        public string HeightZoneToolsText
        {
            get
            {
                string story = SelectedStory?.Name ?? "";
                int count = heightZones.OnStory(ProjectKey, story).Count;
                double floor = FloorHeightOf(story);

                string here = count == 0
                    ? "No zones on " + story + ", so every line load here uses the floor to floor height"
                    : count + (count == 1 ? " zone" : " zones") + " on " + story
                      + ". Line loads mostly inside one use its height instead of the floor to floor height";

                return here + (floor > 0 ? " (" + HeightZone.Metres(floor) + ")." : ".")
                       + " Select a zone to change its height, copy it to other levels or delete it.";
            }
        }

        /// <summary>The zones in the selection, as the store holds them; empty unless every selected element is one.</summary>
        private IList<HeightZone> SelectedHeightZones
        {
            get
            {
                if (selection.Count == 0 || selection.Any(element => element.Kind != ElementKind.HeightZone))
                {
                    return new List<HeightZone>();
                }

                string project = ProjectKey;
                return selection.OfType<HeightZoneElementViewModel>()
                                .Select(element => heightZones.Find(project, element.ZoneId))
                                .Where(zone => zone != null)
                                .ToList();
            }
        }

        private List<ElementViewModel> BuildHeightZoneElements()
        {
            // The larger first, so each is drawn under the smaller ones over it; and each without
            // the part a smaller one covers, since the smaller one answers there - see
            // HeightZones.InOrderOfPrecedence. Only the height in control is coloured in.
            return HeightZones.InOrderOfPrecedence(heightZones.ForProject(ProjectKey))
                              .Reverse()
                              .Select(HeightZoneElement)
                              .ToList();
        }

        private ElementViewModel HeightZoneElement(HeightZone zone)
        {
            double floor = FloorHeightOf(zone.Story);
            int lines = data.LineLoadResults.Count(result => result.HeightZoneId == zone.Id);

            List<ResultField> fields = new List<ResultField>
            {
                new ResultField("HeightZone.Height", "Height", HeightZone.Metres(zone.Height)),
                new ResultField("HeightZone.Name", "Name", zone.Name),
                new ResultField("HeightZone.Floor", "Floor to floor here",
                                floor > 0 ? HeightZone.Metres(floor) : "not known"),
                new ResultField("HeightZone.Area", "Area (m²)",
                                (zone.Area / 1e6).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)),
                new ResultField("HeightZone.Lines", "Line loads using it",
                                data.LineLoadResults.Count == 0 ? "not checked yet" : lines.ToString())
            };

            // Where it overlaps another zone only one of the two can answer, and which is not
            // something to be worked out from the colours.
            IList<KeyValuePair<HeightZone, bool>> overlaps = heightZones.OverlapsOf(ProjectKey, zone);
            if (overlaps.Count > 0)
            {
                fields.Add(new ResultField("HeightZone.Overlaps", "Overlaps",
                    string.Join("; ", overlaps.Select(overlap => ZoneName(overlap.Key) + " - "
                        + (overlap.Value ? "this zone is used there" : "that zone is used there")))));
            }

            // The smaller zones over it answer where they are, so that part is not drawn as this.
            List<PlanPolygon> coveredBy = overlaps.Where(overlap => !overlap.Value)
                                                  .Select(overlap => overlap.Key.Plan)
                                                  .ToList();

            return new HeightZoneElementViewModel(zone, fields, heightZones.ColourOf(ProjectKey, zone.Height), coveredBy)
            {
                PairedWith = "Line loads drawn mostly inside it are worked out from "
                             + HeightZone.Metres(zone.Height) + " floor to floor instead of "
                             + (floor > 0 ? "the level's " + HeightZone.Metres(floor) : "the level's own height")
                             + ". Nothing but the line load check reads it."
                             + (overlaps.Count == 0
                                 ? ""
                                 : " Where two zones overlap, the smaller one is used, and only it is coloured in there.")
            };
        }

        /// <summary>A zone as it is named beside another: its name if it has one, and its height.</summary>
        private static string ZoneName(HeightZone zone)
        {
            return (zone.Name.Length > 0 ? "\"" + zone.Name + "\" " : "a zone of ") + HeightZone.Metres(zone.Height);
        }

        /// <summary>
        /// The level's own floor to floor height: how far the next level up is above it, in mm.
        /// Zero for the top level and for one not in the model - the same answer the line load
        /// check gets, and for the same reason.
        /// </summary>
        private double FloorHeightOf(string story)
        {
            List<Story_E> byElevation = data.Stories.Where(level => level != null)
                                                    .OrderBy(level => level.Elevation)
                                                    .ToList();
            int at = byElevation.FindIndex(level => string.Equals(level.Name, story ?? "",
                                                                  StringComparison.OrdinalIgnoreCase));
            return at < 0 || at == byElevation.Count - 1
                ? 0
                : byElevation[at + 1].Elevation - byElevation[at].Elevation;
        }

        /// <summary>
        /// A zone has been drawn on the plan: its corners, in plan mm. Asks for its height and
        /// keeps it on the level on show.
        /// </summary>
        public void AddHeightZone(IList<System.Windows.Point> corners)
        {
            Story_E story = SelectedStory;
            if (story == null)
            {
                Status = "Pick a level first - a height zone belongs to the level it is drawn on.";
                return;
            }

            HeightZone zone = new HeightZone { Story = story.Name };
            zone.SetOutline(corners.Select(corner => new MyPoint(corner.X, corner.Y, 0)));
            if (zone.Plan == null || zone.Area < 1)
            {
                Status = "That outline has no area to it, so no zone was made. Draw it again.";
                return;
            }

            double floor = FloorHeightOf(story.Name);
            HeightZoneRequest request = new HeightZoneRequest("NEW HEIGHT ZONE ON " + story.Name.ToUpperInvariant(),
                                                              floor, floor, "");
            if (AskForHeightZone == null || !AskForHeightZone(request))
            {
                Status = "No height given, so the zone was not kept.";
                return;
            }

            zone.Height = request.HeightMM;
            zone.Name = request.KeptName;
            heightZones.Add(ProjectKey, zone);

            int overlaps = heightZones.OverlapsOf(ProjectKey, zone).Count;
            HeightZonesChanged("Height zone of " + HeightZone.Metres(zone.Height) + " added on " + story.Name + "."
                               + (overlaps == 0
                                   ? ""
                                   : $" It overlaps {overlaps} other zone(s): where zones overlap, the smaller one is used."),
                               new[] { zone.Id });
        }

        /// <summary>Changes the height of the zones selected, and the name where it is one zone.</summary>
        private void EditHeightZones()
        {
            IList<HeightZone> zones = SelectedHeightZones;
            if (zones.Count == 0)
            {
                return;
            }

            HeightZone first = zones[0];
            bool oneHeight = zones.All(zone => Math.Abs(zone.Height - first.Height) < 0.5);
            HeightZoneRequest request = new HeightZoneRequest(
                zones.Count == 1
                    ? "HEIGHT ZONE ON " + first.Story.ToUpperInvariant()
                    : zones.Count + " HEIGHT ZONES",
                zones.Select(zone => zone.Story).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
                    ? FloorHeightOf(first.Story)
                    : 0,
                oneHeight ? first.Height : 0,
                first.Name,
                canName: zones.Count == 1);

            if (AskForHeightZone == null || !AskForHeightZone(request))
            {
                return;
            }

            string project = ProjectKey;
            foreach (HeightZone zone in zones)
            {
                zone.Height = request.HeightMM;
                if (zones.Count == 1)
                {
                    zone.Name = request.KeptName;
                }
                heightZones.Add(project, zone);
            }

            HeightZonesChanged((zones.Count == 1 ? "The zone is " : zones.Count + " zones are ")
                               + HeightZone.Metres(request.HeightMM) + " now.",
                               zones.Select(zone => zone.Id).ToList());
        }

        /// <summary>Puts copies of the zones selected on the levels ticked.</summary>
        private void CopyHeightZones()
        {
            IList<HeightZone> zones = SelectedHeightZones;
            if (zones.Count == 0)
            {
                return;
            }

            string project = ProjectKey;
            HashSet<string> from = new HashSet<string>(zones.Select(zone => zone.Story), StringComparer.OrdinalIgnoreCase);

            // Top of the building first, the way the level rail reads.
            List<KeyValuePair<string, string>> levels = data.Stories
                .Where(level => level != null && !from.Contains(level.Name))
                .OrderByDescending(level => level.Elevation)
                .Select(level =>
                {
                    int already = heightZones.OnStory(project, level.Name).Count;
                    return new KeyValuePair<string, string>(level.Name,
                        already == 0 ? "" : already + (already == 1 ? " zone already" : " zones already"));
                })
                .ToList();

            LevelChoices choices = new LevelChoices(
                zones.Count == 1 ? "COPY THE ZONE TO" : "COPY " + zones.Count + " ZONES TO",
                "Each level gets a copy of its own, at the same place and height, to change or delete "
                + "separately. A zone already drawn round the same corners on a level is replaced.",
                levels);

            if (AskForLevels == null || !AskForLevels(choices))
            {
                return;
            }

            IList<string> to = choices.Chosen;
            int copies = zones.Sum(zone => heightZones.CopyTo(project, zone.Id, to));

            HeightZonesChanged("Copied " + (zones.Count == 1 ? "the zone" : zones.Count + " zones") + " to "
                               + to.Count + (to.Count == 1 ? " level" : " levels") + " - " + copies
                               + (copies == 1 ? " copy." : " copies."),
                               zones.Select(zone => zone.Id).ToList());
        }

        /// <summary>Takes the zones selected away, after asking.</summary>
        private void DeleteHeightZones()
        {
            IList<HeightZone> zones = SelectedHeightZones;
            if (zones.Count == 0)
            {
                return;
            }

            string what = zones.Count == 1 ? "this height zone" : "these " + zones.Count + " height zones";
            if (MessageBox.Show("Delete " + what + "? The line loads in "
                                + (zones.Count == 1 ? "it" : "them") + " go back to the floor to floor height. "
                                + "This cannot be undone.",
                                "Delete height zones", MessageBoxButton.YesNo, MessageBoxImage.Question,
                                MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            string project = ProjectKey;
            foreach (HeightZone zone in zones)
            {
                heightZones.Remove(project, zone.Id);
            }

            SelectedElement = null;
            HeightZonesChanged((zones.Count == 1 ? "Height zone" : zones.Count + " height zones") + " deleted.",
                               new string[0]);
        }

        /// <summary>
        /// Keeps what was just done to the zones, and puts it into effect: the line loads are
        /// checked again with the zones as they now are - off the loading plans already read, so
        /// without going back to Revit - and the zones changed are left selected.
        ///
        /// Only where the line loads have been checked at all. Before then there is nothing on
        /// the plan to be out of date, and the next run reads the zones as it goes.
        /// </summary>
        private void HeightZonesChanged(string what, IList<string> select)
        {
            SaveUserSettings();

            bool recheck = data.LineLoadResults.Count > 0 && !IsChecking;
            if (recheck)
            {
                CompareLineLoads(reportIssues: false, readAgain: false);
                SettleHandPairings();
                RebuildKind(ElementKind.LineLoad);
            }

            RebuildKind(ElementKind.HeightZone);

            HashSet<string> wanted = new HashSet<string>(select ?? new string[0]);
            List<ElementViewModel> changed = Elements.OfType<HeightZoneElementViewModel>()
                                                     .Where(element => wanted.Contains(element.ZoneId))
                                                     .Cast<ElementViewModel>()
                                                     .ToList();
            if (changed.Count == 1)
            {
                SelectedElement = changed[0];
            }
            else if (changed.Count > 1)
            {
                SelectOnly(null);
                AddToSelection(changed);
            }

            OnPropertyChanged(nameof(HeightZoneToolsText));
            OnPropertyChanged(nameof(HasDecisions));
            OnPropertyChanged(nameof(DecisionsDescription));

            Status = what + (recheck
                ? " The line loads were checked again with it."
                : " The line loads use it the next time they are checked.");
        }

        #endregion

        #region forgetting what was decided by hand

        public ICommand ForgetDecisionsCommand { get; }

        private int HandPairingCount => pairingOverrides.CountFor(ProjectKey);
        private int AcceptedCount => reviewNotes.CountFor(ProjectKey, HumanVerdict.Accepted);
        private int ReviewedCount => reviewNotes.CountFor(ProjectKey, HumanVerdict.Reviewed);
        private int NoteCount => reviewNotes.CountNotes(ProjectKey);
        private int LoadingPlanCount => loadingPlanLinks.CountFor(ProjectKey);
        private int HeightZoneCount => heightZones.CountFor(ProjectKey);

        public bool HasDecisions =>
            HandPairingCount + AcceptedCount + ReviewedCount + NoteCount + LoadingPlanCount + HeightZoneCount > 0;

        /// <summary>What has been decided by hand on this project, for the page the button is on.</summary>
        public string DecisionsDescription
        {
            get
            {
                if (!HasDecisions)
                {
                    return "Nothing has been paired, accepted, reviewed or noted by hand on this project, "
                           + "no level is paired with a loading plan and no height zone is drawn.";
                }

                return $"{HandPairingCount} pairing(s) set by hand, {AcceptedCount} element(s) accepted, "
                       + $"{ReviewedCount} reviewed, {NoteCount} note(s), {LoadingPlanCount} level(s) "
                       + $"paired with a loading plan and {HeightZoneCount} height zone(s) on this project.";
            }
        }

        /// <summary>
        /// Puts up the question of what to forget, and answers whether to go ahead. Set by the
        /// window, which owns the dialog.
        /// </summary>
        public Func<ForgetChoices, bool> AskWhatToForget { get; set; }

        /// <summary>
        /// Forgets, for this project only, whichever of the things decided by hand the person
        /// ticks: the acceptances, the reviews, the notes, the pairings set by hand, the loading
        /// plans the levels were paired with - or everything.
        ///
        /// Asked one by one rather than all at once because they are not worth the same. Taking
        /// the acceptances back after a reissue is routine; losing forty levels of loading plan
        /// pairings along with them is an afternoon's work. Nothing starts ticked.
        ///
        /// The acceptances and reviews come off the plan straight away. The pairings go back to
        /// what the check finds on its own the next time it runs, because re-pairing everything
        /// here would be a run in all but name. The loads read off a forgotten loading plan are
        /// taken off the plan when the settings window closes, as they are when a level's plan
        /// is changed there.
        /// </summary>
        private void ForgetDecisions()
        {
            string project = ProjectKey;

            ForgetChoices choices = new ForgetChoices(AcceptedCount, ReviewedCount, NoteCount,
                                                      HandPairingCount, LoadingPlanCount, HeightZoneCount);
            if (!choices.HasAny || AskWhatToForget == null || !AskWhatToForget(choices))
            {
                return;
            }

            List<string> forgot = new List<string>();
            bool verdictsChanged = false;

            if (choices.IsChosen(ForgetWhat.Acceptances))
            {
                forgot.Add(Counted(reviewNotes.ClearVerdicts(project, HumanVerdict.Accepted), "acceptance"));
                verdictsChanged = true;
            }

            if (choices.IsChosen(ForgetWhat.Reviews))
            {
                forgot.Add(Counted(reviewNotes.ClearVerdicts(project, HumanVerdict.Reviewed), "review"));
                verdictsChanged = true;
            }

            if (choices.IsChosen(ForgetWhat.Notes))
            {
                forgot.Add(Counted(reviewNotes.ClearNotes(project), "note"));
                verdictsChanged = true;
            }

            int pairs = 0;
            if (choices.IsChosen(ForgetWhat.HandPairings))
            {
                pairs = pairingOverrides.ClearProject(project);
                forgot.Add(Counted(pairs, "pairing") + " set by hand");
            }

            int plans = 0;
            if (choices.IsChosen(ForgetWhat.LoadingPlans))
            {
                List<LoadingPlanLink> before = loadingPlanLinks.ForProject(project).ToList();
                plans = loadingPlanLinks.ClearProject(project);
                // The schedule columns said by hand go with the pairings they were said for.
                ScheduleColumnOverrides.Current.ClearProject(project);
                NoteChangedPairings(before);
                forgot.Add("the loading plan pairings of " + Counted(plans, "level"));
            }

            int zones = 0;
            if (choices.IsChosen(ForgetWhat.HeightZones))
            {
                zones = heightZones.ClearProject(project);
                forgot.Add(Counted(zones, "height zone"));
            }

            SaveUserSettings();

            // The zones come off the plan at once, and the line loads that used them go back to
            // their levels' heights - off the plans already read, so without going to Revit.
            if (zones > 0)
            {
                if (data.LineLoadResults.Count > 0 && !IsChecking)
                {
                    CompareLineLoads(reportIssues: false, readAgain: false);
                    RebuildKind(ElementKind.LineLoad);
                }
                RebuildKind(ElementKind.HeightZone);
            }

            if (verdictsChanged)
            {
                RefreshAfterVerdictChange();
            }

            OnPropertyChanged(nameof(HasDecisions));
            OnPropertyChanged(nameof(DecisionsDescription));
            OnPropertyChanged(nameof(LoadingPlanDescription));
            OnPropertyChanged(nameof(ShowsLoadRecheck));
            CommandManager.InvalidateRequerySuggested();

            Status = "Forgot " + JoinWithAnd(forgot) + " on this project."
                     + (pairs > 0 && hasCompared
                         ? " Run the check again to see the pairing it finds on its own."
                         : "")
                     + (plans > 0 && hasCompared
                         ? " The loads read off those plans come off the plan when Set up is closed."
                         : "");
        }

        /// <summary>"1 note", "3 notes".</summary>
        private static string Counted(int count, string thing)
        {
            return count + " " + thing + (count == 1 ? "" : "s");
        }

        /// <summary>"a, b and c".</summary>
        private static string JoinWithAnd(IList<string> parts)
        {
            return parts.Count <= 1
                ? string.Join("", parts)
                : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        #endregion

        #region the project file

        private ICommand exportProjectCommand;
        private ICommand importProjectCommand;

        /// <summary>Writes everything decided by hand on this project to a file - see <see cref="ProjectFile"/>.</summary>
        public ICommand ExportProjectCommand => exportProjectCommand
            ?? (exportProjectCommand = new RelayCommand(_ => ExportProject(), _ => ProjectKey.Length > 0));

        /// <summary>Reads a project file into this project, in place of what it has.</summary>
        public ICommand ImportProjectCommand => importProjectCommand
            ?? (importProjectCommand = new RelayCommand(_ => ImportProject(), _ => ProjectKey.Length > 0));

        /// <summary>The project as a person would name it: the Revit file, without its folder or extension.</summary>
        private string ProjectName
        {
            get
            {
                string key = ProjectKey;
                try
                {
                    return System.IO.Path.GetFileNameWithoutExtension(key);
                }
                catch (ArgumentException)
                {
                    return key;
                }
            }
        }

        private void ExportProject()
        {
            string project = ProjectKey;
            if (project.Length == 0)
            {
                return;
            }

            try
            {
                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export this project's decisions",
                    Filter = "Discrepancy Scanner project (*" + ProjectFile.Extension + ")|*" + ProjectFile.Extension,
                    FileName = ProjectName + ProjectFile.Extension,
                    DefaultExt = ProjectFile.Extension,
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                Dictionary<string, object> file = ProjectFile.Export(CollectView(), project, ProjectName, DateTime.Now);
                System.IO.File.WriteAllText(dialog.FileName, Json.Write(file), System.Text.Encoding.UTF8);

                ProjectFile.Contents contents;
                string problem;
                ProjectFile.TryRead(file, out contents, out problem);
                Status = "Exported to " + dialog.FileName + ": " + string.Join(", ", contents.Lines()) + ".";
            }
            catch (Exception e)
            {
                Status = "The project could not be exported: " + e.Message;
                MessageBox.Show(e.Message, "Export project", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Reads a project file into the project that is open, in place of what it has: the
        /// alignment, the SAFE level, the loading plans, the pairings set by hand, the reviews,
        /// acceptances and notes, and the height zones. Says what the file holds and asks first.
        ///
        /// The acceptances and the zones are on the plan straight away. A pairing is only put
        /// into effect by a run, like any pairing set by hand, and a changed loading plan is
        /// read when Set up is closed, as it is when a level's plan is changed there.
        /// </summary>
        private void ImportProject()
        {
            string project = ProjectKey;
            if (project.Length == 0)
            {
                return;
            }

            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import a project's decisions",
                Filter = "Discrepancy Scanner project (*" + ProjectFile.Extension + ")|*" + ProjectFile.Extension
                         + "|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ProjectFile.Contents contents;
            string problem;
            try
            {
                Dictionary<string, object> file = Json.Read(System.IO.File.ReadAllText(dialog.FileName));
                if (!ProjectFile.TryRead(file, out contents, out problem))
                {
                    MessageBox.Show(problem, "Import project", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("The file could not be read: " + e.Message, "Import project",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string from = contents.Project.Length > 0 ? "\"" + contents.Project + "\"" : "a project";
            string message = "Import what was decided on " + from
                             + (contents.Exported.Length > 0 ? ", exported " + contents.Exported : "")
                             + ", into this project?\n\nThe file holds:\n\n    "
                             + string.Join("\n    ", contents.Lines())
                             + "\n\nThey replace what this project has - a pairing, a note or a zone that is not "
                             + "in the file is gone afterwards. "
                             + (contents.HasAlignment ? "" : "The alignment is left as it is: the file has none. ")
                             + "This cannot be undone; export this project first to keep what it has now.";

            if (MessageBox.Show(message, "Import project", MessageBoxButton.YesNo, MessageBoxImage.Question,
                                MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }

            List<LoadingPlanLink> plansBefore = loadingPlanLinks.ForProject(project).ToList();
            MyTransformation alignmentBefore = DataContainer.ModelTransformation.Copy();

            // Through the settings file's own shape and back, so every store reads the project
            // the way it reads it off disk, and the other projects come back exactly as they were.
            Dictionary<string, object> view = CollectView();
            ProjectFile.Import(view, project, contents.Data);
            view = Json.Read(Json.Write(view));

            loadingPlanLinks.ClearProject(project);
            loadingPlanLinks.ReadFrom(view);
            pairingOverrides.ReadFrom(view);
            reviewNotes.ReadFrom(view);
            heightZones.ReadFrom(view);
            ScheduleColumnOverrides.Current.ClearProject(project);
            ScheduleColumnOverrides.Current.ReadFrom(view);

            string aside = "";
            object alignment;
            if (contents.HasAlignment && Json.Branch(view, ProjectFile.AlignmentBranch).TryGetValue(project, out alignment))
            {
                alignmentsByProject[project] = alignment;
                LoadAlignmentForCurrentProject();
                aside = PutRevitOnImportedAlignment(alignmentBefore);
            }

            NoteChangedPairings(plansBefore);
            SaveUserSettings();

            RefreshAfterVerdictChange();
            RebuildElements();

            OnPropertyChanged(nameof(HasDecisions));
            OnPropertyChanged(nameof(DecisionsDescription));
            OnPropertyChanged(nameof(LoadingPlanDescription));
            OnPropertyChanged(nameof(ShowsLoadRecheck));
            CommandManager.InvalidateRequerySuggested();

            Status = "Imported " + from + ": " + string.Join(", ", contents.Lines()) + "."
                     + (hasCompared && contents.HandPairings > 0
                         ? " Run the check again to put the pairings into effect."
                         : "")
                     + aside;
        }

        /// <summary>
        /// Moves the Revit model already in hand onto an alignment just imported, where it is a
        /// different one - or says it has to be read again, where it cannot be moved. Answers
        /// what to add to the status.
        /// </summary>
        private string PutRevitOnImportedAlignment(MyTransformation before)
        {
            MyTransformation now = DataContainer.ModelTransformation;
            bool same = Math.Abs(before.Translation.X - now.Translation.X) < 1e-6
                        && Math.Abs(before.Translation.Y - now.Translation.Y) < 1e-6
                        && Math.Abs(before.Translation.Z - now.Translation.Z) < 1e-6
                        && Math.Abs(before.Rotation - now.Rotation) < 1e-9
                        && before.IsMirroredAboutX == now.IsMirroredAboutX
                        && before.IsMirroredAboutY == now.IsMirroredAboutY;

            if ((same && RevitNarrowingIsCurrent) || (data.Frames_r.Count == 0 && data.Columns_r.Count == 0 && data.Walls_r.Count == 0))
            {
                return "";
            }

            if (MoveRevitToAlignment())
            {
                return " The Revit model was moved onto the imported alignment - run the check again.";
            }

            Models.Revit.IsStale = true;
            return " Read the Revit model again to put it on the imported alignment.";
        }

        #endregion

        #region pairing the two selected elements

        public ICommand PairSelectionCommand { get; }

        /// <summary>
        /// True when the selection is two elements of one kind that can each take a side in a
        /// pairing. Pairing is a statement about one element and one counterpart, so two is
        /// not a limit on the selection - it is what a selection has to be for this to apply.
        /// </summary>
        private bool SelectionIsTwoOfAKind =>
            selection.Count == 2
            && selection.All(PairingEditor.CanBeMarked)
            && selection[0].Kind == selection[1].Kind;

        /// <summary>
        /// Whether the two selected elements are something that can be paired. A way round
        /// that works is asked of the elements, never of the order they were selected in.
        /// </summary>
        public bool CanPairSelection => PlannedWrites().Count > 0;

        public string PairSelectionText => CanPairSelection
            ? "2 " + KindWord(selection[0].Kind) + " selected"
            : "";

        /// <summary>
        /// What pressing the button would do, said before it is pressed - including which of
        /// the two is about to be recorded as the ETABS side, which is the one thing about a
        /// pairing that is not obvious from looking at the plan.
        /// </summary>
        public string PairSelectionNote
        {
            get
            {
                IList<PairWrite> writes = PlannedWrites();
                if (writes.Count == 0)
                {
                    return "";
                }

                return "ETABS " + writes[0].EtabsUniqueName + " will pair with Revit "
                       + writes[0].RevitId + ".";
            }
        }

        /// <summary>What pairing the selected two would write, and the one place that decides it.</summary>
        private IList<PairWrite> PlannedWrites()
        {
            if (!SelectionIsTwoOfAKind)
            {
                return new List<PairWrite>();
            }

            return TwoWayPairing.Between(
                selection[0].EtabsUniqueName, selection[0].RevitId,
                selection[1].EtabsUniqueName, selection[1].RevitId);
        }

        /// <summary>
        /// Writes the pairing the two selected elements amount to, and re-pairs that kind once.
        /// </summary>
        private void PairSelection()
        {
            IList<PairWrite> writes = PlannedWrites();
            if (writes.Count == 0)
            {
                return;
            }

            ElementKind kind = selection[0].Kind;
            ElementKindKey kindKey = PairKindOf(kind);
            foreach (PairWrite write in writes)
            {
                pairingOverrides.Set(ProjectKey,
                                     PairingOverrides.KeyOf(kindKey, write.EtabsUniqueName),
                                     write.RevitId);
            }

            SaveUserSettings();

            string what = "Paired ETABS " + writes[0].EtabsUniqueName + " with Revit "
                          + writes[0].RevitId + ", by hand.";

            PutHandPairingsIntoEffect(what, new[] { kind });
        }

        #endregion

        #region unpairing what is selected

        public ICommand UnpairSelectionCommand { get; }

        /// <summary>
        /// The selected elements that are a pair and can be taken apart: a beam, a column or a
        /// wall paired with its counterpart. Written from the ETABS side like every pairing, so
        /// it needs the ETABS name - see <see cref="PairingEditor.CanEdit"/>.
        /// </summary>
        private List<ElementViewModel> PairsInSelection =>
            selection.Where(element => element.Source == ElementSource.Paired
                                       && !element.IsDrawnAsContext
                                       && PairingEditor.CanEdit(element))
                     .ToList();

        public bool CanUnpairSelection => PairsInSelection.Count > 0;

        /// <summary>"UNPAIR" on one, and on several how many of the selection it will take apart.</summary>
        public string UnpairSelectionText
        {
            get
            {
                int pairs = PairsInSelection.Count;
                return pairs <= 1 || selection.Count <= 1 ? "UNPAIR"
                     : pairs == selection.Count ? "UNPAIR ALL"
                     : "UNPAIR " + pairs;
            }
        }

        /// <summary>
        /// Takes the selected pairs apart: each ETABS element is recorded as pairing with
        /// nothing, and its kind is paired again once.
        ///
        /// Recorded rather than simply undone, because the pairing pass would otherwise put the
        /// same two straight back together on distance - which is usually why somebody wanted
        /// them apart. The ETABS element then reads as ETABS only and its Revit counterpart is
        /// free for another pairing. It is kept like any hand pairing and undone the same way:
        /// mark the two on the plan and pair them.
        /// </summary>
        private void UnpairSelection()
        {
            List<ElementViewModel> pairs = PairsInSelection;
            if (pairs.Count == 0)
            {
                return;
            }

            string project = ProjectKey;
            foreach (ElementViewModel pair in pairs)
            {
                pairingOverrides.Set(project,
                                     PairingOverrides.KeyOf(PairKindOf(pair.Kind), pair.EtabsUniqueName),
                                     PairingOverrides.Unpaired);
            }

            SaveUserSettings();

            string what = pairs.Count == 1
                ? "Unpaired ETABS " + pairs[0].EtabsUniqueName
                  + (pairs[0].RevitId > 0 ? " from Revit " + pairs[0].RevitId : "") + ", by hand."
                : "Unpaired " + pairs.Count + " " + string.Join(" and ",
                      pairs.Select(pair => pair.Kind).Distinct().Select(KindWord)) + ", by hand.";

            PutHandPairingsIntoEffect(what + " Mark the two on the plan and pair them to put a pair back.",
                                      pairs.Select(pair => pair.Kind));
        }

        #endregion

        /// <summary>
        /// Runs one kind's pairing pass again and puts the results back on the plan, leaving
        /// every other kind - and the slab mesh above all - exactly as the last run left it.
        ///
        /// Deliberately not a run: the run history is not re-recorded, so "new since the last
        /// run" still counts from the last actual run rather than from the last time somebody
        /// corrected a pairing, and the models are not marked as freshly checked. What was
        /// stopped part way through is still stopped.
        ///
        /// The selection goes with it: the pairing is made, the plan shows what came of it, and
        /// the next thing picked is picked fresh.
        /// </summary>
        private void RePairOneKind(ElementKind kind)
        {
            // Columns and walls paired across the kinds take from both kinds' leftovers, so
            // neither can be paired again on its own: the other kind's unpaired lists would
            // still be missing what the last cross pairing took out of them.
            if ((kind == ElementKind.Column || kind == ElementKind.Wall)
                && (PrefrencesData.getPrefrencesData().CrossPairColumnsAndWalls || data.CrossPairs.Count > 0))
            {
                CompareColumns();
                CompareWalls(reportIssues: false);
                CrossPairColumnsAndWalls();

                SettleHandPairings();

                RebuildKind(ElementKind.Column);
                RebuildKind(ElementKind.Wall);
                SelectedElement = null;
                return;
            }

            switch (kind)
            {
                case ElementKind.Beam:
                    CompareBeams();
                    break;
                case ElementKind.Wall:
                    CompareWalls(reportIssues: false);
                    break;
                case ElementKind.LineLoad:
                    // Quietly: the issues are the run's to report, and a pairing set by hand
                    // is not a run.
                    CompareLineLoads(reportIssues: false, readAgain: false);
                    break;
                default:
                    CompareColumns();
                    break;
            }

            SettleHandPairings();

            RebuildKind(kind);
            SelectedElement = null;
        }

        /// <summary>
        /// Saves what the pairing passes just learned about the pairings set by hand - where
        /// the elements in them stand, and which were dropped because one side had moved - and
        /// puts the dropped ones in front of whoever is running the check.
        ///
        /// Said out loud rather than left to be noticed. A dropped pairing is a decision
        /// somebody made being taken back, and an element quietly going back to the pairing
        /// they once corrected looks like the tool forgot rather than like the model changed.
        /// </summary>
        /// <param name="reportThem">
        /// False when the caller shows its own sheet of issues and puts these on it.
        /// </param>
        /// <returns>The pairings dropped, each said in a line.</returns>
        private IList<string> SettleHandPairings(bool reportThem = true)
        {
            IList<string> dropped = pairingOverrides.TakeDropped();

            if (pairingOverrides.TakeChanged())
            {
                SaveUserSettings();
                OnPropertyChanged(nameof(HasDecisions));
                OnPropertyChanged(nameof(DecisionsDescription));
            }

            if (reportThem && dropped.Count > 0)
            {
                ShowIssues?.Invoke("Hand-set pairings dropped",
                    new[] { "These pairings were set by hand, and one side of each is no longer where "
                            + "it was when they were set. They have been forgotten, and the elements "
                            + "paired by distance instead. Pair them again if they still belong together." }
                        .Concat(dropped).ToList());
            }

            return dropped;
        }

        /// <summary>The line the status ends with when pairings were dropped, empty when none were.</summary>
        private static string DroppedPairingsNote(IList<string> dropped)
        {
            return dropped.Count == 0
                ? ""
                : " " + dropped.Count + " pairing(s) set by hand were dropped because an element had moved.";
        }

        /// <summary>
        /// What a kind is called in the plural: "2 line loads selected", "pairing the walls
        /// again". Every kind named, because a fall-through to one of them is how two line loads
        /// came to be called two columns.
        /// </summary>
        private static string KindWord(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Beam: return "beams";
                case ElementKind.Wall: return "walls";
                case ElementKind.Column: return "columns";
                case ElementKind.LineLoad: return "line loads";
                case ElementKind.HeightZone: return "height zones";
                default: return "slab zones";
            }
        }

        private static ElementKindKey PairKindOf(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Beam: return ElementKindKey.Beam;
                case ElementKind.Wall: return ElementKindKey.Wall;
                case ElementKind.LineLoad: return ElementKindKey.LineLoad;
                default: return ElementKindKey.Column;
            }
        }

        #endregion

        /// <summary>
        /// Refreshes the label picker from whatever the visible elements actually publish,
        /// keeping any field the user had already ticked.
        /// </summary>
        private void RefreshLabelFields()
        {
            LabelGroups.Clear();
            foreach (LabelFieldGroup group in LabelGrouping.Build(
                         Elements, LabelGroupTitle, rememberedLabelKeys.Contains,
                         UpdateSelectedLabelKeys))
            {
                LabelGroups.Add(group);
            }

            OnPropertyChanged(nameof(HasLabelFields));
            UpdateSelectedLabelKeys();
        }

        /// <summary>
        /// What a group is called. The slab group is named from the chip that is up rather than
        /// from the kind, because a slab zone publishes its thickness results and its load
        /// results under the same kind, and the two chips are exclusive: whichever is up, the
        /// group is holding that one's fields and heading it "Slabs" would say otherwise.
        /// </summary>
        private string LabelGroupTitle(ElementKind kind)
        {
            string title;
            switch (kind)
            {
                case ElementKind.Column: title = "Columns"; break;
                case ElementKind.Beam: title = "Beams"; break;
                case ElementKind.Wall: title = "Walls"; break;
                case ElementKind.LineLoad: title = "Line loads"; break;
                case ElementKind.HeightZone: title = "Height zones"; break;
                default: title = Findings.IsActive(ViewMode.SlabLoads) ? "Slab loads" : "Slabs"; break;
            }

            // Said, because the raw data offers different things to stamp from the check - what
            // each element was read as rather than what was found - and the same group heading
            // over a different list reads as the list having lost its entries.
            return showRawData ? title + " - raw data" : title;
        }

        private void UpdateSelectedLabelKeys()
        {
            SelectedLabelKeys.Clear();
            foreach (LabelFieldGroup group in LabelGroups)
            {
                foreach (LabelFieldOption option in group.Options)
                {
                    if (option.IsSelected)
                    {
                        SelectedLabelKeys.Add(option.Key);
                        rememberedLabelKeys.Add(option.Key);
                    }
                    else
                    {
                        // Only a row that is actually on screen can be unticked, so a key
                        // belonging to a kind that is not up is left alone rather than
                        // forgotten - it comes back ticked when its chip does.
                        rememberedLabelKeys.Remove(option.Key);
                    }
                }
            }

            OnPropertyChanged(nameof(LabelCountText));
            OnPropertyChanged(nameof(HasLabels));
        }

        #endregion
    }
}
