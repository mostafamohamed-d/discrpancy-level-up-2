using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// How high a wall is along its length, read off its elevation - the face it shows from
    /// the side - rather than off its bounding box.
    ///
    /// A bounding box is the wall's tallest point carried along all of it. An upstand whose
    /// profile was edited - stepped down, sloped, notched - weighs what it is at each place,
    /// and ETABS carries it that way: a length at 1.2 m, the next at 0.3 m, each a load of its
    /// own. So the wall is cut where its height changes, and each length is weighed at its own
    /// height; a sloped length at its average.
    ///
    /// Everything is in the wall's own elevation, in mm: s along the wall from its start, z up.
    /// </summary>
    public static class WallProfile
    {
        /// <summary>Two lengths whose heights are this close, in mm, are one height.</summary>
        public const double SameHeight = 25;

        /// <summary>A length at least this high, in mm, is wall; lower is a gap - a door cut through it.</summary>
        public const double LeastHeight = 1;

        /// <summary>One length of the wall at one height.</summary>
        public sealed class Part
        {
            public Part(double from, double to, double height, bool varies)
            {
                From = from;
                To = to;
                Height = height;
                Varies = varies;
            }

            /// <summary>Where it starts and ends along the wall, in mm.</summary>
            public double From { get; }

            public double To { get; }

            public double Length { get { return To - From; } }

            /// <summary>Its height, in mm: the average where it is not level along its length.</summary>
            public double Height { get; }

            /// <summary>True where the height changes along it - a slope - so the height is an average.</summary>
            public bool Varies { get; }
        }

        /// <summary>
        /// The wall cut into lengths at one height each, off the loops of its elevation - the
        /// outline and any holes in it, as (s, z) points in mm. Lengths shorter than
        /// <paramref name="shortest"/> are folded into the neighbour nearer their height, and
        /// gaps right through the wall - a door - carry nothing and are left out. Empty where
        /// the loops say nothing.
        /// </summary>
        public static List<Part> Parts(IEnumerable<IList<double[]>> loops, double length, double shortest)
        {
            return Parts(new[] { loops }, length, shortest);
        }

        /// <summary>
        /// The same off both faces of the wall, each its own loops, taking the higher of the two
        /// at each place: a wall butting into one face cuts a hole in that face's outline only,
        /// and the upstand is no lower there for it.
        /// </summary>
        public static List<Part> Parts(IEnumerable<IEnumerable<IList<double[]>>> faces, double length, double shortest)
        {
            List<List<IList<double[]>>> outline = (faces ?? Enumerable.Empty<IEnumerable<IList<double[]>>>())
                .Where(face => face != null)
                .Select(face => face.Where(loop => loop != null && loop.Count >= 3).ToList())
                .Where(face => face.Count > 0)
                .ToList();
            if (outline.Count == 0 || length <= 0)
            {
                return new List<Part>();
            }

            // Between two corners of the elevation the height runs straight, so the height
            // half way is the average over the length - exactly, sloped or not.
            List<double> cuts = outline.SelectMany(face => face)
                                       .SelectMany(loop => loop.Select(point => Clamp(point[0], length)))
                                       .Concat(new[] { 0d, length })
                                       .OrderBy(s => s)
                                       .ToList();

            List<Part> parts = new List<Part>();
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                double from = cuts[i];
                double to = cuts[i + 1];
                if (to - from < 1)
                {
                    continue;
                }

                double atFrom = Highest(outline, from + 0.01);
                double atTo = Highest(outline, to - 0.01);
                double height = Highest(outline, (from + to) / 2);
                parts.Add(new Part(from, to, height, Math.Abs(atTo - atFrom) > SameHeight));
            }

            parts = Joined(parts);
            parts = Folded(parts, shortest);
            return Joined(parts).Where(part => part.Height >= LeastHeight).ToList();
        }

        private static double Highest(List<List<IList<double[]>>> faces, double s)
        {
            return faces.Max(face => HeightAt(face, s));
        }

        /// <summary>
        /// How much wall there is straight up at a place along it, in mm: every crossing of the
        /// elevation's edges there, taken in pairs, in to out. A hole is a pair cut out of it.
        /// </summary>
        public static double HeightAt(IEnumerable<IList<double[]>> loops, double s)
        {
            List<double> crossings = new List<double>();
            foreach (IList<double[]> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    double[] a = loop[i];
                    double[] b = loop[(i + 1) % loop.Count];

                    // Half open, so a corner exactly at s is crossed once, not twice.
                    if ((a[0] <= s && s < b[0]) || (b[0] <= s && s < a[0]))
                    {
                        double k = (s - a[0]) / (b[0] - a[0]);
                        crossings.Add(a[1] + (b[1] - a[1]) * k);
                    }
                }
            }

            crossings.Sort();
            double height = 0;
            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                height += crossings[i + 1] - crossings[i];
            }
            return height;
        }

        /// <summary>
        /// The plan line between two distances along it, in mm: where it starts, every corner
        /// in between, and where it ends.
        /// </summary>
        public static List<MyPoint> Cut(IList<MyPoint> line, double from, double to)
        {
            List<MyPoint> points = new List<MyPoint>();
            if (line == null || line.Count < 2 || to <= from)
            {
                return points;
            }

            double walked = 0;
            for (int i = 0; i < line.Count - 1; i++)
            {
                MyPoint a = line[i];
                MyPoint b = line[i + 1];
                double piece = WallGeometry.PlanLength(a, b);
                double start = walked;
                double end = walked + piece;
                walked = end;

                if (piece <= 0 || end < from || start > to)
                {
                    continue;
                }

                if (points.Count == 0)
                {
                    points.Add(Along(a, b, (Math.Max(from, start) - start) / piece));
                }
                if (end < to)
                {
                    points.Add(new MyPoint(b.X, b.Y, b.Z));
                }
                else
                {
                    points.Add(Along(a, b, (to - start) / piece));
                    break;
                }
            }

            return points;
        }

        private static MyPoint Along(MyPoint a, MyPoint b, double k)
        {
            return new MyPoint(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Z + (b.Z - a.Z) * k);
        }

        private static double Clamp(double s, double length)
        {
            return Math.Max(0, Math.Min(length, s));
        }

        /// <summary>Neighbours at the same height made one length.</summary>
        private static List<Part> Joined(List<Part> parts)
        {
            List<Part> joined = new List<Part>();
            foreach (Part part in parts)
            {
                Part last = joined.Count > 0 ? joined[joined.Count - 1] : null;
                if (last != null && Math.Abs(last.To - part.From) < 1
                    && Math.Abs(last.Height - part.Height) <= SameHeight)
                {
                    joined[joined.Count - 1] = Together(last, part);
                }
                else
                {
                    joined.Add(part);
                }
            }
            return joined;
        }

        /// <summary>
        /// Lengths too short to be a load of their own folded into the neighbour nearer their
        /// height, shortest first, so a notch or a short slope between two levels is weighed
        /// with what it sits in.
        /// </summary>
        private static List<Part> Folded(List<Part> parts, double shortest)
        {
            List<Part> folded = parts.ToList();
            HashSet<Part> alone = new HashSet<Part>();
            while (true)
            {
                Part short_ = folded.Where(part => part.Length < shortest && !alone.Contains(part))
                                    .OrderBy(part => part.Length)
                                    .FirstOrDefault();
                if (short_ == null)
                {
                    break;
                }

                int i = folded.IndexOf(short_);
                Part before = i > 0 && Math.Abs(folded[i - 1].To - short_.From) < 1 ? folded[i - 1] : null;
                Part after = i + 1 < folded.Count && Math.Abs(short_.To - folded[i + 1].From) < 1 ? folded[i + 1] : null;
                if (before == null && after == null)
                {
                    // Nothing either side to weigh it with - the whole wall is this short.
                    alone.Add(short_);
                    continue;
                }

                Part into = before == null
                    ? after
                    : after == null
                        ? before
                        : Math.Abs(before.Height - short_.Height) <= Math.Abs(after.Height - short_.Height) ? before : after;

                // A slot too short to be a gap is wall either side of it, not a lighter length.
                if (short_.Height < LeastHeight)
                {
                    short_ = new Part(short_.From, short_.To, into.Height, into.Varies);
                }

                if (into == before)
                {
                    folded[i - 1] = Together(before, short_);
                    folded.RemoveAt(i);
                }
                else
                {
                    folded[i + 1] = Together(short_, after);
                    folded.RemoveAt(i);
                }
            }
            return folded;
        }

        private static Part Together(Part first, Part second)
        {
            double length = first.Length + second.Length;
            double height = length > 0
                ? (first.Height * first.Length + second.Height * second.Length) / length
                : first.Height;
            bool varies = first.Varies || second.Varies || Math.Abs(first.Height - second.Height) > SameHeight;
            return new Part(first.From, second.To, height, varies);
        }
    }
}
