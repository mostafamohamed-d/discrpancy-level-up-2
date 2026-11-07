using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One tab in the strip across the top: the check it selects, and how much is wrong under
    /// it. The count is the point of putting it there - a tab that reads "Walls 27" says where
    /// the work is before it is opened, which the old radio buttons could not.
    /// </summary>
    public sealed class ScopeTab : ViewModelBase
    {
        private readonly Action onActiveChanged;
        private int failed;
        private int review;
        private bool isActive = true;
        private string keyText = "";

        public ScopeTab(ViewMode mode, string label, string tooltip, Action onActiveChanged,
                        bool startsActive = true)
        {
            Mode = mode;
            Label = label;
            description = tooltip;
            this.onActiveChanged = onActiveChanged;
            isActive = startsActive;
        }

        private readonly string description;

        public ViewMode Mode { get; }
        public string Label { get; }

        /// <summary>
        /// The key that switches this tab on and off, as it is set under Settings - 1 for beams,
        /// 2 for columns and so on along the strip unless somebody has changed them. Empty for a
        /// tab with no key.
        /// </summary>
        public string KeyText
        {
            get => keyText;
            internal set
            {
                if (SetProperty(ref keyText, value ?? ""))
                {
                    OnPropertyChanged(nameof(ToolTip));
                }
            }
        }

        public string ToolTip => keyText.Length > 0
            ? description + " Press " + keyText + " to switch it on or off."
            : description;

        /// <summary>
        /// Whether this check is being read. The chips are not exclusive: several can be on at
        /// once, which is how a beam and the column it lands on are compared side by side, and
        /// turning the other four off is how one kind is looked at on its own.
        ///
        /// The two slab chips are one exception, and the tally enforces it: they draw two
        /// different cuts of the same floor, so with both on the second is laid straight over
        /// the first and neither can be read. Switching one on switches the other off. Line
        /// loads and SIDL_C are the other pair, for the same reason.
        ///
        /// Switching a chip off greys that kind out on the plan and takes its rows out of the
        /// panel, but it does NOT reach into the verdict switches underneath - those keep
        /// whatever they were set to, so a chip switched back on finds them as they were left.
        ///
        /// The count stays up on a chip that is off, which is deliberate: the design shows the
        /// switched-off check carrying its number as a bare outline, because how much is wrong
        /// under a check you are not looking at is exactly what tells you to go and look.
        /// </summary>
        public bool IsActive
        {
            get => isActive;
            set
            {
                if (SetProperty(ref isActive, value))
                {
                    onActiveChanged?.Invoke();
                }
            }
        }

        /// <summary>Elements under this check that failed it.</summary>
        public int Failed
        {
            get => failed;
            private set
            {
                if (SetProperty(ref failed, value))
                {
                    OnPropertyChanged(nameof(Open));
                    OnPropertyChanged(nameof(HasOpen));
                }
            }
        }

        /// <summary>Elements under this check that want a human look rather than a verdict.</summary>
        public int Review
        {
            get => review;
            private set
            {
                if (SetProperty(ref review, value))
                {
                    OnPropertyChanged(nameof(Open));
                    OnPropertyChanged(nameof(HasOpen));
                }
            }
        }

        /// <summary>What the tab shows: everything still to be dealt with under this check.</summary>
        public int Open => failed + review;

        /// <summary>
        /// False before the comparison has run, and on a check that came back clean. The count
        /// is hidden rather than shown as a zero, so an untouched tab does not read as a
        /// finished one.
        /// </summary>
        public bool HasOpen => Open > 0;

        internal void Set(int failedCount, int reviewCount)
        {
            Failed = failedCount;
            Review = reviewCount;
        }
    }

    /// <summary>
    /// One level in the rail down the left of the plan: its name, where it sits, and how much
    /// is open on it. The counts are what make the rail worth having - it says which floor to
    /// go to next without visiting all forty-seven.
    /// </summary>
    public sealed class LevelRow : ViewModelBase
    {
        private int failed;
        private int review;
        private bool isSelected;

        public LevelRow(Story_E story)
        {
            Story = story;
        }

        public Story_E Story { get; }
        public string Name => Story?.Name ?? "";

        /// <summary>The level's height, signed, as the rail prints it.</summary>
        public string Elevation
        {
            get
            {
                if (Story == null)
                {
                    return "";
                }

                double metres = Story.Elevation / 1000.0;
                if (Math.Abs(metres) < 0.05)
                {
                    return "±0.0";
                }
                return (metres > 0 ? "+" : "−") + Math.Abs(metres).ToString("0.0");
            }
        }

        public int Failed
        {
            get => failed;
            private set
            {
                if (SetProperty(ref failed, value))
                {
                    OnPropertyChanged(nameof(HasFindings));
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public int Review
        {
            get => review;
            private set
            {
                if (SetProperty(ref review, value))
                {
                    OnPropertyChanged(nameof(HasFindings));
                    OnPropertyChanged(nameof(Description));
                }
            }
        }

        public bool HasFindings => failed + review > 0;

        /// <summary>
        /// The widest the two bars together are allowed to be, and the count that earns it.
        /// Set by the tally after every level has been counted, so the bars are read against
        /// each other down the rail: the worst level fills the width and the rest are honest
        /// fractions of it.
        /// </summary>
        internal int ScaleMax { get; set; } = 1;

        private const double BarWidth = 120;

        public double FailedBarWidth => Width(failed);
        public double ReviewBarWidth => Width(review);

        private double Width(int count)
        {
            if (count <= 0 || ScaleMax <= 0)
            {
                return 0;
            }

            // A level with anything open gets at least a visible sliver, so one finding on a
            // quiet floor is not rounded away to nothing next to a floor with two hundred.
            return Math.Max(2.0, BarWidth * count / ScaleMax);
        }

        /// <summary>The line under the selected level: "17 fails, 6 to look at".</summary>
        public string Description
        {
            get
            {
                if (!HasFindings)
                {
                    return "Nothing open";
                }

                List<string> parts = new List<string>();
                if (failed > 0)
                {
                    parts.Add(failed + (failed == 1 ? " fail" : " fails"));
                }
                if (review > 0)
                {
                    parts.Add(review + " to look at");
                }
                return string.Join(" · ", parts);
            }
        }

        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        internal void Set(int failedCount, int reviewCount)
        {
            Failed = failedCount;
            Review = reviewCount;
        }

        internal void Rescale(int scaleMax)
        {
            ScaleMax = scaleMax < 1 ? 1 : scaleMax;
            OnPropertyChanged(nameof(FailedBarWidth));
            OnPropertyChanged(nameof(ReviewBarWidth));
        }
    }

    /// <summary>
    /// One entry in the key along the right of the scope bar: a colour and what it means. It is
    /// a legend, not a switch - the switches are the chips and the rows down the side - and the
    /// place a colour is changed from: right-clicking an entry picks a new colour for it.
    /// </summary>
    public sealed class VerdictKeyEntry : ViewModelBase
    {
        private readonly string model;

        public VerdictKeyEntry(VerdictColour colour)
        {
            Colour = colour;
        }

        /// <summary>
        /// An entry for the raw data, where the colour stands for a whole model rather than a
        /// verdict: ETABS and Revit, in the colours the check gives what only one of them has.
        /// </summary>
        public VerdictKeyEntry(VerdictColour colour, string model)
        {
            Colour = colour;
            this.model = model;
        }

        /// <summary>
        /// An entry whose colour is not the user's to change - a height zone's height, whose
        /// colour says which height it is.
        /// </summary>
        public VerdictKeyEntry(string label, string swatch, string toolTip)
        {
            model = label ?? "";
            fixedSwatch = swatch ?? "";
            fixedToolTip = toolTip ?? "";
        }

        private readonly string fixedSwatch;
        private readonly string fixedToolTip;

        /// <summary>True for an entry right-clicking does not recolour.</summary>
        public bool IsFixed => fixedSwatch != null;

        public VerdictColour Colour { get; }
        public string Label => model ?? VerdictColours.NameOf(Colour);
        public string Swatch => fixedSwatch ?? VerdictColours.Hex(Colour);

        public string ToolTip => IsFixed ? fixedToolTip : "Right-click to change the colour "
                                 + (model == null
                                     ? Label.ToLowerInvariant()
                                     : "the " + model + " model")
                                 + " is drawn in"
                                 + (model == null
                                     ? ""
                                     : ", which is the colour the check draws "
                                       + VerdictColours.NameOf(Colour) + " in")
                                 + (VerdictColours.IsDefault(Colour) ? "." : " - this one has been changed.");

        /// <summary>Says the colour has changed, after somebody picked a new one.</summary>
        internal void Refresh()
        {
            OnPropertyChanged(nameof(Swatch));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    /// <summary>
    /// A run of tabs drawn together under one caption - Geometry, Loads. A group with no
    /// title is drawn bare.
    /// </summary>
    public sealed class ScopeGroup
    {
        public ScopeGroup(string title, IEnumerable<ScopeTab> tabs)
        {
            Title = title ?? "";
            Tabs = new ObservableCollection<ScopeTab>(tabs);
        }

        public string Title { get; }
        public bool HasTitle => Title.Length > 0;
        public ObservableCollection<ScopeTab> Tabs { get; }
    }

    /// <summary>
    /// The counts behind the tab strip and the level rail, worked out in one pass over every
    /// element the check produced.
    ///
    /// It is deliberately fed the whole model rather than the level on show: the rail's job is
    /// to say which other level is worth going to, which it cannot do from the level already
    /// being looked at. The tab counts are taken from the same pass, scoped to the level, so a
    /// tab and the plan under it always agree about what is on screen.
    /// </summary>
    public sealed class FindingTally
    {
        private readonly Dictionary<ViewMode, ScopeTab> tabsByMode;
        private readonly Dictionary<string, LevelRow> rowsByStory =
            new Dictionary<string, LevelRow>(StringComparer.OrdinalIgnoreCase);

        private readonly Action onActiveChanged;
        private bool settling;

        public FindingTally(Action onActiveChanged)
        {
            this.onActiveChanged = onActiveChanged;

            // Beams first, then columns, then walls, then the two slab checks - the order the
            // design puts them in, which is roughly the order a floor is read.
            Tabs = new ObservableCollection<ScopeTab>
            {
                new ScopeTab(ViewMode.Beams, "Beams", "The beam check.",
                    () => TabChanged(ViewMode.Beams)),
                new ScopeTab(ViewMode.Columns, "Columns",
                    "The column checks: location, orientation, section shape, dimensions and type mark.",
                    () => TabChanged(ViewMode.Columns)),
                new ScopeTab(ViewMode.Walls, "Walls",
                    "The wall checks: whether the ETABS panel is on the Revit wall's line, and whether the two are the same thickness.",
                    () => TabChanged(ViewMode.Walls)),
                new ScopeTab(ViewMode.Slabs, "Slabs",
                    "The slab checks about the slab itself: thickness, the outlines and the openings. Switches Slab loads off - the two draw different cuts of the same floor.",
                    () => TabChanged(ViewMode.Slabs)),

                // Off to begin with, because the other one has to be: two shells over each
                // other is the one thing the pair must never start as.
                new ScopeTab(ViewMode.SlabLoads, "Slab loads",
                    "The area loads on the ETABS shells against the loading plan. Switches Slabs off - the two draw different cuts of the same floor.",
                    () => TabChanged(ViewMode.SlabLoads), false),

                // Off to begin with as well, and for its own reason: it draws the Revit
                // loading plan rather than the ETABS model, so it is the one tab that changes
                // what the plan underneath is.
                new ScopeTab(ViewMode.LineLoads, "Line loads",
                    "The line loads drawn on the loading plan against the distributed loads on the ETABS frames. Draws the detail lines themselves, and shows a frame only where it carries a load nothing drew.",
                    () => TabChanged(ViewMode.LineLoads), false),

                // Off to begin with, like the line loads, and never up at the same time as them.
                new ScopeTab(ViewMode.SidlC, "SIDL_C Line",
                    "The SIDL_C line loads on the ETABS frames against the weight of the walls and fill strips the wall and slab checks leave out - upstands, planters, fill along an edge. Switches Line loads off - the two are different questions about the same frames.",
                    () => TabChanged(ViewMode.SidlC), false),

                new ScopeTab(ViewMode.SidlCArea, "SIDL_C Area",
                    "The SIDL_C area loads on the ETABS slabs against the weight of the light weight fill floors over them - a ramp averaged by the engineer, whole or in pieces, read back as one average. Switches the two slab tabs off - all three are regions of floor.",
                    () => TabChanged(ViewMode.SidlCArea), false),

                // Not a check, so off to begin with and never counted: it is where the zones
                // the line loads take their heights from are drawn and changed. Last, and in
                // no group, for the same reason.
                new ScopeTab(ViewMode.HeightZones, "Height zones",
                    "Patches of a level where the line loads use a height of their own instead of the floor to floor height - a double height lobby, a taller plant storey. Draw them with the tools that come up on the plan.",
                    () => TabChanged(ViewMode.HeightZones), false)
            };

            tabsByMode = Tabs.ToDictionary(tab => tab.Mode);

            // The strip as it is drawn: the checks on the elements themselves, then the checks
            // on what they carry, then the height zones on their own.
            Groups = new ObservableCollection<ScopeGroup>
            {
                new ScopeGroup("GEOMETRY", Pick(ViewMode.Beams, ViewMode.Columns, ViewMode.Walls, ViewMode.Slabs)),
                new ScopeGroup("LOADS", Pick(ViewMode.SlabLoads, ViewMode.LineLoads, ViewMode.SidlC, ViewMode.SidlCArea)),
                new ScopeGroup("", Pick(ViewMode.HeightZones))
            };
            Levels = new ObservableCollection<LevelRow>();

            ShowKeys();
        }

        public ObservableCollection<ScopeTab> Tabs { get; }

        /// <summary>The same tabs, in the groups the strip draws them in.</summary>
        public ObservableCollection<ScopeGroup> Groups { get; }

        private IEnumerable<ScopeTab> Pick(params ViewMode[] modes)
        {
            return modes.Select(mode => tabsByMode[mode]);
        }

        /// <summary>
        /// Puts the key each tab is switched with into its tooltip, as it is set under Settings
        /// now. Asked again whenever the settings change, so a key just changed is the key the
        /// tooltip names.
        /// </summary>
        public void ShowKeys()
        {
            foreach (ScopeTab tab in Tabs)
            {
                tab.KeyText = Shortcuts.Describe(Shortcuts.KeyOf(Shortcuts.ActionOf(tab.Mode)));
            }
        }

        /// <summary>
        /// Switches a tab on or off, which is what its key does. The same as a click on the
        /// tab, the rule that keeps the two slab tabs from both being up included. False where
        /// there is no such tab.
        /// </summary>
        public bool ToggleTab(ViewMode mode)
        {
            ScopeTab tab;
            if (!tabsByMode.TryGetValue(mode, out tab))
            {
                return false;
            }

            tab.IsActive = !tab.IsActive;
            return true;
        }

        /// <summary>
        /// What a chip being switched does, before anybody is told about it.
        ///
        /// The chips are otherwise free to be on together, but the two slab ones are cuts of
        /// the same floor along different lines - one along the Revit floors, one along the
        /// loading plan regions - so switching both on lays the second mesh straight over the
        /// first. The colours underneath are hidden, the edges double up, and a click lands on
        /// whichever piece happens to be on top. So one turns the other off, and the plan
        /// always shows one cut of the floor with one answer on it.
        ///
        /// Both off is still allowed: that is how the frames are looked at on their own.
        /// </summary>
        private void TabChanged(ViewMode mode)
        {
            // Re-entered while turning the other chip off. That switch has its own report to
            // make and it would be the same one, so it is left to the call already in flight.
            if (settling)
            {
                return;
            }

            foreach (ViewMode other in NotUpWith(mode))
            {
                ScopeTab justSwitched;
                ScopeTab otherTab;
                if (tabsByMode.TryGetValue(mode, out justSwitched)
                    && tabsByMode.TryGetValue(other, out otherTab)
                    && justSwitched.IsActive && otherTab.IsActive)
                {
                    settling = true;
                    try
                    {
                        otherTab.IsActive = false;
                    }
                    finally
                    {
                        settling = false;
                    }
                }
            }

            onActiveChanged?.Invoke();
        }

        /// <summary>
        /// The tabs that cannot be up at the same time as this one. The two slab tabs are two
        /// cuts of one floor, and the SIDL_C area tab is a third set of regions over it; the two
        /// line load tabs are two questions about the same frames, a drawn line's value and a
        /// wall's weight, and read off one plan they would be taken for one answer.
        /// </summary>
        private static ViewMode[] NotUpWith(ViewMode mode)
        {
            switch (mode)
            {
                case ViewMode.Slabs: return new[] { ViewMode.SlabLoads, ViewMode.SidlCArea };
                case ViewMode.SlabLoads: return new[] { ViewMode.Slabs, ViewMode.SidlCArea };
                case ViewMode.SidlCArea: return new[] { ViewMode.Slabs, ViewMode.SlabLoads };
                case ViewMode.LineLoads: return new[] { ViewMode.SidlC };
                case ViewMode.SidlC: return new[] { ViewMode.LineLoads };
                default: return new ViewMode[0];
            }
        }

        /// <summary>True while the check this element type answers to is being read.</summary>
        public bool IsActive(ViewMode mode)
        {
            ScopeTab tab;
            return !tabsByMode.TryGetValue(mode, out tab) || tab.IsActive;
        }

        /// <summary>
        /// True while any check on this element type is being read. A piece of slab answers to
        /// two chips, so it stays coloured while either of them is on.
        /// </summary>
        public bool IsKindActive(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Beam: return IsActive(ViewMode.Beams);
                case ElementKind.Column: return IsActive(ViewMode.Columns);
                case ElementKind.Wall: return IsActive(ViewMode.Walls);
                case ElementKind.Slab: return IsActive(ViewMode.Slabs) || IsActive(ViewMode.SlabLoads);
                case ElementKind.LineLoad: return IsActive(ViewMode.LineLoads) || IsActive(ViewMode.SidlC);
                case ElementKind.HeightZone: return IsActive(ViewMode.HeightZones);
                default: return true;
            }
        }

        public ObservableCollection<LevelRow> Levels { get; }

        /// <summary>Rebuilds the rail when the story list changes, keeping the counts it has.</summary>
        public void SetStories(IEnumerable<Story_E> stories)
        {
            Levels.Clear();
            rowsByStory.Clear();

            // Top of the building first, the way a level list is read on a drawing.
            foreach (Story_E story in stories.OrderByDescending(s => s.Elevation))
            {
                LevelRow row = new LevelRow(story);
                Levels.Add(row);
                if (!string.IsNullOrEmpty(row.Name))
                {
                    rowsByStory[row.Name] = row;
                }
            }
        }

        /// <summary>
        /// Counts one rebuild's worth of elements. <paramref name="storyName"/> is the level on
        /// show; the tabs count only that level, the rail counts every level.
        /// </summary>
        public void Update(IEnumerable<ElementViewModel> elements, string storyName)
        {
            Dictionary<ViewMode, int> tabFails = new Dictionary<ViewMode, int>();
            Dictionary<ViewMode, int> tabReviews = new Dictionary<ViewMode, int>();
            Dictionary<string, int> levelFails = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> levelReviews = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (ElementViewModel element in elements)
            {
                if (element == null || element.IsContext)
                {
                    continue;
                }

                // An agreed difference is not work. It stays on the plan and stays in the
                // drawer, but the chip counts and the rail's bars are read to decide where to
                // go next, and an answered question does not belong in either.
                if (element.IsAccepted)
                {
                    continue;
                }

                // Nor is a difference on too small a patch of floor to be worth chasing. It
                // stays on the plan in the colour of what was found - that is the point of it,
                // and the card explains why the number beside it did not move - but the chips
                // and the rail say where to go next, and this is not somewhere to go.
                if (element.IsTooSmallToCount)
                {
                    continue;
                }

                // Unpaired is the slab one-model-only verdict; an element with nothing to
                // pair with is drawn as needs-a-look and counted as one, so the tab counts and
                // the colours on the plan say the same thing.
                bool isFail = element.DisplayState == CheckState.Failed
                              || element.DisplayState == CheckState.Unpaired;
                bool isReview = element.DisplayState == CheckState.Review;
                if (!isFail && !isReview)
                {
                    continue;
                }

                // The rail counts the whole building.
                string story = element.Story ?? "";
                Bump(isFail ? levelFails : levelReviews, story);

                // The tabs count the level on show, so the number on the tab is the number of
                // coloured things the plan would have on it.
                if (!IsOnStory(element, storyName))
                {
                    continue;
                }

                foreach (ViewMode mode in ModesFor(element))
                {
                    Bump(isFail ? tabFails : tabReviews, mode);
                }
            }

            foreach (ScopeTab tab in Tabs)
            {
                tab.Set(Get(tabFails, tab.Mode), Get(tabReviews, tab.Mode));
            }

            foreach (LevelRow row in Levels)
            {
                row.Set(Get(levelFails, row.Name), Get(levelReviews, row.Name));
                row.IsSelected = string.Equals(row.Name, storyName, StringComparison.OrdinalIgnoreCase);
            }

            // The bars share one scale across the building, so a second pass is needed: the
            // worst level is not known until every level has been counted.
            int worst = Levels.Count == 0 ? 1 : Levels.Max(row => row.Failed + row.Review);
            foreach (LevelRow row in Levels)
            {
                row.Rescale(worst);
            }
        }

        /// <summary>
        /// Which tab an element's findings belong under. Every kind maps to one tab except a
        /// piece of slab, which can be a finding on either slab tab depending on which check
        /// spoke - the loads are counted apart from the thickness and the outlines.
        /// </summary>
        private static IEnumerable<ViewMode> ModesFor(ElementViewModel element)
        {
            switch (element.Kind)
            {
                case ElementKind.Beam:
                    yield return ViewMode.Beams;
                    break;
                case ElementKind.Column:
                    yield return ViewMode.Columns;
                    break;
                case ElementKind.Wall:
                    yield return ViewMode.Walls;
                    break;
                case ElementKind.LineLoad:
                    yield return element.Tab ?? ViewMode.LineLoads;
                    break;
                case ElementKind.Slab:
                    bool loads = false;
                    bool slab = false;
                    foreach (CheckOutcome outcome in element.Outcomes)
                    {
                        if (outcome.Level != ResultLevel.Failed && outcome.Level != ResultLevel.Review)
                        {
                            continue;
                        }
                        if (outcome.Kind == CheckKind.SlabLoads)
                        {
                            loads = true;
                        }
                        else
                        {
                            slab = true;
                        }
                    }

                    // A piece that says which cut of the floor it came from counts on that cut's
                    // tab alone. The load cut carries the thickness verdict too, and both cuts
                    // are built together, so counting it on the slab tab as well would count
                    // every thickness finding twice.
                    if (element.Tab == ViewMode.SlabLoads)
                    {
                        if (loads)
                        {
                            yield return ViewMode.SlabLoads;
                        }
                        break;
                    }
                    if (element.Tab == ViewMode.Slabs)
                    {
                        yield return ViewMode.Slabs;
                        break;
                    }
                    if (element.Tab == ViewMode.SidlCArea)
                    {
                        yield return ViewMode.SidlCArea;
                        break;
                    }

                    // A zone with no outcome of its own still reads as a slab finding, so an
                    // unpaired piece is not lost between the two tabs.
                    if (slab || !loads)
                    {
                        yield return ViewMode.Slabs;
                    }
                    if (loads)
                    {
                        yield return ViewMode.SlabLoads;
                    }
                    break;
            }
        }

        private static bool IsOnStory(ElementViewModel element, string storyName)
        {
            if (string.IsNullOrEmpty(storyName) || string.IsNullOrEmpty(element.Story))
            {
                return true;
            }
            return string.Equals(element.Story, storyName, StringComparison.OrdinalIgnoreCase);
        }

        private static void Bump<T>(Dictionary<T, int> counts, T key)
        {
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + 1;
        }

        private static int Get<T>(Dictionary<T, int> counts, T key)
        {
            int value;
            return counts.TryGetValue(key, out value) ? value : 0;
        }
    }
}
