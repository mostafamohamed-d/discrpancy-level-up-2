using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The columns and walls standing in the slab, read from the columns and walls of both
    /// models rather than from how either model marks them in the slab.
    ///
    /// A stiff area stacked on the slab was already read as the support it is - see
    /// <see cref="StackedStiffAreaTests"/> - but only a stiff area, and only stacked. A rigid
    /// zone given a slab type, a Revit floor cut by a column ETABS runs its slab through, a
    /// thicker floor drawn only where the column is: each came back as a thickness or opening
    /// difference at every column on every level, and every one of them was ignored by hand.
    ///
    /// Everything is run through the real check, over a 12 m bay of 250 slab with a 1 m square
    /// column in the middle of it.
    /// </summary>
    [TestClass]
    public class SupportInsideSlabTests
    {
        private const string Story = "L02";
        private const string Above = "L03";
        private const string Below = "L01";

        private const double Centre = 6000;
        private const double ColumnSide = 1000;

        #region the hole the column cuts

        [TestMethod]
        public void TheHoleAColumnCutsInTheRevitFloorIsFilled()
        {
            List<SlabZone> zones = Run(Shells(), Floors(withColumnHole: true), Column(Story));

            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched),
                          "ETABS runs the slab through the column, and so does Revit once the hole is " +
                          "filled: " + Describe(zones));
        }

        [TestMethod]
        public void WithoutTheColumnTheHoleIsStillAnOpening()
        {
            List<SlabZone> zones = Run(Shells(), Floors(withColumnHole: true));

            Assert.IsTrue(zones.Any(zone => zone.State == SlabZoneState.OpeningInRevitOnly),
                          "nothing says a column stands there: " + Describe(zones));
        }

        [TestMethod]
        public void AColumnRisingFromTheSlabFillsItsHoleToo()
        {
            List<SlabZone> zones = Run(Shells(), Floors(withColumnHole: true), Column(Above));

            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
        }

        [TestMethod]
        public void AColumnOnAnotherLevelSaysNothingAboutThisSlab()
        {
            List<SlabZone> zones = Run(Shells(), Floors(withColumnHole: true), Column(Below));

            Assert.IsTrue(zones.Any(zone => zone.State == SlabZoneState.OpeningInRevitOnly),
                          "the column below the level under this one does not reach it: " + Describe(zones));
        }

        #endregion

        #region a rigid zone that is not a stiff area

        [TestMethod]
        public void ARigidZoneGivenASlabTypeIsReadAsTheColumn()
        {
            List<Slab_E> shells = Shells();
            shells.Add(RigidZone());

            List<SlabZone> zones = Run(shells, Floors(withColumnHole: true), Column(Story));

            Assert.IsFalse(zones.Any(zone => zone.EtabsName == "K1"),
                           "it stands in for the column, so it carries no verdict: " + Describe(zones));
            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
        }

        [TestMethod]
        public void WithoutTheColumnARigidZoneIsCheckedAsSlab()
        {
            List<Slab_E> shells = Shells();
            shells.Add(RigidZone());

            List<SlabZone> zones = Run(shells, Floors(withColumnHole: true));

            Assert.IsTrue(zones.Any(zone => zone.EtabsName == "K1" && zone.Level == ResultLevel.Failed),
                          "a thousand of slab over the hole, exactly as before: " + Describe(zones));
        }

        [TestMethod]
        public void AShellMeshedInUnderAWallIsCheckedAsSlab()
        {
            // The slab meshed along a wall: a strip under the wall and the slab either side of it,
            // edge to edge, nothing drawn over anything.
            SlabSupport wall = new SlabSupport
            {
                Plan = Rectangle(0, Centre - 100, 12000, Centre + 100),
                Story = Story,
                Name = "wall W1"
            };
            List<Slab_E> shells = new List<Slab_E>
            {
                Shell("A1", "S250", SlabKind.Slab, 250, Rectangle(0, 0, 12000, Centre - 100)),
                Shell("A2", "S250", SlabKind.Slab, 250, Rectangle(0, Centre - 100, 12000, Centre + 100)),
                Shell("A3", "S250", SlabKind.Slab, 250, Rectangle(0, Centre + 100, 12000, 12000))
            };

            List<SlabZone> zones = Run(shells, Floors(withColumnHole: false), wall);

            Assert.IsTrue(zones.Any(zone => zone.EtabsName == "A2"),
                          "the strip is slab, and is drawn rather than left as a hole along the wall: " + Describe(zones));
            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
        }

        [TestMethod]
        public void AnOpeningRunningOnPastAWallIsReportedForThePartBeyondIt()
        {
            // The floor cut along a 200 wall three metres long, and on for 600 past its end.
            SlabSupport wall = new SlabSupport
            {
                Plan = Rectangle(3000, Centre - 100, 6000, Centre + 100),
                Story = Story,
                Name = "wall W1"
            };
            PlanPolygon plan = PlanPolygon.Create(Rectangle(0, 0, 12000, 12000).Outer,
                                                  new[] { Rectangle(3000, Centre - 100, 6600, Centre + 100).Outer });

            List<SlabZone> zones = Run(Shells(), new List<Slab_R> { Floor(500, plan, 250) }, wall);

            double opening = zones.Where(zone => zone.State == SlabZoneState.OpeningInRevitOnly
                                                 && !zone.IsAcceptedAsInsideSupport)
                                  .Sum(zone => zone.Area);
            Assert.AreEqual(600d * 200d - 50d * 200d, opening, 2000,
                            "the 600 past the wall, less the margin the wall is given: " + Describe(zones));
        }

        #endregion

        #region what is still found inside one

        [TestMethod]
        public void AThicknessDifferenceInsideAColumnIsAccepted()
        {
            List<SlabZone> zones = Run(Shells(), FloorsWithCapital(ColumnSide), Column(Story));

            SlabZone inside = zones.Single(zone => zone.State == SlabZoneState.ThicknessMismatch);

            Assert.IsTrue(inside.IsAcceptedAsInsideSupport, Describe(zones));
            Assert.AreEqual("column C1", inside.InsideSupport);
            StringAssert.Contains(inside.Summary, "accepted automatically: inside column C1");
            Assert.AreEqual(ResultLevel.Failed, inside.Level,
                            "the verdict is untouched: the two models really do differ there");
        }

        [TestMethod]
        public void AThicknessDifferenceInsideAWallIsAccepted()
        {
            SlabSupport wall = new SlabSupport
            {
                Plan = Rectangle(2000, Centre - 100, 10000, Centre + 100),
                Story = Story,
                Name = "wall W1"
            };

            List<Slab_R> floors = Floors(withColumnHole: false);
            floors.Add(Floor(502, Rectangle(2000, Centre - 100, 10000, Centre + 100), 450));

            List<SlabZone> zones = Run(Shells(), floors, wall);

            SlabZone inside = zones.Single(zone => zone.State == SlabZoneState.ThicknessMismatch);
            Assert.AreEqual("wall W1", inside.InsideSupport, Describe(zones));
        }

        [TestMethod]
        public void ADropPanelAroundTheColumnIsNotAccepted()
        {
            // Three metres square around a one metre column: a drop the ETABS model has not got,
            // which is a real difference and stays work.
            List<SlabZone> zones = Run(Shells(), FloorsWithCapital(3000), Column(Story));

            SlabZone drop = zones.Single(zone => zone.State == SlabZoneState.ThicknessMismatch);
            Assert.IsFalse(drop.IsAcceptedAsInsideSupport, Describe(zones));
        }

        [TestMethod]
        public void SwitchedOffEverythingIsCheckedAsBefore()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            bool was = pref.SlabAcceptInsideColumnsAndWalls;
            try
            {
                pref.SlabAcceptInsideColumnsAndWalls = false;

                List<SlabZone> holed = Run(Shells(), Floors(withColumnHole: true), Column(Story));
                Assert.IsTrue(holed.Any(zone => zone.State == SlabZoneState.OpeningInRevitOnly), Describe(holed));

                List<SlabZone> capital = Run(Shells(), FloorsWithCapital(ColumnSide), Column(Story));
                Assert.IsFalse(capital.Any(zone => zone.IsAcceptedAsInsideSupport), Describe(capital));
            }
            finally
            {
                pref.SlabAcceptInsideColumnsAndWalls = was;
            }
        }

        [TestMethod]
        public void AnAcceptedPieceIsDrawnAsSettledAndNotCounted()
        {
            List<SlabZone> zones = Run(Shells(), FloorsWithCapital(ColumnSide), Column(Story));
            SlabZone inside = zones.Single(zone => zone.State == SlabZoneState.ThicknessMismatch);

            ElementViewModel element = ElementViewModelFactory.FromSlabZone(inside);

            Assert.IsTrue(element.IsAutoAccepted);
            Assert.IsTrue(element.IsAccepted, "drawn in the accepted colour and left out of the counts");
        }

        #endregion

        #region the model

        private static List<SlabZone> Run(List<Slab_E> shells, List<Slab_R> floors, params SlabSupport[] supports)
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetColumnsAndWalls(supports);
            engine.StartComparison(shells, floors, new List<Story_E>
            {
                new Story_E(Below, -3000, Form1.eLength.mm),
                new Story_E(Story, 0, Form1.eLength.mm),
                new Story_E(Above, 3000, Form1.eLength.mm)
            });

            return engine.Zones.ToList();
        }

        private static SlabSupport Column(string story)
        {
            return new SlabSupport { Plan = ColumnPlan(ColumnSide), Story = story, Name = "column C1" };
        }

        private static List<Slab_E> Shells()
        {
            return new List<Slab_E> { Shell("A1", "S250", SlabKind.Slab, 250, Rectangle(0, 0, 12000, 12000)) };
        }

        /// <summary>A 1000 thick shell at the column, given a slab type rather than a stiff one.</summary>
        private static Slab_E RigidZone()
        {
            return Shell("K1", "RIGID", SlabKind.Slab, 1000, ColumnPlan(ColumnSide));
        }

        private static List<Slab_R> Floors(bool withColumnHole)
        {
            PlanPolygon plan = Rectangle(0, 0, 12000, 12000);
            if (withColumnHole)
            {
                plan = PlanPolygon.Create(plan.Outer, new[] { ColumnPlan(ColumnSide).Outer });
            }

            return new List<Slab_R> { Floor(500, plan, 250) };
        }

        /// <summary>The 250 floor, with a 450 one drawn over it square on the column.</summary>
        private static List<Slab_R> FloorsWithCapital(double side)
        {
            List<Slab_R> floors = Floors(withColumnHole: false);
            floors.Add(Floor(501, ColumnPlan(side), 450));
            return floors;
        }

        private static PlanPolygon ColumnPlan(double side)
        {
            double half = side / 2;
            return Rectangle(Centre - half, Centre - half, Centre + half, Centre + half);
        }

        private static Slab_E Shell(string name, string property, SlabKind kind, double thickness, PlanPolygon plan)
        {
            List<Joint> joints = plan.Outer.Points
                .Select(point => new Joint("", new MyPoint(point.X, point.Y, 0), Form1.eLength.mm))
                .ToList();

            return new Slab_E(name, joints, Story, property, kind, thickness, thickness, false);
        }

        private static Slab_R Floor(long revitId, PlanPolygon plan, double thickness)
        {
            return new Slab_R(revitId, plan, 0, -thickness)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = "SLAB " + thickness,
                StructuralThickness = thickness
            };
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(minX, minY, 0),
                new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0),
                new MyPoint(minX, maxY, 0)
            });
        }

        private static string Describe(List<SlabZone> zones)
        {
            return zones.Count == 0
                ? "no zones at all"
                : string.Join(", ", zones.Select(z =>
                    $"{z.EtabsName}{(z.RevitId > 0 ? "/R" + z.RevitId : "")}:{z.State}={z.Area / 1e6:0.###} m2"
                    + (z.InsideSupport.Length > 0 ? " in " + z.InsideSupport : "")));
        }

        #endregion
    }
}
