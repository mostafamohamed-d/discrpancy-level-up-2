using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// How thick the Revit slab is at any point on plan, answered by asking the floors that
    /// cover the point rather than by cutting them against each other.
    ///
    /// This is SRV's step three, and its rule: the thickest floor covering the point wins.
    /// Floors are alternatives, not layers. A drop panel drawn as a second floor is the whole
    /// section at that spot - the built up depth ETABS is drawn to - and the slab it overlaps
    /// simply runs on underneath it.
    ///
    /// It replaces a depth field that had to decide, with the geometry kernel, exactly where
    /// one floor ended and the next began: a boolean per pair of floors, and on a real basement
    /// most of the time the whole check spent. That work was never needed. Regions are free to
    /// overlap here, and that is the point - two floors over the same ground do not have to be
    /// cut apart, because nothing asks where the seam is.
    /// </summary>
    public sealed class SlabCoverage
    {
        /// <summary>One Revit floor and the concrete it declares. Whole, uncut, as modelled.</summary>
        public sealed class Region
        {
            public PlanPolygon Plan;
            /// <summary>The concrete this floor declares, in mm.</summary>
            public double Thickness;
            public bool Variable;
            public long RevitId;
            public string TypeName = "";
            public double TopElevation;
            public double BottomElevation;
            /// <summary>Where the floor was read from - Solid or Sketch. See <see cref="Slab_R.GeometrySource"/>.</summary>
            public string Source = Slab_R.FromSolid;
            /// <summary>Read off its sketch, with cuts the sketch could not show - see <see cref="Slab_R.OpeningsUncertain"/>.</summary>
            public bool OpeningsUncertain;

            /// <summary>
            /// True when this floor's top sits at the story plane, which is what an ETABS area
            /// is drawn to. The floors below it are only here so a drop panel hung under the
            /// slab is still seen; a bare patch of one of those is a panel reaching past the
            /// slab over it, not a floor the ETABS model forgot, so only floors at the plane
            /// report as missing.
            /// </summary>
            public bool AtStoryPlane;
        }

        /// <summary>What the Revit model says the slab is at one point.</summary>
        public struct Reading
        {
            /// <summary>False when no floor covers the point at all.</summary>
            public bool Covered;
            /// <summary>The thickest floor here, in mm - what the slab is taken to be.</summary>
            public double Thickness;
            /// <summary>True when that floor is sloped or shape edited.</summary>
            public bool Variable;
            /// <summary>The floor to select in Revit, which is the thickest one here.</summary>
            public long PrimaryRevitId;
            public string TypeName;
            /// <summary>Where that floor was read from - Solid or Sketch.</summary>
            public string Source;
            /// <summary>True when more than one floor covers the point, which reads as a drop.</summary>
            public bool Stacked;
            /// <summary>How many floors cover the point, thickest included.</summary>
            public int CoveringCount;
            /// <summary>The thickest of the floors that did not win, in mm. Zero when it stands alone.</summary>
            public double NextThickness;
            /// <summary>The floor that thickness came from, or null when it stands alone.</summary>
            public Region Next;
            public double TopElevation;
            public double BottomElevation;
        }

        private readonly List<Region> regions = new List<Region>();

        /// <summary>
        /// Regions bucketed by plan position, so a point asks only the few that could possibly
        /// cover it rather than every floor on the story. Without this the point test is linear
        /// in the model and the saving over the old walk is thrown away again.
        /// </summary>
        private readonly Dictionary<long, List<Region>> grid = new Dictionary<long, List<Region>>();

        /// <summary>Bucket size in mm; a couple of metres puts a few regions in each.</summary>
        private const double CellSize = 2000d;

        public IList<Region> Regions { get { return regions; } }

        /// <summary>
        /// Add up floors stacked face to face, for thicknesses measured off the solid.
        ///
        /// Measured, a drop panel drawn hung under the slab is only the part that hangs - 110
        /// under a 240 slab - and the thickest of the two would read the slab, 240, where the
        /// concrete is 350 deep. Here the floors touching one another at the point are taken as
        /// one depth, from the top of the highest to the bottom of the lowest. Off, as it is
        /// for thicknesses read from the type, which already give a drop its whole depth.
        /// </summary>
        public bool AddStackedDepths { get; set; }

        /// <summary>Two floors this close in height are one stack of concrete, in mm.</summary>
        private const double StackGap = 10;

        public void Add(Region region)
        {
            if (region == null || region.Plan == null)
            {
                return;
            }

            regions.Add(region);

            PlanBounds bounds = region.Plan.Bounds;
            long minX = Cell(bounds.MinX), maxX = Cell(bounds.MaxX);
            long minY = Cell(bounds.MinY), maxY = Cell(bounds.MaxY);

            for (long x = minX; x <= maxX; x++)
            {
                for (long y = minY; y <= maxY; y++)
                {
                    long key = Key(x, y);
                    List<Region> bucket;
                    if (!grid.TryGetValue(key, out bucket))
                    {
                        bucket = new List<Region>();
                        grid[key] = bucket;
                    }
                    bucket.Add(region);
                }
            }
        }

        /// <summary>
        /// The slab at one point: whether there is any floor there at all, how thick the
        /// thickest one is, and which floor that was.
        ///
        /// Thickest rather than topmost, so a drop panel modelled as a second floor reads as
        /// the built up section whichever way round it was drawn - hung under the slab or laid
        /// flush with its top. It also picks the structural slab over an architectural finish
        /// that happens to overlap it.
        ///
        /// <paramref name="atStoryPlaneOnly"/> leaves out the floors hung below the plane, for
        /// the one question that is about the slab at the plane alone: which floor there owns
        /// a patch no ETABS shell covers.
        /// </summary>
        public Reading At(double x, double y, bool atStoryPlaneOnly = false)
        {
            Reading reading = new Reading { TypeName = "" };
            Region winner = null;
            List<Region> covering = AddStackedDepths ? new List<Region>() : null;

            List<Region> bucket;
            if (!grid.TryGetValue(Key(Cell(x), Cell(y)), out bucket))
            {
                return reading;
            }

            for (int i = 0; i < bucket.Count; i++)
            {
                Region region = bucket[i];
                if (atStoryPlaneOnly && !region.AtStoryPlane)
                {
                    continue;
                }
                if (!region.Plan.Bounds.Contains(x, y) || !region.Plan.Contains(x, y))
                {
                    continue;
                }

                if (!reading.Covered || region.Thickness > reading.Thickness)
                {
                    // What this one displaces is worth keeping: a spot where two floors differ
                    // is where a drop panel sits, and the loser is what the slab is beside it.
                    if (reading.Covered)
                    {
                        reading.NextThickness = reading.Thickness;
                        reading.Next = winner;
                    }

                    winner = region;
                    reading.Covered = true;
                    reading.Thickness = region.Thickness;
                    reading.Variable = region.Variable;
                    reading.PrimaryRevitId = region.RevitId;
                    reading.TypeName = region.TypeName;
                    reading.Source = region.Source;
                    reading.TopElevation = region.TopElevation;
                    reading.BottomElevation = region.BottomElevation;
                }
                else if (region.Thickness > reading.NextThickness || reading.Next == null)
                {
                    reading.NextThickness = region.Thickness;
                    reading.Next = region;
                }

                reading.CoveringCount++;
                covering?.Add(region);
            }

            reading.Stacked = reading.CoveringCount > 1;
            if (covering != null && covering.Count > 1)
            {
                StackDepth(covering, ref reading);
            }
            return reading;
        }

        /// <summary>
        /// The depth of the stack the highest floor here heads: it, and every floor below that
        /// meets it face to face, down to a gap. See <see cref="AddStackedDepths"/>.
        /// </summary>
        private static void StackDepth(List<Region> covering, ref Reading reading)
        {
            covering.Sort((a, b) => b.TopElevation.CompareTo(a.TopElevation));
            Region head = covering[0];
            double top = head.TopElevation;
            double bottom = head.BottomElevation;

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Region region in covering)
                {
                    if (region.BottomElevation < bottom - 1e-6 && region.TopElevation >= bottom - StackGap)
                    {
                        bottom = region.BottomElevation;
                        grew = true;
                    }
                }
            }

            if (top - bottom > reading.Thickness)
            {
                reading.Thickness = top - bottom;
                reading.PrimaryRevitId = head.RevitId;
                reading.TypeName = head.TypeName;
                reading.Source = head.Source;
                reading.Variable = head.Variable;
                reading.TopElevation = top;
                reading.BottomElevation = bottom;
            }
        }

        /// <summary>
        /// True where a point lies inside the outline of a floor at the story plane but in one of
        /// its holes, and no floor covers it: an opening in the Revit floor, as opposed to a
        /// patch past its edge.
        /// </summary>
        public bool InAHole(double x, double y)
        {
            List<Region> bucket;
            if (!grid.TryGetValue(Key(Cell(x), Cell(y)), out bucket))
            {
                return false;
            }

            bool insideAnOutline = false;
            for (int i = 0; i < bucket.Count; i++)
            {
                Region region = bucket[i];
                if (!region.Plan.Bounds.Contains(x, y))
                {
                    continue;
                }
                if (region.Plan.Contains(x, y))
                {
                    return false;
                }
                if (region.AtStoryPlane && region.Plan.Outer.Contains(x, y))
                {
                    insideAnOutline = true;
                }
            }
            return insideAnOutline;
        }

        private static long Cell(double value)
        {
            return (long)Math.Floor(value / CellSize);
        }

        private static long Key(long x, long y)
        {
            // Two cell indices in one key. The building is nowhere near wide enough for the
            // halves to run into each other.
            return (x << 32) ^ (y & 0xFFFFFFFFL);
        }
    }
}
