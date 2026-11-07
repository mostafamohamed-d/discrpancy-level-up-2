using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>What the detector made of one body standing against a floor.</summary>
    public enum ThickeningOutcome
    {
        /// <summary>Enough geometric evidence that it is a local thickening of the floor.</summary>
        Detected,
        /// <summary>Clear evidence that it is something else - a beam, a column, a wall, a topping.</summary>
        Rejected,
        /// <summary>Not enough evidence either way. Kept visible, and never read as slab.</summary>
        Ambiguous
    }

    public enum ThickeningConfidence { Low, Medium, High }

    /// <summary>
    /// The thresholds the detector is configured with. All of them are defaults that can be
    /// changed in the settings. Area, short side and compactness are evidence that raises or
    /// lowers confidence - none of them excludes a body on its own.
    /// </summary>
    public sealed class ThickeningOptions
    {
        /// <summary>Share of a body's footprint that must lie over the floor to be read as part of it (0..1).</summary>
        public double MinOverlap = 0.90d;

        /// <summary>Heights this close are the same height, in mm.</summary>
        public double FlushTolerance = 10d;

        /// <summary>How far under the floor's underside a body has to reach to add depth, in mm.</summary>
        public double MinProjection = 50d;

        /// <summary>Footprint area at which a body reads as a panel rather than a strip, in mm2.</summary>
        public double MinArea = 3e6d;

        /// <summary>Short side at which a body reads as a panel rather than a strip, in mm.</summary>
        public double MinShortSide = 1500d;

        /// <summary>Footprint area over the area of its smallest enclosing rectangle (0..1).</summary>
        public double MinCompactness = 0.85d;

        /// <summary>How many of the four shape tests (area, short side, compactness, aspect) a body must pass to be Detected.</summary>
        public int MinEvidence = 3;

        /// <summary>Overlap under this is a body that merely grazes the floor.</summary>
        public const double GrazingOverlap = 0.10d;

        /// <summary>A body at least this much longer than wide is strip-like.</summary>
        public const double StripAspect = 6d;

        /// <summary>A strip no wider than this is a beam-like member, in mm.</summary>
        public const double BeamMaxWidth = 900d;

        /// <summary>Aspect a panel may have before it counts against it.</summary>
        public const double PanelAspect = 4d;

        public static ThickeningOptions FromPreferences(PrefrencesData pref)
        {
            ThickeningOptions options = new ThickeningOptions();
            if (pref == null)
            {
                return options;
            }

            options.MinOverlap = Math.Max(0d, Math.Min(1d, pref.ThickeningMinOverlapPercent / 100d));
            options.FlushTolerance = Math.Max(0d, pref.ThickeningFlushToleranceMM);
            options.MinProjection = Math.Max(0d, pref.ThickeningMinProjectionMM);
            options.MinArea = Math.Max(0d, pref.ThickeningMinAreaM2) * 1e6d;
            options.MinShortSide = Math.Max(0d, pref.ThickeningMinShortSideMM);
            options.MinCompactness = Math.Max(0d, Math.Min(1d, pref.ThickeningMinCompactness));
            return options;
        }
    }

    /// <summary>A body near a floor, measured in the same frame as the floor.</summary>
    public sealed class ThickeningCandidate
    {
        public long ElementId;

        /// <summary>Category, family and type, for a person to read. Never used to decide anything.</summary>
        public string Description = "";

        public int Solids;

        /// <summary>The level, upward facing faces, each as its loops (an outline and its holes).</summary>
        public List<List<List<MyPoint>>> TopFaces = new List<List<List<MyPoint>>>();

        public bool HasLevelTop;
        public bool HasLevelBottom;
        public double TopHigh, TopLow, BottomHigh, BottomLow;

        /// <summary>Upward faces that lean - the chamfer or batter of a side - and so give no top height.</summary>
        public int InclinedUpFaces;
        public double InclinedUpArea;

        public string Unread = "";
    }

    /// <summary>The floor a body is measured against.</summary>
    public sealed class ThickeningHost
    {
        public long Id;
        public double Top;
        public double Bottom;

        /// <summary>One top level and one bottom level. False for a ramp, a warped or a shape edited floor.</summary>
        public bool Flat = true;

        public IList<PlanPolygon> Parts = new List<PlanPolygon>();
    }

    /// <summary>
    /// One body judged against one floor. Detected ones become regions of the slab check;
    /// ambiguous ones are kept so they can be looked at; rejected ones are only reported.
    /// </summary>
    public sealed class ThickeningPatch
    {
        public long HostFloorId;

        /// <summary>The model elements this patch was read from. More than one only when identical bodies were folded together.</summary>
        public List<long> SourceElementIds = new List<long>();

        /// <summary>What the bodies are, as the model calls them. Provenance only.</summary>
        public List<string> Provenance = new List<string>();

        /// <summary>The footprint of the body, one region per face looking up.</summary>
        public List<PlanPolygon> Plans = new List<PlanPolygon>();

        /// <summary>Top of the concrete here: the higher of the body's top and the floor's.</summary>
        public double TopElevation;

        /// <summary>Bottom of the concrete here: the lower of the body's bottom and the floor's.</summary>
        public double BottomElevation;

        public double BodyTop;
        public double BodyBottom;

        /// <summary>The depth of concrete under the footprint, floor and body together, in mm.</summary>
        public double Thickness { get { return Math.Max(0d, TopElevation - BottomElevation); } }

        /// <summary>Share of the footprint that lies over the host floor (0..1).</summary>
        public double OverHostRatio;

        public double FootprintArea;
        public double ShortSide;
        public double LongSide;
        public double Compactness;

        /// <summary>
        /// Always Unknown. A body carries no PT or RC of its own and does not take its floor's, so
        /// the PT/RC check has nothing to compare it on.
        /// </summary>
        public string Classification = "Unknown";

        public ThickeningOutcome Outcome;
        public ThickeningConfidence Confidence;

        /// <summary>The one line reason for the outcome.</summary>
        public string Reason = "";

        /// <summary>The tests it passed and failed.</summary>
        public List<string> Evidence = new List<string>();

        /// <summary>The same patch moved by a transform, plan and heights both.</summary>
        public ThickeningPatch Moved(Func<PlanPolygon, PlanPolygon> plan, double dz)
        {
            ThickeningPatch copy = (ThickeningPatch)MemberwiseClone();
            copy.SourceElementIds = new List<long>(SourceElementIds);
            copy.Provenance = new List<string>(Provenance);
            copy.Evidence = new List<string>(Evidence);
            copy.Plans = Plans.Select(plan).Where(p => p != null).ToList();
            copy.TopElevation += dz;
            copy.BottomElevation += dz;
            copy.BodyTop += dz;
            copy.BodyBottom += dz;
            return copy;
        }

        public string IdText { get { return string.Join("+", SourceElementIds); } }
    }

    /// <summary>
    /// Decides, from geometry alone, whether a body standing against a floor is a local thickening
    /// of it. Names, categories and parameters are never read here.
    ///
    /// Only differences are used - the body's heights against the floor's - so a vertical shift
    /// applied to both leaves every outcome as it was.
    /// </summary>
    public static class ThickeningClassifier
    {
        private const int Grid = 25;

        public static ThickeningPatch Classify(ThickeningHost host, ThickeningCandidate body, ThickeningOptions options)
        {
            double overlap = OverlapWithHost(host, body);
            return Classify(host, body, options, overlap);
        }

        /// <summary>As above with the share over the floor given, so a boundary can be tested exactly.</summary>
        public static ThickeningPatch Classify(ThickeningHost host, ThickeningCandidate body, ThickeningOptions options,
                                               double overlap)
        {
            options = options ?? new ThickeningOptions();
            double tol = options.FlushTolerance;

            ThickeningPatch patch = new ThickeningPatch
            {
                HostFloorId = host.Id,
                OverHostRatio = overlap
            };
            patch.SourceElementIds.Add(body.ElementId);
            if (!string.IsNullOrEmpty(body.Description))
            {
                patch.Provenance.Add(body.Description);
            }

            Func<ThickeningOutcome, string, ThickeningPatch> finish = (outcome, reason) =>
            {
                patch.Outcome = outcome;
                patch.Reason = reason;
                if (outcome != ThickeningOutcome.Detected)
                {
                    patch.Confidence = ThickeningConfidence.Low;
                }
                return patch;
            };

            if (!string.IsNullOrEmpty(body.Unread))
            {
                return finish(ThickeningOutcome.Ambiguous, "part of the body could not be read: " + body.Unread);
            }

            // Footprint and heights, from the level faces.
            List<MyPoint> topPoints = body.TopFaces.SelectMany(face => face).SelectMany(loop => loop).ToList();
            if (!body.HasLevelTop || !body.HasLevelBottom || topPoints.Count == 0)
            {
                string why = !body.HasLevelTop && body.InclinedUpFaces > 0
                    ? "its upward faces are all inclined, so it has no top height"
                    : !body.HasLevelTop ? "it has no level face looking up" : "it has no level face looking down";
                return finish(ThickeningOutcome.Ambiguous, why);
            }

            patch.BodyTop = body.TopHigh;
            patch.BodyBottom = body.BottomLow;
            patch.TopElevation = Math.Max(body.TopHigh, host.Top);
            patch.BottomElevation = Math.Min(body.BottomLow, host.Bottom);

            MeasureFootprint(body, patch);
            patch.Plans = BuildPlans(body);

            if (!host.Flat)
            {
                return finish(ThickeningOutcome.Ambiguous, "the floor has no single top and bottom level (a ramp or a warped floor)");
            }

            if (body.TopHigh - body.TopLow > tol || body.BottomHigh - body.BottomLow > tol)
            {
                return finish(ThickeningOutcome.Ambiguous,
                    $"its level faces are not at one height (top {Math.Round(body.TopLow, 1)}..{Math.Round(body.TopHigh, 1)}, " +
                    $"bottom {Math.Round(body.BottomLow, 1)}..{Math.Round(body.BottomHigh, 1)})");
            }

            if (body.InclinedUpArea > patch.FootprintArea)
            {
                return finish(ThickeningOutcome.Ambiguous, "most of its upward faces are inclined");
            }

            double hostTop = host.Top, hostBottom = host.Bottom;
            double top = body.TopHigh, bottom = body.BottomLow;
            double aspect = patch.ShortSide > 1d ? patch.LongSide / patch.ShortSide : double.MaxValue;
            bool strip = aspect >= ThickeningOptions.StripAspect;
            bool beamLike = strip && patch.ShortSide <= ThickeningOptions.BeamMaxWidth;

            if (overlap < ThickeningOptions.GrazingOverlap)
            {
                return finish(ThickeningOutcome.Rejected, $"only {Pct(overlap)} of it is over this floor");
            }

            if (top > hostTop + tol)
            {
                string how = bottom < hostBottom - tol
                    ? "it passes through the floor and rises above its top (a column or a wall)"
                    : bottom >= hostTop - tol
                        ? "it stands on the floor (a topping, a planted wall or a landing)"
                        : "it rises above the floor's top (an upstand)";
                return finish(ThickeningOutcome.Rejected, how + $", {Math.Round(top - hostTop, 1)} above the top");
            }

            if (top < hostBottom - tol)
            {
                return finish(ThickeningOutcome.Rejected,
                    $"it does not touch the floor: its top is {Math.Round(hostBottom - top, 1)} under the underside");
            }

            if (bottom >= hostBottom - tol)
            {
                return finish(ThickeningOutcome.Rejected, "it lies within the floor's own depth and adds none");
            }

            // From here the body reaches below the underside and does not rise above the top.
            double projection = hostBottom - bottom;
            bool flushTop = Math.Abs(top - hostTop) <= tol;

            if (!flushTop)
            {
                string where = Math.Abs(top - hostBottom) <= tol
                    ? "it hangs from the underside"
                    : $"its top is {Math.Round(hostTop - top, 1)} under the floor's top";
                if (beamLike)
                {
                    return finish(ThickeningOutcome.Rejected, where + " and is a narrow strip (a beam or a wall)");
                }
                return finish(ThickeningOutcome.Ambiguous, where + ", so it is not flush with the floor");
            }

            if (beamLike)
            {
                return finish(ThickeningOutcome.Rejected,
                    $"a strip {Math.Round(patch.ShortSide)} wide and {Math.Round(aspect, 1)} times as long (a beam)");
            }
            if (strip)
            {
                return finish(ThickeningOutcome.Ambiguous,
                    $"a band {Math.Round(patch.ShortSide)} wide and {Math.Round(aspect, 1)} times as long");
            }
            if (projection < options.MinProjection)
            {
                return finish(ThickeningOutcome.Ambiguous,
                    $"it reaches only {Math.Round(projection, 1)} under the underside (least {options.MinProjection})");
            }
            if (overlap < options.MinOverlap)
            {
                return finish(ThickeningOutcome.Ambiguous,
                    $"only {Pct(overlap)} of it is over the floor (least {Pct(options.MinOverlap)})");
            }

            // Flush with the top, reaching under the underside, over the floor: a thickening
            // by the geometry. The shape says how sure.
            int passed = 0;
            passed += Test(patch, patch.FootprintArea >= options.MinArea,
                           $"footprint {Math.Round(patch.FootprintArea / 1e6, 2)} m2 (panel from {options.MinArea / 1e6} m2)");
            passed += Test(patch, patch.ShortSide >= options.MinShortSide,
                           $"short side {Math.Round(patch.ShortSide)} (panel from {options.MinShortSide})");
            passed += Test(patch, patch.Compactness >= options.MinCompactness,
                           $"compactness {Math.Round(patch.Compactness, 2)} (panel from {options.MinCompactness})");
            passed += Test(patch, aspect <= ThickeningOptions.PanelAspect,
                           $"length/width {Math.Round(aspect, 1)} (panel to {ThickeningOptions.PanelAspect})");

            patch.Confidence = passed >= 4 ? ThickeningConfidence.High
                             : passed >= options.MinEvidence ? ThickeningConfidence.Medium
                             : ThickeningConfidence.Low;

            if (passed >= options.MinEvidence)
            {
                return finish(ThickeningOutcome.Detected,
                    $"flush with the top, {Math.Round(projection, 1)} under the underside, {Pct(overlap)} over the floor");
            }

            return finish(ThickeningOutcome.Ambiguous,
                $"flush with the top and {Math.Round(projection, 1)} under the underside, but only {passed} of 4 shape tests pass");
        }

        private static int Test(ThickeningPatch patch, bool pass, string text)
        {
            patch.Evidence.Add((pass ? "pass: " : "fail: ") + text);
            return pass ? 1 : 0;
        }

        private static string Pct(double fraction)
        {
            return Math.Round(fraction * 100d, 1).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// Folds patches that are the same shape at the same heights into one, keeping every
        /// source id and every provenance line. Patches that merely overlap are left alone.
        /// </summary>
        public static List<ThickeningPatch> Deduplicate(IEnumerable<ThickeningPatch> patches)
        {
            List<ThickeningPatch> kept = new List<ThickeningPatch>();
            Dictionary<string, ThickeningPatch> seen = new Dictionary<string, ThickeningPatch>();

            foreach (ThickeningPatch patch in patches)
            {
                string key = Signature(patch);
                ThickeningPatch first;
                if (seen.TryGetValue(key, out first))
                {
                    foreach (long id in patch.SourceElementIds.Where(id => !first.SourceElementIds.Contains(id)))
                    {
                        first.SourceElementIds.Add(id);
                    }
                    foreach (string line in patch.Provenance.Where(line => !first.Provenance.Contains(line)))
                    {
                        first.Provenance.Add(line);
                    }
                    continue;
                }

                seen[key] = patch;
                kept.Add(patch);
            }

            return kept;
        }

        private static string Signature(ThickeningPatch patch)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (PlanPolygon plan in patch.Plans)
            {
                foreach (MyPoint point in plan.Outer.Points)
                {
                    minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                }
            }

            Func<double, long> cell = value => (long)Math.Round(value / 2d);
            return string.Join("|", patch.HostFloorId, patch.Outcome, cell(minX), cell(minY), cell(maxX), cell(maxY),
                               cell(patch.FootprintArea / 1000d), cell(patch.TopElevation), cell(patch.BottomElevation));
        }

        // --- geometry -----------------------------------------------------------------------

        /// <summary>Share of the body's footprint over the host floor, by a grid of samples in its own box.</summary>
        public static double OverlapWithHost(ThickeningHost host, ThickeningCandidate body)
        {
            List<MyPoint> points = body.TopFaces.SelectMany(face => face).SelectMany(loop => loop).ToList();
            if (points.Count == 0 || host.Parts == null || host.Parts.Count == 0)
            {
                return 0d;
            }

            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);

            int onBody = 0, onFloor = 0;
            for (int i = 0; i < Grid; i++)
            {
                for (int j = 0; j < Grid; j++)
                {
                    double x = minX + (maxX - minX) * (i + 0.5d) / Grid;
                    double y = minY + (maxY - minY) * (j + 0.5d) / Grid;

                    if (!body.TopFaces.Any(face => InsideLoops(face, x, y)))
                    {
                        continue;
                    }
                    onBody++;
                    if (host.Parts.Any(part => part.Contains(x, y)))
                    {
                        onFloor++;
                    }
                }
            }

            return onBody == 0 ? 0d : (double)onFloor / onBody;
        }

        /// <summary>Even-odd test over one face's loops, outline and holes alike.</summary>
        internal static bool InsideLoops(List<List<MyPoint>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<MyPoint> loop in loops)
            {
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    if ((loop[i].Y > y) != (loop[j].Y > y)
                        && x < (loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        public static double LoopArea(List<MyPoint> loop)
        {
            double twice = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                MyPoint a = loop[i], b = loop[(i + 1) % loop.Count];
                twice += a.X * b.Y - b.X * a.Y;
            }
            return twice / 2d;
        }

        /// <summary>Footprint area, and the width, length and fill of the smallest rectangle round it.</summary>
        private static void MeasureFootprint(ThickeningCandidate body, ThickeningPatch patch)
        {
            double area = 0;
            foreach (List<List<MyPoint>> face in body.TopFaces)
            {
                List<double> areas = face.Select(loop => Math.Abs(LoopArea(loop))).OrderByDescending(a => a).ToList();
                if (areas.Count > 0)
                {
                    area += areas[0] - areas.Skip(1).Sum();
                }
            }
            patch.FootprintArea = area;

            List<double[]> hull = ConvexHull(body.TopFaces.SelectMany(face => face).SelectMany(loop => loop)
                                                 .Select(p => new[] { p.X, p.Y }).ToList());
            if (hull.Count < 3)
            {
                return;
            }

            double bestWidth = double.MaxValue, bestLength = 0;
            for (int i = 0; i < hull.Count; i++)
            {
                double[] a = hull[i], b = hull[(i + 1) % hull.Count];
                double dx = b[0] - a[0], dy = b[1] - a[1];
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9)
                {
                    continue;
                }
                dx /= len; dy /= len;

                double minAlong = double.MaxValue, maxAlong = double.MinValue, minAcross = double.MaxValue, maxAcross = double.MinValue;
                foreach (double[] p in hull)
                {
                    double along = (p[0] - a[0]) * dx + (p[1] - a[1]) * dy;
                    double across = -(p[0] - a[0]) * dy + (p[1] - a[1]) * dx;
                    minAlong = Math.Min(minAlong, along); maxAlong = Math.Max(maxAlong, along);
                    minAcross = Math.Min(minAcross, across); maxAcross = Math.Max(maxAcross, across);
                }

                double width = maxAcross - minAcross;
                if (width < bestWidth)
                {
                    bestWidth = width;
                    bestLength = maxAlong - minAlong;
                }
            }

            if (bestWidth == double.MaxValue)
            {
                return;
            }

            patch.ShortSide = Math.Min(bestWidth, bestLength);
            patch.LongSide = Math.Max(bestWidth, bestLength);
            patch.Compactness = bestWidth * bestLength > 1d ? Math.Min(1d, area / (bestWidth * bestLength)) : 0d;
        }

        private static List<double[]> ConvexHull(List<double[]> points)
        {
            List<double[]> sorted = points.OrderBy(p => p[0]).ThenBy(p => p[1]).ToList();
            if (sorted.Count < 3)
            {
                return sorted;
            }

            Func<double[], double[], double[], double> cross = (o, a, b) =>
                (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);

            List<double[]> hull = new List<double[]>();
            foreach (double[] p in sorted)
            {
                while (hull.Count >= 2 && cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }

            int lower = hull.Count + 1;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                double[] p = sorted[i];
                while (hull.Count >= lower && cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        /// <summary>One plan region per upward face: the largest loop is the outline, the rest are holes.</summary>
        private static List<PlanPolygon> BuildPlans(ThickeningCandidate body)
        {
            List<PlanPolygon> plans = new List<PlanPolygon>();
            foreach (List<List<MyPoint>> face in body.TopFaces)
            {
                List<PlanRing> rings = face
                    .Select(loop => PlanRing.FromPoints(loop))
                    .Where(ring => ring != null)
                    .OrderByDescending(ring => ring.Area)
                    .ToList();
                if (rings.Count == 0)
                {
                    continue;
                }

                PlanPolygon polygon = PlanPolygon.Create(rings[0], rings.Skip(1));
                if (polygon != null)
                {
                    plans.Add(polygon);
                }
            }
            return plans;
        }
    }
}
