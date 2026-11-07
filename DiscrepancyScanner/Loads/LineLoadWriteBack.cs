using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One distributed load on a frame exactly as ETABS states it: in the model's own units, in
    /// its own direction and coordinate system, over a stretch given as fractions of the frame.
    ///
    /// Kept raw rather than turned into kN/m the way <see cref="LineLoad_E"/> is, because what
    /// is read here is written straight back: a load the write-back leaves alone has to go back
    /// in exactly as it came out, whatever direction or units it was assigned in.
    /// </summary>
    public sealed class FrameDistributedLoad
    {
        /// <summary>ETABS's code for a force per length, as against a moment per length.</summary>
        public const int ForceType = 1;

        /// <summary>ETABS's code for the gravity direction, in which a downward load is positive.</summary>
        public const int GravityDirection = 10;

        public FrameDistributedLoad(string pattern, int loadType, string coordinateSystem, int direction,
                                    double relativeFrom, double relativeTo, double valueFrom, double valueTo)
        {
            Pattern = pattern ?? "";
            LoadType = loadType;
            CoordinateSystem = string.IsNullOrEmpty(coordinateSystem) ? "Global" : coordinateSystem;
            Direction = direction;
            RelativeFrom = relativeFrom;
            RelativeTo = relativeTo;
            ValueFrom = valueFrom;
            ValueTo = valueTo;
        }

        public string Pattern { get; }

        /// <summary>1 for a force per length, 2 for a moment per length.</summary>
        public int LoadType { get; }

        public string CoordinateSystem { get; }

        public int Direction { get; }

        public double RelativeFrom { get; }

        public double RelativeTo { get; }

        /// <summary>The value at the start of the stretch, in model units and with ETABS's sign.</summary>
        public double ValueFrom { get; }

        public double ValueTo { get; }

        /// <summary>
        /// True for a force acting plainly downwards - the only loads the line load check reads,
        /// and so the only ones a write-back may take away. Anything else on the frame is put
        /// back as it was.
        /// </summary>
        public bool PressesDown
        {
            get
            {
                double sign;
                return LoadType == ForceType && LineLoad_E.IsDownwardDirection(Direction, out sign);
            }
        }

        /// <summary>The value partway along, read off the straight line between the two ends.</summary>
        public double ValueAt(double relative)
        {
            double span = RelativeTo - RelativeFrom;
            if (span <= 0)
            {
                return ValueFrom;
            }

            double k = Math.Max(0, Math.Min(1, (relative - RelativeFrom) / span));
            return ValueFrom + (ValueTo - ValueFrom) * k;
        }

        /// <summary>The same load over a shorter stretch of itself, a trapezoid cut where it is cut.</summary>
        public FrameDistributedLoad Cut(double from, double to)
        {
            return new FrameDistributedLoad(Pattern, LoadType, CoordinateSystem, Direction,
                                            from, to, ValueAt(from), ValueAt(to));
        }

        public override string ToString()
        {
            return $"{Pattern} dir {Direction} {ValueFrom}-{ValueTo} over {RelativeFrom}-{RelativeTo}";
        }
    }

    /// <summary>A stretch of a frame and the value, in kN/m, a write-back puts on it.</summary>
    public sealed class FrameLoadSegment
    {
        public FrameLoadSegment(double from, double to, double value)
        {
            From = from;
            To = to;
            Value = value;
        }

        /// <summary>Where it starts, as a fraction of the frame from its I end.</summary>
        public double From { get; }

        public double To { get; }

        /// <summary>The load, in kN/m, downwards.</summary>
        public double Value { get; }

        public override string ToString()
        {
            string over = $"{Math.Round(From * 100)}% to {Math.Round(To * 100)}%";
            return Value > 0 ? $"{LoadSet.Text(Value)} kN/m over {over}" : $"cleared over {over}";
        }
    }

    /// <summary>
    /// Working out what a frame should carry once a drawn line load is written back to it.
    ///
    /// A frame is written whole, from the plan: every drawn line on it is put on at its own value
    /// over its own stretch, never one load averaged over several, and where lines overlap they
    /// are added together, the way the check adds them - so the next check agrees with what was
    /// written. The downward loads it already carried under those lines in the patterns the
    /// check reads are taken away, so they are not counted on top; whatever it carries where no
    /// line is drawn, in any other pattern, or in any other direction, is left as it was.
    ///
    /// Kept apart from the API calls so every one of those decisions can be checked without a
    /// model open.
    /// </summary>
    public static class LineLoadWriteBack
    {
        /// <summary>
        /// What one frame is to carry: every stretch any drawn line covers on it, each with what
        /// the lines over that stretch ask for together. Empty when <paramref name="line"/> is
        /// not on the frame.
        ///
        /// Every line on the frame, not only the one being written, because writing one line on a
        /// frame that carries an averaged load would leave the average under the others - and an
        /// averaged line load is never right.
        ///
        /// A stretch ending closer than <paramref name="snapMM"/> to an end of the frame is run
        /// to the end, and two stretches closer than that to each other are run together - a
        /// drawn line stops a little short far more often than a load really does, and the sliver
        /// it would leave is shorter than anything the check reports. Every line on the frame is
        /// snapped alike. A gap closed that way comes back with a value of nought: the old load
        /// is taken off it and nothing is put on.
        /// </summary>
        public static List<FrameLoadSegment> SegmentsOn(string frameUniqueName, LineLoadResult line,
                                                        IEnumerable<LineLoadResult> everyLine, double snapMM)
        {
            List<FrameLoadSegment> segments = new List<FrameLoadSegment>();

            if (OnFrame(line, frameUniqueName, snapMM).Count == 0)
            {
                return segments;
            }

            List<double[]> all = LinesOn(frameUniqueName, line, everyLine)
                .SelectMany(other => OnFrame(other, frameUniqueName, snapMM))
                .OrderBy(span => span[0])
                .ToList();

            double frameLength = line.Placements.First(placement => string.Equals(
                placement.FrameUniqueName, frameUniqueName, StringComparison.OrdinalIgnoreCase)).FrameLength;
            double near = frameLength > 0 ? Math.Max(0, snapMM) / frameLength : 0;

            // The stretches any line covers, with the gaps too short to report closed.
            List<double[]> covered = new List<double[]>();
            foreach (double[] span in all)
            {
                double[] open = covered.Count == 0 ? null : covered[covered.Count - 1];
                if (open != null && span[0] <= open[1] + near)
                {
                    open[1] = Math.Max(open[1], span[1]);
                }
                else
                {
                    covered.Add(new[] { span[0], span[1] });
                }
            }

            foreach (double[] span in covered)
            {
                List<double> cuts = all.SelectMany(other => new[] { other[0], other[1] })
                                       .Where(at => at > span[0] && at < span[1])
                                       .Concat(new[] { span[0], span[1] })
                                       .Distinct()
                                       .OrderBy(at => at)
                                       .ToList();

                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    double from = cuts[i];
                    double to = cuts[i + 1];
                    if (to - from < 1e-6)
                    {
                        continue;
                    }

                    double middle = (from + to) / 2;
                    double value = all.Where(other => other[0] <= middle && other[1] >= middle)
                                      .Sum(other => other[2]);

                    FrameLoadSegment last = segments.Count == 0 ? null : segments[segments.Count - 1];
                    if (last != null && Math.Abs(last.To - from) < 1e-9 && Math.Abs(last.Value - value) < 1e-9)
                    {
                        segments[segments.Count - 1] = new FrameLoadSegment(last.From, to, value);
                    }
                    else
                    {
                        segments.Add(new FrameLoadSegment(from, to, value));
                    }
                }
            }

            return segments;
        }

        /// <summary>
        /// The drawn lines on a frame that have no value worked out - no number, a number the
        /// schedule does not define, no height. A frame is written whole, so one of these on it
        /// would have its stretch cleared and nothing put back; the write is refused instead.
        /// </summary>
        public static List<LineLoadResult> LinesWithoutValueOn(string frameUniqueName, LineLoadResult line,
                                                               IEnumerable<LineLoadResult> everyLine)
        {
            return LinesOn(frameUniqueName, line, everyLine)
                .Where(other => other.Placements.Any(placement =>
                    placement.Value <= 0
                    && string.Equals(placement.FrameUniqueName, frameUniqueName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>Every line placed on a frame, each once, the one being written first.</summary>
        private static List<LineLoadResult> LinesOn(string frameUniqueName, LineLoadResult line,
                                                    IEnumerable<LineLoadResult> everyLine)
        {
            List<LineLoadResult> lines = (everyLine ?? Enumerable.Empty<LineLoadResult>())
                .Where(other => other != null && !ReferenceEquals(other, line))
                .Distinct()
                .Where(other => other.Placements.Any(placement => string.Equals(
                    placement.FrameUniqueName, frameUniqueName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            lines.Insert(0, line);
            return lines;
        }

        /// <summary>One line's stretches on one frame, snapped to its ends, each with the line's value.</summary>
        private static List<double[]> OnFrame(LineLoadResult line, string frameUniqueName, double snapMM)
        {
            return line.Placements
                .Where(placement => string.Equals(placement.FrameUniqueName, frameUniqueName,
                                                  StringComparison.OrdinalIgnoreCase))
                .Select(placement =>
                {
                    double near = placement.FrameLength > 0 ? Math.Max(0, snapMM) / placement.FrameLength : 0;
                    double from = placement.RelativeFrom <= near ? 0 : placement.RelativeFrom;
                    double to = placement.RelativeTo >= 1 - near ? 1 : placement.RelativeTo;
                    return new[] { from, to, placement.Value };
                })
                .Where(span => span[1] > span[0])
                .ToList();
        }

        /// <summary>
        /// The load pattern a write-back assigns to: the pattern the check reads that already
        /// carries most of the frame's downward load, so a frame is corrected in the pattern it
        /// was loaded in - and failing that, the first pattern in the model the settings name,
        /// a name typed whole before one found inside a longer name. Null, with the reason, when
        /// the model has no pattern the check reads.
        /// </summary>
        public static string PatternToWrite(IList<FrameDistributedLoad> existing, IEnumerable<string> modelPatterns,
                                            LineLoadPatterns patterns, out string problem)
        {
            problem = null;

            string carrying = (existing ?? new List<FrameDistributedLoad>())
                .Where(load => load.PressesDown && patterns.Matches(load.Pattern))
                .GroupBy(load => load.Pattern, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Sum(load => Math.Abs(load.ValueFrom + load.ValueTo)
                                                              * (load.RelativeTo - load.RelativeFrom)))
                .Select(group => group.First().Pattern)
                .FirstOrDefault();

            if (carrying != null)
            {
                return carrying;
            }

            List<string> inModel = (modelPatterns ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (patterns.IsEmpty)
            {
                problem = "no load pattern is named for this check in the settings, so there is no saying "
                          + "which pattern the load belongs in. Name one under Settings and try again.";
                return null;
            }

            foreach (string named in patterns.Names)
            {
                string whole = inModel.FirstOrDefault(name => LineLoadPatterns.Key(name) == LineLoadPatterns.Key(named));
                if (whole != null && patterns.Matches(whole))
                {
                    return whole;
                }
            }

            string found = inModel.FirstOrDefault(patterns.Matches);
            if (found == null)
            {
                problem = "the model has no load pattern this check reads (" + patterns.Description
                          + "). Add the pattern to the model, or name one it has under Settings.";
            }

            return found;
        }

        /// <summary>
        /// What each load pattern touched should hold on the frame afterwards, as the loads to
        /// assign in it - in full, because ETABS is told to replace what the pattern had.
        ///
        /// The pattern written to gets the segments carrying a value, as uniform gravity loads - a
        /// segment of nought is only cleared. It and every other
        /// pattern the check reads lose their downward loads over the segments, cut where the
        /// segments start and end so the rest of each is kept. Every other load in those
        /// patterns goes back unchanged. A pattern with nothing over the segments is not touched
        /// at all, and is not in what comes back.
        /// </summary>
        /// <param name="modelUnitsPerKilonewtonMetre">What 1 kN/m is in the model's own units.</param>
        public static Dictionary<string, List<FrameDistributedLoad>> Rewrite(
            IList<FrameDistributedLoad> existing, string pattern, LineLoadPatterns patterns,
            IList<FrameLoadSegment> segments, double modelUnitsPerKilonewtonMetre)
        {
            existing = existing ?? new List<FrameDistributedLoad>();
            List<double[]> ranges = segments.Select(segment => new[] { segment.From, segment.To }).ToList();

            Dictionary<string, List<FrameDistributedLoad>> rewritten =
                new Dictionary<string, List<FrameDistributedLoad>>(StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> touched = existing
                .Where(load => load.PressesDown && patterns.Matches(load.Pattern)
                               && ranges.Any(range => load.RelativeFrom < range[1] && load.RelativeTo > range[0]))
                .Select(load => load.Pattern)
                .Concat(new[] { pattern })
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string name in touched)
            {
                List<FrameDistributedLoad> kept = new List<FrameDistributedLoad>();

                foreach (FrameDistributedLoad load in existing.Where(load =>
                             string.Equals(load.Pattern, name, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!load.PressesDown)
                    {
                        kept.Add(load);
                        continue;
                    }

                    foreach (double[] left in Subtract(load.RelativeFrom, load.RelativeTo, ranges))
                    {
                        kept.Add(load.Cut(left[0], left[1]));
                    }
                }

                if (string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    // A gap between lines closed by the snap is cleared and left empty.
                    foreach (FrameLoadSegment segment in segments.Where(segment => segment.Value > 0))
                    {
                        double value = segment.Value * modelUnitsPerKilonewtonMetre;
                        kept.Add(new FrameDistributedLoad(pattern, FrameDistributedLoad.ForceType, "Global",
                                                          FrameDistributedLoad.GravityDirection,
                                                          segment.From, segment.To, value, value));
                    }
                }

                rewritten[name] = kept.OrderBy(load => load.RelativeFrom).ToList();
            }

            return rewritten;
        }

        /// <summary>What is left of one stretch once the ranges are taken out of it.</summary>
        private static List<double[]> Subtract(double from, double to, List<double[]> ranges)
        {
            List<double[]> left = new List<double[]> { new[] { from, to } };

            foreach (double[] range in ranges)
            {
                List<double[]> next = new List<double[]>();
                foreach (double[] piece in left)
                {
                    if (range[1] <= piece[0] || range[0] >= piece[1])
                    {
                        next.Add(piece);
                        continue;
                    }
                    if (range[0] > piece[0])
                    {
                        next.Add(new[] { piece[0], range[0] });
                    }
                    if (range[1] < piece[1])
                    {
                        next.Add(new[] { range[1], piece[1] });
                    }
                }
                left = next;
            }

            return left.Where(piece => piece[1] - piece[0] > 1e-6).ToList();
        }
    }
}
