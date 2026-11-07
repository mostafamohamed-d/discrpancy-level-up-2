using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Gathers the ETABS wall panels that stand on the same line of wall on the same story, so
    /// the plan can draw them as the one wall they are.
    ///
    /// A wall is meshed in both directions. Along its length that gives panels side by side,
    /// and those are different pieces of wall which belong on the plan separately. Up its
    /// height it gives panels stacked one above another, and in plan those land on top of each
    /// other exactly: three identical rectangles, indistinguishable from one, the last drawn
    /// hiding the colour of the ones under it and only the first of them selectable. Stacked
    /// panels are what this folds together.
    ///
    /// Only the drawing is folded. Every panel keeps its own verdict; what the folded wall
    /// carries is the worst of them and the names of all of them, so nothing is lost and a
    /// failure cannot be buried under a pass drawn later.
    ///
    /// The grouping is by plan line to the millimetre or so, which is what stacked panels
    /// share and neighbouring panels do not. A wall meshed unevenly - panels stacked but cut
    /// to different lengths - has different lines and is left drawn as it was modelled, which
    /// is the honest answer for a wall that really is in pieces.
    /// </summary>
    public static class WallStacks
    {
        /// <summary>
        /// How near two panels' ends have to be, in mm, to count as the same line of wall.
        /// Stacked panels share their corner joints exactly, so this is a guard against
        /// arithmetic rather than a tolerance anything depends on.
        /// </summary>
        private const double SamePlaceInMM = 10;

        /// <summary>The checked panels, gathered into the walls they stand on.</summary>
        public static List<List<PairedWall>> Group(IEnumerable<PairedWall> walls)
        {
            return Gather(walls, wall => Key(wall.Story, wall.etabsWall.PlanStart, wall.etabsWall.PlanEnd,
                                             wall.revitWall.RevitID.ToString()));
        }

        /// <summary>The ETABS panels on their own, gathered the same way.</summary>
        public static List<List<Wall_E>> Group(IEnumerable<Wall_E> walls)
        {
            return Gather(walls, wall => Key(wall.Story, wall.PlanStart, wall.PlanEnd, ""));
        }

        /// <summary>
        /// Groups in the order the first member of each group was met, so the plan is drawn in
        /// the order the check produced rather than in whatever order a hash table happens to
        /// hand back.
        /// </summary>
        private static List<List<T>> Gather<T>(IEnumerable<T> items, Func<T, string> keyOf)
        {
            Dictionary<string, List<T>> byKey = new Dictionary<string, List<T>>(StringComparer.Ordinal);
            List<List<T>> inOrder = new List<List<T>>();

            foreach (T item in items)
            {
                string key = keyOf(item);

                List<T> group;
                if (!byKey.TryGetValue(key, out group))
                {
                    group = new List<T>();
                    byKey[key] = group;
                    inOrder.Add(group);
                }

                group.Add(item);
            }

            return inOrder;
        }

        /// <summary>
        /// One line of wall on one story, as a string that two stacked panels agree on.
        ///
        /// The two ends are put in a fixed order first, so a panel drawn from the other end
        /// still lands in the same group - a wall has no near end and no far end, and ETABS
        /// does not promise to hand the corners over the same way round every time.
        ///
        /// Paired panels carry the Revit wall they were checked against as well. Two panels
        /// that agree in plan but were paired with different Revit walls are two results about
        /// two walls, and folding them would leave one of the Revit walls with nothing drawn.
        /// </summary>
        private static string Key(string story, MyPoint start, MyPoint end, string pairedWith)
        {
            long x1 = Rounded(start == null ? 0 : start.X);
            long y1 = Rounded(start == null ? 0 : start.Y);
            long x2 = Rounded(end == null ? 0 : end.X);
            long y2 = Rounded(end == null ? 0 : end.Y);

            bool inOrder = x1 < x2 || (x1 == x2 && y1 <= y2);

            return string.Join("|", (story ?? "").ToUpperInvariant(), pairedWith,
                               inOrder ? x1 : x2, inOrder ? y1 : y2,
                               inOrder ? x2 : x1, inOrder ? y2 : y1);
        }

        private static long Rounded(double value)
        {
            return (long)Math.Round(value / SamePlaceInMM);
        }
    }
}
