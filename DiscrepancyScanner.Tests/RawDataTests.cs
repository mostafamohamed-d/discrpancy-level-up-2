using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// What the plan draws while Raw data is on: each model's elements as they were read,
    /// with nothing paired and nothing judged, drawn in the model's colour and labelled with
    /// what was read - and where those labels go, turned along the frames they belong to.
    /// </summary>
    [TestClass]
    public class RawDataTests
    {
        private const double FeetToMM = 304.8;
        private const string Story = "L03";

        #region nothing judged

        /// <summary>
        /// A raw element carries no results, so there is nothing for the tab counts, the
        /// findings list, a review or an acceptance to be about - and nothing on it is paired,
        /// so nothing on it can be paired by hand either.
        /// </summary>
        [TestMethod]
        public void RawDataCarriesNoResultsAndCannotBeFiledOrPaired()
        {
            foreach (ElementViewModel element in new[] { RawEtabsBeam(), RawRevitBeam() })
            {
                Assert.IsTrue(element.IsRaw);
                Assert.AreEqual(0, element.Outcomes.Count);
                Assert.AreEqual(CheckState.NotChecked, element.State);
                Assert.IsFalse(element.CanBeFiled);
                Assert.IsFalse(PairingEditor.CanEdit(element));
                Assert.IsFalse(PairingEditor.CanBeMarked(element));
                StringAssert.Contains(element.PairedWith, "Raw data");
            }

            Assert.AreEqual(4101, RawRevitBeam().RevitId, "a Revit element can still be selected in Revit");
        }

        /// <summary>A raw element counts on no tab and on no level, whatever state it is left in.</summary>
        [TestMethod]
        public void RawDataIsNotCounted()
        {
            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new[] { new Story_E(Story, 10000, Form1.eLength.mm) });

            tally.Update(new[] { RawEtabsBeam(), RawRevitBeam() }, Story);

            Assert.IsTrue(tally.Tabs.All(tab => tab.Open == 0));
            Assert.IsTrue(tally.Levels.All(level => !level.HasFindings));
        }

        /// <summary>
        /// Drawn in the colour of the model it came from - the colours the key gives to what
        /// only one model has - and never greyed out for a result switched off, because it has
        /// none. A tab switched off still sends it behind the rest in grey.
        /// </summary>
        [TestMethod]
        public void RawDataIsDrawnInItsModelsColour()
        {
            ElementViewModel etabs = RawEtabsBeam();
            ElementViewModel revit = RawRevitBeam();

            Assert.AreEqual(VerdictColours.ColorOf(VerdictColour.EtabsOnly), ColourOf(etabs));
            Assert.AreEqual(VerdictColours.ColorOf(VerdictColour.RevitOnly), ColourOf(revit));

            etabs.IsMuted = true;
            Assert.AreEqual(VerdictColours.ColorOf(VerdictColour.EtabsOnly), ColourOf(etabs),
                            "there is no result to grey it out for");

            revit.IsBackdrop = true;
            Assert.AreSame(ViewPalette.Context, ViewPalette.ForState(revit), "its tab is off");
        }

        #endregion

        #region what each model read

        /// <summary>
        /// A beam's size is "Size" whichever model it came from, so ticking it once labels both
        /// sides - the whole point of reading the two against each other. What only one model
        /// has, the ETABS label or the Revit mark, is offered as well.
        /// </summary>
        [TestMethod]
        public void BothModelsPublishTheSameValueUnderTheSameKey()
        {
            ElementViewModel etabs = RawEtabsBeam();
            ElementViewModel revit = RawRevitBeam();

            Assert.AreEqual("300 x 600", etabs.GetFieldValue(RawResultKeys.BeamSize));
            Assert.AreEqual("250 x 700", revit.GetFieldValue(RawResultKeys.BeamSize));
            Assert.AreEqual("6", etabs.GetFieldValue(RawResultKeys.BeamLength));
            Assert.AreEqual("3000", etabs.GetFieldValue(RawResultKeys.BeamLevels), "level ends read as one level");

            IList<LabelFieldGroup> groups = LabelGrouping.Build(new[] { etabs, revit },
                kind => kind.ToString(), key => false, () => { });

            Assert.AreEqual(1, groups.Count);
            List<string> offered = groups[0].Options.Select(option => option.Label).ToList();
            Assert.AreEqual(1, offered.Count(label => label == "Size (w x d)"), string.Join(" | ", offered));
            CollectionAssert.Contains(offered, "ETABS label");
            CollectionAssert.Contains(offered, "Beam mark");
        }

        [TestMethod]
        public void AColumnIsReadWithItsSizeFromBothModels()
        {
            ElementViewModel etabs = RawElements.EtabsColumn(EtabsColumn());
            ElementViewModel revit = RawElements.RevitColumn(RevitColumn());

            Assert.AreEqual("400 x 400", etabs.GetFieldValue(RawResultKeys.ColumnSize));
            Assert.AreEqual("400 x 400", revit.GetFieldValue(RawResultKeys.ColumnSize));
            Assert.AreEqual(ElementSource.EtabsOnly, etabs.Source);
            Assert.AreEqual(ElementSource.RevitOnly, revit.Source);
        }

        /// <summary>
        /// Panels meshed up a wall lie on top of each other in plan, so they are folded into the
        /// one wall they are, as the check folds them - their labels would otherwise be printed
        /// over each other - and the fold says how many it holds.
        /// </summary>
        [TestMethod]
        public void TheEtabsPanelsOfOneWallAreOneWall()
        {
            ElementViewModel wall = RawElements.EtabsWall(new List<Wall_E>
            {
                EtabsPanel("W1", 0), EtabsPanel("W2", 3000)
            });

            Assert.AreEqual("W1, W2", wall.GetFieldValue(RawResultKeys.WallName));
            Assert.AreEqual("2", wall.GetFieldValue(RawResultKeys.WallPanels));
            Assert.AreEqual("200", wall.GetFieldValue(RawResultKeys.WallThickness));
        }

        /// <summary>
        /// An ETABS area's loads are what ETABS holds, one per pattern - before any of it is
        /// added up into the four loads the check compares - and it is drawn under both slab
        /// tabs, where the Revit floors belong to one and the loading plan to the other.
        /// </summary>
        [TestMethod]
        public void SlabsAndTheirLoadsGoUnderTheirOwnTabs()
        {
            Slab_E area = EtabsArea();
            area.SetLoads(new[]
            {
                new AreaLoad_E("SDL", 1.5, 10, true, false),
                new AreaLoad_E("LL", 2, 10, true, false)
            });

            ElementViewModel etabs = RawElements.EtabsSlab(area);
            Assert.AreEqual("SDL 1.5, LL 2", etabs.GetFieldValue(RawResultKeys.SlabLoads));
            Assert.AreEqual("36", etabs.GetFieldValue(RawResultKeys.SlabArea));
            Assert.IsNull(etabs.Tab, "an ETABS area is on both slab tabs");

            ElementViewModel floor = RawElements.RevitSlab(RevitFloor());
            Assert.AreEqual(ViewMode.Slabs, floor.Tab);

            LoadCoverage.Region region = new LoadCoverage.Region
            {
                Plan = Square(6000),
                RevitId = 77,
                Number = "3",
                NumberText = "3",
                InSchedule = true,
                Loads = new LoadSet { Sdl = 1.5, Ll = 5 },
                Description = "OFFICE"
            };

            ElementViewModel loadingPlan = RawElements.LoadRegion(region, Story);
            Assert.AreEqual(ViewMode.SlabLoads, loadingPlan.Tab);
            Assert.AreEqual("3", loadingPlan.GetFieldValue(RawResultKeys.SlabLoadNumber));
            StringAssert.Contains(loadingPlan.GetFieldValue(RawResultKeys.SlabLoads), "LL 5");
            Assert.AreEqual(ElementSource.RevitOnly, loadingPlan.Source);
            Assert.AreEqual(Story, loadingPlan.Story);
            Assert.AreEqual("Slab", CheckNaming.KindOf(loadingPlan), "a whole area, not a zone the check cut");
        }

        /// <summary>A loading region whose number the schedule does not have says so rather than showing nothing.</summary>
        [TestMethod]
        public void ALoadingRegionNotInTheScheduleSaysSo()
        {
            LoadCoverage.Region region = new LoadCoverage.Region
            {
                Plan = Square(6000),
                RevitId = 78,
                Number = "12",
                NumberText = "12",
                InSchedule = false
            };

            Assert.AreEqual("not in the schedule",
                            RawElements.LoadRegion(region, Story).GetFieldValue(RawResultKeys.SlabLoads));
        }

        #endregion

        #region labels along the frames

        /// <summary>
        /// A label on a frame runs along it and sits beside it: the ETABS one on one side and the
        /// Revit one on the other, so the two models' labels on the same line do not cover each
        /// other.
        /// </summary>
        [TestMethod]
        public void TheTwoModelsLabelsSitOnEitherSideOfTheLine()
        {
            double angle, top;

            ModelCanvas.PlaceLabel(RawEtabsBeam(), Camera(), 13, out angle, out top);
            Assert.AreEqual(0, angle, 1e-9, "a level beam, a level label");
            Assert.IsTrue(top + 13 <= 0, "the ETABS label is wholly above the line");

            ModelCanvas.PlaceLabel(RawRevitBeam(), Camera(), 13, out angle, out top);
            Assert.AreEqual(0, angle, 1e-9);
            Assert.IsTrue(top >= 0, "the Revit label is wholly below it");
        }

        /// <summary>
        /// Turned to the frame, but never upside down: a beam drawn from right to left reads the
        /// same as one drawn from left to right, and one straight up the screen reads upwards.
        /// </summary>
        [TestMethod]
        public void ALabelIsTurnedAlongItsFrameButNeverUpsideDown()
        {
            Assert.AreEqual(0, AngleOf(Beam(6000, 0, 0, 0)), 1e-9, "drawn right to left");
            Assert.AreEqual(-90, AngleOf(Beam(0, 0, 0, 6000)), 1e-9, "straight up the plan reads upwards");
            Assert.AreEqual(-90, AngleOf(Beam(0, 6000, 0, 0)), 1e-9, "and so does the same beam drawn downwards");
            Assert.AreEqual(-45, AngleOf(Beam(0, 0, 6000, 6000)), 1e-9);
            Assert.AreEqual(45, AngleOf(Beam(6000, 0, 0, 6000)), 1e-9, "down to the right on screen");
        }

        /// <summary>A wall is drawn at its thickness, so its label clears the whole of it.</summary>
        [TestMethod]
        public void AWallsLabelClearsItsThickness()
        {
            ElementViewModel wall = RawElements.EtabsWall(new List<Wall_E> { EtabsPanel("W1", 0) });
            Assert.AreEqual(100, wall.LabelClearance, 1e-9, "half of 200");

            double angle, top;
            ModelCanvas.PlaceLabel(wall, Camera(), 13, out angle, out top);

            // A tenth of a pixel to the millimetre: 100 mm is 10 pixels, and the gap is on top.
            Assert.IsTrue(top + 13 <= -10, "clear of the wall's own edge: " + top);
        }

        /// <summary>
        /// What is not a line is labelled over its middle, or just off it on the side of the one
        /// model it came from.
        /// </summary>
        [TestMethod]
        public void AColumnIsLabelledOverItsMiddle()
        {
            double angle, top;

            ModelCanvas.PlaceLabel(RawElements.EtabsColumn(EtabsColumn()), Camera(), 13, out angle, out top);
            Assert.AreEqual(0, angle);
            Assert.IsTrue(top + 13 <= 0, "ETABS just above");

            ModelCanvas.PlaceLabel(RawElements.RevitColumn(RevitColumn()), Camera(), 13, out angle, out top);
            Assert.IsTrue(top >= 0, "Revit just below");
        }

        /// <summary>
        /// A line load's label goes half way along it by length. It used to go on the middle
        /// point by count, which on a straight line of two points was its far end.
        /// </summary>
        [TestMethod]
        public void ALineLoadIsLabelledHalfWayAlongIt()
        {
            LineLoadElementViewModel straight = new LineLoadElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked, "1", Story, new List<ResultField>(),
                new[] { new MyPoint(0, 0, 0), new MyPoint(4000, 0, 0) });

            Assert.AreEqual(new Point(2000, 0), straight.LabelAnchor);

            // 3 m along and 1 m up: half way is 2 m along the first leg, and the label runs along it.
            LineLoadElementViewModel bent = new LineLoadElementViewModel(
                ElementSource.RevitOnly, CheckState.NotChecked, "2", Story, new List<ResultField>(),
                new[] { new MyPoint(0, 0, 0), new MyPoint(3000, 0, 0), new MyPoint(3000, 1000, 0) });

            Assert.AreEqual(2000, bent.LabelAnchor.X, 1e-6);
            Assert.AreEqual(0, bent.LabelAnchor.Y, 1e-6);
            Assert.AreEqual(new Vector(3000, 0), bent.LabelDirection.Value);
        }

        #endregion

        #region building the inputs

        /// <summary>Model mm to screen pixels the way the canvas does it: a tenth of a pixel a mm, y up.</summary>
        private static Matrix Camera()
        {
            return new Matrix(0.1, 0, 0, -0.1, 400, 300);
        }

        private static double AngleOf(ElementViewModel element)
        {
            double angle, top;
            ModelCanvas.PlaceLabel(element, Camera(), 13, out angle, out top);
            return angle;
        }

        private static Color ColourOf(ElementViewModel element)
        {
            return ((SolidColorBrush)ViewPalette.ForState(element)).Color;
        }

        private static ElementViewModel RawEtabsBeam()
        {
            FrameData_e frame = new FrameData_e("B12") { Story = Story };
            frame.Start_e = new MyPoint(0, 0, 3000);
            frame.END_e = new MyPoint(6000, 0, 3000);
            frame._width = 300;
            frame._depth = 600;
            return RawElements.EtabsBeam(frame);
        }

        private static ElementViewModel RawRevitBeam()
        {
            MyPoint start = new MyPoint(0, 0, 3000 / FeetToMM);
            MyPoint end = new MyPoint(6000 / FeetToMM, 0, 3000 / FeetToMM);
            FrameData_r frame = new FrameData_r(4101, start, end, start.DistanceTo(end), new MyPoint(1, 0, 0))
            {
                Story = Story,
                BeamMark = "RB-3"
            };
            frame._width = 250;
            frame._depth = 700;
            return RawElements.RevitBeam(frame);
        }

        private static ElementViewModel Beam(double x1, double y1, double x2, double y2)
        {
            FrameData_e frame = new FrameData_e("B") { Story = Story };
            frame.Start_e = new MyPoint(x1, y1, 3000);
            frame.END_e = new MyPoint(x2, y2, 3000);
            return RawElements.EtabsBeam(frame);
        }

        private static Column_e EtabsColumn()
        {
            FrameData_e frame = new FrameData_e("C-17")
            {
                Start = new MyPoint { X = 0, Y = 0, Z = 0 },
                End = new MyPoint { X = 0, Y = 0, Z = 3000 },
                Story = Story
            };
            frame._width = 400;
            frame._depth = 400;
            frame.Shape = SectionShape.Rectangular;
            return new Column_e(frame);
        }

        private static Column_r RevitColumn()
        {
            Column_r column = new Column_r(7, new MyPoint { X = 0, Y = 0, Z = 0 }, 0);
            column.SetDimensions(SectionShape.Rectangular, 400 / FeetToMM, 400 / FeetToMM, 0);
            column.Story = Story;
            return column;
        }

        /// <summary>A 200 thick panel 4 m long, standing on the given level.</summary>
        private static Wall_E EtabsPanel(string name, double bottom)
        {
            return new Wall_E(name,
                              new[]
                              {
                                  At(0, 0, bottom), At(4000, 0, bottom),
                                  At(4000, 0, bottom + 3000), At(0, 0, bottom + 3000)
                              },
                              Story, 200, "W200");
        }

        private static Slab_E EtabsArea()
        {
            List<Joint> joints = new List<Joint>
            {
                At(0, 0, 3000), At(6000, 0, 3000), At(6000, 6000, 3000), At(0, 6000, 3000)
            };
            return new Slab_E("A1", joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
        }

        private static Slab_R RevitFloor()
        {
            return new Slab_R(55, Square(6000), 3000, 2800) { Story = Story };
        }

        private static PlanPolygon Square(double side)
        {
            return PlanPolygon.FromPoints(new List<MyPoint>
            {
                new MyPoint(0, 0, 0), new MyPoint(side, 0, 0),
                new MyPoint(side, side, 0), new MyPoint(0, side, 0)
            });
        }

        private static Joint At(double x, double y, double z)
        {
            return new Joint("p", new MyPoint(x, y, z), Form1.eLength.mm);
        }

        #endregion
    }
}
