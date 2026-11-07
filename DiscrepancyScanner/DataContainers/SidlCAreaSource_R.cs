using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A Revit floor the SIDL_C area check weighs: a light weight concrete fill, modelled as a
    /// floor that is not structure and carried in ETABS as an area load in SIDL_C on the slab
    /// it sits on - never on a loading plan.
    ///
    /// Usually a ramp: the fill runs from nothing at one end to its full depth at the other, so
    /// it has no one thickness. What it has is a volume, and over its plan area that volume is
    /// an average depth - which is what any averaging of it in ETABS comes back to, however
    /// many pieces the engineer cut it into. See <see cref="SidlCAreaCompareEngine"/>.
    /// </summary>
    public sealed class SidlCAreaSource_R
    {
        private List<PlanPolygon> outline;

        public SidlCAreaSource_R(long revitId, IEnumerable<PlanPolygon> plan, double volumeCubicMM,
                                 double minimumThicknessMM, double maximumThicknessMM,
                                 double baseElevationMM)
        {
            RevitID = revitId;
            outline = (plan ?? Enumerable.Empty<PlanPolygon>()).Where(part => part != null).ToList();
            Volume = Math.Max(0, volumeCubicMM);
            MinimumThickness = Math.Max(0, minimumThicknessMM);
            MaximumThickness = Math.Max(MinimumThickness, maximumThicknessMM);
            BaseElevation = baseElevationMM;
        }

        public long RevitID { get; }

        public string TypeName { get; set; } = "";

        public string ElementName { get; set; } = "";

        /// <summary>The name off the settings list that made this a SIDL_C area load.</summary>
        public string MatchedName { get; set; } = "";

        /// <summary>
        /// The plan the fill covers, in the ETABS frame, in mm. Usually one region; a fill in
        /// separate pieces is every one of them, and they are weighed together.
        /// </summary>
        public IList<PlanPolygon> Plan { get { return outline; } }

        /// <summary>The fill's volume, in mm3.</summary>
        public double Volume { get; }

        /// <summary>The thinnest the fill gets, in mm: nothing at the foot of a ramp.</summary>
        public double MinimumThickness { get; }

        /// <summary>The deepest the fill gets, in mm.</summary>
        public double MaximumThickness { get; }

        /// <summary>
        /// Where its underside is, in mm, in the ETABS frame. What decides which story's slab it
        /// loads: it sits on that slab.
        /// </summary>
        public double BaseElevation { get; private set; }

        /// <summary>The story it sits on, once one has been given to it.</summary>
        public string Story { get; set; } = "";

        /// <summary>The plan area, in mm2.</summary>
        public double Area { get { return outline.Sum(part => part.Area); } }

        /// <summary>
        /// The average depth, in mm: the volume over the plan area. For a ramp from 0 to 500 it
        /// is 250 - the (0 + 0.5) / 2 of working it by hand - and it is the same number for a
        /// shape edited fill whose top is not one plane, where by hand there is no formula.
        /// </summary>
        public double AverageThickness
        {
            get
            {
                double area = Area;
                return area > 0 ? Volume / area : 0;
            }
        }

        private List<List<MyPoint>> topLoops = new List<List<MyPoint>>();
        private List<List<MyPoint>> bottomLoops = new List<List<MyPoint>>();
        private List<DepthFace> tops;
        private List<DepthFace> bottoms;

        /// <summary>
        /// The fill's faces looking up and looking down, each a loop of points with their
        /// heights, in the ETABS frame. What tells the depth at one place from the depth at
        /// another: a ramp's top is one sloping face, or a facet per slope.
        /// </summary>
        public void SetFaces(IEnumerable<IList<MyPoint>> top, IEnumerable<IList<MyPoint>> bottom)
        {
            topLoops = (top ?? Enumerable.Empty<IList<MyPoint>>()).Where(loop => loop != null && loop.Count >= 3)
                                                                   .Select(loop => loop.ToList()).ToList();
            bottomLoops = (bottom ?? Enumerable.Empty<IList<MyPoint>>()).Where(loop => loop != null && loop.Count >= 3)
                                                                         .Select(loop => loop.ToList()).ToList();
            tops = null;
            bottoms = null;
        }

        /// <summary>True where the faces were read, so the depth can be told place by place.</summary>
        public bool HasFaces { get { return topLoops.Count > 0 && bottomLoops.Count > 0; } }

        /// <summary>
        /// The depth of the fill at a point in plan, in mm: the top face over it less the bottom
        /// face under it. Null where either is not found there, and the average is then all
        /// there is to go on.
        /// </summary>
        public double? DepthAt(double x, double y)
        {
            if (!HasFaces)
            {
                return null;
            }

            tops = tops ?? DepthFace.From(topLoops);
            bottoms = bottoms ?? DepthFace.From(bottomLoops);

            DepthFace top = tops.FirstOrDefault(face => face.Covers(x, y));
            DepthFace bottom = bottoms.FirstOrDefault(face => face.Covers(x, y));
            if (top == null || bottom == null)
            {
                return null;
            }

            return Math.Max(0, top.ZAt(x, y) - bottom.ZAt(x, y));
        }

        /// <summary>
        /// What a depth of fill weighs, in kN/m2: the depth in light weight concrete, with the
        /// topping heavier by the difference between the two densities - then rounded up to the
        /// next <paramref name="roundUpTo"/>, the way it is typed into ETABS. Nought leaves it as it is.
        /// </summary>
        public static double WeightOf(double depthMM, double lightweightDensity, double toppingDensity,
                                      double toppingThicknessMM, double roundUpTo = 0)
        {
            double exact = depthMM / 1000d * lightweightDensity
                           + toppingThicknessMM / 1000d * (toppingDensity - lightweightDensity);
            return LineLoadHeight.RoundUp(exact, roundUpTo);
        }

        /// <summary>The name a sentence calls it by.</summary>
        public string Description
        {
            get { return "fill " + (TypeName.Length > 0 ? TypeName : ElementName); }
        }

        /// <summary>
        /// What the fill puts on the slab, in kN/m2: its average depth in light weight concrete,
        /// with the top of it - the topping - heavier by the difference between the two
        /// densities.
        ///
        ///     average depth x LWC density + topping x (topping density - LWC density)
        ///
        /// A ramp from 0 to 500 mm, LWC 10, topping 100 mm at 25: 0.25 x 10 + 0.1 x 15 = 4.
        ///
        /// Then rounded up to the next <paramref name="roundUpTo"/>, the way the engineer rounds it
        /// before typing it into ETABS: 4.56 to the next 0.5 is 5. Nought leaves it as it is.
        /// </summary>
        public double PerSquareMetre(double lightweightDensity, double toppingDensity, double toppingThicknessMM,
                                     double roundUpTo = 0)
        {
            return WeightOf(AverageThickness, lightweightDensity, toppingDensity, toppingThicknessMM, roundUpTo);
        }

        /// <summary>How <see cref="PerSquareMetre"/> was worked out, for the card.</summary>
        public string How(double lightweightDensity, double toppingDensity, double toppingThicknessMM,
                          double roundUpTo = 0)
        {
            double value = PerSquareMetre(lightweightDensity, toppingDensity, toppingThicknessMM);
            double rounded = PerSquareMetre(lightweightDensity, toppingDensity, toppingThicknessMM, roundUpTo);
            string depth = Math.Abs(MaximumThickness - MinimumThickness) > 1
                ? $"{Mm(MinimumThickness)} to {Mm(MaximumThickness)}, {Mm(AverageThickness)} on average"
                : Mm(AverageThickness);

            return $"{depth} x {LoadSet.Text(lightweightDensity)} kN/m3 + {Mm(toppingThicknessMM)} topping x "
                   + $"({LoadSet.Text(toppingDensity)} - {LoadSet.Text(lightweightDensity)}) kN/m3 "
                   + $"= {LoadSet.Text(value)} kN/m2"
                   + (Math.Abs(rounded - value) > 1e-9 ? $", rounded up to {LoadSet.Text(rounded)} kN/m2" : "");
        }

        /// <summary>Names it after the story nearest where it sits.</summary>
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

            outline = outline.Select(part => Moved(part, move)).Where(part => part != null).ToList();
            BaseElevation += move.Translation.Z;

            topLoops = topLoops.Select(loop => loop.Select(point => move.ApplyToPointInMM(point)).ToList()).ToList();
            bottomLoops = bottomLoops.Select(loop => loop.Select(point => move.ApplyToPointInMM(point)).ToList()).ToList();
            tops = null;
            bottoms = null;
        }

        private static PlanPolygon Moved(PlanPolygon polygon, MyTransformation move)
        {
            PlanRing outer = Moved(polygon.Outer, move);
            if (outer == null)
            {
                return null;
            }

            return PlanPolygon.Create(outer, polygon.Holes.Select(hole => Moved(hole, move))
                                                          .Where(hole => hole != null)
                                                          .ToList());
        }

        private static PlanRing Moved(PlanRing ring, MyTransformation move)
        {
            return ring == null
                ? null
                : PlanRing.FromPoints(ring.Points.Select(point => move.ApplyToPointInMM(point)));
        }

        private static string Mm(double millimetres)
        {
            return Math.Round(millimetres).ToString("0", CultureInfo.InvariantCulture) + " mm";
        }

        public override string ToString()
        {
            return $"{Description} ({RevitID}, {Story})";
        }
    }

    /// <summary>
    /// One flat face of a fill - a top or a bottom - as the region it covers in plan and the
    /// plane it lies in, so its height can be read anywhere over it.
    /// </summary>
    internal sealed class DepthFace
    {
        private readonly PlanPolygon plan;
        private readonly double nx, ny, nz, x0, y0, z0;

        private DepthFace(PlanPolygon plan, double nx, double ny, double nz, double x0, double y0, double z0)
        {
            this.plan = plan;
            this.nx = nx;
            this.ny = ny;
            this.nz = nz;
            this.x0 = x0;
            this.y0 = y0;
            this.z0 = z0;
        }

        public static List<DepthFace> From(IEnumerable<List<MyPoint>> loops)
        {
            List<DepthFace> faces = new List<DepthFace>();
            foreach (List<MyPoint> loop in loops)
            {
                // Newell's normal: the plane the loop lies in, however many points it has.
                double nx = 0, ny = 0, nz = 0;
                for (int i = 0; i < loop.Count; i++)
                {
                    MyPoint a = loop[i];
                    MyPoint b = loop[(i + 1) % loop.Count];
                    nx += (a.Y - b.Y) * (a.Z + b.Z);
                    ny += (a.Z - b.Z) * (a.X + b.X);
                    nz += (a.X - b.X) * (a.Y + b.Y);
                }

                // A face standing on edge has no height to read over a plan point.
                if (Math.Abs(nz) < 1e-9 * Math.Max(1, Math.Abs(nx) + Math.Abs(ny)))
                {
                    continue;
                }

                PlanPolygon plan = PlanPolygon.FromPoints(loop);
                if (plan == null)
                {
                    continue;
                }

                faces.Add(new DepthFace(plan, nx, ny, nz,
                                        loop.Average(point => point.X),
                                        loop.Average(point => point.Y),
                                        loop.Average(point => point.Z)));
            }
            return faces;
        }

        public bool Covers(double x, double y)
        {
            return plan.Bounds.Contains(x, y) && plan.Contains(x, y);
        }

        public double ZAt(double x, double y)
        {
            return z0 - (nx * (x - x0) + ny * (y - y0)) / nz;
        }
    }
}
