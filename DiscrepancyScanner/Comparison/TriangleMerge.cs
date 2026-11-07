using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Puts triangles that came to the same answer back together into outlines, without the
    /// geometry kernel.
    ///
    /// The triangulation hands back a few dozen triangles per ETABS area, and reporting them
    /// one at a time would bury a single disagreement under a page of slivers. They have to be
    /// merged - but merging them with a boolean union is the very cost the triangulation was
    /// brought in to avoid, and it is not needed here: these triangles came out of one
    /// triangulation, so they share vertices exactly, and where two of them meet they meet
    /// along a whole edge.
    ///
    /// That makes the merge arithmetic. Every triangle edge is walked in order; an edge shared
    /// by two triangles is walked once each way and the two cancel, and what is left over is
    /// the boundary of the merged shape. Chaining those leftovers gives the outlines back.
    /// </summary>
    internal static class TriangleMerge
    {
        /// <summary>
        /// Points closer together than this along an outline are treated as one, and a vertex
        /// this far off the line between its neighbours is dropped as a step in a straight
        /// edge. Small next to the razor thin strip the slab edges are inserted as, so a real
        /// edge is never simplified away.
        /// </summary>
        private const double FlatteningToleranceInMM = 0.01d;

        /// <summary>
        /// The merged outlines of a set of triangles, all indexing the same point list.
        /// Triangles may be wound either way; holes and separate islands both come out right.
        /// </summary>
        public static IList<PlanPolygon> Merge(IList<MyPoint> points, IList<int[]> triangles)
        {
            List<PlanPolygon> result = new List<PlanPolygon>();

            if (points == null || triangles == null || triangles.Count == 0)
            {
                return result;
            }

            // ── every edge, wound the same way round every triangle ──────────────────────
            Dictionary<long, int> directed = new Dictionary<long, int>();

            foreach (int[] triangle in triangles)
            {
                if (triangle == null || triangle.Length != 3)
                {
                    continue;
                }

                int a = triangle[0], b = triangle[1], c = triangle[2];
                if (a == b || b == c || c == a)
                {
                    continue;
                }

                if (SignedArea(points, a, b, c) < 0)
                {
                    int swap = b;
                    b = c;
                    c = swap;
                }

                Count(directed, a, b);
                Count(directed, b, c);
                Count(directed, c, a);
            }

            // ── what is left once every shared edge has cancelled ────────────────────────
            Dictionary<int, List<int>> leaving = new Dictionary<int, List<int>>();
            int edges = 0;

            foreach (KeyValuePair<long, int> entry in directed)
            {
                int from = (int)(entry.Key >> 32);
                int to = (int)(entry.Key & 0xFFFFFFFFL);

                int back;
                directed.TryGetValue(Pack(to, from), out back);

                int net = entry.Value - back;
                if (net <= 0)
                {
                    continue;
                }

                List<int> list;
                if (!leaving.TryGetValue(from, out list))
                {
                    list = new List<int>(2);
                    leaving[from] = list;
                }

                for (int i = 0; i < net; i++)
                {
                    list.Add(to);
                    edges++;
                }
            }

            // ── chain the leftovers into closed rings ────────────────────────────────────
            List<PlanRing> rings = new List<PlanRing>();
            int guard = edges + 8;

            while (edges > 0 && guard-- > 0)
            {
                int start = -1;
                foreach (KeyValuePair<int, List<int>> entry in leaving)
                {
                    if (entry.Value.Count > 0)
                    {
                        start = entry.Key;
                        break;
                    }
                }

                if (start < 0)
                {
                    break;
                }

                List<MyPoint> ring = new List<MyPoint>();
                int at = start;
                int cameFrom = -1;

                while (true)
                {
                    List<int> options;
                    if (!leaving.TryGetValue(at, out options) || options.Count == 0)
                    {
                        break;
                    }

                    int pick = ChooseNext(points, cameFrom, at, options);
                    int next = options[pick];
                    options.RemoveAt(pick);
                    edges--;

                    ring.Add(points[at]);
                    cameFrom = at;
                    at = next;

                    if (at == start)
                    {
                        break;
                    }
                }

                if (ring.Count < 3)
                {
                    continue;
                }

                PlanRing built = PlanRing.FromPoints(Flatten(ring));
                if (built != null && built.Area > 0)
                {
                    rings.Add(built);
                }
            }

            // ── sort the rings into regions and their holes ──────────────────────────────
            List<PlanRing> outers = rings.Where(ring => ring.IsCounterClockwise)
                                         .OrderBy(ring => ring.Area)
                                         .ToList();
            List<PlanRing> holes = rings.Where(ring => !ring.IsCounterClockwise).ToList();

            Dictionary<PlanRing, List<PlanRing>> attached = new Dictionary<PlanRing, List<PlanRing>>();
            foreach (PlanRing outer in outers)
            {
                attached[outer] = new List<PlanRing>();
            }

            foreach (PlanRing hole in holes)
            {
                // Smallest boundary first, so a hole inside two nested outlines is given to
                // the inner one. A hole nothing encloses is dropped.
                foreach (PlanRing outer in outers)
                {
                    if (outer.Surrounds(hole))
                    {
                        attached[outer].Add(hole);
                        break;
                    }
                }
            }

            foreach (PlanRing outer in outers)
            {
                PlanPolygon polygon = PlanPolygon.Create(outer, attached[outer]);
                if (polygon != null)
                {
                    result.Add(polygon);
                }
            }

            return result;
        }

        /// <summary>
        /// Which edge to leave a vertex by. Where only one is left there is no choice, and
        /// nearly every vertex is like that. Where several outlines meet at one point, taking
        /// the sharpest turn back on the way in keeps each outline to itself instead of
        /// letting the walk cut across from one to another.
        /// </summary>
        private static int ChooseNext(IList<MyPoint> points, int cameFrom, int at, List<int> options)
        {
            if (options.Count == 1 || cameFrom < 0)
            {
                return 0;
            }

            MyPoint here = points[at];
            MyPoint back = points[cameFrom];
            double baseAngle = Math.Atan2(back.Y - here.Y, back.X - here.X);

            int best = 0;
            double bestTurn = double.MaxValue;

            for (int i = 0; i < options.Count; i++)
            {
                MyPoint to = points[options[i]];
                double turn = baseAngle - Math.Atan2(to.Y - here.Y, to.X - here.X);

                while (turn <= 0)
                {
                    turn += 2 * Math.PI;
                }
                while (turn > 2 * Math.PI)
                {
                    turn -= 2 * Math.PI;
                }

                if (turn < bestTurn)
                {
                    bestTurn = turn;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Drops the vertices that only mark where one triangle ended and the next began. A
        /// merged shape has a point at every triangle corner along what is really one straight
        /// edge, and left in they make the outline dozens of times longer than the shape needs.
        /// </summary>
        private static List<MyPoint> Flatten(List<MyPoint> ring)
        {
            List<MyPoint> kept = new List<MyPoint>(ring.Count);

            for (int i = 0; i < ring.Count; i++)
            {
                MyPoint previous = kept.Count > 0 ? kept[kept.Count - 1] : ring[(i - 1 + ring.Count) % ring.Count];
                MyPoint current = ring[i];
                MyPoint next = ring[(i + 1) % ring.Count];

                if (OffLine(previous, current, next) < FlatteningToleranceInMM)
                {
                    continue;
                }

                kept.Add(current);
            }

            return kept.Count >= 3 ? kept : ring;
        }

        /// <summary>How far <paramref name="b"/> stands off the line from a to c.</summary>
        private static double OffLine(MyPoint a, MyPoint b, MyPoint c)
        {
            double dx = c.X - a.X, dy = c.Y - a.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length < 1e-9)
            {
                return double.MaxValue;
            }

            return Math.Abs((b.X - a.X) * dy - (b.Y - a.Y) * dx) / length;
        }

        private static double SignedArea(IList<MyPoint> points, int a, int b, int c)
        {
            MyPoint pa = points[a], pb = points[b], pc = points[c];
            return (pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X);
        }

        private static void Count(Dictionary<long, int> directed, int from, int to)
        {
            long key = Pack(from, to);
            int count;
            directed.TryGetValue(key, out count);
            directed[key] = count + 1;
        }

        private static long Pack(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }
    }
}
