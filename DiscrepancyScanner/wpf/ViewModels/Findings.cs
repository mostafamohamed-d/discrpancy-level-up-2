using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// How a check and a verdict are named and coloured wherever they are printed - on the
    /// element card, in the findings list, beside a filter switch.
    ///
    /// One place, so the same check cannot end up called two different things in two different
    /// panels, which is how a reader stops trusting either.
    /// </summary>
    public static class CheckNaming
    {
        public static string LabelOf(CheckKind kind)
        {
            switch (kind)
            {
                case CheckKind.Pairing: return "Nothing to pair with";
                case CheckKind.Location: return "Location";
                case CheckKind.Orientation: return "Orientation";
                case CheckKind.Shape: return "Section shape";
                case CheckKind.Dimensions: return "Dimensions";
                case CheckKind.TypeMark: return "Type mark";
                case CheckKind.Beam: return "Beam check";
                case CheckKind.WallLocation: return "Wall on the line";
                case CheckKind.WallThickness: return "Wall thickness";
                case CheckKind.WallLength: return "Wall length";
                case CheckKind.SlabThickness: return "Slab thickness";
                case CheckKind.SlabSystem: return "PT or RC";
                case CheckKind.SlabMissingInRevit: return "Slab in ETABS only";
                case CheckKind.SlabMissingInEtabs: return "Floor in Revit only";
                case CheckKind.SlabOpeningInRevit: return "Opening in Revit only";
                case CheckKind.SlabOpeningInEtabs: return "Opening in ETABS only";
                case CheckKind.SlabLoads: return "Slab loads";
                case CheckKind.LineLoads: return "Line load";
                case CheckKind.LineLoadMissingInEtabs: return "Line load in Revit only";
                case CheckKind.LineLoadMissingInRevit: return "Line load in ETABS only";
                case CheckKind.SidlC: return "SIDL_C load";
                case CheckKind.SidlCMissingInEtabs: return "SIDL_C in Revit only";
                case CheckKind.SidlCMissingInRevit: return "SIDL_C in ETABS only";
                case CheckKind.SidlCArea: return "SIDL_C area load";
                case CheckKind.SidlCAreaMissingInEtabs: return "Fill in Revit only";
                case CheckKind.SidlCAreaMissingInRevit: return "SIDL_C area in ETABS only";
                default: return "Check";
            }
        }

        /// <summary>
        /// What to call one result. Usually the check's own name, but a check that asks the
        /// same question in several places along one element - the reinforcement stations on a
        /// beam, the four loads on a piece of slab - names the place instead, because the whole
        /// point of the row is saying where to go and look.
        /// </summary>
        public static string LabelOf(CheckOutcome outcome)
        {
            return outcome.Label.Length > 0 ? outcome.Label : LabelOf(outcome.Kind);
        }

        /// <summary>
        /// The colour the canvas draws this verdict in, so a row and the plan always agree.
        ///
        /// Pairing is the one check whose failure is not drawn red. An element with nothing to
        /// pair with is not the two models disagreeing about something they both have - it is
        /// one model having something the other does not - so it is drawn in the colour of the
        /// model that has it, the same as a patch of slab only one model knows about.
        /// </summary>
        public static string SwatchOf(CheckKind kind, ResultLevel level, ElementViewModel element)
        {
            if (kind == CheckKind.Pairing && level == ResultLevel.Failed)
            {
                return VerdictColours.Hex(element != null && element.Source == ElementSource.UnmatchedRevit
                    ? VerdictColour.RevitOnly
                    : VerdictColour.EtabsOnly);
            }

            switch (level)
            {
                case ResultLevel.Failed: return VerdictColours.Hex(VerdictColour.Failed);
                case ResultLevel.Review: return VerdictColours.Hex(VerdictColour.Review);
                default: return VerdictColours.Hex(VerdictColour.Passed);
            }
        }

        /// <summary>
        /// The colour an element is drawn in overall, for a list that shows elements rather than
        /// single results - the chooser under the cursor, where what matters is telling a failed
        /// column from a passed one at a glance.
        ///
        /// Something only one model has is in that model's colour, the same as on the plan -
        /// which is why this needs the element and not only its state.
        /// </summary>
        public static string SwatchOfState(CheckState state, ElementSource source)
        {
            switch (state)
            {
                case CheckState.Failed: return VerdictColours.Hex(VerdictColour.Failed);
                case CheckState.Review: return VerdictColours.Hex(VerdictColour.Review);
                case CheckState.Passed: return VerdictColours.Hex(VerdictColour.Passed);
                case CheckState.Unpaired:
                    return VerdictColours.Hex(source == ElementSource.UnmatchedRevit
                        ? VerdictColour.RevitOnly
                        : VerdictColour.EtabsOnly);
                default: return "#FFAAAAAA";
            }
        }

        /// <summary>
        /// Whether what somebody said about an element is still about the element in front of
        /// them. A verdict is recorded against a fingerprint of the results it was made on, so
        /// a run that changes any of them leaves the verdict standing but no longer settled -
        /// otherwise an old agreement would quietly bury a finding nobody has seen.
        ///
        /// Only on an element that still disagrees. One that agrees now has nothing on it to
        /// look at again, so an old review or acceptance of it is simply over rather than
        /// asking for another look.
        /// </summary>
        public static bool IsStale(ReviewNote said, ElementViewModel element)
        {
            return said != null && said.Reviewed && Disagrees(element)
                   && said.Fingerprint != element.VerdictFingerprint;
        }

        /// <summary>
        /// True when the element has something in it to accept: a check that failed or wants a
        /// look, or no counterpart in the other model. Accepting says a difference is real and
        /// agreed, so an element that agrees has nothing to accept.
        ///
        /// Asked of the results as well as of the overall verdict, and either is enough. They
        /// ought always to say the same, but a piece of the slab load cut once had its verdict
        /// from the slab check alone - "not checked" - while its SDL failed, and ACCEPT stayed
        /// greyed out over a red piece. Whatever an element's verdict is made of, a failure it
        /// carries is something to accept.
        /// </summary>
        public static bool Disagrees(ElementViewModel element)
        {
            return element != null
                   && (element.State == CheckState.Failed
                       || element.State == CheckState.Review
                       || element.State == CheckState.Unpaired
                       || element.Outcomes.Any(outcome => outcome.Kind != CheckKind.None
                                                          && (outcome.Level == ResultLevel.Failed
                                                              || outcome.Level == ResultLevel.Review)));
        }

        /// <summary>
        /// Why an acceptance has to be taken back rather than left standing as stale, or null
        /// while it may stand.
        ///
        /// Two things end one outright. The element has moved, changed size or changed partner
        /// in either model, so the difference that was agreed is not the one there now. Or it
        /// was accepted with no counterpart at all and has since been paired, so there is now a
        /// value to check that nobody has looked at. Any other change to its results leaves the
        /// acceptance stale rather than gone - a check switched off under Set up changes the
        /// results as well, and must not cost anybody what they agreed.
        ///
        /// Only on a checked element. One read but not compared yet, or drawn raw, carries
        /// nothing an acceptance could be held to.
        /// </summary>
        public static string WhyAcceptanceIsOvertaken(ReviewNote said, ElementViewModel element)
        {
            if (said == null || said.Verdict != HumanVerdict.Accepted || element == null
                || element.State == CheckState.NotChecked)
            {
                return null;
            }

            if (SaidUnpaired(said.Fingerprint) && element.State != CheckState.Unpaired)
            {
                return "accepted with no counterpart, and paired now - its value needs checking";
            }

            string placeThen = PlaceIn(said.Fingerprint);
            if (placeThen.Length > 0 && element.PlaceFingerprint.Length > 0
                && placeThen != element.PlaceFingerprint)
            {
                return "moved, resized or paired differently since it was accepted";
            }

            return null;
        }

        /// <summary>
        /// True when a fingerprint was taken off an element with no counterpart: one of its
        /// results says the other model has nothing there.
        /// </summary>
        public static bool SaidUnpaired(string fingerprint)
        {
            string results = ResultsIn(fingerprint);
            if (results.Length == 0)
            {
                return false;
            }

            // Each result is "kind:level" with an optional ":token", and tokens are fixed words,
            // so the first two numbers of each are its kind and its level. "Not checked" is
            // left out: an element nobody compared yet is not one found to have no counterpart.
            foreach (string result in results.Split(','))
            {
                string[] parts = result.Split(':');
                int kind, level;
                if (parts.Length >= 2
                    && int.TryParse(parts[0], out kind) && int.TryParse(parts[1], out level)
                    && IsUnpairedFinding((CheckKind)kind)
                    && (ResultLevel)level != ResultLevel.NotChecked)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The results that say an element has no counterpart in the other model.</summary>
        public static bool IsUnpairedFinding(CheckKind kind)
        {
            return kind == CheckKind.Pairing
                   || kind == CheckKind.LineLoadMissingInEtabs
                   || kind == CheckKind.LineLoadMissingInRevit
                   || kind == CheckKind.SidlCMissingInEtabs
                   || kind == CheckKind.SidlCMissingInRevit
                   || kind == CheckKind.SidlCAreaMissingInEtabs
                   || kind == CheckKind.SidlCAreaMissingInRevit;
        }

        /// <summary>The results half of a fingerprint.</summary>
        private static string ResultsIn(string fingerprint)
        {
            string text = fingerprint ?? "";
            int at = text.IndexOf(ElementViewModel.PlaceMarker, StringComparison.Ordinal);
            return at < 0 ? text : text.Substring(0, at);
        }

        /// <summary>The place half of a fingerprint, or empty on one taken without a place.</summary>
        private static string PlaceIn(string fingerprint)
        {
            string text = fingerprint ?? "";
            int at = text.IndexOf(ElementViewModel.PlaceMarker, StringComparison.Ordinal);
            return at < 0 ? "" : text.Substring(at + ElementViewModel.PlaceMarker.Length);
        }

        /// <summary>
        /// What somebody has said about an element, counting only what is still about it.
        ///
        /// The one place the staleness rule is applied. The card, the drawer and the plan all
        /// read a verdict, and three copies of this test would be three chances for one of them
        /// to settle a finding the other two still show as open.
        /// </summary>
        public static HumanVerdict VerdictOn(ReviewNote said, ElementViewModel element)
        {
            if (said == null || !said.Reviewed || said.Fingerprint != element.VerdictFingerprint)
            {
                return HumanVerdict.None;
            }

            // An acceptance of an element that agrees settles nothing, so it is not shown as
            // one. It is left in the file rather than wiped: the element may only agree because
            // a check is switched off, and switching it back on brings the difference back.
            if (said.Verdict == HumanVerdict.Accepted && !Disagrees(element))
            {
                return HumanVerdict.None;
            }
            return said.Verdict;
        }

        /// <summary>Which of the two models an element came from, for the "sits with" column.</summary>
        public static string ModelOf(ElementViewModel element)
        {
            // Drawn on the plan by hand, so neither model has it.
            if (element.Kind == ElementKind.HeightZone)
            {
                return "Drawn by hand";
            }

            switch (element.Source)
            {
                case ElementSource.EtabsOnly:
                case ElementSource.UnmatchedEtabs: return "ETABS";
                case ElementSource.RevitOnly:
                case ElementSource.UnmatchedRevit: return "Revit";
                default: return "Both";
            }
        }

        /// <summary>
        /// Whether an element has a counterpart in the other model: paired with one, or - for a
        /// line load, which is read against frames rather than paired with an element - read
        /// against something.
        ///
        /// Not simply whether it says what it is paired with, because a line load always says
        /// something there, including that nothing in ETABS runs under it. An element carrying
        /// the finding that the other model has nothing here is the opposite of paired.
        /// </summary>
        public static bool IsPaired(ElementViewModel element)
        {
            if (element.Source == ElementSource.Paired)
            {
                return true;
            }
            if (element.Source == ElementSource.UnmatchedEtabs || element.Source == ElementSource.UnmatchedRevit)
            {
                return false;
            }

            return element.PairedWith.Length > 0
                   && !element.Outcomes.Any(outcome => outcome.Level == ResultLevel.Failed
                                                       && SaysTheOtherModelHasNothing(outcome.Kind));
        }

        private static bool SaysTheOtherModelHasNothing(CheckKind kind)
        {
            return kind == CheckKind.Pairing
                   || kind == CheckKind.LineLoadMissingInEtabs
                   || kind == CheckKind.LineLoadMissingInRevit
                   || kind == CheckKind.SidlCMissingInEtabs
                   || kind == CheckKind.SidlCMissingInRevit
                   || kind == CheckKind.SidlCAreaMissingInEtabs
                   || kind == CheckKind.SidlCAreaMissingInRevit;
        }

        /// <summary>How bad a verdict is, for picking the worst of several.</summary>
        public static int Severity(ResultLevel level)
        {
            switch (level)
            {
                case ResultLevel.Failed: return 3;
                case ResultLevel.Review: return 2;
                case ResultLevel.Passed: return 1;
                default: return 0;
            }
        }

        /// <summary>
        /// The one value several elements share, or "Varies" where they do not. Blank counts as
        /// a value: an element that has nothing to say about a field is not agreeing with the
        /// ones that do.
        /// </summary>
        public static string Same(IEnumerable<string> values)
        {
            List<string> distinct = values.Select(value => value ?? "").Distinct().ToList();
            return distinct.Count == 1 ? distinct[0] : "Varies";
        }

        /// <summary>"3 fail · 2 agree", the way a set of verdicts splits.</summary>
        public static string Split(IEnumerable<ResultLevel> levels)
        {
            List<ResultLevel> all = levels.ToList();
            List<string> parts = new List<string>();

            int failed = all.Count(level => level == ResultLevel.Failed);
            int review = all.Count(level => level == ResultLevel.Review);
            int passed = all.Count(level => level == ResultLevel.Passed);
            int other = all.Count - failed - review - passed;

            if (failed > 0) parts.Add(failed + " fail");
            if (review > 0) parts.Add(review + " to look at");
            if (passed > 0) parts.Add(passed + " agree");
            if (other > 0) parts.Add(other + " not checked");

            return string.Join(" · ", parts);
        }

        /// <summary>The plural a kind is counted in: "3 beams", "2 slab zones".</summary>
        public static string KindsOf(ElementViewModel element)
        {
            return KindOf(element) + "s";
        }

        public static string KindOf(ElementViewModel element)
        {
            switch (element.Kind)
            {
                case ElementKind.Beam: return "Beam";
                case ElementKind.Column: return "Column";
                case ElementKind.Wall: return "Wall";
                case ElementKind.LineLoad: return element.Tab == ViewMode.SidlC ? "SIDL_C load" : "Line load";
                case ElementKind.HeightZone: return "Height zone";
                // A zone is a piece the check cut; the raw data is whole areas, as read.
                default:
                    if (element.Tab == ViewMode.SidlCArea)
                    {
                        return element.Source == ElementSource.UnmatchedEtabs ? "SIDL_C area" : "Fill";
                    }
                    return element.IsRaw ? "Slab" : "Slab zone";
            }
        }
    }

    /// <summary>
    /// One check's verdict on the selected element, as the card prints it: what was asked, what
    /// the answer was, and the colour the plan is drawing it in.
    /// </summary>
    public sealed class CheckRow
    {
        public CheckRow(ElementViewModel element, CheckOutcome outcome, bool isHidden)
        {
            Kind = outcome.Kind;
            Label = CheckNaming.LabelOf(outcome);
            Summary = outcome.Summary ?? "";
            Level = outcome.Level;
            Swatch = CheckNaming.SwatchOf(outcome.Kind, outcome.Level, element);
            IsHidden = isHidden;
            Etabs = outcome.Etabs;
            Revit = outcome.Revit;
            IsFailure = outcome.Level == ResultLevel.Failed;

            // The sentence as well as the two values wherever the values cannot say why: a
            // verdict that is not a pass with the same number on both sides, and a line load
            // that is not a pass, whose two numbers are averages over a line while the verdict
            // was reached stretch by stretch or on the total.
            ShowsSummary = !HasSides
                           || (Level != ResultLevel.Passed
                               && (Etabs == Revit || IsLineLoad(Kind)));
        }

        private static bool IsLineLoad(CheckKind kind)
        {
            return kind == CheckKind.LineLoads
                   || kind == CheckKind.LineLoadMissingInEtabs
                   || kind == CheckKind.LineLoadMissingInRevit
                   || kind == CheckKind.SidlC
                   || kind == CheckKind.SidlCMissingInEtabs
                   || kind == CheckKind.SidlCMissingInRevit
                   || kind == CheckKind.SidlCArea
                   || kind == CheckKind.SidlCAreaMissingInEtabs
                   || kind == CheckKind.SidlCAreaMissingInRevit;
        }

        /// <summary>
        /// One check across several selected elements. The worst verdict gives the colour, a
        /// value the elements all agree on is printed and one they do not reads "Varies", and
        /// the line under it says how the verdicts split - which is what a row standing for
        /// twelve beams has to say that a row for one does not.
        /// </summary>
        public CheckRow(string label, IList<CheckRow> rows, int selected)
        {
            CheckRow worst = rows.OrderByDescending(row => CheckNaming.Severity(row.Level)).First();

            Kind = worst.Kind;
            Label = label;
            Level = worst.Level;
            Swatch = worst.Swatch;
            IsHidden = rows.All(row => row.IsHidden);
            Etabs = CheckNaming.Same(rows.Select(row => row.Etabs));
            Revit = CheckNaming.Same(rows.Select(row => row.Revit));
            IsFailure = rows.Any(row => row.IsFailure);

            string split = CheckNaming.Split(rows.Select(row => row.Level));
            string distinctSummary = rows.Select(row => row.Summary).Distinct().Count() == 1
                ? rows[0].Summary
                : "";

            Summary = !HasSides && distinctSummary.Length > 0
                ? distinctSummary
                : split + (rows.Count < selected ? $" ({rows.Count} of {selected} selected)" : "");
            ShowsSummary = true;
        }

        public CheckKind Kind { get; }
        public string Label { get; }

        /// <summary>The one line the check itself wrote about what it found.</summary>
        public string Summary { get; }

        public ResultLevel Level { get; }
        public string Swatch { get; }

        // The two sides the check compared, printed in their own columns. A check with nothing
        // to put in them falls back to its summary line, which is why both are kept.
        public string Etabs { get; }
        public string Revit { get; }

        /// <summary>True where the card should print the two columns rather than the sentence.</summary>
        public bool HasSides => Etabs.Length > 0 || Revit.Length > 0;

        public bool ShowsSummary { get; }

        /// <summary>Drives the red on the value that is the reason this check failed.</summary>
        public bool IsFailure { get; }

        /// <summary>
        /// True while this check's switch is off. The row is still printed, greyed: the card is
        /// where you go to find out what is wrong with one element, and hiding a verdict there
        /// because a filter is set on the plan would be hiding it in the one place it was
        /// actually asked for.
        /// </summary>
        public bool IsHidden { get; }
    }

    /// <summary>
    /// The card that appears over the plan when an element is picked: what it is, what it was
    /// paired with, and every check that ran on it.
    ///
    /// It is built fresh on each selection rather than kept in step with one, because an
    /// element's verdicts do not change while it is selected - a new run replaces the element
    /// object outright.
    /// </summary>
    public sealed class ElementCard
    {
        public ElementCard(ElementViewModel element, ReviewNote said)
        {
            Element = element;
            IsHeightZone = element.Kind == ElementKind.HeightZone;

            // What someone said about it, and whether it is still about this. A review made
            // against a different set of verdicts is shown as stale rather than as clean: the
            // element has changed since it was looked at, and saying "reviewed" would mark a
            // finding nobody has seen as one that has been dealt with.
            CanReview = element.CanBeFiled;
            Note = said?.Note ?? "";
            CanAccept = CanReview && CheckNaming.Disagrees(element);

            if (said != null && said.Reviewed)
            {
                IsReviewStale = CheckNaming.IsStale(said, element);
                Verdict = CheckNaming.VerdictOn(said, element);
                StaleVerdict = said.Verdict;
                ReviewedWhen = said.When;
            }

            Title = string.IsNullOrEmpty(element.Identifier) ? "—" : element.Identifier;
            // Upper case here rather than in the card: it is the label that is shouted,
            // and every other use of KindOf wants it in sentence case.
            Kind = CheckNaming.KindOf(element).ToUpperInvariant();
            Story = element.Story ?? "";
            Model = CheckNaming.ModelOf(element);

            Rows = new ObservableCollection<CheckRow>(
                element.Outcomes
                       .Where(outcome => outcome.Kind != CheckKind.None)
                       .Select(outcome => new CheckRow(element, outcome,
                                       element.IsResultHidden(outcome))));

            Failed = Rows.Count(row => row.Level == ResultLevel.Failed);
            Review = Rows.Count(row => row.Level == ResultLevel.Review);
            Passed = Rows.Count(row => row.Level == ResultLevel.Passed);

            // The identifying fields - the section name, the type mark, the story - are the
            // ones no check owns. They say what the thing is, which is the first question
            // asked of a card and one the verdicts never answer.
            Facts = new ObservableCollection<ResultField>(
                element.ResultFields.Where(field => field.Kind == CheckKind.None).Take(12));

            Count = 1;
            NoteToEdit = Note;
        }

        /// <summary>
        /// The card for several selected elements at once: what they are, every check that ran
        /// on any of them with the values they agree on, and what has been said about them.
        ///
        /// A value every one of them has is printed as it is and one they differ on reads
        /// "Varies", so twelve beams of one section read as one section and a mixed selection
        /// says so without listing twelve lines. The buttons underneath act on all of them.
        /// </summary>
        public ElementCard(IList<ElementViewModel> elements, Func<ElementViewModel, ReviewNote> said)
        {
            Element = null;
            Count = elements.Count;
            IsHeightZone = elements.Count > 0 && elements.All(element => element.Kind == ElementKind.HeightZone);

            List<ReviewNote> notes = elements.Select(element => said(element)).ToList();
            List<ElementViewModel> filed = elements.Where(element => element.CanBeFiled).ToList();
            CanReview = filed.Count > 0;

            // A decision is shown as standing only where every one of them carries it, so the
            // button that takes it back never takes it back from elements that never had it.
            List<HumanVerdict> verdicts = filed
                .Select(element => CheckNaming.VerdictOn(said(element), element))
                .ToList();
            Verdict = verdicts.Count > 0 && verdicts.All(verdict => verdict == verdicts[0])
                ? verdicts[0]
                : HumanVerdict.None;

            // Taking an acceptance back is always allowed; making one needs something to accept.
            CanAccept = Verdict == HumanVerdict.Accepted || filed.Any(CheckNaming.Disagrees);

            int accepted = verdicts.Count(verdict => verdict == HumanVerdict.Accepted);
            int reviewed = verdicts.Count(verdict => verdict == HumanVerdict.Reviewed);
            int stale = elements.Where(element => element.CanBeFiled)
                                .Count(element => CheckNaming.IsStale(said(element), element));
            multiReviewState = MultiReviewState(accepted, reviewed, stale, elements.Count);
            IsReviewStale = stale > 0;

            List<string> distinctNotes = notes.Select(note => note?.Note ?? "").Distinct().ToList();
            NoteToEdit = distinctNotes.Count == 1 ? distinctNotes[0] : "";
            Note = distinctNotes.Count == 1
                ? distinctNotes[0]
                : "The selected elements have different notes. A note added here replaces all of them.";

            List<string> kinds = elements.Select(CheckNaming.KindOf).Distinct().ToList();
            Kind = (kinds.Count == 1
                       ? elements.Count + " " + CheckNaming.KindsOf(elements[0])
                       : elements.Count + " elements").ToUpperInvariant();

            List<string> stories = elements.Select(element => element.Story ?? "").Distinct().ToList();
            Story = stories.Count == 1 ? stories[0] : "";
            Model = CheckNaming.Same(elements.Select(CheckNaming.ModelOf));

            Title = string.Join(", ", elements.Select(element =>
                string.IsNullOrEmpty(element.Identifier) ? "—" : element.Identifier));

            List<string> subtitle = new List<string>
            {
                stories.Count == 1 ? stories[0] : stories.Count + " levels"
            };
            if (kinds.Count > 1)
            {
                subtitle.Add(string.Join(", ", elements
                    .GroupBy(CheckNaming.KindOf)
                    .Select(group => group.Count() + " " + group.Key.ToLowerInvariant()
                                     + (group.Count() == 1 ? "" : "s"))));
            }
            multiSubtitle = string.Join(" · ", subtitle.Where(part => part.Length > 0));

            // Every check that ran on any of them, in the order it first turns up.
            List<KeyValuePair<string, CheckRow>> all = new List<KeyValuePair<string, CheckRow>>();
            foreach (ElementViewModel element in elements)
            {
                foreach (CheckOutcome outcome in element.Outcomes.Where(o => o.Kind != CheckKind.None))
                {
                    all.Add(new KeyValuePair<string, CheckRow>(CheckNaming.LabelOf(outcome),
                            new CheckRow(element, outcome, element.IsResultHidden(outcome))));
                }
            }
            Rows = new ObservableCollection<CheckRow>(
                all.GroupBy(pair => pair.Key)
                   .Select(group => new CheckRow(group.Key, group.Select(pair => pair.Value).ToList(),
                                                 elements.Count)));

            // Counted per element rather than per check: "3 failing" is three elements to go
            // and look at, which is the question a selection is asking.
            Failed = elements.Count(element => element.Outcomes.Any(o => o.Kind != CheckKind.None
                                                                       && o.Level == ResultLevel.Failed));
            Review = elements.Count(element => element.Outcomes.Any(o => o.Kind != CheckKind.None
                                                                       && o.Level == ResultLevel.Review));
            Passed = elements.Count - Failed;

            Facts = new ObservableCollection<ResultField>(
                elements.SelectMany(element => element.ResultFields.Where(field => field.Kind == CheckKind.None))
                        .GroupBy(field => field.Label)
                        .Take(10)
                        .Select(group => MergedFact(group.Key, elements)));

            int paired = elements.Count(CheckNaming.IsPaired);
            multiPairingNote = paired == elements.Count
                ? "All " + elements.Count + " are paired."
                : paired == 0
                    ? "None of them is paired."
                    : paired + " of " + elements.Count + " are paired.";
        }

        /// <summary>
        /// One identifying field across several elements, "Varies" where they differ.
        ///
        /// Asked only of the elements that have the field at all. One of another kind - a line
        /// drawn in Revit beside a stretch of ETABS frame, a beam beside a column - has nothing
        /// to say about it, which is not the same as saying something different: counted, it
        /// made every fact on a mixed selection read "Varies" and told nobody anything. The
        /// label says how many have it where that is not all of them.
        /// </summary>
        private static ResultField MergedFact(string label, IList<ElementViewModel> elements)
        {
            List<ResultField> fields = elements
                .Select(element => element.ResultFields.FirstOrDefault(
                    field => field.Kind == CheckKind.None && field.Label == label))
                .Where(field => field != null)
                .ToList();

            ResultField first = fields[0];
            bool sided = fields.Any(field => field.HasSides);
            string shown = fields.Count < elements.Count
                ? label + " (" + fields.Count + " of " + elements.Count + ")"
                : label;

            return new ResultField(first.Key, shown,
                sided ? "" : CheckNaming.Same(fields.Select(field => field.Value)),
                etabs: sided ? CheckNaming.Same(fields.Select(field => field.Etabs)) : null,
                revit: sided ? CheckNaming.Same(fields.Select(field => field.Revit)) : null);
        }

        private static string MultiReviewState(int accepted, int reviewed, int stale, int of)
        {
            List<string> parts = new List<string>();
            if (accepted > 0) parts.Add(accepted + " accepted");
            if (reviewed > 0) parts.Add(reviewed + " reviewed");
            if (stale > 0) parts.Add(stale + " changed since they were looked at");
            return parts.Count == 0 ? "" : string.Join(", ", parts) + " - of " + of + " selected.";
        }

        private readonly string multiReviewState;
        private readonly string multiSubtitle;
        private readonly string multiPairingNote;

        /// <summary>The single element the card is about, or null on a card for several.</summary>
        public ElementViewModel Element { get; }

        /// <summary>
        /// True when every element on the card is a height zone drawn by hand, which is changed
        /// rather than judged: the card offers its height, a copy to other levels and a delete in
        /// place of the review, the acceptance and the pairing.
        /// </summary>
        public bool IsHeightZone { get; private set; }

        /// <summary>What the card does for a finding: review, accept, note, select in Revit.</summary>
        public bool IsFinding => !IsHeightZone;

        /// <summary>The pairing's CHANGE button, for one element that can be paired at all.</summary>
        public bool ShowsChangePairing => IsSingle && !IsHeightZone;

        /// <summary>What the section under the title is called: what it is paired with, or what it does.</summary>
        public string PairingHeading => IsHeightZone ? "WHAT IT DOES" : "PAIRED WITH";

        /// <summary>How many elements the card is about.</summary>
        public int Count { get; }

        public bool IsSingle => Count == 1;

        public bool IsMultiple => Count > 1;

        /// <summary>
        /// What the note editor opens with: the note, or on several elements the note they
        /// share - and nothing where they differ, since whatever is typed replaces them all.
        /// </summary>
        public string NoteToEdit { get; }

        public string Title { get; }
        public string Kind { get; }
        public string Story { get; }

        /// <summary>"Both", or the one model that has it.</summary>
        public string Model { get; }

        public ObservableCollection<CheckRow> Rows { get; }
        public ObservableCollection<ResultField> Facts { get; }

        public int Failed { get; }
        public int Review { get; }
        public int Passed { get; }

        /// <summary>
        /// False only where the element has no name to be filed under at all - no ETABS name,
        /// no Revit id, and, on a piece of slab, nothing it can be said to have been cut from.
        /// </summary>
        public bool CanReview { get; }

        /// <summary>
        /// True where there is a difference to accept - see <see cref="CheckNaming.Disagrees"/>.
        /// An element that agrees has nothing to settle, so accepting it is not offered.
        /// </summary>
        public bool CanAccept { get; }

        /// <summary>
        /// What was decided, where the decision is still about what this element says now. Reads
        /// as None once it has gone stale, because a decision about something else is not a
        /// decision about this.
        /// </summary>
        public HumanVerdict Verdict { get; }

        /// <summary>What the stale decision was, for saying what has gone out of date.</summary>
        public HumanVerdict StaleVerdict { get; }

        public bool IsReviewed => Verdict == HumanVerdict.Reviewed;

        /// <summary>Settled: still a real difference, but agreed and no longer work.</summary>
        public bool IsAccepted => Verdict == HumanVerdict.Accepted;

        /// <summary>
        /// Reviewed once, but not against these verdicts. Something about the element has
        /// changed since someone looked at it, so the review is shown but not trusted.
        /// </summary>
        public bool IsReviewStale { get; }

        public DateTime ReviewedWhen { get; }

        public string Note { get; }

        public bool HasNote => Note.Length > 0;

        /// <summary>What the card says beside the review button.</summary>
        public string ReviewState
        {
            get
            {
                if (IsMultiple)
                {
                    return multiReviewState ?? "";
                }
                if (IsReviewStale)
                {
                    return (StaleVerdict == HumanVerdict.Accepted ? "Accepted " : "Reviewed ")
                           + ReviewedWhen.ToString("d MMM")
                           + ", but it has changed since - worth another look.";
                }
                if (IsAccepted)
                {
                    return "Accepted " + ReviewedWhen.ToString("d MMM")
                           + ". Still a difference, but agreed.";
                }
                if (IsReviewed)
                {
                    return "Reviewed " + ReviewedWhen.ToString("d MMM") + ".";
                }
                return "";
            }
        }

        public bool HasReviewState => ReviewState.Length > 0;

        /// <summary>The button reads as taking the review back once one is standing.</summary>
        public string ReviewButtonText => IsReviewed
            ? (IsMultiple ? "ALL REVIEWED ✓" : "REVIEWED ✓")
            : (IsMultiple ? "MARK ALL REVIEWED" : "MARK REVIEWED");

        public string AcceptButtonText => IsAccepted
            ? (IsMultiple ? "ALL ACCEPTED ✓" : "ACCEPTED ✓")
            : (IsMultiple ? "ACCEPT ALL" : "ACCEPT");

        public string NoteButtonText => IsMultiple ? "ADD A NOTE TO ALL" : "ADD A NOTE";

        public string SelectInRevitText => IsMultiple ? "SELECT ALL IN REVIT" : "SELECT IN REVIT";

        public bool HasFailed => Failed > 0;
        public bool HasReview => Review > 0;
        public bool HasPassed => Passed > 0;

        /// <summary>
        /// The line under the title: where it is and what it is called in each model. The grid
        /// reference the design shows would go first, but grids are not imported, so it starts
        /// at the level.
        /// </summary>
        public string Subtitle
        {
            get
            {
                if (IsMultiple)
                {
                    return multiSubtitle ?? "";
                }

                List<string> parts = new List<string>();
                if (Story.Length > 0)
                {
                    parts.Add(Story);
                }
                if (Element.RevitId > 0)
                {
                    parts.Add("Revit " + Element.RevitId);
                }
                return string.Join(" · ", parts);
            }
        }

        /// <summary>"of 8 checks", so a single failure is read against how much was asked.</summary>
        public string OfChecksText => IsMultiple
            ? "of " + Count + " selected"
            : "of " + Rows.Count + (Rows.Count == 1 ? " check" : " checks");

        public string FailedText => IsMultiple
            ? Failed + " failing"
            : Failed + (Failed == 1 ? " fail" : " fails");
        public string ReviewText => Review + " to look at";
        public string PassedText => Passed + " agree";

        /// <summary>
        /// Whether this element has a counterpart at all, which is the thing to say before any
        /// verdict: a check that never found a pair has nothing to disagree about.
        /// </summary>
        public bool IsPaired => Element != null
                                && (Element.Source == ElementSource.Paired || Element.PairedWith.Length > 0);

        public string PairingNote
        {
            get
            {
                if (IsMultiple)
                {
                    return multiPairingNote ?? "";
                }

                // An element that knows its own answer is asked first. A line load is read
                // against a run of frames chained together for this check, which is not an
                // element this card could name from the source and the Revit id.
                if (Element.PairedWith.Length > 0)
                {
                    return Element.PairedWith;
                }

                if (Element.Source == ElementSource.Paired)
                {
                    return Element.RevitId > 0
                        ? "Paired with Revit " + Element.RevitId
                        : "Paired.";
                }

                return Model == "ETABS"
                    ? "Nothing in Revit was close enough to pair with."
                    : "Nothing in ETABS was close enough to pair with.";
            }
        }
    }

    /// <summary>
    /// One row of the findings list: a single check's complaint about a single element.
    ///
    /// An element with three things wrong with it is three rows, not one - the list is a
    /// worklist, and the unit of work is the thing to go and fix, not the thing it happened to.
    /// </summary>
    public sealed class FindingRow : ViewModelBase
    {
        private bool isPicked;

        public FindingRow(ElementViewModel element, CheckOutcome outcome, ReviewNote said,
                          RunHistory history)
        {
            Element = element;
            Kind = outcome.Kind;
            Level = outcome.Level;

            ElementLabel = CheckNaming.KindOf(element) + " "
                           + (string.IsNullOrEmpty(element.Identifier) ? "—" : element.Identifier);
            Where = element.Story ?? "";
            CheckLabel = CheckNaming.LabelOf(outcome);
            Summary = outcome.Summary ?? "";
            Model = CheckNaming.ModelOf(element);
            Swatch = CheckNaming.SwatchOf(outcome.Kind, outcome.Level, element);
            IsFail = outcome.Level == ResultLevel.Failed;

            // Whether the last run had this exact finding on this exact element. New ones are
            // what a second run is actually read for.
            //
            // A slab zone is left out, though it can be reviewed like anything else. The name a
            // zone is filed under is meant to be fragile - move the cut and the agreement is
            // dropped rather than handed to whatever landed there instead - and that is the
            // wrong bargain for a history, where a tolerance nudged by a millimetre re-cuts a
            // whole floor and every piece of it would be reported as new.
            HistoryKey = element.Kind == ElementKind.Slab
                ? ""
                : RunHistory.KeyOf(element.ReviewKey, outcome.Kind, outcome.Level, outcome.Token);
            IsNew = HistoryKey.Length > 0 && history != null && history.IsNew(HistoryKey);

            Note = said?.Note ?? "";
            if (said != null && said.Reviewed)
            {
                IsReviewStale = CheckNaming.IsStale(said, element);
                Verdict = CheckNaming.VerdictOn(said, element);
            }
        }

        /// <summary>How this finding is named in the run history.</summary>
        public string HistoryKey { get; }

        /// <summary>Not in the last run. Empty on the first run, which has nothing to differ from.</summary>
        public bool IsNew { get; }

        public string NewMark => IsNew ? "NEW" : "";

        /// <summary>
        /// Ticked for the bulk actions along the foot of the drawer. On the row rather than in
        /// the grid's own selection because a DataGrid will not hand its selected items to a
        /// view model, and because the design ticks rows rather than highlighting them.
        /// </summary>
        public bool IsPicked
        {
            get => isPicked;
            set
            {
                if (SetProperty(ref isPicked, value))
                {
                    PickedChanged?.Invoke();
                }
            }
        }

        internal Action PickedChanged { get; set; }

        public HumanVerdict Verdict { get; }
        public bool IsReviewed => Verdict == HumanVerdict.Reviewed;
        public bool IsAccepted => Verdict == HumanVerdict.Accepted;
        public bool IsReviewStale { get; }
        public string Note { get; }

        /// <summary>
        /// Whether this still counts as work. An accepted difference does not: it is agreed, and
        /// leaving it in the open count would mean the number never came down on a model where
        /// the remaining differences are all ones somebody decided were fine.
        /// </summary>
        public bool IsOpen => !IsAccepted;

        /// <summary>What the Seen column prints: a tick, an A for accepted, a query, or nothing.</summary>
        public string ReviewMark
        {
            get
            {
                if (IsAccepted)
                {
                    return "A";
                }
                return IsReviewed ? "✓" : (IsReviewStale ? "?" : "");
            }
        }

        public string ReviewTip
        {
            get
            {
                if (IsAccepted)
                {
                    return "Accepted: still a difference between the two models, but agreed.";
                }
                if (IsReviewed)
                {
                    return "Reviewed.";
                }
                return IsReviewStale
                    ? "Looked at once, but this has changed since - worth another look."
                    : "";
            }
        }

        /// <summary>Whether this reads as a failure or as something to look at.</summary>
        public bool IsFail { get; }

        public ElementViewModel Element { get; }
        public CheckKind Kind { get; }
        public ResultLevel Level { get; }

        public string ElementLabel { get; }
        public string Where { get; }
        public string CheckLabel { get; }

        /// <summary>What disagrees, in the check's own words.</summary>
        public string Summary { get; }

        public string Model { get; }
        public string Swatch { get; }
    }

    /// <summary>
    /// The findings drawer: every open complaint on the level being looked at, as a list that
    /// can be worked down.
    ///
    /// The plan answers "what is wrong here?"; this answers "what is left?", which is the
    /// question you cannot answer by looking at a drawing. It is filled from the same elements
    /// the plan draws, so a switch turned off up top takes its rows out of here too.
    /// </summary>
    public sealed class FindingsList : ViewModelBase
    {
        private bool isOpen;
        private FindingRow selectedRow;

        public ObservableCollection<FindingRow> Rows { get; } = new ObservableCollection<FindingRow>();

        /// <summary>Set when a row is clicked, so the plan can select the same element.</summary>
        public Action<ElementViewModel> OnRowPicked { get; set; }

        public bool IsOpen
        {
            get => isOpen;
            set => SetProperty(ref isOpen, value);
        }

        public FindingRow SelectedRow
        {
            get => selectedRow;
            set
            {
                if (SetProperty(ref selectedRow, value) && value != null)
                {
                    OnRowPicked?.Invoke(value.Element);
                }
            }
        }

        public int Failed { get; private set; }
        public int Review { get; private set; }
        public int Open => Failed + Review;

        public bool HasAny => Rows.Count > 0;

        /// <summary>What the closed drawer says along its edge.</summary>
        public string Summary
        {
            get
            {
                if (Rows.Count == 0)
                {
                    return "Nothing open on this level.";
                }

                List<string> parts = new List<string>();
                if (Failed > 0)
                {
                    parts.Add(Failed + (Failed == 1 ? " fail" : " fails"));
                }
                if (Review > 0)
                {
                    parts.Add(Review + " to look at");
                }
                return string.Join("   ·   ", parts);
            }
        }

        public string OpenText => Open + " open";

        /// <summary>
        /// Rebuilds the list from the elements on the plan. Only what is still switched on is
        /// listed: the drawer and the drawing are two views of one set of results, and a
        /// finding you have filtered off the plan is not one you are working on.
        /// </summary>
        /// <summary>How many rows are ticked, for the bulk actions.</summary>
        public int PickedCount => Rows.Count(row => row.IsPicked);

        public bool HasPicked => PickedCount > 0;

        public string PickedText => PickedCount + (PickedCount == 1 ? " row picked" : " rows picked");

        /// <summary>The elements behind the ticked rows, each once however many rows it has.</summary>
        public IList<ElementViewModel> PickedElements
        {
            get
            {
                List<ElementViewModel> found = new List<ElementViewModel>();
                foreach (FindingRow row in Rows.Where(r => r.IsPicked))
                {
                    if (!found.Contains(row.Element))
                    {
                        found.Add(row.Element);
                    }
                }
                return found;
            }
        }

        private void OnPickedChanged()
        {
            OnPropertyChanged(nameof(PickedCount));
            OnPropertyChanged(nameof(HasPicked));
            OnPropertyChanged(nameof(PickedText));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>How many of these were not in the last run.</summary>
        public int NewCount { get; private set; }

        /// <summary>How many of the last run's findings are gone from this one.</summary>
        public int FixedCount { get; private set; }

        public bool HasChanges => NewCount > 0 || FixedCount > 0;

        /// <summary>"38 new since the last run  ·  12 fixed", or nothing on a first run.</summary>
        public string ChangeSummary
        {
            get
            {
                if (!HasChanges)
                {
                    return "";
                }

                List<string> parts = new List<string>();
                if (NewCount > 0)
                {
                    parts.Add(NewCount + " new since the last run");
                }
                if (FixedCount > 0)
                {
                    parts.Add(FixedCount + " fixed");
                }
                return string.Join("   ·   ", parts);
            }
        }

        public void Update(IEnumerable<ElementViewModel> onScreen,
                           Func<ElementViewModel, ReviewNote> saidAbout,
                           RunHistory history)
        {
            Rows.Clear();
            Failed = 0;
            Review = 0;

            foreach (ElementViewModel element in onScreen)
            {
                if (element == null || element.IsDrawnAsContext)
                {
                    continue;
                }

                // A difference on too small a patch of floor to chase is not a job, and this
                // list is the job list. It is the one place such a piece is left out entirely
                // rather than shown and not counted: the plan still colours it, and the card
                // still says what differs, so nothing has been hidden - it has been kept out of
                // the worklist, which is the whole of what the sizes are for.
                if (element.IsTooSmallToCount)
                {
                    continue;
                }

                // Nor is a piece accepted for lying inside a column or a wall: nobody has an
                // answer to give on it, and the card still says what it found and where.
                if (element.IsAutoAccepted)
                {
                    continue;
                }

                foreach (CheckOutcome outcome in element.Outcomes)
                {
                    if (outcome.Kind == CheckKind.None)
                    {
                        continue;
                    }
                    if (outcome.Level != ResultLevel.Failed && outcome.Level != ResultLevel.Review)
                    {
                        continue;
                    }
                    if (element.IsResultHidden(outcome))
                    {
                        continue;
                    }

                    FindingRow row = new FindingRow(element, outcome,
                        saidAbout == null ? null : saidAbout(element), history);
                    row.PickedChanged = OnPickedChanged;
                    Rows.Add(row);

                    // An agreed difference is still a difference, so the row stays - it is how
                    // the agreement is found again and taken back. It stops being counted,
                    // though: the number along the drawer is what is left to do.
                    if (!row.IsOpen)
                    {
                        continue;
                    }

                    // Counted by how it is drawn rather than by its raw level, so the drawer's
                    // tally and the colours on the plan cannot tell two different stories about
                    // an unpaired element.
                    if (row.IsFail)
                    {
                        Failed++;
                    }
                    else
                    {
                        Review++;
                    }
                }
            }

            // Worst first, then by element, so the list opens on the work rather than on
            // whatever the mesh happened to produce first.
            List<FindingRow> ordered = Rows
                .OrderBy(row => row.Level == ResultLevel.Failed ? 0 : 1)
                .ThenBy(row => row.ElementLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.CheckLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Rows.Clear();
            foreach (FindingRow row in ordered)
            {
                Rows.Add(row);
            }

            // The row that was selected is gone with the rebuild; putting the selection back
            // by value would re-fire the click and move the plan, so it is simply dropped.
            selectedRow = null;
            OnPropertyChanged(nameof(SelectedRow));

            OnPropertyChanged(nameof(Failed));
            OnPropertyChanged(nameof(Review));
            OnPropertyChanged(nameof(Open));
            OnPropertyChanged(nameof(OpenText));
            OnPropertyChanged(nameof(HasAny));
            OnPropertyChanged(nameof(Summary));

            NewCount = Rows.Count(row => row.IsNew);
            FixedCount = history == null
                ? 0
                : history.FixedCount(new HashSet<string>(
                    Rows.Select(row => row.HistoryKey), StringComparer.Ordinal));

            OnPropertyChanged(nameof(NewCount));
            OnPropertyChanged(nameof(FixedCount));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(ChangeSummary));
            OnPickedChanged();
        }
    }
}
