using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>Overall verdict for one checked wall.</summary>
    public enum WallCheckState
    {
        /// <summary>Thickness and location both inside the buffers. Drawn green.</summary>
        Matched,
        /// <summary>Paired but at least one check failed. Drawn red.</summary>
        Mismatched,
        /// <summary>
        /// Nothing failed, but something could not be checked - today that means a thickness
        /// one of the two models does not give up. Drawn orange.
        /// </summary>
        Review,
        /// <summary>No counterpart found in the other model.</summary>
        Unpaired
    }

    /// <summary>
    /// An ETABS wall matched to a Revit wall, with the thickness and location checks between
    /// them.
    ///
    /// The pair is not always one shell to one wall. ETABS splits a wall run into panels
    /// wherever it needs a node, so several ETABS areas commonly stand along one Revit wall,
    /// and each of them is checked against that wall on its own. What is compared is therefore
    /// the piece of wall the ETABS area covers, not the whole run: its thickness, and how far
    /// off the Revit wall's line it sits.
    /// </summary>
    public class PairedWall : IHasResultFields
    {
        public readonly Wall_E etabsWall;
        public readonly Wall_R revitWall;

        private readonly List<ResultField> resultFields = new List<ResultField>();
        private readonly List<CheckOutcome> outcomes = new List<CheckOutcome>();

        private double locationDelta;
        private double thicknessDelta;
        private double angleDelta;
        private double overlapLength;
        private double coveredLength;

        private bool locationMatched;
        private bool thicknessMatched;
        private bool thicknessReadable;
        private bool lengthMatched = true;
        private bool lengthKnown;

        private WallCheckState state = WallCheckState.Matched;
        private string note = "";

        public PairedWall(Wall_E etabs, Wall_R revit)
        {
            etabsWall = etabs;
            revitWall = revit;

            RunChecks();
            BuildResultFields();
        }

        #region results

        public WallCheckState State { get => state; }

        /// <summary>
        /// What each check concluded, one entry per check. The viewer shows and hides these
        /// independently, so a wall can have its thickness result on show while its location
        /// result is switched off, or the other way round.
        /// </summary>
        public IList<CheckOutcome> Outcomes { get => outcomes; }

        public string Note { get => note; }

        /// <summary>How far the ETABS wall sits off the Revit wall's line, in mm.</summary>
        public double LocationDelta { get => locationDelta; }

        /// <summary>Difference between the two thicknesses, in mm.</summary>
        public double ThicknessDelta { get => thicknessDelta; }

        /// <summary>Angle between the two centre lines, folded into 0..90 degrees.</summary>
        public double AngleDelta { get => angleDelta; }

        /// <summary>How much of the two walls run alongside each other, in mm.</summary>
        public double OverlapLength { get => overlapLength; }

        /// <summary>
        /// How much of the Revit wall has ETABS wall standing along it, in mm - every panel on
        /// it counted, not only this one. Nought until <see cref="SetCoveredLength"/> has said.
        /// </summary>
        public double CoveredLength { get => coveredLength; }

        /// <summary>How much of the Revit wall ETABS does not reach, in mm.</summary>
        public double LengthShortfall
        {
            get { return Math.Max(0, revitWall.Length - coveredLength); }
        }

        /// <summary>
        /// True while ETABS runs the length of the Revit wall, give or take the buffer. False
        /// is the warning: the two agree there is a wall here and disagree about how far it
        /// runs.
        /// </summary>
        public bool LengthMatched { get => lengthMatched; }

        /// <summary>
        /// True where the panel stands off the Revit wall line by more than the buffer: the
        /// wall is drawn where ETABS has it, and this says Revit has it somewhere else.
        /// </summary>
        public bool Displaced { get => !locationMatched; }

        public bool LocationMatched { get => locationMatched; }
        public bool ThicknessMatched { get => thicknessMatched; }

        /// <summary>
        /// True where the two thicknesses were both read and did not agree. A thickness that
        /// could not be read is not a failure and is counted with the walls wanting a look,
        /// so <see cref="ThicknessMatched"/> alone would over-count the mismatches.
        /// </summary>
        public bool ThicknessFailed { get => thicknessReadable && !thicknessMatched; }

        public IList<ResultField> ResultFields { get => resultFields; }

        /// <summary>The story the pair is reported on, which is the ETABS one where it has a name.</summary>
        public string Story
        {
            get { return string.IsNullOrEmpty(etabsWall.Story) ? revitWall.Story : etabsWall.Story; }
        }

        /// <summary>
        /// Where the pair is drawn: on the ETABS panel, not on the Revit wall.
        ///
        /// ETABS is the model being checked against, so it is the one the plan shows. It is
        /// also the one that answers the question the plan is being read for - what the design
        /// model has, and where - and the panels are what carry the verdicts. The Revit wall
        /// stays off the plan altogether: where ETABS stops short of it, the pair says so as a
        /// warning instead - see <see cref="PrefrencesData.WallLengthTolerance"/>.
        /// </summary>
        public MyPoint PlanStart { get => etabsWall.PlanStart; }
        public MyPoint PlanEnd { get => etabsWall.PlanEnd; }

        /// <summary>
        /// The thickness the pair is drawn at: the ETABS one, falling back to the Revit one
        /// where ETABS would not give a thickness up. A wall whose property could not be read
        /// is still a wall on the plan rather than a hairline, and its thickness result says
        /// outright that nobody knows.
        /// </summary>
        public double Thickness
        {
            get { return etabsWall.Thickness > 0 ? etabsWall.Thickness : revitWall.Thickness; }
        }

        /// <summary>Plan outline of the piece of wall drawn, from the same line and thickness.</summary>
        public PointF[] PlanOutline { get => WallGeometry.PlanOutline(PlanStart, PlanEnd, Thickness); }

        #endregion

        /// <summary>
        /// The worst of several results about the same piece of wall: the one that failed
        /// over the one that only wants a look, and among equals the one furthest off the
        /// line.
        ///
        /// Wanted where a wall meshed up its height is drawn as one wall. A wall is only as
        /// right as its worst panel, and the panel this picks is the one whose verdict the
        /// wall is then shown and coloured by.
        /// </summary>
        public static PairedWall Worst(IEnumerable<PairedWall> walls)
        {
            return walls.OrderByDescending(wall => Severity(wall.State))
                        .ThenByDescending(wall => wall.LocationDelta)
                        .First();
        }

        private static int Severity(WallCheckState state)
        {
            switch (state)
            {
                case WallCheckState.Matched:
                    return 0;
                case WallCheckState.Review:
                    return 1;
                default:
                    return 2;
            }
        }

        /// <summary>
        /// How much of the Revit wall the ETABS panels along it cover, told to the pair once
        /// they are all known, which is what turns the length warning on.
        ///
        /// It cannot be worked out while the pair is being built: a wall meshed into five
        /// panels is five pairs, and no one of them knows what the other four cover. So the
        /// checks are run again here with the answer in hand - they are written to be run
        /// twice, and the second run is the one that publishes a length result at all.
        /// </summary>
        public void SetCoveredLength(double covered)
        {
            coveredLength = covered;
            lengthKnown = true;

            RunChecks();
            BuildResultFields();
        }

        private void RunChecks()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            // From scratch: this runs a second time once the covered length is known, and a
            // note appended twice would say everything twice.
            note = "";

            CheckLocation(pref);
            CheckThickness(pref);
            CheckLength(pref);

            CollectOutcomes();

            if (outcomes.Any(outcome => outcome.Level == ResultLevel.Failed))
            {
                state = WallCheckState.Mismatched;
            }
            else if (outcomes.Any(outcome => outcome.Level == ResultLevel.Review))
            {
                state = WallCheckState.Review;
            }
            else
            {
                state = WallCheckState.Matched;
            }

            if (state == WallCheckState.Matched)
            {
                note = "OK";
            }
            else
            {
                note = note.TrimEnd(',', ' ');
            }
        }

        /// <summary>
        /// How far the ETABS wall stands off the line the Revit wall runs along, measured at
        /// both of its ends and reported as the worse of the two.
        ///
        /// Both ends rather than the middle, because that one number then covers the two ways a
        /// wall goes wrong: one moved sideways, and one drawn at an angle to the other model's.
        /// The angle itself is reported beside it so a wall that is out at one end only can be
        /// told from one that is out along its whole length.
        /// </summary>
        private void CheckLocation(PrefrencesData pref)
        {
            locationDelta = WallGeometry.Offset(etabsWall, revitWall);
            angleDelta = WallGeometry.AngleBetween(etabsWall, revitWall);
            overlapLength = WallGeometry.OverlapLength(etabsWall, revitWall);

            locationMatched = locationDelta <= pref.WallLocationTolerance;
            if (!locationMatched)
            {
                note += $"displaced {Math.Round(locationDelta)}mm from the Revit wall, ";
            }
        }

        /// <summary>
        /// How far ETABS runs against how far Revit runs, once every panel on the Revit wall
        /// has been counted.
        ///
        /// A warning rather than a failure. The two models are agreed there is a wall here, and
        /// a run modelled in one piece in Revit and in three in ETABS is not wrong for that.
        /// What is worth saying is that ETABS stops short of where Revit stops, which on a plan
        /// drawn from the ETABS model is otherwise invisible.
        /// </summary>
        private void CheckLength(PrefrencesData pref)
        {
            if (!lengthKnown)
            {
                lengthMatched = true;
                return;
            }

            lengthMatched = LengthShortfall <= pref.WallLengthTolerance;
            if (!lengthMatched)
            {
                note += $"ETABS covers {Math.Round(coveredLength)} of {Math.Round(revitWall.Length)}mm, ";
            }
        }

        /// <summary>
        /// The two thicknesses against each other. A thickness neither model gives up is not a
        /// mismatch and is not a pass either: it is said so, in orange, rather than being
        /// reported as a wall that agrees when nothing was compared.
        /// </summary>
        private void CheckThickness(PrefrencesData pref)
        {
            thicknessReadable = etabsWall.Thickness > 0 && revitWall.Thickness > 0;

            if (!thicknessReadable)
            {
                thicknessMatched = false;
                note += ThicknessUnreadable() + ", ";
                return;
            }

            thicknessDelta = Math.Abs(etabsWall.Thickness - revitWall.Thickness);
            thicknessMatched = thicknessDelta <= pref.WallThicknessTolerance;

            if (!thicknessMatched)
            {
                note += $"thickness {Math.Round(revitWall.Thickness)} vs {Math.Round(etabsWall.Thickness)}, ";
            }
        }

        private string ThicknessUnreadable()
        {
            if (etabsWall.Thickness <= 0 && revitWall.Thickness <= 0)
            {
                return "neither model gives a thickness";
            }
            return etabsWall.Thickness <= 0
                ? "the ETABS section gives no thickness"
                : "the Revit type gives no thickness";
        }

        /// <summary>
        /// Records what each check concluded, as its own result. These stand on their own: the
        /// viewer draws and filters each one without reference to the other.
        /// </summary>
        private void CollectOutcomes()
        {
            outcomes.Clear();

            outcomes.Add(new CheckOutcome(CheckKind.WallLocation,
                locationMatched ? ResultLevel.Passed : ResultLevel.Failed,
                locationMatched
                    ? $"on the Revit wall line, {Math.Round(locationDelta)}mm off"
                    : $"displaced {Math.Round(locationDelta)}mm from the Revit wall",
                etabs: $"{Math.Round(locationDelta)} mm off",
                revit: "on the line"));

            // Only once the covered length is known. Before that the pair has no length result
            // rather than a colourless one, the same way a column with no type mark has none.
            if (lengthKnown)
            {
                outcomes.Add(new CheckOutcome(CheckKind.WallLength,
                    lengthMatched ? ResultLevel.Passed : ResultLevel.Review,
                    $"ETABS covers {Math.Round(coveredLength)} of "
                    + $"{Math.Round(revitWall.Length)}mm of the Revit wall",
                    etabs: $"{Math.Round(coveredLength)} mm",
                    revit: $"{Math.Round(revitWall.Length)} mm"));
            }

            outcomes.Add(new CheckOutcome(CheckKind.WallThickness, ThicknessLevel(),
                thicknessReadable
                    ? $"{Math.Round(revitWall.Thickness)} in Revit vs {Math.Round(etabsWall.Thickness)} in ETABS"
                    : ThicknessUnreadable(),
                etabs: thicknessReadable ? $"{Math.Round(etabsWall.Thickness)}" : "no section",
                revit: thicknessReadable ? $"{Math.Round(revitWall.Thickness)}" : "no layers"));

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        private ResultLevel ThicknessLevel()
        {
            if (!thicknessReadable)
            {
                return ResultLevel.Review;
            }
            return thicknessMatched ? ResultLevel.Passed : ResultLevel.Failed;
        }

        private void BuildResultFields()
        {
            resultFields.Clear();

            resultFields.Add(new ResultField(WallResultKeys.State, "Check state",
                state.ToString(), state != WallCheckState.Matched));
            resultFields.Add(new ResultField(WallResultKeys.RevitId, "Revit id",
                revitWall.RevitID.ToString()));
            resultFields.Add(new ResultField(WallResultKeys.EtabsName, "ETABS name",
                etabsWall.UniqueName));
            resultFields.Add(new ResultField(WallResultKeys.RevitType, "Revit wall type",
                revitWall.TypeName));
            resultFields.Add(new ResultField(WallResultKeys.EtabsProperty, "ETABS property",
                etabsWall.PropertyName));
            resultFields.Add(new ResultField(WallResultKeys.TypeMark, "Wall Type Mark",
                revitWall.TypeMark));

            resultFields.Add(new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                $"{Math.Round(revitWall.Thickness)} / {Math.Round(etabsWall.Thickness)}",
                !thicknessMatched, CheckKind.WallThickness));
            resultFields.Add(ResultField.Number(WallResultKeys.ThicknessDelta,
                "Thickness delta (mm)", thicknessDelta, 0, !thicknessMatched, CheckKind.WallThickness));

            resultFields.Add(ResultField.Number(WallResultKeys.LocationDelta,
                "Line offset (mm)", locationDelta, 0, !locationMatched, CheckKind.WallLocation));
            resultFields.Add(ResultField.Number(WallResultKeys.AngleDelta,
                "Angle delta (deg)", angleDelta, 1, !locationMatched, CheckKind.WallLocation));
            resultFields.Add(ResultField.Number(WallResultKeys.Overlap,
                "Runs alongside (mm)", overlapLength, 0, false, CheckKind.WallLocation));

            if (lengthKnown)
            {
                resultFields.Add(new ResultField(WallResultKeys.Coverage, "ETABS length / Revit length",
                    $"{Math.Round(coveredLength)} / {Math.Round(revitWall.Length)}",
                    !lengthMatched, CheckKind.WallLength));
                resultFields.Add(ResultField.Number(WallResultKeys.LengthShortfall,
                    "Length ETABS does not reach (mm)", LengthShortfall, 0,
                    !lengthMatched, CheckKind.WallLength));
            }

            resultFields.Add(new ResultField(WallResultKeys.Note, "Note", note,
                state != WallCheckState.Matched));
        }
    }

    /// <summary>Field keys produced by the wall check, so the viewer can name them.</summary>
    public static class WallResultKeys
    {
        public const string State = "Wall.State";
        public const string RevitId = "Wall.RevitId";
        public const string EtabsName = "Wall.EtabsName";

        /// <summary>
        /// How far ETABS runs along the Revit wall against how far the Revit wall runs. The
        /// plan is drawn from the ETABS model, so this is what says the Revit wall carries on
        /// past where the drawing stops.
        /// </summary>
        public const string Coverage = "Wall.Coverage";

        /// <summary>How much of the Revit wall ETABS has nothing along, in mm.</summary>
        public const string LengthShortfall = "Wall.LengthShortfall";

        /// <summary>
        /// How many ETABS panels stand on this line of wall, where the plan has folded a
        /// meshed wall back into one. Absent from a wall that is one panel, so it never
        /// stamps "1" on every wall in the model.
        /// </summary>
        public const string PanelCount = "Wall.PanelCount";
        public const string RevitType = "Wall.RevitType";
        public const string EtabsProperty = "Wall.EtabsProperty";
        public const string TypeMark = "Wall.TypeMark";
        public const string Thickness = "Wall.Thickness";
        public const string ThicknessDelta = "Wall.ThicknessDelta";
        public const string LocationDelta = "Wall.LocationDelta";
        public const string AngleDelta = "Wall.AngleDelta";
        public const string Overlap = "Wall.Overlap";
        public const string Note = "Wall.Note";
    }
}
