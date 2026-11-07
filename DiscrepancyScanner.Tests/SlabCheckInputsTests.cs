using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The slab check's inputs written to a file and read back: the check run on what comes out
    /// has to say what it said on what went in, or the file is no way to look at somebody's
    /// finding at all.
    ///
    /// Also the place a file sent in from a real model is run: put its path in the
    /// DISCREPANCY_SLAB_INPUTS environment variable and <see cref="ASavedFileRunsAgain"/> runs
    /// the slab check on it and prints every piece it found.
    /// </summary>
    [TestClass]
    public class SlabCheckInputsTests
    {
        private const string Story = "L1";
        private const string Above = "L2";

        [TestMethod]
        public void TheCheckSaysTheSameOnTheFileAsOnTheModel()
        {
            // A 250 slab over a floor with a Revit-only opening in it, and a column standing
            // in the slab whose hole in the floor is filled back in.
            List<Story_E> stories = Stories();
            List<Slab_E> shells = new List<Slab_E> { Shell("A1", Rectangle(0, 0, 12000, 12000)) };
            PlanPolygon floorPlan = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer, new[]
            {
                Rectangle(2000, 2000, 3500, 3000).Outer,
                Rectangle(5800, 5800, 6200, 6200).Outer
            });
            List<Slab_R> floors = new List<Slab_R> { Floor(500, floorPlan) };
            List<SlabSupport> supports = new List<SlabSupport>
            {
                new SlabSupport { Plan = Rectangle(5800, 5800, 6200, 6200), Story = Above, Name = "column C1" }
            };

            List<string> before = Describe(Run(shells, floors, supports, stories));

            string path = Path.Combine(Path.GetTempPath(), "slab-inputs-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                SlabCheckInputs.Collect(Story, stories, shells, floors, supports).Write(path);
                SlabCheckInputs read = SlabCheckInputs.Read(path);

                Assert.AreEqual(1, read.EtabsSlabs.Count);
                Assert.AreEqual(1, read.RevitFloors.Count);
                Assert.AreEqual(1, read.Supports.Count);

                List<string> after = Describe(Run(read.EtabsSlabs, read.ToRevitFloors(), read.ToSupports(), read.Stories));

                CollectionAssert.AreEqual(before, after, "before:\n" + string.Join("\n", before)
                                                         + "\nafter:\n" + string.Join("\n", after));
                Assert.IsTrue(before.Any(zone => zone.StartsWith(SlabZoneState.OpeningInRevitOnly.ToString())),
                              "the opening is found in the first place:\n" + string.Join("\n", before));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ASavedFileRunsAgain()
        {
            string path = Environment.GetEnvironmentVariable("DISCREPANCY_SLAB_INPUTS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Assert.Inconclusive("Set DISCREPANCY_SLAB_INPUTS to a saved slab inputs file to run it.");
            }

            SlabCheckInputs read = SlabCheckInputs.Read(path);
            read.ApplyPreferences();
            if (Environment.GetEnvironmentVariable("DISCREPANCY_SLAB_MEASURED") == "1")
            {
                PrefrencesData.getPrefrencesData().ReadSlabThicknessFromType = false;
            }

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = read.Story };
            engine.SetColumnsAndWalls(read.ToSupports());
            engine.StartComparison(read.EtabsSlabs, read.ToRevitFloors(), read.Stories);

            foreach (string line in engine.Issues)
            {
                Console.WriteLine("ISSUE " + line);
            }
            foreach (SlabZone zone in engine.Zones)
            {
                MyPoint inside = zone.Plan?.InteriorPoint();
                Console.WriteLine($"{zone.State} {zone.EtabsName} area={Math.Round(zone.Area)} "
                                  + $"at={(inside == null ? "-" : Math.Round(inside.X) + "," + Math.Round(inside.Y))} "
                                  + $"inside={zone.InsideSupport} etabs={zone.EtabsProperty}:{Math.Round(zone.EtabsThickness)} "
                                  + $"revit={zone.RevitId}:{zone.RevitType}:{Math.Round(zone.RevitThickness)}");
            }
        }

        private static List<SlabZone> Run(IList<Slab_E> shells, IList<Slab_R> floors, IList<SlabSupport> supports,
                                          IList<Story_E> stories)
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetColumnsAndWalls(supports);
            engine.StartComparison(shells, floors, stories);
            return engine.Zones.ToList();
        }

        private static List<string> Describe(IEnumerable<SlabZone> zones)
        {
            return zones.Select(zone => $"{zone.State} {zone.EtabsName} {Math.Round(zone.Area)} {zone.InsideSupport}")
                        .OrderBy(line => line, StringComparer.Ordinal)
                        .ToList();
        }

        private static List<Story_E> Stories()
        {
            return new List<Story_E>
            {
                new Story_E(Story, 0, Form1.eLength.mm),
                new Story_E(Above, 3000, Form1.eLength.mm)
            };
        }

        private static Slab_E Shell(string name, PlanPolygon plan)
        {
            List<Joint> joints = plan.Outer.Points
                .Select(point => new Joint("", new MyPoint(point.X, point.Y, 0), Form1.eLength.mm))
                .ToList();
            return new Slab_E(name, joints, Story, "S250", SlabKind.Slab, 250, 250, false);
        }

        private static Slab_R Floor(long revitId, PlanPolygon plan)
        {
            return new Slab_R(revitId, plan, 0, -250)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = "SLAB 250",
                StructuralThickness = 250
            };
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(minX, minY, 0), new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0), new MyPoint(minX, maxY, 0)
            });
        }
    }
}
