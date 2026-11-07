using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A floor read off its sketch when Revit gives no solid for it: the regions its loops
    /// make, the openings cut out of them, and what the slab check does with one whose cuts
    /// could not all be taken out.
    /// </summary>
    [TestClass]
    public class SketchFloorTests
    {
        private const string Story = "GROUND";

        private static IList<MyPoint> Loop(double minX, double minY, double maxX, double maxY, bool clockwise = false)
        {
            List<MyPoint> points = new List<MyPoint>
            {
                new MyPoint(minX, minY, 0), new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0), new MyPoint(minX, maxY, 0)
            };
            if (clockwise)
            {
                points.Reverse();
            }
            return points;
        }

        [TestMethod]
        public void ALoopInsideAnotherIsAHoleWhicheverWayItIsDrawn()
        {
            List<PlanPolygon> regions = SketchFloor.Regions(new[]
            {
                Loop(2000, 2000, 3000, 3000),            // drawn the same way as the outline
                Loop(0, 0, 10000, 10000, clockwise: true)
            });

            Assert.AreEqual(1, regions.Count);
            Assert.AreEqual(1, regions[0].Holes.Count);
            Assert.AreEqual(100e6 - 1e6, regions[0].Area, 1);
        }

        [TestMethod]
        public void ALoopInsideAHoleIsSlabAgain()
        {
            List<PlanPolygon> regions = SketchFloor.Regions(new[]
            {
                Loop(0, 0, 10000, 10000),
                Loop(2000, 2000, 8000, 8000),
                Loop(4000, 4000, 6000, 6000)
            });

            Assert.AreEqual(2, regions.Count, "the outline with its hole, and the island in the hole");
            Assert.AreEqual(100e6 - 36e6 + 4e6, regions.Sum(r => r.Area), 1);
        }

        [TestMethod]
        public void AnOpeningInsideTheSlabIsCutOut()
        {
            int cut, notCut;
            List<PlanPolygon> regions = SketchFloor.CutOpenings(
                SketchFloor.Regions(new[] { Loop(0, 0, 10000, 10000) }),
                new[] { Loop(1000, 1000, 2000, 2000) }, out cut, out notCut);

            Assert.AreEqual(1, cut);
            Assert.AreEqual(0, notCut);
            Assert.AreEqual(100e6 - 1e6, regions.Sum(r => r.Area), 1);
        }

        [TestMethod]
        public void AnOpeningAcrossTheEdgeIsNotCutButCounted()
        {
            int cut, notCut;
            List<PlanPolygon> regions = SketchFloor.CutOpenings(
                SketchFloor.Regions(new[] { Loop(0, 0, 10000, 10000) }),
                new[] { Loop(9500, 1000, 10500, 2000) }, out cut, out notCut);

            Assert.AreEqual(0, cut);
            Assert.AreEqual(1, notCut, "left in, and counted so the floor goes up for review");
            Assert.AreEqual(100e6, regions.Sum(r => r.Area), 1);
        }

        [TestMethod]
        public void AnOpeningInAHoleTheSketchHasCutsNothing()
        {
            int cut, notCut;
            SketchFloor.CutOpenings(
                SketchFloor.Regions(new[] { Loop(0, 0, 10000, 10000), Loop(1000, 1000, 3000, 3000) }),
                new[] { Loop(1500, 1500, 2500, 2500) }, out cut, out notCut);

            Assert.AreEqual(0, cut);
            Assert.AreEqual(0, notCut);
        }

        [TestMethod]
        public void AnOpeningOutsideTheFloorIsNotCounted()
        {
            int cut, notCut;
            SketchFloor.CutOpenings(
                SketchFloor.Regions(new[] { Loop(0, 0, 10000, 10000) }),
                new[] { Loop(20000, 20000, 21000, 21000) }, out cut, out notCut);

            Assert.AreEqual(0, cut);
            Assert.AreEqual(0, notCut);
        }

        [TestMethod]
        public void AFloorWithUncertainOpeningsIsPutUpForReviewNeverPassedOrFailed()
        {
            // ETABS has 250 over half the plan; the sketch-read Revit floor is 250 over all of it
            // and is cut by something its sketch cannot show.
            PlanPolygon plan = PlanPolygon.FromPoints(Loop(0, 0, 12000, 12000));
            Slab_R floor = new Slab_R(900, plan, 0, -250)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = "MHT_STR_DRP_350",
                StructuralThickness = 250,
                GeometrySource = Slab_R.FromSketch,
                OpeningsUncertain = true
            };
            Slab_E shell = new Slab_E("A1", Loop(0, 0, 6000, 12000).Select(p => new Joint("", p, Form1.eLength.mm)).ToList(),
                                      Story, "S250", SlabKind.Slab, 250, 250, false);

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(new List<Slab_E> { shell }, new List<Slab_R> { floor },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });
            List<SlabZone> zones = engine.Zones.ToList();

            Assert.IsTrue(zones.Any(), "the floor is in the check");
            Assert.IsFalse(zones.Any(z => z.State == SlabZoneState.Matched || z.State == SlabZoneState.ThicknessMismatch
                                          || z.State == SlabZoneState.MissingInEtabs),
                           string.Join(", ", zones.Select(z => z.State + " " + z.Area / 1e6)));
            Assert.IsTrue(zones.Any(z => z.State == SlabZoneState.ThicknessReview), "where both have slab: review");
            Assert.IsTrue(zones.Any(z => z.State == SlabZoneState.Unresolved), "where only the sketch says slab: unresolved");
            Assert.IsTrue(zones.All(z => z.RevitSource == Slab_R.FromSketch));
        }

        [TestMethod]
        public void TheSourceIsKeptThroughTheSavedInputs()
        {
            PlanPolygon plan = PlanPolygon.FromPoints(Loop(0, 0, 1000, 1000));
            Slab_R floor = new Slab_R(900, plan, 0, -250)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                GeometrySource = Slab_R.FromSketch,
                OpeningsUncertain = true
            };

            SlabCheckInputs inputs = SlabCheckInputs.Collect(Story, new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) },
                                                             new List<Slab_E>(), new List<Slab_R> { floor }, null);
            Slab_R back = inputs.ToRevitFloors().Single();

            Assert.AreEqual(Slab_R.FromSketch, back.GeometrySource);
            Assert.IsTrue(back.OpeningsUncertain);
        }
    }
}
