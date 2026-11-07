using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A plan bounding box in mm, used to rule pairs of regions out before the far more
    /// expensive boolean operations are reached for.
    /// </summary>
    public sealed class PlanBounds
    {
        public PlanBounds()
        {
            MinX = double.MaxValue;
            MinY = double.MaxValue;
            MaxX = double.MinValue;
            MaxY = double.MinValue;
        }

        public double MinX { get; private set; }
        public double MinY { get; private set; }
        public double MaxX { get; private set; }
        public double MaxY { get; private set; }

        public bool IsEmpty { get { return MinX > MaxX; } }

        public double Width { get { return IsEmpty ? 0 : MaxX - MinX; } }
        public double Height { get { return IsEmpty ? 0 : MaxY - MinY; } }

        public void Include(double x, double y)
        {
            if (x < MinX) MinX = x;
            if (y < MinY) MinY = y;
            if (x > MaxX) MaxX = x;
            if (y > MaxY) MaxY = y;
        }

        public void Include(PlanBounds other)
        {
            if (other == null || other.IsEmpty)
            {
                return;
            }
            Include(other.MinX, other.MinY);
            Include(other.MaxX, other.MaxY);
        }

        /// <summary>
        /// True when the two boxes come within <paramref name="gap"/> of each other. The gap
        /// keeps regions that only touch along an edge in play, since two slabs meeting on a
        /// grid line still have to be told apart from two that overlap.
        /// </summary>
        public bool IntersectsWith(PlanBounds other, double gap = 0)
        {
            if (other == null || IsEmpty || other.IsEmpty)
            {
                return false;
            }
            return MinX - gap <= other.MaxX && other.MinX - gap <= MaxX
                && MinY - gap <= other.MaxY && other.MinY - gap <= MaxY;
        }

        public bool Contains(double x, double y, double gap = 0)
        {
            return !IsEmpty && x >= MinX - gap && x <= MaxX + gap && y >= MinY - gap && y <= MaxY + gap;
        }
    }

    /// <summary>
    /// One closed ring of a plan region, in model mm. The closing edge back to the first
    /// point is implied rather than stored, and consecutive points that land on top of each
    /// other are dropped as the ring is built, because a zero length edge is what the
    /// geometry kernel rejects most often.
    /// </summary>
    public sealed class PlanRing
    {
        /// <summary>Points closer together than this along a ring are the same point.</summary>
        public const double PointTolerance = 1d;

        private readonly List<MyPoint> points;
        private readonly PlanBounds bounds = new PlanBounds();
        private readonly double signedArea;
        private double perimeter = -1;

        private PlanRing(List<MyPoint> cleaned)
        {
            points = cleaned;
            foreach (MyPoint point in points)
            {
                bounds.Include(point.X, point.Y);
            }
            signedArea = ShoelaceArea(points);
        }

        /// <summary>
        /// Builds a ring, or returns null when what is left after removing repeated points
        /// cannot enclose an area.
        /// </summary>
        public static PlanRing FromPoints(IEnumerable<MyPoint> source)
        {
            if (source == null)
            {
                return null;
            }

            List<MyPoint> cleaned = new List<MyPoint>();
            foreach (MyPoint point in source)
            {
                if (point == null)
                {
                    continue;
                }
                if (cleaned.Count > 0 && IsSamePoint(cleaned[cleaned.Count - 1], point))
                {
                    continue;
                }
                cleaned.Add(new MyPoint(point.X, point.Y, point.Z));
            }

            // The ring closes implicitly, so a repeated first point at the end is redundant.
            while (cleaned.Count > 1 && IsSamePoint(cleaned[0], cleaned[cleaned.Count - 1]))
            {
                cleaned.RemoveAt(cleaned.Count - 1);
            }

            return cleaned.Count < 3 ? null : new PlanRing(cleaned);
        }

        public IList<MyPoint> Points { get { return points; } }

        public PlanBounds Bounds { get { return bounds; } }

        /// <summary>Positive when the points run counter clockwise.</summary>
        public double SignedArea { get { return signedArea; } }

        public double Area { get { return Math.Abs(signedArea); } }

        public bool IsCounterClockwise { get { return signedArea > 0; } }

        /// <summary>
        /// The ring cut into simple rings wherever it returns to a point it has already been.
        ///
        /// A ring that touches itself is not a boundary the geometry kernel will accept, and
        /// the models are full of them: an opening that reaches the slab edge, or one Revit
        /// chose to reach out to, comes back as part of the outer loop rather than as a loop
        /// of its own - the boundary runs out along a zero width slit, all the way round the
        /// opening, and back down the same slit. It arrives as one ring that visits the mouth
        /// of the slit twice.
        ///
        /// Cutting at that repeated point is what the shape meant all along: the excursion is
        /// its own ring, wound against the one it came out of, which is to say a hole. What is
        /// left is the boundary without the slit. Rings that are already simple come back
        /// alone and untouched.
        /// </summary>
        public IList<PlanRing> SplitAtRepeatedPoints()
        {
            List<PlanRing> pieces = new List<PlanRing>();
            List<MyPoint> walked = new List<MyPoint>();
            Dictionary<string, int> seenAt = new Dictionary<string, int>();

            foreach (MyPoint point in points)
            {
                string key = Key(point);
                int firstSeen;

                if (seenAt.TryGetValue(key, out firstSeen))
                {
                    // Back where it has been, so everything since then closes on itself and
                    // comes off as a ring in its own right.
                    List<MyPoint> excursion = walked.GetRange(firstSeen, walked.Count - firstSeen);

                    PlanRing piece = FromPoints(excursion);
                    if (piece != null)
                    {
                        pieces.Add(piece);
                    }

                    for (int i = firstSeen + 1; i < walked.Count; i++)
                    {
                        seenAt.Remove(Key(walked[i]));
                    }
                    walked.RemoveRange(firstSeen + 1, walked.Count - firstSeen - 1);
                    continue;
                }

                seenAt[key] = walked.Count;
                walked.Add(point);
            }

            PlanRing rest = FromPoints(walked);
            if (rest != null)
            {
                pieces.Add(rest);
            }

            // Nothing was cut, so the ring is handed back as it came rather than rebuilt.
            if (pieces.Count == 1 && pieces[0].Points.Count == points.Count)
            {
                return new List<PlanRing> { this };
            }

            return pieces;
        }

        /// <summary>Position to the nearest millimetre, which is what counts as the same point.</summary>
        private static string Key(MyPoint point)
        {
            return Math.Round(point.X).ToString(System.Globalization.CultureInfo.InvariantCulture)
                   + "_"
                   + Math.Round(point.Y).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>True when every point of <paramref name="other"/> lies inside this ring.</summary>
        public bool Surrounds(PlanRing other)
        {
            if (other == null || !bounds.IntersectsWith(other.Bounds))
            {
                return false;
            }

            // One point settles it for rings that came out of the same boundary: they cannot
            // cross, only nest.
            MyPoint probe = other.Points[0];
            for (int i = 0; i < other.Points.Count && i < 4; i++)
            {
                probe = other.Points[i];
                if (Contains(probe.X, probe.Y))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The same ring wound the other way, used to orient holes against the outline.</summary>
        public PlanRing Reversed()
        {
            List<MyPoint> reversed = new List<MyPoint>(points);
            reversed.Reverse();
            return new PlanRing(reversed);
        }

        public PlanRing OrientedCounterClockwise()
        {
            return IsCounterClockwise ? this : Reversed();
        }

        public PlanRing OrientedClockwise()
        {
            return IsCounterClockwise ? Reversed() : this;
        }

        /// <summary>
        /// Perimeter in mm, including the implied closing edge. Worked out on the first ask
        /// and kept, since a ring never changes once it is built and the sliver test asks
        /// every region that comes back from the geometry kernel for this.
        /// </summary>
        public double Perimeter()
        {
            if (perimeter >= 0)
            {
                return perimeter;
            }

            double total = 0;
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                total += Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }

            perimeter = total;
            return perimeter;
        }

        /// <summary>Crossing number test; points exactly on an edge may go either way.</summary>
        public bool Contains(double x, double y)
        {
            if (!bounds.Contains(x, y))
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                double xi = points[i].X, yi = points[i].Y;
                double xj = points[j].X, yj = points[j].Y;

                if (yi > y != yj > y && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>Shortest distance in mm from a point to this ring's outline.</summary>
        public double DistanceToOutline(double x, double y)
        {
            double nearest = double.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                double distance = DistanceToSegment(x, y, a.X, a.Y, b.X, b.Y);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }
            return nearest;
        }

        private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquared = dx * dx + dy * dy;

            double k = lengthSquared < 1e-9 ? 0 : ((px - ax) * dx + (py - ay) * dy) / lengthSquared;
            k = Math.Max(0, Math.Min(1, k));

            double cx = ax + k * dx;
            double cy = ay + k * dy;
            return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        private static bool IsSamePoint(MyPoint a, MyPoint b)
        {
            return Math.Abs(a.X - b.X) <= PointTolerance && Math.Abs(a.Y - b.Y) <= PointTolerance;
        }

        private static double ShoelaceArea(IList<MyPoint> ring)
        {
            double twiceArea = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                MyPoint a = ring[i];
                MyPoint b = ring[(i + 1) % ring.Count];
                twiceArea += a.X * b.Y - b.X * a.Y;
            }
            return twiceArea / 2d;
        }
    }

    /// <summary>
    /// A plan region in model mm: one outline with any number of holes inside it. This is the
    /// currency the slab check works in - an ETABS slab, a Revit floor and every piece either
    /// of them ends up cut into are all one of these.
    ///
    /// The outline is always wound counter clockwise and the holes clockwise, which is what
    /// the geometry kernel expects when the region is turned back into a solid.
    /// </summary>
    public sealed class PlanPolygon
    {
        private readonly PlanRing outer;
        private readonly List<PlanRing> holes;
        private readonly PlanBounds bounds;
        private double area = -1;
        private double typicalWidth = -1;
        private MyPoint interiorPoint;

        private PlanPolygon(PlanRing outer, List<PlanRing> holes)
        {
            this.outer = outer;
            this.holes = holes;
            bounds = new PlanBounds();
            bounds.Include(outer.Bounds);
        }

        /// <summary>A region with no holes, or null when the points cannot enclose an area.</summary>
        public static PlanPolygon FromPoints(IEnumerable<MyPoint> outline)
        {
            PlanRing ring = PlanRing.FromPoints(outline);
            return ring == null ? null : new PlanPolygon(ring.OrientedCounterClockwise(), new List<PlanRing>());
        }

        /// <summary>
        /// A region with holes. A ring that cannot enclose an area is dropped rather than
        /// reported: a degenerate hole is not a hole.
        /// </summary>
        public static PlanPolygon Create(PlanRing outline, IEnumerable<PlanRing> holeRings)
        {
            if (outline == null)
            {
                return null;
            }

            List<PlanRing> cleanedHoles = new List<PlanRing>();
            if (holeRings != null)
            {
                foreach (PlanRing hole in holeRings)
                {
                    if (hole != null)
                    {
                        cleanedHoles.Add(hole.OrientedClockwise());
                    }
                }
            }

            return new PlanPolygon(outline.OrientedCounterClockwise(), cleanedHoles);
        }

        public PlanRing Outer { get { return outer; } }

        public IList<PlanRing> Holes { get { return holes; } }

        public PlanBounds Bounds { get { return bounds; } }

        /// <summary>Area in mm squared, with the holes taken off.</summary>
        public double Area
        {
            get
            {
                if (area < 0)
                {
                    area = Math.Max(0, outer.Area - holes.Sum(hole => hole.Area));
                }
                return area;
            }
        }

        /// <summary>
        /// Twice the area over the perimeter: how wide the region is where it is typical,
        /// which is what tells a real mismatched strip from the hairline sliver two nearly
        /// coincident edges leave behind.
        /// </summary>
        public double TypicalWidth
        {
            get
            {
                if (typicalWidth < 0)
                {
                    double perimeter = outer.Perimeter() + holes.Sum(hole => hole.Perimeter());
                    typicalWidth = perimeter < 1e-6 ? 0 : 2d * Area / perimeter;
                }
                return typicalWidth;
            }
        }

        public bool Contains(double x, double y)
        {
            if (!outer.Contains(x, y))
            {
                return false;
            }
            return !holes.Any(hole => hole.Contains(x, y));
        }

        /// <summary>
        /// A point inside the region, for anchoring a label and for asking which side of
        /// another region this one is on. The centroid of the outline is used when it lands
        /// inside, and a scan line finds one when it does not, which is what an L shaped or a
        /// ring shaped piece needs.
        ///
        /// Worked out once and kept, the same as the area is: a region never changes once it is
        /// made, and the scan line costs hundreds of point tests on a shape whose centroid
        /// falls outside it - which is far too much to pay again on every pair of regions the
        /// check compares.
        /// </summary>
        public MyPoint InteriorPoint()
        {
            return interiorPoint ?? (interiorPoint = FindInteriorPoint());
        }

        private MyPoint FindInteriorPoint()
        {
            MyPoint centroid = Centroid();
            if (Contains(centroid.X, centroid.Y))
            {
                return centroid;
            }

            // Walk a few horizontal lines across the region and take the middle of the widest
            // span that is actually inside it.
            double bestWidth = 0;
            MyPoint best = centroid;
            const int lines = 9;
            const int samples = 64;

            for (int i = 1; i <= lines; i++)
            {
                double y = bounds.MinY + bounds.Height * i / (lines + 1d);
                double spanStart = double.NaN;
                double previousX = bounds.MinX;

                for (int j = 0; j <= samples; j++)
                {
                    double x = bounds.MinX + bounds.Width * j / (double)samples;
                    bool inside = Contains(x, y);

                    if (inside && double.IsNaN(spanStart))
                    {
                        spanStart = x;
                    }
                    else if (!inside && !double.IsNaN(spanStart))
                    {
                        if (previousX - spanStart > bestWidth)
                        {
                            bestWidth = previousX - spanStart;
                            best = new MyPoint((spanStart + previousX) / 2d, y);
                        }
                        spanStart = double.NaN;
                    }
                    previousX = x;
                }

                if (!double.IsNaN(spanStart) && previousX - spanStart > bestWidth)
                {
                    bestWidth = previousX - spanStart;
                    best = new MyPoint((spanStart + previousX) / 2d, y);
                }
            }

            return best;
        }

        /// <summary>Area weighted centroid of the outline, holes ignored.</summary>
        public MyPoint Centroid()
        {
            IList<MyPoint> ring = outer.Points;
            double twiceArea = 0;
            double cx = 0;
            double cy = 0;

            for (int i = 0; i < ring.Count; i++)
            {
                MyPoint a = ring[i];
                MyPoint b = ring[(i + 1) % ring.Count];
                double cross = a.X * b.Y - b.X * a.Y;
                twiceArea += cross;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            if (Math.Abs(twiceArea) < 1e-9)
            {
                return new MyPoint(outer.Bounds.MinX + outer.Bounds.Width / 2d,
                                   outer.Bounds.MinY + outer.Bounds.Height / 2d);
            }

            return new MyPoint(cx / (3d * twiceArea), cy / (3d * twiceArea));
        }

        /// <summary>
        /// Shortest distance from a point to the region: zero anywhere inside it, and the
        /// distance to the nearest edge outside. Used for picking on the plan.
        /// </summary>
        public double DistanceTo(double x, double y)
        {
            if (Contains(x, y))
            {
                return 0;
            }

            double nearest = outer.DistanceToOutline(x, y);
            foreach (PlanRing hole in holes)
            {
                double distance = hole.DistanceToOutline(x, y);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }
            return nearest;
        }

        /// <summary>
        /// True when this region's outline runs along the outline of <paramref name="other"/>.
        /// This is what separates a hole in the middle of a slab, which is an opening one
        /// model has and the other does not, from a slab edge the two models simply drew in
        /// different places.
        /// </summary>
        public bool TouchesOutlineOf(PlanPolygon other, double tolerance)
        {
            if (other == null)
            {
                return false;
            }

            foreach (MyPoint point in outer.Points)
            {
                if (other.outer.DistanceToOutline(point.X, point.Y) <= tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        public override string ToString()
        {
            return $"region {Math.Round(Area / 1e6, 3)} m2, {holes.Count} hole(s)";
        }
    }
}
