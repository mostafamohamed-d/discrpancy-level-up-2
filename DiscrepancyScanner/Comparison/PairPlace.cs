using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Where one side of a hand-set pairing stood when it was set, so the pairing can be
    /// dropped once either side has moved.
    ///
    /// A pairing is written against an ETABS unique name and a Revit element id, and neither
    /// of those says where anything is. ETABS hands its unique names out again when a model is
    /// renumbered, so the name a pairing was set on can end up on another beam on another
    /// floor - and the pairing, obeyed before any distance is measured, then pins a beam on
    /// L03 to one on L01. Keeping where the two stood is what lets the check see that a pairing
    /// is about something that is no longer there.
    ///
    /// Each side is kept in its own model's coordinates rather than the shared ones the check
    /// works in: ETABS as it is, Revit with the alignment taken back off. Kept in the shared
    /// system, the Revit side would seem to move every time the alignment was changed, and
    /// every pairing on the project would be dropped for a move nobody made.
    /// </summary>
    public sealed class PairPlace
    {
        /// <summary>
        /// How far a point can be from where it was and still be the same point, in mm. The
        /// allowance the wall stacks use for two ends being one end: below it is the rounding a
        /// model picks up being written out and read back in, not a move.
        /// </summary>
        public const double SamePlaceInMM = 10;

        // In the element's own model, in mm. What is kept, and what a move is measured on.
        private readonly List<MyPoint> own;

        // In the shared (ETABS) system, in mm, for measuring one side against the other. Null
        // on a place read back from the settings file, which is only ever compared with itself.
        private readonly List<MyPoint> shared;

        private PairPlace(string story, List<MyPoint> own, List<MyPoint> shared)
        {
            Story = story ?? "";
            this.own = own;
            this.shared = shared;
        }

        /// <summary>The story the element was on, for telling whether two could be one element.</summary>
        public string Story { get; }

        #region where each kind of element stands

        public static PairPlace Of(FrameData_e frame)
        {
            return frame == null ? null : OfEtabs(frame.Story, frame.Start, frame.End);
        }

        public static PairPlace Of(FrameData_r frame)
        {
            return frame == null ? null : OfRevit(frame.Story, frame.Start, frame.End);
        }

        /// <summary>Base and top, so a column carried up or down a floor has moved as well.</summary>
        public static PairPlace Of(Column_e column)
        {
            return column == null ? null : OfEtabs(column.Story, column.Start, column.End);
        }

        public static PairPlace Of(Column_r column)
        {
            return column == null ? null : OfRevit(column.Story, column.PlanPoint);
        }

        public static PairPlace Of(Wall_E wall)
        {
            return wall == null ? null : OfEtabs(wall.Story, wall.PlanStart, wall.PlanEnd);
        }

        public static PairPlace Of(Wall_R wall)
        {
            return wall == null ? null : OfRevit(wall.Story, wall.PlanStart, wall.PlanEnd);
        }

        public static PairPlace Of(LineLoadFrame_E frame)
        {
            return frame == null ? null : OfEtabs(frame.Story, frame.PlanStart, frame.PlanEnd);
        }

        /// <summary>
        /// A drawn line by its two ends. A curved line is walked into as many points as its
        /// tessellation gives, and the ends are what moving it moves.
        ///
        /// A line on a loading plan several stories share is on all of them at once, so it is
        /// placed on none in particular - otherwise a pairing set on the third of them would be
        /// held against whichever story happened to be read first, and taken for a different floor.
        /// </summary>
        public static PairPlace Of(LineLoad_R line)
        {
            if (line == null || line.Pieces.Count == 0)
            {
                return null;
            }

            return OfRevit(line.IsOnSeveralStories ? "" : line.Story, line.Pieces[0].PlanStart,
                           line.Pieces[line.Pieces.Count - 1].PlanEnd);
        }

        /// <summary>An ETABS element, whose own coordinates are the shared ones.</summary>
        public static PairPlace OfEtabs(string story, params MyPoint[] points)
        {
            if (!AllThere(points))
            {
                return null;
            }

            List<MyPoint> copied = points.Select(Copy).ToList();
            return new PairPlace(story, copied, copied);
        }

        /// <summary>
        /// A Revit element, from where it stands in the shared system. The alignment in force is
        /// taken back off it for what is kept - see the class summary for why.
        /// </summary>
        public static PairPlace OfRevit(string story, params MyPoint[] sharedPoints)
        {
            if (!AllThere(sharedPoints))
            {
                return null;
            }

            MyTransformation alignment = DataContainer.ModelTransformation ?? new MyTransformation();

            return new PairPlace(story,
                                 sharedPoints.Select(alignment.InvertPointInMM).ToList(),
                                 sharedPoints.Select(Copy).ToList());
        }

        /// <summary>
        /// Where each element stands, by ETABS unique name, for
        /// <see cref="PairingOverrides.Check"/>. The first of a name that comes twice is kept.
        /// </summary>
        public static Dictionary<string, PairPlace> ByName<T>(IEnumerable<T> elements,
                                                              Func<T, string> nameOf,
                                                              Func<T, PairPlace> placeOf)
        {
            Dictionary<string, PairPlace> places =
                new Dictionary<string, PairPlace>(StringComparer.OrdinalIgnoreCase);

            foreach (T element in elements ?? Enumerable.Empty<T>())
            {
                string name = element == null ? null : nameOf(element)?.Trim();
                PairPlace place = string.IsNullOrEmpty(name) ? null : placeOf(element);
                if (place != null && !places.ContainsKey(name))
                {
                    places[name] = place;
                }
            }
            return places;
        }

        /// <summary>Where each element stands, by Revit id. The first of an id that comes twice is kept.</summary>
        public static Dictionary<long, PairPlace> ById<T>(IEnumerable<T> elements,
                                                          Func<T, long> idOf,
                                                          Func<T, PairPlace> placeOf)
        {
            Dictionary<long, PairPlace> places = new Dictionary<long, PairPlace>();

            foreach (T element in elements ?? Enumerable.Empty<T>())
            {
                PairPlace place = element == null ? null : placeOf(element);
                if (place != null && !places.ContainsKey(idOf(element)))
                {
                    places[idOf(element)] = place;
                }
            }
            return places;
        }

        private static bool AllThere(MyPoint[] points)
        {
            return points != null && points.Length > 0 && points.All(point => point != null);
        }

        private static MyPoint Copy(MyPoint point)
        {
            return new MyPoint(point.X, point.Y, point.Z);
        }

        #endregion

        #region measuring

        /// <summary>
        /// How far this is from where <paramref name="earlier"/> was, in mm: the furthest any one
        /// point has gone. Taken either way round, because a line drawn again from its other
        /// end has not moved. Infinite when the two are not the same shape at all.
        /// </summary>
        public double DistanceFrom(PairPlace earlier)
        {
            if (earlier == null || earlier.own.Count != own.Count)
            {
                return double.PositiveInfinity;
            }

            double forwards = 0, backwards = 0;
            int last = own.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                forwards = Math.Max(forwards, own[i].DistanceTo(earlier.own[i]));
                backwards = Math.Max(backwards, own[i].DistanceTo(earlier.own[last - i]));
            }
            return Math.Min(forwards, backwards);
        }

        /// <summary>True when this stands where <paramref name="earlier"/> did.</summary>
        public bool IsWhere(PairPlace earlier)
        {
            return DistanceFrom(earlier) <= SamePlaceInMM;
        }

        /// <summary>
        /// True when the two stories could be the same one. An empty story is in reach of every
        /// story, the same bargain <see cref="IPlanLine.Story"/> makes, so a SAFE model that
        /// names no floors is not taken for being on the wrong one.
        /// </summary>
        public static bool OnOneStory(PairPlace a, PairPlace b)
        {
            return a == null || b == null || a.Story.Length == 0 || b.Story.Length == 0
                   || string.Equals(a.Story, b.Story, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// How close the two come in plan, in mm, measured on the shared system: zero where
        /// they cross or touch. Infinite when either was read back from the file and so has no
        /// shared position to measure.
        /// </summary>
        public static double PlanGap(PairPlace a, PairPlace b)
        {
            if (a?.shared == null || b?.shared == null)
            {
                return double.PositiveInfinity;
            }

            double gap = double.PositiveInfinity;
            foreach (MyPoint[] first in Segments(a.shared))
            {
                foreach (MyPoint[] second in Segments(b.shared))
                {
                    gap = Math.Min(gap, SegmentGap(first[0], first[1], second[0], second[1]));
                }
            }
            return gap;
        }

        /// <summary>A single point is a segment with no length, so it measures the same way.</summary>
        private static IEnumerable<MyPoint[]> Segments(List<MyPoint> points)
        {
            if (points.Count == 1)
            {
                yield return new[] { points[0], points[0] };
                yield break;
            }

            for (int i = 0; i < points.Count - 1; i++)
            {
                yield return new[] { points[i], points[i + 1] };
            }
        }

        private static double SegmentGap(MyPoint a1, MyPoint a2, MyPoint b1, MyPoint b2)
        {
            if (Cross(a1, a2, b1, b2))
            {
                return 0;
            }

            return Math.Min(Math.Min(PointToSegment(a1, b1, b2), PointToSegment(a2, b1, b2)),
                            Math.Min(PointToSegment(b1, a1, a2), PointToSegment(b2, a1, a2)));
        }

        private static bool Cross(MyPoint a1, MyPoint a2, MyPoint b1, MyPoint b2)
        {
            double d1 = Turn(b1, b2, a1), d2 = Turn(b1, b2, a2);
            double d3 = Turn(a1, a2, b1), d4 = Turn(a1, a2, b2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                   && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Turn(MyPoint from, MyPoint to, MyPoint point)
        {
            return (to.X - from.X) * (point.Y - from.Y) - (to.Y - from.Y) * (point.X - from.X);
        }

        private static double PointToSegment(MyPoint point, MyPoint start, MyPoint end)
        {
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;

            double along = lengthSquared <= 0
                ? 0
                : Math.Max(0, Math.Min(1, ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared));

            double x = start.X + along * dx - point.X;
            double y = start.Y + along * dy - point.Y;
            return Math.Sqrt(x * x + y * y);
        }

        #endregion

        #region the settings file

        /// <summary>
        /// The kept points as one line of text - "x,y,z;x,y,z" to a tenth of a millimetre - so a
        /// pairing is one readable line in the settings file rather than a dozen.
        /// </summary>
        public string ToText()
        {
            return string.Join(";", own.Select(point => string.Join(",",
                Number(point.X), Number(point.Y), Number(point.Z))));
        }

        /// <summary>A place read back from <see cref="ToText"/>, or null for text that is not one.</summary>
        public static PairPlace FromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            List<MyPoint> points = new List<MyPoint>();
            foreach (string part in text.Split(';'))
            {
                string[] values = part.Split(',');
                double x, y, z;
                if (values.Length != 3
                    || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                    || !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
                    || !double.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                {
                    return null;
                }
                points.Add(new MyPoint(x, y, z));
            }

            return new PairPlace("", points, null);
        }

        private static string Number(double value)
        {
            return Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
