using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reads local thickenings of the Revit floors - drop panels, pads - off the geometry that
    /// stands against them, and hangs what it finds on <see cref="Slab_R.Patches"/>.
    ///
    /// Everything is judged by <see cref="ThickeningClassifier"/> from the solids alone. Category,
    /// family and type are written beside each body for a person and are never read to decide.
    /// Runs only when <see cref="PrefrencesData.DetectLocalThickenings"/> is on.
    /// </summary>
    public partial class RevitEngine
    {
        /// <summary>A face this close to level all the way round is a level face, in mm.</summary>
        private const double LevelFaceTolerance = 1d;

        private void detectLocalThickenings(List<PlacedElement> floorElements, PrefrencesData pref)
        {
            Stopwatch watch = Stopwatch.StartNew();
            ThickeningOptions options = ThickeningOptions.FromPreferences(pref);

            HashSet<long> floorIds = new HashSet<long>(floorElements.Select(placed => placed.RevitId));
            Dictionary<long, PlacedElement> hostElements = new Dictionary<long, PlacedElement>();
            foreach (PlacedElement placed in floorElements)
            {
                hostElements[placed.RevitId] = placed;
            }

            List<ModelPlacement> placements = getSelectedPlacements();
            Dictionary<string, ThickeningCandidate> measured = new Dictionary<string, ThickeningCandidate>();

            string folder = GeometryDump.DefaultFolder();
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "thickening-detection-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");

            int detected = 0, ambiguous = 0, rejected = 0, hostsWithPatches = 0, hostsDone = 0;
            List<Slab_R> hosts = slabsData.Where(slab => hostElements.ContainsKey(slab.RevitID)).ToList();

            using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
            {
                w.WriteLine("ETABS / Revit slab check - local thickening detection");
                w.WriteLine("started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                w.WriteLine("Lengths and heights in mm, areas in m2, all in the frame the slab check reads floors in.");
                w.WriteLine("Judged from geometry alone. Category, family and type are printed to be read by a person.");
                w.WriteLine($"Thresholds: flush {options.FlushTolerance} mm, least reach under the floor {options.MinProjection} mm, " +
                            $"over the floor {Math.Round(options.MinOverlap * 100)}%, panel area {options.MinArea / 1e6} m2, " +
                            $"short side {options.MinShortSide} mm, compactness {options.MinCompactness}, " +
                            $"{options.MinEvidence} of 4 shape tests.");
                w.WriteLine("DETECTED = read as slab depth by the check.  AMBIGUOUS = listed, not used.  REJECTED = another element.");
                w.WriteLine(new string('=', 78));

                foreach (Slab_R slab in hosts)
                {
                    report("Looking for local thickenings", ++hostsDone, hosts.Count);

                    PlacedElement host = hostElements[slab.RevitID];
                    List<ThickeningPatch> found = detectAgainstFloor(w, host, slab, floorIds, placements, measured, options);

                    List<ThickeningPatch> kept = ThickeningClassifier.Deduplicate(
                        found.Where(p => p.Outcome != ThickeningOutcome.Rejected).ToList());
                    slab.Patches.Clear();
                    foreach (ThickeningPatch patch in kept)
                    {
                        slab.Patches.Add(patch);
                    }
                    if (kept.Count > 0)
                    {
                        hostsWithPatches++;
                    }

                    detected += kept.Count(p => p.Outcome == ThickeningOutcome.Detected);
                    ambiguous += kept.Count(p => p.Outcome == ThickeningOutcome.Ambiguous);
                    rejected += found.Count(p => p.Outcome == ThickeningOutcome.Rejected);

                    foreach (ThickeningPatch patch in kept)
                    {
                        w.WriteLine(describePatch(patch));
                    }
                    foreach (ThickeningPatch patch in found.Where(p => p.Outcome == ThickeningOutcome.Rejected))
                    {
                        w.WriteLine(describePatch(patch));
                    }
                }

                w.WriteLine();
                w.WriteLine(new string('=', 78));
                w.WriteLine($"{hosts.Count} floor(s) examined, {measured.Count} distinct bod(ies) measured: " +
                            $"{detected} detected, {ambiguous} ambiguous, {rejected} rejected (counted per floor). " +
                            $"{watch.ElapsedMilliseconds} ms. finished " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            addIssue($"Local thickenings: {detected} detected on {hostsWithPatches} floor(s), {ambiguous} ambiguous " +
                     $"(listed, not used), {rejected} rejected as other elements. Report: {path}");
        }

        private List<ThickeningPatch> detectAgainstFloor(StreamWriter w, PlacedElement host, Slab_R slab,
                                                         HashSet<long> floorIds, List<ModelPlacement> placements,
                                                         Dictionary<string, ThickeningCandidate> measured,
                                                         ThickeningOptions options)
        {
            List<ThickeningPatch> results = new List<ThickeningPatch>();

            w.WriteLine();
            w.WriteLine($"FLOOR {host.RevitId}  '{slab.TypeName}'  top {Math.Round(slab.TopElevation, 1)}  " +
                        $"bottom {Math.Round(slab.BottomElevation, 1)}  plan {Math.Round(slab.Plan.Area / 1e6, 2)} m2");

            BoundingBoxXYZ box = host.Element.get_BoundingBox(null);
            if (box == null)
            {
                w.WriteLine("  the floor has no bounding box, nothing searched");
                return results;
            }

            ThickeningHost thickeningHost = new ThickeningHost
            {
                Id = host.RevitId,
                Top = slab.TopElevation,
                Bottom = slab.BottomElevation,
                Flat = !slab.HasVariableThickness && slab.GeometrySource == Slab_R.FromSolid,
                Parts = slab.Parts
            };

            HashSet<string> seen = new HashSet<string>();
            int near = 0, notOver = 0;

            foreach (ModelPlacement placement in placements)
            {
                Outline outline = searchOutline(box, host.Placement, placement);
                if (outline == null)
                {
                    continue;
                }

                IList<Element> elements;
                try
                {
                    elements = new FilteredElementCollector(placement.Doc)
                        .WherePasses(new BoundingBoxIntersectsFilter(outline))
                        .WhereElementIsNotElementType()
                        .ToElements();
                }
                catch (Exception e)
                {
                    w.WriteLine($"  search in '{placement.Doc.Title}' failed: {e.Message}");
                    continue;
                }

                foreach (Element element in elements)
                {
                    if (element is RevitLinkInstance || element.Category == null
                        || element.Category.CategoryType != CategoryType.Model)
                    {
                        continue;
                    }

                    PlacedElement placed = new PlacedElement(element, placement);

                    // A floor is read as a floor already, with its own depth.
                    if (floorIds.Contains(placed.RevitId))
                    {
                        continue;
                    }

                    string key = placement.IdOffset + ":" + placement.Doc.Title + ":" + element.Id.Value + ":" + placement.GetHashCode();
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    ThickeningCandidate candidate;
                    if (!measured.TryGetValue(key, out candidate))
                    {
                        candidate = measureCandidate(placed);
                        measured[key] = candidate;
                    }

                    if (candidate.Solids == 0
                        || (candidate.TopFaces.Count == 0 && candidate.InclinedUpFaces == 0 && !candidate.HasLevelBottom
                            && string.IsNullOrEmpty(candidate.Unread)))
                    {
                        continue;
                    }
                    near++;

                    double overlap = candidate.TopFaces.Count > 0
                        ? ThickeningClassifier.OverlapWithHost(thickeningHost, candidate)
                        : 0d;
                    if (candidate.TopFaces.Count > 0 && overlap <= 0d)
                    {
                        notOver++;
                        continue;
                    }
                    if (candidate.TopFaces.Count == 0)
                    {
                        // Nothing level looks up, so there is no footprint to put over the floor.
                        // Only listed if it is something that leans or could not be read.
                        if (candidate.InclinedUpFaces == 0 && string.IsNullOrEmpty(candidate.Unread))
                        {
                            continue;
                        }
                    }

                    results.Add(ThickeningClassifier.Classify(thickeningHost, candidate, options, overlap));
                }
            }

            w.WriteLine($"  {near} body(ies) with a horizontal face near; {notOver} not over the floor's plan; " +
                        $"{results.Count} judged{(thickeningHost.Flat ? "" : " (the floor is not flat)")}");
            return results;
        }

        /// <summary>One body read into the plain form the classifier takes, all its solids, in the floors' frame.</summary>
        private ThickeningCandidate measureCandidate(PlacedElement placed)
        {
            Element element = placed.Element;
            ThickeningCandidate candidate = new ThickeningCandidate { ElementId = placed.RevitId };

            try
            {
                string category = element.Category == null ? "" : element.Category.Name;
                string family = "";
                string hostedBy = "";
                FamilyInstance instance = element as FamilyInstance;
                if (instance != null)
                {
                    family = instance.Symbol == null || instance.Symbol.Family == null ? "" : instance.Symbol.Family.Name;
                    if (instance.Host != null)
                    {
                        hostedBy = $" hosted by {instance.Host.Category?.Name} {instance.Host.Id.Value}";
                    }
                }
                candidate.Description = $"[{category}] family '{family}' type '{getElementTypeName(element)}'{hostedBy}";

                Options options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
                GeometryElement geometry = element.get_Geometry(options);
                if (geometry == null)
                {
                    return candidate;
                }

                List<Solid> solids = enumerateSolids(geometry).Where(solid => solid.Volume > 0d).ToList();
                candidate.Solids = solids.Count;

                List<MyPoint> topPoints = new List<MyPoint>();
                List<MyPoint> bottomPoints = new List<MyPoint>();

                foreach (Solid solid in solids)
                {
                    List<List<List<MyPoint>>> upFaces = new List<List<List<MyPoint>>>();
                    List<List<MyPoint>> downLoops = new List<List<MyPoint>>();
                    readHorizontalFaceLoops(solid, placed.Placement.Transform, upFaces, downLoops);

                    foreach (List<List<MyPoint>> face in upFaces)
                    {
                        List<MyPoint> points = face.SelectMany(loop => loop).ToList();
                        if (points.Count == 0)
                        {
                            continue;
                        }

                        // A face looking up that leans is the chamfer or batter of a side. It says
                        // nothing about the top, and does not disqualify the body for having it.
                        if (points.Max(p => p.Z) - points.Min(p => p.Z) <= LevelFaceTolerance)
                        {
                            candidate.TopFaces.Add(face);
                            topPoints.AddRange(points);
                        }
                        else
                        {
                            candidate.InclinedUpFaces++;
                            List<double> areas = face.Select(loop => Math.Abs(ThickeningClassifier.LoopArea(loop))).OrderByDescending(a => a).ToList();
                            candidate.InclinedUpArea += areas.Count == 0 ? 0 : areas[0] - areas.Skip(1).Sum();
                        }
                    }

                    foreach (List<MyPoint> loop in downLoops)
                    {
                        if (loop.Count > 0 && loop.Max(p => p.Z) - loop.Min(p => p.Z) <= LevelFaceTolerance)
                        {
                            bottomPoints.AddRange(loop);
                        }
                    }
                }

                if (topPoints.Count > 0)
                {
                    candidate.HasLevelTop = true;
                    candidate.TopHigh = topPoints.Max(p => p.Z);
                    candidate.TopLow = topPoints.Min(p => p.Z);
                }
                if (bottomPoints.Count > 0)
                {
                    candidate.HasLevelBottom = true;
                    candidate.BottomHigh = bottomPoints.Max(p => p.Z);
                    candidate.BottomLow = bottomPoints.Min(p => p.Z);
                }
            }
            catch (Exception e)
            {
                candidate.Unread = e.Message;
            }

            return candidate;
        }

        private static string describePatch(ThickeningPatch patch)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"  {patch.Outcome.ToString().ToUpperInvariant()} body {patch.IdText}  confidence {patch.Confidence}" +
                          (patch.Outcome == ThickeningOutcome.Detected
                              ? $"  depth {Math.Round(patch.Thickness, 1)} (top {Math.Round(patch.TopElevation, 1)}, bottom {Math.Round(patch.BottomElevation, 1)})"
                              : ""));
            foreach (string line in patch.Provenance)
            {
                sb.AppendLine("      " + line);
            }
            sb.AppendLine($"      {patch.Reason}");
            if (patch.FootprintArea > 0)
            {
                sb.AppendLine($"      footprint {Math.Round(patch.FootprintArea / 1e6, 3)} m2, {Math.Round(patch.ShortSide)} x {Math.Round(patch.LongSide)}, " +
                              $"compactness {Math.Round(patch.Compactness, 2)}, {Math.Round(patch.OverHostRatio * 100)}% over the floor");
            }
            foreach (string line in patch.Evidence)
            {
                sb.AppendLine("      " + line);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
