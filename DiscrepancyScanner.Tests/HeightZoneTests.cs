using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Height zones: patches of a level, drawn by hand, where the line loads take a floor to
    /// floor height of their own - which zone a line answers to, what that does to the load it
    /// asks for, how the zones are kept, and how they travel in a project file.
    /// </summary>
    [TestClass]
    public class HeightZoneTests
    {
        private const string Project = @"C:\jobs\Tower A.rvt";
        private const string Story = "L03";

        #region which zone a line answers to

        [TestMethod]
        public void ALineInsideAZoneTakesIt()
        {
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);

            Assert.AreSame(lobby, HeightZones.MostOf(new[] { lobby }, Line(2000, 5000, 8000, 5000)));
        }

        /// <summary>
        /// The zone covering most of the line gives the whole line its height - and where most
        /// of it is outside every zone, the level's own height stands.
        /// </summary>
        [TestMethod]
        public void TheZoneCoveringMostOfTheLineDecides()
        {
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);

            Assert.AreSame(lobby, HeightZones.MostOf(new[] { lobby }, Line(4000, 5000, 14000, 5000)),
                           "six metres in, four out");
            Assert.IsNull(HeightZones.MostOf(new[] { lobby }, Line(8000, 5000, 18000, 5000)),
                          "two metres in, eight out: the floor height");
            Assert.IsNull(HeightZones.MostOf(new[] { lobby }, Line(20000, 5000, 30000, 5000)),
                          "nowhere near it");
        }

        /// <summary>A small zone drawn over a large one is the exception to it, inside it.</summary>
        [TestMethod]
        public void TheSmallerOfTwoOverlappingZonesAnswersInsideIt()
        {
            HeightZone floor = Zone("whole bay", 0, 0, 20000, 20000, 4000);
            HeightZone plant = Zone("plant", 5000, 5000, 10000, 10000, 5500);

            Assert.AreSame(plant, HeightZones.MostOf(new[] { floor, plant }, Line(5500, 7000, 9500, 7000)));
            Assert.AreSame(floor, HeightZones.MostOf(new[] { floor, plant }, Line(1000, 1000, 4000, 1000)));
        }

        /// <summary>A bent line is measured along every leg of it.</summary>
        [TestMethod]
        public void EveryLegOfABentLineCounts()
        {
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);

            // Three metres in along the first leg, then eight metres on, out of it, along the second.
            IEnumerable<KeyValuePair<MyPoint, MyPoint>> bent = new[]
            {
                Piece(7000, 5000, 10000, 5000),
                Piece(10000, 5000, 18000, 5000)
            };

            Assert.IsNull(HeightZones.MostOf(new[] { lobby }, bent));
        }

        #endregion

        #region what it does to the load

        /// <summary>
        /// A load written per square metre of elevation is worked out from the zone's height in
        /// place of the story above - with the allowance still taken off it - and the working
        /// says the zone was where the height came from. A line outside keeps the level's.
        /// </summary>
        [TestMethod]
        public void ALineLoadInAZoneIsWorkedOutFromItsHeight()
        {
            HeightZones zones = new HeightZones();
            zones.Add(Project, Zone("lobby", 0, 0, 10000, 10000, 4000));

            LineLoadCompareEngine engine = Engine(zones);
            engine.StartComparison(new[] { Drawn(901, 1, 1000, 5000, 9000, 5000), Drawn(902, 1, 20000, 5000, 28000, 5000) },
                                   new LineLoadFrame_E[0]);

            LineLoadResult inside = engine.Results.Single(result => result.Line.RevitID == 901);
            LineLoadResult outside = engine.Results.Single(result => result.Line.RevitID == 902);

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            double reduction = pref.LineLoadHeightReduction;

            Assert.AreEqual(LineLoadHeight.RoundUp(4.8 * (4000 - reduction) / 1000, pref.LineLoadRoundUpTo),
                            inside.Expected, 1e-9, inside.How);
            Assert.AreEqual(LineLoadHeight.RoundUp(4.8 * (3000 - reduction) / 1000, pref.LineLoadRoundUpTo),
                            outside.Expected, 1e-9, outside.How);

            StringAssert.Contains(inside.How, "height zone \"lobby\"");
            StringAssert.Contains(inside.HeightZone, "lobby");
            Assert.AreEqual("", outside.HeightZone);
            StringAssert.Contains(outside.How, "the story above");
        }

        /// <summary>A load off the schedule per metre run is the same whatever the storey, so a zone says nothing about it.</summary>
        [TestMethod]
        public void AZoneLeavesALoadThatDoesNotDependOnHeightAlone()
        {
            HeightZones zones = new HeightZones();
            zones.Add(Project, Zone("lobby", 0, 0, 10000, 10000, 4000));

            LineLoadCompareEngine engine = Engine(zones);
            engine.StartComparison(new[] { Drawn(903, 5, 1000, 5000, 9000, 5000) }, new LineLoadFrame_E[0]);

            Assert.AreEqual(1.5, engine.Results[0].Expected, 1e-9);
            Assert.AreEqual("", engine.Results[0].HeightZone);
        }

        /// <summary>A zone belongs to the level it was drawn on.</summary>
        [TestMethod]
        public void AZoneOnAnotherLevelDoesNothingHere()
        {
            HeightZones zones = new HeightZones();
            HeightZone upstairs = Zone("lobby", 0, 0, 10000, 10000, 4000);
            upstairs.Story = "L04";
            zones.Add(Project, upstairs);

            LineLoadCompareEngine engine = Engine(zones);
            engine.StartComparison(new[] { Drawn(904, 1, 1000, 5000, 9000, 5000) }, new LineLoadFrame_E[0]);

            Assert.AreEqual("", engine.Results[0].HeightZone);
        }

        #endregion

        #region keeping them

        [TestMethod]
        public void AZoneWithNoAreaOrNoHeightIsNotKept()
        {
            HeightZones zones = new HeightZones();

            HeightZone flat = new HeightZone { Story = Story, Height = 4000 };
            flat.SetOutline(new[] { new MyPoint(0, 0, 0), new MyPoint(1000, 0, 0) });
            Assert.IsFalse(zones.Add(Project, flat), "two corners are not a region");

            Assert.IsFalse(zones.Add(Project, Zone("", 0, 0, 1000, 1000, 0)), "no height");
            Assert.AreEqual(0, zones.CountFor(Project));
        }

        /// <summary>
        /// A copy goes on each level ticked and on no other, and copying twice is copying once:
        /// a zone round the same corners on the level already is replaced, not doubled.
        /// </summary>
        [TestMethod]
        public void CopyingToLevelsPutsOneCopyOnEach()
        {
            HeightZones zones = new HeightZones();
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);
            zones.Add(Project, lobby);

            Assert.AreEqual(2, zones.CopyTo(Project, lobby.Id, new[] { "L04", "L05", Story }),
                            "its own level is not copied onto");
            Assert.AreEqual(1, zones.CopyTo(Project, lobby.Id, new[] { "L04" }));

            Assert.AreEqual(1, zones.OnStory(Project, "L04").Count, "replaced, not doubled");
            Assert.AreEqual(6000, zones.OnStory(Project, "L05").Single().Height);
            Assert.AreNotEqual(lobby.Id, zones.OnStory(Project, "L05").Single().Id, "a copy of its own");
        }

        /// <summary>Zones are kept in the settings file, and come back out of it as they went in.</summary>
        [TestMethod]
        public void ZonesComeBackOutOfTheSettingsFileAsTheyWentIn()
        {
            HeightZones zones = new HeightZones();
            HeightZone plant = Zone("plant room", 1000.5, 2000, 7000, 9000, 5250);
            zones.Add(Project, plant);
            zones.Add("other.rvt", Zone("", 0, 0, 3000, 3000, 3600));

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { HeightZones.Branch, zones.ToJson() }
            };

            HeightZones read = new HeightZones();
            read.ReadFrom(Json.Read(Json.Write(view)));

            HeightZone back = read.Find(Project, plant.Id);
            Assert.IsNotNull(back);
            Assert.AreEqual("plant room", back.Name);
            Assert.AreEqual(5250, back.Height);
            Assert.AreEqual(Story, back.Story);
            Assert.IsTrue(back.SameOutlineAs(plant));
            Assert.AreEqual(1, read.CountFor("other.rvt"));
        }

        #endregion

        #region travelling in a project file

        /// <summary>
        /// A project file carries the one project - its zones, its pairings, its loading plans -
        /// and read into another it replaces what that one had, leaving every other project as
        /// it was.
        /// </summary>
        [TestMethod]
        public void AProjectFileCarriesOneProjectIntoAnother()
        {
            HeightZones zones = new HeightZones();
            zones.Add(Project, Zone("lobby", 0, 0, 10000, 10000, 6000));

            PairingOverrides pairs = new PairingOverrides();
            pairs.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Beam, "B1"), 5501);
            pairs.Set("copy.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B9"), 7777);
            pairs.Set("unrelated.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B2"), 6601);

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { HeightZones.Branch, zones.ToJson() },
                { PairingOverrides.Branch, pairs.ToJson() },
                { ProjectFile.AlignmentBranch, new Dictionary<string, object>
                    {
                        { "copy.rvt", new Dictionary<string, object> { { "dx", 5d } } }
                    }
                }
            };

            Dictionary<string, object> file = Json.Read(Json.Write(
                ProjectFile.Export(view, Project, "Tower A", new DateTime(2026, 9, 26, 10, 30, 0))));

            ProjectFile.Contents contents;
            string problem;
            Assert.IsTrue(ProjectFile.TryRead(file, out contents, out problem), problem);
            Assert.AreEqual("Tower A", contents.Project);
            Assert.AreEqual(1, contents.HeightZones);
            Assert.AreEqual(1, contents.HandPairings);
            Assert.IsFalse(contents.HasAlignment, "the project exported was never aligned");

            ProjectFile.Import(view, "copy.rvt", contents.Data);
            Dictionary<string, object> after = Json.Read(Json.Write(view));

            HeightZones zonesAfter = new HeightZones();
            zonesAfter.ReadFrom(after);
            PairingOverrides pairsAfter = new PairingOverrides();
            pairsAfter.ReadFrom(after);

            Assert.AreEqual(1, zonesAfter.CountFor("copy.rvt"), "the zone came across");
            Assert.AreEqual(5501L, pairsAfter.Find("copy.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B1")));
            Assert.IsNull(pairsAfter.Find("copy.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B9")),
                          "what the project had and the file had not is gone");
            Assert.AreEqual(6601L, pairsAfter.Find("unrelated.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B2")),
                            "other projects are left alone");
            Assert.AreEqual(1, Json.Branch(after, ProjectFile.AlignmentBranch).Count,
                            "an alignment the file has none of is left as it was");
        }

        [TestMethod]
        public void AFileThatIsNotAProjectFileIsTurnedAway()
        {
            ProjectFile.Contents contents;
            string problem;

            Assert.IsFalse(ProjectFile.TryRead(new Dictionary<string, object> { { "preferences", 1d } },
                                               out contents, out problem));
            Assert.IsNotNull(problem);
        }

        #endregion

        #region on the plan and in the dialogs

        /// <summary>
        /// A zone on the plan is changed rather than judged: its card offers its height, a copy
        /// and a delete, never a review, an acceptance or a pairing, and it always says its height.
        /// </summary>
        [TestMethod]
        public void AZoneIsChangedRatherThanJudged()
        {
            HeightZoneElementViewModel element = new HeightZoneElementViewModel(
                Zone("lobby", 0, 0, 10000, 10000, 6000), new List<ResultField>());

            ElementCard card = new ElementCard(element, null);
            Assert.IsTrue(card.IsHeightZone);
            Assert.IsFalse(card.IsFinding);
            Assert.IsFalse(card.CanReview);
            Assert.IsFalse(PairingEditor.CanBeMarked(element));
            Assert.AreEqual("Height zone", CheckNaming.KindOf(element));
            StringAssert.Contains(element.Caption, "6.00");
            StringAssert.Contains(element.Caption, "lobby");
        }

        /// <summary>Two opposite corners make the four of the rectangle, in order round it.</summary>
        [TestMethod]
        public void TwoCornersMakeTheRectangle()
        {
            IList<Point> corners = ModelCanvas.RectangleCorners(new Point(0, 0), new Point(4000, 3000));

            CollectionAssert.AreEqual(new[] { new Point(0, 0), new Point(4000, 0), new Point(4000, 3000), new Point(0, 3000) },
                                      corners.ToArray());
        }

        /// <summary>The height is typed in metres, as a drawing gives it, and nothing that is not a height is kept.</summary>
        [TestMethod]
        public void TheHeightIsTypedInMetres()
        {
            HeightZoneRequest request = new HeightZoneRequest("NEW HEIGHT ZONE ON L03", 3200, 3200, "");
            Assert.AreEqual(3200, request.HeightMM, 1e-9, "starts at the level's own");

            request.HeightText = "4.5 m";
            Assert.AreEqual(4500, request.HeightMM, 1e-9);
            Assert.IsTrue(request.IsValid);

            request.HeightText = "tall";
            Assert.IsFalse(request.IsValid);
            Assert.IsTrue(request.HasProblem);

            request.HeightText = "0";
            Assert.IsFalse(request.IsValid, "no height at all is not a height");

            request.Name = "plant room ";
            Assert.AreEqual("plant room", request.KeptName);
        }

        [TestMethod]
        public void HeightZonesCanBeForgotten()
        {
            ForgetChoices choices = new ForgetChoices(0, 0, 0, 0, 0, heightZones: 3);

            ForgetOption zones = choices.Options.Single(option => option.What == ForgetWhat.HeightZones);
            Assert.AreEqual(3, zones.Count);

            choices.Everything = true;
            Assert.IsTrue(choices.IsChosen(ForgetWhat.HeightZones));
        }

        #endregion

        #region colours and overlaps

        /// <summary>
        /// The colour is the height's: zones of one height are one colour, on any level, and two
        /// heights are two colours - lowest first down the list.
        /// </summary>
        [TestMethod]
        public void TheColourIsTheHeights()
        {
            HeightZones zones = new HeightZones();
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);
            HeightZone plant = Zone("plant", 20000, 0, 30000, 10000, 4500);
            HeightZone upstairs = Zone("upstairs lobby", 0, 0, 10000, 10000, 6000);
            upstairs.Story = "L04";
            zones.Add(Project, lobby);
            zones.Add(Project, plant);
            zones.Add(Project, upstairs);

            Assert.AreEqual(zones.ColourOf(Project, 6000), zones.ColourOf(Project, lobby.Height));
            Assert.AreEqual(zones.ColourOf(Project, lobby.Height), zones.ColourOf(Project, upstairs.Height),
                            "one height, one colour, on any level");
            Assert.AreNotEqual(zones.ColourOf(Project, lobby.Height), zones.ColourOf(Project, plant.Height));

            Assert.AreEqual(HeightZones.Colours[0], zones.ColourOf(Project, 4500), "the lowest height takes the first colour");
            Assert.AreEqual(HeightZones.Colours[1], zones.ColourOf(Project, 6000));
        }

        [TestMethod]
        public void EveryHeightOnAFloorIsItsOwnColourUpToAsManyAsThereAre()
        {
            double[] heights = Enumerable.Range(0, HeightZones.Colours.Length).Select(i => 3000d + 250 * i).ToArray();

            CollectionAssert.AllItemsAreUnique(heights.Select(height => HeightZones.ColourOfHeight(heights, height)).ToList());
            Assert.AreEqual(HeightZones.ColourOfHeight(heights, 3000), HeightZones.ColourOfHeight(heights, 3000.4),
                            "a height is read to the millimetre");
        }

        /// <summary>
        /// Where two zones overlap, each says so and says which of the two is used there - the
        /// smaller - which is the one the line load check takes. Zones only sharing an edge do
        /// not overlap.
        /// </summary>
        [TestMethod]
        public void OverlappingZonesSayWhichOfThemIsUsed()
        {
            HeightZones zones = new HeightZones();
            HeightZone bay = Zone("whole bay", 0, 0, 20000, 20000, 4000);
            HeightZone plant = Zone("plant", 5000, 5000, 10000, 10000, 5500);
            HeightZone beside = Zone("beside", 20000, 0, 30000, 10000, 5000);
            zones.Add(Project, bay);
            zones.Add(Project, plant);
            zones.Add(Project, beside);

            KeyValuePair<HeightZone, bool> seenFromBay = zones.OverlapsOf(Project, bay).Single();
            Assert.AreEqual(plant.Id, seenFromBay.Key.Id, "the bay overlaps the plant, and not the zone beside it");
            Assert.IsFalse(seenFromBay.Value, "the plant is used where the two overlap");

            KeyValuePair<HeightZone, bool> seenFromPlant = zones.OverlapsOf(Project, plant).Single();
            Assert.AreEqual(bay.Id, seenFromPlant.Key.Id);
            Assert.IsTrue(seenFromPlant.Value, "and the plant says it is the one used");

            Assert.AreEqual(0, zones.OverlapsOf(Project, beside).Count, "sharing an edge is not overlapping");

            Assert.AreSame(plant, HeightZones.MostOf(zones.OnStory(Project, Story), Line(5500, 7000, 9500, 7000)),
                           "which is the zone the line load check takes");
        }

        /// <summary>
        /// Two the same size overlapping: the one kept first is used, and editing it - keeping it
        /// again - does not change which.
        /// </summary>
        [TestMethod]
        public void OfTwoTheSameSizeTheFirstKeptIsUsedEvenAfterAnEdit()
        {
            HeightZones zones = new HeightZones();
            HeightZone first = Zone("first", 0, 0, 10000, 10000, 6000);
            HeightZone second = Zone("second", 5000, 0, 15000, 10000, 4500);
            zones.Add(Project, first);
            zones.Add(Project, second);

            first.Height = 6500;
            zones.Add(Project, first);

            Assert.AreEqual("first", HeightZones.MostOf(zones.OnStory(Project, Story), Line(6000, 5000, 9000, 5000)).Name);
            Assert.IsTrue(zones.OverlapsOf(Project, first).Single().Value);
        }

        [TestMethod]
        public void AZoneIsDrawnInItsHeightsColour()
        {
            HeightZone lobby = Zone("lobby", 0, 0, 10000, 10000, 6000);

            System.Windows.Media.SolidColorBrush brush = ViewPalette.ForState(
                new HeightZoneElementViewModel(lobby, new List<ResultField>(), "#00796B")) as System.Windows.Media.SolidColorBrush;

            Assert.IsNotNull(brush);
            Assert.AreEqual(System.Windows.Media.Color.FromRgb(0x00, 0x79, 0x6B), brush.Color);
            Assert.AreSame(ViewPalette.HeightZone, ViewPalette.ZoneBrush("not a colour"), "a colour that does not read falls back");
        }

        /// <summary>
        /// Where a smaller zone of another height overlaps a larger one, only the smaller one is
        /// coloured in there: the larger is drawn without that part, writes its height in what
        /// is left of it, and a click in the part cut out of it picks the smaller one.
        /// </summary>
        [TestMethod]
        public void AZoneIsDrawnOnlyWhereItIsInControl()
        {
            HeightZone bay = Zone("whole bay", 0, 0, 20000, 10000, 4000);
            HeightZone plant = Zone("plant", 0, 0, 12000, 10000, 5500);

            HeightZoneElementViewModel under = new HeightZoneElementViewModel(
                bay, new List<ResultField>(), "#1565C0", new List<PlanPolygon> { plant.Plan });

            Assert.AreEqual(8000d * 10000d, under.Geometry.GetArea(), 1000, "the bay less the plant room");
            Assert.IsTrue(under.IsFilled);
            Assert.IsTrue(under.LabelAnchor.X > 12000, "its height is written in the part still its own: " + under.LabelAnchor);
            Assert.AreEqual("4.00 m - whole bay", under.Caption.Replace(",", "."));

            HeightZoneElementViewModel over = new HeightZoneElementViewModel(plant, new List<ResultField>(), "#6A1B9A");
            Point inTheOverlap = new Point(6000, 5000);
            Assert.AreEqual(0, over.DistanceTo(inTheOverlap), "the plant room is picked there");
            Assert.IsTrue(under.DistanceTo(inTheOverlap) > 1000, "and the bay is not");
            Assert.AreEqual(0, under.DistanceTo(new Point(16000, 5000)), "the bay is picked where it is in control");
        }

        /// <summary>A zone the smaller ones cover completely answers nowhere: a bare outline, no height written.</summary>
        [TestMethod]
        public void AZoneCoveredCompletelyIsABareOutline()
        {
            HeightZone bay = Zone("whole bay", 0, 0, 10000, 10000, 4000);
            HeightZone left = Zone("left", 0, 0, 5000, 10000, 5500);
            HeightZone right = Zone("right", 5000, 0, 10000, 10000, 6000);

            HeightZoneElementViewModel under = new HeightZoneElementViewModel(
                bay, new List<ResultField>(), "#1565C0", new List<PlanPolygon> { left.Plan, right.Plan });

            Assert.IsTrue(under.IsHidden);
            Assert.IsFalse(under.IsFilled);
            Assert.IsNull(under.Caption);
            Assert.IsFalse(under.Geometry.Bounds.IsEmpty, "still an outline to be found by");
        }

        #endregion

        #region building the inputs

        private static HeightZone Zone(string name, double x1, double y1, double x2, double y2, double height)
        {
            HeightZone zone = new HeightZone { Story = Story, Name = name, Height = height };
            zone.SetOutline(new[]
            {
                new MyPoint(x1, y1, 0), new MyPoint(x2, y1, 0), new MyPoint(x2, y2, 0), new MyPoint(x1, y2, 0)
            });
            return zone;
        }

        private static IEnumerable<KeyValuePair<MyPoint, MyPoint>> Line(double x1, double y1, double x2, double y2)
        {
            return new[] { Piece(x1, y1, x2, y2) };
        }

        private static KeyValuePair<MyPoint, MyPoint> Piece(double x1, double y1, double x2, double y2)
        {
            return new KeyValuePair<MyPoint, MyPoint>(new MyPoint(x1, y1, 0), new MyPoint(x2, y2, 0));
        }

        private static LineLoadCompareEngine Engine(HeightZones zones)
        {
            LineLoadCompareEngine engine = new LineLoadCompareEngine
            {
                HeightZones = zones,
                ProjectKey = Project
            };

            LineLoadSchedule schedule = new LineLoadSchedule("", "Line Load D (TOWER)");
            schedule.Add(new LineLoadEntry(1, "ALLOWANCE FOR 200mm THICK SOLID BLOCK WALL", 4.8, false, "4.8"));
            schedule.Add(new LineLoadEntry(5, "ALLOWANCE FOR GLASS BALUSTRADE / RAILING", 1.5, true, "1.50 kN/m'"));
            engine.SetSchedule(Story, schedule);
            engine.SetStoryAboveHeight(Story, 3000);
            return engine;
        }

        private static LineLoad_R Drawn(long revitId, int number, double x1, double y1, double x2, double y2)
        {
            return new LineLoad_R(revitId, "MRG_STR-Line Load-TL" + number,
                                  new[] { new MyPoint(x1, y1, 0), new MyPoint(x2, y2, 0) }, number, Story);
        }

        #endregion
    }
}
