using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Everything the slab check reads for one level, written to a file so the check can be run
    /// again on it with no Revit and no ETABS - see <see cref="PrefrencesData.SaveSlabCheckInputs"/>.
    ///
    /// The saved design model already holds the ETABS side. What it cannot hold is the Revit
    /// floors, which are the half a missed opening or a dropped shell is usually about: their
    /// outlines and holes, how deep they are, and where they sit against the level. With those,
    /// the columns and walls standing in the slab, and the settings the check ran under, a
    /// finding on somebody's model becomes a test rather than a description of a screenshot.
    /// </summary>
    [DataContract]
    public sealed class SlabCheckInputs
    {
        [DataMember] public string Story = "";
        [DataMember] public double StoryElevation;
        [DataMember] public string WrittenBy = "";
        [DataMember] public List<Story_E> Stories = new List<Story_E>();
        [DataMember] public List<Slab_E> EtabsSlabs = new List<Slab_E>();
        [DataMember] public List<FloorRecord> RevitFloors = new List<FloorRecord>();
        [DataMember] public List<SupportRecord> Supports = new List<SupportRecord>();
        [DataMember] public Dictionary<string, string> Preferences = new Dictionary<string, string>();

        [DataContract]
        public sealed class FloorRecord
        {
            [DataMember] public long RevitId;
            [DataMember] public string TypeName = "";
            [DataMember] public string ElementName = "";
            [DataMember] public string CategoryName = "";
            [DataMember] public string Story = "";
            [DataMember] public double TopElevation;
            [DataMember] public double BottomElevation;
            [DataMember] public double MinimumThickness;
            [DataMember] public double MaximumThickness;
            [DataMember] public double NominalThickness;
            [DataMember] public double StructuralThickness;
            [DataMember] public bool HasVariableTypeThickness;
            [DataMember] public bool IsStructural;
            [DataMember] public string GeometrySource = Slab_R.FromSolid;
            [DataMember] public bool OpeningsUncertain;
            [DataMember] public PolygonRecord Plan;
            [DataMember] public List<PolygonRecord> Parts = new List<PolygonRecord>();
            [DataMember] public List<PatchRecord> Patches = new List<PatchRecord>();
        }

        /// <summary>A local thickening read against a floor, with where it came from.</summary>
        [DataContract]
        public sealed class PatchRecord
        {
            [DataMember] public long HostFloorId;
            [DataMember] public List<long> SourceElementIds = new List<long>();
            [DataMember] public List<string> Provenance = new List<string>();
            [DataMember] public List<PolygonRecord> Plans = new List<PolygonRecord>();
            [DataMember] public double TopElevation;
            [DataMember] public double BottomElevation;
            [DataMember] public double BodyTop;
            [DataMember] public double BodyBottom;
            [DataMember] public double OverHostRatio;
            [DataMember] public double FootprintArea;
            [DataMember] public double ShortSide;
            [DataMember] public double LongSide;
            [DataMember] public double Compactness;
            [DataMember] public string Classification = "Unknown";
            [DataMember] public string Outcome = "";
            [DataMember] public string Confidence = "";
            [DataMember] public string Reason = "";
            [DataMember] public List<string> Evidence = new List<string>();

            public static PatchRecord From(ThickeningPatch patch)
            {
                return new PatchRecord
                {
                    HostFloorId = patch.HostFloorId,
                    SourceElementIds = new List<long>(patch.SourceElementIds),
                    Provenance = new List<string>(patch.Provenance),
                    Plans = patch.Plans.Select(PolygonRecord.From).Where(plan => plan != null).ToList(),
                    TopElevation = patch.TopElevation,
                    BottomElevation = patch.BottomElevation,
                    BodyTop = patch.BodyTop,
                    BodyBottom = patch.BodyBottom,
                    OverHostRatio = patch.OverHostRatio,
                    FootprintArea = patch.FootprintArea,
                    ShortSide = patch.ShortSide,
                    LongSide = patch.LongSide,
                    Compactness = patch.Compactness,
                    Classification = patch.Classification,
                    Outcome = patch.Outcome.ToString(),
                    Confidence = patch.Confidence.ToString(),
                    Reason = patch.Reason,
                    Evidence = new List<string>(patch.Evidence)
                };
            }

            public ThickeningPatch ToPatch()
            {
                ThickeningOutcome outcome;
                ThickeningConfidence confidence;
                Enum.TryParse(Outcome, out outcome);
                Enum.TryParse(Confidence, out confidence);

                return new ThickeningPatch
                {
                    HostFloorId = HostFloorId,
                    SourceElementIds = new List<long>(SourceElementIds ?? new List<long>()),
                    Provenance = new List<string>(Provenance ?? new List<string>()),
                    Plans = (Plans ?? new List<PolygonRecord>()).Select(plan => plan.ToPolygon()).Where(plan => plan != null).ToList(),
                    TopElevation = TopElevation,
                    BottomElevation = BottomElevation,
                    BodyTop = BodyTop,
                    BodyBottom = BodyBottom,
                    OverHostRatio = OverHostRatio,
                    FootprintArea = FootprintArea,
                    ShortSide = ShortSide,
                    LongSide = LongSide,
                    Compactness = Compactness,
                    Classification = string.IsNullOrEmpty(Classification) ? "Unknown" : Classification,
                    Outcome = outcome,
                    Confidence = confidence,
                    Reason = Reason ?? "",
                    Evidence = new List<string>(Evidence ?? new List<string>())
                };
            }
        }

        [DataContract]
        public sealed class SupportRecord
        {
            [DataMember] public string Name = "";
            [DataMember] public string Story = "";
            [DataMember] public PolygonRecord Plan;
        }

        /// <summary>A plan region as plain numbers: x, y pairs for the outline and for each hole.</summary>
        [DataContract]
        public sealed class PolygonRecord
        {
            [DataMember] public List<double[]> Outer = new List<double[]>();
            [DataMember] public List<List<double[]>> Holes = new List<List<double[]>>();

            public static PolygonRecord From(PlanPolygon polygon)
            {
                if (polygon == null)
                {
                    return null;
                }

                return new PolygonRecord
                {
                    Outer = Points(polygon.Outer),
                    Holes = polygon.Holes.Select(Points).ToList()
                };
            }

            public PlanPolygon ToPolygon()
            {
                PlanRing outer = Ring(Outer);
                return outer == null ? null : PlanPolygon.Create(outer, (Holes ?? new List<List<double[]>>()).Select(Ring));
            }

            private static List<double[]> Points(PlanRing ring)
            {
                return ring == null
                    ? new List<double[]>()
                    : ring.Points.Select(point => new[] { point.X, point.Y }).ToList();
            }

            private static PlanRing Ring(List<double[]> points)
            {
                return points == null || points.Count < 3
                    ? null
                    : PlanRing.FromPoints(points.Select(point => new MyPoint(point[0], point[1], 0)));
            }
        }

        /// <summary>
        /// What the check would read for one level, cut down to that level: its ETABS shells,
        /// the Revit floors near enough its plane to be reached, and the columns and walls rising
        /// to it or from it.
        /// </summary>
        public static SlabCheckInputs Collect(string story, IList<Story_E> stories, IEnumerable<Slab_E> etabsSlabs,
                                              IEnumerable<Slab_R> revitFloors, IEnumerable<SlabSupport> supports)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            List<Story_E> upwards = (stories ?? new List<Story_E>())
                .Where(s => s != null && !string.IsNullOrEmpty(s.Name))
                .OrderBy(s => s.Elevation)
                .ToList();

            int at = upwards.FindIndex(s => Same(s.Name, story));
            double elevation = at >= 0 ? upwards[at].Elevation : 0;
            string above = at >= 0 && at + 1 < upwards.Count ? upwards[at + 1].Name : null;

            // The same reach the check gives a floor - see SlabCompareEngine.BuildCoverage - and a
            // metre either side of it, so a floor just outside it is in the file to be seen.
            double highest = elevation + pref.SlabTopElevationTolerance + 1000;
            double lowest = elevation - pref.SlabStackSearchDepth - pref.SlabTopElevationTolerance - 1000;

            SlabCheckInputs inputs = new SlabCheckInputs
            {
                Story = story ?? "",
                StoryElevation = elevation,
                WrittenBy = "DiscrepancyScanner " + Assembly.GetExecutingAssembly().GetName().Version,
                Stories = upwards,
                EtabsSlabs = (etabsSlabs ?? Enumerable.Empty<Slab_E>()).Where(slab => slab != null && Same(slab.Story, story)).ToList(),
                Preferences = ReadPreferences(pref)
            };

            foreach (Slab_R floor in revitFloors ?? Enumerable.Empty<Slab_R>())
            {
                if (floor == null || floor.Plan == null)
                {
                    continue;
                }
                if (!Same(floor.Story, story) && (floor.TopElevation > highest || floor.TopElevation < lowest))
                {
                    continue;
                }

                inputs.RevitFloors.Add(new FloorRecord
                {
                    RevitId = floor.RevitID,
                    TypeName = floor.TypeName ?? "",
                    ElementName = floor.ElementName ?? "",
                    CategoryName = floor.CategoryName ?? "",
                    Story = floor.Story ?? "",
                    TopElevation = floor.TopElevation,
                    BottomElevation = floor.BottomElevation,
                    MinimumThickness = floor.MinimumThickness,
                    MaximumThickness = floor.MaximumThickness,
                    NominalThickness = floor.NominalThickness,
                    StructuralThickness = floor.StructuralThickness,
                    HasVariableTypeThickness = floor.HasVariableTypeThickness,
                    IsStructural = floor.IsStructural,
                    GeometrySource = floor.GeometrySource ?? Slab_R.FromSolid,
                    OpeningsUncertain = floor.OpeningsUncertain,
                    Plan = PolygonRecord.From(floor.Plan),
                    Parts = floor.Parts.Select(PolygonRecord.From).Where(part => part != null).ToList(),
                    Patches = floor.Patches.Select(PatchRecord.From).ToList()
                });
            }

            foreach (SlabSupport support in supports ?? Enumerable.Empty<SlabSupport>())
            {
                if (support?.Plan == null || !(Same(support.Story, story) || Same(support.Story, above)))
                {
                    continue;
                }

                inputs.Supports.Add(new SupportRecord
                {
                    Name = support.Name ?? "",
                    Story = support.Story ?? "",
                    Plan = PolygonRecord.From(support.Plan)
                });
            }

            return inputs;
        }

        public void Write(string path)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(SlabCheckInputs)).WriteObject(stream, this);
                File.WriteAllBytes(path, stream.ToArray());
            }
        }

        public static SlabCheckInputs Read(string path)
        {
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(path)))
            {
                return (SlabCheckInputs)new DataContractJsonSerializer(typeof(SlabCheckInputs)).ReadObject(stream);
            }
        }

        /// <summary>The Revit floors as the check reads them.</summary>
        public List<Slab_R> ToRevitFloors()
        {
            List<Slab_R> floors = new List<Slab_R>();
            foreach (FloorRecord record in RevitFloors ?? new List<FloorRecord>())
            {
                Slab_R floor = new Slab_R(record.RevitId, record.Plan?.ToPolygon(), record.TopElevation, record.BottomElevation)
                {
                    TypeName = record.TypeName ?? "",
                    ElementName = record.ElementName ?? "",
                    CategoryName = record.CategoryName ?? "",
                    Story = record.Story ?? "",
                    MinimumThickness = record.MinimumThickness,
                    MaximumThickness = record.MaximumThickness,
                    NominalThickness = record.NominalThickness,
                    StructuralThickness = record.StructuralThickness,
                    HasVariableTypeThickness = record.HasVariableTypeThickness,
                    IsStructural = record.IsStructural,
                    GeometrySource = string.IsNullOrEmpty(record.GeometrySource) ? Slab_R.FromSolid : record.GeometrySource,
                    OpeningsUncertain = record.OpeningsUncertain
                };
                floor.Parts = (record.Parts ?? new List<PolygonRecord>()).Select(part => part.ToPolygon()).ToList();
                foreach (PatchRecord patch in record.Patches ?? new List<PatchRecord>())
                {
                    floor.Patches.Add(patch.ToPatch());
                }
                floors.Add(floor);
            }
            return floors;
        }

        public List<SlabSupport> ToSupports()
        {
            return (Supports ?? new List<SupportRecord>())
                .Select(record => new SlabSupport { Name = record.Name, Story = record.Story, Plan = record.Plan?.ToPolygon() })
                .Where(support => support.Plan != null)
                .ToList();
        }

        /// <summary>Puts the settings the check ran under back, for a run of it here.</summary>
        public void ApplyPreferences()
        {
            ApplyPreferences(Preferences);
        }

        /// <summary>Puts a set of saved settings back - see <see cref="ReadPreferences"/>.</summary>
        internal static void ApplyPreferences(Dictionary<string, string> preferences)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            foreach (KeyValuePair<string, string> entry in preferences ?? new Dictionary<string, string>())
            {
                FieldInfo field = typeof(PrefrencesData).GetField(entry.Key, BindingFlags.Public | BindingFlags.Instance);
                if (field == null)
                {
                    continue;
                }

                try
                {
                    field.SetValue(pref, Convert.ChangeType(entry.Value, field.FieldType, CultureInfo.InvariantCulture));
                }
                catch (Exception)
                {
                    // A setting this build reads differently is left as it is.
                }
            }
        }

        /// <summary>Every plain setting - numbers, switches and names - as text.</summary>
        internal static Dictionary<string, string> ReadPreferences(PrefrencesData pref)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FieldInfo field in typeof(PrefrencesData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Type type = field.FieldType;
                if (type != typeof(double) && type != typeof(bool) && type != typeof(int) && type != typeof(string))
                {
                    continue;
                }

                object value = field.GetValue(pref);
                values[field.Name] = value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            return values;
        }

        internal static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A name for the file: the level and the moment, safe for any folder.</summary>
        public static string FileNameFor(string story)
        {
            return FileNameFor("slab-inputs", story);
        }

        internal static string FileNameFor(string what, string story)
        {
            StringBuilder safe = new StringBuilder();
            foreach (char letter in story ?? "level")
            {
                safe.Append(char.IsLetterOrDigit(letter) ? letter : '-');
            }
            return $"{what}-{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        }
    }
}
