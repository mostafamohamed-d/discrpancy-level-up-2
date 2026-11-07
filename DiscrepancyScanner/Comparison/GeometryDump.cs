using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Everything known about a ring that the geometry kernel would not accept.
    ///
    /// The kernel says only that the curve loops do not satisfy its requirements, which is
    /// true and useless: it names no vertex, no edge and no reason. These are the faults it
    /// could have meant, tested here so a refusal comes back as something that can be looked
    /// up in the model rather than as a sentence to be guessed at.
    /// </summary>
    public static class PlanRingCheck
    {
        /// <summary>Revit rejects a curve shorter than this outright, in mm.</summary>
        public const double ShortCurveInMM = 304.8d / 384d;

        /// <summary>Above this many points the pairwise tests are skipped as too slow.</summary>
        private const int PairwiseLimit = 3000;

        public static IList<string> Faults(PlanRing ring)
        {
            List<string> faults = new List<string>();

            if (ring == null)
            {
                faults.Add("there is no ring");
                return faults;
            }

            IList<MyPoint> points = ring.Points;
            int count = points.Count;

            if (count < 3)
            {
                faults.Add($"only {count} point(s), which cannot enclose an area");
                return faults;
            }

            ShortEdges(points, faults);
            RepeatedVertices(points, faults);
            Spikes(points, faults);
            SelfIntersections(points, faults);

            if (faults.Count == 0)
            {
                faults.Add("no fault found by these tests - the outline looks sound from here, " +
                           "so the refusal is something the kernel sees and this does not");
            }

            return faults;
        }

        private static void ShortEdges(IList<MyPoint> points, List<string> faults)
        {
            int shortCount = 0;
            double shortest = double.MaxValue;
            int shortestAt = -1;

            for (int i = 0; i < points.Count; i++)
            {
                double length = Distance(points[i], points[(i + 1) % points.Count]);
                if (length < shortest)
                {
                    shortest = length;
                    shortestAt = i;
                }
                if (length < ShortCurveInMM)
                {
                    shortCount++;
                }
            }

            faults.Add($"shortest edge {Round(shortest)} mm at vertex {shortestAt}" +
                       (shortCount > 0
                           ? $"; {shortCount} edge(s) under the kernel's {Round(ShortCurveInMM)} mm limit"
                           : ""));
        }

        private static void RepeatedVertices(IList<MyPoint> points, List<string> faults)
        {
            if (points.Count > PairwiseLimit)
            {
                return;
            }

            List<string> hits = new List<string>();

            for (int i = 0; i < points.Count && hits.Count < 6; i++)
            {
                // Neighbours are meant to be apart; it is a vertex coming back round to one
                // it already visited that pinches the ring into two lobes the kernel cannot
                // read as one boundary.
                for (int j = i + 2; j < points.Count; j++)
                {
                    if (i == 0 && j == points.Count - 1)
                    {
                        continue;
                    }
                    if (Distance(points[i], points[j]) <= 1d)
                    {
                        hits.Add($"{i} and {j}");
                        break;
                    }
                }
            }

            if (hits.Count > 0)
            {
                faults.Add($"the ring returns to a point it already visited, at vertex pairs " +
                           string.Join(", ", hits) + " - it is pinched there rather than simple");
            }
        }

        private static void Spikes(IList<MyPoint> points, List<string> faults)
        {
            int spikes = 0;
            int firstAt = -1;

            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a = points[i];
                MyPoint b = points[(i + 1) % points.Count];
                MyPoint c = points[(i + 2) % points.Count];

                double abx = b.X - a.X, aby = b.Y - a.Y;
                double bcx = c.X - b.X, bcy = c.Y - b.Y;

                double abLength = Math.Sqrt(abx * abx + aby * aby);
                double bcLength = Math.Sqrt(bcx * bcx + bcy * bcy);
                if (abLength < 1e-9 || bcLength < 1e-9)
                {
                    continue;
                }

                double dot = (abx * bcx + aby * bcy) / (abLength * bcLength);

                // Doubling straight back on itself: the edge out of b runs back along the
                // edge into it, so the ring has zero width there.
                if (dot < -0.9999)
                {
                    spikes++;
                    if (firstAt < 0)
                    {
                        firstAt = (i + 1) % points.Count;
                    }
                }
            }

            if (spikes > 0)
            {
                faults.Add($"{spikes} place(s) where the ring doubles straight back on itself, " +
                           $"first at vertex {firstAt}");
            }
        }

        private static void SelfIntersections(IList<MyPoint> points, List<string> faults)
        {
            if (points.Count > PairwiseLimit)
            {
                faults.Add($"{points.Count} points, too many to test for crossings here");
                return;
            }

            int crossings = 0;
            List<string> firstFew = new List<string>();

            for (int i = 0; i < points.Count; i++)
            {
                MyPoint a1 = points[i];
                MyPoint a2 = points[(i + 1) % points.Count];

                for (int j = i + 1; j < points.Count; j++)
                {
                    // Edges that share a vertex are allowed to touch at it.
                    if (j == i || (j + 1) % points.Count == i || (i + 1) % points.Count == j)
                    {
                        continue;
                    }

                    if (!Crosses(a1, a2, points[j], points[(j + 1) % points.Count]))
                    {
                        continue;
                    }

                    crossings++;
                    if (firstFew.Count < 6)
                    {
                        firstFew.Add($"{i}x{j}");
                    }
                }
            }

            if (crossings > 0)
            {
                faults.Add($"the outline crosses itself {crossings} time(s), at edge pairs " +
                           string.Join(", ", firstFew) +
                           " - this is what the kernel refuses most often");
            }
        }

        /// <summary>Proper crossing: the two segments meet away from their end points.</summary>
        private static bool Crosses(MyPoint p1, MyPoint p2, MyPoint q1, MyPoint q2)
        {
            double d1 = Side(q1, q2, p1);
            double d2 = Side(q1, q2, p2);
            double d3 = Side(p1, p2, q1);
            double d4 = Side(p1, p2, q2);

            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
                && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Side(MyPoint a, MyPoint b, MyPoint p)
        {
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

            // Anything under a square millimetre of moment is the two lying on each other,
            // which is touching rather than crossing.
            return Math.Abs(cross) < 1d ? 0 : cross;
        }

        private static double Distance(MyPoint a, MyPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Round(double value)
        {
            return Math.Round(value, 4);
        }
    }

    /// <summary>
    /// A written record of the geometry the check could not work with, so an outline the
    /// kernel refuses can be looked at rather than guessed at.
    ///
    /// Off unless it is asked for. Every fault the check reports as a one line issue can be
    /// written here in full instead: which element, what was tried, what came back, what is
    /// wrong with the ring as far as this can tell, and the coordinates themselves, so the
    /// shape can be drawn out or fed back in.
    /// </summary>
    public static class GeometryDump
    {
        private static readonly object gate = new object();

        private static StreamWriter writer;

        /// <summary>Where the dump is being written, or null when it is not.</summary>
        public static string FilePath { get; private set; }

        /// <summary>How many faults have been written to it.</summary>
        public static int Entries { get; private set; }

        public static bool IsOpen { get { return writer != null; } }

        /// <summary>
        /// Starts a dump in the given folder, named for the moment it was started so one run
        /// never writes over another. Failing to open one is never worth stopping a check for,
        /// so it is reported by returning null rather than by throwing.
        /// </summary>
        /// <summary>A headline turned into something that can be a file name.</summary>
        private static string Slug(string headline)
        {
            if (string.IsNullOrEmpty(headline))
            {
                return "slab-check";
            }

            StringBuilder slug = new StringBuilder();
            foreach (char letter in headline.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(letter))
                {
                    slug.Append(letter);
                }
                else if (slug.Length > 0 && slug[slug.Length - 1] != '-')
                {
                    slug.Append('-');
                }
            }

            return slug.Length == 0 ? "slab-check" : slug.ToString().TrimEnd('-');
        }

        public static string Start(string folder, string headline)
        {
            Stop();

            try
            {
                if (string.IsNullOrEmpty(folder))
                {
                    folder = DefaultFolder();
                }

                Directory.CreateDirectory(folder);

                // Named after what was being done, because the import and the comparison each
                // write one and the two answer different questions. Told apart only by the
                // time they were written, the import one - which is where a floor being left
                // out of the check is recorded - is the easy one to open by mistake and the
                // easy one to overlook.
                string file = Path.Combine(folder,
                    Slug(headline) + "-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");

                lock (gate)
                {
                    writer = new StreamWriter(file, false, Encoding.UTF8);
                    FilePath = file;
                    Entries = 0;

                    writer.WriteLine("ETABS / Revit slab check - geometry dump");
                    writer.WriteLine(headline ?? "");
                    writer.WriteLine("started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    writer.WriteLine(new string('=', 78));
                    writer.WriteLine();
                    writer.Flush();
                }

                return file;
            }
            catch (Exception)
            {
                writer = null;
                FilePath = null;
                return null;
            }
        }

        public static string DefaultFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "DiscrepancyScanner-diagnostics");
        }

        public static void Stop()
        {
            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    writer.WriteLine();
                    writer.WriteLine(new string('=', 78));
                    writer.WriteLine($"{Entries} fault(s) written, finished " +
                                     DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    writer.Flush();
                    writer.Dispose();
                }
                catch (Exception)
                {
                    // A dump that cannot be closed tidily is still a dump.
                }

                writer = null;
            }
        }

        /// <summary>
        /// Writes one fault: what was being done, what came back, and the geometry it was
        /// being done to. Silent and harmless when no dump is open.
        /// </summary>
        public static void Fault(string subject, string attempted, string kernelSaid,
                                 params PlanPolygon[] regions)
        {
            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    Entries++;

                    writer.WriteLine(new string('-', 78));
                    writer.WriteLine($"[{Entries}] {subject}");
                    writer.WriteLine($"  doing   : {attempted}");
                    if (!string.IsNullOrEmpty(kernelSaid))
                    {
                        writer.WriteLine($"  kernel  : {Flatten(kernelSaid)}");
                    }

                    if (regions != null)
                    {
                        for (int i = 0; i < regions.Length; i++)
                        {
                            WriteRegion(regions.Length == 1 ? "region" : $"region {i + 1}", regions[i]);
                        }
                    }

                    writer.WriteLine();
                    writer.Flush();
                }
                catch (Exception)
                {
                    // Never let the record keeping break the run it is recording.
                }
            }
        }

        /// <summary>
        /// Writes a fault that is about numbers rather than about an outline: the detail is
        /// already worked out by the caller and just needs recording.
        /// </summary>
        public static void Note(string subject, string attempted, IEnumerable<string> detail)
        {
            lock (gate)
            {
                if (writer == null)
                {
                    return;
                }

                try
                {
                    Entries++;

                    writer.WriteLine(new string('-', 78));
                    writer.WriteLine($"[{Entries}] {subject}");
                    writer.WriteLine($"  doing   : {attempted}");

                    if (detail != null)
                    {
                        foreach (string line in detail)
                        {
                            writer.WriteLine("      " + line);
                        }
                    }

                    writer.WriteLine();
                    writer.Flush();
                }
                catch (Exception)
                {
                    // Never let the record keeping break the run it is recording.
                }
            }
        }

        private static void WriteRegion(string label, PlanPolygon region)
        {
            if (region == null)
            {
                writer.WriteLine($"  {label}  : none");
                return;
            }

            writer.WriteLine($"  {label}  : {Math.Round(region.Area / 1e6, 3)} m2, " +
                             $"{region.Holes.Count} hole(s), " +
                             $"bounds x {Math.Round(region.Bounds.MinX)}..{Math.Round(region.Bounds.MaxX)} " +
                             $"y {Math.Round(region.Bounds.MinY)}..{Math.Round(region.Bounds.MaxY)} mm");

            WriteRing("outline", region.Outer);

            int holes = Math.Min(region.Holes.Count, MaximumHolesPerRegion);
            for (int i = 0; i < holes; i++)
            {
                WriteRing($"hole {i + 1}", region.Holes[i]);
            }

            if (region.Holes.Count > holes)
            {
                writer.WriteLine($"    ({region.Holes.Count - holes} more hole(s) not written)");
            }
        }

        /// <summary>Coordinates written per ring before the rest are left out.</summary>
        private const int MaximumPointsPerRing = 300;

        /// <summary>Holes written per region before the rest are left out.</summary>
        private const int MaximumHolesPerRegion = 12;

        private static void WriteRing(string label, PlanRing ring)
        {
            if (ring == null)
            {
                writer.WriteLine($"    {label}: none");
                return;
            }

            writer.WriteLine($"    {label}: {ring.Points.Count} points, " +
                             $"{Math.Round(ring.Area / 1e6, 3)} m2, " +
                             $"{(ring.IsCounterClockwise ? "counter clockwise" : "clockwise")}");

            foreach (string fault in PlanRingCheck.Faults(ring))
            {
                writer.WriteLine($"      ! {fault}");
            }

            // The coordinates themselves, so the shape can be drawn out or fed back in - but
            // only so many. A basement raft has hundreds of points and a hundred and forty
            // holes, and writing every one of them for every fault is what made the file
            // unopenable. The faults above are what identify the ring; the points are there to
            // draw it, and a few hundred draw it well enough to see what is wrong.
            int cap = Math.Min(ring.Points.Count, MaximumPointsPerRing);

            writer.WriteLine($"      points (mm, x y one per line), {cap} of {ring.Points.Count}:");
            for (int i = 0; i < cap; i++)
            {
                MyPoint point = ring.Points[i];
                writer.WriteLine("        "
                    + point.X.ToString("0.###", CultureInfo.InvariantCulture) + " "
                    + point.Y.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        private static string Flatten(string text)
        {
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
