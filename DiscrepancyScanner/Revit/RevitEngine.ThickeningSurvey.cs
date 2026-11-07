using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A read-only survey of what stands against each Revit floor, written to a text file.
    ///
    /// It exists to answer one question from real geometry before anything is built on it: where a
    /// floor is locally deeper than its type says - a drop panel, a pad, a thickening - what
    /// is the thing that makes it so, and how does it sit against the floor? Nothing in this
    /// file decides anything. It changes no result, no check and no setting; with the switch
    /// off (the default) none of it runs.
    ///
    /// Everything is read from the solids. Category, family and type names are written next to
    /// each body so a person can recognise it, and are never used to pick, skip or rank one.
    /// </summary>
    public partial class RevitEngine
    {
        /// <summary>Heights this close are the same height, in mm. The same gap the slab check stacks floors by.</summary>
        private const double SurveyHeightTolerance = 10d;

        /// <summary>How far under a floor's underside, and over its top, bodies are looked for, in mm.</summary>
        private const double SurveyBelow = 2000d;
        private const double SurveyAbove = 500d;

        /// <summary>Floors surveyed when none are named, so a whole building is not read by accident.</summary>
        private const int SurveyDefaultFloorCap = 40;

        /// <summary>Bodies written per floor.</summary>
        private const int SurveyBodiesPerFloor = 250;

        private const int SurveyGrid = 9;

        /// <summary>One body found near a floor, measured once however many floors it stands near.</summary>
        private sealed class SurveyBody
        {
            public long Id;
            public string Category = "";
            public string Family = "";
            public string TypeName = "";
            public string HostedBy = "";
            public int Solids;
            public double VolumeM3;

            public bool HasHorizontalFaces;
            public bool Flat;
            public double MinX, MaxX, MinY, MaxY;
            public double TopHigh, TopLow, BottomHigh, BottomLow;
            public double FootprintArea;
            public List<List<List<MyPoint>>> TopFaces = new List<List<List<MyPoint>>>();
            public string Unread = "";
        }

        private void runThickeningSurvey(List<PlacedElement> floorElements, PrefrencesData pref)
        {
            HashSet<long> named = new HashSet<long>();
            foreach (string word in (pref.ThickeningSurveyFloorIds ?? "").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                long id;
                if (long.TryParse(word, out id))
                {
                    named.Add(id);
                }
            }

            Dictionary<long, Slab_R> readFloors = new Dictionary<long, Slab_R>();
            foreach (Slab_R slab in slabsData)
            {
                readFloors[slab.RevitID] = slab;
            }

            List<PlacedElement> hosts = floorElements
                .Where(placed => readFloors.ContainsKey(placed.RevitId)
                                 && (named.Count == 0 || named.Contains(placed.RevitId)))
                .ToList();

            string capNote = null;
            if (named.Count == 0 && hosts.Count > SurveyDefaultFloorCap)
            {
                capNote = $"{hosts.Count} floors were read and no floor ids were named, so only the first " +
                          $"{SurveyDefaultFloorCap} are surveyed. Name the floors to survey in the setting to choose them.";
                hosts = hosts.Take(SurveyDefaultFloorCap).ToList();
            }

            string folder = GeometryDump.DefaultFolder();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "thickening-survey-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");

            List<ModelPlacement> placements = getSelectedPlacements();
            Dictionary<string, SurveyBody> measured = new Dictionary<string, SurveyBody>();

            using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("ETABS / Revit slab check - thickening survey (read-only)");
                w.WriteLine("started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                w.WriteLine("Lengths and heights in mm, areas in m2, all in the frame the slab check reads floors in.");
                w.WriteLine("Category, family and type names are printed to be read by a person. Nothing here is chosen, skipped or ranked by them.");
                w.WriteLine($"A height within {SurveyHeightTolerance} mm of another is the same height. Bodies are searched from {SurveyBelow} mm under each floor to {SurveyAbove} mm over it.");
                w.WriteLine("Nothing here changes any result of the check.");
                if (capNote != null)
                {
                    w.WriteLine(capNote);
                }
                w.WriteLine(new string('=', 78));

                int doneHosts = 0;
                foreach (PlacedElement host in hosts)
                {
                    report("Surveying what stands against the floors", ++doneHosts, hosts.Count);
                    surveyOneFloor(w, host, readFloors[host.RevitId], placements, measured);
                }

                w.WriteLine();
                w.WriteLine(new string('=', 78));
                w.WriteLine($"{hosts.Count} floor(s) surveyed, {measured.Count} distinct bod(ies) measured. finished " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            addIssue("Thickening survey: " + path);
        }

        private void surveyOneFloor(StreamWriter w, PlacedElement host, Slab_R floor,
                                    List<ModelPlacement> placements, Dictionary<string, SurveyBody> measured)
        {
            double top = floor.TopElevation, bottom = floor.BottomElevation;
            double depth = top - bottom;

            w.WriteLine();
            w.WriteLine($"FLOOR {host.RevitId}  '{floor.TypeName}'  top {Math.Round(top, 1)}  bottom {Math.Round(bottom, 1)}  " +
                        $"depth {Math.Round(depth, 1)}  plan {Math.Round(floor.Plan.Area / 1e6, 2)} m2  ({floor.Parts.Count} part(s), {floor.Plan.Holes.Count} opening(s))");

            BoundingBoxXYZ box = host.Element.get_BoundingBox(null);
            if (box == null)
            {
                w.WriteLine("  the floor has no bounding box, nothing searched");
                return;
            }

            List<Tuple<SurveyBody, ModelPlacement>> found = new List<Tuple<SurveyBody, ModelPlacement>>();

            foreach (ModelPlacement placement in placements)
            {
                Outline outline = searchOutline(box, host.Placement, placement);
                if (outline == null)
                {
                    continue;
                }

                IList<Element> near;
                try
                {
                    near = new FilteredElementCollector(placement.Doc)
                        .WherePasses(new BoundingBoxIntersectsFilter(outline))
                        .WhereElementIsNotElementType()
                        .ToElements();
                }
                catch (Exception e)
                {
                    w.WriteLine($"  search in '{placement.Doc.Title}' failed: {e.Message}");
                    continue;
                }

                foreach (Element element in near)
                {
                    if (ReferenceEquals(placement, host.Placement) && element.Id == host.Element.Id)
                    {
                        continue;
                    }
                    if (element is RevitLinkInstance || element.Category == null
                        || element.Category.CategoryType != CategoryType.Model)
                    {
                        continue;
                    }

                    string key = placement.IdOffset + ":" + placement.Doc.Title + ":" + element.Id.Value + ":" + placement.GetHashCode();
                    SurveyBody body;
                    if (!measured.TryGetValue(key, out body))
                    {
                        body = measureBody(new PlacedElement(element, placement));
                        measured[key] = body;
                    }
                    found.Add(Tuple.Create(body, placement));
                }
            }

            int noGeometry = found.Count(f => f.Item1.Solids == 0);
            int noOverlap = 0;
            List<string> records = new List<string>();
            List<double> order = new List<double>();

            foreach (Tuple<SurveyBody, ModelPlacement> pair in found)
            {
                SurveyBody body = pair.Item1;
                if (body.Solids == 0 || !body.HasHorizontalFaces)
                {
                    continue;
                }

                double inside, overlapArea;
                if (!overlapWithFloor(body, floor, out inside, out overlapArea))
                {
                    noOverlap++;
                    continue;
                }

                records.Add(describeBody(body, floor, inside, overlapArea));
                order.Add(overlapArea);
            }

            w.WriteLine($"  {found.Count} body(ies) near; {noGeometry} with no solid, {noOverlap} not over the floor's plan, {records.Count} over it");

            int written = 0;
            foreach (int i in Enumerable.Range(0, records.Count).OrderByDescending(i => order[i]))
            {
                if (written++ >= SurveyBodiesPerFloor)
                {
                    w.WriteLine($"  ... {records.Count - SurveyBodiesPerFloor} smaller one(s) not written");
                    break;
                }
                w.WriteLine(records[i]);
            }
        }

        /// <summary>The host floor's box carried into one document's own coordinates, as a search outline.</summary>
        private static Outline searchOutline(BoundingBoxXYZ box, ModelPlacement hostPlacement, ModelPlacement into)
        {
            double lowZ = box.Min.Z - SurveyBelow / 304.8d;
            double highZ = box.Max.Z + SurveyAbove / 304.8d;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            Transform inverse = into.Transform == null ? null : into.Transform.Inverse;

            foreach (double x in new[] { box.Min.X, box.Max.X })
            foreach (double y in new[] { box.Min.Y, box.Max.Y })
            foreach (double z in new[] { lowZ, highZ })
            {
                XYZ point = new XYZ(x, y, z);
                if (hostPlacement.Transform != null)
                {
                    point = hostPlacement.Transform.OfPoint(point);
                }
                if (inverse != null)
                {
                    point = inverse.OfPoint(point);
                }
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z); maxZ = Math.Max(maxZ, point.Z);
            }

            if (maxX <= minX || maxY <= minY || maxZ <= minZ)
            {
                return null;
            }
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        private SurveyBody measureBody(PlacedElement placed)
        {
            Element element = placed.Element;
            SurveyBody body = new SurveyBody { Id = placed.RevitId };

            try
            {
                body.Category = element.Category == null ? "" : element.Category.Name;
                body.TypeName = getElementTypeName(element);

                FamilyInstance instance = element as FamilyInstance;
                if (instance != null)
                {
                    body.Family = instance.Symbol == null || instance.Symbol.Family == null ? "" : instance.Symbol.Family.Name;
                    Element hostElement = instance.Host;
                    if (hostElement != null)
                    {
                        body.HostedBy = $"{hostElement.Category?.Name} {hostElement.Id.Value}";
                    }
                }

                Options options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
                GeometryElement geometry = element.get_Geometry(options);
                if (geometry == null)
                {
                    return body;
                }

                List<Solid> solids = enumerateSolids(geometry).ToList();
                body.Solids = solids.Count;
                if (solids.Count == 0)
                {
                    return body;
                }

                body.VolumeM3 = solids.Sum(s => s.Volume) * 0.0283168466d;
                Solid largest = solids.OrderByDescending(s => s.Volume).First();

                List<List<MyPoint>> bottomLoops = new List<List<MyPoint>>();
                readHorizontalFaceLoops(largest, placed.Placement.Transform, body.TopFaces, bottomLoops);

                List<MyPoint> topPoints = body.TopFaces.SelectMany(face => face).SelectMany(loop => loop).ToList();
                List<MyPoint> bottomPoints = bottomLoops.SelectMany(loop => loop).ToList();
                if (topPoints.Count == 0 || bottomPoints.Count == 0)
                {
                    return body;
                }

                body.HasHorizontalFaces = true;
                body.TopHigh = topPoints.Max(p => p.Z);
                body.TopLow = topPoints.Min(p => p.Z);
                body.BottomHigh = bottomPoints.Max(p => p.Z);
                body.BottomLow = bottomPoints.Min(p => p.Z);
                body.Flat = body.TopHigh - body.TopLow <= 1d && body.BottomHigh - body.BottomLow <= 1d;

                List<MyPoint> all = topPoints.Concat(bottomPoints).ToList();
                body.MinX = all.Min(p => p.X); body.MaxX = all.Max(p => p.X);
                body.MinY = all.Min(p => p.Y); body.MaxY = all.Max(p => p.Y);

                foreach (List<List<MyPoint>> face in body.TopFaces)
                {
                    List<double> areas = face.Select(loop => Math.Abs(loopArea(loop))).OrderByDescending(a => a).ToList();
                    if (areas.Count > 0)
                    {
                        body.FootprintArea += areas[0] - areas.Skip(1).Sum();
                    }
                }
            }
            catch (Exception e)
            {
                body.Unread = e.Message;
            }

            return body;
        }

        private static double loopArea(List<MyPoint> loop)
        {
            double twice = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                MyPoint a = loop[i], b = loop[(i + 1) % loop.Count];
                twice += a.X * b.Y - b.X * a.Y;
            }
            return twice / 2d;
        }

        private static bool insideLoops(List<List<MyPoint>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<MyPoint> loop in loops)
            {
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    if ((loop[i].Y > y) != (loop[j].Y > y)
                        && x < (loop[j].X - loop[i].X) * (y - loop[i].Y) / (loop[j].Y - loop[i].Y) + loop[i].X)
                    {
                        inside = !inside;
                    }
                }
            }
            return inside;
        }

        /// <summary>
        /// How much of the body's footprint lies over the floor's plan, by a grid of samples in
        /// the body's own box. False when none of it does.
        /// </summary>
        private static bool overlapWithFloor(SurveyBody body, Slab_R floor, out double fractionInside, out double overlapArea)
        {
            fractionInside = 0;
            overlapArea = 0;

            int onBody = 0, onFloor = 0;
            for (int i = 0; i < SurveyGrid; i++)
            {
                for (int j = 0; j < SurveyGrid; j++)
                {
                    double x = body.MinX + (body.MaxX - body.MinX) * (i + 0.5d) / SurveyGrid;
                    double y = body.MinY + (body.MaxY - body.MinY) * (j + 0.5d) / SurveyGrid;

                    if (!body.TopFaces.Any(face => insideLoops(face, x, y)))
                    {
                        continue;
                    }
                    onBody++;
                    if (floor.Parts.Any(part => part.Contains(x, y)))
                    {
                        onFloor++;
                    }
                }
            }

            if (onBody == 0 || onFloor == 0)
            {
                return false;
            }

            fractionInside = (double)onFloor / onBody;
            overlapArea = body.FootprintArea * fractionInside;
            return true;
        }

        private static string heightAgainst(double value, double top, double bottom)
        {
            double tol = SurveyHeightTolerance;
            if (value > top + tol) return $"{Math.Round(value - top, 1)} above the floor's top";
            if (Math.Abs(value - top) <= tol) return "at the floor's top";
            if (value > bottom + tol) return $"{Math.Round(top - value, 1)} below the floor's top, inside its depth";
            if (Math.Abs(value - bottom) <= tol) return "at the floor's underside";
            return $"{Math.Round(bottom - value, 1)} below the floor's underside";
        }

        private static string describeBody(SurveyBody body, Slab_R floor, double fractionInside, double overlapArea)
        {
            double top = floor.TopElevation, bottom = floor.BottomElevation, depth = top - bottom;

            double high = body.TopHigh, low = body.BottomLow;
            double combined = Math.Max(top, high) - Math.Min(bottom, low);
            double dx = body.MaxX - body.MinX, dy = body.MaxY - body.MinY;
            double longSide = Math.Max(dx, dy), shortSide = Math.Max(1d, Math.Min(dx, dy));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"  BODY {body.Id}  [{body.Category}] family '{body.Family}' type '{body.TypeName}'" +
                          (string.IsNullOrEmpty(body.HostedBy) ? "" : $"  hosted by {body.HostedBy}"));
            sb.AppendLine($"      footprint {Math.Round(body.FootprintArea / 1e6, 3)} m2, box {Math.Round(dx)} x {Math.Round(dy)} (long/short {Math.Round(longSide / shortSide, 1)}), " +
                          $"{Math.Round(fractionInside * 100)}% of it over the floor, {Math.Round(overlapArea / floor.Plan.Area * 100, 1)}% of the floor's plan");
            sb.AppendLine($"      top {Math.Round(high, 1)}: {heightAgainst(high, top, bottom)};  bottom {Math.Round(low, 1)}: {heightAgainst(low, top, bottom)}");
            sb.AppendLine($"      own depth {Math.Round(high - low, 1)} = {Math.Round((high - low) / Math.Max(1d, depth), 2)} x the floor's;  " +
                          $"floor and body together {Math.Round(combined, 1)} ({Math.Round(combined - depth, 1)} more than the floor alone)");
            sb.Append($"      {body.Solids} solid(s), {Math.Round(body.VolumeM3, 3)} m3, faces {(body.Flat ? "flat and level" : $"not flat: top {Math.Round(body.TopLow, 1)}..{Math.Round(body.TopHigh, 1)}, bottom {Math.Round(body.BottomLow, 1)}..{Math.Round(body.BottomHigh, 1)}")}");
            if (!string.IsNullOrEmpty(body.Unread))
            {
                sb.Append("; part unread: " + body.Unread);
            }
            return sb.ToString();
        }
    }
}
