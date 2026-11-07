using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Everything the line load check reads for one level, written to a file so the check can be
    /// run again on it with no Revit and no ETABS - see <see cref="PrefrencesData.SaveLineLoadCheckInputs"/>.
    ///
    /// The slab check's file over again for the other check: the lines as they were handed to
    /// the check - read off the loading plan, copies already taken out - the ETABS frames on the
    /// level with the loads they carry, the schedule sheet the numbers are looked up in, the
    /// pairings set by hand, the height zones, and the settings it ran under. Which line went to
    /// which frame, and which of two lines took a load, is then a run here rather than a guess.
    /// </summary>
    [DataContract]
    public sealed class LineLoadCheckInputs
    {
        [DataMember] public string Story = "";
        [DataMember] public string ProjectKey = "";
        [DataMember] public string WrittenBy = "";
        [DataMember] public List<Story_E> Stories = new List<Story_E>();
        [DataMember] public List<LineRecord> RevitLines = new List<LineRecord>();
        [DataMember] public List<LineLoadFrame_E> EtabsFrames = new List<LineLoadFrame_E>();
        [DataMember] public ScheduleRecord Schedule;
        [DataMember] public Dictionary<string, long> Pairings = new Dictionary<string, long>();
        [DataMember] public List<HeightZoneRecord> HeightZones = new List<HeightZoneRecord>();
        [DataMember] public Dictionary<string, string> Preferences = new Dictionary<string, string>();

        [DataContract]
        public sealed class LineRecord
        {
            [DataMember] public long RevitId;
            [DataMember] public string StyleName = "";
            [DataMember] public int Number;
            [DataMember] public string Story = "";
            [DataMember] public bool IsOnSeveralStories;
            [DataMember] public double OwnPerMetre;
            [DataMember] public string OwnHow = "";
            [DataMember] public int Part;
            /// <summary>The line's corners in plan, x and y in mm, in the order it was drawn.</summary>
            [DataMember] public List<double[]> Points = new List<double[]>();
        }

        [DataContract]
        public sealed class ScheduleRecord
        {
            [DataMember] public string FilePath = "";
            [DataMember] public string SheetName = "";
            [DataMember] public string Title = "";
            [DataMember] public List<EntryRecord> Entries = new List<EntryRecord>();
        }

        [DataContract]
        public sealed class EntryRecord
        {
            [DataMember] public int Number;
            [DataMember] public string Description = "";
            [DataMember] public double Value;
            [DataMember] public bool HeightIndependent;
            [DataMember] public string ValueText = "";
        }

        [DataContract]
        public sealed class HeightZoneRecord
        {
            [DataMember] public string Id = "";
            [DataMember] public string Story = "";
            [DataMember] public string Name = "";
            [DataMember] public double Height;
            [DataMember] public List<double[]> Outline = new List<double[]>();
        }

        /// <summary>
        /// What the check would read for one level, cut down to that level: the lines drawn on
        /// it, the frames on it, its schedule sheet, and the pairings and height zones set on it.
        /// </summary>
        public static LineLoadCheckInputs Collect(string story, string projectKey, IList<Story_E> stories,
                                                  IEnumerable<LineLoad_R> drawnLines,
                                                  IEnumerable<LineLoadFrame_E> frames,
                                                  LineLoadSchedule schedule,
                                                  PairingOverrides overrides, HeightZones zones)
        {
            List<LineLoadFrame_E> onStory = (frames ?? Enumerable.Empty<LineLoadFrame_E>())
                .Where(frame => frame != null && SlabCheckInputs.Same(frame.Story, story))
                .ToList();
            HashSet<string> frameKeys = new HashSet<string>(
                onStory.Select(frame => PairingOverrides.KeyOf(ElementKindKey.LineLoad, frame.UniqueName)),
                StringComparer.OrdinalIgnoreCase);

            LineLoadCheckInputs inputs = new LineLoadCheckInputs
            {
                Story = story ?? "",
                ProjectKey = projectKey ?? "",
                WrittenBy = "DiscrepancyScanner " + Assembly.GetExecutingAssembly().GetName().Version,
                Stories = (stories ?? new List<Story_E>()).Where(s => s != null).OrderBy(s => s.Elevation).ToList(),
                EtabsFrames = onStory,
                Preferences = SlabCheckInputs.ReadPreferences(PrefrencesData.getPrefrencesData())
            };

            foreach (LineLoad_R line in drawnLines ?? Enumerable.Empty<LineLoad_R>())
            {
                if (line == null || !SlabCheckInputs.Same(line.Story, story))
                {
                    continue;
                }

                LineRecord record = new LineRecord
                {
                    RevitId = line.RevitID,
                    StyleName = line.StyleName ?? "",
                    Number = line.Number,
                    Story = line.Story ?? "",
                    IsOnSeveralStories = line.IsOnSeveralStories,
                    OwnPerMetre = line.OwnPerMetre,
                    OwnHow = line.OwnHow ?? "",
                    Part = line.Part
                };
                if (line.Pieces.Count > 0)
                {
                    record.Points.Add(new[] { line.Pieces[0].PlanStart.X, line.Pieces[0].PlanStart.Y });
                    record.Points.AddRange(line.Pieces.Select(piece => new[] { piece.PlanEnd.X, piece.PlanEnd.Y }));
                }
                inputs.RevitLines.Add(record);
            }

            if (schedule != null)
            {
                inputs.Schedule = new ScheduleRecord
                {
                    FilePath = schedule.FilePath ?? "",
                    SheetName = schedule.SheetName ?? "",
                    Title = schedule.Title ?? "",
                    Entries = schedule.Entries.Select(entry => new EntryRecord
                    {
                        Number = entry.Number,
                        Description = entry.Description ?? "",
                        Value = entry.Value,
                        HeightIndependent = entry.HeightIndependent,
                        ValueText = entry.ValueText ?? ""
                    }).ToList()
                };
            }

            if (overrides != null)
            {
                foreach (KeyValuePair<string, long> pin in overrides.For(projectKey))
                {
                    if (frameKeys.Contains(pin.Key))
                    {
                        inputs.Pairings[pin.Key] = pin.Value;
                    }
                }
            }

            if (zones != null)
            {
                inputs.HeightZones = zones.OnStory(projectKey, story).Select(zone => new HeightZoneRecord
                {
                    Id = zone.Id ?? "",
                    Story = zone.Story ?? "",
                    Name = zone.Name ?? "",
                    Height = zone.Height,
                    Outline = zone.Outline.Select(point => new[] { point.X, point.Y }).ToList()
                }).ToList();
            }

            return inputs;
        }

        public void Write(string path)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(LineLoadCheckInputs)).WriteObject(stream, this);
                File.WriteAllBytes(path, stream.ToArray());
            }
        }

        public static LineLoadCheckInputs Read(string path)
        {
            using (MemoryStream stream = new MemoryStream(File.ReadAllBytes(path)))
            {
                return (LineLoadCheckInputs)new DataContractJsonSerializer(typeof(LineLoadCheckInputs)).ReadObject(stream);
            }
        }

        /// <summary>The drawn lines as the check reads them.</summary>
        public List<LineLoad_R> ToRevitLines()
        {
            List<LineLoad_R> lines = new List<LineLoad_R>();
            foreach (LineRecord record in RevitLines ?? new List<LineRecord>())
            {
                IEnumerable<MyPoint> points = (record.Points ?? new List<double[]>())
                    .Select(point => new MyPoint(point[0], point[1], 0));

                LineLoad_R line = record.OwnPerMetre > 0
                    ? LineLoad_R.Weighed(record.RevitId, record.StyleName, points, record.OwnPerMetre,
                                         record.OwnHow, record.Story, record.Part)
                    : new LineLoad_R(record.RevitId, record.StyleName, points, record.Number, record.Story);

                // Through OnStory so the several-stories mark is set the way the read sets it.
                lines.Add(line.OnStory(record.Story, record.IsOnSeveralStories));
            }
            return lines;
        }

        public LineLoadSchedule ToSchedule()
        {
            if (Schedule == null)
            {
                return null;
            }

            LineLoadSchedule schedule = new LineLoadSchedule(Schedule.FilePath, Schedule.SheetName)
            {
                Title = Schedule.Title ?? ""
            };
            foreach (EntryRecord entry in Schedule.Entries ?? new List<EntryRecord>())
            {
                schedule.Add(new LineLoadEntry(entry.Number, entry.Description, entry.Value,
                                               entry.HeightIndependent, entry.ValueText));
            }
            return schedule;
        }

        /// <summary>
        /// The line load check set up the way it ran, with the saved settings put back first,
        /// and run on the saved lines and frames.
        /// </summary>
        public LineLoadCompareEngine Replay()
        {
            SlabCheckInputs.ApplyPreferences(Preferences);

            PairingOverrides overrides = new PairingOverrides();
            foreach (KeyValuePair<string, long> pin in Pairings ?? new Dictionary<string, long>())
            {
                overrides.Set(ProjectKey, pin.Key, pin.Value);
            }

            HeightZones zones = new HeightZones();
            foreach (HeightZoneRecord record in HeightZones ?? new List<HeightZoneRecord>())
            {
                HeightZone zone = new HeightZone { Id = record.Id, Story = record.Story, Name = record.Name, Height = record.Height };
                zone.SetOutline((record.Outline ?? new List<double[]>()).Select(point => new MyPoint(point[0], point[1], 0)));
                zones.Add(ProjectKey, zone);
            }

            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                Overrides = overrides,
                HeightZones = zones,
                ProjectKey = ProjectKey
            };
            engine.SetStoryHeights(Stories);
            engine.Patterns = LineLoadPatterns.From(PrefrencesData.getPrefrencesData());
            engine.SetStoriesToCheck(new[] { Story });

            LineLoadSchedule schedule = ToSchedule();
            if (schedule != null)
            {
                engine.SetSchedule(Story, schedule);
            }

            engine.StartComparison(ToRevitLines(), EtabsFrames ?? new List<LineLoadFrame_E>());
            return engine;
        }

        public static string FileNameFor(string story)
        {
            return SlabCheckInputs.FileNameFor("line-load-inputs", story);
        }
    }
}
