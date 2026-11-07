using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// What the slab check does today with small differences between the two models' slab
    /// boundaries - characterization, not specification. Nothing here says the behaviour is
    /// right; it pins down what it is, under the shipped thresholds, so a change to any of
    /// them shows up as a changed line here before it shows up in a model.
    ///
    /// The thresholds in play (see SlabMeshCompare): a piece under SlabMinimumZoneArea
    /// (10,000 mm2) or SlabMinimumZoneWidth (20 mm, twice the area over the perimeter) is held
    /// back, kept only if the same answer carries on into a reported piece, otherwise removed.
    /// SlabEdgeTolerance (25 mm) only decides whether a mismatched piece is an edge or an
    /// opening. Each case logs every zone it got.
    /// </summary>
    [TestClass]
    public class SlabBoundaryCharacterizationTests
    {
        private const string Story = "GROUND";
        private const double Size = 12000;

        private double area, width, edge, thickness;
        private bool fromType;

        [TestInitialize]
        public void ShippedThresholds()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            area = pref.SlabMinimumZoneArea;
            width = pref.SlabMinimumZoneWidth;
            edge = pref.SlabEdgeTolerance;
            thickness = pref.SlabThicknessTolerance;
            fromType = pref.ReadSlabThicknessFromType;

            pref.SlabMinimumZoneArea = 10000;
            pref.SlabMinimumZoneWidth = 20;
            pref.SlabEdgeTolerance = 25;
            pref.SlabThicknessTolerance = 1;
            pref.ReadSlabThicknessFromType = true;
        }

        [TestCleanup]
        public void PutBack()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.SlabMinimumZoneArea = area;
            pref.SlabMinimumZoneWidth = width;
            pref.SlabEdgeTolerance = edge;
            pref.SlabThicknessTolerance = thickness;
            pref.ReadSlabThicknessFromType = fromType;
        }

        #region 1. slab outline offsets

        [TestMethod]
        public void RevitEdgeShortOfEtabs()
        {
            foreach (double d in new[] { 5d, 15d, 25d, 40d })
            {
                List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size) },
                                           Floor(500, Rectangle(0, 0, Size - d, Size), 250));
                Log($"1a Revit edge {d} mm short of ETABS (strip {d} x {Size} = {d * Size / 1e6:0.###} m2)", zones);

                // Under 20 mm wide the strip is removed outright; from 20 mm it is reported.
                if (d < 20)
                {
                    Expect(zones, $"{d} mm", "Matched");
                }
                else
                {
                    Expect(zones, $"{d} mm", "Matched", "MissingInRevit");
                    Assert.AreEqual(d * Size, Area(zones, SlabZoneState.MissingInRevit), d * Size * 0.02, $"{d} mm strip");
                }
            }
        }

        [TestMethod]
        public void RevitEdgePastEtabs()
        {
            foreach (double d in new[] { 5d, 15d, 25d, 40d })
            {
                List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size) },
                                           Floor(500, Rectangle(0, 0, Size + d, Size), 250));
                Log($"1b Revit edge {d} mm past ETABS (strip {d} x {Size} = {d * Size / 1e6:0.###} m2)", zones);

                if (d < 20)
                {
                    Expect(zones, $"{d} mm", "Matched");
                }
                else
                {
                    Expect(zones, $"{d} mm", "Matched", "MissingInEtabs");
                    Assert.AreEqual(d * Size, Area(zones, SlabZoneState.MissingInEtabs), d * Size * 0.02, $"{d} mm strip");
                }
            }
        }

        #endregion

        #region 2. drop panel offsets

        [TestMethod]
        public void DropPanelShifted()
        {
            foreach (double d in new[] { 10d, 30d })
            {
                // ETABS: 250 slab with a 350 drop over 4250..7750. Revit: the same slab, and the
                // drop as a floor of its own shifted d along X.
                List<SlabZone> zones = Run(
                    new List<Slab_E>
                    {
                        Shell("A1", 250, 0, 0, Size, Size),
                        Shell("D1", 350, 4250, 4250, 7750, 7750, SlabKind.Drop)
                    },
                    Floor(500, Rectangle(0, 0, Size, Size), 250),
                    Floor(501, Rectangle(4250 + d, 4250, 7750 + d, 7750), 350));
                Log($"2 Revit drop shifted {d} mm (two strips {d} x 3500 = {d * 3500 / 1e6:0.###} m2 each)", zones);

                // 10 mm: both strips are under the width and are removed - the drop reads as
                // matching all round. 30 mm: one strip each way, reported as a thickness mismatch.
                if (d < 20)
                {
                    Expect(zones, $"{d} mm", "Matched", "Matched");
                }
                else
                {
                    Expect(zones, $"{d} mm", "Matched", "Matched", "ThicknessMismatch", "ThicknessMismatch");
                    Assert.AreEqual(2 * d * 3500, Area(zones, SlabZoneState.ThicknessMismatch), 2 * d * 3500 * 0.02);
                }
            }
        }

        #endregion

        #region 3. small openings

        [TestMethod]
        public void OpeningOnlyInRevit()
        {
            foreach (double s in new[] { 90d, 150d })
            {
                PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, Size, Size).Outer,
                                                       new[] { Rectangle(6000, 6000, 6000 + s, 6000 + s).Outer });
                List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size) },
                                           Floor(500, floor, 250));
                Log($"3a opening {s} x {s} in Revit only ({s * s / 1e6:0.####} m2)", zones);

                // 90 x 90 is under the smallest piece (0.01 m2) and is removed; 150 x 150 is reported.
                if (s * s < 10000)
                {
                    Expect(zones, $"{s}", "Matched");
                }
                else
                {
                    Expect(zones, $"{s}", "Matched", "OpeningInRevitOnly");
                }
            }
        }

        [TestMethod]
        public void OpeningOnlyInEtabs()
        {
            foreach (double s in new[] { 90d, 150d })
            {
                Slab_E opening = new Slab_E("O1", Corners(6000, 6000, 6000 + s, 6000 + s).Select(Joint).ToList(),
                                            Story, "", SlabKind.Opening, 0, 0, true);
                List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size), opening },
                                           Floor(500, Rectangle(0, 0, Size, Size), 250));
                Log($"3b opening {s} x {s} in ETABS only ({s * s / 1e6:0.####} m2)", zones);

                if (s * s < 10000)
                {
                    Expect(zones, $"{s}", "Matched");
                }
                else
                {
                    Expect(zones, $"{s}", "Matched", "OpeningInEtabsOnly");
                }
            }
        }

        #endregion

        #region 4. how an opening near an edge is classified

        [TestMethod]
        public void OpeningNearAnEdge()
        {
            // Two ETABS shells meeting at x = 6000; a 1000 x 1000 hole in the Revit floor only.
            List<Slab_E> shells = new List<Slab_E>
            {
                Shell("A1", 250, 0, 0, 6000, Size),
                Shell("B1", 250, 6000, 0, Size, Size)
            };

            var cases = new[]
            {
                new { Label = "4a 20 mm from the joint between two shells", MinX = 4980d },
                new { Label = "4b 100 mm from the joint between two shells", MinX = 4900d },
                new { Label = "4c 20 mm from the slab's outer edge", MinX = 20d },
                new { Label = "4d 100 mm from the slab's outer edge", MinX = 100d }
            };

            foreach (var c in cases)
            {
                PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, Size, Size).Outer,
                    new[] { Rectangle(c.MinX, 5000, c.MinX + 1000, 6000).Outer });
                List<SlabZone> zones = Run(shells, Floor(500, floor, 250));
                Log(c.Label + " (1000 x 1000 hole in Revit only)", zones);

                // All four are openings: within 25 mm of an edge would make it an edge, but a
                // piece lying in a hole in the Revit floor is an opening whatever edge it is near.
                Expect(zones, c.Label, "Matched", "Matched", "OpeningInRevitOnly");
                Assert.AreEqual(1e6, Area(zones, SlabZoneState.OpeningInRevitOnly), 1e6 * 0.01, c.Label);
            }
        }

        #endregion

        #region 5. the cases the first four did not reach

        [TestMethod]
        public void TwentyMillimetreOffset()
        {
            List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size) },
                                       Floor(500, Rectangle(0, 0, Size - 20, Size), 250));
            Log("5a Revit edge exactly 20 mm short of ETABS (20 x 12000 = 0.24 m2)", zones);

            // Twice the area over the perimeter of a 20 x 12000 strip is 19.97 mm, under the
            // 20 mm width, so even a 20 mm offset is removed.
            Expect(zones, "20 mm", "Matched");
        }

        [TestMethod]
        public void TaperingEdgeAcrossFourShells()
        {
            // Four ETABS shells stacked along Y, 3000 each. The Revit floor's right edge runs from
            // 0 mm short at the bottom to 40 mm short at the top, so each shell gets a wedge of
            // the mismatch: thinnest at the bottom, under the 20 mm width there.
            List<Slab_E> shells = Enumerable.Range(0, 4)
                .Select(i => Shell("S" + i, 250, 0, i * 3000, Size, (i + 1) * 3000))
                .ToList();
            PlanPolygon floor = PlanPolygon.FromPoints(new[]
            {
                new MyPoint(0, 0, 0), new MyPoint(Size, 0, 0),
                new MyPoint(Size - 40, Size, 0), new MyPoint(0, Size, 0)
            });
            List<SlabZone> zones = Run(shells, Floor(500, floor, 250));
            Log("5b Revit edge tapering 0 -> 40 mm short across four shells (wedge 0.24 m2 in all)", zones);

            // The two thin wedges at the bottom (5 and 15 mm typical width) are under the hold-back
            // sizes, and are kept because the same answer carries on into the wedges above them.
            List<SlabZone> missing = zones.Where(z => z.State == SlabZoneState.MissingInRevit).ToList();
            Assert.AreEqual(4, missing.Count, "one wedge per shell, none removed");
            Assert.AreEqual(2, missing.Count(z => z.Plan.TypicalWidth < 20), "two held back and kept by joining");
            Assert.AreEqual(0.24e6, missing.Sum(z => z.Area), 0.24e6 * 0.02);
        }

        [TestMethod]
        public void GapBetweenRevitFloorsNearAShellEdge()
        {
            // Four Revit floors round a 1000 x 1000 gap at 5000..6000 - a gap in no floor's own
            // outline, so the hole test does not apply and the edge tolerance decides.
            foreach (double joint in new[] { 6020d, 6100d })
            {
                List<Slab_E> shells = new List<Slab_E>
                {
                    Shell("A1", 250, 0, 0, joint, Size),
                    Shell("B1", 250, joint, 0, Size, Size)
                };
                List<SlabZone> zones = Run(shells,
                    Floor(500, Rectangle(0, 0, 5000, Size), 250),
                    Floor(501, Rectangle(6000, 0, Size, Size), 250),
                    Floor(502, Rectangle(5000, 0, 6000, 5000), 250),
                    Floor(503, Rectangle(5000, 6000, 6000, Size), 250));
                Log($"5c gap between Revit floors, {joint - 6000} mm from the joint between two shells", zones);

                // Here the 25 mm edge tolerance decides: 20 mm from the shell edge it is an edge,
                // 100 mm from it an opening.
                SlabZoneState expected = joint - 6000 <= 25 ? SlabZoneState.MissingInRevit : SlabZoneState.OpeningInRevitOnly;
                Assert.AreEqual(1e6, Area(zones, expected), 1e6 * 0.01, $"{joint - 6000} mm");
                Assert.AreEqual(1, zones.Count(z => z.State != SlabZoneState.Matched), $"{joint - 6000} mm");
            }
        }

        [TestMethod]
        public void RevitOnlyPatchNearAndFarFromTheFloorEdge()
        {
            // An ETABS opening 1000 x 1000 over a Revit floor with none: Revit alone has slab
            // there. 20 mm and 100 mm in from the floor's edge.
            foreach (double inset in new[] { 20d, 100d })
            {
                Slab_E opening = new Slab_E("O1", Corners(inset, 5000, inset + 1000, 6000).Select(Joint).ToList(),
                                            Story, "", SlabKind.Opening, 0, 0, true);
                List<SlabZone> zones = Run(new List<Slab_E> { Shell("A1", 250, 0, 0, Size, Size), opening },
                                           Floor(500, Rectangle(0, 0, Size, Size), 250));
                Log($"5d slab in Revit only, {inset} mm in from the floor edge (1 m2)", zones);

                // The same 25 mm, against the Revit floor's own outline.
                Expect(zones, $"{inset} mm", "Matched", inset <= 25 ? "MissingInEtabs" : "OpeningInEtabsOnly");
            }
        }

        #endregion

        #region helpers

        private static List<SlabZone> Run(List<Slab_E> shells, params Slab_R[] floors)
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(shells, floors.ToList(), new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });
            return engine.Zones.ToList();
        }

        /// <summary>The states of the zones, sorted, are exactly these.</summary>
        private static void Expect(List<SlabZone> zones, string label, params string[] states)
        {
            CollectionAssert.AreEqual(states.OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                                      zones.Select(z => z.State.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                                      label + ": " + string.Join(", ", zones.Select(z => $"{z.State} {z.Area / 1e6:0.####}")));
        }

        private static double Area(List<SlabZone> zones, SlabZoneState state)
        {
            return zones.Where(z => z.State == state).Sum(z => z.Area);
        }

        /// <summary>Every zone: what it says, how big, and whether it is under the hold-back sizes.</summary>
        private static void Log(string title, List<SlabZone> zones)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            Console.WriteLine("== " + title);
            foreach (SlabZone zone in zones.OrderBy(z => z.State).ThenBy(z => z.Area))
            {
                bool small = zone.Plan != null && (zone.Area < pref.SlabMinimumZoneArea
                                                   || zone.Plan.TypicalWidth < pref.SlabMinimumZoneWidth);
                Console.WriteLine($"   {zone.State,-20} {zone.EtabsName,-3} area {zone.Area / 1e6,9:0.######} m2  "
                                  + $"width {zone.Plan?.TypicalWidth ?? 0,8:0.0} mm"
                                  + (small ? "  [under the hold-back sizes: was held back, kept by joining]" : "")
                                  + $"  E {zone.EtabsThickness:0} / R {zone.RevitThickness:0}");
            }
        }

        private static Slab_E Shell(string name, double thickness, double minX, double minY, double maxX, double maxY,
                                    SlabKind kind = SlabKind.Slab)
        {
            return new Slab_E(name, Corners(minX, minY, maxX, maxY).Select(Joint).ToList(),
                              Story, "S" + thickness, kind, thickness, thickness, false);
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

        private static Joint Joint(MyPoint point)
        {
            return new Joint("", point, Form1.eLength.mm);
        }

        private static PlanPolygon Rectangle(double minX, double minY, double maxX, double maxY)
        {
            return PlanPolygon.FromPoints(Corners(minX, minY, maxX, maxY));
        }

        private static MyPoint[] Corners(double minX, double minY, double maxX, double maxY)
        {
            return new[]
            {
                new MyPoint(minX, minY, 0),
                new MyPoint(maxX, minY, 0),
                new MyPoint(maxX, maxY, 0),
                new MyPoint(minX, maxY, 0)
            };
        }

        #endregion
    }
}
