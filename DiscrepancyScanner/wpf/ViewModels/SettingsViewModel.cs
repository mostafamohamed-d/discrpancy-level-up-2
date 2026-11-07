using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>One group of settings, as the list down the side of the window draws it.</summary>
    public sealed class SettingCategory : ViewModelBase
    {
        private int matchCount;

        public SettingCategory(string name, string blurb, int total, string pane = null,
                               string section = "What counts as a discrepancy")
        {
            Name = name;
            Blurb = blurb;
            matchCount = total;
            Pane = pane;
            Section = section;
        }

        /// <summary>
        /// Set on a row that opens a page of its own rather than a list of settings - the two
        /// models and their alignment, and what the check is run over. Null on every row that
        /// is just settings, which is most of them.
        ///
        /// They are rows in the same rail because that is what they are to the person using
        /// it: the things you set up once and then leave. That they happen to be built out of
        /// buttons rather than out of reflected fields is not a distinction worth showing.
        /// </summary>
        public string Pane { get; }

        public bool IsPane => !string.IsNullOrEmpty(Pane);

        /// <summary>The heading this row sits under in the rail.</summary>
        public string Section { get; }

        public string Name { get; }

        /// <summary>One line saying what the group covers, drawn at the top of the page.</summary>
        public string Blurb { get; }

        /// <summary>How many of its settings answer to the search, for the count on the row.</summary>
        public int MatchCount
        {
            get => matchCount;
            set
            {
                if (SetProperty(ref matchCount, value))
                {
                    OnPropertyChanged(nameof(HasMatches));
                }
            }
        }

        /// <summary>
        /// False dims the row. A page of its own has no settings for a search to find, and
        /// counting that as nothing found left the two pages reached for first drawn as if
        /// they were switched off.
        /// </summary>
        public bool HasMatches => IsPane || matchCount > 0;
    }

    /// <summary>A heading and the rows under it, which is one page or one slice of a search.</summary>
    public sealed class SettingGroup
    {
        public SettingGroup(string header, string blurb, IEnumerable<SettingItem> items)
        {
            Header = header;
            Blurb = blurb;
            Items = new ObservableCollection<SettingItem>(items);
        }

        public string Header { get; }
        public string Blurb { get; }
        public bool HasBlurb => !string.IsNullOrEmpty(Blurb);
        public ObservableCollection<SettingItem> Items { get; }
    }

    /// <summary>
    /// Everything the settings window shows: the settings themselves, split into the groups
    /// the side list walks, and a search that cuts across all of them at once.
    ///
    /// The rows write straight through to <see cref="PrefrencesData"/>, so a change is live the
    /// moment it is typed. What makes Cancel work is the snapshot taken when the window opens:
    /// closing without keeping the changes puts every value back where it was found.
    /// </summary>
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly Dictionary<string, object> snapshot = new Dictionary<string, object>(StringComparer.Ordinal);
        private readonly List<SettingItem> allItems = new List<SettingItem>();
        private readonly Action onChanged;

        private SettingCategory selectedCategory;
        private string searchText = string.Empty;

        public SettingsViewModel(Action onChanged, Func<string> geometryDumpNote)
        {
            this.onChanged = onChanged;

            TakeSnapshot();
            Build(geometryDumpNote);

            // The pages that are not settings lists come first, because they are what is
            // reached for first: which two models, lined up how, checked over what.
            List<SettingCategory> categories = new List<SettingCategory>
            {
                new SettingCategory("Models & alignment",
                    "Which two models are being compared, and how the Revit one is turned and moved to land "
                    + "on the ETABS one. Done once and then left.",
                    0, pane: "models", section: "Once per project"),
                new SettingCategory("What is checked",
                    "Which levels the slab check runs over, and whether the loads are checked against the "
                    + "loading plan at all.",
                    0, pane: "scope", section: "Once per project")
            };

            categories.AddRange(
                allItems.Select(item => item.Category)
                        .Distinct()
                        .Select(name => new SettingCategory(name, Blurbs[name],
                                                            allItems.Count(item => item.Category == name),
                                                            section: SectionOf(name))));

            Categories = new ObservableCollection<SettingCategory>(categories);

            // Grouped here rather than with a CollectionViewSource in the XAML: one declared in
            // Window.Resources has no reliable DataContext to bind its Source to, and the rail
            // silently comes back flat when it fails.
            CategoriesView = CollectionViewSource.GetDefaultView(Categories);
            CategoriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SettingCategory.Section)));

            Groups = new ObservableCollection<SettingGroup>();
            selectedCategory = Categories.FirstOrDefault();

            RestoreDefaultsCommand = new RelayCommand(_ => RestoreDefaults());
            SaveAsDefaultsCommand = new RelayCommand(_ => SaveAsDefaults());
            ForgetDefaultsCommand = new RelayCommand(_ => ForgetDefaults(),
                                                     _ => UserSettings.HasSavedDefaults);
            ClearSearchCommand = new RelayCommand(_ => SearchText = string.Empty,
                                                  _ => !string.IsNullOrEmpty(searchText));

            RefreshGroups();
        }

        #region what is on screen

        public ObservableCollection<SettingCategory> Categories { get; }

        /// <summary>The pages being drawn: one when browsing, one per matching group when searching.</summary>
        public ObservableCollection<SettingGroup> Groups { get; }

        public SettingCategory SelectedCategory
        {
            get => selectedCategory;
            set
            {
                if (SetProperty(ref selectedCategory, value))
                {
                    OnPropertyChanged(nameof(RestoreScope));
                    OnPropertyChanged(nameof(ShowsSettings));
                    OnPropertyChanged(nameof(ShowsModels));
                    OnPropertyChanged(nameof(ShowsScope));
                    if (!IsSearching)
                    {
                        RefreshGroups();
                    }
                }
            }
        }

        /// <summary>
        /// Narrows every group at once. Matched on the label, the description, the unit and the
        /// name of the underlying field, so both "500" kinds of user find the tolerance they
        /// are after - the one who knows it as "plan buffer" and the one who knows it as mm.
        /// </summary>
        public string SearchText
        {
            get => searchText;
            set
            {
                if (SetProperty(ref searchText, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(IsSearching));
                    OnPropertyChanged(nameof(RestoreScope));
                    RefreshGroups();
                }
            }
        }

        public bool IsSearching => searchText.Trim().Length > 0;

        /// <summary>Shown in place of the rows when a search matches nothing.</summary>
        public bool HasNoMatches => Groups.Count == 0;

        public string ChangedSummary
        {
            get
            {
                int changed = allItems.Count(item => item.IsChanged);
                string against = UserSettings.HasSavedDefaults ? "your default" : "the default";
                return changed == 0
                    ? (UserSettings.HasSavedDefaults
                        ? "Every setting is at your saved default."
                        : "Every setting is at its default.")
                    : $"{changed} setting{(changed == 1 ? " is" : "s are")} away from {against}.";
            }
        }

        private void RefreshGroups()
        {
            string term = searchText.Trim();

            Groups.Clear();

            foreach (SettingCategory category in Categories)
            {
                List<SettingItem> matches = allItems
                    .Where(item => item.Category == category.Name && item.Matches(term))
                    .ToList();

                category.MatchCount = matches.Count;

                bool wanted = IsSearching ? matches.Count > 0 : category == selectedCategory;
                if (wanted)
                {
                    Groups.Add(new SettingGroup(category.Name, category.Blurb, matches));
                }
            }

            OnPropertyChanged(nameof(HasNoMatches));
            OnPropertyChanged(nameof(ChangedSummary));
        }

        #endregion

        #region commands and the snapshot

        public ICommand RestoreDefaultsCommand { get; }
        public ICommand SaveAsDefaultsCommand { get; }
        public ICommand ForgetDefaultsCommand { get; }
        public ICommand ClearSearchCommand { get; }

        /// <summary>
        /// Makes the settings as they stand the ones every project starts from, and the ones
        /// the restore button puts back.
        ///
        /// The preferences were always one file for the user rather than one per project, so
        /// nothing is being copied anywhere - what this changes is what counts as "default",
        /// which until now was only ever the values compiled into the tool.
        /// </summary>
        private void SaveAsDefaults()
        {
            UserSettings.SaveAsDefaults(PrefrencesData.getPrefrencesData(), null);
            RefreshDefaultsState();
        }

        /// <summary>Drops the saved defaults so the ones the tool ships with stand again.</summary>
        private void ForgetDefaults()
        {
            UserSettings.ForgetSavedDefaults(PrefrencesData.getPrefrencesData(), null);
            RefreshDefaultsState();
        }

        /// <summary>
        /// Every row works out for itself whether it is at its default, and what its default
        /// is, so changing what "default" means has to be pushed through all of them.
        /// </summary>
        private void RefreshDefaultsState()
        {
            foreach (SettingItem item in allItems)
            {
                item.RefreshDefault();
            }
            OnPropertyChanged(nameof(ChangedSummary));
            OnPropertyChanged(nameof(DefaultsSummary));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>What the footer says about the saved defaults.</summary>
        public string DefaultsSummary => UserSettings.HasSavedDefaults
            ? "Your saved defaults are what new projects start from and what Restore puts back."
            : "No defaults of your own saved yet, so Restore puts back the ones the tool ships with.";

        /// <summary>Puts back the values the tool ships with, for whatever is on screen.</summary>
        private void RestoreDefaults()
        {
            foreach (SettingItem item in Groups.SelectMany(group => group.Items).ToList())
            {
                item.RestoreDefault();
            }
            OnPropertyChanged(nameof(ChangedSummary));
        }

        // Which of the three things the right-hand side is showing. Written as three flags
        // rather than one enum because that is what a XAML Visibility binding can read without
        // a converter, and this project's markup compiler cannot take a local converter type.

        public bool ShowsSettings => selectedCategory == null || !selectedCategory.IsPane;
        public bool ShowsModels => selectedCategory != null && selectedCategory.Pane == "models";
        public bool ShowsScope => selectedCategory != null && selectedCategory.Pane == "scope";

        /// <summary>
        /// The check window this was opened from, so the pages that are not settings lists can
        /// reach the import commands and the alignment. A settings page is part of the same
        /// session as the window that opened it, not a thing on its own.
        /// </summary>
        public ComparisonViewModel Owner { get; set; }

        /// <summary>Set by the window, so a button in it can close it on the way somewhere else.</summary>
        public Action RequestClose { get; set; }

        /// <summary>
        /// True when this window was closed by way of the alignment button rather than by OK
        /// or Cancel, which is what tells the caller to open the alignment window next.
        /// </summary>
        public bool AlignmentRequested { get; private set; }

        private ICommand openAlignmentCommand;

        /// <summary>
        /// Leaves here for the alignment window, and leaving is the point: this window is
        /// shown as a dialog, and a dialog disables every window belonging to the process -
        /// Revit's own included - so nothing opened from underneath it could ever pick a
        /// point in Revit.
        /// </summary>
        public ICommand OpenAlignmentCommand
        {
            get
            {
                return openAlignmentCommand ?? (openAlignmentCommand = new RelayCommand(_ =>
                {
                    AlignmentRequested = true;
                    RequestClose?.Invoke();
                }));
            }
        }

        /// <summary>The rail's rows, grouped under their section headings.</summary>
        public ICollectionView CategoriesView { get; }

        /// <summary>How the restore button describes what it is about to do.</summary>
        public string RestoreScope => IsSearching ? "the settings shown" : selectedCategory?.Name ?? "these settings";

        private void TakeSnapshot()
        {
            PrefrencesData live = PrefrencesData.getPrefrencesData();
            foreach (FieldInfo fieldInfo in UserSettings.PreferenceFields())
            {
                snapshot[fieldInfo.Name] = fieldInfo.GetValue(live);
            }
        }

        /// <summary>Puts every setting back to what it was when the window opened.</summary>
        public void Revert()
        {
            PrefrencesData live = PrefrencesData.getPrefrencesData();
            foreach (FieldInfo fieldInfo in UserSettings.PreferenceFields())
            {
                object value;
                if (snapshot.TryGetValue(fieldInfo.Name, out value))
                {
                    fieldInfo.SetValue(live, value);
                }
            }

            foreach (SettingItem item in allItems)
            {
                item.Reread();
            }

            onChanged?.Invoke();
        }

        /// <summary>True while a box is holding something that does not read as a number.</summary>
        public bool HasErrors => allItems.Any(item => item.HasError);

        /// <summary>The labels of the boxes that do not read, for the message on the way out.</summary>
        public IEnumerable<string> ErrorLabels => allItems.Where(item => item.HasError).Select(item => item.Label);

        #endregion

        #region the catalogue

        private static readonly Dictionary<string, string> Blurbs = new Dictionary<string, string>
        {
            {
                "Revit parameters",
                "Which parameters are read off the Revit elements. Re-import the Revit model for a change here to take effect."
            },
            {
                "Beam pairing",
                "Which Revit beams are collected at all, and how an ETABS frame and a Revit beam are decided to be the same beam. Applied on the next comparison, except where a row says the Revit model has to be read again."
            },
            {
                "Column check",
                "What ETABS reads back as a column at all, how an ETABS and a Revit column are paired, and how far apart their sections may be. Applied on the next comparison, except where a row says the ETABS model has to be read again."
            },
            {
                "Wall check",
                "Which Revit walls are read as structure, how an ETABS wall panel and a Revit wall are decided to be the same wall, and how far apart they may be. Applied on the next comparison, except where a row says the Revit model has to be read again."
            },
            {
                "Slab check",
                "Which Revit floors are read as slab, and how far the two models may differ before a piece is coloured red."
            },
            {
                "Slab loads",
                "How the area loads on an ETABS shell are matched against the loading plan and its schedule. The plan itself is picked on the check panel, not here."
            },
            {
                "Line loads",
                "How the line loads drawn on the loading plan as detail lines are matched against the distributed loads on the ETABS frames, and how the schedule's load on elevation is turned into a load per metre run. The plan and the sheet are picked on the check panel, not here."
            },
            {
                "SIDL_C Line",
                "How the SIDL_C line loads on the ETABS frames are checked against the weight of the Revit walls and fill strips the wall and slab checks leave out - upstands, planters, fill along an edge. Read like the line loads, frame by frame; the Revit side's value is the element's own weight. The names are read with the Revit model, so a change to them needs the Revit model imported again."
            },
            {
                "SIDL_C Area",
                "How the SIDL_C area loads on the ETABS slabs are checked against the weight of the Revit light weight fill floors over them. A ramp of fill is averaged by the engineer - whole, or in pieces - and the check reads the average back off the slabs under the fill, area by area, so it does not matter how many pieces it was split into. The names are read with the Revit model, so a change to them needs the Revit model imported again."
            },
            {
                "Single level models",
                "How a SAFE model - one floor, with nothing in it that says which floor - is placed against the Revit building. Which level it stands for is the Revit level its story is paired with in the alignment; this is how much of the Revit model counts as that level."
            },
            {
                "Diagnostics",
                "What the tool records about its own run, for when a result needs explaining."
            },
            {
                KeyboardShortcuts,
                "The keys that work anywhere in the check window, except while something is being typed. "
                + "Click a box and press the key you want, with Ctrl, Shift or Alt held if you want them "
                + "too. For two keys one after the other - A, A - press the second straight after the "
                + "first. Backspace or Delete takes the key away. The digits along the top and the ones "
                + "on the number pad count as the same key."
            }
        };

        private const string KeyboardShortcuts = "Keyboard shortcuts";

        /// <summary>
        /// The heading a group of settings sits under in the rail. Everything is about what
        /// counts as a discrepancy except the keys, which are about working the window.
        /// </summary>
        private static string SectionOf(string category)
        {
            return category == KeyboardShortcuts ? "Working the window" : "What counts as a discrepancy";
        }

        private void Build(Func<string> geometryDumpNote)
        {
            const string revit = "Revit parameters";
            const string beams = "Beam pairing";
            const string columns = "Column check";
            const string walls = "Wall check";
            const string slabs = "Slab check";
            const string loads = "Slab loads";
            const string lineLoads = "Line loads";
            const string sidlC = "SIDL_C Line";
            const string sidlCArea = "SIDL_C Area";
            const string singleLevel = "Single level models";
            const string diagnostics = "Diagnostics";

            Add(singleLevel, "SingleLevelBand", "How far off the level still counts as it",
                "How far a Revit element may stand from the paired level and still be compared "
                + "against the SAFE model, which holds that one floor. A floor is not drawn on its "
                + "level - a slab hangs below it, a beam is dropped for an upstand, a column stops "
                + "short - so the test is how far off it stands rather than whether it sits on it. "
                + "What this must not reach is the floor above or below: anything approaching a "
                + "storey height pulls in the next floor and compares it against this one. Only "
                + "used for SAFE; an ETABS model says what floor everything is on.",
                SettingItem.OnRevitImport,
                unit: "mm", keywords: "safe single level band tolerance floor storey narrow");

            // Revit parameters -------------------------------------------------------------

            Add(revit, "ColumnDiameterParameterNames", "Column diameter (round)",
                "The parameter mapped to a round column's diameter, for example D. " + MappingNote
                + " A column whose diameter is read is round; otherwise width and depth make it rectangular.",
                SettingItem.OnRevitImport,
                keywords: "circular round dia section mapping parameter type instance");

            Add(revit, "ColumnWidthParameterNames", "Column width (rectangular)",
                "The parameter mapped to a rectangular column's width, for example b. " + MappingNote,
                SettingItem.OnRevitImport,
                keywords: "rectangular b section mapping parameter type instance");

            Add(revit, "ColumnDepthParameterNames", "Column depth (rectangular)",
                "The parameter mapped to a rectangular column's depth, for example h. " + MappingNote,
                SettingItem.OnRevitImport,
                keywords: "rectangular h d section height mapping parameter type instance");

            Add(revit, "SwapRevitColumnWidthDepth", "Revit width lines up with the ETABS depth",
                "The Revit rectangle family measures Width across the family X axis while ETABS "
                + "measures T3 across its local 3 axis, so at the same rotation the two line up "
                + "crossed. Untick where the family is authored the other way round.",
                SettingItem.OnComparison,
                keywords: "swap t2 t3 orientation rotate section");

            Add(revit, "ColumnTypeMarkParameterNames", "Column type mark",
                "The mark the ETABS frame group assignments are checked against. Searched in order "
                + "on each column type, then on the instance.",
                SettingItem.OnRevitImport,
                keywords: "group mark type");

            Add(revit, "BeamWidthParameterNames", "Beam width",
                "The parameter mapped to a beam's width, for example b. " + MappingNote,
                SettingItem.OnRevitImport,
                keywords: "beam b section mapping parameter type instance");

            Add(revit, "BeamDepthParameterNames", "Beam depth",
                "The parameter mapped to a beam's depth, for example h. " + MappingNote,
                SettingItem.OnRevitImport,
                keywords: "beam h d section height mapping parameter type instance");

            Add(revit, "MeasureSizeFromGeometry", "Measure the size when no parameter matches",
                "A beam or column whose family uses none of the names above has its width and depth "
                + "measured off its own solid instead, so any model can be read whatever its families "
                + "call their parameters. Where both are there they are compared, and the members whose "
                + "parameters disagree with their geometry are listed in the import report.",
                SettingItem.OnRevitImport,
                keywords: "beam column width depth size geometry measure solid parameter missing");

            Add(revit, "WallTypeMarkParameterNames", "Wall type mark",
                "Searched in order on each wall type, then on the wall. Read and reported rather "
                + "than checked: nothing is compared against it yet, and it is what the wall "
                + "reinforcement check will be keyed on.",
                SettingItem.OnRevitImport,
                keywords: "wall mark type name");

            Add(revit, "BeamMarkParameterNames", "Beam reinforcement mark",
                "The first one a beam carries is shown as its Beam Reinf. Mark and is what goes out "
                + "in the Excel export.",
                SettingItem.OnRevitImport,
                keywords: "rebar reinforcement export excel");

            // Beam pairing -----------------------------------------------------------------

            Add(beams, "BeamTypeNamesToIgnore", "Leave out beams whose type name contains",
                "Comma separated names. A Revit beam whose type is named with any of them is not "
                + "collected at all: it is not paired, not checked, not drawn on the plan and not "
                + "in the Excel export. Matched anywhere in the name and without regard to case, "
                + "and a name may have a space in it, which is why these are separated with commas "
                + "rather than spaces. This is for the beams Revit draws that ETABS does not "
                + "frame: they have nothing to be compared against, so left in they read as beams "
                + "ETABS is missing, every run. \"RB\" ships as the default - two letters matched "
                + "anywhere, so it takes a KERB or a CURB type with it, and \"RB-\" is no escape "
                + "either since KERB-200 carries that too; naming the series itself, RB1, is. The "
                + "import says how many beams each name took and which name it was. Leave empty to "
                + "collect every beam.",
                SettingItem.OnCheckOrRevitImport,
                keywords: "filter exclude ignore rib RB kerb curb secondary type name");

            Add(beams, "BeamMarksToIgnore", "Leave out beams whose mark contains",
                "The same, against the beam's reinforcement mark rather than its type name. The "
                + "mark is the one read through the beam reinforcement mark parameters under Revit "
                + "parameters - the type parameter is searched before the instance - so this leaves "
                + "out exactly the beams that would have been reported under that mark. Kept apart "
                + "from the type name list because a project that carries the distinction in the "
                + "mark rather than in the type name wants one of these and not the other. Leave "
                + "empty to collect every beam.",
                SettingItem.OnCheckOrRevitImport,
                keywords: "filter exclude ignore rib RB reinforcement mark instance parameter");

            Add(beams, "EtabsBeamSectionsToIgnore", "Leave out ETABS beams whose section contains",
                "Comma separated section names. An ETABS frame drawn with any of them - a line "
                + "drawn only to hang a line load on - is not a beam, so it is left out of the beam "
                + "check: not paired, not reported, not drawn once the check has run. Its loads are "
                + "still read, so the line load checks are unchanged. Matched anywhere in the name "
                + "and without regard to case. Applied when the check runs, so a saved design model "
                + "file needs no new read. Frames on the None section are left out already.",
                SettingItem.OnComparison,
                keywords: "etabs line load frame section exclude ignore dummy null beam");

            Add(beams, "EtabsSectionsToSkip", "Don't read ETABS frames whose section contains",
                "Comma separated section names. Frames on any of them - dummies, rigid links, steel "
                + "sections - are not read at all, beams and columns alike, line loads included. "
                + "Matched anywhere in the name without regard to case. Empty reads every frame.",
                SettingItem.OnEtabsImport,
                keywords: "etabs frame section skip ignore dummy rigid steel dum rig clad shs rhs ipe");

            Add(beams, "EtabsMaterialsToSkip", "Don't read ETABS frames whose material contains",
                "Comma separated material names, matched the same way. Empty reads every material.",
                SettingItem.OnEtabsImport,
                keywords: "etabs frame material skip ignore dummy other");

            Add(beams, "CompareCurvedBeamsByChord", "Compare curved beams by their chord",
                "An ETABS frame is straight. On, a curved Revit beam is compared by the straight line "
                + "between its ends and listed in the import report. Off, curved beams are left out.",
                SettingItem.OnRevitImport,
                keywords: "arc curve curved spline beam chord skip");

            Add(beams, "UseEndpointPairing", "Pair beams on their end points",
                "Beams pair when both ends line up, either way round, and the nearest one wins. "
                + "Angle and length come out of the two ends, so they have no buffer of their own, "
                + "and ETABS frames are never merged to match one Revit beam. Untick to pair on "
                + "midpoints instead, the way the tool used to.",
                SettingItem.OnComparison,
                keywords: "endpoint midpoint match");

            Add(beams, "BeamPlanTolerance", "End point buffer in plan",
                "How far a Revit beam end may sit from the ETABS end in plan and still be the same "
                + "end. Measured as a radius, so it reads the same in every direction. End point "
                + "pairing only.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance xy radius endpoint");

            Add(beams, "BeamVerticalTolerance", "End point buffer in level",
                "The same, measured up and down. Kept apart from the plan buffer because a model is "
                + "usually far better placed in plan than in level, where a structural depth or an "
                + "offset moves a beam. End point pairing only.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance z height endpoint");

            Add(beams, "LocationTolerance", "Midpoint buffer",
                "Midpoint offset allowed on each axis. Midpoint pairing only - the comparison widens "
                + "up to this in ten steps, taking the closest beams first.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance midpoint location");

            Add(beams, "LengthTolerance", "Length buffer",
                "Length difference allowed between two beams paired on their midpoints. Midpoint "
                + "pairing only.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance midpoint length");

            // Which checks run -------------------------------------------------------------
            //
            // Each of these sits at the top of the page for the check it governs, above the
            // tolerances that check is run to - because whether a question is asked at all
            // comes before how tightly it is judged, and a page of buffers for a check that is
            // switched off would otherwise read as live.

            const string switchNote =
                "Switch off and this check publishes nothing at all: no colour on the plan, no "
                + "count on the chips, no row on the element card. It is not reported as passing, "
                + "because it was not made.";

            Add(columns, "ColumnTypeNamesToIgnore", "Leave out Revit columns whose name contains",
                "Comma separated names, matched in the family and the type name without regard to "
                + "case. For tapered columns, whose size changes up the height: Varies by default.",
                SettingItem.OnRevitImport,
                keywords: "column family type skip ignore tapered varies");

            Add(columns, "RunColumnLocationCheck", "Check where the column stands",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run location position off on");
            Add(columns, "RunColumnOrientationCheck", "Check how the column is turned",
                switchNote + " Worth switching off where the Revit columns are not rotated to match "
                + "the design.",
                SettingItem.OnComparison,
                keywords: "enable disable run orientation rotation off on");
            Add(columns, "RunColumnShapeCheck", "Check the section shape",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run shape round rectangular off on");
            Add(columns, "RunColumnDimensionsCheck", "Check the section size",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run dimensions size off on");
            Add(columns, "RunColumnTypeMarkCheck", "Check the type mark against the ETABS groups",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run type mark group off on");

            Add(beams, "RunBeamMarkCheck", "Check the beam reinforcement mark",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run beam mark reinforcement off on");

            Add(beams, "BeamSizeTolerance", "Beam size buffer",
                "How far a paired beam's width or depth may differ between ETABS and Revit and "
                + "still count as the same section.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance size section width depth height dimension");

            Add(walls, "RunWallLocationCheck", "Check the wall is on the line",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run wall location line off on");
            Add(walls, "RunWallThicknessCheck", "Check the wall thickness",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run wall thickness off on");

            Add(walls, "RunWallLengthCheck", "Check how far the wall runs",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run wall length coverage short off on");

            Add(slabs, "RunSlabThicknessCheck", "Check the slab thickness",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run slab thickness depth off on");
            Add(slabs, "RunSlabSystemCheck", "Check PT against RC",
                "Reads each slab as post-tensioned or reinforced from its name, on the same pieces "
                + "the thickness is checked on: a PT word below anywhere in the ETABS property or "
                + "the Revit floor type is PT, otherwise an RC word is RC. Where either name has "
                + "neither, that piece is not checked. " + switchNote,
                SettingItem.OnComparison,
                keywords: "enable disable run slab pt rc post tensioned reinforced system type off on");
            Add(slabs, "PtSlabNames", "Names that mean post-tensioned",
                "Comma separated, matched anywhere in the slab or floor type name without regard to "
                + "case. Asked first.",
                SettingItem.OnComparison,
                keywords: "slab pt post tensioned name");
            Add(slabs, "RcSlabNames", "Names that mean reinforced",
                "Comma separated, matched the same way, once the name is not PT. \"S\" catches "
                + "S200 and SLAB. A drop panel named DP7 has neither and is not checked.",
                SettingItem.OnComparison,
                keywords: "slab rc reinforced name drop panel");
            Add(slabs, "RunSlabOutlineCheck", "Check the slab outlines agree",
                switchNote + " This is the one that reports slab in ETABS with no Revit floor over "
                + "it, and floor in Revit with no ETABS shell under it.",
                SettingItem.OnComparison,
                keywords: "enable disable run slab outline edge missing off on");
            Add(slabs, "RunSlabOpeningCheck", "Check the openings agree",
                switchNote, SettingItem.OnComparison,
                keywords: "enable disable run slab opening hole void off on");

            // Column check -----------------------------------------------------------------
            //
            // The lean comes first because it settles what the rest of this page is even about:
            // every buffer below is applied to the frames this row let through as columns, and
            // a frame it hands to the beams instead is never weighed against any of them.

            Add(columns, "ColumnMaxLeanFromVertical", "Lean allowed before it is a beam",
                "How far a frame may lean off plumb and still be read as a column when the ETABS "
                + "model is imported. ETABS hands back where a frame's two ends are and nothing "
                + "saying whether it meant a column or a beam, so the import decides from the ends. "
                + "It used to decide on the rise alone - ends more than 50mm apart in level meant "
                + "column - which reads every inclined beam as a column: a ramp beam, a stair beam, "
                + "a roof beam, a six metre beam falling sixty millimetres to a drain. A rise says "
                + "nothing without the plan run it is spread over, so the two are now measured "
                + "together as an angle, and this is where that angle is cut. Thirty degrees sits "
                + "in the gap the two kinds of member leave: a raking column leans ten or twenty, a "
                + "stair flight sixty or more. Raise it, up to 60, for a model whose columns rake "
                + "hard; lower it towards nought where the columns are all plumb and a steep beam "
                + "is being taken for one. A frame whose ends are within 50mm of each other in "
                + "level is a beam whatever this says.",
                SettingItem.OnEtabsImport,
                unit: "deg", keywords: "column beam inclined sloped raking ramp stair brace "
                    + "vertical plumb angle classify detection");

            Add(columns, "ColumnLocationTolerance", "Location buffer",
                "Plan buffer inside which an ETABS and a Revit column count as the same column.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance position plan");

            Add(columns, "ColumnSearchRadius", "Search radius",
                "How far the pairing pass reaches to find a counterpart once the location buffer has "
                + "failed. Anything found between the buffer and this radius is reported as a "
                + "location mismatch rather than as an unmatched column.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance pairing unmatched");

            Add(columns, "ColumnOrientationTolerance", "Orientation buffer",
                "How far a column may be turned from the ETABS one and still count as correctly "
                + "rotated.",
                SettingItem.OnComparison,
                unit: "deg", keywords: "tolerance rotation angle");

            Add(columns, "ColumnDimensionTolerance", "Section dimension buffer",
                "How far the width, the depth or the diameter may differ and still count as the "
                + "same section.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance size width depth diameter");

            Add(columns, "DrawBothColumnsOnMismatch", "Draw both columns where they differ",
                "The plan draws a paired column once, as the Revit section, coloured by how it "
                + "did. That says a column is wrong without saying how: a column 300mm out of "
                + "place, one turned thirty degrees and one 100mm too deep are all the same red "
                + "rectangle. Tick this and the ETABS section is drawn underneath it as a dark "
                + "outline wherever the place, the rotation, the shape or the size failed, so "
                + "the difference is the gap between the two rather than something to be read "
                + "off the card. Only where a check failed, and only those four - a type mark "
                + "against its groups is not a difference two outlines could show, and a column "
                + "that passed would be two outlines drawn on top of each other. The outline "
                + "carries no verdict of its own - it is not counted, not labelled and not "
                + "selectable, because the column drawn over it already answers for both.",
                SettingItem.OnRedraw,
                keywords: "draw plan overlay revit etabs on top of both outline ghost compare "
                    + "size location rotation orientation shape offset see difference");

            Add(columns, "EtabsGroupNamePatterns", "Group names must contain",
                "Only groups whose name carries one of these take part in the type mark check. "
                + "Comma separated, matched without regard to case, and whitespace is ignored on "
                + "both sides, so T - 1 reads as T-1. Every other group is left out, so it neither "
                + "matches a column nor counts as an extra group. Leave empty to use every group.",
                SettingItem.OnComparison,
                keywords: "etabs group type mark filter");

            // Wall check -------------------------------------------------------------------

            Add(walls, "WallLocationTolerance", "Line buffer",
                "How far an ETABS wall panel may stand off the line its Revit wall runs along and "
                + "still count as being in the right place. Measured at both ends of the panel, so "
                + "this covers a wall moved sideways and a wall drawn at an angle to the other "
                + "model's with the one number.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance position plan offset");

            Add(walls, "WallThicknessTolerance", "Thickness buffer",
                "How far the two wall thicknesses may differ and still count as the same wall.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance width");

            Add(walls, "WallSearchRadius", "Search radius",
                "How far the pairing reaches to find a counterpart once the line buffer has failed. "
                + "A wall found between the buffer and this is reported as being in the wrong place "
                + "rather than as a wall the other model has not got.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance pairing unmatched");

            Add(walls, "WallAngleTolerance", "Out of parallel buffer",
                "How far out of parallel two walls may be and still be the same wall. Folded into "
                + "0 to 90 degrees, so a wall drawn from the other end reads as the same wall.",
                SettingItem.OnComparison,
                unit: "deg", keywords: "tolerance angle rotation parallel");

            Add(walls, "WallMinimumOverlap", "Must run alongside for",
                "How much of two walls has to run alongside each other before they can be paired - "
                + "never more than half the length of the shorter one, so a short panel is not held "
                + "to a length it has not got. This is what tells a wall from the wall on the other "
                + "side of a corridor running parallel to it.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance pairing overlap");

            Add(walls, "WallLengthTolerance", "Warn when ETABS stops short by",
                "The plan draws the ETABS walls, so a Revit wall that ETABS only half covers would "
                + "be half a wall on the drawing with nothing to say the other half exists. Rather "
                + "than drawing both models over each other, a wall ETABS stops short of by more "
                + "than this carries a warning saying how far it actually runs. Panels stacked up "
                + "the height of a wall lie along the same stretch and count once.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "length coverage warning short partial plan");

            Add(walls, "WallUncoveredDrawLength", "Draw the missing stretch when it is over",
                "Where ETABS misses more of a Revit wall than this, the stretch it misses is drawn "
                + "on the plan as a piece of Revit wall, on top of the warning. Only the missing "
                + "stretch is drawn, never the whole wall, so nothing is laid over the ETABS "
                + "panels that are there. Keep it at or above the warning buffer, or walls would "
                + "be drawn that were never warned about.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "drawing plan uncovered gap missing revit");

            Add(walls, "CrossPairColumnsAndWalls", "Pair columns with walls across the two models",
                "Tick this where a wall in Revit is modelled as a column in ETABS, or a Revit column "
                + "as a wall. Whatever the column and the wall checks leave unpaired is then offered "
                + "to the other kind: an ETABS column with a Revit wall standing on it, or an ETABS "
                + "wall with a Revit column on it, is paired instead of both being reported as "
                + "missing. The pair is judged the way walls are - on the same line within the line "
                + "buffer, the column's short side against the wall's thickness, and its long side "
                + "against the wall's length within the length buffer - and drawn where ETABS has "
                + "it: with the columns when ETABS has a column, with the walls when ETABS has a "
                + "wall. Its card says what the other model has it as. A column somebody set by hand "
                + "to pair with nothing stays unpaired.",
                SettingItem.OnComparison,
                keywords: "cross column wall blade stub pier modelled as pair detect mixed kind");

            Add(walls, "StructuralWallsOnly", "Structural walls only",
                "Leave out Revit walls that Revit does not mark as bearing or shear walls, which is "
                + "how a partition is kept from reading as a wall ETABS is missing.",
                SettingItem.OnRevitImport,
                keywords: "partition bearing shear filter");

            Add(walls, "WallMinimumLength", "Shortest wall worth checking",
                "Revit walls shorter than this are read as jambs, fins and leftover slivers rather "
                + "than as walls, and are left out of the check.",
                SettingItem.OnRevitImport,
                unit: "mm", keywords: "filter short jamb fin");

            Add(walls, "WallTypeNamesToIgnore", "Leave out walls whose name contains",
                "Comma separated names. A Revit wall whose name carries any of them is left out of "
                + "the wall check altogether - both the wall's own name and its type name are "
                + "looked at. Matched anywhere in the name and without regard to case, so blockwork "
                + "catches Blockwork-200 and upstand catches Upstand Wall 200. A name may have a "
                + "space in it, which is why these are separated with commas rather than spaces: "
                + "Second Cast is one name to leave out, not two. Leave empty to check every wall "
                + "that gets past the other filters.",
                SettingItem.OnCheckOrRevitImport,
                keywords: "filter partition cladding upstand second cast type name");

            Add(walls, "ReadWallThicknessFromStructuralLayers", "Read thickness from the structural layers",
                "Take a wall's thickness from the layers its type marks as structure rather than from "
                + "its whole width. A wall type carries its finishes as layers too, and the whole "
                + "width reads those as structure - a 200 wall with 20 of plaster either side reads "
                + "as 240 and fails against the ETABS 200 every time. A type that marks nothing as "
                + "structure gets its whole width either way.",
                SettingItem.OnRevitImport,
                keywords: "thickness layers finish plaster type");

            Add(walls, "SnapWallsToCorners", "Read walls as running to the corner",
                "Where two Revit walls meet, read both of them as running to the point their centre "
                + "lines cross rather than to wherever they were drawn to. Revit leaves the corner "
                + "to whoever drew it - one wall taken the full width and the other butted into its "
                + "face, both mitred to meet, one run through to the far face - and ETABS meshes the "
                + "joint on the crossing whatever the drawing did. Left alone that is half a wall "
                + "thickness of length and of position out at every corner in the building, none of "
                + "it a difference worth reporting. Only ends already at a corner move, and only "
                + "onto it; a wall that stops well short of the one it should meet is left where it "
                + "was drawn.",
                SettingItem.OnRevitImport,
                keywords: "corner junction intersection length trim extend centre line");

            Add(walls, "WallCornerBuffer", "Corner buffer",
                "How far past half the other wall's thickness the corner search reaches. The half "
                + "thickness is the convention itself - a wall butted into another one's face stops "
                + "exactly that far short of the crossing, and one run through to its far face goes "
                + "exactly that far past it - and this is the drafting on top of it. Keep it small: "
                + "past this, a wall stopping short of the one it should meet is a gap the check is "
                + "there to find rather than a corner drawn a second way.",
                SettingItem.OnRevitImport,
                unit: "mm", keywords: "corner junction intersection tolerance snap");

            // Slab check -------------------------------------------------------------------

            Add(slabs, "SlabThicknessTolerance", "Thickness buffer",
                "How far the two thicknesses may differ and still count as the same. A piece of slab "
                + "further apart than this is what the check colours red.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance depth");

            Add(slabs, "SlabThicknessMinimumIssueArea", "Smallest patch a thickness difference is counted on",
                "A piece of slab smaller than this whose two thicknesses differ is still cut, still "
                + "coloured on the plan and still opens a card saying what differs - it simply stops "
                + "being added up. A hand's width of slab reading 5 mm out is worth seeing and is not "
                + "a job, and a hundred of them standing in the counts are how a level with three "
                + "things wrong with it reads as a level with a hundred and three. Different from "
                + "\"Smallest zone worth reporting\" below, which is far smaller and throws the piece "
                + "away altogether. Measured on the piece, which is the whole shell wherever the two "
                + "models agreed all the way across it. The PT against RC check is counted from the "
                + "same size, since it is asked of the same pieces. Set it to 0 to count every piece.",
                SettingItem.OnComparison,
                unit: "m2", keywords: "sliver noise count issue finding minimum area thickness patch small");

            Add(slabs, "SlabAcceptInsideColumnsAndWalls", "Accept what is found inside columns and walls",
                "The concrete inside a column or a wall belongs to the support, and the two models "
                + "draw it differently as a matter of course - a rigid zone or stiff area in ETABS, a "
                + "hole the column cuts in the Revit floor - so read as slab it is a thickness or "
                + "opening difference at every column. On, the slab is read as solid through the "
                + "columns and walls of both models: the holes they cut in the Revit floors are "
                + "filled, ETABS shells lying inside one are read as the support rather than as slab, "
                + "and any piece still found wrong inside one is accepted automatically. It stays on "
                + "the plan and its card says what differs and which column or wall it is inside, "
                + "but it is drawn as settled and left out of the counts. Untick to check the slab "
                + "there like anywhere else.",
                SettingItem.OnComparison,
                keywords: "column wall support rigid zone stiff inside accept auto noise thickness");

            Add(slabs, "SlabTopElevationTolerance", "Top face buffer",
                "How near the story level a Revit floor's top face has to sit to be read as that "
                + "story's slab. An ETABS area is assumed to be drawn at the top of the slab. Keep "
                + "this well under a story height and comfortably over whatever the top of a floor "
                + "is offset by.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance elevation level story");

            Add(slabs, "SlabStackSearchDepth", "How far below to look for a drop",
                "A drop panel drawn as a second floor hung under the slab has its top face a slab "
                + "thickness below the story plane, so it would be missed entirely if only the "
                + "floors at the plane were read. It is the thickest floor covering a point that "
                + "answers for it.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "drop panel thickening stack");

            Add(slabs, "SlabMinimumZoneArea", "Smallest zone worth reporting, by area",
                "Two edges drawn to the same grid line rarely land on exactly the same coordinate. "
                + "Without a floor here, every one of those would be reported as a mismatch.",
                SettingItem.OnComparison,
                unit: "mm2", keywords: "tolerance sliver noise area minimum");

            Add(slabs, "SlabMinimumZoneWidth", "Smallest zone worth reporting, by width",
                "Works with the area above rather than instead of it: a hairline running the length "
                + "of a long edge can be wide enough in area to pass that test on its own.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance sliver noise width minimum");

            Add(slabs, "SlabEdgeTolerance", "Edge buffer",
                "How near the slab edge a mismatched piece has to run to be read as the two outlines "
                + "disagreeing rather than as an opening one model has and the other has not.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "tolerance outline opening boundary");

            Add(slabs, "FloorSketchFallback", "Read floors with no solid from their sketch",
                "On trial, off by default. A floor Revit gives no solid for is read off its sketch instead: "
                + "outline from the sketch with hosted and shaft openings cut out, thickness from the type's "
                + "structural layers, top at its level plus its height offset. Flat floors only. One cut by a "
                + "void or an opening that cannot be taken out is put up for review, never passed or failed. "
                + "While on, every floor that has a solid is also checked against its sketch, and the import "
                + "report says how many agree.",
                SettingItem.OnRevitImport,
                keywords: "floor sketch solid geometry missing fallback drop panel joined");

            Add(slabs, "SlabFlatnessTolerance", "Flatness buffer",
                "How far an ETABS area may be out of level and still be read as a floor. A ramp "
                + "compared on its plan outline against a thickness measured vertically would be "
                + "wrong in a way that is hard to see, so a sloped area is left out of the check "
                + "instead.",
                SettingItem.OnEtabsImport,
                unit: "mm", keywords: "tolerance ramp slope sloped");

            Add(slabs, "ReadSlabThicknessFromType", "Read thickness from the floor type",
                "Sums the layers the type marks as structure, so a screed or finish in the same "
                + "floor is not counted as slab, and a warped or ramped floor reads as easily as a "
                + "flat one. Untick to measure the concrete between the two faces instead, which is "
                + "worth doing only where the floor types are not to be trusted and the geometry is.",
                SettingItem.OnComparison,
                keywords: "structure layers measure");

            Add(slabs, "DetectLocalThickenings", "Detect local thickenings from the geometry",
                "Finds drop panels and pads modelled as separate bodies - framing, floors, generic "
                + "models, anything - by their shape: top flush with the floor's, reaching under its "
                + "underside, lying over it. The depth they make is then compared with ETABS. Names and "
                + "parameters are never read. A body with no clear verdict is listed, not used. A "
                + "thickening carries no PT or RC class of its own. Off leaves every result as it was.",
                SettingItem.OnRevitImport,
                keywords: "drop panel thickening pad geometry detect");

            Add(slabs, "CompareRibbedSlabsByOverallDepth", "Ribbed and waffle: compare the full depth",
                "Off compares the topping, which suits the usual arrangement where Revit carries the "
                + "topping as a floor and the ribs as beams.",
                SettingItem.OnComparison,
                keywords: "waffle rib topping");

            Add(slabs, "IncludeStructuralFoundations", "Include foundation slabs",
                "Reads structural foundation slabs as floors too, rafts included. Isolated footings "
                + "are never included.",
                SettingItem.OnRevitImport,
                keywords: "raft footing foundation");

            Add(slabs, "StructuralFloorsOnly", "Structural floors only",
                "Leaves out any floor whose Structural box is unticked, which is how a finish "
                + "sitting on the slab is kept from reading as extra depth.",
                SettingItem.OnRevitImport,
                keywords: "architectural finish structure");

            Add(slabs, "SlabTypeNamesToIgnore", "Leave out floors whose type name contains",
                "Separate with spaces or commas. Case is ignored, so \"void\" catches \"VOID - Shaft\" "
                + "and \"Void Former\" alike, and each word is matched anywhere in the name rather "
                + "than as a whole word, so \"fill\" catches \"Light Weight Fill Variable-300\". Both "
                + "the floor's type name and its own name are looked at. This is for the floors that "
                + "are not structure - a fill making up a level, a screed, a finish. ETABS has nothing "
                + "for those to be compared against. Empty checks every floor.",
                SettingItem.OnCheckOrRevitImport,
                keywords: "exclude ignore fill screed finish void plinth case sensitive");

            // Slab loads -------------------------------------------------------------------

            Add(loads, "LoadRegionNumberParameterNames", "Area load number on the loading region",
                "The parameter the area load number is written in, on the filled regions of the "
                + "loading plan. Searched in order, comma separated, and the match ignores case. "
                + "Both the region and its type are looked at, so it reads a number typed onto each "
                + "region and a type parameter carried by a region type per area load alike - the "
                + "second is the usual arrangement, and is why \"Loading Type Mark\" leads the list. "
                + "This is what ties a patch of the loading plan to a row of the loading schedule, "
                + "so the load check cannot run without it.",
                SettingItem.OnComparison,
                keywords: "filled region parameter number plan schedule loading type mark");

            Add(loads, "WriteSlabLoadDump", "Write down what the slab load check read",
                "Writes a file per level beside the other diagnostics with everything the slab load "
                + "check compared: the schedule rows as read, every filled region on the loading plan "
                + "with its outline and every load number parameter it carries - instance and type - "
                + "and each piece of slab with the ETABS shells whose loads were added, the load "
                + "number it was held against and what was found. On while a plan is being set up; "
                + "the path is named in the check's issue list. Turn it off once the plans read as "
                + "expected.",
                SettingItem.OnComparison,
                keywords: "dump diagnostic log debug load region filled schedule number slab");

            Add(loads, "LoadingPlanViewNamePatterns", "A loading plan view is named",
                "What a view name says when it is the loading plan rather than another plan of the "
                + "same floor. Comma separated, matched anywhere in the name, ignoring case. Used "
                + "by \"Guess by name\" in Loading plans: a floor usually has several "
                + "views carrying its name, and only a view whose name also contains one of these "
                + "is offered. Left empty, the level name alone is asked for.",
                SettingItem.OnComparison,
                keywords: "view name guess pair automatic loading plan naming convention");

            Add(loads, "LoadingPlanViewNameRequired", "A loading plan view's name must also carry",
                "Text every loading plan view's name must also contain - all of it, where the "
                + "names above are any one. Comma separated, matched anywhere in the name, ignoring "
                + "case. For a floor with several loading plans that all say \"loading plan\": one "
                + "per tower, or SDL drawn apart from live load. Used by \"Guess by name\"; left "
                + "empty, nothing more is asked for.",
                SettingItem.OnComparison,
                keywords: "view name guess pair must contain all tower sdl required loading plan");

            Add(loads, "SlabLoadTolerance", "Load buffer",
                "How far a load on the ETABS shell may differ from what the schedule asks for and "
                + "still count as the same load. A schedule is written to one decimal place and a "
                + "load set is typed in from it, so anything past rounding is a different number "
                + "rather than a drift.",
                SettingItem.OnComparison,
                unit: "kN/m2", keywords: "tolerance sdl live buffer");

            Add(loads, "SlabLoadMinimumIssueArea", "Smallest patch a load difference is counted on",
                "A piece of slab smaller than this whose loads do not match the plan is still cut, "
                + "still coloured on the plan and still opens a card saying what differs - it simply "
                + "stops being added up. A sliver left between two loading regions carries whichever "
                + "of them the middle of it fell in, and being told about that every time is noise "
                + "rather than work. Kept apart from the thickness size in the slab check because "
                + "the two are cut along different edges and asked different questions, so what is "
                + "too small to chase differs too. Set it to 0 to count every piece.",
                SettingItem.OnComparison,
                unit: "m2", keywords: "sliver noise count issue finding minimum area load patch small");

            Add(loads, "SdlLoadPatternNames", "ETABS patterns that are SDL",
                "Matched against the schedule's TOTAL column, which is the superimposed dead load. "
                + "Comma separated. A name is matched whole first and then as a word inside a longer "
                + "name, and case, spaces and dashes are ignored, so \"Super Dead\" reads as "
                + "\"SUPERDEAD\". A pattern named by none of these four rows is left out of the "
                + "check and reported on the zone rather than counted as nothing.",
                SettingItem.OnComparison,
                keywords: "sidl superimposed dead pattern load case");

            Add(loads, "LiveLoadPatternNames", "ETABS patterns that are LL",
                "The live load the schedule does not mark as reducible or mechanical. Where the "
                + "schedule leaves its own LL column empty, its live load is read as this one.",
                SettingItem.OnComparison,
                keywords: "live pattern load case");

            Add(loads, "ReducibleLiveLoadPatternNames", "ETABS patterns that are LR",
                "The reducible live load, which the reference schedule marks LLR.",
                SettingItem.OnComparison,
                keywords: "live reducible llr roof pattern load case");

            Add(loads, "MepLoadPatternNames", "ETABS patterns that are MEP",
                "The mechanical and plant live load, which the reference schedule marks MECH.",
                SettingItem.OnComparison,
                keywords: "mech mechanical plant pattern load case");

            Add(loads, "ExcludedLoadPatternNames", "ETABS patterns that are none of the four",
                "Names answered before the four rows above are asked, so they are never counted "
                + "however those rows read them. SIDL_C is here because it is a pattern of its own "
                + "and not the SIDL, and without it the match inside a longer name would find "
                + "\"SIDL\" sitting in that name and add it to the superimposed dead load. A pattern "
                + "named here is still reported on the zone as one the settings do not map, so it "
                + "is left out of the sum rather than hidden.",
                SettingItem.OnComparison,
                keywords: "sidl_c exclude ignore skip pattern load case none");

            Add(loads, "ScheduleSidlHeadingNames", "Schedule heading for SIDL",
                "What the loading schedule heads its superimposed dead load with: the breakdown band "
                + "on the area load sheet, and the value column on the line load sheet. Comma "
                + "separated; a heading that starts with any of them matches, ignoring case, spaces and "
                + "dots. Change it where the schedule says SDL or Super Dead instead. The same mapping, "
                + "with the model's own patterns listed, is under Loading plans, Load patterns.",
                SettingItem.OnComparison,
                keywords: "sidl sdl excel schedule heading column name workbook");

            Add(loads, "ScheduleTotalHeadingNames", "Schedule heading for the SIDL total",
                "The area load schedule column the superimposed dead load is read from.",
                SettingItem.OnComparison,
                keywords: "total sidl excel schedule heading column");

            Add(loads, "ScheduleLiveHeadingNames", "Schedule heading for live load",
                "The area load schedule column the live load value is read from.",
                SettingItem.OnComparison,
                keywords: "live load excel schedule heading column");

            Add(loads, "ScheduleLiveMarkings", "Schedule marks plain live load as",
                "What the schedule's narrow LL column says for plain live load. A blank, or a marking "
                + "none of the three rows names, is read as plain live load.",
                SettingItem.OnComparison,
                keywords: "ll live marking excel schedule type");

            Add(loads, "ScheduleReducibleLiveMarkings", "Schedule marks reducible live load as",
                "What the schedule's LL column says for reducible live load. A marking is matched "
                + "whole first and then on how it starts, longest first, so LL never takes LLR.",
                SettingItem.OnComparison,
                keywords: "llr lr reducible marking excel schedule type");

            Add(loads, "ScheduleMepMarkings", "Schedule marks mechanical live load as",
                "What the schedule's LL column says for mechanical and plant load.",
                SettingItem.OnComparison,
                keywords: "mech mep plant marking excel schedule type");

            Add(loads, "ReadFromEtabsTextFile", "Read load sets from the ETABS text file",
                "Reads the shell uniform load sets out of the .$et file ETABS writes beside the model "
                + "on every save, rather than asking ETABS for the same two tables. It is the same "
                + "answer reached far quicker: the table saying which shell carries which load set has "
                + "a row per shell, and on a large model that is well over a hundred thousand rows "
                + "handed over one string at a time. Safe to leave on - a model with no text file "
                + "beside it falls back to the tables on its own. The catch is that the file is only "
                + "as new as the last save, so the import says how many of the shells it names this "
                + "model has not got. Untick to read only the model that is open.",
                SettingItem.OnEtabsImport,
                keywords: "et text file speed fast performance load set stale save");

            // Line loads -------------------------------------------------------------------

            Add(lineLoads, "RunLineLoadCheck", "Check the line loads",
                "Compares the line loads drawn on the loading plan as detail lines against the "
                + "distributed loads on the ETABS frames under them. Off by default, because it "
                + "needs a loading plan view and a line load sheet paired with every level before "
                + "it can say anything, and because the ETABS import only reads the frames' "
                + "distributed loads when it is on - switching it on means importing the ETABS "
                + "model again.",
                SettingItem.OnEtabsImport,
                keywords: "line load detail line frame distributed check switch on off");

            Add(lineLoads, "LineLoadStyleNamePatterns", "A line load is drawn in a style named",
                "What a line style name says when the detail line is a line load. Comma separated, "
                + "matched anywhere in the name, ignoring case and accents. A loading plan carries "
                + "ordinary detail lines too - hatch boundaries, leaders, annotation - and without "
                + "this every one of them would be read as a load with no number on it. The load's "
                + "schedule number is the digits on the end of the style name, so "
                + "\"MRG_STR-Line Load-TL1\" is number 1 on the sheet that level reads.",
                SettingItem.OnComparison,
                keywords: "line style detail line naming convention number schedule");

            Add(lineLoads, "LineLoadPatternNames", "ETABS load patterns to read",
                "The load patterns on the ETABS frames this check compares against. Comma "
                + "separated, matched whole first and then as a word inside a longer name, with "
                + "case, spaces and dashes ignored. SIDL because that is what the line load "
                + "schedules are headed and all they hold - the same beam commonly carries a live "
                + "load and a wind load too, and counting those would report a correctly loaded "
                + "beam as carrying several times what the plan asks for. Patterns named under "
                + "\"ETABS patterns that are none of the four\" are left out before this is "
                + "asked. Leave it empty to read every downward distributed load whatever pattern "
                + "it is in, which is how to see what is actually on the frames.",
                SettingItem.OnComparison,
                keywords: "sidl pattern load case dead superimposed which read filter");

            Add(lineLoads, "LineLoadHeightReduction", "Taken off the story height for the wall",
                "How much comes off the gap between two levels to get the height of the wall a line "
                + "load stands over. The gap is not all wall - the slab and its finish take the "
                + "bottom of it. Applied to every line load except the ones the row below names as "
                + "running the full height.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "wall height story elevation allowance slab depth reduction");

            Add(lineLoads, "LineLoadRoundUpTo", "Round the Revit line load up to",
                "What the load a Revit line asks for is rounded up to once it has been worked out "
                + "from the schedule. A load on elevation times a wall height comes out as 26.04 "
                + "kN/m and is modelled as 26.1, so the plan's side is rounded up the same way "
                + "before it is compared. Up, never down, because a load is rounded to the safe "
                + "side. 0 leaves the value unrounded.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "round rounding up line load 0.1 decimal precision");

            Add(lineLoads, "FullHeightLineLoadNames", "Line loads that run the full story height",
                "Line loads whose schedule description says any of these take the whole gap between "
                + "the levels, with no allowance off. Comma separated, matched anywhere in the "
                + "description, ignoring case and accents - which matters, because a schedule that "
                + "types FACADE with a cedilla would otherwise miss every facade on the sheet and "
                + "quietly check them against a height 200mm short.",
                SettingItem.OnComparison,
                keywords: "facade cladding glazing full height curtain wall accent cedilla");

            Add(lineLoads, "LineLoadTolerance", "Line load buffer",
                "How far a load on the ETABS frame may differ from what the schedule asks for and "
                + "still count as the same load. Larger than the slab's buffer because the number it "
                + "is held against is a product rather than a figure typed off a sheet: a load on "
                + "elevation times a wall height worked out from two story elevations and a round "
                + "allowance.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "tolerance buffer line load difference");

            Add(lineLoads, "LineLoadLocationTolerance", "How far a line may sit off its frame",
                "A load line is drawn on the face of the wall it stands for, or along a slab edge, "
                + "while the frame it lands on runs down the middle of a beam - so a few hundred "
                + "between them is the normal case rather than a mistake. This check is not asking "
                + "whether the line is in the right place; it is asking whether the load is there. "
                + "A line further off than this is still read against the frame where it is inside "
                + "the search radius, but as a bundled line: its load counts towards the frame's "
                + "total and is not compared stretch by stretch.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "offset buffer distance line frame face centre");

            Add(lineLoads, "LineLoadSearchRadius", "How far to look for a frame under a line",
                "Past this there is nothing under the drawn line, and it is reported as a load ETABS "
                + "has no frame to carry.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "search radius reach distance frame");

            Add(lineLoads, "LineLoadAngleTolerance", "How far a line may run off its frame",
                "A drawn line further round than this from the frame is still kept where it is close "
                + "enough, but as a bundled line rather than an aligned one - a line at forty degrees "
                + "to a frame has no stretch of it to be compared against, so its load is put onto "
                + "the frame's total instead.",
                SettingItem.OnComparison,
                unit: "degrees", keywords: "angle skew rotation parallel collinear");

            Add(lineLoads, "LineLoadCheckInSteps", "Check each line in steps",
                "Walk every drawn line from its start to its end in steps, compare what ETABS carries "
                + "at each step with what the plan asks for, and report only the stretches that "
                + "differ - each with the value ETABS carries there, not an average over the line. A "
                + "step is always forced where an ETABS frame starts or ends and where a load starts "
                + "or stops along one. Off, each line gets a single verdict for its whole length.",
                SettingItem.OnComparison,
                keywords: "step steps station sample stretch segment line load walk 250");

            Add(lineLoads, "LineLoadStepLength", "Step along a line",
                "How far apart the steps are when each line is checked in steps. Frame ends and "
                + "load ends always get a step of their own on top of these.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "step length station spacing line load 250");

            Add(lineLoads, "LineLoadMinimumIssueLength", "Shortest difference worth reporting",
                "Where two drawn lines meet end to end their ends do not quite agree, and the sliver "
                + "between them is not a stretch of unloaded beam. Without this there would be a "
                + "finding between every pair of lines on the plan. Set it to 0 to report every one.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "sliver noise gap minimum length line load small");

            Add(lineLoads, "LineLoadMinimumCountedLength", "Shortest finding that is counted",
                "A line load finding shorter than this is still found, still coloured on the plan "
                + "and still opens a card saying what differs - it simply stops being added up in "
                + "the counts, the level rail and the findings list. A line running a hand's width "
                + "past the end of its frame is worth seeing and is not a job. Measured on what the "
                + "plan draws: each stretch of a line checked in steps, the part that is wrong of a "
                + "line checked whole, and the ETABS load itself where nothing asks for it. "
                + "Different from \"Shortest difference worth reporting\" above, which is far "
                + "shorter and changes what the check finds. Set it to 0 to count every one.",
                SettingItem.OnRedraw,
                unit: "m", keywords: "count counted finding minimum length line load small "
                    + "short noise report tally");

            Add(lineLoads, "WriteLineLoadDump", "Write down every line on the loading plan",
                "Writes a file beside the other diagnostics listing every line on each loading "
                + "plan the check reads - whatever style it is drawn in - tallied by style name, "
                + "with what the check made of each one. On, because the one thing an empty result "
                + "cannot tell you is why it is empty: a drawing whose line styles are named "
                + "differently from the setting above gives a plan with nothing on it and no way "
                + "to find out what happened. The path is named in the check's issue list. Turn it "
                + "off once the styles are set up and reading.",
                SettingItem.OnComparison,
                keywords: "dump diagnostic log debug lines style empty nothing missing");

            // SIDL_C loads -----------------------------------------------------------------

            Add(sidlC, "RunSidlCCheck", "Check the SIDL_C loads",
                "Compares the SIDL_C line loads on the ETABS frames against the weight of the Revit "
                + "walls and fill strips named below. These are the elements the wall and slab "
                + "checks leave out as not structure: ETABS carries them as load rather than as "
                + "elements, so what is checked is that the load is there and is what they weigh. "
                + "Its tab is never up at the same time as the line loads tab. The walls are read "
                + "with the Revit model, so switching it on means importing the Revit model again.",
                SettingItem.OnRevitImport,
                keywords: "sidl_c sidlc wall weight self weight upstand planter fill check switch on off");

            Add(sidlC, "SidlCLoadPatternNames", "ETABS / SAFE load patterns to read",
                "The load patterns the SIDL_C line and area checks read: the walls' and fill strips' "
                + "weight is compared against these on the frames, the fill floors' against these on "
                + "the slabs. Comma separated, one name or several - SIDL_C, SIDL_LWC. Matched whole "
                + "only: case, spaces, dashes and underscores are ignored, but a name is never found "
                + "inside a longer one, so exactly the patterns named are read. A pattern named here "
                + "is never read as the SIDL by the area or line load checks either.",
                SettingItem.OnComparison,
                keywords: "sidl_c sidl_lwc lwc pattern load case which read filter exact safe");

            Add(sidlC, "SidlCWallNames", "Walls read as a SIDL_C load",
                "A Revit wall whose own name or type name contains any of these is weighed as a "
                + "SIDL_C line load: thickness times height times the density below, on the frames "
                + "of the floor it stands on. Comma separated, matched anywhere in the name, without "
                + "regard to case. Starts as the names the wall check leaves out, since those are the "
                + "walls carried as load rather than modelled.",
                SettingItem.OnRevitImport,
                keywords: "sidl_c wall name upstand planter pool partition blockwork non structural");

            Add(sidlC, "SidlCFillSlabNames", "Floors read as a fill strip",
                "A Revit floor whose own name or type name contains any of these, and which is "
                + "narrower than the width below, is weighed as a SIDL_C line load down its middle: "
                + "thickness times width times the density. Comma separated, matched anywhere in the "
                + "name, without regard to case.",
                SettingItem.OnRevitImport,
                keywords: "sidl_c fill slab floor strip name");

            Add(sidlC, "SidlCFillSlabMaximumWidth", "Widest floor read as a fill strip",
                "A fill floor this wide or wider is an area of fill rather than a strip, and is left "
                + "to the slab load check.",
                SettingItem.OnRevitImport,
                unit: "mm", keywords: "sidl_c fill strip width narrow 500");

            Add(sidlC, "SidlCConcreteDensity", "Density",
                "What the walls and fill strips weigh per cubic metre.",
                SettingItem.OnComparison,
                unit: "kN/m3", keywords: "sidl_c density concrete unit weight 25 self weight");

            Add(sidlC, "SidlCToleranceBelow", "ETABS may carry less by",
                "How far under the element's weight the SIDL_C load on the frame may be and still "
                + "agree. Small, because a load short of the weight is a load short.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "sidl_c tolerance buffer less under below minus 0.3");

            Add(sidlC, "SidlCToleranceAbove", "ETABS may carry more by",
                "How far over the element's weight the SIDL_C load on the frame may be and still "
                + "agree. Larger, because a load modelled a little heavy - rounded up, or a finish "
                + "allowed for - is on the safe side.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "sidl_c tolerance buffer more over above plus 3");

            Add(sidlC, "SidlCLocationTolerance", "How far a wall may sit off its frame",
                "How far between a wall or fill strip and the centre line of the ETABS frame "
                + "under it. Past this the frame is not looked at, and the wall is reported as "
                + "having no frame to carry it. The SIDL_C check's own: the line loads tab's "
                + "buffer and search radius are not used here.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "sidl_c location offset buffer distance wall frame");

            Add(sidlC, "SidlCMinimumLoad", "Lightest load worth checking",
                "A wall or fill strip that weighs less than this per metre is left out of the "
                + "check. A SIDL_C load ETABS carries under one is then reported as nothing in "
                + "Revit accounting for it.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "sidl_c minimum lightest smallest load weight skip ignore 1");

            Add(sidlC, "SidlCLoadIntervals", "Step the weight up to",
                "Loads the weight worked out from the geometry is stepped up to before it is checked, "
                + "comma separated - for example 2, 6, 10, 14, 18. A wall weighing 3 kN/m is then "
                + "checked as 6, one weighing 6 as 6, and one weighing more than the last as what it "
                + "weighs. Empty, the weight is checked as it is.",
                SettingItem.OnComparison,
                unit: "kN/m", keywords: "sidl_c interval step band round up 2 6 10 14 18 line load value");

            Add(sidlC, "SidlCLoadFromComment", "Take the load from the instance comment",
                "Where a wall's or fill strip's instance Comments states a load - only a number, as "
                + "6, or a number in kN/m, as 6 kN/m - that is the load it is checked against, as it "
                + "is. Where it states none, the load is worked out from the geometry as usual, and "
                + "stepped up to the intervals above if any are set.",
                SettingItem.OnComparison,
                keywords: "sidl_c comment comments instance parameter load value override manual");

            // SIDL_C area loads ------------------------------------------------------------

            Add(sidlCArea, "RunSidlCAreaCheck", "Check the SIDL_C area loads",
                "Compares the SIDL_C area loads on the ETABS slabs against the weight of the Revit "
                + "light weight fill floors over them. The fill is carried as load rather than "
                + "modelled, and is not on a loading plan. Its tab is never up at the same time as "
                + "either slab tab. The fills are read with the Revit model, so switching it on "
                + "means importing the Revit model again. The ETABS patterns read are the ones "
                + "under SIDL_C Line.",
                SettingItem.OnRevitImport,
                keywords: "sidl_c sidlc area fill ramp light weight lwc check switch on off");

            Add(sidlCArea, "SidlCAreaLoadPatternNames", "ETABS / SAFE load patterns to read",
                "The load patterns the fill floors' weight is compared against on the slabs, when "
                + "the model keeps it apart from the walls' - SDLF beside SDLP. Comma separated, "
                + "matched whole only. Empty reads the SIDL_C Line patterns.",
                SettingItem.OnComparison,
                keywords: "sidl_c area fill pattern load case sdlf");

            Add(sidlCArea, "SidlCAreaFloorNames", "Floors read as light weight fill",
                "A Revit floor whose own name or type name contains any of these is weighed as a "
                + "SIDL_C area load, structure or not. Comma separated, matched anywhere in the name, "
                + "without regard to case.",
                SettingItem.OnRevitImport,
                keywords: "sidl_c fill light weight lwc floor name ramp");

            Add(sidlCArea, "SidlCAreaLightweightDensity", "Light weight concrete density",
                "What the fill weighs per cubic metre. The whole depth of the fill is counted at this "
                + "density, and the topping below adds the difference.",
                SettingItem.OnComparison,
                unit: "kN/m3", keywords: "sidl_c lwc light weight density unit weight 10");

            Add(sidlCArea, "SidlCAreaToppingDensity", "Topping density",
                "What the topping over the fill weighs per cubic metre.",
                SettingItem.OnComparison,
                unit: "kN/m3", keywords: "sidl_c topping screed density unit weight 25");

            Add(sidlCArea, "SidlCAreaToppingThickness", "Topping thickness",
                "The top of the fill that is topping rather than light weight concrete. The load is "
                + "average depth x light weight density + this x (topping density - light weight "
                + "density): a ramp from 0 to 500 mm is 0.25 x 10 + 0.1 x 15 = 4 kN/m2.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "sidl_c topping thickness depth 100");

            Add(sidlCArea, "SidlCAreaRoundUpTo", "Round the fill's weight up to",
                "What the fill's weight is rounded up to once it has been worked out, the whole fill's "
                + "and each band's. A fill weighing 4.56 kN/m2 is modelled as 5, so it is rounded up "
                + "the same way before it is compared. Up, never down, because a load is rounded to "
                + "the safe side. 0 leaves the value unrounded.",
                SettingItem.OnComparison,
                unit: "kN/m2", keywords: "sidl_c area round rounding up fill weight 0.5 precision");

            Add(sidlCArea, "SidlCAreaToleranceBelow", "ETABS may carry less by",
                "How far under the fill's weight the average SIDL_C under it may be and still agree.",
                SettingItem.OnComparison,
                unit: "kN/m2", keywords: "sidl_c area tolerance buffer less under below minus 0.5");

            Add(sidlCArea, "SidlCAreaToleranceAbove", "ETABS may carry more by",
                "How far over the fill's weight the average SIDL_C under it may be and still agree. "
                + "Larger, because a load a little heavy is on the safe side.",
                SettingItem.OnComparison,
                unit: "kN/m2", keywords: "sidl_c area tolerance buffer more over above plus 1");

            Add(sidlCArea, "SidlCAreaMisplacedDistance", "How far a misplaced load may be",
                "A fill with no SIDL_C under it, and an ETABS area carrying SIDL_C with no fill over "
                + "it this close by, are reported once, as the fill's load drawn in the wrong place, "
                + "rather than as two findings. Nought reports them apart.",
                SettingItem.OnComparison,
                unit: "mm", keywords: "sidl_c area misplaced wrong place offset distance near 2000");

            // Diagnostics ------------------------------------------------------------------

            Add(diagnostics, "WriteGeometryDump", "Record refused geometry to a file",
                "Writes every outline the geometry kernel would not accept, with what is wrong with "
                + "it and the coordinates themselves, so it can be looked at rather than guessed at. "
                + "Takes effect on the next Revit import.",
                SettingItem.OnRevitImport,
                keywords: "log dump debug geometry fault",
                note: geometryDumpNote);

            Add(diagnostics, "WriteThickeningSurvey", "Survey what stands against the floors",
                "Writes a text file, in the same folder as the geometry dump, listing the bodies "
                + "standing against each Revit floor: footprint, heights against the floor's top and "
                + "underside, and the depth the two make together. Read from the geometry, whatever "
                + "the category or name. Changes nothing the check reads. Takes effect on the next "
                + "Revit import.",
                SettingItem.OnRevitImport,
                keywords: "log dump debug geometry drop panel thickening survey");

            Add(diagnostics, "ThickeningSurveyFloorIds", "Floors to survey",
                "Revit ids of the floors the survey looks at, separated by commas. Empty surveys the "
                + "first forty floors read.",
                SettingItem.OnRevitImport,
                keywords: "drop panel thickening survey floor id");

            Add(diagnostics, "ThickeningMinOverlapPercent", "Thickening: share over the floor",
                "How much of a body's footprint has to lie over the floor to be read as part of it. "
                + "A body part way over is listed as ambiguous, not dropped.",
                SettingItem.OnRevitImport, unit: "%",
                keywords: "thickening drop panel overlap");

            Add(diagnostics, "ThickeningFlushToleranceMM", "Thickening: flush tolerance",
                "Heights this close are the same height: a body's top against the floor's top.",
                SettingItem.OnRevitImport, unit: "mm",
                keywords: "thickening drop panel flush");

            Add(diagnostics, "ThickeningMinProjectionMM", "Thickening: least reach under the floor",
                "How far under the floor's underside a body has to reach to add depth.",
                SettingItem.OnRevitImport, unit: "mm",
                keywords: "thickening drop panel projection");

            Add(diagnostics, "ThickeningMinAreaM2", "Thickening: panel area",
                "The footprint at which a body reads as a panel. Evidence towards a verdict, not a cut-off: "
                + "a smaller body is listed with lower confidence.",
                SettingItem.OnRevitImport, unit: "m2",
                keywords: "thickening drop panel area");

            Add(diagnostics, "ThickeningMinShortSideMM", "Thickening: panel short side",
                "The short side at which a body reads as a panel and not a strip. Evidence, not a cut-off.",
                SettingItem.OnRevitImport, unit: "mm",
                keywords: "thickening drop panel width");

            Add(diagnostics, "ThickeningMinCompactness", "Thickening: panel compactness",
                "How fully a body fills the smallest rectangle round it, 0 to 1. Evidence, not a cut-off.",
                SettingItem.OnRevitImport,
                keywords: "thickening drop panel shape");

            Add(diagnostics, "SaveSlabCheckInputs", "Save the slab check's inputs for the level on show",
                "Each time the slab check runs, writes everything it reads for the level on show - the "
                + "ETABS shells, the Revit floors with their openings, the columns and walls, and these "
                + "settings - to one file in %APPDATA%\\DiscrepancyScanner. The slab check's issues say "
                + "where. Send that file with a slab finding that looks wrong and it can be run again "
                + "exactly, without Revit or ETABS.",
                SettingItem.OnComparison,
                keywords: "log dump debug slab inputs replay send report bug");

            Add(diagnostics, "SaveLineLoadCheckInputs", "Save the line load check's inputs for the level on show",
                "Each time the line load check runs, writes everything it reads for the level on show - "
                + "the drawn load lines, the ETABS frames with their loads, the schedule sheet, the "
                + "pairings and height zones set by hand, and these settings - to one file in "
                + "%APPDATA%\\DiscrepancyScanner. The line load check's issues say where. Send that file "
                + "with a line load finding that looks wrong and it can be run again exactly, without "
                + "Revit or ETABS.",
                SettingItem.OnComparison,
                keywords: "log dump debug line load inputs replay send report bug");

            // Keyboard shortcuts -----------------------------------------------------------

            foreach (ShortcutAction action in Shortcuts.All)
            {
                ShortcutAction which = action;
                allItems.Add(new SettingItem(KeyboardShortcuts, Shortcuts.FieldOf(action),
                    Shortcuts.NameOf(action), ShortcutDescription(action), SettingItem.Immediately,
                    keywords: "key keyboard shortcut hotkey press " + Shortcuts.NameOf(action),
                    noteProvider: () => ShortcutClash(which), onChanged: SettingChanged,
                    editor: SettingItem.KeyEditor));
            }
        }

        private static string ShortcutDescription(ShortcutAction action)
        {
            switch (action)
            {
                case ShortcutAction.LevelUp:
                    return "Steps to the level above, by height - the same as the up arrow beside the level name.";
                case ShortcutAction.LevelDown:
                    return "Steps to the level below, by height - the same as the down arrow beside the level name.";
                case ShortcutAction.RawData:
                    return "Switches the plan between the check and the raw data of both models, the same as "
                           + "the Raw data box beside the tabs.";
                case ShortcutAction.Accept:
                    return "Accepts what is selected as an agreed difference, or takes the acceptance back - "
                           + "the same as ACCEPT on the card.";
                case ShortcutAction.Pair:
                    return "Pairs the two elements selected, one from each model - the same as PAIR THESE.";
                default:
                    return "Switches the " + Shortcuts.NameOf(action).ToLowerInvariant()
                           + " along the top on or off, the same as clicking it.";
            }
        }

        /// <summary>
        /// Says so where another action's key gets in this one's way: the same key, which does
        /// whichever is higher up this page, or one key the start of the other, which goes to
        /// the shorter as soon as it is pressed.
        /// </summary>
        private static string ShortcutClash(ShortcutAction action)
        {
            return Shortcuts.ClashNote(action, PrefrencesData.getPrefrencesData());
        }

        /// <summary>How a size mapping is written, said once for every one of them.</summary>
        private const string MappingNote =
            "Write the exact parameter name: \"b\" looks on the type and then the instance, \"Type:b\" on the "
            + "type only, \"Instance:b\" on the instance only. Several are tried in the order written, comma "
            + "separated. Only a Length parameter is read - text and plain numbers are not. Nothing is guessed: "
            + "left empty, the size is measured off the solid (if that is switched on below). Kept per project.";

        private void Add(string category, string key, string label, string description, string applies,
                         string unit = null, string keywords = null, Func<string> note = null)
        {
            allItems.Add(new SettingItem(category, key, label, description, applies, unit, keywords,
                                         note, SettingChanged));
        }

        /// <summary>
        /// Raised by every row as it is edited. The summary at the foot of the window is
        /// refreshed, and the owner is told so the plan behind can redraw where it needs to.
        /// </summary>
        private void SettingChanged()
        {
            // A key taken by one action can clash with, or stop clashing with, any other, so
            // every key row has its say again rather than only the one that was changed.
            foreach (SettingItem item in allItems.Where(item => item.IsKey))
            {
                item.RefreshNote();
            }

            OnPropertyChanged(nameof(ChangedSummary));
            onChanged?.Invoke();
        }

        #endregion

        /// <summary>Lets the owner push a live note back onto the rows that carry one.</summary>
        public void RefreshNotes()
        {
            foreach (SettingItem item in allItems)
            {
                item.RefreshNote();
            }
        }
    }
}
