using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// One Revit element the selected ETABS element could be paired with, and how far away it
    /// is. The distance is the whole reason the list is worth showing: the automatic pass takes
    /// the nearest, so seeing what came second is seeing why it chose what it chose.
    /// </summary>
    public sealed class PairCandidate
    {
        public PairCandidate(long revitId, string description, double distanceMM, bool isCurrent)
        {
            RevitId = revitId;
            Description = description ?? "";
            DistanceMM = distanceMM;
            IsCurrent = isCurrent;
        }

        public long RevitId { get; }
        public string Description { get; }
        public double DistanceMM { get; }

        /// <summary>True for the one it is paired with now, so the list says where it stands.</summary>
        public bool IsCurrent { get; }

        public string DistanceText => Math.Round(DistanceMM) + " mm";

        public string Title => RevitId.ToString("N0",
            System.Globalization.CultureInfo.InvariantCulture);

        public string Note => IsCurrent
            ? Description + (Description.Length > 0 ? " · " : "") + "paired now"
            : Description;
    }

    /// <summary>
    /// The pairing editor on the element card: which Revit element this one really goes with.
    ///
    /// The pair the check guessed is never final. It is right nearly always and wrong in the
    /// cases worth arguing about - two columns a hand's width apart, a wall the two models
    /// split differently - and those are exactly the cases where someone looks at the model and
    /// knows the answer. This is where that answer is written down, and because it goes into
    /// the override store it is written down once rather than after every run.
    /// </summary>
    public sealed class PairingEditor : ViewModelBase
    {
        private readonly Func<ElementViewModel, IList<PairCandidate>> findCandidates;
        private readonly Action<ElementViewModel, long?> apply;

        private bool isOpen;
        private ElementViewModel element;
        private PairCandidate selected;
        private string typedId = "";

        public PairingEditor(Func<ElementViewModel, IList<PairCandidate>> findCandidates,
                             Action<ElementViewModel, long?> apply)
        {
            this.findCandidates = findCandidates;
            this.apply = apply;

            OpenCommand = new RelayCommand(e => Open(e as ElementViewModel), e => CanEdit(e as ElementViewModel));
            CancelCommand = new RelayCommand(_ => IsOpen = false);
            ApplyCommand = new RelayCommand(_ => Commit(), _ => HasChoice);
            UnpairCommand = new RelayCommand(_ => Unpair(), _ => element != null);
        }

        public ObservableCollection<PairCandidate> Candidates { get; }
            = new ObservableCollection<PairCandidate>();

        public ICommand OpenCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand UnpairCommand { get; }

        public bool IsOpen
        {
            get => isOpen;
            private set => SetProperty(ref isOpen, value);
        }

        public PairCandidate Selected
        {
            get => selected;
            set
            {
                if (SetProperty(ref selected, value))
                {
                    OnPropertyChanged(nameof(HasChoice));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// An element id typed in rather than picked off the list, for the counterpart that is
        /// too far away to be a candidate - which is the case the search radius exists to
        /// exclude and the one a person is most likely to be correcting.
        /// </summary>
        public string TypedId
        {
            get => typedId;
            set
            {
                if (SetProperty(ref typedId, value ?? ""))
                {
                    OnPropertyChanged(nameof(HasChoice));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasChoice => Selected != null || ParsedTypedId() != null;

        public string Title => element == null
            ? ""
            : "Pair " + (element.EtabsUniqueName.Length > 0
                            ? element.EtabsUniqueName
                            : element.Identifier)
              + " with which Revit element";

        /// <summary>
        /// Why the list stops where it does. Said out loud because an empty list otherwise
        /// reads as "there is nothing there" when it means "nothing within the radius".
        /// </summary>
        public string Footnote => Candidates.Count == 0
            ? "Nothing within the search radius. Type the element id instead, or widen the "
              + "radius under Set up."
            : "Sorted by distance from the ETABS element. Anything past the search radius is "
              + "not listed - type its id to pair with it anyway.";

        /// <summary>
        /// Whether this element can be re-paired at all. A slab zone cannot: it is a piece of
        /// geometry where two outlines overlap rather than an element paired with an element,
        /// so there is no pair here to correct. Nor can an element that came from Revit alone -
        /// a pairing is written from the ETABS side, which that has not got.
        /// </summary>
        public static bool CanEdit(ElementViewModel candidate)
        {
            return IsPairableKind(candidate) && candidate.EtabsUniqueName.Length > 0;
        }

        /// <summary>
        /// Whether this element can take a side in a pairing gathered off the plan - the ETABS
        /// side if it has a name, the Revit side if it has an id, and both if it is already
        /// paired. Wider than <see cref="CanEdit"/> on purpose: that one asks whose pairing is
        /// being written, and this one asks who is allowed in the room.
        /// </summary>
        public static bool CanBeMarked(ElementViewModel candidate)
        {
            return IsPairableKind(candidate)
                   && !candidate.IsDrawnAsContext
                   && (candidate.EtabsUniqueName.Length > 0 || candidate.RevitId > 0);
        }

        /// <summary>
        /// Whether pairing means anything for this kind at all. Written once and asked by both
        /// tests above, so the slab exclusion cannot come to be true of one and not the other.
        /// </summary>
        private static bool IsPairableKind(ElementViewModel candidate)
        {
            // Never on the raw data either, which is each model as it was read: nothing on the
            // plan is paired while it is up, so a pairing made there would be made blind.
            // Nor on the SIDL_C tab: a wall weighed as a load is laid on the frames under it by
            // where it stands, and a pairing written there would land among the loading plan's.
            return candidate != null && candidate.Kind != ElementKind.Slab
                   && candidate.Kind != ElementKind.HeightZone && !candidate.IsRaw
                   && candidate.Tab != ViewMode.SidlC;
        }

        private void Open(ElementViewModel target)
        {
            if (!CanEdit(target))
            {
                return;
            }

            element = target;
            Selected = null;
            TypedId = "";

            Candidates.Clear();
            foreach (PairCandidate candidate in findCandidates(target) ?? new List<PairCandidate>())
            {
                Candidates.Add(candidate);
            }

            IsOpen = true;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Footnote));
        }

        private void Commit()
        {
            long? chosen = Selected != null ? Selected.RevitId : ParsedTypedId();
            if (chosen == null)
            {
                return;
            }

            IsOpen = false;
            apply(element, chosen);
        }

        private void Unpair()
        {
            IsOpen = false;
            // Null is not "no opinion" here - it is the opinion that this element pairs with
            // nothing, which the store keeps apart from having said nothing at all.
            apply(element, null);
        }

        private long? ParsedTypedId()
        {
            // Typed by a person reading an id off Revit, so the spaces and separators that come
            // with a copied number are thrown away rather than refused.
            string digits = new string((typedId ?? "").Where(char.IsDigit).ToArray());
            long id;
            return digits.Length > 0
                   && long.TryParse(digits, out id)
                   && id > 0
                ? (long?)id
                : null;
        }
    }
}
