using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What the loading plan says the slab carries at any point, answered by asking the
    /// regions that cover the point.
    ///
    /// The same shape as <see cref="SlabCoverage"/>, and for the same reason: the regions are
    /// left exactly as they were drawn and nothing is cut against anything, because nothing
    /// ever asks where the seam between two of them is. It only asks what is at a point.
    ///
    /// Where two regions overlap the smaller one answers, because that is what a plan drawn
    /// that way means - a small patch drawn over a large one is the exception to it - and the
    /// overlap is flagged so the answer can be put up for review rather than stated.
    /// </summary>
    public sealed class LoadCoverage
    {
        /// <summary>One filled region, whole, with the loads its number looks up.</summary>
        public sealed class Region
        {
            public PlanPolygon Plan;
            public long RevitId;
            public string Number = "";
            public string NumberText = "";

            /// <summary>True when the number was found in the loading schedule.</summary>
            public bool InSchedule;

            public LoadSet Loads;
            public string Description = "";
        }

        /// <summary>What the loading plan says at one point.</summary>
        public struct Reading
        {
            /// <summary>False when no loading region covers the point at all.</summary>
            public bool Covered;

            /// <summary>The region that answered, which is the smallest one covering the point.</summary>
            public long RevitId;

            public string Number;
            public string NumberText;

            /// <summary>True when that number was found in the loading schedule.</summary>
            public bool InSchedule;

            public LoadSet Loads;
            public string Description;

            /// <summary>How many regions cover the point, the one that answered included.</summary>
            public int CoveringCount;

            /// <summary>
            /// True when more than one region covers the point and they do not agree on the
            /// number, which is a loading plan that has to be looked at rather than a load
            /// that is right or wrong.
            /// </summary>
            public bool Conflicting;

            /// <summary>
            /// The other regions here, named by element id as well as by number, for the note
            /// on the zone. An id is what makes a region findable in Revit; a number on its own
            /// is not, because the same number is drawn all over the plan.
            /// </summary>
            public string Others;

            /// <summary>
            /// The ids of those other regions, so two pieces of slab in conflict with
            /// different regions are not merged into one zone carrying one of the two notes.
            /// </summary>
            public string ConflictKey;
        }

        private readonly List<Region> regions = new List<Region>();
        private readonly Dictionary<long, List<Region>> grid = new Dictionary<long, List<Region>>();

        /// <summary>Bucket size in mm, the same as the slab coverage uses.</summary>
        private const double CellSize = 2000d;

        public IList<Region> Regions { get { return regions; } }

        /// <summary>True when there is nothing here to check against, so the load check is off.</summary>
        public bool IsEmpty { get { return regions.Count == 0; } }

        public void Add(Region region)
        {
            if (region == null || region.Plan == null)
            {
                return;
            }

            regions.Add(region);

            PlanBounds bounds = region.Plan.Bounds;

            for (long x = Cell(bounds.MinX); x <= Cell(bounds.MaxX); x++)
            {
                for (long y = Cell(bounds.MinY); y <= Cell(bounds.MaxY); y++)
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
        /// Builds the coverage from the regions read off a loading plan view and the schedule
        /// their numbers are looked up in. A region whose number the schedule does not have is
        /// still added, carrying no loads and marked as not in the schedule, because a number
        /// on the plan that the schedule never defined is worth reporting rather than dropping.
        /// </summary>
        public static LoadCoverage Build(IEnumerable<LoadRegion_R> loadRegions, LoadSchedule schedule,
                                         IList<string> issues)
        {
            LoadCoverage coverage = new LoadCoverage();

            if (loadRegions == null)
            {
                return coverage;
            }

            Dictionary<string, string> missingNumbers = new Dictionary<string, string>();

            foreach (LoadRegion_R region in loadRegions)
            {
                if (region == null)
                {
                    continue;
                }

                LoadScheduleEntry entry = region.HasNumber && schedule != null
                    ? schedule.Find(region.Number)
                    : null;

                if (region.HasNumber && entry == null)
                {
                    missingNumbers[LoadLabel.Key(region.Number)] = region.Number;
                }

                foreach (PlanPolygon part in region.Parts)
                {
                    coverage.Add(new Region
                    {
                        Plan = part,
                        RevitId = region.RevitID,
                        Number = region.Number,
                        NumberText = region.NumberText,
                        InSchedule = entry != null,
                        Loads = entry == null ? new LoadSet() : entry.Loads,
                        Description = entry == null ? "" : entry.Description
                    });
                }
            }

            if (issues != null)
            {
                if (missingNumbers.Count > 0)
                {
                    issues.Add($"The loading plan uses {missingNumbers.Count} number(s) the schedule sheet " +
                               $"does not define: {string.Join(", ", missingNumbers.Values.OrderBy(n => n, LoadLabel.Order))}. " +
                               "Either the wrong sheet is picked or the plan and the schedule have " +
                               "drifted apart.");
                }
            }

            return coverage;
        }

        /// <summary>
        /// What the loading plan says at one point. The smallest region covering it answers,
        /// so a patch drawn over a larger area is the one that counts, and a point that two
        /// regions with different numbers both claim says so.
        /// </summary>
        public Reading At(double x, double y)
        {
            Reading reading = new Reading
            {
                Number = "",
                NumberText = "",
                Description = "",
                Others = "",
                ConflictKey = ""
            };

            List<Region> bucket;
            if (!grid.TryGetValue(Key(Cell(x), Cell(y)), out bucket))
            {
                return reading;
            }

            Region winner = null;
            List<Region> others = null;

            for (int i = 0; i < bucket.Count; i++)
            {
                Region region = bucket[i];
                if (!region.Plan.Bounds.Contains(x, y) || !region.Plan.Contains(x, y))
                {
                    continue;
                }

                reading.CoveringCount++;

                if (winner == null || region.Plan.Area < winner.Plan.Area)
                {
                    if (winner != null)
                    {
                        (others ?? (others = new List<Region>())).Add(winner);
                    }
                    winner = region;
                }
                else
                {
                    (others ?? (others = new List<Region>())).Add(region);
                }
            }

            if (winner == null)
            {
                return reading;
            }

            reading.Covered = true;
            reading.RevitId = winner.RevitId;
            reading.Number = winner.Number;
            reading.NumberText = winner.NumberText;
            reading.InSchedule = winner.InSchedule;
            reading.Loads = winner.Loads;
            reading.Description = winner.Description;

            if (others != null)
            {
                // By element id, because one region drawn as several islands can put more than
                // one part over the same point and that is one region, not two.
                List<Region> different = others
                    .Where(region => !LoadLabel.Same(region.Number, winner.Number))
                    .GroupBy(region => region.RevitId)
                    .Select(group => group.First())
                    .OrderBy(region => region.Number, LoadLabel.Order)
                    .ThenBy(region => region.RevitId)
                    .ToList();

                if (different.Count > 0)
                {
                    reading.Conflicting = true;
                    reading.Others = string.Join(", ", different.Select(Describe));
                    reading.ConflictKey = string.Join("+", different.Select(region => region.RevitId));
                }
            }

            return reading;
        }

        /// <summary>
        /// How one point's answer is told from another's, for grouping the triangles an ETABS
        /// area is cut into. Two points belong to the same zone when the loading plan says the
        /// same thing about both.
        /// </summary>
        public static string KeyOf(Reading reading)
        {
            if (!reading.Covered)
            {
                return "-";
            }
            return LoadLabel.Key(reading.Number) + "|" + reading.RevitId
                   + (reading.Conflicting ? "|?" + reading.ConflictKey : "");
        }

        /// <summary>One region as it is named in a note: what it says, and where to find it.</summary>
        private static string Describe(Region region)
        {
            string number = region.Number.Length > 0
                ? "load " + region.Number
                : "no load number";

            return $"{number} on region {region.RevitId} " +
                   $"({Math.Round(region.Plan.Area / 1e6, 2)} m2)";
        }

        private static long Cell(double value)
        {
            return (long)Math.Floor(value / CellSize);
        }

        private static long Key(long x, long y)
        {
            return (x << 32) ^ (y & 0xFFFFFFFFL);
        }
    }
}
