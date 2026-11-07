using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One tickable entry in the label picker. Ticking it makes that comparison result
    /// appear on top of every element that carries it.
    /// </summary>
    public class LabelFieldOption : ViewModelBase
    {
        private bool isSelected;
        private readonly Action onSelectionChanged;

        public LabelFieldOption(string key, string label, Action onSelectionChanged)
        {
            Key = key;
            Label = label;
            this.onSelectionChanged = onSelectionChanged;
        }

        public string Key { get; }
        public string Label { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (SetProperty(ref isSelected, value))
                {
                    onSelectionChanged?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// The label picker for one kind of element.
    ///
    /// A column and a beam both publish a result called "Check state", a column, a beam, a wall
    /// and a slab all publish one called "Revit id". Listed together they are four identical
    /// lines with no way to tell which is which, and ticking one of them stamps a value on one
    /// kind of element while the other three ticks sit there doing nothing to it. The values
    /// were always kept apart - the keys read Column.State, Frame.State - so this is only the
    /// picker catching up with them.
    /// </summary>
    public sealed class LabelFieldGroup : ViewModelBase
    {
        public LabelFieldGroup(ElementKind kind, string title, IEnumerable<LabelFieldOption> options)
        {
            Kind = kind;
            Title = title;
            Options = new ObservableCollection<LabelFieldOption>(options);
            ClearCommand = new RelayCommand(_ => Clear(), _ => SelectedCount > 0);

            foreach (LabelFieldOption option in Options)
            {
                option.PropertyChanged += OnOptionChanged;
            }
        }

        public ElementKind Kind { get; }

        /// <summary>What this group is called on screen: Columns, Beams, Walls, Slab loads.</summary>
        public string Title { get; }

        public ObservableCollection<LabelFieldOption> Options { get; }

        /// <summary>Clears this group without touching the ticks belonging to the others.</summary>
        public ICommand ClearCommand { get; }

        public int SelectedCount
        {
            get { return Options.Count(option => option.IsSelected); }
        }

        public bool HasSelection
        {
            get { return SelectedCount > 0; }
        }

        /// <summary>How many of this group are stamped, so a collapsed group still says.</summary>
        public string CountText
        {
            get { return SelectedCount == 0 ? "" : SelectedCount.ToString(); }
        }

        private void Clear()
        {
            foreach (LabelFieldOption option in Options)
            {
                option.IsSelected = false;
            }
        }

        private void OnOptionChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LabelFieldOption.IsSelected))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CountText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Turning what is on the plan into the picker's groups. Out here rather than in the view
    /// model because it is the whole of the rule the picker was getting wrong, and testing it
    /// against a couple of stub elements is a great deal cheaper than against a live Revit
    /// document.
    /// </summary>
    public static class LabelGrouping
    {
        /// <summary>Kinds in the order the chips along the top put them, so the two agree.</summary>
        public static readonly ElementKind[] Order =
        {
            ElementKind.Column, ElementKind.Beam, ElementKind.Wall, ElementKind.Slab,
            ElementKind.LineLoad, ElementKind.HeightZone
        };

        /// <summary>
        /// One group per kind of element on the plan, holding the results that kind publishes.
        ///
        /// Bucketed by the element's own <see cref="ElementViewModel.Kind"/> rather than by
        /// anything read out of the key. Context is left out: it is drawn grey and unlabelled,
        /// so a tick against it would do nothing at all.
        /// </summary>
        public static IList<LabelFieldGroup> Build(IEnumerable<ElementViewModel> elements,
                                                   Func<ElementKind, string> titleFor,
                                                   Func<string, bool> isTicked,
                                                   Action onSelectionChanged)
        {
            Dictionary<ElementKind, List<ResultField>> byKind =
                new Dictionary<ElementKind, List<ResultField>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (ElementViewModel element in elements)
            {
                if (element.IsDrawnAsContext)
                {
                    continue;
                }

                foreach (ResultField field in element.ResultFields)
                {
                    if (!seen.Add(field.Key))
                    {
                        continue;
                    }

                    List<ResultField> bucket;
                    if (!byKind.TryGetValue(element.Kind, out bucket))
                    {
                        bucket = new List<ResultField>();
                        byKind[element.Kind] = bucket;
                    }

                    bucket.Add(field);
                }
            }

            List<LabelFieldGroup> groups = new List<LabelFieldGroup>();
            foreach (ElementKind kind in Order)
            {
                List<ResultField> fields;
                if (!byKind.TryGetValue(kind, out fields))
                {
                    continue;
                }

                List<LabelFieldOption> options = new List<LabelFieldOption>();
                foreach (ResultField field in fields.OrderBy(f => f.Label, StringComparer.CurrentCulture))
                {
                    LabelFieldOption option =
                        new LabelFieldOption(field.Key, field.Label, onSelectionChanged);
                    option.IsSelected = isTicked(field.Key);
                    options.Add(option);
                }

                groups.Add(new LabelFieldGroup(kind, titleFor(kind), options));
            }

            return groups;
        }
    }
}
