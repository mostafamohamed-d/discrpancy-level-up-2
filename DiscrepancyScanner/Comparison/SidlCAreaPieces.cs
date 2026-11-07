using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>What a piece of the SIDL_C area plan is: fill with load, fill without, load without fill.</summary>
    public enum SidlCAreaPieceKind
    {
        /// <summary>Fill, with SIDL_C under it. Carries the fill's verdict.</summary>
        OnFill,

        /// <summary>Fill with no SIDL_C under it: Revit only.</summary>
        FillBare,

        /// <summary>SIDL_C counted towards a fill but lying outside it: ETABS only.</summary>
        OutsideFill
    }

    /// <summary>
    /// One piece of the SIDL_C area plan, cut where the fill's edges and the SIDL_C areas'
    /// edges cross, the way the slab load check cuts the ETABS mesh along the loading plan.
    /// </summary>
    public sealed class SidlCAreaPiece
    {
        public SidlCAreaPiece(SidlCAreaResult fill, SidlCAreaPieceKind kind, double load,
                              PlanPolygon plan, IList<string> shells, int index)
        {
            Fill = fill;
            Kind = kind;
            Load = load;
            Plan = plan;
            Shells = shells ?? new List<string>();
            Index = index;
        }

        /// <summary>The fill the piece belongs to, or whose load it is.</summary>
        public SidlCAreaResult Fill { get; }

        public SidlCAreaPieceKind Kind { get; }

        /// <summary>The SIDL_C here, kN/m2.</summary>
        public double Load { get; }

        public PlanPolygon Plan { get; }

        /// <summary>The ETABS areas carrying the SIDL_C here.</summary>
        public IList<string> Shells { get; }

        /// <summary>Which of the pieces saying the same thing about the same fill this is.</summary>
        public int Index { get; }

        /// <summary>Below the size a difference is worth chasing: drawn, not counted.</summary>
        public bool IsTooSmallToCount { get; set; }

        /// <summary>The band of the fill this piece of it stands on, or null for a piece off the fill.</summary>
        public SidlCAreaBand Band
        {
            get
            {
                return Kind == SidlCAreaPieceKind.OutsideFill
                    ? null
                    : Fill.Bands.Where(band => Math.Abs(band.Value - Load) < 0.005)
                                .OrderByDescending(band => band.Area)
                                .FirstOrDefault();
            }
        }

        /// <summary>
        /// The verdict on a piece of fill standing on SIDL_C: its own band's, so a ramp with one
        /// band right and one wrong is drawn half green and half red. A band that is wrong only
        /// because the load was put on a different area, with the total right, is up for review.
        /// </summary>
        public ResultLevel Level
        {
            get
            {
                SidlCAreaBand band = Band;
                if (band == null || band.Agrees)
                {
                    return ResultLevel.Passed;
                }
                return Fill.State == SidlCAreaState.EquivalentTotal ? ResultLevel.Review : ResultLevel.Failed;
            }
        }

        /// <summary>A name for the piece that comes back the same next run, while the piece reads the same.</summary>
        public string Key
        {
            get
            {
                return Fill.Source.RevitID + "|" + Kind + "|"
                       + Math.Round(Load, 2).ToString("0.##", CultureInfo.InvariantCulture) + "|" + Index;
            }
        }

        public string Summary
        {
            get
            {
                string fill = Fill.Source.Description;
                string area = Math.Round(Plan.Area / 1e6, 1).ToString("0.#", CultureInfo.InvariantCulture) + " m2";
                string load = LoadSet.Text(Load) + " kN/m2";

                switch (Kind)
                {
                    case SidlCAreaPieceKind.OnFill:
                        SidlCAreaBand band = Band;
                        return band == null
                            ? $"{load} of SIDL_C on {area} of {fill} - " + Fill.Summary
                            : $"ETABS carries {load} on {area} of {fill}, where the fill weighs "
                              + $"{LoadSet.Text(band.Weighs)} kN/m2 ({Math.Round(band.Depth)} mm deep on average) - "
                              + (band.Agrees ? "matches" : "does not match")
                              + (Fill.State == SidlCAreaState.EquivalentTotal && !band.Agrees
                                  ? ", though the fill's total load is all there, put on a different area"
                                  : "");

                    case SidlCAreaPieceKind.FillBare:
                        return Fill.IsMissingInEtabs || Fill.State == SidlCAreaState.Misplaced
                            ? Fill.Summary
                            : $"{area} of {fill} has no SIDL_C under it - " + Fill.Summary;

                    default:
                        string why;
                        switch (Fill.State)
                        {
                            case SidlCAreaState.Misplaced:
                                why = "most likely its load drawn in the wrong place";
                                break;
                            case SidlCAreaState.EquivalentTotal:
                                why = "counted in its total, which agrees";
                                break;
                            default:
                                why = "no fill in Revit stands here";
                                break;
                        }
                        return $"{load} of SIDL_C on {string.Join(", ", Shells)} over {area} outside {fill} - {why}";
                }
            }
        }

        public override string ToString()
        {
            return $"{Kind} {LoadSet.Text(Load)} kN/m2, {Math.Round(Plan.Area / 1e6, 2)} m2";
        }
    }

    /// <summary>
    /// Cuts the fills and the SIDL_C areas counted towards them into pieces along each other's
    /// edges: one triangulation per story with every outline put in as a constraint, each
    /// triangle answered at its middle, and triangles with the same answer merged back into
    /// outlines. No geometry kernel, the same as the slab check's mesh.
    /// </summary>
    internal static class SidlCAreaCutter
    {
        private const double SnapTolerance = 0.5d;

        public static List<SidlCAreaPiece> Cut(IList<SidlCAreaResult> onStory, Func<Slab_E, double> loadOf,
                                               double smallestPiece, double narrowestPiece, double smallestIssue)
        {
            List<SidlCAreaPiece> pieces = new List<SidlCAreaPiece>();

            List<SidlCAreaResult> fills = onStory.Where(fill => fill.Source.Plan.Count > 0).ToList();
            List<Slab_E> shells = fills.SelectMany(fill => fill.Overlaps.Keys.Concat(fill.MisplacedShells))
                                       .Distinct()
                                       .Where(shell => shell.Plan != null)
                                       .ToList();
            if (fills.Count == 0)
            {
                return pieces;
            }

            ConstrainedDelaunay cdt = new ConstrainedDelaunay();
            foreach (PlanPolygon part in fills.SelectMany(fill => fill.Source.Plan).Concat(shells.Select(shell => shell.Plan)))
            {
                AddRing(cdt, part.Outer);
                foreach (PlanRing hole in part.Holes)
                {
                    AddRing(cdt, hole);
                }
            }

            cdt.Triangulate();

            List<MyPoint> points = cdt.Points.Select(point => new MyPoint(point.X, point.Y, 0)).ToList();
            Dictionary<Slab_E, double> loads = shells.ToDictionary(shell => shell, loadOf);

            // Every triangle answered at its middle, and filed under what it says.
            Dictionary<string, Group> groups = new Dictionary<string, Group>();
            foreach (int[] triangle in cdt.Triangles)
            {
                MyPoint a = points[triangle[0]], b = points[triangle[1]], c = points[triangle[2]];
                double x = (a.X + b.X + c.X) / 3d;
                double y = (a.Y + b.Y + c.Y) / 3d;

                SidlCAreaResult fill = fills.FirstOrDefault(one => Contains(one.Source.Plan, x, y));
                List<Slab_E> here = shells.Where(shell => shell.Plan.Bounds.Contains(x, y) && shell.Plan.Contains(x, y))
                                          .ToList();
                double load = here.Sum(shell => loads[shell]);

                if (fill == null && load == 0)
                {
                    continue;
                }

                SidlCAreaPieceKind kind = fill == null
                    ? SidlCAreaPieceKind.OutsideFill
                    : load != 0 ? SidlCAreaPieceKind.OnFill : SidlCAreaPieceKind.FillBare;
                SidlCAreaResult owner = fill ?? OwnerOf(here, fills);
                if (owner == null)
                {
                    continue;
                }

                List<string> names = here.Where(shell => loads[shell] != 0)
                                         .Select(shell => shell.UniqueName)
                                         .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                         .ToList();
                string key = fills.IndexOf(owner) + "|" + kind + "|" + Math.Round(load, 2).ToString(CultureInfo.InvariantCulture)
                             + "|" + string.Join(",", names);

                Group group;
                if (!groups.TryGetValue(key, out group))
                {
                    groups[key] = group = new Group { Fill = owner, Kind = kind, Load = Math.Round(load, 2), Shells = names };
                }
                group.Triangles.Add(triangle);
            }

            Dictionary<string, int> counted = new Dictionary<string, int>();
            foreach (Group group in groups.Values)
            {
                foreach (PlanPolygon plan in TriangleMerge.Merge(points, group.Triangles)
                                                          .OrderByDescending(plan => plan.Area))
                {
                    // Slivers left where two outlines all but meet: nothing to show.
                    if (plan.Area < smallestPiece || plan.TypicalWidth < narrowestPiece)
                    {
                        continue;
                    }

                    string name = group.Fill.Source.RevitID + "|" + group.Kind + "|" + group.Load.ToString(CultureInfo.InvariantCulture);
                    int index;
                    counted.TryGetValue(name, out index);
                    counted[name] = index + 1;

                    pieces.Add(new SidlCAreaPiece(group.Fill, group.Kind, group.Load, plan, group.Shells, index)
                    {
                        IsTooSmallToCount = plan.Area < smallestIssue
                    });
                }
            }

            return pieces;
        }

        private sealed class Group
        {
            public SidlCAreaResult Fill;
            public SidlCAreaPieceKind Kind;
            public double Load;
            public IList<string> Shells;
            public readonly List<int[]> Triangles = new List<int[]>();
        }

        /// <summary>The fill whose load the SIDL_C here is: the one standing most on these areas.</summary>
        private static SidlCAreaResult OwnerOf(List<Slab_E> here, List<SidlCAreaResult> fills)
        {
            SidlCAreaResult best = null;
            double bestOverlap = -1;

            foreach (SidlCAreaResult fill in fills)
            {
                foreach (Slab_E shell in here)
                {
                    double overlap;
                    if (fill.Overlaps.TryGetValue(shell, out overlap) && overlap > bestOverlap)
                    {
                        best = fill;
                        bestOverlap = overlap;
                    }
                    else if (best == null && fill.MisplacedShells.Contains(shell))
                    {
                        best = fill;
                        bestOverlap = 0;
                    }
                }
            }

            return best;
        }

        private static bool Contains(IList<PlanPolygon> plan, double x, double y)
        {
            return plan.Any(part => part.Bounds.Contains(x, y) && part.Contains(x, y));
        }

        private static void AddRing(ConstrainedDelaunay cdt, PlanRing ring)
        {
            if (ring == null || ring.Points.Count < 3)
            {
                return;
            }

            int count = ring.Points.Count;
            int[] index = new int[count];
            for (int k = 0; k < count; k++)
            {
                index[k] = cdt.AddOrSnapPoint(ring.Points[k].X, ring.Points[k].Y, SnapTolerance);
            }

            for (int k = 0; k < count; k++)
            {
                if (index[k] != index[(k + 1) % count])
                {
                    cdt.AddConstraint(index[k], index[(k + 1) % count]);
                }
            }
        }
    }
}
