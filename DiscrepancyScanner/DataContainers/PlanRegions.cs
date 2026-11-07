using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Turning the rings of a drafted region - a filled region off a loading plan - into
    /// outlines with their openings.
    ///
    /// Sorting rings by winding is the right thing to do when the winding can be trusted, which
    /// it can for a face taken off a solid: the model says which way the face points and the
    /// outline comes back counter clockwise with its holes wound against it.
    ///
    /// A drafted region is not that, and it is worse than it looks. A filled region is a
    /// sketch, and its loops come back wound however each of them happened to be drawn. It
    /// used to be assumed here that at least the relative winding held - that a hole would
    /// always be wound against the outline it sits in, because that is what makes it a hole -
    /// and a real loading plan says otherwise: one region on it came back with ten of its
    /// openings wound against its outlines and five wound with them. Sorted by winding, those
    /// five stopped being openings and became solid pieces of region sitting on top of the
    /// region they were cut out of, so the loading plan claimed the ground inside them and
    /// every patch drawn in one of those openings was reported as an overlap.
    ///
    /// So winding is not consulted at all here. What makes a ring a hole is that it sits
    /// inside another ring, which is a fact about the shape rather than about how it was
    /// drawn: a ring inside nothing is an outline, a ring inside one outline is an opening in
    /// it, and a ring inside an opening is an island in that opening and an outline again.
    /// </summary>
    internal static class PlanRegions
    {
        /// <summary>
        /// The regions a drafted sketch's rings describe: every ring that is not inside
        /// anything, carrying the rings that sit directly inside it as its openings.
        ///
        /// Rings that cannot enclose an area never get this far, and a region the winding
        /// would have thrown away comes back whole, because nothing is thrown away for being
        /// wound the wrong way round.
        /// </summary>
        public static IList<PlanPolygon> FromDraftedRings(IList<PlanRing> rings)
        {
            List<PlanPolygon> regions = new List<PlanPolygon>();

            if (rings == null)
            {
                return regions;
            }

            List<PlanRing> usable = rings.Where(ring => ring != null).ToList();
            if (usable.Count == 0)
            {
                return regions;
            }

            // How many rings each ring sits inside. Even is an outline - nothing, or an island
            // in an opening - and odd is an opening in the ring immediately around it.
            List<List<int>> around = new List<List<int>>();

            for (int i = 0; i < usable.Count; i++)
            {
                List<int> containers = new List<int>();

                for (int j = 0; j < usable.Count; j++)
                {
                    if (j != i && Inside(usable[i], usable[j]))
                    {
                        containers.Add(j);
                    }
                }

                around.Add(containers);
            }

            Dictionary<int, List<PlanRing>> openings = new Dictionary<int, List<PlanRing>>();

            for (int i = 0; i < usable.Count; i++)
            {
                if (around[i].Count % 2 == 0)
                {
                    continue;
                }

                // The ring immediately around it is the one that most others are also around,
                // which is to say the innermost of them.
                int parent = around[i].OrderByDescending(index => around[index].Count).First();

                List<PlanRing> holes;
                if (!openings.TryGetValue(parent, out holes))
                {
                    holes = new List<PlanRing>();
                    openings[parent] = holes;
                }
                holes.Add(usable[i]);
            }

            for (int i = 0; i < usable.Count; i++)
            {
                if (around[i].Count % 2 != 0)
                {
                    continue;
                }

                List<PlanRing> holes;
                PlanPolygon region = PlanPolygon.Create(
                    usable[i], openings.TryGetValue(i, out holes) ? holes : null);

                if (region != null)
                {
                    regions.Add(region);
                }
            }

            return regions;
        }

        /// <summary>
        /// True when <paramref name="ring"/> sits inside <paramref name="other"/>.
        ///
        /// The rings of one sketch cannot cross, only nest, so this is decided off the ring's
        /// own outline rather than off a point in the middle of it - the middle of a region
        /// with an opening in it is inside the opening, which would have every outline reading
        /// as though it sat inside its own hole.
        ///
        /// Several points are asked rather than one, and the answer is the one most of them
        /// give, because an opening drawn onto the outline it cuts shares vertices with it and
        /// which side of the boundary a shared vertex falls is a coin toss.
        /// </summary>
        private static bool Inside(PlanRing ring, PlanRing other)
        {
            const double tolerance = PlanRing.PointTolerance;

            PlanBounds mine = ring.Bounds;
            PlanBounds theirs = other.Bounds;

            if (mine.MinX < theirs.MinX - tolerance || mine.MaxX > theirs.MaxX + tolerance
                || mine.MinY < theirs.MinY - tolerance || mine.MaxY > theirs.MaxY + tolerance)
            {
                return false;
            }

            const int most = 8;

            IList<MyPoint> points = ring.Points;
            int step = Math.Max(1, points.Count / most);
            int yes = 0;
            int no = 0;

            for (int i = 0; i < points.Count; i += step)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];

                Ask(other, a.X, a.Y, ref yes, ref no);
                Ask(other, (a.X + b.X) / 2d, (a.Y + b.Y) / 2d, ref yes, ref no);
            }

            return yes > no;
        }

        private static void Ask(PlanRing ring, double x, double y, ref int yes, ref int no)
        {
            if (ring.Contains(x, y))
            {
                yes++;
            }
            else
            {
                no++;
            }
        }
    }
}
