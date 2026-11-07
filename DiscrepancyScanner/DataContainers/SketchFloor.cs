using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A floor read from its sketch rather than its solid, for the floors Revit gives no solid
    /// for. The pure part: the sketch's loops made into regions, and the openings cut through
    /// the floor taken out of them. See RevitEngine.readSlabFromSketch for the Revit part.
    /// </summary>
    public static class SketchFloor
    {
        /// <summary>
        /// The slab regions a sketch's loops describe. A sketch says nothing of which loop is
        /// an outline and which a hole, and which way it is drawn is not to be relied on, so
        /// it is read from how the loops nest: a loop inside no other is an outline, a loop
        /// inside one is a hole in it, a loop inside a hole is an island of slab again.
        /// </summary>
        public static List<PlanPolygon> Regions(IEnumerable<IList<MyPoint>> loops)
        {
            List<PlanRing> rings = (loops ?? Enumerable.Empty<IList<MyPoint>>())
                .Select(loop => PlanRing.FromPoints(loop))
                .Where(ring => ring != null)
                .OrderByDescending(ring => ring.Area)
                .ToList();

            int[] parent = new int[rings.Count];
            int[] depth = new int[rings.Count];
            for (int i = 0; i < rings.Count; i++)
            {
                parent[i] = -1;
                // The smallest larger ring around it is the one it sits directly inside.
                for (int j = i - 1; j >= 0; j--)
                {
                    if (rings[j].Surrounds(rings[i]))
                    {
                        parent[i] = j;
                        break;
                    }
                }
                depth[i] = parent[i] < 0 ? 0 : depth[parent[i]] + 1;
            }

            List<PlanPolygon> regions = new List<PlanPolygon>();
            for (int i = 0; i < rings.Count; i++)
            {
                if (depth[i] % 2 != 0)
                {
                    continue;
                }

                List<PlanRing> holes = new List<PlanRing>();
                for (int k = 0; k < rings.Count; k++)
                {
                    if (parent[k] == i)
                    {
                        holes.Add(rings[k]);
                    }
                }

                PlanPolygon region = PlanPolygon.Create(rings[i], holes);
                if (region != null)
                {
                    regions.Add(region);
                }
            }
            return regions;
        }

        /// <summary>
        /// The regions with the openings cut through the floor taken out. An opening that lies
        /// wholly inside one region and clear of its holes is cut out of it. Any other - one
        /// crossing the slab edge, overlapping a hole, or reaching over two regions - cannot be
        /// cut out reliably this way, so it is left in and counted in
        /// <paramref name="notCut"/>, for the floor to be put up for review instead of being
        /// read as slab where it may not be.
        /// </summary>
        public static List<PlanPolygon> CutOpenings(List<PlanPolygon> regions, IEnumerable<IList<MyPoint>> openings,
                                                    out int cut, out int notCut)
        {
            cut = 0;
            notCut = 0;
            List<PlanPolygon> result = new List<PlanPolygon>(regions ?? new List<PlanPolygon>());

            foreach (IList<MyPoint> outline in openings ?? Enumerable.Empty<IList<MyPoint>>())
            {
                PlanRing opening = PlanRing.FromPoints(outline);
                if (opening == null)
                {
                    continue;
                }

                int index = result.FindIndex(region => WhollyInside(region.Outer, opening)
                                                       && region.Holes.All(hole => Clear(hole, opening)));
                if (index < 0)
                {
                    // Lying wholly in a hole the sketch already has, it has nothing to cut.
                    if (result.Any(region => region.Holes.Any(hole => WhollyInside(hole, opening))))
                    {
                        continue;
                    }
                    if (result.Any(region => region.Outer.Bounds.IntersectsWith(opening.Bounds)))
                    {
                        notCut++;
                    }
                    continue;
                }

                PlanPolygon region0 = result[index];
                PlanPolygon withHole = PlanPolygon.Create(region0.Outer, region0.Holes.Concat(new[] { opening }));
                if (withHole == null)
                {
                    notCut++;
                    continue;
                }
                result[index] = withHole;
                cut++;
            }

            return result;
        }

        /// <summary>
        /// Every corner of <paramref name="inner"/> inside <paramref name="outer"/>, and no
        /// corner of the outer one inside the inner - so an opening reaching across the slab
        /// edge, which a test of one corner would take as inside, is not.
        /// </summary>
        private static bool WhollyInside(PlanRing outer, PlanRing inner)
        {
            return outer.Bounds.IntersectsWith(inner.Bounds)
                   && inner.Points.All(p => outer.Contains(p.X, p.Y))
                   && !outer.Points.Any(p => inner.Contains(p.X, p.Y));
        }

        /// <summary>True when two rings neither overlap nor one holds the other.</summary>
        private static bool Clear(PlanRing a, PlanRing b)
        {
            if (!a.Bounds.IntersectsWith(b.Bounds))
            {
                return true;
            }
            if (a.Surrounds(b) || b.Surrounds(a))
            {
                return false;
            }
            return !b.Points.Any(p => a.Contains(p.X, p.Y)) && !a.Points.Any(p => b.Contains(p.X, p.Y));
        }
    }
}
