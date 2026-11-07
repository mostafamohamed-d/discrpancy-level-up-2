using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A column or a wall standing in the slab, as the outline it takes up on plan.
    ///
    /// The concrete there is the column or the wall, not slab, so the slab check has nothing to
    /// say about it: a thickness read inside a column is the column's own stiff area or rigid
    /// zone against the hole the column cuts in the Revit floor, and either way the answer is
    /// noise. Read from the columns and walls both models already carry, so it does not depend
    /// on how either model chose to mark them in the slab.
    /// </summary>
    public sealed class SlabSupport
    {
        public PlanPolygon Plan;

        /// <summary>The story the column or wall rises to, which is how both models name it.</summary>
        public string Story = "";

        /// <summary>How it is named to the user: "column C12", "Revit wall 351204".</summary>
        public string Name = "";
    }

    /// <summary>
    /// The columns and walls of both models as <see cref="SlabSupport"/>s, and the questions the
    /// slab check asks of them.
    /// </summary>
    public static class SlabSupports
    {
        /// <summary>
        /// How far past a column or wall outline a piece may reach and still be read as inside
        /// it, in mm. The two models rarely draw a column to the same millimetre, and a sliver
        /// running round the outside of one is still the column.
        /// </summary>
        public const double Margin = 50;

        /// <summary>
        /// How much of a piece has to lie inside the columns and walls for the piece to be
        /// theirs. Not all of it: a piece cut along one model's column edge and read against the
        /// other model's column is a few percent out at the corners.
        /// </summary>
        public const double InsideFraction = 0.9;

        /// <summary>Sample points along each side of the box a piece is tested over.</summary>
        private const int SamplesPerSide = 24;

        /// <summary>Every column and wall in both models, as outlines.</summary>
        public static List<SlabSupport> From(IEnumerable<Column_e> etabsColumns, IEnumerable<Column_r> revitColumns,
                                             IEnumerable<Wall_E> etabsWalls, IEnumerable<Wall_R> revitWalls)
        {
            List<SlabSupport> supports = new List<SlabSupport>();

            foreach (Column_e column in etabsColumns ?? Enumerable.Empty<Column_e>())
            {
                Add(supports, column?.RectanglePoints, column?.Story, "ETABS column " + column?.EtabsUniqueName);
            }
            foreach (Column_r column in revitColumns ?? Enumerable.Empty<Column_r>())
            {
                Add(supports, column?.PlanOutline, column?.Story, "Revit column " + column?.RevitID);
            }
            foreach (Wall_E wall in etabsWalls ?? Enumerable.Empty<Wall_E>())
            {
                Add(supports, wall?.DrawingPoints, wall?.Story, "ETABS wall " + wall?.Identifier);
            }
            foreach (Wall_R wall in revitWalls ?? Enumerable.Empty<Wall_R>())
            {
                Add(supports, wall?.PlanOutline, wall?.Story, "Revit wall " + wall?.RevitID);
            }

            return supports;
        }

        private static void Add(List<SlabSupport> supports, PointF[] outline, string story, string name)
        {
            if (outline == null || outline.Length < 3 || string.IsNullOrWhiteSpace(story))
            {
                return;
            }

            PlanPolygon plan = PlanPolygon.FromPoints(outline.Select(point => new MyPoint(point.X, point.Y, 0)));
            if (plan == null || plan.Area <= 0)
            {
                return;
            }

            supports.Add(new SlabSupport { Plan = plan, Story = story.Trim(), Name = name });
        }

        /// <summary>
        /// The support a region lies inside, or null when it does not lie inside any. Inside
        /// means <see cref="InsideFraction"/> of it within <see cref="Margin"/> of the columns and
        /// walls taken together - a piece of slab crossing where a wall meets a column belongs
        /// to the two of them rather than to either - and the one named is the one holding most
        /// of it.
        /// </summary>
        public static SlabSupport Holding(PlanPolygon region, IList<SlabSupport> supports)
        {
            return Holding(region, supports, InsideFraction);
        }

        /// <summary>The same, with how much of the region has to lie inside said outright.</summary>
        public static SlabSupport Holding(PlanPolygon region, IList<SlabSupport> supports, double fraction)
        {
            if (region == null || supports == null || supports.Count == 0 || region.Area <= 0)
            {
                return null;
            }

            PlanBounds bounds = region.Bounds;
            List<SlabSupport> near = supports
                .Where(support => support.Plan.Bounds.IntersectsWith(bounds, Margin))
                .ToList();

            // Bigger than everything near it put together, so it cannot be inside: a floor-wide
            // slab passes every column on the level, and is let go here without a point tested.
            double room = near.Sum(support => (support.Plan.Bounds.Width + 2 * Margin)
                                              * (support.Plan.Bounds.Height + 2 * Margin));
            if (near.Count == 0 || region.Area * fraction > room)
            {
                return null;
            }

            int inRegion = 0;
            int inSupport = 0;
            Dictionary<SlabSupport, int> hits = new Dictionary<SlabSupport, int>();

            double stepX = bounds.Width / SamplesPerSide;
            double stepY = bounds.Height / SamplesPerSide;

            for (int i = 0; i < SamplesPerSide; i++)
            {
                double x = bounds.MinX + (i + 0.5) * stepX;
                for (int j = 0; j < SamplesPerSide; j++)
                {
                    double y = bounds.MinY + (j + 0.5) * stepY;
                    if (!region.Contains(x, y))
                    {
                        continue;
                    }

                    inRegion++;
                    SlabSupport at = SupportAt(near, x, y);
                    if (at != null)
                    {
                        inSupport++;
                        int count;
                        hits.TryGetValue(at, out count);
                        hits[at] = count + 1;
                    }
                }
            }

            if (inRegion == 0)
            {
                // Too thin for the grid to land in. One point properly inside it decides.
                MyPoint inside = region.InteriorPoint();
                return inside == null ? null : SupportAt(near, inside.X, inside.Y);
            }

            if (inSupport < fraction * inRegion)
            {
                return null;
            }

            return hits.OrderByDescending(hit => hit.Value).First().Key;
        }

        private static SlabSupport SupportAt(IList<SlabSupport> supports, double x, double y)
        {
            foreach (SlabSupport support in supports)
            {
                if (support.Plan.Bounds.Contains(x, y, Margin) && support.Plan.DistanceTo(x, y) <= Margin)
                {
                    return support;
                }
            }
            return null;
        }
    }
}
