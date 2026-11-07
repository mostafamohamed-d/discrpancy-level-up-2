using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A patch of one level, drawn on the plan by hand, where the line loads are worked out from
    /// a height of its own instead of the level's floor to floor height: a double height lobby,
    /// a plant room with a taller storey, a mezzanine that makes the storey above shorter.
    ///
    /// The height stands in for the floor to floor height and nothing else. The allowance for
    /// the slab still comes off it, and a load the schedule calls full height still runs the
    /// whole of it - exactly as for the level's own height, with this number in its place. It
    /// is read by the line load check alone.
    ///
    /// Drawn in the plan's own coordinates, which are the design model's, so it lies where it was
    /// drawn whatever is later done to the alignment of the Revit model.
    /// </summary>
    public sealed class HeightZone
    {
        private PlanPolygon plan;

        /// <summary>What the zone is filed under, so it can be found again to be edited or taken away.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>The level it was drawn on, by the design model's story name.</summary>
        public string Story { get; set; } = "";

        /// <summary>What somebody called it, if anything: "lobby", "plant".</summary>
        public string Name { get; set; } = "";

        /// <summary>The floor to floor height it stands for, in mm.</summary>
        public double Height { get; set; }

        /// <summary>The corners, in order, in plan mm. Not closed: the last corner joins the first.</summary>
        public IList<MyPoint> Outline { get; private set; } = new List<MyPoint>();

        public void SetOutline(IEnumerable<MyPoint> corners)
        {
            Outline = (corners ?? new MyPoint[0]).Where(point => point != null)
                                                .Select(point => new MyPoint(point.X, point.Y, 0))
                                                .ToList();
            plan = null;
        }

        /// <summary>The outline as a region, or null for one with fewer than three corners.</summary>
        public PlanPolygon Plan
        {
            get
            {
                if (plan == null && Outline.Count >= 3)
                {
                    plan = PlanPolygon.FromPoints(Outline);
                }
                return plan;
            }
        }

        /// <summary>How much of the level it covers, in square mm.</summary>
        public double Area => Plan == null ? 0 : Plan.Area;

        public bool Contains(double x, double y)
        {
            return Plan != null && Plan.Contains(x, y);
        }

        /// <summary>How it is named in a sentence that goes on to give its height: "height zone "lobby"".</summary>
        public string Label => Name.Length > 0 ? "height zone \"" + Name + "\"" : "a height zone";

        /// <summary>How it is named in a sentence: its name where it has one, otherwise its height.</summary>
        public string Description
        {
            get
            {
                string height = Metres(Height);
                return Name.Length > 0
                    ? "height zone \"" + Name + "\" (" + height + ")"
                    : "a height zone of " + height;
            }
        }

        /// <summary>The same zone again on another level, with a name of its own to be filed under.</summary>
        public HeightZone CopyTo(string story)
        {
            HeightZone copy = new HeightZone { Story = story ?? "", Name = Name, Height = Height };
            copy.SetOutline(Outline);
            return copy;
        }

        /// <summary>True when two zones are drawn round the same corners, in the same order.</summary>
        public bool SameOutlineAs(HeightZone other)
        {
            return other != null && other.Outline.Count == Outline.Count
                   && Outline.Zip(other.Outline, (a, b) => Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1)
                             .All(same => same);
        }

        internal static string Metres(double millimetres)
        {
            return (millimetres / 1000d).ToString("0.00", CultureInfo.CurrentCulture) + " m";
        }
    }

    /// <summary>
    /// Every height zone drawn, by project, and which of them a drawn line load answers to.
    ///
    /// Kept in the settings file beside the other things decided by hand, keyed by project and
    /// then by nothing more - a zone says which level it is on - so a project's zones go
    /// wherever its pairings and acceptances go, into a project file included.
    /// </summary>
    public sealed class HeightZones
    {
        /// <summary>Where these sit in the settings file.</summary>
        public const string Branch = "heightZones";

        /// <summary>How far apart the line is sampled when working out which zone it lies in, in mm.</summary>
        private const double SampleStep = 50;

        /// <summary>
        /// The colours the zones are drawn in, one per height. None is a verdict's, a model's,
        /// the selection's or the picking marker's, so a zone never reads as a result, and they
        /// are far enough apart that two heights side by side are told apart at a glance.
        /// </summary>
        public static readonly string[] Colours =
        {
            "#1565C0", "#6A1B9A", "#00796B", "#795548", "#827717", "#AD1457", "#455A64", "#5C6BC0"
        };

        /// <summary>
        /// The colour zones of one height are drawn in, "#RRGGBB": every height in
        /// <paramref name="heights"/>, lowest first, takes the next of <see cref="Colours"/>. So
        /// the colour says the height - two zones of one height are one colour, on any level -
        /// and two heights are two colours, up to as many as there are.
        /// </summary>
        public static string ColourOfHeight(IEnumerable<double> heights, double height)
        {
            List<long> distinct = (heights ?? new double[0]).Select(WholeMillimetres)
                                                            .Distinct()
                                                            .OrderBy(each => each)
                                                            .ToList();
            int at = distinct.IndexOf(WholeMillimetres(height));
            return Colours[Math.Max(0, at) % Colours.Length];
        }

        /// <summary>The colour zones of this height are drawn in, among every height in the project.</summary>
        public string ColourOf(string project, double height)
        {
            return ColourOfHeight(ForProject(project).Select(zone => zone.Height), height);
        }

        private static long WholeMillimetres(double millimetres)
        {
            return (long)Math.Round(millimetres);
        }

        private readonly Dictionary<string, List<HeightZone>> byProject =
            new Dictionary<string, List<HeightZone>>(StringComparer.OrdinalIgnoreCase);

        #region the store

        /// <summary>Every zone of one project, on every level.</summary>
        public IList<HeightZone> ForProject(string project)
        {
            List<HeightZone> zones;
            return byProject.TryGetValue(Key(project), out zones)
                ? (IList<HeightZone>)zones.ToList()
                : new List<HeightZone>();
        }

        /// <summary>The zones drawn on one level of one project.</summary>
        public IList<HeightZone> OnStory(string project, string story)
        {
            return ForProject(project)
                .Where(zone => string.Equals(zone.Story, story ?? "", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public HeightZone Find(string project, string id)
        {
            return ForProject(project).FirstOrDefault(zone => zone.Id == id);
        }

        public int CountFor(string project)
        {
            return ForProject(project).Count;
        }

        /// <summary>Keeps a zone. One that is not a region - fewer than three corners, or no height - is not kept.</summary>
        public bool Add(string project, HeightZone zone)
        {
            if (Key(project).Length == 0 || zone == null || zone.Plan == null || zone.Height <= 0)
            {
                return false;
            }

            List<HeightZone> zones;
            if (!byProject.TryGetValue(Key(project), out zones))
            {
                zones = new List<HeightZone>();
                byProject[Key(project)] = zones;
            }

            // A zone kept again - edited - keeps its place, which is what breaks the tie between
            // two zones the same size where they overlap.
            int at = zones.FindIndex(existing => existing.Id == zone.Id);
            zones.RemoveAll(existing => existing.Id == zone.Id);

            if (at >= 0 && at <= zones.Count)
            {
                zones.Insert(at, zone);
            }
            else
            {
                zones.Add(zone);
            }
            return true;
        }

        /// <summary>True when two zones share some of the plan, rather than only an edge.</summary>
        private static bool Overlap(HeightZone a, HeightZone b)
        {
            return a != null && b != null && a.Plan != null && b.Plan != null && !ReferenceEquals(a, b)
                   && a.Plan.Bounds.IntersectsWith(b.Plan.Bounds)
                   && !PlanRegionQuick.Disjoint(a.Plan, b.Plan);
        }

        /// <summary>
        /// The zones in the order they answer for a point: the smaller first, so where two
        /// overlap the smaller one is used there - the way a small patch drawn over a large one
        /// is the exception to it. Two the same size go in the order they were kept.
        /// </summary>
        public static IList<HeightZone> InOrderOfPrecedence(IEnumerable<HeightZone> zones)
        {
            return (zones ?? new HeightZone[0]).Where(zone => zone != null && zone.Plan != null)
                                              .OrderBy(zone => zone.Area)
                                              .ToList();
        }

        /// <summary>
        /// The other zones on a zone's level that share some of its plan, each with whether
        /// this zone is the one used where the two overlap.
        /// </summary>
        public IList<KeyValuePair<HeightZone, bool>> OverlapsOf(string project, HeightZone zone)
        {
            List<KeyValuePair<HeightZone, bool>> overlaps = new List<KeyValuePair<HeightZone, bool>>();
            if (zone == null)
            {
                return overlaps;
            }

            IList<HeightZone> order = InOrderOfPrecedence(OnStory(project, zone.Story));
            int at = order.ToList().FindIndex(candidate => candidate.Id == zone.Id);

            for (int i = 0; i < order.Count; i++)
            {
                if (order[i].Id != zone.Id && Overlap(order[i], zone))
                {
                    overlaps.Add(new KeyValuePair<HeightZone, bool>(order[i], at >= 0 && at < i));
                }
            }
            return overlaps;
        }

        public bool Remove(string project, string id)
        {
            List<HeightZone> zones;
            return byProject.TryGetValue(Key(project), out zones)
                   && zones.RemoveAll(zone => zone.Id == id) > 0;
        }

        /// <summary>
        /// Puts a copy of a zone on each of the levels named, and answers how many were put.
        /// A zone drawn round the same corners on one of those levels already is replaced rather
        /// than doubled: copying twice is copying once.
        /// </summary>
        public int CopyTo(string project, string id, IEnumerable<string> stories)
        {
            HeightZone original = Find(project, id);
            if (original == null)
            {
                return 0;
            }

            int copied = 0;
            foreach (string story in (stories ?? new string[0]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.Equals(story, original.Story, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (HeightZone same in OnStory(project, story).Where(original.SameOutlineAs))
                {
                    Remove(project, same.Id);
                }

                if (Add(project, original.CopyTo(story)))
                {
                    copied++;
                }
            }
            return copied;
        }

        /// <summary>Forgets every zone of one project, and answers how many there were.</summary>
        public int ClearProject(string project)
        {
            List<HeightZone> zones;
            if (!byProject.TryGetValue(Key(project), out zones))
            {
                return 0;
            }

            byProject.Remove(Key(project));
            return zones.Count;
        }

        #endregion

        #region which zone a line answers to

        /// <summary>
        /// The zone a drawn line load takes its height from, or null for the level's own.
        ///
        /// The zone it mostly lies in: the whole line takes one height - the load stays one
        /// value along it - and that is the height of whatever covers the most of its length,
        /// the level itself included. A line reaching a little way into a zone keeps the floor
        /// height; one reaching a little way out of it keeps the zone's. Where two zones overlap,
        /// the smaller one answers inside it, the way a small patch drawn over a large one is
        /// the exception to it.
        /// </summary>
        public HeightZone ZoneFor(string project, string story, LineLoad_R line)
        {
            if (line == null || !line.HasLine)
            {
                return null;
            }

            IList<HeightZone> zones = OnStory(project, story);
            if (zones.Count == 0)
            {
                return null;
            }

            return MostOf(zones, line.Pieces.Select(piece => new KeyValuePair<MyPoint, MyPoint>(piece.PlanStart,
                                                                                                 piece.PlanEnd)));
        }

        /// <summary>The zone covering the most of a line made of these pieces, or null where the level covers more.</summary>
        public static HeightZone MostOf(IList<HeightZone> zones, IEnumerable<KeyValuePair<MyPoint, MyPoint>> pieces)
        {
            IList<HeightZone> smallestFirst = InOrderOfPrecedence(zones);
            if (smallestFirst.Count == 0)
            {
                return null;
            }

            Dictionary<HeightZone, double> inside = new Dictionary<HeightZone, double>();
            double outside = 0;

            foreach (KeyValuePair<MyPoint, MyPoint> piece in pieces)
            {
                MyPoint from = piece.Key;
                MyPoint to = piece.Value;
                if (from == null || to == null)
                {
                    continue;
                }

                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-9)
                {
                    continue;
                }

                int steps = Math.Max(1, (int)Math.Ceiling(length / SampleStep));
                double each = length / steps;

                for (int i = 0; i < steps; i++)
                {
                    double t = (i + 0.5) / steps;
                    double x = from.X + dx * t;
                    double y = from.Y + dy * t;

                    HeightZone zone = smallestFirst.FirstOrDefault(candidate => candidate.Contains(x, y));
                    if (zone == null)
                    {
                        outside += each;
                        continue;
                    }

                    double sofar;
                    inside.TryGetValue(zone, out sofar);
                    inside[zone] = sofar + each;
                }
            }

            if (inside.Count == 0)
            {
                return null;
            }

            KeyValuePair<HeightZone, double> most = inside.OrderByDescending(entry => entry.Value).First();
            return most.Value > outside ? most.Key : null;
        }

        #endregion

        #region the settings file

        /// <summary>Replaces what is here with whatever the settings file holds.</summary>
        public void ReadFrom(IDictionary<string, object> view)
        {
            byProject.Clear();

            foreach (KeyValuePair<string, object> project in Json.Branch(view, Branch))
            {
                ReadProject(project.Key, project.Value);
            }
        }

        /// <summary>
        /// Replaces one project's zones with the ones stored in a settings file or a project
        /// file - its branch for that one project. Nothing there leaves the project with none.
        /// </summary>
        public void ReadProject(string project, object stored)
        {
            byProject.Remove(Key(project));

            List<object> zones = stored as List<object>;
            if (zones == null)
            {
                return;
            }

            foreach (Dictionary<string, object> entry in zones.OfType<Dictionary<string, object>>())
            {
                HeightZone zone = new HeightZone
                {
                    Id = Json.GetString(entry, "id", Guid.NewGuid().ToString("N")),
                    Story = Json.GetString(entry, "story", ""),
                    Name = Json.GetString(entry, "name", ""),
                    Height = Json.GetDouble(entry, "height", 0)
                };

                List<object> numbers = null;
                object raw;
                if (entry.TryGetValue("outline", out raw))
                {
                    numbers = raw as List<object>;
                }

                List<MyPoint> corners = new List<MyPoint>();
                if (numbers != null)
                {
                    for (int i = 0; i + 1 < numbers.Count; i += 2)
                    {
                        corners.Add(new MyPoint(Number(numbers[i]), Number(numbers[i + 1]), 0));
                    }
                }
                zone.SetOutline(corners);

                Add(project, zone);
            }
        }

        /// <summary>Every project's zones as they go into the settings file.</summary>
        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> projects = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (string project in byProject.Keys.OrderBy(key => key, StringComparer.Ordinal))
            {
                List<object> zones = ProjectToJson(project);
                if (zones.Count > 0)
                {
                    projects[project] = zones;
                }
            }
            return projects;
        }

        /// <summary>One project's zones as they go into a file.</summary>
        public List<object> ProjectToJson(string project)
        {
            return ForProject(project)
                .OrderBy(zone => zone.Story, StringComparer.Ordinal)
                .ThenBy(zone => zone.Id, StringComparer.Ordinal)
                .Select(zone => (object)new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "id", zone.Id },
                    { "story", zone.Story },
                    { "name", zone.Name },
                    { "height", zone.Height },
                    { "outline", zone.Outline.SelectMany(point => new object[] { point.X, point.Y }).ToList() }
                })
                .ToList();
        }

        private static double Number(object value)
        {
            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        #endregion

        private static string Key(string project)
        {
            return (project ?? "").Trim();
        }
    }
}
