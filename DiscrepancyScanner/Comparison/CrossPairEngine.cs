using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A rectangular column read as the short wall it is in plan: a centre line down its long
    /// side and its short side for a thickness.
    ///
    /// This is what lets a column be weighed against a wall at all. A wall is a line and a
    /// thickness, and a 1200 by 250 column is exactly that, so once it is put that way the
    /// geometry the wall check already trusts - how far one line stands off another, how much
    /// of the two run alongside - answers the question unchanged.
    /// </summary>
    public sealed class ColumnAsWall : IWall
    {
        /// <param name="column">The column to read as a wall.</param>
        /// <param name="towardsDegrees">
        /// Which way the wall it is being weighed against runs. Only read for a column whose
        /// two sides are the same within the thickness buffer: a square column has no long
        /// side to run along, so it is laid along whichever of its axes is nearer the wall
        /// rather than one picked by a rounding difference.
        /// </param>
        public ColumnAsWall(IColumn column, double? towardsDegrees = null)
        {
            Column = column;

            MyPoint centre = column.PlanPoint ?? MyPoint.Zero;
            double longSide = ColumnGeometry.LongSide(column);
            double shortSide = ColumnGeometry.ShortSide(column);

            // The depth spans the local 3 axis, which is global X at no rotation, and the width
            // the local 2 axis - see ColumnGeometry.RectanglePoints.
            double along = column.Depth >= column.Width ? column.AngleDegrees : column.AngleDegrees + 90;

            double tolerance = PrefrencesData.getPrefrencesData().WallThicknessTolerance;
            if (towardsDegrees.HasValue && longSide - shortSide <= tolerance
                && ColumnGeometry.AngularDifference(along, towardsDegrees.Value) > 45)
            {
                along += 90;
            }

            double radians = along * Math.PI / 180d;
            double dx = Math.Cos(radians) * longSide / 2d;
            double dy = Math.Sin(radians) * longSide / 2d;

            PlanStart = new MyPoint(centre.X - dx, centre.Y - dy, centre.Z);
            PlanEnd = new MyPoint(centre.X + dx, centre.Y + dy, centre.Z);
            Thickness = shortSide;
            Length = longSide;
        }

        public IColumn Column { get; }

        public MyPoint PlanStart { get; }
        public MyPoint PlanEnd { get; }
        public string Story { get => Column.Story; }
        public double Thickness { get; }
        public string SectionName { get => Column.SectionName; }
        public string Identifier { get => Column.Identifier; }

        /// <summary>The long side, which is the length of the wall it stands for.</summary>
        public double Length { get; }

        /// <summary>
        /// True for a column there is any wall in at all. A round one has no line to lie along,
        /// and one with no readable section has nothing to measure.
        /// </summary>
        public static bool CanBeRead(IColumn column)
        {
            return column != null
                && column.Shape != SectionShape.Round
                && column.PlanPoint != null
                && column.Width > 0 && column.Depth > 0;
        }
    }

    /// <summary>
    /// One element drawn as a column in one model and as a wall in the other, paired across
    /// the two kinds and checked as walls are checked: is it on the same line, is it the same
    /// thickness, does it run as far.
    ///
    /// One ETABS element against every Revit element found on it. That is usually one, but a
    /// Revit wall cut in two where ETABS has a single long column is the same element drawn in
    /// two pieces, and reporting the second piece as a wall ETABS has not got would be wrong.
    /// The length is then weighed against all of them together.
    /// </summary>
    public sealed class CrossPair : IHasResultFields
    {
        private readonly List<IWall> revit = new List<IWall>();
        private readonly List<CheckOutcome> outcomes = new List<CheckOutcome>();
        private readonly List<ResultField> resultFields = new List<ResultField>();

        private double locationDelta;
        private double angleDelta;
        private double thicknessDelta;
        private double lengthDelta;
        private double revitLength;
        private bool locationMatched;
        private bool thicknessReadable;
        private bool thicknessMatched;
        private bool lengthMatched;
        private string note = "";
        private Dictionary<object, List<IWall>> etabsAlong;

        /// <summary>An ETABS column that Revit has drawn as a wall.</summary>
        public CrossPair(Column_e etabsColumn, Wall_R revitWall)
        {
            EtabsColumn = etabsColumn;
            revit.Add(revitWall);
            Check();
        }

        /// <summary>An ETABS wall panel that Revit has drawn as a column.</summary>
        public CrossPair(Wall_E etabsWall, Column_r revitColumn)
        {
            EtabsWall = etabsWall;
            revit.Add(new ColumnAsWall(revitColumn, WallGeometry.AngleDegrees(etabsWall.PlanStart, etabsWall.PlanEnd)));
            Check();
        }

        /// <summary>Set where the ETABS side is a column; null where it is a wall.</summary>
        public Column_e EtabsColumn { get; }

        /// <summary>Set where the ETABS side is a wall panel; null where it is a column.</summary>
        public Wall_E EtabsWall { get; }

        /// <summary>True where ETABS has a column and Revit a wall.</summary>
        public bool EtabsIsColumn { get => EtabsColumn != null; }

        /// <summary>The Revit walls standing on the ETABS column. Empty the other way round.</summary>
        public IEnumerable<Wall_R> RevitWalls { get => revit.OfType<Wall_R>(); }

        /// <summary>The Revit columns standing on the ETABS wall. Empty the other way round.</summary>
        public IEnumerable<Column_r> RevitColumns
        {
            get => revit.OfType<ColumnAsWall>().Select(column => (Column_r)column.Column);
        }

        /// <summary>The Revit element the pair is filed under: the first one found on it.</summary>
        public long RevitID
        {
            get
            {
                Wall_R wall = revit[0] as Wall_R;
                return wall != null ? wall.RevitID : ((Column_r)((ColumnAsWall)revit[0]).Column).RevitID;
            }
        }

        public IList<CheckOutcome> Outcomes { get => outcomes; }
        public IList<ResultField> ResultFields { get => resultFields; }
        public string Note { get => note; }

        public double LocationDelta { get => locationDelta; }
        public double ThicknessDelta { get => thicknessDelta; }
        public double LengthDelta { get => lengthDelta; }

        public string Story
        {
            get
            {
                string story = EtabsIsColumn ? EtabsColumn.Story : EtabsWall.Story;
                return string.IsNullOrEmpty(story) ? revit[0].Story : story;
            }
        }

        /// <summary>The ETABS element as a wall, which is what every measurement is taken off.</summary>
        private IWall EtabsLine
        {
            get { return EtabsIsColumn ? (IWall)new ColumnAsWall(EtabsColumn, RevitAngle) : EtabsWall; }
        }

        private double RevitAngle
        {
            get { return WallGeometry.AngleDegrees(revit[0].PlanStart, revit[0].PlanEnd); }
        }

        /// <summary>Another Revit element found standing on the same ETABS one.</summary>
        internal void Add(Wall_R revitWall)
        {
            revit.Add(revitWall);
            Check();
        }

        internal void Add(Column_r revitColumn)
        {
            revit.Add(new ColumnAsWall(revitColumn, WallGeometry.AngleDegrees(EtabsWall.PlanStart, EtabsWall.PlanEnd)));
            Check();
        }

        private void Check()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            IWall etabs = EtabsLine;
            note = "";

            // Off the line: the worst of the Revit pieces, the same way a wall is only as right
            // as its worst panel.
            locationDelta = revit.Max(piece => WallGeometry.Offset(etabs, piece));
            angleDelta = revit.Max(piece => WallGeometry.AngleBetween(etabs, piece));
            locationMatched = locationDelta <= pref.WallLocationTolerance;
            if (!locationMatched)
            {
                note += $"{Math.Round(locationDelta)}mm off the Revit {RevitWord}, ";
            }

            thicknessReadable = etabs.Thickness > 0 && revit.All(piece => piece.Thickness > 0);
            thicknessDelta = thicknessReadable ? revit.Max(piece => Math.Abs(piece.Thickness - etabs.Thickness)) : 0;
            thicknessMatched = thicknessReadable && thicknessDelta <= pref.WallThicknessTolerance;
            if (thicknessReadable && !thicknessMatched)
            {
                note += $"thickness {RevitThickness} vs {Math.Round(etabs.Thickness)}, ";
            }

            // The pieces added end to end. Two halves of one Revit wall do not lie along the
            // same stretch the way stacked ETABS panels do, so adding them up is the length.
            revitLength = revit.Sum(piece => WallGeometry.PlanLength(piece.PlanStart, piece.PlanEnd));

            // How much of them ETABS stands along, measured as the wall check measures it: every
            // ETABS element on the piece counted, not this one alone, and a stretch covered
            // twice counted once. A column meshed into three panels along its length is three
            // pairs, and no one of them is a third of a column short.
            double covered = revit.Sum(piece => WallGeometry.CoveredLength(piece, EtabsAlong(piece, etabs)));
            double shortfall = Math.Max(0, revitLength - covered);

            // And how far this ETABS element runs on past the Revit ones, which coverage of the
            // Revit pieces cannot see: a 1500 column on a 1200 wall covers all of the wall.
            double ownLength = WallGeometry.PlanLength(etabs.PlanStart, etabs.PlanEnd);
            double overrun = Math.Max(0, ownLength - WallGeometry.CoveredLength(etabs, revit));

            double etabsLength = covered + overrun;
            lengthDelta = Math.Max(shortfall, overrun);
            lengthMatched = lengthDelta <= pref.WallLengthTolerance;
            if (!lengthMatched)
            {
                note += $"{Math.Round(etabsLength)} long in ETABS, {Math.Round(revitLength)} in Revit, ";
            }

            note = note.Length == 0 ? "OK" : note.TrimEnd(',', ' ');

            CollectOutcomes(etabs, etabsLength);
            BuildResultFields(etabs, etabsLength);
        }

        /// <summary>
        /// Every ETABS element standing on each Revit one, told to the pair once every pair is
        /// known - see <see cref="CrossPairEngine"/>. The checks are run again with it in hand.
        /// </summary>
        internal void SetEtabsAlong(Dictionary<object, List<IWall>> along)
        {
            etabsAlong = along;
            Check();
        }

        /// <summary>The Revit elements in the pair: a Wall_R, or the Column_r a piece was read from.</summary>
        internal IEnumerable<object> RevitElements { get => revit.Select(ElementOf); }

        /// <summary>The ETABS element as the wall every measurement is taken off.</summary>
        internal IWall EtabsAsWall { get => EtabsLine; }

        internal static object ElementOf(IWall piece)
        {
            ColumnAsWall column = piece as ColumnAsWall;
            return column != null ? column.Column : (object)piece;
        }

        /// <summary>The ETABS elements standing on one Revit piece; this one alone until told.</summary>
        private IEnumerable<IWall> EtabsAlong(IWall piece, IWall etabs)
        {
            List<IWall> along;
            if (etabsAlong != null && etabsAlong.TryGetValue(ElementOf(piece), out along))
            {
                return along;
            }
            return new[] { etabs };
        }

        private string RevitWord { get => EtabsIsColumn ? "wall" : "column"; }

        private string RevitThickness
        {
            get { return string.Join(", ", revit.Select(piece => Math.Round(piece.Thickness).ToString()).Distinct()); }
        }

        /// <summary>
        /// Published under the checks of the tab the ETABS element is drawn on, so the chips
        /// there show and hide it with everything else on that tab: an ETABS column is drawn
        /// with the columns and answers to where it stands and how big it is, an ETABS wall is
        /// drawn with the walls and answers to the three wall checks.
        /// </summary>
        private void CollectOutcomes(IWall etabs, double etabsLength)
        {
            outcomes.Clear();

            string onLine = locationMatched
                ? $"on the Revit {RevitWord}'s line, {Math.Round(locationDelta)}mm off"
                : $"{Math.Round(locationDelta)}mm off the Revit {RevitWord}'s line";

            if (EtabsIsColumn)
            {
                outcomes.Add(new CheckOutcome(CheckKind.Location,
                    locationMatched ? ResultLevel.Passed : ResultLevel.Failed,
                    onLine + " (modelled as a wall in Revit)",
                    etabs: $"{Math.Round(locationDelta)} mm off", revit: "on the line"));

                bool sizeMatched = thicknessMatched && lengthMatched;
                outcomes.Add(new CheckOutcome(CheckKind.Dimensions,
                    sizeMatched ? ResultLevel.Passed : ResultLevel.Failed,
                    $"{Math.Round(etabsLength)} x {Math.Round(etabs.Thickness)} in ETABS against a "
                    + $"{Math.Round(revitLength)} long, {RevitThickness} thick wall in Revit",
                    etabs: $"{Math.Round(etabsLength)} x {Math.Round(etabs.Thickness)}",
                    revit: $"{Math.Round(revitLength)} x {RevitThickness}"));
            }
            else
            {
                outcomes.Add(new CheckOutcome(CheckKind.WallLocation,
                    locationMatched ? ResultLevel.Passed : ResultLevel.Failed,
                    onLine + " (modelled as a column in Revit)",
                    etabs: $"{Math.Round(locationDelta)} mm off", revit: "on the line"));

                outcomes.Add(new CheckOutcome(CheckKind.WallLength,
                    lengthMatched ? ResultLevel.Passed : ResultLevel.Review,
                    $"{Math.Round(etabsLength)}mm of ETABS wall against a {Math.Round(revitLength)}mm Revit column",
                    etabs: $"{Math.Round(etabsLength)} mm", revit: $"{Math.Round(revitLength)} mm"));

                outcomes.Add(new CheckOutcome(CheckKind.WallThickness,
                    !thicknessReadable ? ResultLevel.Review
                        : thicknessMatched ? ResultLevel.Passed : ResultLevel.Failed,
                    thicknessReadable
                        ? $"{RevitThickness} in Revit vs {Math.Round(etabs.Thickness)} in ETABS"
                        : "one of the two gives no thickness",
                    etabs: thicknessReadable ? $"{Math.Round(etabs.Thickness)}" : "no section",
                    revit: thicknessReadable ? RevitThickness : "no section"));
            }

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        private void BuildResultFields(IWall etabs, double etabsLength)
        {
            resultFields.Clear();
            bool failed = note != "OK";

            if (EtabsIsColumn)
            {
                resultFields.Add(new ResultField(ColumnResultKeys.State, "Check state",
                    failed ? "Mismatched (a wall in Revit)" : "Matched (a wall in Revit)", failed));
                resultFields.Add(new ResultField(ColumnResultKeys.RevitId, "Revit id",
                    string.Join(", ", RevitWalls.Select(wall => wall.RevitID))));
                resultFields.Add(new ResultField(ColumnResultKeys.EtabsName, "ETABS name",
                    EtabsColumn.EtabsUniqueName));
                resultFields.Add(new ResultField(ColumnResultKeys.RevitType, "Revit wall type",
                    string.Join(", ", RevitWalls.Select(wall => wall.TypeName).Distinct())));
                resultFields.Add(new ResultField(ColumnResultKeys.EtabsSection, "ETABS section",
                    EtabsColumn.SectionName));
                resultFields.Add(ResultField.Number(ColumnResultKeys.LocationDelta,
                    "Off the wall line (mm)", locationDelta, 0, !locationMatched, CheckKind.Location));
                resultFields.Add(new ResultField(ColumnResultKeys.LongSide, "Long side / wall length E / R",
                    $"{Math.Round(etabsLength)} / {Math.Round(revitLength)}", !lengthMatched, CheckKind.Dimensions));
                resultFields.Add(new ResultField(ColumnResultKeys.ShortSide, "Short side / wall thickness E / R",
                    $"{Math.Round(etabs.Thickness)} / {RevitThickness}", thicknessReadable && !thicknessMatched,
                    CheckKind.Dimensions));
                resultFields.Add(new ResultField(ColumnResultKeys.Note, "Note", note, failed));
                return;
            }

            resultFields.Add(new ResultField(WallResultKeys.State, "Check state",
                failed ? "Mismatched (a column in Revit)" : "Matched (a column in Revit)", failed));
            resultFields.Add(new ResultField(WallResultKeys.RevitId, "Revit id",
                string.Join(", ", RevitColumns.Select(column => column.RevitID))));
            resultFields.Add(new ResultField(WallResultKeys.EtabsName, "ETABS name", EtabsWall.UniqueName));
            resultFields.Add(new ResultField(WallResultKeys.RevitType, "Revit column type",
                string.Join(", ", RevitColumns.Select(column => column.TypeName).Distinct())));
            resultFields.Add(new ResultField(WallResultKeys.EtabsProperty, "ETABS property", EtabsWall.PropertyName));
            resultFields.Add(new ResultField(WallResultKeys.Thickness, "Thickness R / E",
                $"{RevitThickness} / {Math.Round(etabs.Thickness)}",
                thicknessReadable && !thicknessMatched, CheckKind.WallThickness));
            resultFields.Add(ResultField.Number(WallResultKeys.LocationDelta,
                "Line offset (mm)", locationDelta, 0, !locationMatched, CheckKind.WallLocation));
            resultFields.Add(ResultField.Number(WallResultKeys.AngleDelta,
                "Angle delta (deg)", angleDelta, 1, !locationMatched, CheckKind.WallLocation));
            resultFields.Add(new ResultField(WallResultKeys.Coverage, "ETABS length / Revit length",
                $"{Math.Round(etabsLength)} / {Math.Round(revitLength)}", !lengthMatched, CheckKind.WallLength));
            resultFields.Add(new ResultField(WallResultKeys.Note, "Note", note, failed));
        }
    }

    /// <summary>
    /// Pairs what is left unpaired after the column and the wall checks across the two kinds:
    /// an ETABS column with a Revit wall standing on it, and an ETABS wall panel with a Revit
    /// column on it.
    ///
    /// The two models are allowed to disagree about what a short length of wall is. ETABS is
    /// often given a column where the drawing has a stub of wall, because that is how the
    /// designer wanted it to work, and the Revit model is just as often given a wall where
    /// ETABS has a blade column. Neither is a missing element, and reporting both halves as
    /// unpaired buries the elements that really are missing under ones that are not.
    ///
    /// Only the leftovers take part. An element the column or the wall check paired already
    /// has a counterpart of its own kind, and one somebody said by hand should pair with
    /// nothing is left that way.
    ///
    /// Paired the way walls are: same story, running the same way, within the wall search
    /// radius of each other's line and alongside each other for long enough. A Revit element
    /// may be claimed by more than one ETABS element - stacked ETABS panels all stand on the
    /// one Revit column - and a Revit element nothing claimed is then offered to the ETABS
    /// element it stands on, which is what keeps a Revit wall drawn in two pieces from
    /// leaving one of them unpaired.
    /// </summary>
    public class CrossPairEngine
    {
        private readonly List<CrossPair> pairs = new List<CrossPair>();

        public List<CrossPair> Pairs { get => pairs; }

        /// <summary>
        /// Pairs across the kinds and takes everything it pairs out of the four lists handed
        /// in, so what is left in them is what is really unpaired.
        /// </summary>
        /// <param name="heldUnpaired">
        /// ETABS elements somebody said by hand should pair with nothing, by unique name. Left
        /// out, so that decision stands.
        /// </param>
        public void StartComparison(List<Column_e> unmatchedEtabsColumns, List<Column_r> unmatchedRevitColumns,
                                    List<Wall_E> unmatchedEtabsWalls, List<Wall_R> unmatchedRevitWalls,
                                    ICollection<string> heldUnpaired = null)
        {
            pairs.Clear();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            Func<string, bool> held = name => heldUnpaired != null && heldUnpaired.Contains(name);

            // ETABS columns against Revit walls.
            List<Column_e> etabsColumns = unmatchedEtabsColumns
                .Where(column => ColumnAsWall.CanBeRead(column) && !held(column.EtabsUniqueName)).ToList();
            List<Wall_R> revitWalls = unmatchedRevitWalls.Where(wall => wall != null && wall.Length > 0).ToList();

            Dictionary<Column_e, CrossPair> byColumn = new Dictionary<Column_e, CrossPair>();
            HashSet<Wall_R> claimedWalls = new HashSet<Wall_R>();

            foreach (Column_e column in etabsColumns)
            {
                Wall_R wall = Best(revitWalls, candidate => new ColumnAsWall(column, AngleOf(candidate)),
                                   candidate => candidate, column.Story, pref);
                if (wall == null)
                {
                    continue;
                }
                claimedWalls.Add(wall);
                byColumn[column] = new CrossPair(column, wall);
            }

            foreach (Wall_R wall in revitWalls.Where(wall => !claimedWalls.Contains(wall)))
            {
                double angle = AngleOf(wall);
                Column_e column = Best(etabsColumns, candidate => wall,
                                       candidate => new ColumnAsWall(candidate, angle), wall.Story, pref);
                if (column == null)
                {
                    continue;
                }
                claimedWalls.Add(wall);

                CrossPair pair;
                if (byColumn.TryGetValue(column, out pair))
                {
                    pair.Add(wall);
                }
                else
                {
                    byColumn[column] = new CrossPair(column, wall);
                }
            }

            // ETABS walls against Revit columns.
            List<Wall_E> etabsWalls = unmatchedEtabsWalls
                .Where(wall => wall != null && wall.HasCentreLine && wall.Length > 0 && !held(wall.UniqueName))
                .ToList();
            List<Column_r> revitColumns = unmatchedRevitColumns.Where(ColumnAsWall.CanBeRead).ToList();

            Dictionary<Wall_E, CrossPair> byWall = new Dictionary<Wall_E, CrossPair>();
            HashSet<Column_r> claimedColumns = new HashSet<Column_r>();

            foreach (Wall_E wall in etabsWalls)
            {
                double angle = AngleOf(wall);
                Column_r column = Best(revitColumns, candidate => wall,
                                       candidate => new ColumnAsWall(candidate, angle), wall.Story, pref);
                if (column == null)
                {
                    continue;
                }
                claimedColumns.Add(column);
                byWall[wall] = new CrossPair(wall, column);
            }

            foreach (Column_r column in revitColumns.Where(column => !claimedColumns.Contains(column)))
            {
                Wall_E wall = Best(etabsWalls, candidate => new ColumnAsWall(column, AngleOf(candidate)),
                                   candidate => candidate, column.Story, pref);
                if (wall == null)
                {
                    continue;
                }
                claimedColumns.Add(column);

                CrossPair pair;
                if (byWall.TryGetValue(wall, out pair))
                {
                    pair.Add(column);
                }
                else
                {
                    byWall[wall] = new CrossPair(wall, column);
                }
            }

            // In the order the ETABS elements came in, so the plan is drawn the same way each run.
            pairs.AddRange(etabsColumns.Where(byColumn.ContainsKey).Select(column => byColumn[column]));
            pairs.AddRange(etabsWalls.Where(byWall.ContainsKey).Select(wall => byWall[wall]));

            MeasureCoverage();

            unmatchedEtabsColumns.RemoveAll(byColumn.ContainsKey);
            unmatchedRevitWalls.RemoveAll(claimedWalls.Contains);
            unmatchedEtabsWalls.RemoveAll(byWall.ContainsKey);
            unmatchedRevitColumns.RemoveAll(claimedColumns.Contains);
        }

        /// <summary>
        /// Tells every pair which ETABS elements stand on each of its Revit ones, so the length
        /// is weighed the way the wall check weighs it: a Revit column with three ETABS panels
        /// along it is covered by all three, not by whichever one a pair happens to hold.
        /// </summary>
        private void MeasureCoverage()
        {
            Dictionary<object, List<IWall>> along = new Dictionary<object, List<IWall>>();

            foreach (CrossPair pair in pairs)
            {
                IWall etabs = pair.EtabsAsWall;
                foreach (object element in pair.RevitElements)
                {
                    List<IWall> onElement;
                    if (!along.TryGetValue(element, out onElement))
                    {
                        onElement = new List<IWall>();
                        along[element] = onElement;
                    }
                    onElement.Add(etabs);
                }
            }

            foreach (CrossPair pair in pairs)
            {
                pair.SetEtabsAlong(along);
            }
        }

        private static double AngleOf(IPlanLine line)
        {
            return WallGeometry.AngleDegrees(line.PlanStart, line.PlanEnd);
        }

        /// <summary>
        /// The best candidate for one element, judged as the wall check judges two walls: the
        /// same way, near the line, alongside for long enough; then one inside the line buffer
        /// over one outside it, the longer run alongside among those inside, and the nearer
        /// among those outside.
        ///
        /// The element and each candidate are both handed over as walls, built per candidate
        /// because a square column is laid along whichever of its axes the other one runs.
        /// </summary>
        private static T Best<T>(IEnumerable<T> candidates, Func<T, IWall> measuredFor, Func<T, IWall> candidateAsWall,
                                 string story, PrefrencesData pref)
            where T : class
        {
            T best = null;
            double bestOffset = double.MaxValue;
            double bestOverlap = 0;
            bool bestInside = false;

            foreach (T candidate in candidates)
            {
                IWall measured = measuredFor(candidate);
                IWall other = candidateAsWall(candidate);

                if (!SameStory(story, other.Story))
                {
                    continue;
                }

                if (WallGeometry.AngleBetween(measured, other) > pref.WallAngleTolerance)
                {
                    continue;
                }

                double offset = WallGeometry.Offset(measured, other);
                if (offset > pref.WallSearchRadius)
                {
                    continue;
                }

                double length = WallGeometry.PlanLength(measured.PlanStart, measured.PlanEnd);
                double overlap = WallGeometry.OverlapLength(measured, other);
                if (overlap < Math.Min(pref.WallMinimumOverlap, length / 2d))
                {
                    continue;
                }

                bool inside = offset <= pref.WallLocationTolerance;
                if (best != null)
                {
                    bool better = inside != bestInside ? inside
                                : inside ? overlap > bestOverlap
                                : offset < bestOffset;
                    if (!better)
                    {
                        continue;
                    }
                }

                best = candidate;
                bestOffset = offset;
                bestOverlap = overlap;
                bestInside = inside;
            }

            return best;
        }

        /// <summary>An unassigned story is in reach of every one, as it is for the walls.</summary>
        private static bool SameStory(string a, string b)
        {
            return string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)
                || string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
