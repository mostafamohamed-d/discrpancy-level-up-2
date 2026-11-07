using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Agreeing to a difference in the slab, one piece or a whole floor of them at a time.
    ///
    /// A piece of slab is the one finding that had nothing to hang an agreement on: it has no
    /// name of its own, being cut afresh out of a shell every run. So it is named by what it was
    /// cut from and where the piece sits, and everything here is about that name being worth
    /// trusting - the same cut has to come back under the same name, and two different pieces
    /// must never end up sharing one, because an agreement that lands on the wrong piece settles
    /// a finding nobody has ever seen.
    /// </summary>
    [TestClass]
    public class SlabZoneReviewTests
    {
        private const string Story = "L03";
        private const double Size = 6000;
        private const string Project = "tower.rvt";

        /// <summary>
        /// The promise the whole thing rests on: check the same two models twice and every piece
        /// comes back filed where it was, so what was agreed last week is still agreed today.
        /// </summary>
        [TestMethod]
        public void TheSameFloorCutTwiceFilesEveryPieceUnderTheSameName()
        {
            List<string> first = Cut().Select(zone => zone.ReviewKey).ToList();
            List<string> second = Cut().Select(zone => zone.ReviewKey).ToList();

            CollectionAssert.AreEqual(first, second,
                "the same floor cut twice must file its pieces the same way, or every agreement " +
                "left on it is lost the next time the check runs");
            Assert.IsFalse(first.Any(key => key.Length == 0),
                "a piece cut out of a named shell always has something to be filed under");
        }

        /// <summary>
        /// The other half of it. One shell cut into pieces that disagree with each other is
        /// exactly when the pieces must be told apart: accepting the thick half is not accepting
        /// the half that was right all along.
        /// </summary>
        [TestMethod]
        public void TwoPiecesOfOneShellAreFiledApart()
        {
            List<SlabZone> zones = Cut();

            Assert.IsTrue(zones.Count >= 2, "the thickness change should have cut the shell");
            Assert.AreEqual(zones.Count, zones.Select(zone => zone.ReviewKey).Distinct().Count(),
                "two pieces of one shell sharing a name would hand one piece's agreement to the " +
                "other: " + string.Join(", ", zones.Select(zone => zone.State + " " + zone.ReviewKey)));
        }

        /// <summary>
        /// An opening sits at the centre of the piece it is a hole in, so a name made of position
        /// alone would file the two together. The size is what tells them apart.
        /// </summary>
        [TestMethod]
        public void AnOpeningIsNotFiledAsThePieceItIsAHoleIn()
        {
            SlabZone around = Zone(Rectangle(0, 0, Size, Size), SlabZoneState.MissingInRevit);
            SlabZone hole = Zone(Rectangle(2500, 2500, 3500, 3500), SlabZoneState.OpeningInEtabsOnly);

            Assert.AreNotEqual(around.ReviewKey, hole.ReviewKey,
                "the opening and the slab it is cut out of share a centre and are not the same " +
                "finding");
        }

        /// <summary>
        /// The two meshes cut the same floor for different questions, and a shell that neither
        /// of them has to cut comes through both of them whole - same shell, same shape, same
        /// centre. Only which mesh cut it keeps the thickness verdict and the load verdict apart.
        /// </summary>
        [TestMethod]
        public void AThicknessPieceAndALoadPieceOverOneShellAreFiledApart()
        {
            SlabZone thickness = Zone(Rectangle(0, 0, Size, Size), SlabZoneState.ThicknessMismatch);
            SlabZone loads = Zone(Rectangle(0, 0, Size, Size), SlabZoneState.Matched);
            loads.Purpose = SlabMeshPurpose.Loads;

            Assert.AreNotEqual(thickness.ReviewKey, loads.ReviewKey,
                "accepting a load on this shell would otherwise settle its thickness too");
        }

        /// <summary>
        /// A cut that really has moved leaves its agreement behind rather than handing it to
        /// whatever was cut there instead. That is the safe way round: a lost acceptance comes
        /// back as open work, where an inherited one is a finding nobody sees.
        /// </summary>
        [TestMethod]
        public void APieceCutSomewhereElseIsNotTheSamePiece()
        {
            SlabZone was = Zone(Rectangle(0, 0, 3000, 3000), SlabZoneState.MissingInRevit);
            SlabZone now = Zone(Rectangle(3000, 0, 6000, 3000), SlabZoneState.MissingInRevit);

            Assert.AreNotEqual(was.ReviewKey, now.ReviewKey);
        }

        /// <summary>
        /// Geometry on its own is too thin a name to hang an agreement on - it says where
        /// something was without saying what it was a piece of - so a zone with neither an ETABS
        /// shell nor a Revit floor behind it is left unfileable rather than filed by shape.
        /// </summary>
        [TestMethod]
        public void APieceWithNothingBehindItCannotBeFiled()
        {
            SlabZone orphan = new SlabZone(Rectangle(0, 0, Size, Size), SlabZoneState.Unresolved,
                                           Story);
            orphan.BuildResults();

            Assert.AreEqual("", orphan.ReviewKey);
            Assert.IsFalse(new ElementCard(ElementViewModelFactory.FromSlabZone(orphan), null)
                               .CanReview);
        }

        /// <summary>
        /// What the card offers on a piece of slab. It used to offer nothing, which read as the
        /// buttons being broken rather than as the finding being unfileable.
        /// </summary>
        [TestMethod]
        public void ThePieceOfSlabUnderTheCursorCanBeAccepted()
        {
            ElementViewModel element = ElementViewModelFactory.FromSlabZone(
                Zone(Rectangle(0, 0, Size, Size), SlabZoneState.ThicknessMismatch));

            Assert.IsTrue(new ElementCard(element, null).CanReview);
        }

        /// <summary>
        /// The whole floor at once, which is how a screed the analysis leaves out is actually
        /// dealt with: tick every row in the drawer, press ACCEPT, and none of them is quietly
        /// skipped for want of a name.
        /// </summary>
        [TestMethod]
        public void EveryTickedPieceOfSlabIsAccepted()
        {
            List<ElementViewModel> elements = Cut()
                .Select(ElementViewModelFactory.FromSlabZone).ToList();

            ReviewNotes said = new ReviewNotes();
            FindingsList drawer = new FindingsList();
            drawer.Update(elements, element => said.Find(Project, element.ReviewKey), null);

            Assert.IsTrue(drawer.Rows.Count > 0, "the cut floor should have something open on it");
            Assert.IsTrue(drawer.Open > 0);

            foreach (FindingRow row in drawer.Rows)
            {
                row.IsPicked = true;
            }

            // The one test the bulk actions skip a row on. Asked here rather than copied, so a
            // guard added to it later cannot start skipping slab again behind a green test.
            Assert.IsTrue(drawer.PickedElements.All(element => element.CanBeFiled),
                          "no piece of slab may be skipped by a bulk accept");

            foreach (ElementViewModel element in drawer.PickedElements)
            {
                said.SetVerdict(Project, element.ReviewKey, HumanVerdict.Accepted,
                                element.VerdictFingerprint);
            }

            drawer.Update(elements, element => said.Find(Project, element.ReviewKey), null);

            Assert.IsTrue(drawer.Rows.Count > 0, "an agreed difference is still a difference");
            Assert.IsTrue(drawer.Rows.All(row => row.IsAccepted),
                          "every ticked row should read back as accepted");
            Assert.AreEqual(0, drawer.Open, "and none of it should still count as work");
        }

        /// <summary>
        /// And the counts agree with the drawer. The chip and the level rail are read to decide
        /// which floor to go to next, so a floor whose differences have all been agreed has to
        /// stop asking to be visited.
        /// </summary>
        [TestMethod]
        public void AnAcceptedPieceOfSlabIsNotCountedAsWork()
        {
            ElementViewModel open = Failing();
            ElementViewModel agreed = Failing();
            agreed.IsAccepted = true;

            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new List<Story_E> { new Story_E(Story, 10000, Form1.eLength.mm) });
            tally.Update(new List<ElementViewModel> { open, agreed }, Story);

            ScopeTab slabs = tally.Tabs.First(tab => tab.Mode == ViewMode.Slabs);
            Assert.AreEqual(1, slabs.Failed, "the agreed piece is not work left to do");
            Assert.AreEqual(1, tally.Levels[0].Failed, "and the rail says which floor to go to");
        }

        #region building the inputs

        private static ElementViewModel Failing()
        {
            ElementViewModel element = ElementViewModelFactory.FromSlabZone(
                Zone(Rectangle(0, 0, Size, Size), SlabZoneState.ThicknessMismatch));
            element.DisplayState = CheckState.Failed;
            return element;
        }

        /// <summary>
        /// One shell with a thicker triangle of floor under part of it, which is the ordinary
        /// reason a shell is cut: the answer changes part way across it.
        /// </summary>
        private static List<SlabZone> Cut()
        {
            SlabCoverage coverage = new SlabCoverage();
            coverage.Add(Floor(500, Rectangle(0, 0, Size, Size), 200));
            coverage.Add(Floor(501, Triangle(), 300));

            List<SlabZone> zones = new List<SlabZone>();
            EtabsFace face = new EtabsFace
            {
                Source = Shell(),
                Plan = Rectangle(0, 0, Size, Size),
                IsWholeArea = true
            };

            SlabMeshCompare compare = new SlabMeshCompare(zones, new List<string>())
            {
                Purpose = SlabMeshPurpose.Slab,
                ThicknessTolerance = 5,
                MinimumZoneArea = 10000,
                MinimumZoneWidth = 20,
                EdgeTolerance = 25,
                LoadTolerance = 0.05,
                LoadPatterns = LoadPatternMap.FromPreferences(PrefrencesData.getPrefrencesData())
            };

            compare.CompareFaces(Story, new List<EtabsFace> { face }, coverage);
            return zones;
        }

        /// <summary>A piece of the shell, built by hand where the shape is the point.</summary>
        private static SlabZone Zone(PlanPolygon plan, SlabZoneState state)
        {
            SlabZone zone = new SlabZone(plan, state, Story)
            {
                EtabsName = "A1",
                EtabsProperty = "SLAB200",
                EtabsThickness = 200,
                RevitId = 500,
                RevitThickness = 300
            };
            zone.BuildResults();
            return zone;
        }

        private static SlabCoverage.Region Floor(long id, PlanPolygon plan, double thickness)
        {
            return new SlabCoverage.Region
            {
                Plan = plan,
                Thickness = thickness,
                RevitId = id,
                TypeName = "SLAB " + thickness,
                TopElevation = 0,
                BottomElevation = -thickness,
                AtStoryPlane = true
            };
        }

        private static Slab_E Shell()
        {
            List<Joint> joints = new[]
            {
                new MyPoint(0, 0, 0),
                new MyPoint(Size, 0, 0),
                new MyPoint(Size, Size, 0),
                new MyPoint(0, Size, 0)
            }.Select(point => new Joint("", point, Form1.eLength.mm)).ToList();

            return new Slab_E("A1", joints, Story, "SLAB200", SlabKind.Slab, 200, 200, false);
        }

        private static PlanPolygon Triangle()
        {
            return PlanPolygon.FromPoints(new[]
            {
                new MyPoint(3000, 3000),
                new MyPoint(6000, 3000),
                new MyPoint(3000, 6000)
            });
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

        #endregion
    }
}
