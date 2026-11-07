using System;
using System.Drawing;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Plan geometry and angle helpers shared by the column comparison and the viewer.
    /// All lengths are in mm and all angles in degrees measured from global X.
    /// </summary>
    public static class ColumnGeometry
    {
        private const int RoundSegments = 32;

        /// <summary>
        /// Plan outline of a column. Rectangular sections give the four corners,
        /// round sections give a polygon approximation of the circle.
        /// </summary>
        public static PointF[] PlanOutline(IColumn column)
        {
            if (column == null)
            {
                return new PointF[0];
            }

            MyPoint center = column.PlanPoint ?? MyPoint.Zero;

            if (column.Shape == SectionShape.Round)
            {
                return CirclePoints(center, column.Diameter / 2d);
            }

            return RectanglePoints(center, column.Width, column.Depth, column.AngleDegrees);
        }

        /// <summary>
        /// The larger of a column's two plan dimensions, in mm. Sections are compared long
        /// side to long side rather than width to width, because which of the two a Revit
        /// family calls its width does not reliably line up with the ETABS local axes. A
        /// square column has the same value for both sides, so it compares equal either way.
        /// </summary>
        public static double LongSide(IColumn column)
        {
            if (column == null)
            {
                return 0;
            }
            return Math.Max(column.Width, column.Depth);
        }

        /// <summary>The smaller of a column's two plan dimensions, in mm. See <see cref="LongSide"/>.</summary>
        public static double ShortSide(IColumn column)
        {
            if (column == null)
            {
                return 0;
            }
            return Math.Min(column.Width, column.Depth);
        }

        /// <summary>
        /// Corners of a rectangle centred on <paramref name="center"/>. The depth spans the
        /// local 3 axis (global X at zero rotation) and the width spans the local 2 axis.
        /// </summary>
        public static PointF[] RectanglePoints(MyPoint center, double width, double depth, double angleDegrees)
        {
            PointF p1 = new PointF((float)(center.X - depth / 2), (float)(center.Y - width / 2));
            PointF p2 = new PointF((float)(center.X + depth / 2), (float)(center.Y - width / 2));
            PointF p3 = new PointF((float)(center.X + depth / 2), (float)(center.Y + width / 2));
            PointF p4 = new PointF((float)(center.X - depth / 2), (float)(center.Y + width / 2));

            PointF[] points = { p1, p2, p3, p4 };
            RotateAllPointsAround(ref points, center, angleDegrees);
            return points;
        }

        public static PointF[] CirclePoints(MyPoint center, double radius)
        {
            PointF[] points = new PointF[RoundSegments];
            for (int i = 0; i < RoundSegments; i++)
            {
                double theta = 2 * Math.PI * i / RoundSegments;
                points[i] = new PointF((float)(center.X + radius * Math.Cos(theta)),
                                       (float)(center.Y + radius * Math.Sin(theta)));
            }
            return points;
        }

        public static void RotateAllPointsAround(ref PointF[] points, MyPoint centerPoint, double angleInDegrees)
        {
            double angleInRadians = angleInDegrees * (Math.PI / 180);
            double cosTheta = Math.Cos(angleInRadians);
            double sinTheta = Math.Sin(angleInRadians);

            for (int i = 0; i < points.Length; i++)
            {
                var dx = points[i].X - centerPoint.X;
                var dy = points[i].Y - centerPoint.Y;
                points[i].X = (float)(cosTheta * dx - sinTheta * dy + centerPoint.X);
                points[i].Y = (float)(sinTheta * dx + cosTheta * dy + centerPoint.Y);
            }
        }

        /// <summary>
        /// Smallest difference between two angles once folded into the given period.
        /// A rectangular column repeats every 180 degrees, so that is the period used
        /// when comparing column orientations.
        /// </summary>
        public static double AngularDifference(double angle1Degrees, double angle2Degrees, double periodDegrees = 180d)
        {
            double delta = Math.Abs(angle1Degrees - angle2Degrees) % periodDegrees;
            return Math.Min(delta, periodDegrees - delta);
        }

        /// <summary>Plan (XY only) distance between two points, ignoring elevation.</summary>
        public static double PlanDistance(MyPoint p1, MyPoint p2)
        {
            if (p1 == null || p2 == null)
            {
                return double.MaxValue;
            }
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
