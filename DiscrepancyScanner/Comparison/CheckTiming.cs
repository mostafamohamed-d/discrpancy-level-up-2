using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Where the slab check spent its time, and what it spent it on.
    ///
    /// The check is almost entirely geometry kernel work, and the kernel gives nothing back
    /// about its own cost, so the only way to know which part of a slow run is slow is to
    /// time the parts. Everything here is counters and stopwatches: it costs nothing worth
    /// measuring against the work it is measuring, so it is always on and always reported.
    /// </summary>
    public static class CheckTiming
    {
        private sealed class Phase
        {
            public long Milliseconds;
            public long Count;
        }

        private static readonly Dictionary<string, Phase> phases = new Dictionary<string, Phase>();
        private static readonly List<KeyValuePair<string, long>> stories = new List<KeyValuePair<string, long>>();
        private static Stopwatch overall;

        /// <summary>Prisms built, and how long that took.</summary>
        public static long SolidsBuilt;
        public static long SolidsAsked;
        public static long MillisecondsBuildingSolids;

        /// <summary>Answers reached without troubling the kernel at all.</summary>
        public static long SkippedAsDisjoint;
        public static long SkippedAsContained;

        /// <summary>Boolean operations asked of the kernel, and how they went.</summary>
        public static long BooleansTried;
        public static long BooleansRefusedFirstTry;
        public static long BooleansRefusedAfterResize;
        public static long BooleansWonByScalingUp;
        public static long BooleansLost;
        public static long MillisecondsInBooleans;

        public static void Reset()
        {
            phases.Clear();
            stories.Clear();
            overall = Stopwatch.StartNew();

            SolidsBuilt = 0;
            SolidsAsked = 0;
            MillisecondsBuildingSolids = 0;
            SkippedAsDisjoint = 0;
            SkippedAsContained = 0;
            BooleansTried = 0;
            BooleansRefusedFirstTry = 0;
            BooleansRefusedAfterResize = 0;
            BooleansWonByScalingUp = 0;
            BooleansLost = 0;
            MillisecondsInBooleans = 0;
        }

        /// <summary>Adds to a named phase. Use with <see cref="Stopwatch.StartNew"/>.</summary>
        public static void Add(string phase, Stopwatch watch)
        {
            if (watch == null)
            {
                return;
            }
            watch.Stop();
            Add(phase, watch.ElapsedMilliseconds);
        }

        public static void Add(string phase, long milliseconds)
        {
            Phase entry;
            if (!phases.TryGetValue(phase, out entry))
            {
                entry = new Phase();
                phases[phase] = entry;
            }
            entry.Milliseconds += milliseconds;
            entry.Count++;
        }

        /// <summary>Records how long one story took, so the worst can be named.</summary>
        public static void Story(string name, long milliseconds)
        {
            stories.Add(new KeyValuePair<string, long>(name, milliseconds));
        }

        /// <summary>
        /// The whole account, as lines meant to be read rather than parsed. Phases are listed
        /// heaviest first, because on a slow run the question is always which one it was.
        /// </summary>
        public static IList<string> Report()
        {
            List<string> lines = new List<string>();

            long total = overall == null ? 0 : overall.ElapsedMilliseconds;
            lines.Add($"Slab check took {Seconds(total)}.");

            if (phases.Count > 0)
            {
                lines.Add("Where the time went:");
                foreach (KeyValuePair<string, Phase> entry in
                         phases.OrderByDescending(p => p.Value.Milliseconds))
                {
                    double share = total <= 0 ? 0 : 100d * entry.Value.Milliseconds / total;
                    lines.Add($"    {Seconds(entry.Value.Milliseconds)}  ({Math.Round(share)}%)  " +
                              $"{entry.Key}, {entry.Value.Count} time(s)");
                }
            }

            lines.Add($"Geometry kernel: {BooleansTried} boolean(s) in " +
                      $"{Seconds(MillisecondsInBooleans)}; {SolidsBuilt} prism(s) built in " +
                      $"{Seconds(MillisecondsBuildingSolids)} out of {SolidsAsked} asked for " +
                      $"({Percent(SolidsAsked - SolidsBuilt, SolidsAsked)} from the cache).");

            lines.Add($"Answered without the kernel: {SkippedAsDisjoint} as not overlapping, " +
                      $"{SkippedAsContained} as one inside the other.");

            if (BooleansRefusedFirstTry > 0)
            {
                lines.Add($"    {BooleansRefusedFirstTry} refused at first ask, of which " +
                          $"{BooleansRefusedFirstTry - BooleansRefusedAfterResize} were won by " +
                          $"resizing the prism and {BooleansWonByScalingUp} by scaling up. " +
                          $"{BooleansLost} were lost for good.");
            }

            if (stories.Count > 0)
            {
                lines.Add("Slowest stories:");
                foreach (KeyValuePair<string, long> story in
                         stories.OrderByDescending(s => s.Value).Take(5))
                {
                    lines.Add($"    {Seconds(story.Value)}  {story.Key}");
                }
            }

            return lines;
        }

        private static string Percent(long part, long whole)
        {
            return whole <= 0 ? "0%" : Math.Round(100d * part / whole) + "%";
        }

        private static string Seconds(long milliseconds)
        {
            if (milliseconds < 1000)
            {
                return milliseconds + " ms";
            }
            if (milliseconds < 60000)
            {
                return Math.Round(milliseconds / 1000d, 1) + " s";
            }
            return $"{milliseconds / 60000} min {Math.Round(milliseconds % 60000 / 1000d)} s";
        }
    }
}
