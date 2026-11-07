using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The SIDL_C area check: each light weight fill floor in Revit, weighed, against the SIDL_C
    /// area load ETABS carries on the slab under it.
    ///
    /// The fill is usually a ramp, and ETABS cannot carry a ramp - only uniform loads on areas.
    /// So the engineer averages it: over the whole fill, or in two halves, or in five strips,
    /// each strip carrying the average of the fill over it. ETABS's values are taken exactly as
    /// they are, never averaged: each distinct value under the fill is a band, and each band is
    /// checked against what the fill weighs over that band's own footprint -
    ///
    ///     ETABS   the value on the band, as it is
    ///     Revit   the fill's average depth over the band x LWC density
    ///             + topping x (topping density - LWC density)
    ///
    /// - so a ramp averaged whole, in halves or in five strips passes, and one with its bands
    /// the wrong way round fails. The depth is read off the fill's own top and bottom faces.
    ///
    /// The footprint is read on a grid of points rather than cut against the shells, which
    /// needs no geometry kernel, does not care how the shells are drawn, and adds up shells
    /// drawn one over another the way the slab load check does.
    /// </summary>
    public sealed class SidlCAreaCompareEngine
    {
        /// <summary>The most points a footprint is read at. Plenty for a fill of any size.</summary>
        public const int MaximumSamples = 6000;

        /// <summary>The closest the grid is drawn, in mm: finer says nothing more about a load.</summary>
        public const double SmallestSpacing = 25;

        /// <summary>
        /// How much of the fill has to stand over an ETABS slab for the average to be taken over
        /// the slab alone. Less, and it is taken over the whole fill, the bare part as nothing.
        /// </summary>
        public const double LeastCovered = 0.5;

        /// <summary>
        /// How much of the fill's area the SIDL_C counted towards it has to reach outside it
        /// before its load is read as put on a different area. Also where the card starts saying so.
        /// </summary>
        public const double OutsideShare = 0.2;

        private readonly List<SidlCAreaResult> results = new List<SidlCAreaResult>();
        private readonly List<SidlCAreaUnclaimed> unclaimed = new List<SidlCAreaUnclaimed>();
        private readonly List<string> issues = new List<string>();
        private HashSet<string> storiesToCheck;

        /// <summary>The ETABS load patterns read as SIDL_C.</summary>
        public LineLoadPatterns Patterns { get; set; } = LineLoadPatterns.Exactly("SIDL_C, SIDL_LWC");

        /// <summary>How far ETABS may carry less, and more, than the fill weighs.</summary>
        public LoadBand Band { get; set; } = new LoadBand(0.5, 1);

        public double LightweightDensity { get; set; } = 10;

        public double ToppingDensity { get; set; } = 25;

        /// <summary>The topping, in mm: the top of the fill, at the topping density.</summary>
        public double ToppingThickness { get; set; } = 100;

        /// <summary>
        /// What the fill's weight is rounded up to, in kN/m2, before it is compared - the whole
        /// fill's and each band's. Nought leaves it as it is.
        /// </summary>
        public double RoundUpTo { get; set; }

        /// <summary>
        /// How far from a fill with no SIDL_C under it an ETABS area carrying SIDL_C may be, in
        /// mm, and still be read as that fill's load drawn in the wrong place.
        /// </summary>
        public double MisplacedDistance { get; set; } = 2000;

        /// <summary>Pieces smaller than this, in mm2, or narrower than the next, are slivers and not drawn.</summary>
        public double SmallestPiece { get; set; } = 10000;

        public double NarrowestPiece { get; set; } = 20;

        /// <summary>Pieces smaller than this, in mm2, are drawn but not counted.</summary>
        public double SmallestIssue { get; set; } = 1e6;

        public IList<SidlCAreaResult> Results { get { return results; } }

        /// <summary>ETABS areas carrying SIDL_C with no fill floor over any of them.</summary>
        public IList<SidlCAreaUnclaimed> Unclaimed { get { return unclaimed; } }

        public IList<string> Issues { get { return issues; } }

        /// <summary>The stories the check covers. Unset, every story.</summary>
        public void SetStoriesToCheck(IEnumerable<string> stories)
        {
            storiesToCheck = stories == null
                ? null
                : new HashSet<string>(stories.Where(name => !string.IsNullOrEmpty(name)),
                                      StringComparer.OrdinalIgnoreCase);
        }

        public void StartComparison(IEnumerable<SidlCAreaSource_R> sources, IEnumerable<Slab_E> shells)
        {
            results.Clear();
            unclaimed.Clear();
            issues.Clear();

            List<SidlCAreaSource_R> fills = (sources ?? Enumerable.Empty<SidlCAreaSource_R>())
                .Where(fill => fill != null && fill.Plan.Count > 0 && InScope(fill.Story))
                .ToList();

            // Openings carry nothing and are nothing to stand on.
            Dictionary<string, List<Slab_E>> byStory = (shells ?? Enumerable.Empty<Slab_E>())
                .Where(shell => shell != null && !shell.IsOpening && shell.Plan != null && InScope(shell.Story))
                .GroupBy(shell => shell.Story ?? "", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (SidlCAreaSource_R fill in fills)
            {
                List<Slab_E> onStory;
                byStory.TryGetValue(fill.Story ?? "", out onStory);
                results.Add(Weigh(fill, onStory ?? new List<Slab_E>()));
            }

            List<Slab_E> bare = AddUpTotals(byStory);
            FindMisplaced(bare);
            unclaimed.AddRange(bare.Select(shell => new SidlCAreaUnclaimed(shell, SidlCOn(shell))));

            CutIntoPieces();
        }

        /// <summary>
        /// Each story's fills and the SIDL_C areas counted towards them, cut along each other's
        /// edges. A story that will not cut is drawn a fill at a time, as before, and says so.
        /// </summary>
        private void CutIntoPieces()
        {
            foreach (IGrouping<string, SidlCAreaResult> story in results.GroupBy(result => result.Story ?? "",
                                                                                 StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    foreach (IGrouping<SidlCAreaResult, SidlCAreaPiece> fill in
                             SidlCAreaCutter.Cut(story.ToList(), SidlCOn, SmallestPiece, NarrowestPiece, SmallestIssue)
                                            .GroupBy(piece => piece.Fill))
                    {
                        foreach (SidlCAreaPiece piece in fill)
                        {
                            fill.Key.Pieces.Add(piece);
                        }
                    }
                }
                catch (Exception e)
                {
                    foreach (SidlCAreaResult result in story)
                    {
                        result.Pieces.Clear();
                    }
                    issues.Add($"The SIDL_C area plan on {story.Key} could not be cut into pieces, so each fill is "
                               + $"drawn whole - {e.Message}");
                }
            }
        }

        private bool InScope(string story)
        {
            return storiesToCheck == null || storiesToCheck.Contains(story ?? "");
        }

        /// <summary>The SIDL_C an area carries, in kN/m2, added up over the patterns read.</summary>
        public double SidlCOn(Slab_E shell)
        {
            if (shell == null || shell.IsOpening || shell.Loads == null)
            {
                return 0;
            }

            return shell.Loads.Where(load => load != null && load.IsGravity && Patterns.Matches(load.Pattern))
                              .Sum(load => load.Value);
        }

        #region one fill

        private SidlCAreaResult Weigh(SidlCAreaSource_R fill, List<Slab_E> onStory)
        {
            SidlCAreaResult result = new SidlCAreaResult(fill)
            {
                Expected = fill.PerSquareMetre(LightweightDensity, ToppingDensity, ToppingThickness, RoundUpTo),
                How = fill.How(LightweightDensity, ToppingDensity, ToppingThickness, RoundUpTo)
            };

            PlanBounds bounds = BoundsOf(fill.Plan);
            List<Slab_E> near = onStory.Where(shell => shell.Plan.Bounds.IntersectsWith(bounds)).ToList();
            Dictionary<Slab_E, double> loadOf = near.ToDictionary(shell => shell, SidlCOn);

            List<MyPoint> samples = Samples(fill.Plan, bounds);
            if (samples.Count == 0)
            {
                result.State = SidlCAreaState.NoEtabsSlab;
                result.Note = "the fill has no area to read";
                return result;
            }

            // Every point read for what ETABS carries there, exactly, and how deep the fill is.
            List<Reading> readings = new List<Reading>(samples.Count);
            Dictionary<Slab_E, int> hits = new Dictionary<Slab_E, int>();
            HashSet<Slab_E> carrying = new HashSet<Slab_E>();
            int covered = 0;

            foreach (MyPoint point in samples)
            {
                bool slabHere = false;
                double here = 0;

                foreach (Slab_E shell in near)
                {
                    if (!shell.Plan.Bounds.Contains(point.X, point.Y) || !shell.Plan.Contains(point.X, point.Y))
                    {
                        continue;
                    }

                    slabHere = true;
                    double load = loadOf[shell];
                    if (load != 0)
                    {
                        here += load;
                        carrying.Add(shell);

                        int hit;
                        hits.TryGetValue(shell, out hit);
                        hits[shell] = hit + 1;
                    }
                }

                if (slabHere)
                {
                    covered++;
                }

                readings.Add(new Reading
                {
                    Covered = slabHere,
                    Load = Math.Round(here, 2),
                    Depth = fill.DepthAt(point.X, point.Y) ?? fill.AverageThickness
                });
            }

            double area = fill.Area;
            result.CoveredShare = (double)covered / samples.Count;
            result.HasDepthByPlace = fill.HasFaces;

            // Read over the part of the fill ETABS has slab under: a fill running on past the
            // slab edge - over a wall, into a core - has nothing there to load. Where most of it
            // has nothing under it, the load was most likely never put in, and the whole fill is
            // read, the bare part as carrying nothing.
            result.AveragedOverWholeFill = result.CoveredShare < LeastCovered;
            List<Reading> read = result.AveragedOverWholeFill
                ? readings
                : readings.Where(reading => reading.Covered).ToList();

            int loaded = read.Count(reading => reading.Load != 0);
            result.LoadedShare = (double)loaded / read.Count;
            result.Actual = read.Average(reading => reading.Load);
            foreach (KeyValuePair<Slab_E, int> hit in hits)
            {
                result.Overlaps[hit.Key] = area * hit.Value / samples.Count;
            }
            result.Shells = carrying.Select(shell => shell.UniqueName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

            // One band per value ETABS carries, exactly as it carries it, and against it what
            // the fill weighs over that band's own footprint - its depth there, not the whole
            // fill's. A ramp averaged in halves is two bands, each against its own half.
            result.Bands = read.GroupBy(reading => reading.Load)
                               .Select(group =>
                               {
                                   double depth = group.Average(reading => reading.Depth);
                                   double weighs = SidlCAreaSource_R.WeightOf(depth, LightweightDensity,
                                                                               ToppingDensity, ToppingThickness,
                                                                               RoundUpTo);
                                   return new SidlCAreaBand(group.Key, area * group.Count() / samples.Count,
                                                            (double)group.Count() / read.Count, weighs, depth,
                                                            Band.Agrees(weighs, group.Key));
                               })
                               .OrderByDescending(band => band.Area)
                               .ThenByDescending(band => band.Value)
                               .ToList();

            if (loaded == 0)
            {
                result.State = SidlCAreaState.NoEtabsLoad;
                return result;
            }

            result.State = result.Bands.All(band => band.Agrees)
                ? SidlCAreaState.Matched
                : SidlCAreaState.ValueMismatch;

            return result;
        }

        /// <summary>One point of a fill: what ETABS carries there and how deep the fill is.</summary>
        private struct Reading
        {
            public bool Covered;
            public double Load;
            public double Depth;
        }

        /// <summary>
        /// Points on an even grid over the footprint, one per cell of equal area, kept where
        /// they fall inside it. Fine enough that the narrowest strip an engineer would average
        /// on its own has rows of points across it.
        /// </summary>
        public static List<MyPoint> Samples(IList<PlanPolygon> plan, PlanBounds bounds)
        {
            List<MyPoint> inside = new List<MyPoint>();
            if (plan == null || plan.Count == 0 || bounds == null || bounds.IsEmpty)
            {
                return inside;
            }

            double area = plan.Sum(part => part.Area);
            if (area <= 0)
            {
                return inside;
            }

            double spacing = Math.Max(SmallestSpacing, Math.Sqrt(area / MaximumSamples));

            // A long thin fill can lose most of its points to the grid's phase; drawn closer
            // until it holds a fair number of them, or as close as is worth drawing.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                inside.Clear();

                // The cells fitted to the bounds exactly, so the points sit evenly from one
                // edge to the other: a grid stopping short of the far edge read a ramp's depth
                // a little thin, and a load on the edge of the tolerance the wrong side of it.
                // An even count, so a band boundary down the middle - a ramp in halves - falls
                // between two columns rather than on one.
                int columns = Math.Max(1, (int)Math.Ceiling(bounds.Width / spacing));
                int rows = Math.Max(1, (int)Math.Ceiling(bounds.Height / spacing));
                columns += columns % 2;
                rows += rows % 2;
                double stepX = bounds.Width / columns;
                double stepY = bounds.Height / rows;

                for (int i = 0; i < columns; i++)
                {
                    double x = bounds.MinX + (i + 0.5) * stepX;
                    for (int j = 0; j < rows; j++)
                    {
                        double y = bounds.MinY + (j + 0.5) * stepY;
                        if (plan.Any(part => part.Bounds.Contains(x, y) && part.Contains(x, y)))
                        {
                            inside.Add(new MyPoint(x, y, 0));
                        }
                    }
                }

                if (inside.Count >= 200 || spacing <= SmallestSpacing)
                {
                    break;
                }

                spacing = Math.Max(SmallestSpacing, spacing / 2);
            }

            return inside;
        }

        private static PlanBounds BoundsOf(IList<PlanPolygon> plan)
        {
            PlanBounds bounds = new PlanBounds();
            foreach (PlanPolygon part in plan)
            {
                bounds.Include(part.Bounds);
            }
            return bounds;
        }

        #endregion

        #region totals, and SIDL_C with no fill

        /// <summary>
        /// Every area carrying SIDL_C, shared out among the fills standing on it, and the ones
        /// with no fill over any of it handed back.
        ///
        /// Over any of it, not all of it: an engineer who averaged a ramp over the whole panel
        /// it sits on has drawn the load wider than the fill, and that is the ramp's load, not a
        /// second one. Its whole force is counted towards the fill - shared, where two fills
        /// stand on one panel, by how much of each stands on it - so a fill whose load was
        /// spread thinner over a bigger area still adds up to the same total.
        /// </summary>
        private List<Slab_E> AddUpTotals(Dictionary<string, List<Slab_E>> byStory)
        {
            Dictionary<Slab_E, List<SidlCAreaResult>> standingOn = new Dictionary<Slab_E, List<SidlCAreaResult>>();
            foreach (SidlCAreaResult result in results)
            {
                foreach (Slab_E shell in result.Overlaps.Keys)
                {
                    List<SidlCAreaResult> fills;
                    if (!standingOn.TryGetValue(shell, out fills))
                    {
                        standingOn[shell] = fills = new List<SidlCAreaResult>();
                    }
                    fills.Add(result);
                }
            }

            List<Slab_E> bare = new List<Slab_E>();
            foreach (Slab_E shell in byStory.Values.SelectMany(onStory => onStory))
            {
                double load = SidlCOn(shell);
                if (load == 0)
                {
                    continue;
                }

                List<SidlCAreaResult> fills;
                if (!standingOn.TryGetValue(shell, out fills))
                {
                    bare.Add(shell);
                    continue;
                }

                double overlapping = fills.Sum(fill => fill.Overlaps[shell]);
                foreach (SidlCAreaResult fill in fills)
                {
                    double share = overlapping > 0 ? fill.Overlaps[shell] / overlapping : 1d / fills.Count;
                    fill.EtabsLoadedArea += shell.Plan.Area * share;
                    fill.EtabsForce += load * shell.Plan.Area / 1e6 * share;
                }
            }

            foreach (SidlCAreaResult result in results)
            {
                JudgeOnTotal(result);
            }

            return bare;
        }

        /// <summary>
        /// A fill whose bands are wrong because its load was put on a bigger or a different area
        /// - a good part of the SIDL_C counted towards it lies outside it - but whose load is all
        /// there, is the same total put on differently: up for review, not failed. Bands wrong
        /// with the load all over the fill - the wrong way round, say - stay failed. The total's
        /// tolerance is the band's, over the fill's area.
        /// </summary>
        private void JudgeOnTotal(SidlCAreaResult result)
        {
            if (result.State != SidlCAreaState.ValueMismatch
                || result.LoadedOutside <= OutsideShare * result.Source.Area)
            {
                return;
            }

            double area = result.Source.Area / 1e6;
            LoadBand onTotal = new LoadBand(Band.Below * area, Band.Above * area);
            if (onTotal.Agrees(result.RevitForce, result.EtabsForce))
            {
                result.State = SidlCAreaState.EquivalentTotal;
            }
        }

        /// <summary>
        /// A fill with no SIDL_C under it, and SIDL_C on an area near it with no fill over it, is
        /// most likely one load drawn in the wrong place rather than two findings. Each such
        /// area goes to the nearest such fill, and is no longer reported on its own.
        /// </summary>
        private void FindMisplaced(List<Slab_E> bare)
        {
            List<SidlCAreaResult> unloaded = results.Where(result => result.State == SidlCAreaState.NoEtabsLoad).ToList();
            if (unloaded.Count == 0 || bare.Count == 0 || MisplacedDistance <= 0)
            {
                return;
            }

            foreach (Slab_E shell in bare.ToList())
            {
                SidlCAreaResult nearest = null;
                double nearestGap = double.MaxValue;

                foreach (SidlCAreaResult result in unloaded)
                {
                    if (!string.Equals(result.Story, shell.Story, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    double gap = Gap(shell.Plan, result.Source.Plan);
                    if (gap <= MisplacedDistance && gap < nearestGap)
                    {
                        nearest = result;
                        nearestGap = gap;
                    }
                }

                if (nearest == null)
                {
                    continue;
                }

                bare.Remove(shell);
                nearest.Misplaced.Add(shell.UniqueName);
                nearest.MisplacedShells.Add(shell);
                nearest.MisplacedGap = Math.Max(nearest.MisplacedGap, nearestGap);
                nearest.EtabsLoadedArea += shell.Plan.Area;
                nearest.EtabsForce += SidlCOn(shell) * shell.Plan.Area / 1e6;
            }

            foreach (SidlCAreaResult result in unloaded.Where(result => result.Misplaced.Count > 0))
            {
                result.State = SidlCAreaState.Misplaced;
            }
        }

        /// <summary>
        /// The gap between an area and a fill, in mm, read corner to region both ways: nought
        /// where they touch. Near enough for deciding whether two things are within a couple of
        /// metres of each other.
        /// </summary>
        private static double Gap(PlanPolygon shell, IList<PlanPolygon> fill)
        {
            double gap = double.MaxValue;
            foreach (PlanPolygon part in fill)
            {
                foreach (MyPoint corner in shell.Outer.Points)
                {
                    gap = Math.Min(gap, part.DistanceTo(corner.X, corner.Y));
                }
                foreach (MyPoint corner in part.Outer.Points)
                {
                    gap = Math.Min(gap, shell.DistanceTo(corner.X, corner.Y));
                }
            }
            return gap;
        }

        #endregion
    }

    public enum SidlCAreaState
    {
        Matched,
        ValueMismatch,

        /// <summary>No SIDL_C anywhere under the fill: the load is not in ETABS.</summary>
        NoEtabsLoad,

        /// <summary>The fill has no plan area to read at all.</summary>
        NoEtabsSlab,

        /// <summary>
        /// The average under the fill is wrong, but the total force on the areas it stands on
        /// is right: the load put on a bigger, or a different, area. Up for review.
        /// </summary>
        EquivalentTotal,

        /// <summary>
        /// No SIDL_C under the fill, and SIDL_C on an area near it with no fill over it: the
        /// load drawn in the wrong place.
        /// </summary>
        Misplaced
    }

    /// <summary>
    /// One load the engineer put on part of the fill, and how much of the fill it covers: one
    /// band for a fill averaged whole, two for one averaged in halves, and so on.
    /// </summary>
    public sealed class SidlCAreaBand
    {
        public SidlCAreaBand(double value, double areaMM2, double share)
            : this(value, areaMM2, share, 0, 0, false)
        {
        }

        public SidlCAreaBand(double value, double areaMM2, double share, double weighs, double depthMM, bool agrees)
        {
            Value = value;
            Area = areaMM2;
            Share = share;
            Weighs = weighs;
            Depth = depthMM;
            Agrees = agrees;
        }

        /// <summary>What the fill weighs over this band's footprint, kN/m2.</summary>
        public double Weighs { get; }

        /// <summary>The fill's average depth over this band's footprint, mm.</summary>
        public double Depth { get; }

        /// <summary>True where the band's value is what the fill weighs over it, within the tolerance.</summary>
        public bool Agrees { get; }

        /// <summary>kN/m2, nought where the fill stands on slab carrying no SIDL_C.</summary>
        public double Value { get; }

        /// <summary>mm2.</summary>
        public double Area { get; }

        /// <summary>Of the part of the fill that stands on ETABS slab.</summary>
        public double Share { get; }

        public override string ToString()
        {
            return $"{LoadSet.Text(Value)} kN/m2 over {Math.Round(Area / 1e6, 1).ToString("0.#", CultureInfo.InvariantCulture)} m2"
                   + $" (the fill weighs {LoadSet.Text(Weighs)} there{(Agrees ? "" : " - does not match")})";
        }
    }

    /// <summary>What the SIDL_C area check made of one fill floor.</summary>
    public sealed class SidlCAreaResult
    {
        public SidlCAreaResult(SidlCAreaSource_R source)
        {
            Source = source;
        }

        public SidlCAreaSource_R Source { get; }

        public string Story { get { return Source.Story; } }

        public SidlCAreaState State { get; set; }

        /// <summary>What the fill weighs, kN/m2.</summary>
        public double Expected { get; set; }

        public string How { get; set; } = "";

        /// <summary>What ETABS carries under it, averaged by area, kN/m2.</summary>
        public double Actual { get; set; }

        /// <summary>How much of the fill stands over an ETABS slab.</summary>
        public double CoveredShare { get; set; }

        /// <summary>Of what was averaged over, how much carries any SIDL_C.</summary>
        public double LoadedShare { get; set; }

        /// <summary>
        /// True where most of the fill had no ETABS slab under it, so the average was taken
        /// over the whole fill with the bare part as nothing.
        /// </summary>
        public bool AveragedOverWholeFill { get; set; }

        /// <summary>
        /// True where the fill's depth was read place by place off its faces. False, every band
        /// is held against the fill's average depth.
        /// </summary>
        public bool HasDepthByPlace { get; set; }

        public IList<SidlCAreaBand> Bands { get; set; } = new List<SidlCAreaBand>();

        /// <summary>The ETABS areas whose SIDL_C was read under the fill.</summary>
        public IList<string> Shells { get; set; } = new List<string>();

        public string Note { get; set; } = "";

        /// <summary>
        /// How much of the fill, in mm2, stands on each ETABS area carrying SIDL_C. What a panel
        /// two fills stand on is shared out between them by.
        /// </summary>
        public Dictionary<Slab_E, double> Overlaps { get; } = new Dictionary<Slab_E, double>();

        /// <summary>What the fill weighs in all, in kN.</summary>
        public double RevitForce { get { return Expected * Source.Area / 1e6; } }

        /// <summary>
        /// The SIDL_C on every area the fill stands on - all of each area, not only the part
        /// under the fill - in kN, shared where other fills stand on it too.
        /// </summary>
        public double EtabsForce { get; set; }

        /// <summary>The area that SIDL_C is spread over, in mm2.</summary>
        public double EtabsLoadedArea { get; set; }

        /// <summary>How much of the ETABS load area lies outside the fill, in mm2.</summary>
        public double LoadedOutside
        {
            get { return Math.Max(0, EtabsLoadedArea - Overlaps.Values.Sum()); }
        }

        /// <summary>The areas near the fill carrying its load, where it was drawn in the wrong place.</summary>
        public IList<string> Misplaced { get; } = new List<string>();

        public IList<Slab_E> MisplacedShells { get; } = new List<Slab_E>();

        /// <summary>
        /// The fill and the SIDL_C counted towards it, cut along each other's edges. Empty where
        /// the story would not cut, and the fill is then drawn whole.
        /// </summary>
        public IList<SidlCAreaPiece> Pieces { get; } = new List<SidlCAreaPiece>();

        /// <summary>How far from the fill the furthest of those is, in mm.</summary>
        public double MisplacedGap { get; set; }

        /// <summary>True where the fill has no SIDL_C, or no slab, in ETABS at all.</summary>
        public bool IsMissingInEtabs
        {
            get { return State == SidlCAreaState.NoEtabsLoad || State == SidlCAreaState.NoEtabsSlab; }
        }

        public ResultLevel Level
        {
            get
            {
                switch (State)
                {
                    case SidlCAreaState.Matched: return ResultLevel.Passed;
                    case SidlCAreaState.EquivalentTotal: return ResultLevel.Review;
                    default: return ResultLevel.Failed;
                }
            }
        }

        /// <summary>The bands as a sentence, each with what the fill weighs over it.</summary>
        public string BandText
        {
            get
            {
                return Bands.Count == 0 ? "" : string.Join(", ", Bands.OrderBy(band => band.Value).Select(band => band.ToString()));
            }
        }

        public string Summary
        {
            get
            {
                string name = Source.Description;
                switch (State)
                {
                    case SidlCAreaState.Matched:
                        return Bands.Count == 1
                            ? $"{name} matches: ETABS carries {LoadSet.Text(Bands[0].Value)} kN/m2 and the fill weighs "
                              + $"{LoadSet.Text(Bands[0].Weighs)} kN/m2" + Whole + Outside
                            : $"{name} matches band by band: {BandText}" + Whole + Outside;

                    case SidlCAreaState.ValueMismatch:
                        return $"{name} does not match: {WrongBands}" + Whole
                               + $"; in all it weighs {Kn(RevitForce)} and ETABS carries {Kn(EtabsForce)} on the areas under it";

                    case SidlCAreaState.EquivalentTotal:
                        return $"the same total load, put on a different area: {name} weighs {Kn(RevitForce)} over "
                               + $"{SquareMetres(Source.Area)} and ETABS carries {Kn(EtabsForce)} over "
                               + $"{SquareMetres(EtabsLoadedArea)} - under the fill itself {WrongBands}" + Outside;

                    case SidlCAreaState.Misplaced:
                        return $"the SIDL_C load for {name} is not under it: ETABS carries {Kn(EtabsForce)} of SIDL_C "
                               + $"on {string.Join(", ", Misplaced)}, up to {Metres(MisplacedGap)} away with no fill over "
                               + $"{(Misplaced.Count == 1 ? "it" : "them")}, and the fill weighs {Kn(RevitForce)}";

                    case SidlCAreaState.NoEtabsLoad:
                        return $"the SIDL_C load for {name} is not in ETABS: it weighs {LoadSet.Text(Expected)} "
                               + "kN/m2 and nothing under it carries any SIDL_C"
                               + (CoveredShare < 0.999
                                   ? $" ({Percent(1 - CoveredShare)} of it has no ETABS slab under it either)"
                                   : "");

                    default:
                        return $"{name}: {Note}";
                }
            }
        }

        /// <summary>The bands that do not match, as a sentence.</summary>
        private string WrongBands
        {
            get
            {
                return string.Join("; ", Bands.Where(band => !band.Agrees).Select(band =>
                    (band.Value == 0 ? "ETABS carries no SIDL_C" : $"ETABS carries {LoadSet.Text(band.Value)} kN/m2")
                    + $" over {SquareMetres(band.Area)} where the fill weighs {LoadSet.Text(band.Weighs)} kN/m2"));
            }
        }

        private string Whole
        {
            get
            {
                return AveragedOverWholeFill
                    ? $" - averaged over the whole fill, since only {Percent(CoveredShare)} of it stands over an ETABS slab"
                    : "";
            }
        }

        /// <summary>Where the ETABS load runs well past the fill, how far.</summary>
        private string Outside
        {
            get
            {
                return LoadedOutside > SidlCAreaCompareEngine.OutsideShare * Source.Area
                    ? $"; {SquareMetres(LoadedOutside)} of the SIDL_C area under it lies outside the fill"
                    : "";
            }
        }

        private static string Kn(double kiloNewtons)
        {
            return Math.Round(kiloNewtons).ToString("0", CultureInfo.InvariantCulture) + " kN";
        }

        private static string SquareMetres(double squareMillimetres)
        {
            return Math.Round(squareMillimetres / 1e6, 1).ToString("0.#", CultureInfo.InvariantCulture) + " m2";
        }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / 1000d, 1).ToString("0.#", CultureInfo.InvariantCulture) + " m";
        }

        private static string Percent(double share)
        {
            return Math.Round(share * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        }

        public override string ToString()
        {
            return Summary;
        }
    }

    /// <summary>The fields a SIDL_C area element carries, by key.</summary>
    public static class SidlCAreaResultKeys
    {
        public const string RevitId = "SidlCArea.RevitId";
        public const string EtabsName = "SidlCArea.EtabsName";
        public const string Type = "SidlCArea.Type";
        public const string Area = "SidlCArea.Area";
        public const string Depth = "SidlCArea.Depth";
        public const string Expected = "SidlCArea.Expected";
        public const string How = "SidlCArea.How";
        public const string Actual = "SidlCArea.Actual";
        public const string Bands = "SidlCArea.Bands";
        public const string Shells = "SidlCArea.Shells";
        public const string Total = "SidlCArea.Total";
        public const string Here = "SidlCArea.Here";
        public const string WeighsHere = "SidlCArea.WeighsHere";
        public const string PieceArea = "SidlCArea.PieceArea";
    }

    /// <summary>An ETABS area carrying SIDL_C that no fill floor in Revit stands over.</summary>
    public sealed class SidlCAreaUnclaimed
    {
        public SidlCAreaUnclaimed(Slab_E shell, double load)
        {
            Shell = shell;
            Load = load;
        }

        public Slab_E Shell { get; }

        /// <summary>kN/m2.</summary>
        public double Load { get; }

        public string Story { get { return Shell.Story; } }

        public string Summary
        {
            get
            {
                return $"{Shell.Description} carries {LoadSet.Text(Load)} kN/m2 of SIDL_C and no light "
                       + "weight fill in Revit stands over it";
            }
        }
    }
}
