using System;
using System.Collections.Generic;
using System.Drawing;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Plan geometry shared by the wall comparison, the line load check and the viewer. Almost
    /// everything here works on the two ends of a line and so is asked of an
    /// <see cref="IPlanLine"/>; only the few that push a line out to a footprint need a wall's
    /// thickness. Lengths are in mm, angles in degrees measured from global X, and elevation is
    /// ignored throughout: these are plan checks, run one story at a time.
    ///
    /// Nothing here touches Revit or ETABS, which is what lets the pairing rules be tested
    /// without a model open.
    /// </summary>
    public static class WallGeometry
    {
        /// <summary>
        /// The two points of a wall's footprint that are furthest apart in plan, which is the
        /// centre line of a vertical panel however it happens to be drawn.
        ///
        /// An ETABS wall area seen in plan collapses onto a line, so the pair furthest apart is
        /// that line's two ends. Taking the extremes rather than, say, the top edge means a wall
        /// stepped at the top, one sloping down to a footing, and one whose corners were drawn
        /// in an odd order all read the same. Null when there is no line to be had: fewer than
        /// two points, or every point on top of the next.
        /// </summary>
        public static MyPoint[] PlanSpan(IList<MyPoint> points)
        {
            if (points == null || points.Count < 2)
            {
                return null;
            }

            MyPoint furthestA = null;
            MyPoint furthestB = null;
            double furthest = 0;

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    double distance = PlanLength(points[i], points[j]);
                    if (distance > furthest)
                    {
                        furthest = distance;
                        furthestA = points[i];
                        furthestB = points[j];
                    }
                }
            }

            if (furthestA == null || furthest <= 0)
            {
                return null;
            }

            return new[]
            {
                new MyPoint(furthestA.X, furthestA.Y),
                new MyPoint(furthestB.X, furthestB.Y)
            };
        }

        /// <summary>Plan (XY only) distance between two points.</summary>
        public static double PlanLength(MyPoint a, MyPoint b)
        {
            if (a == null || b == null)
            {
                return 0;
            }

            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Which way the wall runs, in degrees from global X. A wall has no near end and no far
        /// end, so this is only ever read through <see cref="ColumnGeometry.AngularDifference"/>
        /// with a period of 180, where a wall drawn the other way round is the same wall.
        /// </summary>
        public static double AngleDegrees(MyPoint start, MyPoint end)
        {
            if (start == null || end == null)
            {
                return 0;
            }

            return Math.Atan2(end.Y - start.Y, end.X - start.X) * 180d / Math.PI;
        }

        /// <summary>The angle between two wall centre lines, folded into 0..90 degrees.</summary>
        public static double AngleBetween(IPlanLine a, IPlanLine b)
        {
            if (a == null || b == null)
            {
                return 90;
            }

            return ColumnGeometry.AngularDifference(AngleDegrees(a.PlanStart, a.PlanEnd),
                                                    AngleDegrees(b.PlanStart, b.PlanEnd), 180d);
        }

        /// <summary>
        /// Distance from a point to the infinite line through two others, in plan. Falls back to
        /// the distance to the point itself where the line has no length.
        /// </summary>
        public static double PerpendicularDistance(MyPoint point, MyPoint lineStart, MyPoint lineEnd)
        {
            if (point == null || lineStart == null || lineEnd == null)
            {
                return double.MaxValue;
            }

            double dx = lineEnd.X - lineStart.X;
            double dy = lineEnd.Y - lineStart.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= 0)
            {
                return PlanLength(point, lineStart);
            }

            // Twice the area of the triangle over the length of its base, which is its height.
            double cross = (point.X - lineStart.X) * dy - (point.Y - lineStart.Y) * dx;
            return Math.Abs(cross) / length;
        }

        /// <summary>
        /// How far the first wall sits off the line the second one runs along, in mm: the worse
        /// of its two ends.
        ///
        /// The worse end rather than the middle, because that is what makes this one number
        /// enough. Two walls a hand's breadth apart and two walls that cross in the middle and
        /// splay apart are both wrong, and the middle would call the second one a perfect match.
        /// </summary>
        public static double Offset(IPlanLine measured, IPlanLine against)
        {
            if (measured == null || against == null)
            {
                return double.MaxValue;
            }

            double atStart = PerpendicularDistance(measured.PlanStart, against.PlanStart, against.PlanEnd);
            double atEnd = PerpendicularDistance(measured.PlanEnd, against.PlanStart, against.PlanEnd);

            return Math.Max(atStart, atEnd);
        }

        /// <summary>
        /// How much of the two walls run alongside each other, in mm, measured along the first
        /// one. Nought where they only cross, or pass each other end to end.
        ///
        /// This is what tells a wall from the one on the other side of the corridor running
        /// parallel to it: being near in perpendicular distance means nothing if the two do not
        /// actually stand alongside each other.
        /// </summary>
        public static double OverlapLength(IPlanLine a, IPlanLine b)
        {
            double from, to;
            return OverlapSpan(a, b, out from, out to) ? to - from : 0;
        }

        /// <summary>
        /// Which stretch of the first wall the second one runs alongside, as two distances
        /// measured along the first from its start. False where they do not run alongside each
        /// other at all.
        ///
        /// The stretch rather than its length, for the times it matters which part of a wall is
        /// covered and not only how much of it: several panels standing along one wall can
        /// cover the same metre of it three times over, and only the stretches say so.
        /// </summary>
        public static bool OverlapSpan(IPlanLine a, IPlanLine b, out double from, out double to)
        {
            from = 0;
            to = 0;

            if (a == null || b == null)
            {
                return false;
            }

            double length = PlanLength(a.PlanStart, a.PlanEnd);
            if (length <= 0)
            {
                return false;
            }

            double ux = (a.PlanEnd.X - a.PlanStart.X) / length;
            double uy = (a.PlanEnd.Y - a.PlanStart.Y) / length;

            double first = Along(b.PlanStart, a.PlanStart, ux, uy);
            double second = Along(b.PlanEnd, a.PlanStart, ux, uy);

            from = Math.Max(0, Math.Min(first, second));
            to = Math.Min(length, Math.Max(first, second));

            return to > from;
        }

        /// <summary>
        /// How much of a wall has one of the others standing along it, in mm, counting a
        /// stretch covered twice only once.
        ///
        /// The double counting is not a corner case: a wall meshed up its height puts three
        /// panels along the same metre of Revit wall, and adding their lengths up would say a
        /// three metre wall was nine metres covered. What is wanted is how much of the wall
        /// has anything against it at all, so the stretches are merged before they are
        /// measured.
        /// </summary>
        public static double CoveredLength(IPlanLine wall, IEnumerable<IPlanLine> others)
        {
            if (wall == null || others == null)
            {
                return 0;
            }

            double covered = 0;

            // The stretches are merged before they are measured, so a wall covered at both ends
            // and open in the middle is counted on the two ends alone.
            foreach (double[] span in MergedSpans(wall, others))
            {
                covered += span[1] - span[0];
            }

            return covered;
        }

        /// <summary>How far along a direction a point lies, measured from an origin.</summary>
        private static double Along(MyPoint point, MyPoint origin, double ux, double uy)
        {
            if (point == null || origin == null)
            {
                return 0;
            }

            return (point.X - origin.X) * ux + (point.Y - origin.Y) * uy;
        }

        /// <summary>
        /// The stretches of a wall that none of the others stand along, as pairs of plan
        /// points. Empty where they cover it end to end.
        ///
        /// The other side of <see cref="CoveredLength"/>, and worked out the same way: the
        /// covered stretches are merged first, so panels stacked up the height of a wall do
        /// not leave phantom gaps between them, and what is left over is what nothing reaches.
        /// A wall covered at both ends and open in the middle gives one piece, in the middle,
        /// which is exactly where the drawing should show it.
        /// </summary>
        public static List<MyPoint[]> UncoveredSpans(IPlanLine wall, IEnumerable<IPlanLine> others,
                                                     double shortestWorthHaving)
        {
            List<MyPoint[]> pieces = new List<MyPoint[]>();

            if (wall == null)
            {
                return pieces;
            }

            double length = PlanLength(wall.PlanStart, wall.PlanEnd);
            if (length <= 0)
            {
                return pieces;
            }

            double at = 0;

            foreach (double[] span in MergedSpans(wall, others))
            {
                AddPiece(pieces, wall, length, at, span[0], shortestWorthHaving);
                at = Math.Max(at, span[1]);
            }

            AddPiece(pieces, wall, length, at, length, shortestWorthHaving);

            return pieces;
        }

        private static void AddPiece(List<MyPoint[]> pieces, IPlanLine wall, double length,
                                     double from, double to, double shortestWorthHaving)
        {
            if (to - from < Math.Max(shortestWorthHaving, 0))
            {
                return;
            }

            pieces.Add(new[] { PointAlong(wall, length, from), PointAlong(wall, length, to) });
        }

        /// <summary>The point a given distance along a line from its start.</summary>
        private static MyPoint PointAlong(IPlanLine wall, double length, double distance)
        {
            double k = distance / length;

            return new MyPoint(
                wall.PlanStart.X + (wall.PlanEnd.X - wall.PlanStart.X) * k,
                wall.PlanStart.Y + (wall.PlanEnd.Y - wall.PlanStart.Y) * k,
                wall.PlanStart.Z);
        }

        /// <summary>
        /// The stretches of the first wall the others stand along, overlapping ones merged into
        /// one, in order along the wall.
        /// </summary>
        private static List<double[]> MergedSpans(IPlanLine wall, IEnumerable<IPlanLine> others)
        {
            List<double[]> spans = new List<double[]>();

            if (others == null)
            {
                return spans;
            }

            List<double[]> raw = new List<double[]>();

            foreach (IPlanLine other in others)
            {
                double from, to;
                if (OverlapSpan(wall, other, out from, out to))
                {
                    raw.Add(new[] { from, to });
                }
            }

            if (raw.Count == 0)
            {
                return spans;
            }

            raw.Sort((left, right) => left[0].CompareTo(right[0]));

            double openFrom = raw[0][0];
            double openTo = raw[0][1];

            foreach (double[] span in raw)
            {
                if (span[0] > openTo)
                {
                    spans.Add(new[] { openFrom, openTo });
                    openFrom = span[0];
                    openTo = span[1];
                    continue;
                }

                openTo = Math.Max(openTo, span[1]);
            }

            spans.Add(new[] { openFrom, openTo });

            return spans;
        }

        /// <summary>
        /// The four corners of a wall in plan: the centre line pushed out by half the thickness
        /// either side. Empty where there is no line to push out.
        /// </summary>
        public static PointF[] PlanOutline(MyPoint start, MyPoint end, double thickness)
        {
            double length = PlanLength(start, end);
            if (length <= 0)
            {
                return new PointF[0];
            }

            double half = Math.Max(thickness, 0) / 2d;
            double nx = -(end.Y - start.Y) / length * half;
            double ny = (end.X - start.X) / length * half;

            return new[]
            {
                new PointF((float)(start.X + nx), (float)(start.Y + ny)),
                new PointF((float)(end.X + nx), (float)(end.Y + ny)),
                new PointF((float)(end.X - nx), (float)(end.Y - ny)),
                new PointF((float)(start.X - nx), (float)(start.Y - ny))
            };
        }

        public static PointF[] PlanOutline(IWall wall)
        {
            return wall == null
                ? new PointF[0]
                : PlanOutline(wall.PlanStart, wall.PlanEnd, wall.Thickness);
        }

        /// <summary>The middle of a wall's centre line, where its labels are anchored.</summary>
        public static MyPoint Midpoint(IPlanLine wall)
        {
            if (wall == null || wall.PlanStart == null || wall.PlanEnd == null)
            {
                return MyPoint.Zero;
            }

            return new MyPoint((wall.PlanStart.X + wall.PlanEnd.X) / 2d,
                               (wall.PlanStart.Y + wall.PlanEnd.Y) / 2d);
        }
    }
}
