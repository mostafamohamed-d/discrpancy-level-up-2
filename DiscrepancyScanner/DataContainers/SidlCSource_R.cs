using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A Revit element the SIDL_C check reads as a line load: a wall that is not structure,
    /// or a narrow strip of fill slab.
    ///
    /// These are the elements the wall and slab checks leave out - an upstand, a planter wall,
    /// a fill strip along an edge. ETABS does not model them as elements; it carries them as
    /// load, in a pattern of their own, on the frame they stand on. So what the Revit side has
    /// to say about one is what it weighs per metre run, and where it runs.
    ///
    /// Held apart from the walls and floors the other checks read, because it is read under a
    /// different list of names and asked a different question, and because which story it
    /// loads is the one it stands on rather than the one it rises to.
    /// </summary>
    public sealed class SidlCSource_R
    {
        private List<MyPoint> line;

        public SidlCSource_R(SidlCSourceKind kind, long revitId, IEnumerable<MyPoint> planLine,
                             double thicknessMM, double sizeMM, double baseElevationMM)
        {
            Kind = kind;
            RevitID = revitId;
            line = (planLine ?? Enumerable.Empty<MyPoint>()).Where(point => point != null).ToList();
            Thickness = thicknessMM;
            Size = sizeMM;
            BaseElevation = baseElevationMM;
        }

        public SidlCSourceKind Kind { get; }

        public long RevitID { get; }

        public string TypeName { get; set; } = "";

        public string ElementName { get; set; } = "";

        /// <summary>The name off the settings list that made this a SIDL_C load.</summary>
        public string MatchedName { get; set; } = "";

        /// <summary>Where it runs, in plan, in the ETABS frame, in mm.</summary>
        public IList<MyPoint> PlanLine { get { return line; } }

        /// <summary>Wall thickness, or the depth of the fill, in mm.</summary>
        public double Thickness { get; }

        /// <summary>The wall's height, or the fill strip's width, in mm.</summary>
        public double Size { get; }

        /// <summary>
        /// Which length of its wall this is, from 1, where the wall's height changes along it and
        /// each length at one height is a load of its own - see <see cref="WallProfile"/>.
        /// Nought for the whole of it.
        /// </summary>
        public int Part { get; set; }

        /// <summary>How many lengths its wall was cut into; nought or one for a wall at one height.</summary>
        public int Parts { get; set; }

        /// <summary>True where the height changes along this length - a slope - so <see cref="Size"/> is its average.</summary>
        public bool SizeVaries { get; set; }

        /// <summary>
        /// Where it stands, in mm, in the ETABS frame. What decides which story's frames it
        /// loads: a wall rising from L03 to L04 is a load on L03.
        /// </summary>
        public double BaseElevation { get; private set; }

        /// <summary>The story it stands on, once one has been given to it.</summary>
        public string Story { get; set; } = "";

        public double Length
        {
            get
            {
                double length = 0;
                for (int i = 0; i < line.Count - 1; i++)
                {
                    length += WallGeometry.PlanLength(line[i], line[i + 1]);
                }
                return length;
            }
        }

        /// <summary>What it weighs per metre run, in kN/m, before any rounding.</summary>
        public double PerMetre(double densityKNPerCubicMetre)
        {
            return Thickness / 1000d * Size / 1000d * densityKNPerCubicMetre;
        }

        /// <summary>The name a sentence calls it by: "wall Upstand-200", "fill strip Fill SLAB-100".</summary>
        public string Description
        {
            get
            {
                string name = TypeName.Length > 0 ? TypeName : ElementName;
                return (Kind == SidlCSourceKind.Wall ? "wall " : "fill strip ") + name
                       + (Part > 0 && Parts > 1 ? $", part {Part} of {Parts}" : "");
            }
        }

        /// <summary>The element's instance comment, as read. Where it states a load, that can be the load.</summary>
        public string Comment { get; set; } = "";

        /// <summary>The load the instance comment states, in kN/m, or nought where it states none.</summary>
        public double CommentLoad
        {
            get
            {
                double load;
                return TryReadLoad(Comment, out load) ? load : 0;
            }
        }

        /// <summary>
        /// What the element is read as weighing per metre, before any rounding or stepping: the
        /// load its comment states where that is asked for and it states one, otherwise its
        /// geometry times the density. What the lightest load worth checking is held against.
        /// </summary>
        public double Weighs(double densityKNPerCubicMetre, bool fromComment)
        {
            double stated = fromComment ? CommentLoad : 0;
            return stated > 0 ? stated : PerMetre(densityKNPerCubicMetre);
        }

        /// <summary>
        /// The weight as a line load on the story it stands on: thickness times height (or
        /// width) times the density, rounded up the way a plan line load is.
        /// </summary>
        public LineLoad_R ToLineLoad(double densityKNPerCubicMetre, double roundUpTo)
        {
            return ToLineLoad(densityKNPerCubicMetre, roundUpTo, null, false);
        }

        /// <summary>
        /// The same, by one of three paths. Where <paramref name="fromComment"/> is asked for and
        /// the instance comment states a load, that load, as it is. Otherwise the weight off the
        /// geometry, rounded up - and where <paramref name="intervals"/> are given, stepped up to
        /// the next of them: with 2, 6, 10, 14, 18 a wall weighing 3 is checked as 6, and one
        /// weighing more than 18 as what it weighs.
        /// </summary>
        public LineLoad_R ToLineLoad(double densityKNPerCubicMetre, double roundUpTo,
                                     IList<double> intervals, bool fromComment)
        {
            double stated = fromComment ? CommentLoad : 0;
            if (stated > 0)
            {
                return LineLoad_R.Weighed(RevitID, Description, line, stated,
                                          $"{LoadSet.Text(stated)} kN/m, from its instance comment \"{Comment.Trim()}\"",
                                          Story, Part);
            }

            double exact = PerMetre(densityKNPerCubicMetre);
            double rounded = LineLoadHeight.RoundUp(exact, roundUpTo);
            double stepped = StepUp(rounded, intervals);

            string how = $"{Mm(Thickness)} thick x {Metres(Size)} "
                         + (Kind == SidlCSourceKind.Wall ? "high" : "wide")
                         + (SizeVaries ? " on average" : "")
                         + $" x {LoadSet.Text(densityKNPerCubicMetre)} kN/m3";
            how += Math.Abs(rounded - exact) > 1e-9
                ? $" = {LoadSet.Text(exact)} kN/m, rounded up to {LoadSet.Text(rounded)}"
                : $" = {LoadSet.Text(rounded)} kN/m";
            if (Math.Abs(stepped - rounded) > 1e-9)
            {
                how += $", taken as the next interval, {LoadSet.Text(stepped)}";
            }
            else if (intervals != null && intervals.Count > 0 && rounded > intervals.Max())
            {
                how += ", above the last interval so taken as it is";
            }

            if (fromComment)
            {
                how += Comment.Trim().Length > 0
                    ? " (its instance comment states no load)"
                    : " (no instance comment)";
            }

            return LineLoad_R.Weighed(RevitID, Description, line, stepped, how, Story, Part);
        }

        /// <summary>
        /// The load up to the next of the intervals, or the load as it is where it is above the
        /// last of them or there are none. A load on an interval stays on it.
        /// </summary>
        public static double StepUp(double load, IList<double> intervals)
        {
            if (intervals == null || load <= 0)
            {
                return load;
            }

            foreach (double interval in intervals.OrderBy(value => value))
            {
                if (load <= interval + 1e-9)
                {
                    return interval;
                }
            }

            return load;
        }

        /// <summary>
        /// The intervals as typed - "2,6,10,14,18" - in order, with anything that is not a
        /// positive number left out. Commas, semicolons and spaces all separate them.
        /// </summary>
        public static List<double> ParseIntervals(string typed)
        {
            return (typed ?? "")
                .Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part =>
                {
                    double value;
                    return double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                        ? value
                        : 0;
                })
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        /// <summary>
        /// The load an instance comment states, in kN/m: a comment that is only a number -
        /// "6", "6.5" - or one with a number in kN/m in it - "6 kN/m", "SIDL_C 6.5kN/m".
        /// Anything else - "cast later", "level 2 upstand" - states no load, which is what keeps
        /// a stray number in a note from being read as one.
        /// </summary>
        public static bool TryReadLoad(string comment, out double load)
        {
            load = 0;
            string text = (comment ?? "").Trim();
            if (text.Length == 0)
            {
                return false;
            }

            Match match = Regex.Match(text, @"^(\d+(?:[.,]\d+)?)$");
            if (!match.Success)
            {
                match = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*kn\s*/\s*m(?![\s]*[23²³])(?![a-z])",
                                    RegexOptions.IgnoreCase);
            }

            if (!match.Success)
            {
                return false;
            }

            double value;
            if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out value) || value <= 0)
            {
                return false;
            }

            load = value;
            return true;
        }

        /// <summary>Names it after the story nearest where it stands.</summary>
        public void AssignStory(List<Story_E> stories)
        {
            if (stories == null || stories.Count == 0)
            {
                return;
            }

            Story_E nearest = Story_E.getNearestStory(BaseElevation, stories);
            if (nearest != null)
            {
                Story = nearest.Name;
            }
        }

        /// <summary>Moves it by a change of alignment, the same as every other Revit element.</summary>
        public void MoveBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            line = line.Select(point => move.ApplyToPointInMM(point)).ToList();

            // A move carries the Z of the alignment with it, and the elevation was read after
            // the alignment had been applied - so it moves by the same amount.
            BaseElevation += move.Translation.Z;
        }

        /// <summary>
        /// The centre line of a strip of floor and how wide it is, read off its outline as the
        /// smallest rectangle that holds it - which, for a strip, runs along it.
        ///
        /// False where the outline is not near enough a rectangle to have one centre line: an L
        /// or a bend, whose smallest rectangle is mostly air. Such a strip is reported rather
        /// than guessed at.
        /// </summary>
        public static bool StripAxis(IList<MyPoint> outline, double area,
                                     out MyPoint start, out MyPoint end, out double width)
        {
            start = null;
            end = null;
            width = 0;

            List<MyPoint> points = (outline ?? new List<MyPoint>()).Where(point => point != null).ToList();
            if (points.Count < 3)
            {
                return false;
            }

            double bestArea = double.MaxValue;
            double bestUx = 0, bestUy = 0;
            double bestMinU = 0, bestMaxU = 0, bestMinV = 0, bestMaxV = 0;

            // Every edge is tried as the long side's direction: the smallest rectangle round a
            // convex shape has a side along one of its edges, and the hull's edges are among
            // the outline's.
            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double edge = Math.Sqrt(dx * dx + dy * dy);
                if (edge < 1e-6)
                {
                    continue;
                }

                double ux = dx / edge;
                double uy = dy / edge;

                double minU = double.MaxValue, maxU = double.MinValue;
                double minV = double.MaxValue, maxV = double.MinValue;
                foreach (MyPoint point in points)
                {
                    double u = point.X * ux + point.Y * uy;
                    double v = -point.X * uy + point.Y * ux;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }

                double boxArea = (maxU - minU) * (maxV - minV);
                if (boxArea < bestArea)
                {
                    bestArea = boxArea;
                    bestUx = ux;
                    bestUy = uy;
                    bestMinU = minU;
                    bestMaxU = maxU;
                    bestMinV = minV;
                    bestMaxV = maxV;
                }
            }

            if (bestArea <= 0 || bestArea >= double.MaxValue)
            {
                return false;
            }

            // Mostly air: not one strip.
            if (area > 0 && area / bestArea < 0.85)
            {
                return false;
            }

            double alongU = bestMaxU - bestMinU;
            double alongV = bestMaxV - bestMinV;

            // The long side is the one the strip runs along.
            bool runsAlongU = alongU >= alongV;
            double length = runsAlongU ? alongU : alongV;
            width = runsAlongU ? alongV : alongU;
            if (length <= 0)
            {
                return false;
            }

            // The width a strip of that length needs to have the area it has, which reads a
            // strip whose edges are not quite parallel as what it averages.
            if (area > 0)
            {
                width = Math.Min(width, area / length);
            }

            double midU = (bestMinU + bestMaxU) / 2d;
            double midV = (bestMinV + bestMaxV) / 2d;
            double z = points.Average(point => point.Z);

            Func<double, double, MyPoint> fromUV = (u, v) =>
                new MyPoint(u * bestUx - v * bestUy, u * bestUy + v * bestUx, z);

            if (runsAlongU)
            {
                start = fromUV(bestMinU, midV);
                end = fromUV(bestMaxU, midV);
            }
            else
            {
                start = fromUV(midU, bestMinV);
                end = fromUV(midU, bestMaxV);
            }

            return true;
        }

        private static string Mm(double millimetres)
        {
            return Math.Round(millimetres).ToString("0", CultureInfo.InvariantCulture) + " mm";
        }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / 1000d, 2).ToString("0.00", CultureInfo.InvariantCulture) + " m";
        }

        public override string ToString()
        {
            return $"{Description} ({RevitID}, {Story})";
        }
    }

    public enum SidlCSourceKind
    {
        Wall,
        FillSlab
    }
}
