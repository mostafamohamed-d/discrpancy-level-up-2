using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One switch in the filter panel: whether a single check's single verdict is on show.
    /// Turning it off never removes an element from the plan, only that one result.
    /// </summary>
    public sealed class CheckResultFilter : ViewModelBase
    {
        private readonly Action onChanged;
        private bool isSelected = true;

        public CheckResultFilter(CheckKind kind, ResultLevel level, string label, VerdictColour swatch,
                                 Action onChanged)
        {
            Kind = kind;
            Level = level;
            Label = label;
            SwatchColour = swatch;
            this.onChanged = onChanged;
        }

        public CheckKind Kind { get; }
        public ResultLevel Level { get; }
        public string Label { get; }

        /// <summary>Which of the key's colours the square next to the switch is.</summary>
        public VerdictColour SwatchColour { get; }

        /// <summary>Colour of the square next to the switch, as the canvas would draw it.</summary>
        public string Swatch => VerdictColours.Hex(SwatchColour);

        /// <summary>Says the colour has changed, after somebody picked a new one off the key.</summary>
        internal void RefreshSwatch()
        {
            OnPropertyChanged(nameof(Swatch));
        }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value))
                {
                    onChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// One row of the filter panel: a switch for the check itself, and under it a switch per
    /// verdict it can reach. A row usually belongs to one check, but the slab rows put the two
    /// directions of the same question - what only ETABS has and what only Revit has - side by
    /// side, because that is how they are read.
    /// </summary>
    public sealed class CheckFilterGroup : ViewModelBase
    {
        public CheckFilterGroup(CheckKind kind, string label, IEnumerable<CheckResultFilter> options,
                                ViewMode mode = ViewMode.Columns)
        {
            Kind = kind;
            Label = label;
            Mode = mode;
            Options = new ObservableCollection<CheckResultFilter>(options);

            // The check's own switch is worked out from the verdicts below it rather than
            // stored, so it stays right however they were moved: by a click on one of them,
            // by the All results button, or by the settings file on the way in.
            foreach (CheckResultFilter option in Options)
            {
                option.PropertyChanged += OnOptionChanged;
            }
        }

        public CheckKind Kind { get; }
        public string Label { get; }

        /// <summary>Which tab this row belongs to, so only that tab's rows are shown.</summary>
        public ViewMode Mode { get; }

        public ObservableCollection<CheckResultFilter> Options { get; }

        /// <summary>
        /// The one switch for the whole check: on while every verdict under it is on, off while
        /// none is, and indeterminate in between. Setting it takes every verdict with it, which
        /// is what saves switching the colours off one at a time.
        /// </summary>
        public bool? IsSelected
        {
            get
            {
                if (Options.All(option => option.IsSelected))
                {
                    return true;
                }

                return Options.Any(option => option.IsSelected) ? (bool?)null : false;
            }
            set
            {
                // Indeterminate is something the verdicts say about themselves, never something
                // asked of them, so a null here is left alone.
                if (value == null)
                {
                    return;
                }

                foreach (CheckResultFilter option in Options)
                {
                    option.IsSelected = value.Value;
                }
            }
        }

        private void OnOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckResultFilter.IsSelected))
            {
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    /// <summary>
    /// Every result switch in the panel, and the answer to "is this result on show?".
    ///
    /// The switches are per check and per verdict rather than per element, which is what lets
    /// two results on the same column be controlled separately: hiding matched locations
    /// leaves that column's failed type mark exactly where it was.
    /// </summary>
    public sealed class CheckFilters
    {
        private readonly Dictionary<string, CheckResultFilter> byKey =
            new Dictionary<string, CheckResultFilter>();

        public CheckFilters(Action onChanged)
        {
            List<CheckFilterGroup> groups = new List<CheckFilterGroup>
            {
                Build(CheckKind.Location, "Location", onChanged),
                Build(CheckKind.Orientation, "Orientation", onChanged),
                Build(CheckKind.Shape, "Section shape", onChanged),
                Build(CheckKind.Dimensions, "Dimensions (long / short side)", onChanged),
                Build(CheckKind.TypeMark, "Type mark vs ETABS group", onChanged, withReview: true),
                Build(CheckKind.Beam, "Beam check", onChanged, mode: ViewMode.Beams),

                new CheckFilterGroup(CheckKind.WallLocation, "Wall on the right line", new[]
                {
                    new CheckResultFilter(CheckKind.WallLocation, ResultLevel.Passed,
                        "On the line", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.WallLocation, ResultLevel.Failed,
                        "Displaced from the Revit wall", MismatchedSwatch, onChanged)
                }, ViewMode.Walls),

                new CheckFilterGroup(CheckKind.WallLength, "Wall length vs the Revit wall", new[]
                {
                    new CheckResultFilter(CheckKind.WallLength, ResultLevel.Passed,
                        "Runs the full length", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.WallLength, ResultLevel.Review,
                        "ETABS stops short of it", ReviewSwatch, onChanged)
                }, ViewMode.Walls),

                new CheckFilterGroup(CheckKind.WallThickness, "Wall thickness", new[]
                {
                    new CheckResultFilter(CheckKind.WallThickness, ResultLevel.Passed,
                        "Same thickness", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.WallThickness, ResultLevel.Failed,
                        "Different thickness", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.WallThickness, ResultLevel.Review,
                        "No thickness to compare", ReviewSwatch, onChanged)
                }, ViewMode.Walls),

                new CheckFilterGroup(CheckKind.SlabThickness, "Slab thickness", new[]
                {
                    new CheckResultFilter(CheckKind.SlabThickness, ResultLevel.Passed,
                        "Same thickness", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabThickness, ResultLevel.Failed,
                        "Different thickness", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabThickness, ResultLevel.Review,
                        "Not flat, needs a look", ReviewSwatch, onChanged)
                }, ViewMode.Slabs),

                new CheckFilterGroup(CheckKind.SlabSystem, "Slab PT or RC", new[]
                {
                    new CheckResultFilter(CheckKind.SlabSystem, ResultLevel.Passed,
                        "Same in both models", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabSystem, ResultLevel.Failed,
                        "PT in one, RC in the other", MismatchedSwatch, onChanged)
                }, ViewMode.Slabs),

                new CheckFilterGroup(CheckKind.SlabMissingInRevit, "Slab outline does not agree", new[]
                {
                    new CheckResultFilter(CheckKind.SlabMissingInRevit, ResultLevel.Failed,
                        "Slab in ETABS only", EtabsOnlySwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabMissingInEtabs, ResultLevel.Failed,
                        "Floor in Revit only", RevitOnlySwatch, onChanged)
                }, ViewMode.Slabs),

                new CheckFilterGroup(CheckKind.SlabLoads, "Slab loads vs the loading plan", new[]
                {
                    new CheckResultFilter(CheckKind.SlabLoads, ResultLevel.Passed,
                        "Loads match the plan", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabLoads, ResultLevel.Failed,
                        "Loads do not match", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabLoads, ResultLevel.Review,
                        "Nothing to check it against", ReviewSwatch, onChanged)
                }, ViewMode.SlabLoads),

                new CheckFilterGroup(CheckKind.SlabOpeningInRevit, "Openings in one model only", new[]
                {
                    new CheckResultFilter(CheckKind.SlabOpeningInRevit, ResultLevel.Failed,
                        "Opening in Revit, none in ETABS", EtabsOnlySwatch, onChanged),
                    new CheckResultFilter(CheckKind.SlabOpeningInEtabs, ResultLevel.Failed,
                        "Opening in ETABS, none in Revit", RevitOnlySwatch, onChanged)
                }, ViewMode.Slabs),

                new CheckFilterGroup(CheckKind.LineLoads, "Line loads vs the loading plan", new[]
                {
                    new CheckResultFilter(CheckKind.LineLoads, ResultLevel.Passed,
                        "Load matches the plan", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.LineLoads, ResultLevel.Failed,
                        "Load does not match, or is missing", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.LineLoads, ResultLevel.Review,
                        "Same total, put on differently", ReviewSwatch, onChanged)
                }, ViewMode.LineLoads),

                new CheckFilterGroup(CheckKind.LineLoadMissingInEtabs, "Line load in one model only", new[]
                {
                    new CheckResultFilter(CheckKind.LineLoadMissingInEtabs, ResultLevel.Failed,
                        "Drawn on the plan, not in ETABS", RevitOnlySwatch, onChanged),
                    new CheckResultFilter(CheckKind.LineLoadMissingInRevit, ResultLevel.Failed,
                        "On an ETABS frame, not on the plan", EtabsOnlySwatch, onChanged)
                }, ViewMode.LineLoads),

                new CheckFilterGroup(CheckKind.SidlC, "SIDL_C vs the walls' weight", new[]
                {
                    new CheckResultFilter(CheckKind.SidlC, ResultLevel.Passed,
                        "Load matches the weight", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlC, ResultLevel.Failed,
                        "Load does not match, or is missing", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlC, ResultLevel.Review,
                        "Same total, put on differently", ReviewSwatch, onChanged)
                }, ViewMode.SidlC),

                new CheckFilterGroup(CheckKind.SidlCMissingInEtabs, "SIDL_C in one model only", new[]
                {
                    new CheckResultFilter(CheckKind.SidlCMissingInEtabs, ResultLevel.Failed,
                        "Wall in Revit, no SIDL_C in ETABS", RevitOnlySwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlCMissingInRevit, ResultLevel.Failed,
                        "SIDL_C on an ETABS frame, no wall in Revit", EtabsOnlySwatch, onChanged)
                }, ViewMode.SidlC),

                new CheckFilterGroup(CheckKind.SidlCArea, "SIDL_C vs the fill's weight", new[]
                {
                    new CheckResultFilter(CheckKind.SidlCArea, ResultLevel.Passed,
                        "Average load matches the fill", MatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlCArea, ResultLevel.Failed,
                        "Average load does not match, or is beside the fill", MismatchedSwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlCArea, ResultLevel.Review,
                        "Same total, put on a different area", ReviewSwatch, onChanged)
                }, ViewMode.SidlCArea),

                new CheckFilterGroup(CheckKind.SidlCAreaMissingInEtabs, "SIDL_C area in one model only", new[]
                {
                    new CheckResultFilter(CheckKind.SidlCAreaMissingInEtabs, ResultLevel.Failed,
                        "Fill in Revit, no SIDL_C in ETABS", RevitOnlySwatch, onChanged),
                    new CheckResultFilter(CheckKind.SidlCAreaMissingInRevit, ResultLevel.Failed,
                        "SIDL_C on an ETABS area, no fill in Revit", EtabsOnlySwatch, onChanged)
                }, ViewMode.SidlCArea)
            };

            Groups = new ObservableCollection<CheckFilterGroup>(groups);

            foreach (CheckResultFilter filter in Groups.SelectMany(group => group.Options))
            {
                byKey[Key(filter.Kind, filter.Level)] = filter;
            }
        }

        public ObservableCollection<CheckFilterGroup> Groups { get; }

        /// <summary>
        /// True while this verdict of this check is switched on. A check with no switch of its
        /// own - the identifying fields, and anything a future check adds before it gets one -
        /// is always on show.
        /// </summary>
        public bool IsShown(CheckKind kind, ResultLevel level)
        {
            CheckResultFilter filter;
            return !byKey.TryGetValue(Key(kind, level), out filter) || filter.IsSelected;
        }

        /// <summary>
        /// True while nothing is being held back. The panel these switches used to live in was
        /// always on screen, so a switched-off result was visible as a switched-off result;
        /// behind a button it is not, and the button has to say so itself.
        /// </summary>
        public bool AllShown
        {
            get { return byKey.Values.All(filter => filter.IsSelected); }
        }

        /// <summary>Redraws every swatch, after a colour on the key has been changed.</summary>
        public void RefreshSwatches()
        {
            foreach (CheckResultFilter filter in Groups.SelectMany(group => group.Options))
            {
                filter.RefreshSwatch();
            }
        }

        public void SetAll(bool selected)
        {
            foreach (CheckResultFilter filter in byKey.Values)
            {
                filter.IsSelected = selected;
            }
        }

        /// <summary>
        /// The switches that are off, for the settings file. Only the off ones are written
        /// because everything starts on: a switch added to a later version is then on by
        /// default rather than off, which is the safer way round for something that decides
        /// whether a result is seen at all.
        /// </summary>
        public IEnumerable<string> SwitchedOffKeys()
        {
            return byKey.Where(entry => !entry.Value.IsSelected).Select(entry => entry.Key).ToList();
        }

        /// <summary>Turns off the switches named, and leaves the rest as they are.</summary>
        public void ApplySwitchedOff(IEnumerable<string> keys)
        {
            if (keys == null)
            {
                return;
            }

            foreach (string key in keys)
            {
                CheckResultFilter filter;
                if (byKey.TryGetValue(key, out filter))
                {
                    filter.IsSelected = false;
                }
            }
        }

        // The colours the canvas draws each verdict in, so a switch and what it controls
        // always read as the same thing - whatever colours they have been given.
        private const VerdictColour MatchedSwatch = VerdictColour.Passed;
        private const VerdictColour MismatchedSwatch = VerdictColour.Failed;
        private const VerdictColour ReviewSwatch = VerdictColour.Review;
        private const VerdictColour EtabsOnlySwatch = VerdictColour.EtabsOnly;
        private const VerdictColour RevitOnlySwatch = VerdictColour.RevitOnly;

        private static CheckFilterGroup Build(CheckKind kind, string label, Action onChanged,
                                              bool withReview = false,
                                              ViewMode mode = ViewMode.Columns)
        {
            List<CheckResultFilter> options = new List<CheckResultFilter>
            {
                new CheckResultFilter(kind, ResultLevel.Passed, "Matched", MatchedSwatch, onChanged),
                new CheckResultFilter(kind, ResultLevel.Failed, "Mismatched", MismatchedSwatch, onChanged)
            };

            if (withReview)
            {
                options.Add(new CheckResultFilter(kind, ResultLevel.Review, "Multiple groups",
                    ReviewSwatch, onChanged));
            }

            return new CheckFilterGroup(kind, label, options, mode);
        }

        private static string Key(CheckKind kind, ResultLevel level)
        {
            return kind + "|" + level;
        }
    }
}
