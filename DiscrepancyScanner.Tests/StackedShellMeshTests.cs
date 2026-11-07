using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Shells drawn one over another, run through the whole slab check: a drop panel with its
    /// column's stiff area in it, and a load shell laid over part of the slab - the way SAFE is
    /// drawn as a matter of course and ETABS often is.
    ///
    /// Each point of plan belongs to one shell in the thickness check and carries every shell
    /// in the load check. What used to happen instead was that the slab under them came back
    /// whole, laid over the drop and the load shell, so every drop read as the slab's thickness
    /// against the drop's, and the pieces the load check read were not the pieces that were
    /// there. The slab was cut by one boolean against everything over it, and a real floor has
    /// hundreds of things over it; these run with no geometry kernel at all, which is also why
    /// they can run here.
    /// </summary>
    [TestClass]
    public class StackedShellMeshTests
    {
        private const string Story = "GROUND";
        private const double Size = 12000;
        private const double Centre = 6000;
        private const double StiffSide = 797;

        /// <summary>
        /// How far a total may be out, in mm2: the check's own smallest zone. Outlines go into
        /// the triangulation a third of a millimetre either side, so an edge metres long moves a
        /// few thousand mm2 between the two shells beside it and no more.
        /// </summary>
        private const double Tolerance = 10000;

        [TestMethod]
        public void TheSlabTheDropAndTheColumnAreEachCheckedOnce()
        {
            SlabCompareEngine engine = Run(
                new List<Slab_E> { Slab(), Drop(), Stiff() },
                FloorWithColumnHole(500, 0, 0, Size, Size, 250),
                FloorWithColumnHole(501, 4250, 4250, 7750, 7750, 350));

            List<SlabZone> zones = engine.Zones.ToList();

            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));

            Assert.AreEqual(Size * Size - 3500d * 3500d, AreaOf(zones, "A1"), Tolerance,
                            "the slab stops at the drop: " + Describe(zones));
            Assert.AreEqual(3500d * 3500d - StiffSide * StiffSide, AreaOf(zones, "D1"), Tolerance,
                            "the drop stops at the column: " + Describe(zones));
            Assert.AreEqual(0, AreaOf(zones, "K1"), "the column carries no slab verdict: " + Describe(zones));
        }

        [TestMethod]
        public void MeasuredADropHungUnderTheSlabAddsToIt()
        {
            // The 1ST FLOOR drop panels: a 240 PT slab and DP1 drawn under it as a floor of its
            // own, 110 of solid from the slab's underside down. Measured, the thickest alone is
            // the slab's 240; the concrete there is 350, which is what ETABS has.
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            bool fromType = pref.ReadSlabThicknessFromType;
            try
            {
                pref.ReadSlabThicknessFromType = false;

                Slab_R slab = new Slab_R(500, Rectangle(0, 0, Size, Size), 0, -240)
                {
                    Story = Story,
                    Parts = new List<PlanPolygon> { Rectangle(0, 0, Size, Size) },
                    TypeName = "PT SLAB-240"
                };
                Slab_R drop = new Slab_R(501, Rectangle(4250, 4250, 7750, 7750), -240, -350)
                {
                    Story = Story,
                    Parts = new List<PlanPolygon> { Rectangle(4250, 4250, 7750, 7750) },
                    TypeName = "DP1 (3500x3500x350)"
                };
                Slab_E etabsSlab = Shell("A1", "PT24", SlabKind.Slab, 240, 0, 0, Size, Size);
                Slab_E etabsDrop = Shell("D1", "D35", SlabKind.Drop, 350, 4250, 4250, 7750, 7750);

                List<SlabZone> zones = Run(new List<Slab_E> { etabsSlab, etabsDrop }, slab, drop).Zones.ToList();

                Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
                Assert.AreEqual(3500d * 3500d, AreaOf(zones, "D1"), Tolerance, Describe(zones));
            }
            finally
            {
                pref.ReadSlabThicknessFromType = fromType;
            }
        }

        [TestMethod]
        public void AFloorNamedAfterTheStoryAboveIsNotThisStorysSlab()
        {
            // The 1ST FLOOR band: a 600 floor with its top 2.6 m over the plane, within the top
            // face buffer but nearer the story above, which it is named after. It used to be
            // read as this story's slab and, thickest, to stand in for the 250 under it.
            PlanPolygon whole = Rectangle(0, 0, Size, Size);
            Slab_R band = new Slab_R(900, Rectangle(4000, 4000, 8000, 8000), 2600, 2400)
            {
                Story = "UPPER",
                Parts = new List<PlanPolygon> { Rectangle(4000, 4000, 8000, 8000) },
                TypeName = "Thickened band-600",
                StructuralThickness = 600
            };

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(new List<Slab_E> { Slab() }, new List<Slab_R> { Floor(500, whole, 250), band },
                                   new List<Story_E>
                                   {
                                       new Story_E(Story, 0, Form1.eLength.mm),
                                       new Story_E("UPPER", 4500, Form1.eLength.mm)
                                   });

            List<SlabZone> zones = engine.Zones.ToList();
            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
            Assert.IsFalse(zones.Any(zone => zone.RevitId == 900), "the band is the floor above's: " + Describe(zones));
        }

        [TestMethod]
        public void ALongDropOutranksASmallerSlabShellItCrosses()
        {
            // A drop strip 12 m x 2 m (24 m2) crosses a 350 slab shell 4 m x 4 m (16 m2). The
            // shell is the smaller of the two, and the drop is still the depth where they meet.
            Slab_E strip = Shell("D2", "D50", SlabKind.Drop, 500, 0, 5000, Size, 7000);
            Slab_E shell = Shell("S2", "CIPS35", SlabKind.Slab, 350, 4000, 4000, 8000, 8000);

            SlabCompareEngine engine = Run(new List<Slab_E> { Slab(), shell, strip },
                Floor(500, Rectangle(0, 0, Size, Size), 250));

            List<SlabZone> zones = engine.Zones.ToList();
            Assert.AreEqual(Size * 2000d, AreaOf(zones, "D2"), Tolerance, "the drop keeps all of itself: " + Describe(zones));
            Assert.AreEqual(4000d * 4000d - 4000d * 2000d, AreaOf(zones, "S2"), Tolerance, Describe(zones));
        }

        [TestMethod]
        public void ALoadShellOverTheSlabIsItsOwnPieceAndCarriesBoth()
        {
            // The slab carries SIDL 0.5 and Live 3.5 everywhere, and a load shell over its left
            // half carries SIDL 0.5 and Live 20 more. The loading plan asks for the sums.
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });

            Slab_E loadShell = Shell("L1", "S250", SlabKind.Slab, 250, 0, 0, Size / 2, Size);
            loadShell.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 20) });

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "PLANT", new LoadSet { Sdl = 1.0, Ll = 23.5 }, "LL"));
            schedule.Add(new LoadScheduleEntry("2", "OFFICE", new LoadSet { Sdl = 0.5, Ll = 3.5 }, "LL"));

            LoadCoverage plan = LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size / 2, Size) }, "1"),
                new LoadRegion_R(2, new[] { Rectangle(Size / 2, 0, Size, Size) }, "2")
            }, schedule, new List<string>());

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, plan);
            engine.StartComparison(new List<Slab_E> { slab, loadShell },
                                   new List<Slab_R> { Floor(500, Rectangle(0, 0, Size, Size), 250) },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            List<SlabZone> zones = engine.Zones.ToList();
            Assert.AreEqual(Size * Size, zones.Sum(zone => zone.Area), Tolerance,
                            "no piece of plan is checked twice: " + Describe(zones));
            Assert.AreEqual(Size * Size / 2, AreaOf(zones, "A1"), Tolerance, Describe(zones));
            Assert.AreEqual(Size * Size / 2, AreaOf(zones, "L1"), Tolerance, Describe(zones));

            List<SlabZone> loads = engine.LoadZones.ToList();
            Assert.AreEqual(Size * Size, loads.Sum(zone => zone.Area), Tolerance,
                            "no piece of plan is load checked twice: " + Describe(loads));
            Assert.IsTrue(loads.All(zone => zone.LoadState == SlabLoadState.Matched),
                          "each half carries what the plan asks, the left half both shells' loads: " +
                          string.Join(", ", loads.Select(zone => $"{zone.EtabsName}:{zone.LoadState} {zone.EtabsLoads}")));
        }

        [TestMethod]
        public void ALoadPieceSaysWhatThePlanAskedAndWhichShellsItAdded()
        {
            // The piece under the load shell is answered by the loading plan: it names the load
            // asked for, the region that asked, and the shells whose loads were added - and no
            // Revit floor, which the load cut is never asked about. It used to name whichever
            // floor lay under one corner of it, a drop panel on a piece hundreds of m2 across.
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });
            Slab_E loadShell = Shell("L1", "S250", SlabKind.Slab, 250, 0, 0, Size / 2, Size);
            loadShell.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 20) });

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("7", "PLANT", new LoadSet { Sdl = 1.0, Ll = 23.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(9001, new[] { Rectangle(0, 0, Size, Size) }, "7")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { slab, loadShell },
                                   new List<Slab_R>
                                   {
                                       Floor(500, Rectangle(0, 0, Size, Size), 250),
                                       Floor(501, Rectangle(1000, 1000, 3000, 3000), 500)
                                   },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            SlabZone piece = engine.LoadZones.Single(zone => zone.EtabsName == "L1");
            Dictionary<string, string> facts = piece.ResultFields
                .Where(field => field.Kind == CheckKind.None)
                .ToDictionary(field => field.Key, field => field.Value);

            Assert.AreEqual(SlabLoadState.Matched, piece.LoadState, "SIDL 0.5 + 0.5 and Live 3.5 + 20");
            Assert.AreEqual("L1 + A1", facts[SlabResultKeys.LoadShells]);
            Assert.AreEqual("load 7 - PLANT", facts[SlabResultKeys.LoadPlan]);
            Assert.AreEqual("9001", facts[SlabResultKeys.LoadRegion]);

            Assert.AreEqual(0, piece.RevitId, "no Revit floor on a piece of the load cut");
            Assert.IsFalse(facts.ContainsKey(SlabResultKeys.RevitType), "no Revit floor type");
            Assert.IsFalse(facts.ContainsKey(SlabResultKeys.State), "no slab check on the load cut");

            DiscrepancyScanner.Wpf.ElementViewModel element =
                DiscrepancyScanner.Wpf.ElementViewModelFactory.FromSlabZone(piece);
            Assert.AreEqual(9001, element.RevitId, "SELECT IN REVIT picks the loading region");
            StringAssert.Contains(element.PairedWith, "Loading region 9001");
        }

        [TestMethod]
        public void ADropAcrossTwoSlabsAddsWhicheverSlabIsUnderEachPartOfIt()
        {
            // Loads add, whichever shell is on top. The drop straddles the line between two
            // slabs carrying different loads, so its left half carries the left slab's load
            // plus its own and its right half the right slab's plus its own - one piece read
            // from one point would give one of the halves the other half's sum.
            Slab_E left = Shell("A1", "S250", SlabKind.Slab, 250, 0, 0, Size / 2, Size);
            left.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });

            Slab_E right = Shell("A2", "S250", SlabKind.Slab, 250, Size / 2, 0, Size, Size);
            right.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 20) });

            Slab_E drop = Drop();
            drop.SetLoads(new List<AreaLoad_E> { Load("SIDL", 1.0) });

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "LEFT", new LoadSet { Sdl = 0.5, Ll = 3.5 }, "LL"));
            schedule.Add(new LoadScheduleEntry("2", "RIGHT", new LoadSet { Sdl = 0.5, Ll = 20 }, "LL"));
            schedule.Add(new LoadScheduleEntry("3", "LEFT DROP", new LoadSet { Sdl = 1.5, Ll = 3.5 }, "LL"));
            schedule.Add(new LoadScheduleEntry("4", "RIGHT DROP", new LoadSet { Sdl = 1.5, Ll = 20 }, "LL"));

            PlanPolygon dropPlan = Rectangle(4250, 4250, 7750, 7750);
            LoadCoverage plan = LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { PlanPolygon.Create(Rectangle(0, 0, Size / 2, Size).Outer,
                                                               new[] { Rectangle(4250, 4250, Size / 2, 7750).Outer }) }, "1"),
                new LoadRegion_R(2, new[] { PlanPolygon.Create(Rectangle(Size / 2, 0, Size, Size).Outer,
                                                               new[] { Rectangle(Size / 2, 4250, 7750, 7750).Outer }) }, "2"),
                new LoadRegion_R(3, new[] { Rectangle(4250, 4250, Size / 2, 7750) }, "3"),
                new LoadRegion_R(4, new[] { Rectangle(Size / 2, 4250, 7750, 7750) }, "4")
            }, schedule, new List<string>());

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, plan);
            engine.StartComparison(new List<Slab_E> { left, right, drop },
                                   new List<Slab_R> { Floor(500, Rectangle(0, 0, Size, Size), 250) },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            List<SlabZone> loads = engine.LoadZones.ToList();
            Assert.AreEqual(Size * Size, loads.Sum(zone => zone.Area), Tolerance, Describe(loads));
            Assert.IsTrue(loads.All(zone => zone.LoadState == SlabLoadState.Matched),
                          "every piece carries the sum of the shells over it: " +
                          string.Join(", ", loads.Select(zone => $"{zone.EtabsName}:{zone.LoadState} {zone.EtabsLoads}")));
        }

        [TestMethod]
        public void TheLoadMeshFollowsTheSlabsUnderADropEvenWithNoPlanEdgeThere()
        {
            // The same drop across the same two slabs, but one loading region over the whole
            // drop, so nothing on the plan cuts it. It still has to come apart where the slab
            // under it changes: the two halves carry different sums.
            Slab_E left = Shell("A1", "S250", SlabKind.Slab, 250, 0, 0, Size / 2, Size);
            left.SetLoads(new List<AreaLoad_E> { Load("Live", 3.5) });
            Slab_E right = Shell("A2", "S250", SlabKind.Slab, 250, Size / 2, 0, Size, Size);
            right.SetLoads(new List<AreaLoad_E> { Load("Live", 20) });

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "ALL", new LoadSet { Ll = 3.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, Size) }, "1")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { left, right, Drop() },
                                   new List<Slab_R> { Floor(500, Rectangle(0, 0, Size, Size), 250) },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            List<SlabZone> drop = engine.LoadZones.Where(zone => zone.EtabsName == "D1").ToList();

            Assert.AreEqual(3500d * 3500d / 2, drop.Where(zone => zone.LoadState == SlabLoadState.Matched).Sum(zone => zone.Area),
                            Tolerance, "the half over the left slab carries 3.5: " + Describe(drop));
            Assert.AreEqual(3500d * 3500d / 2, drop.Where(zone => zone.LoadState == SlabLoadState.Mismatch).Sum(zone => zone.Area),
                            Tolerance, "the half over the right slab carries 20: " + Describe(drop));
        }

        [TestMethod]
        public void AnOpeningOverStackedShellsStaysAHoleInBoth()
        {
            // Two shells stacked over a hole the Revit floor has too: nothing to report.
            Slab_E opening = new Slab_E("O1", Corners(1000, 1000, 3000, 3000).Select(Joint).ToList(),
                                        Story, "", SlabKind.Opening, 0, 0, true);
            Slab_E loadShell = Shell("L1", "S250", SlabKind.Slab, 250, 0, 0, Size / 2, Size);

            PlanPolygon floor = PlanPolygon.Create(Rectangle(0, 0, Size, Size).Outer,
                                                   new[] { Rectangle(1000, 1000, 3000, 3000).Outer });

            SlabCompareEngine engine = Run(new List<Slab_E> { Slab(), loadShell, opening },
                                           Floor(500, floor, 250));

            List<SlabZone> zones = engine.Zones.ToList();
            Assert.IsTrue(zones.All(zone => zone.State == SlabZoneState.Matched), Describe(zones));
            Assert.AreEqual(Size * Size - 2000d * 2000d, zones.Sum(zone => zone.Area), Tolerance, Describe(zones));
        }

        [TestMethod]
        public void ANullLoadAreaOverTheSlabAddsItsLoadWhereItIs()
        {
            // The way the podium was drawn: the slab carries the parking load everywhere, and
            // a None area over its left half carries the paving's extra SIDL 3. The plan asks
            // for paving on the left and parking on the right. The None area used to be left
            // out of the import as not being a floor, and the whole left half read as parking.
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });
            Slab_E paving = LoadArea("N1", 0, 0, Size / 2, Size, Load("SIDL", 3.0));

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "PARKING", new LoadSet { Sdl = 0.5, Ll = 3.5 }, "LL"));
            schedule.Add(new LoadScheduleEntry("7", "PAVING", new LoadSet { Sdl = 3.5, Ll = 3.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(7, new[] { Rectangle(0, 0, Size / 2, Size) }, "7"),
                new LoadRegion_R(1, new[] { Rectangle(Size / 2, 0, Size, Size) }, "1")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { slab, paving },
                                   new List<Slab_R> { Floor(500, Rectangle(0, 0, Size, Size), 250) },
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            SlabZone thickness = engine.Zones.Single();
            Assert.AreEqual("A1", thickness.EtabsName, "the load area is no slab: the thickness check never sees it");
            Assert.IsTrue(thickness.IsWholeEtabsArea, "and it does not cut the slab it lies on");
            Assert.AreEqual(SlabZoneState.Matched, thickness.State);

            List<SlabZone> loads = engine.LoadZones.ToList();
            Assert.AreEqual(Size * Size, loads.Sum(zone => zone.Area), Tolerance, Describe(loads));
            Assert.IsTrue(loads.All(zone => zone.EtabsName == "A1"), "every piece is a piece of the slab: " + Describe(loads));
            Assert.IsTrue(loads.All(zone => zone.LoadState == SlabLoadState.Matched),
                          "the left half carries the slab's load and the paving's, the right half the slab's: " +
                          string.Join(", ", loads.Select(zone => $"{zone.LoadNumber}:{zone.LoadState} {zone.EtabsLoads}")));
            Assert.AreEqual(Size * Size / 2,
                            loads.Where(zone => zone.LoadShells.Contains("N1")).Sum(zone => zone.Area), Tolerance,
                            "the pieces under the load area name it among the shells added");

            DiscrepancyScanner.Wpf.ElementViewModel raw = DiscrepancyScanner.Wpf.RawElements.EtabsLoadArea(paving);
            Assert.AreEqual(DiscrepancyScanner.Wpf.ViewMode.SlabLoads, raw.Tab, "raw data shows it under the slab load tab alone");
        }

        [TestMethod]
        public void ANullLoadAreaCutsTheSlabWithNoPlanEdgeUnderIt()
        {
            // One region over the whole floor asking for the slab's own load. Nothing on the
            // plan runs along the load area, and the piece under it still has to come apart
            // from the rest, because it carries more.
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });
            Slab_E plant = LoadArea("N1", 3000, 3000, 7000, 9000, Load("SIDL", 6.0), Load("Live", 6.5));

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "PARKING", new LoadSet { Sdl = 0.5, Ll = 3.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story, LoadsOnly = true };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, Size) }, "1")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { slab, plant }, new List<Slab_R>(),
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            List<SlabZone> loads = engine.LoadZones.ToList();
            Assert.AreEqual(4000d * 6000d, loads.Where(zone => zone.LoadState == SlabLoadState.Mismatch).Sum(zone => zone.Area),
                            Tolerance, "the piece under the plant carries SIDL 6.5 and Live 10: " + Describe(loads));
            Assert.AreEqual(Size * Size - 4000d * 6000d,
                            loads.Where(zone => zone.LoadState == SlabLoadState.Matched).Sum(zone => zone.Area),
                            Tolerance, "the rest carries the slab's own: " + Describe(loads));

            SlabZone underPlant = loads.First(zone => zone.LoadState == SlabLoadState.Mismatch);
            Assert.AreEqual(6.5, underPlant.EtabsLoads.Sdl, 1e-9);
            Assert.AreEqual(10.0, underPlant.EtabsLoads.Ll, 1e-9);
        }

        [TestMethod]
        public void ManyLoadAreasOverlappingEachOtherAndADropAllAdd()
        {
            // Three None areas overlapping one another over the slab, and a drop panel in the
            // middle of them: five shells stand over the middle of the drop. Every piece has to
            // come out carrying the sum of exactly the shells over it, and no piece may straddle
            // an outline, whichever shell drew it.
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.5), Load("Live", 3.5) });
            Slab_E drop = Drop();
            drop.SetLoads(new List<AreaLoad_E> { Load("SIDL", 0.25) });
            Slab_E n1 = LoadArea("N1", 0, 0, 8000, Size, Load("SIDL", 3.0));
            Slab_E n2 = LoadArea("N2", 4000, 0, Size, Size, Load("Live", 1.5));
            Slab_E n3 = LoadArea("N3", 2000, 3000, 10000, 6000, Load("SIDL", 1.0));

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "ANY", new LoadSet { Sdl = 0.5, Ll = 3.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story, LoadsOnly = true };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, Size) }, "1")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { slab, drop, n1, n2, n3 }, new List<Slab_R>(),
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            // Four times the usual allowance: some sixty metres of outline cross this slab, and
            // each gives up its third-of-a-millimetre strip.
            List<SlabZone> loads = engine.LoadZones.ToList();
            Assert.AreEqual(Size * Size, loads.Sum(zone => zone.Area), 4 * Tolerance, "no plan twice, none missed: " + Describe(loads));

            Func<double, double, double, double, double, double, bool> inside =
                (x, y, minX, minY, maxX, maxY) => x > minX && x < maxX && y > minY && y < maxY;

            foreach (SlabZone zone in loads)
            {
                // Every corner of every piece reads the same stack as its middle, give or take
                // the third of a millimetre the outlines go in either side.
                MyPoint middle = zone.Plan.InteriorPoint();
                foreach (MyPoint at in new[] { middle }.Concat(zone.Plan.Outer.Points))
                {
                    MyPoint probe = new MyPoint(at.X + (middle.X - at.X) * 0.01, at.Y + (middle.Y - at.Y) * 0.01, 0);
                    if (!zone.Plan.Contains(probe.X, probe.Y))
                    {
                        // A corner of a piece that bends round a drop: the step towards the
                        // middle goes across the notch rather than into the piece.
                        continue;
                    }

                    double sdl = 0.5 + (inside(probe.X, probe.Y, 0, 0, 8000, Size) ? 3.0 : 0)
                                     + (inside(probe.X, probe.Y, 2000, 3000, 10000, 6000) ? 1.0 : 0)
                                     + (inside(probe.X, probe.Y, 4250, 4250, 7750, 7750) ? 0.25 : 0);
                    double live = 3.5 + (inside(probe.X, probe.Y, 4000, 0, Size, Size) ? 1.5 : 0);

                    Assert.AreEqual(sdl, zone.EtabsLoads.Sdl, 1e-9, $"SIDL at {probe.X:0},{probe.Y:0} of " + Describe(new[] { zone }));
                    Assert.AreEqual(live, zone.EtabsLoads.Ll, 1e-9, $"Live at {probe.X:0},{probe.Y:0} of " + Describe(new[] { zone }));
                }
            }

            Assert.AreEqual(3500d * 1750d,
                            loads.Where(zone => zone.LoadShells.Count == 5).Sum(zone => zone.Area), Tolerance,
                            "the drop where all three load areas cross it has five shells over it: " + Describe(loads));
        }

        [TestMethod]
        public void ALoadAreaOverNoSlabIsSaidRatherThanLost()
        {
            Slab_E slab = Slab();
            slab.SetLoads(new List<AreaLoad_E> { Load("Live", 3.5) });
            Slab_E away = LoadArea("N9", Size + 1000, 0, Size + 5000, 4000, Load("SIDL", 3.0));

            LoadSchedule schedule = new LoadSchedule("test.xlsx", "Area Load");
            schedule.Add(new LoadScheduleEntry("1", "PARKING", new LoadSet { Ll = 3.5 }, "LL"));

            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story, LoadsOnly = true };
            engine.SetLoads(Story, LoadCoverage.Build(new List<LoadRegion_R>
            {
                new LoadRegion_R(1, new[] { Rectangle(0, 0, Size, Size) }, "1")
            }, schedule, new List<string>()));
            engine.StartComparison(new List<Slab_E> { slab, away }, new List<Slab_R>(),
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            Assert.IsTrue(engine.Issues.Any(issue => issue.Contains("no slab is drawn under them") && issue.Contains("N9")),
                          string.Join(" | ", engine.Issues));
            Assert.IsTrue(engine.LoadZones.All(zone => zone.LoadState == SlabLoadState.Matched), Describe(engine.LoadZones));
        }

        [TestMethod]
        public void AStiffAreaOverNothingButALoadAreaIsNotStacked()
        {
            Slab_E underIt = LoadArea("N1", 0, 0, Size, Size, Load("SIDL", 1.0));
            Assert.AreEqual(0, SlabCompareEngine.StackedStiffAreas(new List<Slab_E> { underIt, Stiff() }).Count,
                            "a load area is no slab for a column to stand in");
        }

        [TestMethod]
        public void ANullAreaIsTheNoneProperty()
        {
            Assert.IsTrue(ModelIndex.IsNullArea("None"));
            Assert.IsTrue(ModelIndex.IsNullArea(" NONE "));
            Assert.IsTrue(ModelIndex.IsNullArea(""), "SAFE can hand one back with no property name");
            Assert.IsTrue(ModelIndex.IsNullArea(null));
            Assert.IsFalse(ModelIndex.IsNullArea("S250-C50"));
        }

        [TestMethod]
        public void AShellNothingOverlapsIsReportedAsDrawn()
        {
            SlabCompareEngine engine = Run(new List<Slab_E> { Slab() },
                                           Floor(500, Rectangle(0, 0, Size, Size), 250));

            SlabZone only = engine.Zones.Single();
            Assert.IsTrue(only.IsWholeEtabsArea, "nothing over it, so it goes through untouched");
            Assert.AreEqual(SlabZoneState.Matched, only.State);
        }

        #region the model

        private static SlabCompareEngine Run(List<Slab_E> shells, params Slab_R[] floors)
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(shells, floors.ToList(),
                                   new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });
            return engine;
        }

        private static double AreaOf(IEnumerable<SlabZone> zones, string etabsName)
        {
            return zones.Where(zone => zone.EtabsName == etabsName).Sum(zone => zone.Area);
        }

        private static Slab_E Slab()
        {
            return Shell("A1", "S250", SlabKind.Slab, 250, 0, 0, Size, Size);
        }

        private static Slab_E Drop()
        {
            return Shell("D1", "DP350", SlabKind.Drop, 350, 4250, 4250, 7750, 7750);
        }

        private static Slab_E Stiff()
        {
            double half = StiffSide / 2;
            return Shell("K1", "STIFF", SlabKind.Stiff, 1000,
                         Centre - half, Centre - half, Centre + half, Centre + half);
        }

        private static Slab_E Shell(string name, string property, SlabKind kind, double thickness,
                                    double minX, double minY, double maxX, double maxY)
        {
            return new Slab_E(name, Corners(minX, minY, maxX, maxY).Select(Joint).ToList(),
                              Story, property, kind, thickness, thickness, false);
        }

        /// <summary>A None area carrying the loads given: no slab, only load laid over one.</summary>
        private static Slab_E LoadArea(string name, double minX, double minY, double maxX, double maxY,
                                       params AreaLoad_E[] loads)
        {
            Slab_E area = new Slab_E(name, Corners(minX, minY, maxX, maxY).Select(Joint).ToList(),
                                     Story, "None", SlabKind.LoadOnly, 0, 0, false);
            area.SetLoads(loads);
            return area;
        }

        private static AreaLoad_E Load(string pattern, double value)
        {
            return new AreaLoad_E(pattern, value, 10, true, false);
        }

        private static Joint Joint(MyPoint point)
        {
            return new Joint("", point, Form1.eLength.mm);
        }

        private static Slab_R FloorWithColumnHole(long revitId, double minX, double minY,
                                                  double maxX, double maxY, double thickness)
        {
            PlanRing hole = PlanRing.FromPoints(Enumerable.Range(0, 64).Select(i =>
            {
                double angle = 2 * Math.PI * i / 64;
                return new MyPoint(Centre + 450 * Math.Cos(angle), Centre + 450 * Math.Sin(angle), 0);
            }));

            return Floor(revitId, PlanPolygon.Create(Rectangle(minX, minY, maxX, maxY).Outer, new[] { hole }),
                         thickness);
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

        private static string Describe(IEnumerable<SlabZone> zones)
        {
            return string.Join(", ", zones.Select(z =>
                $"{z.EtabsName}{(z.RevitId > 0 ? "/R" + z.RevitId : "")}:{z.State}={z.Area / 1e6:0.###} m2 " +
                $"[{z.Plan.Bounds.MinX:0}..{z.Plan.Bounds.MaxX:0} x {z.Plan.Bounds.MinY:0}..{z.Plan.Bounds.MaxY:0}, " +
                $"{z.Plan.Holes.Count} hole(s) {string.Join("/", z.Plan.Holes.Select(h => (h.Area / 1e6).ToString("0.###")))}]"));
        }

        #endregion
    }
}
