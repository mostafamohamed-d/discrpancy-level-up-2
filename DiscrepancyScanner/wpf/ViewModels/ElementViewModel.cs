using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>Which model an element came from and whether it has been checked.</summary>
    public enum ElementSource
    {
        EtabsOnly,
        RevitOnly,
        Paired,
        UnmatchedEtabs,
        UnmatchedRevit
    }

    public enum ElementKind
    {
        Beam,
        Column,
        Wall,
        /// <summary>A piece of slab: a whole ETABS area, or a part of one that reads differently.</summary>
        Slab,

        /// <summary>
        /// A line load: a detail line off the loading plan, or a stretch of ETABS frame
        /// carrying a load nobody drew.
        /// </summary>
        LineLoad,

        /// <summary>
        /// A patch of a level drawn by hand where the line loads take a height of their own -
        /// see <see cref="DiscrepancyScanner.HeightZone"/>. Not a model element and not a
        /// finding: it carries no results, and is drawn so it can be seen, picked and changed.
        /// </summary>
        HeightZone
    }

    /// <summary>
    /// Which check the viewer is showing. One element type at a time, because each answers a
    /// different question and colours the plan for a different reason: a red beam, a red wall
    /// and a red patch of slab on the same drawing would be unreadable. Everything the mode is
    /// not checking is still drawn, in grey, so the floor always reads as a floor.
    /// </summary>
    public enum ViewMode
    {
        /// <summary>The column checks, with everything else behind them in grey.</summary>
        Columns,
        /// <summary>The beam check, with everything else behind it in grey.</summary>
        Beams,
        /// <summary>The wall checks, with everything else behind them in grey.</summary>
        Walls,
        /// <summary>
        /// The slab checks that are about the slab itself: thickness, the outlines and the
        /// openings.
        /// </summary>
        Slabs,
        /// <summary>
        /// The area loads on the ETABS shells against the loading plan. Kept apart from the
        /// other slab checks because it is a different question asked of the same shapes - one
        /// is whether the slab is the right slab, the other whether it carries the right load -
        /// and because it is the one check that can be switched off entirely.
        /// </summary>
        SlabLoads,

        /// <summary>
        /// The line loads drawn on the loading plan against the distributed loads on the ETABS
        /// frames under them.
        ///
        /// The one tab that draws the Revit model rather than the ETABS one. Every other check
        /// colours the ETABS plan because that is the model being checked; this one is read
        /// against the loading drawing, and what the person looking at it needs to see is which
        /// of the lines they drew the model disagrees with. The frames are put on it only where
        /// there is something about one that the drawn lines cannot show - a stretch carrying a
        /// load nobody drew leaves nothing on the drawing to colour.
        /// </summary>
        LineLoads,

        /// <summary>
        /// The height zones drawn on the level, and the tools to draw more. Not a check: it
        /// carries no count, and what it changes shows up on the line load tab.
        /// </summary>
        HeightZones,

        /// <summary>
        /// The SIDL_C line loads on the ETABS frames against the weight of the Revit walls and
        /// fill strips the wall and slab checks leave out. Read like the line loads tab - the
        /// Revit side drawn, a frame shown only where it carries a load nothing accounts for -
        /// and never up at the same time as it, so a wall's weight and a drawn line load are
        /// not read off one plan as if they were the same question.
        /// </summary>
        SidlC,

        /// <summary>
        /// The SIDL_C area loads on the ETABS slabs against the weight of the Revit light weight
        /// fill floors over them. Draws the fills, and an ETABS area only where it carries SIDL_C
        /// no fill accounts for. Never up with either slab tab: all three are regions of floor,
        /// and laid over each other none of them can be read.
        /// </summary>
        SidlCArea
    }

    /// <summary>Drives the colour an element is drawn in.</summary>
    public enum CheckState
    {
        /// <summary>Imported but not compared yet.</summary>
        NotChecked,
        /// <summary>Every check passed. Drawn green.</summary>
        Passed,
        /// <summary>At least one check failed. Drawn red.</summary>
        Failed,
        /// <summary>
        /// Every check passed, but something still wants a human look - a column whose type
        /// mark matches one ETABS group while the column sits in others too. Drawn orange.
        /// </summary>
        Review,
        /// <summary>No counterpart in the other model.</summary>
        Unpaired
    }

    /// <summary>
    /// One drawable, selectable, annotatable thing in the viewer. Every comparison result an
    /// element carries is exposed through <see cref="ResultFields"/>, which is what lets the
    /// canvas draw any of them as a label on top of the beam or column.
    /// </summary>
    public abstract class ElementViewModel : ViewModelBase
    {
        private bool isSelected;
        private bool isMuted;
        private Geometry cachedGeometry;

        protected ElementViewModel(ElementKind kind, ElementSource source, CheckState state,
                                   string identifier, string story, IList<ResultField> resultFields)
        {
            Kind = kind;
            Source = source;
            State = state;
            Identifier = identifier ?? "";
            Story = story ?? "";
            ResultFields = resultFields ?? new List<ResultField>();
        }

        public ElementKind Kind { get; }
        public ElementSource Source { get; }
        public CheckState State { get; }
        public string Identifier { get; }
        public string Story { get; }

        /// <summary>Every named result this element carries, keyed for the label picker.</summary>
        public IList<ResultField> ResultFields { get; }

        /// <summary>
        /// What each check concluded about this element, one entry per check it went through.
        /// The filters work on these one at a time, so switching one check's result off never
        /// touches the others.
        /// </summary>
        public IList<CheckOutcome> Outcomes { get; set; } = new List<CheckOutcome>();

        /// <summary>
        /// The result actually being drawn: the most serious of the outcomes whose check is
        /// still switched on. <see cref="CheckState.NotChecked"/> together with
        /// <see cref="IsMuted"/> means every result on this element is switched off, and it is
        /// drawn grey - the element stays on the plan either way.
        /// </summary>
        public CheckState DisplayState { get; set; } = CheckState.NotChecked;

        /// <summary>
        /// Somebody has looked at this element and agreed that the two models differ here - a
        /// screed the analysis leaves out, a mark the design team has confirmed.
        ///
        /// A statement about the element, not a result of a check, which is why it sits beside
        /// <see cref="DisplayState"/> rather than being one of its values: the element still
        /// failed, and the card still says what failed. What changes is that it stops being
        /// drawn as a failure and stops counting as work left to do.
        /// </summary>
        public bool IsAccepted { get; set; }

        /// <summary>
        /// A real difference on too small a patch of floor to be work - see
        /// <see cref="SlabZone.IsTooSmallToCount"/>, which is where the sizes are applied and
        /// only pieces of slab ever carry one.
        ///
        /// Beside <see cref="IsAccepted"/> because it takes the same thing away: the element
        /// stops being work. It is still drawn, still opens a card saying what differs, and can
        /// still be reviewed, accepted and noted - what it leaves is the chip counts, the level
        /// rail and the findings list, which are all one question, "what is left to do?", and
        /// this is not left to do.
        ///
        /// The colour is where the two part company. An agreed difference is settled and stops
        /// reading as a failure, so it is recoloured; this one is settled by nobody and stays in
        /// the colour of what was found, because the whole point of a size that quietens the
        /// counts is that the plan still shows what it quietened.
        /// </summary>
        public bool IsTooSmallToCount { get; set; }

        /// <summary>
        /// Accepted by the check rather than by a person: a piece of slab whose finding lies
        /// inside a column or a wall - see <see cref="SlabZone.IsAcceptedAsInsideSupport"/>.
        /// Carries <see cref="IsAccepted"/> with it, so it is drawn as settled and left out of
        /// the counts the same as an agreed difference, and kept out of the worklist the same as
        /// a patch too small to count, since nobody has an answer to give on it.
        /// </summary>
        public bool IsAutoAccepted { get; set; }

        /// <summary>
        /// The elements this one stands for on the plan, where it stands for more than itself.
        ///
        /// Only wall stacks use it. Several ETABS panels on one line at one story are drawn as
        /// the single wall they are - separately they sit exactly on top of each other, the last
        /// drawn hides the colours beneath it, and only the top one could ever be clicked, so a
        /// failed panel under a passed one was both invisible and unreachable.
        ///
        /// Folding fixed the drawing. This is how the panels stay reachable anyway: the plan
        /// still draws and selects the one wall, and ctrl+click looks through it to the panels
        /// underneath. Empty on everything else, which stands only for itself.
        /// </summary>
        public IList<ElementViewModel> Parts { get; set; } = new List<ElementViewModel>();

        public bool HasParts => Parts.Count > 1;

        /// <summary>
        /// The drawn element this one was folded into, for a panel that is never drawn itself.
        /// The plan highlights this instead, so picking a panel out of the chooser still lights
        /// up the wall it belongs to rather than nothing at all.
        /// </summary>
        public ElementViewModel PartOf { get; set; }

        /// <summary>Whichever of the two the canvas actually draws.</summary>
        public ElementViewModel Drawn => PartOf ?? this;

        /// <summary>
        /// The other model's outline for this element, drawn under it rather than instead of
        /// it: the ETABS section beneath a Revit column the two models put in different places,
        /// at different sizes or at different angles.
        ///
        /// Null on nearly everything. A paired element is normally drawn once, in the colour of
        /// its verdict, and one outline is enough - it is only where a check has failed on the
        /// geometry itself that a second outline says something the first cannot, which is how
        /// far out and in which direction. See <see cref="PrefrencesData.DrawBothColumnsOnMismatch"/>.
        ///
        /// Held here rather than added to the plan alongside its owner, because the two belong
        /// together: the outline goes on the plan when the element does and comes off with it,
        /// and a filter that greys the failure this was drawn for takes the outline with it.
        /// </summary>
        /// <summary>
        /// What this element is paired with, in words, for the checks whose pairing the card
        /// cannot work out from the element alone.
        ///
        /// Empty on everything that pairs one element with one element, where the source and
        /// the Revit id say it between them. A line load does not: it is read against a run of
        /// ETABS frames chained together for this check alone, and a run is not an element the
        /// card could name. Left to the general answer, a line load that had found its frames
        /// still read "nothing in ETABS was close enough to pair with", which the same card
        /// then contradicted two rows further down by naming them.
        /// </summary>
        public string PairedWith { get; set; } = "";

        public ElementViewModel Counterpart { get; set; }

        /// <summary>
        /// True on the outline itself, for the palette. It is <see cref="IsContext"/> as well -
        /// it carries no verdict, cannot be picked and is never labelled - but it is a section
        /// somebody is being asked to look at rather than a backdrop, and the grey a backdrop is
        /// drawn in would lose it under the column it belongs to.
        /// </summary>
        public bool IsCounterpart { get; set; }

        /// <summary>
        /// Checks with nothing left on show, so their values are not stamped either. A check
        /// that reports on several places at once - the reinforcement stations along a beam,
        /// the four loads on a piece of slab - only lands in here once every one of its results
        /// is switched off: a beam whose failed station is still being shown should still carry
        /// that station's numbers on the plan.
        /// </summary>
        public ISet<CheckKind> HiddenChecks { get; set; } = new HashSet<CheckKind>();

        /// <summary>
        /// The single results being held back, named as <see cref="ResultKey"/> names them.
        ///
        /// Kept beside <see cref="HiddenChecks"/> rather than folded into it because the
        /// switches are per verdict as well as per check: with a check's passes switched off
        /// and its failures on, the check itself is neither hidden nor shown, and only the
        /// individual results can say which of them is which.
        /// </summary>
        public ISet<string> HiddenResults { get; set; } =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// How one result is named in the hidden set: by the switch that decides it, which is
        /// the check and the verdict. Two results of the same check reaching the same verdict
        /// are shown and hidden together, so they share a name here.
        /// </summary>
        public static string ResultKey(CheckOutcome outcome)
        {
            return (int)outcome.Kind + "|" + (int)outcome.Level;
        }

        /// <summary>True while the switches are holding this one result back.</summary>
        public bool IsResultHidden(CheckOutcome outcome)
        {
            return outcome != null && HiddenResults.Contains(ResultKey(outcome));
        }

        /// <summary>True when this element's value for <paramref name="field"/> is being shown.</summary>
        public bool IsFieldShown(ResultField field)
        {
            return field != null && !HiddenChecks.Contains(field.Kind);
        }

        /// <summary>The Revit element id to select back in Revit, or zero for ETABS-only elements.</summary>
        public long RevitId { get; set; }

        /// <summary>
        /// The ETABS unique name this element was built from, which is what a hand-set pairing
        /// is filed under. Empty on an element that came from Revit alone - there is no ETABS
        /// side to pin, and the pairing is written from the ETABS element's point of view.
        ///
        /// Kept apart from <see cref="Identifier"/> because that is whatever reads best on the
        /// plan - the Revit id on a paired column - while this has to be the one name that
        /// survives a re-run.
        /// </summary>
        public string EtabsUniqueName { get; set; } = "";

        /// <summary>
        /// How a piece of slab is filed, worked out by the zone itself - see
        /// <see cref="SlabZone.ReviewKey"/>, which is where the facts to name one with are.
        /// Also set on a stretch of a line load checked in steps, named by the line and where
        /// along it the stretch lies, and on a line load whose loading plan several stories
        /// share, named by the line and its story. Empty on everything else, and on an imported
        /// outline that no run has cut yet.
        /// </summary>
        public string ZoneKey { get; set; } = "";

        /// <summary>
        /// How this element is filed when someone reviews it or writes a note on it.
        ///
        /// The ETABS name where there is one, and the Revit id otherwise - so a Revit element
        /// with no counterpart can still be reviewed, which a pairing key could not manage
        /// because a pairing is always written from the ETABS side.
        ///
        /// A slab zone has no name of either kind: it is cut afresh every run, out of a shell
        /// that can come through in any number of pieces. It is named by where the piece is and
        /// what it was cut from instead, which the zone works out and hands over as
        /// <see cref="ZoneKey"/>. That name is deliberately fragile - a cut that really moves
        /// leaves the review behind - and the fingerprint does the rest, so what is inherited
        /// across runs is only ever an agreement about the same piece saying the same thing.
        /// </summary>
        public string ReviewKey
        {
            get
            {
                // A stretch of a line checked in steps is named by its place too, so a review of
                // one stretch is not a review of the whole line.
                if (Kind == ElementKind.Slab || ZoneKey.Length > 0)
                {
                    return ZoneKey;
                }
                if (EtabsUniqueName.Length > 0)
                {
                    return Kind + "|" + EtabsUniqueName;
                }
                return RevitId > 0 ? Kind + "|R" + RevitId : "";
            }
        }

        /// <summary>
        /// Whether there is a name to file a review, an agreement or a note under.
        ///
        /// The one test all of them go through - the buttons on the card, the bulk actions
        /// along the foot of the drawer, and reading a verdict back onto the plan - so that a
        /// finding cannot end up half filed: offered a button that then quietly does nothing,
        /// or agreed to in a batch and open again on the next run.
        /// </summary>
        public bool CanBeFiled => !IsRaw && ReviewKey.Length > 0;

        /// <summary>
        /// True on an element drawn from the raw data - one model's element as it was read,
        /// with nothing compared. See <see cref="ComparisonViewModel.ShowRawData"/>.
        ///
        /// It carries no results, so it counts on no tab and is in no findings list, and it is
        /// drawn in its own model's colour rather than a verdict's. Nothing can be filed under
        /// it and it cannot be paired: a review or an acceptance is about what a check found,
        /// and there is no finding here for either to be about.
        /// </summary>
        public bool IsRaw { get; set; }

        /// <summary>
        /// What a review or an acceptance is recorded against, for telling one that still holds
        /// from one that was made about something else: every verdict the element carries, and
        /// - where the element publishes one - where it stands and how big it is in each model.
        ///
        /// The second half is there because the verdicts alone can say the same thing about a
        /// different element. A beam moved a bay over, or made deeper in both models, can pass
        /// or fail exactly as it did, and an acceptance carried across on the strength of that
        /// would settle a beam nobody has looked at.
        /// </summary>
        public string VerdictFingerprint
        {
            get
            {
                return PlaceFingerprint.Length == 0
                    ? ResultFingerprint
                    : ResultFingerprint + PlaceMarker + PlaceFingerprint;
            }
        }

        /// <summary>Where <see cref="PlaceFingerprint"/> starts in a <see cref="VerdictFingerprint"/>.</summary>
        public const string PlaceMarker = "|at:";

        /// <summary>
        /// Every verdict this element carries, as one string. Ordered so the same set of
        /// results always reads the same way, whatever order the checks happened to run in.
        /// This on its own was the whole fingerprint before places were part of it.
        /// </summary>
        public string ResultFingerprint
        {
            get
            {
                return string.Join(",", Outcomes
                    .Where(outcome => outcome.Kind != CheckKind.None)
                    .Select(outcome => (int)outcome.Kind + ":" + (int)outcome.Level
                                       + (outcome.Token.Length > 0 ? ":" + outcome.Token : ""))
                    .OrderBy(text => text, StringComparer.Ordinal));
            }
        }

        /// <summary>
        /// Where the element stands and how big it is, in each model's own terms, for
        /// <see cref="VerdictFingerprint"/>. Empty on a kind that does not publish one.
        /// </summary>
        public string PlaceFingerprint { get; set; } = "";

        /// <summary>
        /// True for an element that is only here to say where things are and never carries a
        /// result of its own: the ETABS slab outlines behind the frame check, the other model's
        /// outline under a column. Context is drawn grey and unfilled and cannot be selected,
        /// so it never gets in the way of the check actually being looked at.
        /// </summary>
        public bool IsContext { get; set; }

        /// <summary>
        /// True while this element's own tab is switched off, so it is drawn behind the check
        /// on show exactly as context is - the beams and columns behind the slab check.
        ///
        /// Kept apart from <see cref="IsContext"/> because it is a statement about the plan
        /// rather than about the element. The element still carries its results and still
        /// counts on its tab, which stays up with its number while switched off; it used to be
        /// marked as context outright, and the tally, which leaves context out, then lost every
        /// finding on every tab not on show the next time it counted.
        /// </summary>
        public bool IsBackdrop { get; set; }

        /// <summary>Drawn, picked and labelled as context: context by nature, or for now.</summary>
        public bool IsDrawnAsContext => IsContext || IsBackdrop;

        /// <summary>
        /// The tab this element answers to, where its kind alone does not say. Null for
        /// everything but a piece of slab after a run.
        ///
        /// The floor is cut twice after a run - once for the slab check and once, over the
        /// loading plan, for the load check - and both cuts are built together so that moving
        /// between the two tabs only changes which is shown. Each piece then has to say which
        /// cut it came from: the load cut carries the thickness verdict too, and counted on the
        /// slab tab it would count every thickness finding twice.
        /// </summary>
        public ViewMode? Tab { get; set; }

        public bool IsSelected
        {
            get => isSelected;
            set => SetProperty(ref isSelected, value);
        }

        /// <summary>
        /// True when the filters have switched this element's result off. The element still
        /// draws, so the plan stays whole, but in grey and with no labels: the column is
        /// there, its verdict is simply not being shown.
        /// </summary>
        public bool IsMuted
        {
            get => isMuted;
            set => SetProperty(ref isMuted, value);
        }

        /// <summary>Point, in model mm, where labels for this element are anchored.</summary>
        public abstract Point LabelAnchor { get; }

        /// <summary>
        /// Which way the element runs at <see cref="LabelAnchor"/>, in model mm, for an element
        /// that is a line - a beam, a line load, a wall. Its label is turned to run along it and
        /// set beside it rather than on top of it. Null on everything else, whose label is
        /// written level across the plan.
        /// </summary>
        public virtual Vector? LabelDirection => null;

        /// <summary>
        /// How far from the line, in model mm, the element is drawn on either side, so a label
        /// set beside it clears what is drawn rather than only the line down its middle: half
        /// the thickness of a wall, nothing for a beam drawn as its centre line.
        /// </summary>
        public virtual double LabelClearance => 0;

        /// <summary>
        /// Text written on the element whatever is ticked under LABEL, or null for none. Only a
        /// height zone has one - its height, which is the one thing it is there to say.
        /// </summary>
        public virtual string Caption => null;

        /// <summary>Shortest distance, in model mm, from a point to this element.</summary>
        public abstract double DistanceTo(Point modelPoint);

        /// <summary>
        /// The points on this element that a point picked off the plan should snap to, in
        /// model mm. Empty here: an element that offers nothing is simply never snapped to.
        /// </summary>
        public virtual IEnumerable<SnapPoint> SnapPoints { get { yield break; } }

        /// <summary>
        /// The straight runs of this element, which is where the midpoint, the crossing and
        /// the nearest-point snaps come from.
        /// </summary>
        public virtual IEnumerable<SnapEdge> SnapEdges { get { yield break; } }

        /// <summary>Geometry in model coordinates. Built once and reused across renders.</summary>
        public Geometry Geometry
        {
            get
            {
                if (cachedGeometry == null)
                {
                    cachedGeometry = BuildGeometry();
                    cachedGeometry.Freeze();
                }
                return cachedGeometry;
            }
        }

        protected abstract Geometry BuildGeometry();

        /// <summary>True when the element outline should be filled rather than only stroked.</summary>
        public virtual bool IsFilled => false;

        public string Description => $"{Kind} {Identifier}";

        /// <summary>Value of one named result, or an empty string when this element has none.</summary>
        public string GetFieldValue(string key)
        {
            ResultField field = ResultFields.FirstOrDefault(f => f.Key == key);
            return field?.Value ?? "";
        }

        public bool IsFieldFailure(string key)
        {
            ResultField field = ResultFields.FirstOrDefault(f => f.Key == key);
            return field != null && field.IsFailure;
        }
    }

    /// <summary>A beam, drawn as its centre line.</summary>
    public class BeamElementViewModel : ElementViewModel
    {
        public BeamElementViewModel(ElementSource source, CheckState state, string identifier,
                                    string story, IList<ResultField> resultFields,
                                    MyPoint start, MyPoint end)
            : base(ElementKind.Beam, source, state, identifier, story, resultFields)
        {
            Start = new Point(start?.X ?? 0, start?.Y ?? 0);
            End = new Point(end?.X ?? 0, end?.Y ?? 0);
        }

        public Point Start { get; }
        public Point End { get; }

        public override Point LabelAnchor => new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

        public override Vector? LabelDirection => End - Start;

        protected override Geometry BuildGeometry()
        {
            return new LineGeometry(Start, End);
        }

        public override IEnumerable<SnapPoint> SnapPoints
        {
            get
            {
                yield return new SnapPoint(Start, SnapKind.Endpoint);
                yield return new SnapPoint(End, SnapKind.Endpoint);
            }
        }

        public override IEnumerable<SnapEdge> SnapEdges
        {
            get { yield return new SnapEdge(Start, End); }
        }

        public override double DistanceTo(Point p)
        {
            double dx = End.X - Start.X;
            double dy = End.Y - Start.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared < 1e-9)
            {
                return Distance(p, Start);
            }

            double k = ((p.X - Start.X) * dx + (p.Y - Start.Y) * dy) / lengthSquared;
            k = Math.Max(0, Math.Min(1, k));
            Point closest = new Point(Start.X + k * dx, Start.Y + k * dy);
            return Distance(p, closest);
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// A line load, drawn as the chain of straight pieces it really is.
    ///
    /// A chain rather than two ends, because a drawn load line can be an arc or a stepped run
    /// and its shape is half of what the check is about: a line that disagrees with the frame
    /// under it usually disagrees because of where it goes, and a straight line drawn between
    /// its two ends would hide exactly that.
    /// </summary>
    public class LineLoadElementViewModel : ElementViewModel
    {
        private readonly Point[] points;
        private readonly Point labelAnchor;
        private readonly Vector? labelDirection;

        public LineLoadElementViewModel(ElementSource source, CheckState state, string identifier,
                                        string story, IList<ResultField> resultFields,
                                        IEnumerable<MyPoint> polyline)
            : base(ElementKind.LineLoad, source, state, identifier, story, resultFields)
        {
            points = (polyline ?? new MyPoint[0])
                .Where(point => point != null)
                .Select(point => new Point(point.X, point.Y))
                .ToArray();

            labelAnchor = HalfWay(points, out labelDirection);
        }

        /// <summary>The line as it was drawn, in order.</summary>
        public IList<Point> Points { get { return points; } }

        /// <summary>
        /// What the check made of the drawn line this stands for - the whole line, or the line a
        /// stretch of it belongs to. Null on what is not a drawn line: an ETABS load nothing on
        /// the plan asks for, or a frame drawn grey under the lines.
        /// </summary>
        public LineLoadResult Result { get; set; }

        /// <summary>
        /// True when <see cref="Result"/> can be written back to the design model: the line was
        /// paired with ETABS frames and has a value to put on them.
        /// </summary>
        public bool CanWriteBack { get { return Result != null && Result.CanWriteBack; } }

        public override Point LabelAnchor { get { return labelAnchor; } }

        public override Vector? LabelDirection { get { return labelDirection; } }

        /// <summary>
        /// The point half way along the line by length, and which way the line runs there.
        ///
        /// By length rather than by counting the points, which put a straight line's label on
        /// its far end - two points, and the "middle" one was the second - and which would turn
        /// the label along the wrong leg of a bent one.
        /// </summary>
        private static Point HalfWay(Point[] line, out Vector? direction)
        {
            direction = null;
            if (line.Length == 0)
            {
                return new Point(0, 0);
            }

            double total = 0;
            for (int i = 0; i < line.Length - 1; i++)
            {
                total += (line[i + 1] - line[i]).Length;
            }

            double left = total / 2;
            for (int i = 0; i < line.Length - 1; i++)
            {
                Vector leg = line[i + 1] - line[i];
                if (leg.Length < 1e-9)
                {
                    continue;
                }

                if (left <= leg.Length || i == line.Length - 2)
                {
                    direction = leg;
                    return line[i] + leg * Math.Min(1, left / leg.Length);
                }
                left -= leg.Length;
            }

            return line[0];
        }

        protected override Geometry BuildGeometry()
        {
            if (points.Length < 2)
            {
                return Geometry.Empty;
            }

            PathFigure figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };

            for (int i = 1; i < points.Length; i++)
            {
                figure.Segments.Add(new LineSegment(points[i], true));
            }

            PathGeometry geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            geometry.Freeze();

            return geometry;
        }

        public override IEnumerable<SnapPoint> SnapPoints
        {
            get
            {
                if (points.Length > 0)
                {
                    yield return new SnapPoint(points[0], SnapKind.Endpoint);
                    yield return new SnapPoint(points[points.Length - 1], SnapKind.Endpoint);
                }
            }
        }

        public override IEnumerable<SnapEdge> SnapEdges
        {
            get
            {
                for (int i = 0; i < points.Length - 1; i++)
                {
                    yield return new SnapEdge(points[i], points[i + 1]);
                }
            }
        }

        public override double DistanceTo(Point p)
        {
            if (points.Length == 0)
            {
                return double.MaxValue;
            }

            if (points.Length == 1)
            {
                return Distance(p, points[0]);
            }

            double nearest = double.MaxValue;

            for (int i = 0; i < points.Length - 1; i++)
            {
                nearest = Math.Min(nearest, DistanceToSegment(p, points[i], points[i + 1]));
            }

            return nearest;
        }

        private static double DistanceToSegment(Point p, Point start, Point end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;

            if (lengthSquared < 1e-9)
            {
                return Distance(p, start);
            }

            double k = ((p.X - start.X) * dx + (p.Y - start.Y) * dy) / lengthSquared;
            k = Math.Max(0, Math.Min(1, k));

            return Distance(p, new Point(start.X + k * dx, start.Y + k * dy));
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>A column, drawn as its plan outline: a rectangle or a circle.</summary>
    public class ColumnElementViewModel : ElementViewModel
    {
        private readonly Point[] outline;

        public ColumnElementViewModel(ElementSource source, CheckState state, string identifier,
                                      string story, IList<ResultField> resultFields, IColumn column)
            : base(ElementKind.Column, source, state, identifier, story, resultFields)
        {
            MyPoint center = column.PlanPoint ?? MyPoint.Zero;
            Center = new Point(center.X, center.Y);
            IsRound = column.Shape == SectionShape.Round;
            Radius = column.Diameter / 2d;

            outline = ColumnGeometry.PlanOutline(column)
                                    .Select(p => new Point(p.X, p.Y))
                                    .ToArray();

            // Used for hit testing; never smaller than the fallback marker so a column with
            // unreadable dimensions can still be clicked.
            double sectionRadius = IsRound
                ? Radius
                : Math.Sqrt(column.Width * column.Width + column.Depth * column.Depth) / 2d;
            BoundingRadius = Math.Max(sectionRadius, 100);
        }

        public Point Center { get; }
        public bool IsRound { get; }
        public double Radius { get; }
        public double BoundingRadius { get; }

        public override Point LabelAnchor => Center;

        /// <summary>
        /// The centre first, because that is the point a column stands for and the one a
        /// grid intersection is usually taken from, and then its corners.
        /// </summary>
        public override IEnumerable<SnapPoint> SnapPoints
        {
            get
            {
                yield return new SnapPoint(Center, SnapKind.Centre);

                if (IsRound)
                {
                    // A circle has no corners, and its flattened outline is a hundred points
                    // none of which anybody means.
                    yield break;
                }

                foreach (Point corner in outline)
                {
                    yield return new SnapPoint(corner, SnapKind.Corner);
                }
            }
        }

        public override IEnumerable<SnapEdge> SnapEdges
        {
            get
            {
                if (IsRound || outline.Length < 3)
                {
                    yield break;
                }

                for (int i = 0; i < outline.Length; i++)
                {
                    yield return new SnapEdge(outline[i], outline[(i + 1) % outline.Length]);
                }
            }
        }

        /// <summary>
        /// Filled where the column is the thing being checked, an outline only where it is not:
        /// the grey backdrop behind another check, and the other model's section drawn under a
        /// column the two disagree about. Both of those are there to be seen through - a filled
        /// one hides the column it was drawn to explain.
        /// </summary>
        public override bool IsFilled => !IsDrawnAsContext;

        protected override Geometry BuildGeometry()
        {
            if (IsRound)
            {
                // A section with no readable dimension still gets a marker, so the column is
                // visible on the plan and its failed dimension check can be selected.
                return new EllipseGeometry(Center, Math.Max(Radius, 100), Math.Max(Radius, 100));
            }

            if (outline.Length < 3 || BoundingRadius <= 0)
            {
                return new EllipseGeometry(Center, 100, 100);
            }

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(outline[0], true, true);
                context.PolyLineTo(outline.Skip(1).ToArray(), true, true);
            }
            return geometry;
        }

        public override double DistanceTo(Point p)
        {
            double dx = p.X - Center.X;
            double dy = p.Y - Center.Y;
            double distanceToCenter = Math.Sqrt(dx * dx + dy * dy);
            // Anywhere inside the section counts as a direct hit.
            return Math.Max(0, distanceToCenter - BoundingRadius);
        }
    }

    /// <summary>
    /// A wall, drawn as the rectangle its centre line and its thickness make in plan.
    ///
    /// The line is what the two models are compared on and the thickness is what is drawn, so a
    /// wall that is out of place reads as a strip beside the one it should be lying on. A wall
    /// whose thickness could not be read is still drawn, at a width that can be seen and picked;
    /// what its thickness check says about it is that nobody knows.
    /// </summary>
    public class WallElementViewModel : ElementViewModel
    {
        /// <summary>Narrowest a wall is ever drawn, in model mm, so it stays visible and pickable.</summary>
        private const double MinimumDrawnThickness = 100;

        private readonly Point[] outline;

        public WallElementViewModel(ElementSource source, CheckState state, string identifier,
                                    string story, IList<ResultField> resultFields,
                                    MyPoint start, MyPoint end, double thickness)
            : base(ElementKind.Wall, source, state, identifier, story, resultFields)
        {
            Start = new Point(start?.X ?? 0, start?.Y ?? 0);
            End = new Point(end?.X ?? 0, end?.Y ?? 0);
            Thickness = thickness;

            DrawnHalfThickness = Math.Max(thickness, MinimumDrawnThickness) / 2d;

            outline = WallGeometry.PlanOutline(start, end, Math.Max(thickness, MinimumDrawnThickness))
                                  .Select(p => new Point(p.X, p.Y))
                                  .ToArray();
        }

        public Point Start { get; }
        public Point End { get; }
        public double Thickness { get; }

        /// <summary>Half the width the wall is drawn at, which is also its picking distance.</summary>
        public double DrawnHalfThickness { get; }

        public override Point LabelAnchor => new Point((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

        public override Vector? LabelDirection => End - Start;

        /// <summary>A wall is drawn at its thickness, so its label is set beside that.</summary>
        public override double LabelClearance => DrawnHalfThickness;

        /// <summary>
        /// The ends of the centre line, which is the line the two models are compared on, and
        /// the corners of the rectangle the wall is drawn as, which is what is visible to aim at.
        /// </summary>
        public override IEnumerable<SnapPoint> SnapPoints
        {
            get
            {
                yield return new SnapPoint(Start, SnapKind.Endpoint);
                yield return new SnapPoint(End, SnapKind.Endpoint);

                foreach (Point corner in outline)
                {
                    yield return new SnapPoint(corner, SnapKind.Corner);
                }
            }
        }

        public override IEnumerable<SnapEdge> SnapEdges
        {
            get
            {
                yield return new SnapEdge(Start, End);

                if (outline.Length < 3)
                {
                    yield break;
                }

                for (int i = 0; i < outline.Length; i++)
                {
                    yield return new SnapEdge(outline[i], outline[(i + 1) % outline.Length]);
                }
            }
        }

        public override bool IsFilled => true;

        protected override Geometry BuildGeometry()
        {
            if (outline.Length < 3)
            {
                return new LineGeometry(Start, End);
            }

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(outline[0], true, true);
                context.PolyLineTo(outline.Skip(1).ToArray(), true, true);
            }
            return geometry;
        }

        public override double DistanceTo(Point p)
        {
            double dx = End.X - Start.X;
            double dy = End.Y - Start.Y;
            double lengthSquared = dx * dx + dy * dy;

            Point closest = Start;
            if (lengthSquared > 1e-9)
            {
                double k = ((p.X - Start.X) * dx + (p.Y - Start.Y) * dy) / lengthSquared;
                k = Math.Max(0, Math.Min(1, k));
                closest = new Point(Start.X + k * dx, Start.Y + k * dy);
            }

            double distance = Math.Sqrt((p.X - closest.X) * (p.X - closest.X)
                                        + (p.Y - closest.Y) * (p.Y - closest.Y));

            // Anywhere inside the drawn wall counts as a direct hit.
            return Math.Max(0, distance - DrawnHalfThickness);
        }
    }

    /// <summary>
    /// A piece of slab, drawn as the plan region it covers with its openings left as holes.
    ///
    /// Normally this is a whole ETABS area object, drawn exactly as it was modelled. It is a
    /// part of one only where the two models stopped agreeing part way across it, and then
    /// the shape drawn here is the shape of the disagreement.
    /// </summary>
    public class SlabElementViewModel : ElementViewModel
    {
        private readonly PlanPolygon plan;
        private readonly Point labelAnchor;

        public SlabElementViewModel(ElementSource source, CheckState state, string identifier,
                                    string story, IList<ResultField> resultFields, PlanPolygon plan)
            : this(ElementKind.Slab, source, state, identifier, story, resultFields, plan)
        {
        }

        /// <summary>For another kind of region drawn the same way - a height zone.</summary>
        protected SlabElementViewModel(ElementKind kind, ElementSource source, CheckState state,
                                       string identifier, string story, IList<ResultField> resultFields,
                                       PlanPolygon plan)
            : base(kind, source, state, identifier, story, resultFields)
        {
            this.plan = plan;

            MyPoint anchor = plan == null ? MyPoint.Zero : plan.InteriorPoint();
            labelAnchor = new Point(anchor.X, anchor.Y);
        }

        public PlanPolygon Plan { get { return plan; } }

        /// <summary>Area of the piece in square metres, used for the tallies.</summary>
        public double AreaInSquareMetres { get { return plan == null ? 0 : plan.Area / 1e6; } }

        public override Point LabelAnchor { get { return labelAnchor; } }

        /// <summary>Every corner of the outline and of the openings inside it.</summary>
        public override IEnumerable<SnapPoint> SnapPoints
        {
            get
            {
                foreach (PlanRing ring in Rings())
                {
                    foreach (MyPoint corner in ring.Points)
                    {
                        yield return new SnapPoint(new Point(corner.X, corner.Y), SnapKind.Corner);
                    }
                }
            }
        }

        public override IEnumerable<SnapEdge> SnapEdges
        {
            get
            {
                foreach (PlanRing ring in Rings())
                {
                    IList<MyPoint> points = ring.Points;
                    for (int i = 0; i < points.Count; i++)
                    {
                        MyPoint from = points[i];
                        MyPoint to = points[(i + 1) % points.Count];
                        yield return new SnapEdge(new Point(from.X, from.Y), new Point(to.X, to.Y));
                    }
                }
            }
        }

        /// <summary>The outline and its openings, which snap the same way as each other.</summary>
        private IEnumerable<PlanRing> Rings()
        {
            if (plan == null)
            {
                yield break;
            }

            yield return plan.Outer;
            foreach (PlanRing hole in plan.Holes)
            {
                yield return hole;
            }
        }

        /// <summary>Context is an outline only; a result is filled so the zone reads as an area.</summary>
        public override bool IsFilled { get { return !IsDrawnAsContext; } }

        protected override Geometry BuildGeometry()
        {
            if (plan == null)
            {
                return Geometry.Empty;
            }

            StreamGeometry geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (StreamGeometryContext context = geometry.Open())
            {
                AddRing(context, plan.Outer);
                foreach (PlanRing hole in plan.Holes)
                {
                    // Even-odd filling turns the inner rings into the holes they are, so an
                    // opening in the slab is drawn as a hole rather than painted over.
                    AddRing(context, hole);
                }
            }
            return geometry;
        }

        private static void AddRing(StreamGeometryContext context, PlanRing ring)
        {
            IList<MyPoint> points = ring.Points;
            if (points.Count < 3)
            {
                return;
            }

            context.BeginFigure(new Point(points[0].X, points[0].Y), true, true);
            context.PolyLineTo(points.Skip(1).Select(point => new Point(point.X, point.Y)).ToArray(),
                               true, true);
        }

        public override double DistanceTo(Point p)
        {
            return plan == null ? double.MaxValue : plan.DistanceTo(p.X, p.Y);
        }
    }

    /// <summary>
    /// A height zone drawn by hand, drawn as the region it covers with its height written in
    /// it. It belongs to neither model, carries no results, and cannot be filed or paired; it is
    /// on the plan to be seen, picked, changed and taken away.
    ///
    /// Drawn only where it is the zone in control. Where a smaller zone overlaps it, the smaller
    /// one answers there - see <see cref="HeightZones.InOrderOfPrecedence"/> - so that part is
    /// cut out of this one and only the height that counts is coloured in. A zone the smaller
    /// ones cover completely controls nothing, and is left as a bare outline to be found by.
    /// </summary>
    public sealed class HeightZoneElementViewModel : SlabElementViewModel
    {
        private readonly IList<PlanPolygon> coveredBy;
        private Geometry visible;
        private bool hidden;
        private Point? visibleAnchor;

        /// <param name="colour">The colour of its height, "#RRGGBB" - see <see cref="HeightZones.ColourOfHeight"/>.</param>
        /// <param name="coveredBy">The smaller zones overlapping it, which answer where they do.</param>
        public HeightZoneElementViewModel(HeightZone zone, IList<ResultField> resultFields,
                                          string colour = "", IList<PlanPolygon> coveredBy = null)
            : base(ElementKind.HeightZone, ElementSource.Paired, CheckState.NotChecked,
                   zone.Name.Length > 0 ? zone.Name : "Height zone", zone.Story, resultFields, zone.Plan)
        {
            ZoneId = zone.Id;
            Height = zone.Height;
            Name = zone.Name;
            Colour = colour ?? "";
            this.coveredBy = (coveredBy ?? new List<PlanPolygon>()).Where(plan => plan != null).ToList();
            Outcomes = new List<CheckOutcome>();
        }

        /// <summary>The zone this draws, as the store files it.</summary>
        public string ZoneId { get; }

        /// <summary>The colour of its height, as "#RRGGBB".</summary>
        public string Colour { get; }

        /// <summary>The floor to floor height it stands for, in mm.</summary>
        public double Height { get; }

        public string Name { get; }

        /// <summary>True when smaller zones cover all of it, so it answers nowhere.</summary>
        public bool IsHidden
        {
            get
            {
                Settle();
                return hidden;
            }
        }

        /// <summary>No height written over a zone that answers nowhere: every point of it is another zone's.</summary>
        public override string Caption => IsHidden ? null : HeightZone.Metres(Height) + (Name.Length > 0 ? " - " + Name : "");

        public override bool IsFilled => base.IsFilled && !IsHidden;

        /// <summary>In the part of it that is still its own, so the height is written where it counts.</summary>
        public override Point LabelAnchor
        {
            get
            {
                Settle();
                return visibleAnchor ?? base.LabelAnchor;
            }
        }

        protected override Geometry BuildGeometry()
        {
            Settle();
            return visible;
        }

        /// <summary>
        /// Picked where it answers, not where a smaller zone does: a click inside the part cut
        /// out of it is as far from it as its nearest edge.
        /// </summary>
        public override double DistanceTo(Point p)
        {
            double distance = base.DistanceTo(p);
            if (distance > 0 || !coveredBy.Any(plan => plan.Bounds.Contains(p.X, p.Y) && plan.Contains(p.X, p.Y)))
            {
                return distance;
            }
            return Plan == null ? double.MaxValue : DistanceToEdge(Plan.Outer, p);
        }

        /// <summary>Works out once what is left of it to draw, and where to write on it.</summary>
        private void Settle()
        {
            if (visible != null)
            {
                return;
            }

            Geometry whole = base.BuildGeometry();
            if (coveredBy.Count == 0 || whole.IsEmpty())
            {
                visible = whole;
                return;
            }

            Geometry over = null;
            foreach (PlanPolygon plan in coveredBy)
            {
                Geometry one = Outline(plan, true);
                over = over == null ? one : Geometry.Combine(over, one, GeometryCombineMode.Union, null);
            }

            PathGeometry left = Geometry.Combine(whole, over, GeometryCombineMode.Exclude, null);

            // Less than a square centimetre left is the smaller zones drawn to its edges, not a
            // part of it that answers.
            if (left.IsEmpty() || left.GetArea() < 100)
            {
                hidden = true;
                visible = Outline(Plan, false);
                return;
            }

            visible = left;
            visibleAnchor = AnchorIn(left);
        }

        /// <summary>
        /// A point well inside the largest piece left: its outline with the holes in it, asked
        /// for an interior point the way every slab piece is.
        /// </summary>
        private static Point? AnchorIn(PathGeometry left)
        {
            List<PlanRing> rings = new List<PlanRing>();
            foreach (PathFigure figure in left.GetFlattenedPathGeometry().Figures)
            {
                List<MyPoint> points = new List<MyPoint> { new MyPoint(figure.StartPoint.X, figure.StartPoint.Y, 0) };
                foreach (PathSegment segment in figure.Segments)
                {
                    PolyLineSegment poly = segment as PolyLineSegment;
                    LineSegment line = segment as LineSegment;
                    if (poly != null)
                    {
                        points.AddRange(poly.Points.Select(point => new MyPoint(point.X, point.Y, 0)));
                    }
                    else if (line != null)
                    {
                        points.Add(new MyPoint(line.Point.X, line.Point.Y, 0));
                    }
                }

                PlanRing ring = points.Count >= 3 ? PlanRing.FromPoints(points) : null;
                if (ring != null)
                {
                    rings.Add(ring);
                }
            }

            PlanRing outer = rings.OrderByDescending(ring => ring.Area).FirstOrDefault();
            if (outer == null)
            {
                return null;
            }

            PlanPolygon piece = PlanPolygon.Create(outer, rings.Where(ring => !ReferenceEquals(ring, outer)
                                                                             && PlanPolygon.Create(outer, new PlanRing[0])
                                                                                           .Contains(ring.Points[0].X, ring.Points[0].Y)));
            MyPoint inside = piece.InteriorPoint();
            return new Point(inside.X, inside.Y);
        }

        private static Geometry Outline(PlanPolygon plan, bool filled)
        {
            StreamGeometry geometry = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (StreamGeometryContext context = geometry.Open())
            {
                foreach (PlanRing ring in new[] { plan.Outer }.Concat(plan.Holes))
                {
                    IList<MyPoint> points = ring.Points;
                    if (points.Count < 3)
                    {
                        continue;
                    }
                    context.BeginFigure(new Point(points[0].X, points[0].Y), filled, true);
                    context.PolyLineTo(points.Skip(1).Select(point => new Point(point.X, point.Y)).ToArray(), true, true);
                }
            }
            geometry.Freeze();
            return geometry;
        }

        private static double DistanceToEdge(PlanRing ring, Point p)
        {
            IList<MyPoint> points = ring.Points;
            double nearest = double.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double lengthSquared = dx * dx + dy * dy;
                double t = lengthSquared < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared));
                double x = a.X + t * dx - p.X;
                double y = a.Y + t * dy - p.Y;
                nearest = Math.Min(nearest, Math.Sqrt(x * x + y * y));
            }
            return nearest;
        }
    }
}
