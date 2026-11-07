using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>Overall verdict for one checked column.</summary>
    public enum ColumnCheckState
    {
        /// <summary>
        /// Location, orientation, section and type mark all inside the buffers. Drawn green.
        /// </summary>
        Matched,
        /// <summary>Paired but at least one check failed. Drawn red.</summary>
        Mismatched,
        /// <summary>
        /// Every check passed, but something about the column still wants a human look -
        /// today that means the ETABS column is in further groups besides the one carrying
        /// its type mark. Drawn orange.
        /// </summary>
        Review,
        /// <summary>No counterpart found in the other model.</summary>
        Unpaired
    }


    /// <summary>
    /// An ETABS column matched to a Revit column, together with the location, orientation,
    /// section and type mark checks between them.
    /// </summary>
    public class PairedColumn : IDrawAble, IHasResultFields
    {
        public readonly Column_e etabsColumn;
        public readonly Column_r revitColumn;

        private readonly List<ResultField> resultFields = new List<ResultField>();
        private readonly List<CheckOutcome> outcomes = new List<CheckOutcome>();

        private double locationDelta;
        private double locationTolerance;
        private double orientationDelta;
        private double longSideDelta;
        private double shortSideDelta;
        private double diameterDelta;

        private bool locationMatched;
        private bool orientationMatched;
        private bool shapeMatched;
        private bool dimensionsMatched;
        private ColumnGroupCheck groupCheck;

        private ColumnCheckState state = ColumnCheckState.Matched;
        private string note = "";

        public PairedColumn(Column_e etabs, Column_r revit)
            : base(ComparasonState.Matched, etabs.PlanPoint, etabs.PlanPoint)
        {
            etabsColumn = etabs;
            revitColumn = revit;
            Story = etabs.Story;
            Rotaion = etabs.AngleDegrees;

            RunChecks();
            BuildResultFields();
        }

        #region results

        public ColumnCheckState State { get => state; }

        /// <summary>
        /// What each check concluded, one entry per check. The viewer shows and hides these
        /// independently, so a column can have its location result on show while its type
        /// mark result is switched off, or the other way round.
        /// </summary>
        public IList<CheckOutcome> Outcomes { get => outcomes; }

        public string Note { get => note; }

        /// <summary>The Revit column type mark measured against the ETABS group assignments.</summary>
        public ColumnGroupCheck GroupCheck { get => groupCheck; }

        /// <summary>Plan distance between the two insertion points, in mm.</summary>
        public double LocationDelta { get => locationDelta; }

        /// <summary>Orientation difference folded into 0..90 degrees, in degrees.</summary>
        public double OrientationDelta { get => orientationDelta; }

        /// <summary>Difference between the two long sides, in mm.</summary>
        public double LongSideDelta { get => longSideDelta; }

        /// <summary>Difference between the two short sides, in mm.</summary>
        public double ShortSideDelta { get => shortSideDelta; }

        public double DiameterDelta { get => diameterDelta; }

        public bool LocationMatched { get => locationMatched; }
        public bool OrientationMatched { get => orientationMatched; }
        public bool ShapeMatched { get => shapeMatched; }
        public bool DimensionsMatched { get => dimensionsMatched; }

        /// <summary>True when nothing about the type mark against the groups counts against the column.</summary>
        public bool TypeMarkMatched { get => groupCheck.Passed; }

        public IList<ResultField> ResultFields { get => resultFields; }

        /// <summary>Plan outline of the Revit column, used to draw the checked shape.</summary>
        public PointF[] PlanOutline { get => revitColumn.PlanOutline; }

        #endregion

        private void RunChecks()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            CheckLocation(pref);
            CheckShape();
            CheckOrientation(pref);
            CheckDimensions(pref);
            CheckTypeMarkAgainstGroups();

            CollectOutcomes();

            // The overall verdict, used for the tallies. A failed check outranks a review: a
            // column in the wrong place is mismatched whatever its groups say. Review is for
            // a column that is otherwise correct and only needs its extra groups looking at.
            if (outcomes.Any(outcome => outcome.Level == ResultLevel.Failed))
            {
                state = ColumnCheckState.Mismatched;
            }
            else if (outcomes.Any(outcome => outcome.Level == ResultLevel.Review))
            {
                state = ColumnCheckState.Review;
            }
            else
            {
                state = ColumnCheckState.Matched;
            }

            if (state == ColumnCheckState.Matched)
            {
                note = "OK";
            }
            else
            {
                note = note.TrimEnd(',', ' ');
            }
        }

        /// <summary>
        /// Records what every check concluded, as its own result. These stand on their own:
        /// the viewer draws and filters each one without reference to the others.
        /// </summary>
        private void CollectOutcomes()
        {
            outcomes.Clear();

            // Location is the one check with no second value to show: it measures one
            // distance between the two models, so the "ETABS" side is that distance and the
            // "Revit" side is the tolerance it was judged against.
            outcomes.Add(new CheckOutcome(CheckKind.Location, Level(locationMatched),
                $"location off by {Math.Round(locationDelta)}mm",
                etabs: $"{Math.Round(locationDelta)} mm off",
                revit: $"tol {Math.Round(locationTolerance)}"));

            outcomes.Add(new CheckOutcome(CheckKind.Orientation, Level(orientationMatched),
                $"rotated by {Math.Round(orientationDelta, 1)}deg",
                etabs: $"{Math.Round(etabsColumn.AngleDegrees, 1)}°",
                revit: $"{Math.Round(revitColumn.AngleDegrees, 1)}°"));

            outcomes.Add(new CheckOutcome(CheckKind.Shape, Level(shapeMatched),
                $"{revitColumn.Shape} in Revit vs {etabsColumn.Shape} in ETABS",
                etabs: etabsColumn.Shape.ToString(),
                revit: revitColumn.Shape.ToString()));

            outcomes.Add(new CheckOutcome(CheckKind.Dimensions, Level(dimensionsMatched),
                DimensionsSummary(),
                etabs: SideSummary(etabsColumn),
                revit: SideSummary(revitColumn)));

            // With neither a type mark nor a naming group there is nothing this check can
            // conclude, so it publishes no result at all rather than a colourless one.
            if (groupCheck.Result != GroupCheckResult.NotChecked)
            {
                outcomes.Add(new CheckOutcome(CheckKind.TypeMark, TypeMarkLevel(),
                    groupCheck.Description,
                    etabs: string.Join(", ", groupCheck.Groups), revit: groupCheck.TypeMark));
            }

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        private static ResultLevel Level(bool matched)
        {
            return matched ? ResultLevel.Passed : ResultLevel.Failed;
        }

        private ResultLevel TypeMarkLevel()
        {
            switch (groupCheck.Result)
            {
                case GroupCheckResult.Mismatched:
                    return ResultLevel.Failed;
                case GroupCheckResult.MatchedWithExtraGroups:
                    return ResultLevel.Review;
                default:
                    return ResultLevel.Passed;
            }
        }

        /// <summary>The section as one string, so the two sit under each other in the card.</summary>
        private static string SideSummary(IColumn column)
        {
            return column.Shape == SectionShape.Round
                ? $"Ø{Math.Round(column.Diameter)}"
                : $"{Math.Round(ColumnGeometry.LongSide(column))} × {Math.Round(ColumnGeometry.ShortSide(column))}";
        }

        private string DimensionsSummary()
        {
            if (etabsColumn.Shape == SectionShape.Round && revitColumn.Shape == SectionShape.Round)
            {
                return $"diameter {revitColumn.Diameter} vs {etabsColumn.Diameter}";
            }
            return $"{ColumnGeometry.LongSide(revitColumn)}x{ColumnGeometry.ShortSide(revitColumn)} vs " +
                   $"{ColumnGeometry.LongSide(etabsColumn)}x{ColumnGeometry.ShortSide(etabsColumn)}";
        }

        private void CheckLocation(PrefrencesData pref)
        {
            locationDelta = ColumnGeometry.PlanDistance(etabsColumn.PlanPoint, revitColumn.PlanPoint);
            locationTolerance = pref.ColumnLocationTolerance;
            locationMatched = locationDelta <= locationTolerance;
            if (!locationMatched)
            {
                note += $"location off by {Math.Round(locationDelta)}mm, ";
            }
        }

        private void CheckShape()
        {
            shapeMatched = etabsColumn.Shape == revitColumn.Shape
                           && etabsColumn.Shape != SectionShape.Unknown;
            if (!shapeMatched)
            {
                note += $"shape {revitColumn.Shape} in Revit vs {etabsColumn.Shape} in ETABS, ";
            }
        }

        private void CheckOrientation(PrefrencesData pref)
        {
            if (etabsColumn.Shape == SectionShape.Round && revitColumn.Shape == SectionShape.Round)
            {
                // A circle has no orientation to get wrong.
                orientationDelta = 0;
                orientationMatched = true;
                return;
            }

            // The difference is folded into 0..180 degrees, since a rectangle looks the same
            // rotated by half a turn. A quarter turn is not folded away here: which plan
            // dimension each model calls its width is already settled by
            // PrefrencesData.SwapRevitColumnWidthDepth, so a 90 degree difference at this
            // point is a real discrepancy, and CheckDimensions counts on this check to catch it.
            orientationDelta = ColumnGeometry.AngularDifference(
                etabsColumn.AngleDegrees, revitColumn.AngleDegrees, 180d);

            // Matching, or off by less than the buffer, counts as a pass.
            orientationMatched = orientationDelta <= pref.ColumnOrientationTolerance;
            if (!orientationMatched)
            {
                note += $"rotated by {Math.Round(orientationDelta, 1)}deg, ";
            }
        }

        private void CheckDimensions(PrefrencesData pref)
        {
            if (!shapeMatched)
            {
                dimensionsMatched = false;
                return;
            }

            if (etabsColumn.Shape == SectionShape.Round)
            {
                diameterDelta = Math.Abs(etabsColumn.Diameter - revitColumn.Diameter);
                dimensionsMatched = diameterDelta <= pref.ColumnDimensionTolerance;
                if (!dimensionsMatched)
                {
                    note += $"diameter {revitColumn.Diameter} vs {etabsColumn.Diameter}, ";
                }
                return;
            }

            // The section is measured long side against long side and short against short,
            // so a column modelled the other way round is still the right size. A square
            // column has the same value for both sides and so passes either way; the quarter
            // turn itself, where it matters, is what the orientation check is for.
            longSideDelta = Math.Abs(ColumnGeometry.LongSide(etabsColumn) - ColumnGeometry.LongSide(revitColumn));
            shortSideDelta = Math.Abs(ColumnGeometry.ShortSide(etabsColumn) - ColumnGeometry.ShortSide(revitColumn));
            dimensionsMatched = longSideDelta <= pref.ColumnDimensionTolerance
                                && shortSideDelta <= pref.ColumnDimensionTolerance;

            if (dimensionsMatched)
            {
                return;
            }

            if (longSideDelta > pref.ColumnDimensionTolerance)
            {
                note += $"long side {ColumnGeometry.LongSide(revitColumn)} vs {ColumnGeometry.LongSide(etabsColumn)}, ";
            }
            if (shortSideDelta > pref.ColumnDimensionTolerance)
            {
                note += $"short side {ColumnGeometry.ShortSide(revitColumn)} vs {ColumnGeometry.ShortSide(etabsColumn)}, ";
            }
        }

        /// <summary>
        /// Checks the Revit column type mark against the ETABS frame groups the column is
        /// assigned to. A column in several groups still matches on any one of them; the ones
        /// left over are noted so they can be reviewed. See <see cref="ColumnGroupCheck"/>.
        /// </summary>
        private void CheckTypeMarkAgainstGroups()
        {
            groupCheck = ColumnGroupCheck.Compare(revitColumn, etabsColumn);

            if (groupCheck.Result == GroupCheckResult.Matched
                || groupCheck.Result == GroupCheckResult.NotChecked)
            {
                return;
            }

            note += groupCheck.Description + ", ";
        }

        private void BuildResultFields()
        {
            resultFields.Clear();

            resultFields.Add(new ResultField(ColumnResultKeys.State, "Check state",
                state.ToString(), state != ColumnCheckState.Matched));
            resultFields.Add(new ResultField(ColumnResultKeys.RevitId, "Revit id",
                revitColumn.RevitID.ToString()));
            resultFields.Add(new ResultField(ColumnResultKeys.EtabsName, "ETABS name",
                etabsColumn.EtabsUniqueName));
            resultFields.Add(new ResultField(ColumnResultKeys.RevitType, "Revit type",
                revitColumn.TypeName));
            if (!string.IsNullOrEmpty(revitColumn.SizeSource))
            {
                resultFields.Add(new ResultField(ColumnResultKeys.SizeSource, "Revit size from",
                    revitColumn.SizeSource));
            }
            resultFields.Add(new ResultField(ColumnResultKeys.RevitFamily, "Revit family",
                revitColumn.FamilyName));
            resultFields.Add(new ResultField(ColumnResultKeys.EtabsSection, "ETABS section",
                etabsColumn.SectionName));

            bool typeMarkFailed = groupCheck.Result == GroupCheckResult.Mismatched;
            resultFields.Add(new ResultField(ColumnResultKeys.TypeMark, "Column Type Mark",
                groupCheck.TypeMark, typeMarkFailed, CheckKind.TypeMark));
            resultFields.Add(new ResultField(ColumnResultKeys.EtabsGroups, "ETABS groups",
                groupCheck.GroupsText, typeMarkFailed, CheckKind.TypeMark));
            resultFields.Add(new ResultField(ColumnResultKeys.GroupCheck, "Type mark vs groups",
                groupCheck.ResultText, typeMarkFailed || groupCheck.HasExtraGroups, CheckKind.TypeMark));

            resultFields.Add(ResultField.Number(ColumnResultKeys.LocationDelta,
                "Location delta (mm)", locationDelta, 0, !locationMatched, CheckKind.Location));
            resultFields.Add(ResultField.Number(ColumnResultKeys.OrientationDelta,
                "Orientation delta (deg)", orientationDelta, 1, !orientationMatched, CheckKind.Orientation));

            if (etabsColumn.Shape == SectionShape.Round || revitColumn.Shape == SectionShape.Round)
            {
                resultFields.Add(new ResultField(ColumnResultKeys.Diameter, "Diameter R / E",
                    $"{revitColumn.Diameter} / {etabsColumn.Diameter}", !dimensionsMatched,
                    CheckKind.Dimensions));
            }
            else
            {
                double tolerance = PrefrencesData.getPrefrencesData().ColumnDimensionTolerance;
                resultFields.Add(new ResultField(ColumnResultKeys.LongSide, "Long side R / E",
                    $"{ColumnGeometry.LongSide(revitColumn)} / {ColumnGeometry.LongSide(etabsColumn)}",
                    longSideDelta > tolerance, CheckKind.Dimensions));
                resultFields.Add(new ResultField(ColumnResultKeys.ShortSide, "Short side R / E",
                    $"{ColumnGeometry.ShortSide(revitColumn)} / {ColumnGeometry.ShortSide(etabsColumn)}",
                    shortSideDelta > tolerance, CheckKind.Dimensions));
            }

            resultFields.Add(new ResultField(ColumnResultKeys.Note, "Note", note,
                state != ColumnCheckState.Matched));
        }
    }

    /// <summary>Field keys produced by the column check, so the viewer can name them.</summary>
    public static class ColumnResultKeys
    {
        public const string State = "Column.State";
        public const string RevitId = "Column.RevitId";
        public const string EtabsName = "Column.EtabsName";
        public const string RevitType = "Column.RevitType";
        public const string SizeSource = "Column.SizeSource";
        public const string RevitFamily = "Column.RevitFamily";
        public const string EtabsSection = "Column.EtabsSection";
        public const string TypeMark = "Column.TypeMark";
        public const string EtabsGroups = "Column.EtabsGroups";
        public const string GroupCheck = "Column.GroupCheck";
        public const string LocationDelta = "Column.LocationDelta";
        public const string OrientationDelta = "Column.OrientationDelta";
        public const string LongSide = "Column.LongSide";
        public const string ShortSide = "Column.ShortSide";
        public const string Diameter = "Column.Diameter";
        public const string Note = "Column.Note";
    }
}
