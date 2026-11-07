using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One ETABS frame, and the ruler everything about it is measured against.
    ///
    /// Each frame is read on its own, never chained to the frames it meets end to end. ETABS
    /// splits a beam line wherever another frame lands on it, so what the drawing shows as one
    /// line load is often three frames in the model - and the drawn line is split to match,
    /// each part read against the frame under it (see LineLoadCompareEngine). Chaining them
    /// instead straightened a kinked beam line onto one axis and answered for three frames
    /// with one value, which is exactly what somebody checking one frame cannot use.
    ///
    /// Everything - the frame's own loads, the lines drawn over it - is projected onto its
    /// axis as a chainage in mm from one end, so several drawn lines on one frame land at
    /// different chainages and a load over part of its length is a shorter stretch of it.
    ///
    /// Still called a run, and still able to hold several frames, because everything that
    /// measures along it is written that way.
    /// </summary>
    public sealed class LineLoadRun : IPlanLine
    {
        private readonly List<LineLoadFrame_E> frames = new List<LineLoadFrame_E>();

        private LineLoadRun(string story)
        {
            Story = story ?? "";
        }

        public string Story { get; }

        /// <summary>One end of the run's axis, in plan.</summary>
        public MyPoint PlanStart { get; private set; }

        /// <summary>The other end of the run's axis.</summary>
        public MyPoint PlanEnd { get; private set; }

        /// <summary>The frames this run was chained from, in no particular order.</summary>
        public IList<LineLoadFrame_E> Frames { get { return frames; } }

        /// <summary>How long the run is along its axis, in mm.</summary>
        public double Length { get { return WallGeometry.PlanLength(PlanStart, PlanEnd); } }

        /// <summary>True when any frame on the run carries a downward distributed load.</summary>
        public bool HasGravityLoad { get { return frames.Any(frame => frame.HasGravityLoad); } }

        /// <summary>
        /// What the frames on this run carry, in words - "1.5 kN/m", or where it changes along
        /// the run, each value and where it is. Set by the check, which is what knows which load
        /// patterns are read; empty on a run it did not describe.
        /// </summary>
        public string Carries { get; internal set; } = "";

        /// <summary>How this run is named in a report: its frames, or the first few of them.</summary>
        public string Identifier
        {
            get
            {
                List<string> names = frames.Select(frame => frame.Identifier)
                                           .Where(name => name.Length > 0)
                                           .Distinct()
                                           .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                                           .ToList();

                return names.Count <= 3
                    ? string.Join(", ", names)
                    : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
            }
        }

        /// <summary>
        /// The frame that most of a stretch of this run lies on, by name. Empty when the run
        /// has no frames, which cannot happen to a run that was built from one.
        ///
        /// Most of it rather than the frame its start falls on, because a stretch commonly
        /// begins a few millimetres before a joint - it is cut where the drawing stops, not
        /// where ETABS split the beam - and naming it after the frame it barely touches would
        /// move the name every time the drawing moved a little.
        /// </summary>
        public string FrameMostlyUnder(MyPoint start, MyPoint end)
        {
            double from = Math.Min(ChainageOf(start), ChainageOf(end));
            double to = Math.Max(ChainageOf(start), ChainageOf(end));

            string best = "";
            double most = 0;

            foreach (LineLoadFrame_E frame in frames)
            {
                double frameFrom = Math.Min(ChainageOf(frame.PlanStart), ChainageOf(frame.PlanEnd));
                double frameTo = Math.Max(ChainageOf(frame.PlanStart), ChainageOf(frame.PlanEnd));

                double shared = Math.Min(to, frameTo) - Math.Max(from, frameFrom);

                if (shared > most)
                {
                    most = shared;
                    best = frame.UniqueName;
                }
            }

            // Nothing overlapped, which means the stretch is shorter than the rounding on a
            // joint. The nearest frame is still a better name than none.
            return best.Length > 0 || frames.Count == 0 ? best : frames[0].UniqueName;
        }

        /// <summary>Every frame on this run, by unique name.</summary>
        public IEnumerable<string> FrameNames
        {
            get { return frames.Select(frame => frame.UniqueName).Where(name => name.Length > 0); }
        }

        /// <summary>How far along the axis a point lies, in mm from <see cref="PlanStart"/>.</summary>
        public double ChainageOf(MyPoint point)
        {
            double length = Length;
            if (length <= 0 || point == null)
            {
                return 0;
            }

            double ux = (PlanEnd.X - PlanStart.X) / length;
            double uy = (PlanEnd.Y - PlanStart.Y) / length;

            return (point.X - PlanStart.X) * ux + (point.Y - PlanStart.Y) * uy;
        }

        /// <summary>The point at a chainage along the axis.</summary>
        public MyPoint PointAt(double chainage)
        {
            double length = Length;
            if (length <= 0)
            {
                return PlanStart;
            }

            double k = chainage / length;

            return new MyPoint(PlanStart.X + (PlanEnd.X - PlanStart.X) * k,
                               PlanStart.Y + (PlanEnd.Y - PlanStart.Y) * k,
                               PlanStart.Z);
        }

        /// <summary>
        /// What the ETABS frames on this run actually carry, along the axis, added where two
        /// of them cover the same chainage.
        ///
        /// Loads that do not plainly act downwards and distributed moments are left out: they
        /// are not line loads and putting them in would compare a number against something
        /// that is not the same kind of thing. They are reported instead, by the engine.
        /// </summary>
        internal LoadProfile GravityProfile(LineLoadPatterns patterns)
        {
            LoadProfile profile = new LoadProfile();

            foreach (LineLoadFrame_E frame in frames)
            {
                double frameLength = frame.PlanLength;
                if (frameLength <= 0)
                {
                    continue;
                }

                double atI = ChainageOf(frame.PlanStart);
                double atJ = ChainageOf(frame.PlanEnd);

                foreach (LineLoad_E load in frame.Loads)
                {
                    if (load == null || !load.IsGravity || load.IsMoment)
                    {
                        continue;
                    }

                    if (patterns != null && !patterns.Matches(load.Pattern))
                    {
                        continue;
                    }

                    profile.Add(atI + (atJ - atI) * load.RelativeFrom,
                                atI + (atJ - atI) * load.RelativeTo,
                                load.ValueAtStart, load.ValueAtEnd);
                }
            }

            return profile;
        }

        public override string ToString()
        {
            return $"run of {frames.Count} frame(s) on {Story}, {Math.Round(Length / 1000d, 2)} m";
        }

        #region making the runs

        /// <summary>
        /// One run per frame with a line in plan - a column has none. Never chained: see the
        /// class summary.
        /// </summary>
        public static List<LineLoadRun> Build(IEnumerable<LineLoadFrame_E> frames)
        {
            List<LineLoadRun> runs = new List<LineLoadRun>();

            if (frames == null)
            {
                return runs;
            }

            foreach (LineLoadFrame_E frame in frames.Where(frame => frame != null && frame.HasPlanLine))
            {
                LineLoadRun run = new LineLoadRun(frame.Story);
                run.frames.Add(frame);
                run.SetAxis();
                runs.Add(run);
            }

            return runs;
        }

        /// <summary>
        /// The run's axis: the two endpoints of its frames that are furthest apart, which for
        /// one frame is the frame itself.
        /// </summary>
        private void SetAxis()
        {
            List<MyPoint> ends = new List<MyPoint>();

            foreach (LineLoadFrame_E frame in frames)
            {
                ends.Add(frame.PlanStart);
                ends.Add(frame.PlanEnd);
            }

            MyPoint[] span = WallGeometry.PlanSpan(ends);

            if (span != null)
            {
                PlanStart = span[0];
                PlanEnd = span[1];
            }
        }

        #endregion
    }
}
