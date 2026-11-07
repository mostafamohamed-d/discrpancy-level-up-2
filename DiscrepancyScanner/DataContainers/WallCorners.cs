using System;
using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reads a Revit wall drawn past a junction, or short of one, as running to the junction.
    ///
    /// Revit leaves the choice to whoever drew the wall. At a corner one wall is often taken
    /// the full width of the building and the other butted into its face; the same corner
    /// drawn the other way round has the two lines mitred to meet on the point they cross; a
    /// third has one wall run through to the far face of the other. ETABS has no such choice.
    /// A wall run is meshed at the joint where the two centre lines cross, so its panels start
    /// and stop on that point whatever the drawing did.
    ///
    /// What that leaves is two models disagreeing about the length of a wall by half the
    /// thickness of whatever it runs into, at every corner in the building, and about where
    /// its end sits by the same. It is a drawing convention rather than a difference anybody
    /// wants reported, and worse, it is spent out of the buffers that are there to catch the
    /// real ones: a wall run through a 400 core at both ends reads as 400 longer than ETABS
    /// has it, which is most of the length buffer gone before a real difference is measured.
    /// So the ends are read as sitting where the two centre lines cross, which is where ETABS
    /// would have put them.
    ///
    /// Only the ends move, and only onto a crossing already within half the other wall's
    /// thickness of them - what the conventions above can actually account for. A wall that
    /// genuinely stops a metre short of the one it should meet is left exactly where it was
    /// drawn: that is the thing the check exists to find.
    ///
    /// Nothing here touches Revit, so the rule can be tested without a model open.
    /// </summary>
    public static class WallCorners
    {
        /// <summary>
        /// Two walls nearer to parallel than this are one wall run drawn slightly off rather
        /// than a corner, and where their lines happen to cross says nothing about either of
        /// them. Kept above <see cref="PrefrencesData.WallAngleTolerance"/>, which is the angle
        /// at which the comparison stops reading two walls as the same wall.
        /// </summary>
        private const double LeastCornerAngle = 20;

        /// <summary>How wide a square the junction search files walls into, in mm.</summary>
        private const double SquareSizeMM = 2000;

        /// <summary>
        /// An end already this near its corner, in mm, was drawn to it. It is not moved and it
        /// is not counted: the import says how many walls this changed, and a count that took
        /// in every wall already drawn the way ETABS wanted would say it did far more than it
        /// did.
        /// </summary>
        private const double AlreadyThereMM = 0.1;

        /// <summary>
        /// Moves the end of every wall that meets another onto the point where the two centre
        /// lines cross. Returns how many walls were moved.
        ///
        /// Every end is decided against the lines as they were drawn, and nothing is moved
        /// until all of them are decided. Snapping as it went would let a wall already moved
        /// carry the next one along with it, and what the import produced would then depend on
        /// the order the model happened to be read in.
        /// </summary>
        /// <param name="walls">The walls to work on, in the shared frame and in mm.</param>
        /// <param name="cornerBuffer">
        /// How far past half the other wall's thickness the search reaches - see
        /// <see cref="PrefrencesData.WallCornerBuffer"/>.
        /// </param>
        public static int SnapToJunctions(IList<Wall_R> walls, double cornerBuffer)
        {
            List<Drawn> drawn = AsDrawn(walls);
            if (drawn.Count < 2)
            {
                return 0;
            }

            PlanSquares squares = new PlanSquares();
            for (int i = 0; i < drawn.Count; i++)
            {
                squares.Add(i, drawn[i].Start, drawn[i].End);
            }

            int moved = 0;

            foreach (Drawn wall in drawn)
            {
                MyPoint start = JunctionAt(wall, wall.Start, wall.End, drawn, squares, cornerBuffer);
                MyPoint end = JunctionAt(wall, wall.End, wall.Start, drawn, squares, cornerBuffer);

                if (start == null && end == null)
                {
                    continue;
                }

                MyPoint movedStart = start ?? wall.Start;
                MyPoint movedEnd = end ?? wall.End;

                if (WallGeometry.PlanLength(wall.Start, movedStart) <= AlreadyThereMM
                    && WallGeometry.PlanLength(wall.End, movedEnd) <= AlreadyThereMM)
                {
                    continue;
                }

                // A wall short enough that its two corners land on top of each other is not a
                // wall any more, and is left as it was drawn rather than collapsed.
                if (WallGeometry.PlanLength(movedStart, movedEnd) <= 0)
                {
                    continue;
                }

                wall.Wall.SetPlanLine(movedStart, movedEnd);
                moved++;
            }

            return moved;
        }

        /// <summary>
        /// Where one end of a wall should sit if it is at a junction, or null where it is not
        /// at one.
        ///
        /// Four things have to hold, and each of them is a way of being at a corner rather
        /// than merely near one. The two walls have to turn away from each other; the crossing
        /// has to be within reach of this end; it has to be this end's crossing rather than the
        /// far end's or one through the middle of the wall; and it has to be on the other wall
        /// rather than off the end of it. Where more than one wall qualifies - a wall ending in
        /// the corner of a lift core meets two - the nearest crossing is the corner, because
        /// the drawing conventions this undoes are all short moves.
        /// </summary>
        private static MyPoint JunctionAt(Drawn wall, MyPoint at, MyPoint away,
                                          List<Drawn> drawn, PlanSquares squares, double cornerBuffer)
        {
            MyPoint corner = null;
            double nearest = double.MaxValue;

            foreach (int index in squares.Near(at))
            {
                Drawn other = drawn[index];
                if (ReferenceEquals(other, wall))
                {
                    continue;
                }

                if (ColumnGeometry.AngularDifference(WallGeometry.AngleDegrees(at, away),
                                                     WallGeometry.AngleDegrees(other.Start, other.End),
                                                     180d) < LeastCornerAngle)
                {
                    continue;
                }

                MyPoint crossing = Crossing(at, away, other.Start, other.End);
                if (crossing == null)
                {
                    continue;
                }

                // Near enough to be this corner. A wall butted into another one's face stops
                // half that wall's thickness short of the crossing, and one run through to its
                // far face goes half a thickness past it; the buffer is the drafting on top.
                double reach = WallGeometry.PlanLength(at, crossing);
                if (reach > other.Wall.Thickness / 2d + cornerBuffer)
                {
                    continue;
                }

                // This end's corner and not the far end's, which is what keeps a wall crossed
                // near its middle - a short wall between two thick ones - from being pulled in
                // on itself from both directions.
                if (reach >= WallGeometry.PlanLength(away, crossing))
                {
                    continue;
                }

                // On the other wall rather than off the end of it, give or take what this wall
                // stopping at its face leaves over: a corner where both walls stop short of
                // each other puts the crossing past the end of both of them.
                if (!Along(crossing, other.Start, other.End, wall.Wall.Thickness / 2d + cornerBuffer))
                {
                    continue;
                }

                if (reach < nearest)
                {
                    nearest = reach;
                    corner = new MyPoint(crossing.X, crossing.Y, at.Z);
                }
            }

            return corner;
        }

        /// <summary>
        /// Where the lines through two segments cross, or null where they are parallel. The
        /// lines rather than the segments: a wall butted into another one's face crosses it
        /// beyond its own end, which is the whole reason for asking.
        /// </summary>
        private static MyPoint Crossing(MyPoint a1, MyPoint a2, MyPoint b1, MyPoint b2)
        {
            double ax = a2.X - a1.X;
            double ay = a2.Y - a1.Y;
            double bx = b2.X - b1.X;
            double by = b2.Y - b1.Y;

            double cross = ax * by - ay * bx;
            if (cross == 0)
            {
                return null;
            }

            double along = ((b1.X - a1.X) * by - (b1.Y - a1.Y) * bx) / cross;

            return new MyPoint(a1.X + ax * along, a1.Y + ay * along, a1.Z);
        }

        /// <summary>
        /// Whether a point falls on a segment, allowing it to run off either end by the given
        /// overrun. Measured along the segment only - the point is on the line already.
        /// </summary>
        private static bool Along(MyPoint point, MyPoint start, MyPoint end, double overrun)
        {
            double length = WallGeometry.PlanLength(start, end);
            if (length <= 0)
            {
                return false;
            }

            double along = ((point.X - start.X) * (end.X - start.X)
                            + (point.Y - start.Y) * (end.Y - start.Y)) / length;

            return along >= -overrun && along <= length + overrun;
        }

        private static List<Drawn> AsDrawn(IList<Wall_R> walls)
        {
            List<Drawn> drawn = new List<Drawn>();

            if (walls == null)
            {
                return drawn;
            }

            foreach (Wall_R wall in walls)
            {
                if (wall != null && wall.Length > 0)
                {
                    drawn.Add(new Drawn(wall));
                }
            }

            return drawn;
        }

        /// <summary>One wall's line as it was drawn, held aside while every end is decided.</summary>
        private sealed class Drawn
        {
            public Drawn(Wall_R wall)
            {
                Wall = wall;
                Start = wall.PlanStart;
                End = wall.PlanEnd;
            }

            public Wall_R Wall { get; }
            public MyPoint Start { get; }
            public MyPoint End { get; }
        }

        /// <summary>
        /// Which walls pass anywhere near a point, without going through all of them.
        ///
        /// A corner is a local thing - the crossing has to be within half a wall thickness of
        /// the end - so each wall is filed once into every square its line covers, and an end
        /// then only looks at the square it falls in and the ring around it. Without it every
        /// wall in a tower is measured against every other wall in it, which is the one part
        /// of the import that would grow with the square of the model. A wall filed in more
        /// than one of the nine squares comes back more than once; what the search concludes
        /// does not depend on how often a wall is offered, so they are not weeded out.
        /// </summary>
        private sealed class PlanSquares
        {
            private readonly Dictionary<long, List<int>> bySquare = new Dictionary<long, List<int>>();

            public void Add(int index, MyPoint start, MyPoint end)
            {
                int fromX = SquareOf(Math.Min(start.X, end.X));
                int toX = SquareOf(Math.Max(start.X, end.X));
                int fromY = SquareOf(Math.Min(start.Y, end.Y));
                int toY = SquareOf(Math.Max(start.Y, end.Y));

                for (int x = fromX; x <= toX; x++)
                {
                    for (int y = fromY; y <= toY; y++)
                    {
                        long key = KeyOf(x, y);

                        List<int> here;
                        if (!bySquare.TryGetValue(key, out here))
                        {
                            here = new List<int>();
                            bySquare[key] = here;
                        }

                        here.Add(index);
                    }
                }
            }

            public IEnumerable<int> Near(MyPoint point)
            {
                int atX = SquareOf(point.X);
                int atY = SquareOf(point.Y);

                for (int x = atX - 1; x <= atX + 1; x++)
                {
                    for (int y = atY - 1; y <= atY + 1; y++)
                    {
                        List<int> here;
                        if (!bySquare.TryGetValue(KeyOf(x, y), out here))
                        {
                            continue;
                        }

                        foreach (int index in here)
                        {
                            yield return index;
                        }
                    }
                }
            }

            private static int SquareOf(double atMM)
            {
                return (int)Math.Floor(atMM / SquareSizeMM);
            }

            private static long KeyOf(int x, int y)
            {
                return ((long)x << 32) | (uint)y;
            }
        }
    }
}
