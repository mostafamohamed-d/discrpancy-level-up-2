using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What a piece of slab actually carries when more than one shell is drawn over it.
    ///
    /// Both programs let two area objects sit one on top of the other, and both are load
    /// bearing the moment somebody assigns a load to each. SAFE models are drawn that way as a
    /// matter of course - the slab, and a second shell over part of it for the loads that only
    /// act there - and an ETABS drop panel is the same arrangement. Read one shell at a time,
    /// the piece of plan under both reports whichever shell happened to win the mesh, and the
    /// other shell's load is simply not counted. That is a load going missing in silence,
    /// which is the one failure this check exists to prevent.
    ///
    /// So they are added. A point under two shells carries the sum of what both put there,
    /// which is what the structure underneath it carries.
    ///
    /// Openings take no part. An opening is ETABS saying there is no slab here at all, so it
    /// carries nothing and adds nothing, and a load assigned to one would be a load on a hole.
    /// </summary>
    public static class StackedShells
    {
        /// <summary>
        /// Every uniform load acting at <paramref name="point"/>: the loads on the shell the
        /// piece was cut from, plus the loads on every other shell of the story drawn over
        /// that same point.
        ///
        /// Asked at a point rather than shell against shell because the mesh has already cut
        /// the shells apart, and a piece of one is only ever under the shells that cover that
        /// piece. A shell overlapping somewhere else on the floor is not over this and does
        /// not add to it.
        /// </summary>
        public static IList<AreaLoad_E> LoadsAt(Slab_E owner, IList<Slab_E> onStory, MyPoint point)
        {
            List<AreaLoad_E> loads = new List<AreaLoad_E>();

            if (owner != null && !owner.IsOpening && owner.Loads != null)
            {
                loads.AddRange(owner.Loads);
            }

            if (onStory == null || point == null)
            {
                return loads;
            }

            foreach (Slab_E other in onStory)
            {
                if (other == null || ReferenceEquals(other, owner) || other.IsOpening)
                {
                    continue;
                }

                // Same shell read twice - the story list and the face's own source - is one
                // shell, and adding it to itself would double a load nobody drew twice.
                if (owner != null && string.Equals(other.UniqueName, owner.UniqueName,
                                                   StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (other.Loads == null || other.Loads.Count == 0 || other.Plan == null)
                {
                    continue;
                }

                if (other.Plan.Bounds.Contains(point.X, point.Y) && other.Plan.Contains(point.X, point.Y))
                {
                    loads.AddRange(other.Loads);
                }
            }

            return loads;
        }

        /// <summary>
        /// How many shells of the story stand over a point, counting the one the piece was cut
        /// from. One is the ordinary case; more than one is worth saying on the zone, because a
        /// load twice what the plan asks for is nearly always two shells rather than a typo.
        /// </summary>
        public static int CountAt(Slab_E owner, IList<Slab_E> onStory, MyPoint point)
        {
            return ShellsAt(owner, onStory, point).Count;
        }

        /// <summary>
        /// Every shell of the story standing over a point, the one the piece was cut from first:
        /// the shells whose loads <see cref="LoadsAt"/> adds together there, named so a piece can
        /// say which of them it is carrying.
        /// </summary>
        public static IList<Slab_E> ShellsAt(Slab_E owner, IList<Slab_E> onStory, MyPoint point)
        {
            List<Slab_E> shells = new List<Slab_E>();
            if (owner != null && !owner.IsOpening)
            {
                shells.Add(owner);
            }

            if (onStory == null || point == null)
            {
                return shells;
            }

            foreach (Slab_E other in onStory)
            {
                if (other == null || ReferenceEquals(other, owner) || other.IsOpening || other.Plan == null)
                {
                    continue;
                }

                if (owner != null && string.Equals(other.UniqueName, owner.UniqueName,
                                                   StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (other.Plan.Bounds.Contains(point.X, point.Y) && other.Plan.Contains(point.X, point.Y))
                {
                    shells.Add(other);
                }
            }

            return shells;
        }
    }
}
