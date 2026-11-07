using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Everything the slab load check compared on one level, written down as json: the
    /// schedule rows as read, every filled region off the loading plan with its outline and
    /// every load number parameter it carries, and each piece of slab with the shells whose
    /// loads were added, the load number it was held against and what was found.
    ///
    /// A load check that disagrees nearly everywhere can be the plan, the schedule or the model,
    /// and the card only shows one piece at a time. This is the whole level at once, in a form
    /// that can be read back and run again without Revit - the regions are the one part of
    /// the check that exists nowhere but in the Revit model.
    /// </summary>
    internal static class SlabLoadDump
    {
        /// <summary>What was read for one level, collected as the check reads it.</summary>
        internal sealed class Level
        {
            public string Story = "";
            public long ViewId;
            public string ViewName = "";
            public string Workbook = "";
            public string Sheet = "";
            public LoadSchedule Schedule;
            public IList<LoadRegion_R> Regions = new List<LoadRegion_R>();

            /// <summary>
            /// Every ETABS or SAFE area on the level - slabs, drops, stiff areas and the null
            /// areas laid over them for load - with the loads each carries as the model holds
            /// them. A piece only says what its shells add up to; this says what each one put in.
            /// </summary>
            public IList<Slab_E> Shells = new List<Slab_E>();
        }

        /// <summary>The dump for one level, as the tree the json writer takes.</summary>
        public static Dictionary<string, object> Build(Level level, IEnumerable<SlabZone> pieces)
        {
            Dictionary<string, object> root = new Dictionary<string, object>
            {
                { "story", level.Story },
                { "viewId", level.ViewId },
                { "viewName", level.ViewName },
                { "workbook", level.Workbook },
                { "sheet", level.Sheet },
                { "written", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) }
            };

            root["schedule"] = level.Schedule == null
                ? new List<object>()
                : level.Schedule.Entries.Select(entry => (object)new Dictionary<string, object>
                {
                    { "number", entry.Number },
                    { "description", entry.Description ?? "" },
                    { "liveKind", entry.LiveLoadKind ?? "" },
                    { "loads", Loads(entry.Loads) }
                }).ToList();

            root["scheduleIssues"] = level.Schedule == null
                ? new List<object>()
                : level.Schedule.Issues.Cast<object>().ToList();

            root["regions"] = (level.Regions ?? new List<LoadRegion_R>()).Select(region => (object)new Dictionary<string, object>
            {
                { "id", region.RevitID },
                { "type", region.TypeName ?? "" },
                { "number", region.Number },
                { "numberText", region.NumberText ?? "" },
                { "numberFrom", (region.NumberSources ?? new List<string>()).Cast<object>().ToList() },
                { "areaM2", Math.Round(region.Area / 1e6, 3) },
                { "parts", region.Parts.Select(part => (object)Polygon(part)).ToList() }
            }).ToList();

            root["shells"] = (level.Shells ?? new List<Slab_E>()).Select(shell => (object)new Dictionary<string, object>
            {
                { "name", shell.UniqueName },
                { "property", shell.PropertyName },
                { "kind", shell.Kind.ToString() },
                { "opening", shell.IsOpening },
                { "thickness", Math.Round(shell.Thickness, 1) },
                { "loads", (shell.Loads ?? new List<AreaLoad_E>()).Select(load => (object)new Dictionary<string, object>
                    {
                        { "pattern", load.Pattern },
                        { "value", load.Value },
                        { "gravity", load.IsGravity },
                        { "toFrames", load.IsToFrame }
                    }).ToList() },
                { "areaM2", shell.Plan == null ? 0d : Math.Round(shell.Plan.Area / 1e6, 3) },
                { "outline", shell.Plan == null ? null : (object)Polygon(shell.Plan) }
            }).ToList();

            root["pieces"] = (pieces ?? Enumerable.Empty<SlabZone>()).Select(zone =>
            {
                MyPoint at = zone.Plan == null ? null : zone.Plan.InteriorPoint();
                return (object)new Dictionary<string, object>
                {
                    { "shell", zone.EtabsName ?? "" },
                    { "property", zone.EtabsProperty ?? "" },
                    { "shellsAdded", (zone.LoadShells ?? new List<string>()).Cast<object>().ToList() },
                    { "etabs", Loads(zone.EtabsLoads) },
                    { "region", zone.LoadRegionId },
                    { "number", zone.LoadNumber },
                    { "description", zone.LoadDescription ?? "" },
                    { "plan", Loads(zone.PlannedLoads) },
                    { "state", zone.LoadState.ToString() },
                    { "mismatched", (zone.MismatchedLoads ?? new List<LoadCategory>()).Select(c => (object)c.ToString()).ToList() },
                    { "note", zone.LoadNote ?? "" },
                    { "areaM2", Math.Round(zone.Area / 1e6, 3) },
                    { "at", at == null ? null : new List<object> { Math.Round(at.X, 1), Math.Round(at.Y, 1) } }
                };
            }).ToList();

            return root;
        }

        /// <summary>Writes one level's dump and hands back where it went.</summary>
        public static string Write(string folder, Level level, IEnumerable<SlabZone> pieces)
        {
            Directory.CreateDirectory(folder);

            string path = Path.Combine(folder, "slab-loads-" + Slug(level.Story) + "-"
                                               + DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)
                                               + ".json");

            File.WriteAllText(path, Json.Write(Build(level, pieces)));
            return path;
        }

        private static Dictionary<string, object> Loads(LoadSet loads)
        {
            return new Dictionary<string, object>
            {
                { "sdl", loads.Sdl },
                { "ll", loads.Ll },
                { "lr", loads.Lr },
                { "mep", loads.Mep }
            };
        }

        private static Dictionary<string, object> Polygon(PlanPolygon polygon)
        {
            return new Dictionary<string, object>
            {
                { "outer", Ring(polygon.Outer) },
                { "holes", polygon.Holes.Select(hole => (object)Ring(hole)).ToList() }
            };
        }

        /// <summary>A ring as [x, y] pairs to the millimetre, which is all a plan needs.</summary>
        private static List<object> Ring(PlanRing ring)
        {
            return ring.Points
                       .Select(point => (object)new List<object> { Math.Round(point.X, 1), Math.Round(point.Y, 1) })
                       .ToList();
        }

        private static string Slug(string name)
        {
            char[] kept = (name ?? "").Select(letter => char.IsLetterOrDigit(letter) ? letter : '-').ToArray();
            string slug = new string(kept).Trim('-');
            return slug.Length == 0 ? "level" : slug;
        }
    }
}
