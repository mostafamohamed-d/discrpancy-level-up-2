using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The two questions about a pair of plan regions that can be settled without the geometry
    /// kernel: do they miss each other entirely, and does one hold the other completely.
    ///
    /// Both answers are worth having because both make a boolean unnecessary, and a boolean is
    /// expensive out of all proportion to them. The kernel is a solid modeller: it extrudes two
    /// prisms, meshes them, works out how their faces cut each other and hands back a new body,
    /// several milliseconds a time. These are segment tests on the outlines - the same
    /// arithmetic the bounding box check already does, only exact - and run in microseconds.
    ///
    /// Every test here is conservative in the same direction: unsure is always answered "no",
    /// so being unable to tell costs a boolean that would have been made anyway and never a
    /// wrong answer.
    /// </summary>
    public static class PlanRegionQuick
    {
        /// <summary>
        /// True when the two regions provably share no ground, so an intersection is empty and
        /// a difference leaves the first region exactly as it was.
        ///
        /// The boxes overlapping is not enough - an L shaped slab claims the corner it does not
        /// cover - so this is only settled once no edge of one crosses an edge of the other and
        /// neither sits inside the other.
        /// </summary>
        public static bool Disjoint(PlanPolygon a, PlanPolygon b)
        {
            if (a == null || b == null)
            {
                return true;
            }

            if (!a.Bounds.IntersectsWith(b.Bounds))
            {
                return true;
            }

            if (AnyEdgeCrosses(a, b))
            {
                return false;
            }

            // No edges cross, so one is either wholly inside the other or wholly outside it,
            // and a single point of each settles which - but it has to be a point that is
            // properly inside, not a corner. See InsideOther.
            return !InsideOther(a, b) && !InsideOther(b, a);
        }

        /// <summary>
        /// True when <paramref name="outer"/> provably covers the whole of <paramref name="inner"/>,
        /// so their intersection is <paramref name="inner"/> unchanged.
        ///
        /// A region with holes can only be said to cover another once the holes are known to be
        /// clear of it, which is what the containment test on every ring is for.
        /// </summary>
        public static bool Covers(PlanPolygon outer, PlanPolygon inner)
        {
            if (outer == null || inner == null)
            {
                return false;
            }

            PlanBounds o = outer.Bounds;
            PlanBounds i = inner.Bounds;

            if (i.MinX < o.MinX || i.MaxX > o.MaxX || i.MinY < o.MinY || i.MaxY > o.MaxY)
            {
                return false;
            }

            // An edge of the inner region crossing one of the outer means it reaches outside,
            // or into one of its holes; either way it is not covered.
            if (AnyEdgeCrosses(outer, inner))
            {
                return false;
            }

            // Nothing crosses, so every point of the inner region is on the same side of every
            // ring of the outer one, and one point says which side that is - as long as it is
            // a point that is properly inside. See InsideOther.
            return InsideOther(inner, outer);
        }

        /// <summary>
        /// Whether <paramref name="region"/> lies inside <paramref name="other"/>, asked at a
        /// point properly inside <paramref name="region"/> rather than at one of its corners.
        ///
        /// Which corner is used to matter enormously, and wrongly. Two areas that were cut
        /// apart along the same line share that line exactly, so a corner of one sits on the
        /// edge of the other - and whether a point exactly on an outline counts as inside it is
        /// not something an even-odd crossing count answers, it is whatever the arithmetic
        /// happens to do. Asked at such a corner this said an area beside another was inside
        /// it, and <see cref="PlanRegionMath.Subtract"/> then took the whole of it away: on a
        /// real basement thirteen ETABS areas were dropped that way and the plan was blank
        /// where they had been. A point properly inside the region cannot be on the other's
        /// outline unless the two genuinely overlap, and an overlap has already been ruled out
        /// by the crossing test above.
        /// </summary>
        private static bool InsideOther(PlanPolygon region, PlanPolygon other)
        {
            MyPoint inside = region.InteriorPoint();
            return other.Contains(inside.X, inside.Y);
        }

        /// <summary>Whether any edge of either region properly crosses an edge of the other.</summary>
        private static bool AnyEdgeCrosses(PlanPolygon a, PlanPolygon b)
        {
            foreach (PlanRing ringA in Rings(a))
            {
                foreach (PlanRing ringB in Rings(b))
                {
                    if (!ringA.Bounds.IntersectsWith(ringB.Bounds))
                    {
                        continue;
                    }
                    if (RingsCross(ringA, ringB))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<PlanRing> Rings(PlanPolygon region)
        {
            yield return region.Outer;
            foreach (PlanRing hole in region.Holes)
            {
                yield return hole;
            }
        }

        /// <summary>
        /// Segment against segment, with each edge's own box used to throw out the pairs that
        /// cannot meet. That reject is what keeps this cheap on the outlines that matter, which
        /// are the long ones: two rings of a few hundred points each would otherwise be tens of
        /// thousands of full tests, and almost all of them are decided by a comparison.
        /// </summary>
        private static bool RingsCross(PlanRing a, PlanRing b)
        {
            IList<MyPoint> pa = a.Points;
            IList<MyPoint> pb = b.Points;

            for (int i = 0; i < pa.Count; i++)
            {
                MyPoint a1 = pa[i];
                MyPoint a2 = pa[(i + 1) % pa.Count];

                double aMinX = Math.Min(a1.X, a2.X), aMaxX = Math.Max(a1.X, a2.X);
                double aMinY = Math.Min(a1.Y, a2.Y), aMaxY = Math.Max(a1.Y, a2.Y);

                for (int j = 0; j < pb.Count; j++)
                {
                    MyPoint b1 = pb[j];
                    MyPoint b2 = pb[(j + 1) % pb.Count];

                    if (Math.Min(b1.X, b2.X) > aMaxX || Math.Max(b1.X, b2.X) < aMinX
                        || Math.Min(b1.Y, b2.Y) > aMaxY || Math.Max(b1.Y, b2.Y) < aMinY)
                    {
                        continue;
                    }

                    if (Crosses(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// A proper crossing, where the two segments pass through each other. Two edges that
        /// merely touch or lie along each other are not counted, because two slabs drawn to the
        /// same gridline share an edge without either reaching into the other - and that shared
        /// edge is the commonest arrangement in the whole model.
        /// </summary>
        private static bool Crosses(MyPoint p1, MyPoint p2, MyPoint q1, MyPoint q2)
        {
            double d1 = Side(q1, q2, p1);
            double d2 = Side(q1, q2, p2);
            double d3 = Side(p1, p2, q1);
            double d4 = Side(p1, p2, q2);

            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        /// <summary>
        /// Which side of a b the point p falls, with anything within a square millimetre of
        /// moment counted as on the line. Two outlines meant to coincide rarely land on exactly
        /// the same coordinate, and without that slack every shared edge would read as a
        /// crossing and the whole point of this would be lost.
        /// </summary>
        private static double Side(MyPoint a, MyPoint b, MyPoint p)
        {
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            return Math.Abs(cross) < 1d ? 0 : cross;
        }
    }
}
