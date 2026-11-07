using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The line load check's inputs written to a file and read back: the check run on what comes
    /// out has to say what it said on what went in.
    ///
    /// Also the place a file sent in from a real model is run: put its path in the
    /// DISCREPANCY_LINELOAD_INPUTS environment variable and <see cref="ASavedFileRunsAgain"/>
    /// runs the line load check on it and prints every line's stretches.
    /// </summary>
    [TestClass]
    public class LineLoadCheckInputsTests
    {
        private const string Story = "L03";
        private const string Project = "test-project";

        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadStyleNamePatterns = "Line Load";
            pref.LineLoadHeightReduction = 200;
            pref.LineLoadRoundUpTo = 0;
            pref.LineLoadLocationTolerance = 700;
            pref.LineLoadSearchRadius = 700;
            pref.LineLoadAngleTolerance = 20;
            pref.LineLoadMinimumIssueLength = 250;
            pref.LineLoadTolerance = 0.2;
            pref.LineLoadCheckInSteps = true;
            pref.LineLoadStepLength = 250;
            pref.LineLoadPatternNames = "SIDL";
        }

        [TestMethod]
        public void TheCheckSaysTheSameOnTheFileAsOnTheModel()
        {
            // Two lines 100 apart along one loaded beam, a second beam 500 over carrying nothing,
            // one of the lines pinned to it by hand, and a height zone over half the level.
            List<Story_E> stories = new List<Story_E>
            {
                new Story_E(Story, 0, Form1.eLength.mm),
                new Story_E("L04", 3000, Form1.eLength.mm)
            };
            List<LineLoadFrame_E> frames = new List<LineLoadFrame_E>
            {
                Frame("B1", 0, 0, 6000, 0, 13.44),
                Frame("B2", 0, 500, 6000, 500, 0),
                new LineLoadFrame_E("B9", "B9", "L04", new MyPoint(0, 0, 0), new MyPoint(6000, 0, 0))
            };
            List<LineLoad_R> lines = new List<LineLoad_R>
            {
                new LineLoad_R(901, "MRG_STR-Line Load-TL1", new[] { At(0, 50), At(6000, 50) }, 1, Story),
                new LineLoad_R(902, "MRG_STR-Line Load-TL1", new[] { At(0, 150), At(3000, 150), At(6000, 160) }, 1, Story),
                new LineLoad_R(903, "MRG_STR-Line Load-TL1", new[] { At(0, 0), At(6000, 0) }, 1, "L04")
            };
            LineLoadSchedule schedule = new LineLoadSchedule("", "Line Load D (TOWER)");
            schedule.Add(new LineLoadEntry(1, "ALLOWANCE FOR 200mm THICK SOLID BLOCK WALL", 4.8, false, "4.8"));

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.LineLoad, "B2"), 902);
            HeightZones zones = new HeightZones();
            HeightZone zone = new HeightZone { Story = Story, Name = "lobby", Height = 4500 };
            zone.SetOutline(new[] { At(3000, -1000), At(7000, -1000), At(7000, 1000), At(3000, 1000) });
            zones.Add(Project, zone);

            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                Overrides = overrides,
                HeightZones = zones,
                ProjectKey = Project
            };
            engine.SetStoryHeights(stories);
            engine.Patterns = LineLoadPatterns.From(PrefrencesData.getPrefrencesData());
            engine.SetStoriesToCheck(new[] { Story });
            engine.SetSchedule(Story, schedule);
            engine.StartComparison(lines.Where(line => line.Story == Story).ToList(), frames);
            List<string> before = Describe(engine);

            string path = Path.Combine(Path.GetTempPath(), "line-load-inputs-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                LineLoadCheckInputs.Collect(Story, Project, stories, lines, frames, schedule, overrides, zones).Write(path);
                LineLoadCheckInputs read = LineLoadCheckInputs.Read(path);

                Assert.AreEqual(2, read.RevitLines.Count, "only the lines on the level");
                Assert.AreEqual(2, read.EtabsFrames.Count, "only the frames on the level");
                Assert.AreEqual(1, read.Pairings.Count);
                Assert.AreEqual(1, read.HeightZones.Count);
                Assert.AreEqual(1, read.Schedule.Entries.Count);
                Assert.AreEqual(3, read.RevitLines[1].Points.Count, "every corner of the line");

                List<string> after = Describe(read.Replay());

                CollectionAssert.AreEqual(before, after, "before:\n" + string.Join("\n", before)
                                                         + "\nafter:\n" + string.Join("\n", after));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ASavedFileRunsAgain()
        {
            string path = Environment.GetEnvironmentVariable("DISCREPANCY_LINELOAD_INPUTS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Assert.Inconclusive("Set DISCREPANCY_LINELOAD_INPUTS to a saved line load inputs file to run it.");
            }

            LineLoadCompareEngine engine = LineLoadCheckInputs.Read(path).Replay();

            foreach (string issue in engine.Issues)
            {
                Console.WriteLine("ISSUE " + issue);
            }
            foreach (string line in Describe(engine))
            {
                Console.WriteLine(line);
            }
        }

        private static List<string> Describe(LineLoadCompareEngine engine)
        {
            List<string> lines = new List<string>();
            foreach (LineLoadResult result in engine.Results)
            {
                lines.Add($"{result.Line.RevitID} {result.State} expected={Math.Round(result.Expected, 2)} "
                          + $"on={string.Join("+", result.Runs.Select(run => run.Identifier))}");
                foreach (LineLoadStretch stretch in result.Stretches)
                {
                    lines.Add($"  {Math.Round(stretch.From)}-{Math.Round(stretch.To)} {stretch.State} "
                              + $"wanted={Math.Round(stretch.Wanted, 2)} carried={Math.Round(stretch.Carried, 2)} "
                              + $"frames={stretch.Frames} taken={stretch.TakenBy}");
                }
            }
            foreach (EtabsLineLoad unclaimed in engine.Unclaimed)
            {
                lines.Add($"unclaimed {unclaimed.FrameName} {Math.Round(unclaimed.Start.X)},{Math.Round(unclaimed.Start.Y)}-"
                          + $"{Math.Round(unclaimed.End.X)},{Math.Round(unclaimed.End.Y)} {Math.Round(unclaimed.PerMetre, 2)}");
            }
            return lines;
        }

        private static LineLoadFrame_E Frame(string name, double x1, double y1, double x2, double y2, double perMetre)
        {
            LineLoadFrame_E frame = new LineLoadFrame_E(name, name, Story, At(x1, y1), At(x2, y2));
            if (perMetre > 0)
            {
                frame.Add(new LineLoad_E("SIDL", perMetre, perMetre, 0, 1, 10, true, false));
            }
            return frame;
        }

        private static MyPoint At(double x, double y)
        {
            return new MyPoint(x, y, 0);
        }
    }
}
