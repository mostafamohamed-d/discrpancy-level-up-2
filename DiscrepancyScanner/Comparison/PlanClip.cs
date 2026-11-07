using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Clipping a plan outline to a convex one, worked out by hand rather than asked of the
    /// geometry kernel: the slab check fills the part of a Revit floor hole a column or wall
    /// stands in - see <see cref="SlabCompareEngine.SupportPatches"/> - and columns and walls
    /// are convex, which is all this needs. No kernel, so it runs in a test as it runs in Revit.
    /// </summary>
    internal static class PlanClip
    {
        /// <summary>Two edges this close to one line are one edge, in mm of turn per mm of edge.</summary>
        private const double Straight = 1e-9;

        /// <summary>
        /// The part of <paramref name="subject"/> inside the convex <paramref name="clip"/>, by
        /// Sutherland-Hodgman: the subject cut down by each side of the clip in turn. The subject
        /// may be any simple outline; the clip has to be convex.
        /// </summary>
        public static List<MyPoint> ToConvex(IList<MyPoint> subject, IList<MyPoint> clip)
        {
            List<MyPoint> output = Open(subject);
            List<MyPoint> sides = CounterClockwise(Open(clip));

            for (int i = 0; i < sides.Count && output.Count > 0; i++)
            {
                MyPoint a = sides[i];
                MyPoint b = sides[(i + 1) % sides.Count];

                List<MyPoint> input = output;
                output = new List<MyPoint>();

                for (int j = 0; j < input.Count; j++)
                {
                    MyPoint current = input[j];
                    MyPoint previous = input[(j + input.Count - 1) % input.Count];
                    bool currentIn = Side(a, b, current) >= 0;
                    bool previousIn = Side(a, b, previous) >= 0;

                    if (currentIn)
                    {
                        if (!previousIn)
                        {
                            output.Add(Crossing(previous, current, a, b));
                        }
                        output.Add(current);
                    }
                    else if (previousIn)
                    {
                        output.Add(Crossing(previous, current, a, b));
                    }
                }
            }

            return output;
        }

        /// <summary>
        /// A convex outline pushed out by <paramref name="distance"/> on every side, or null when
        /// the outline is not convex.
        /// </summary>
        public static List<MyPoint> Grown(IList<MyPoint> outline, double distance)
        {
            List<MyPoint> points = WithoutStraightCorners(CounterClockwise(Open(outline)));
            if (points.Count < 3 || !IsConvex(points))
            {
                return null;
            }

            int n = points.Count;
            List<MyPoint> grown = new List<MyPoint>(n);
            for (int i = 0; i < n; i++)
            {
                MyPoint before = points[(i + n - 1) % n];
                MyPoint here = points[i];
                MyPoint after = points[(i + 1) % n];

                // Each side moved out along its own outward normal; the new corner is where the
                // two moved sides meet.
                MyPoint[] first = Shifted(before, here, distance);
                MyPoint[] second = Shifted(here, after, distance);
                grown.Add(Crossing(first[0], first[1], second[0], second[1]));
            }
            return grown;
        }

        /// <summary>The ring without its closing point, where it repeats the first.</summary>
        private static List<MyPoint> Open(IList<MyPoint> ring)
        {
            List<MyPoint> points = (ring ?? new List<MyPoint>()).Where(point => point != null).ToList();
            if (points.Count > 1 && Same(points[0], points[points.Count - 1]))
            {
                points.RemoveAt(points.Count - 1);
            }
            return points;
        }

        private static List<MyPoint> CounterClockwise(List<MyPoint> points)
        {
            double twiceArea = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                twiceArea += a.X * b.Y - b.X * a.Y;
            }
            if (twiceArea < 0)
            {
                points.Reverse();
            }
            return points;
        }

        private static List<MyPoint> WithoutStraightCorners(List<MyPoint> points)
        {
            List<MyPoint> kept = new List<MyPoint>();
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint before = points[(i + points.Count - 1) % points.Count];
                MyPoint here = points[i];
                MyPoint after = points[(i + 1) % points.Count];

                if (Same(before, here))
                {
                    continue;
                }

                double turn = Cross(before, here, after);
                double scale = Length(before, here) * Length(here, after);
                if (scale > 0 && Math.Abs(turn) / scale > Straight)
                {
                    kept.Add(here);
                }
            }
            return kept;
        }

        private static bool IsConvex(List<MyPoint> counterClockwise)
        {
            int n = counterClockwise.Count;
            for (int i = 0; i < n; i++)
            {
                if (Cross(counterClockwise[i], counterClockwise[(i + 1) % n], counterClockwise[(i + 2) % n]) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The side from a to b moved outwards - to the right of it, for a counterclockwise ring.</summary>
        private static MyPoint[] Shifted(MyPoint a, MyPoint b, double distance)
        {
            double length = Length(a, b);
            double nx = (b.Y - a.Y) / length * distance;
            double ny = -(b.X - a.X) / length * distance;
            return new[] { new MyPoint(a.X + nx, a.Y + ny, 0), new MyPoint(b.X + nx, b.Y + ny, 0) };
        }

        /// <summary>Above nought to the left of the line from a to b, which is inside a counterclockwise ring.</summary>
        private static double Side(MyPoint a, MyPoint b, MyPoint p)
        {
            return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        }

        private static double Cross(MyPoint a, MyPoint b, MyPoint c)
        {
            return (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
        }

        /// <summary>Where the line through p and q meets the line through a and b.</summary>
        private static MyPoint Crossing(MyPoint p, MyPoint q, MyPoint a, MyPoint b)
        {
            double rx = q.X - p.X, ry = q.Y - p.Y;
            double sx = b.X - a.X, sy = b.Y - a.Y;
            double denominator = rx * sy - ry * sx;
            if (Math.Abs(denominator) < 1e-12)
            {
                return new MyPoint(q.X, q.Y, 0);
            }

            double t = ((a.X - p.X) * sy - (a.Y - p.Y) * sx) / denominator;
            return new MyPoint(p.X + t * rx, p.Y + t * ry, 0);
        }

        private static double Length(MyPoint a, MyPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool Same(MyPoint a, MyPoint b)
        {
            return Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        }
    }
}
