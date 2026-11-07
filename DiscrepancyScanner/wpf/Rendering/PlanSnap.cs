using System;
using System.Collections.Generic;
using System.Windows;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>Which model a picked point is being taken from.</summary>
    public enum PlanSide
    {
        /// <summary>The Revit model, as it is drawn under the alignment in force.</summary>
        Revit,
        /// <summary>The ETABS or SAFE model, which is drawn where it was modelled.</summary>
        Etabs
    }

    /// <summary>
    /// What a picked point landed on. Ranked in this order, so a corner two pixels further
    /// away than an edge still wins: a person aiming near a corner meant the corner.
    /// </summary>
    public enum SnapKind
    {
        /// <summary>Nothing was near enough, so the point is wherever the cursor was.</summary>
        Free,
        /// <summary>The end of a beam or of a wall centre line.</summary>
        Endpoint,
        /// <summary>A corner of a column, a wall or a slab outline.</summary>
        Corner,
        /// <summary>The centre of a column.</summary>
        Centre,
        /// <summary>Where two drawn lines cross - the usual way to hit a grid intersection.</summary>
        Intersection,
        /// <summary>Halfway along a drawn line.</summary>
        Midpoint,
        /// <summary>The nearest point on a line, when nothing better was in reach.</summary>
        Edge
    }

    /// <summary>One point on an element worth snapping to, in model mm.</summary>
    public struct SnapPoint
    {
        public SnapPoint(Point at, SnapKind kind)
        {
            At = at;
            Kind = kind;
        }

        public Point At { get; }
        public SnapKind Kind { get; }
    }

    /// <summary>One straight run of an element, for the midpoint, crossing and edge snaps.</summary>
    public struct SnapEdge
    {
        public SnapEdge(Point from, Point to)
        {
            From = from;
            To = to;
        }

        public Point From { get; }
        public Point To { get; }
    }

    /// <summary>Where a pick ended up and what it caught, for the marker drawn under the cursor.</summary>
    public sealed class SnapResult
    {
        public SnapResult(Point at, SnapKind kind)
        {
            At = at;
            Kind = kind;
        }

        public Point At { get; }
        public SnapKind Kind { get; }

        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case SnapKind.Endpoint: return "endpoint";
                    case SnapKind.Corner: return "corner";
                    case SnapKind.Centre: return "centre";
                    case SnapKind.Intersection: return "intersection";
                    case SnapKind.Midpoint: return "midpoint";
                    case SnapKind.Edge: return "on the edge";
                    default: return "free point";
                }
            }
        }
    }

    /// <summary>
    /// A point the plan is being asked for, and where the answer is to go.
    ///
    /// The alignment panel raises one of these instead of asking the person to read a
    /// coordinate off one program and type it into another: the two models are already drawn
    /// on top of each other, so the point and the point it should land on are both on screen.
    /// </summary>
    public sealed class PlanPickRequest
    {
        public PlanPickRequest(string prompt, PlanSide side, Action<Point> onPicked, Action onCancelled)
        {
            Prompt = prompt ?? "";
            Side = side;
            OnPicked = onPicked;
            OnCancelled = onCancelled;
        }

        /// <summary>What to ask for, drawn across the top of the plan while the pick is on.</summary>
        public string Prompt { get; }

        /// <summary>Which model the point is being taken from; nothing else is snapped to.</summary>
        public PlanSide Side { get; }

        /// <summary>Given the picked point in model mm.</summary>
        public Action<Point> OnPicked { get; }

        /// <summary>Given nothing, when the pick was called off with escape or a right click.</summary>
        public Action OnCancelled { get; }
    }

    /// <summary>
    /// Finds the point on the drawn model nearest the cursor, so an alignment picked off the
    /// plan lands on real geometry rather than wherever the hand stopped.
    ///
    /// It works off the points the elements themselves offer rather than off their drawn
    /// outlines, because the outline of a round column is a hundred points on a circle and
    /// not one of them is the point anybody means by that column.
    /// </summary>
    public static class PlanSnap
    {
        /// <summary>
        /// Crossings are worked out pairwise, so a cursor sat over a dense slab could be asked
        /// for thousands of them. Past this many lines in reach the crossing snap is dropped:
        /// the endpoints and the corners are all still offered, and a place that crowded has
        /// no shortage of them.
        /// </summary>
        private const int MostLinesCrossed = 48;

        /// <summary>
        /// Which model an element was drawn from. A paired element is drawn from its Revit
        /// half, so that is the side it answers for.
        /// </summary>
        public static PlanSide SideOf(ElementViewModel element)
        {
            switch (element.Source)
            {
                case ElementSource.EtabsOnly:
                case ElementSource.UnmatchedEtabs:
                    return PlanSide.Etabs;
                default:
                    return PlanSide.Revit;
            }
        }

        /// <summary>
        /// The best point within <paramref name="tolerance"/> of the cursor, or the cursor
        /// itself when the nearest geometry on that side is further off than that. A null
        /// side snaps to either model.
        /// </summary>
        public static SnapResult Nearest(IEnumerable<ElementViewModel> elements, Point at,
                                         double tolerance, PlanSide? side)
        {
            SnapKind bestKind = SnapKind.Free;
            double bestDistance = double.MaxValue;
            Point best = at;

            void Offer(Point candidate, SnapKind kind)
            {
                double distance = Distance(candidate, at);
                if (distance > tolerance)
                {
                    return;
                }

                // Rank first and distance second, so a corner a little further off than the
                // edge running into it is still the point that was meant.
                if (Rank(kind) > Rank(bestKind) || (kind == bestKind && distance < bestDistance))
                {
                    bestKind = kind;
                    bestDistance = distance;
                    best = candidate;
                }
            }

            List<SnapEdge> inReach = new List<SnapEdge>();

            if (elements != null)
            {
                Rect reach = new Rect(at.X - tolerance, at.Y - tolerance, tolerance * 2, tolerance * 2);

                foreach (ElementViewModel element in elements)
                {
                    if (side.HasValue && PlanSnap.SideOf(element) != side.Value)
                    {
                        continue;
                    }

                    Rect bounds = element.Geometry.Bounds;
                    if (!bounds.IsEmpty && !bounds.IntersectsWith(reach))
                    {
                        continue;
                    }

                    foreach (SnapPoint point in element.SnapPoints)
                    {
                        Offer(point.At, point.Kind);
                    }

                    foreach (SnapEdge edge in element.SnapEdges)
                    {
                        Point closest = ClosestOn(edge, at);
                        if (Distance(closest, at) > tolerance)
                        {
                            continue;
                        }

                        inReach.Add(edge);
                        Offer(new Point((edge.From.X + edge.To.X) / 2, (edge.From.Y + edge.To.Y) / 2),
                              SnapKind.Midpoint);
                        Offer(closest, SnapKind.Edge);
                    }
                }
            }

            if (inReach.Count <= MostLinesCrossed)
            {
                for (int i = 0; i < inReach.Count; i++)
                {
                    for (int j = i + 1; j < inReach.Count; j++)
                    {
                        Point crossing;
                        if (TryCross(inReach[i], inReach[j], out crossing))
                        {
                            Offer(crossing, SnapKind.Intersection);
                        }
                    }
                }
            }

            return new SnapResult(best, bestKind);
        }

        /// <summary>How much a kind of snap is preferred, higher first.</summary>
        private static int Rank(SnapKind kind)
        {
            switch (kind)
            {
                case SnapKind.Endpoint:
                case SnapKind.Corner:
                case SnapKind.Centre:
                    return 4;
                case SnapKind.Intersection:
                    return 3;
                case SnapKind.Midpoint:
                    return 2;
                case SnapKind.Edge:
                    return 1;
                default:
                    return 0;
            }
        }

        private static Point ClosestOn(SnapEdge edge, Point at)
        {
            double dx = edge.To.X - edge.From.X;
            double dy = edge.To.Y - edge.From.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9)
            {
                return edge.From;
            }

            double k = ((at.X - edge.From.X) * dx + (at.Y - edge.From.Y) * dy) / lengthSquared;
            k = Math.Max(0, Math.Min(1, k));
            return new Point(edge.From.X + k * dx, edge.From.Y + k * dy);
        }

        /// <summary>Where two segments cross, when they cross within both of their own lengths.</summary>
        private static bool TryCross(SnapEdge first, SnapEdge second, out Point crossing)
        {
            crossing = new Point();

            double ax = first.To.X - first.From.X;
            double ay = first.To.Y - first.From.Y;
            double bx = second.To.X - second.From.X;
            double by = second.To.Y - second.From.Y;

            double denominator = ax * by - ay * bx;
            if (Math.Abs(denominator) < 1e-9)
            {
                // Parallel, or one of them has no length: no single crossing to offer.
                return false;
            }

            double dx = second.From.X - first.From.X;
            double dy = second.From.Y - first.From.Y;

            double alongFirst = (dx * by - dy * bx) / denominator;
            double alongSecond = (dx * ay - dy * ax) / denominator;

            if (alongFirst < 0 || alongFirst > 1 || alongSecond < 0 || alongSecond > 1)
            {
                return false;
            }

            crossing = new Point(first.From.X + alongFirst * ax, first.From.Y + alongFirst * ay);
            return true;
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
