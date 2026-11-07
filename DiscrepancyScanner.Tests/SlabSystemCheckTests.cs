using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// PT or RC, read off the ETABS property name and the Revit floor type name, on the same
    /// pieces of slab the thickness is checked on.
    /// </summary>
    [TestClass]
    public class SlabSystemCheckTests
    {
        private const string Story = "L03";

        #region reading a name

        [TestMethod]
        public void TheRevitTypeNamesFromTheProjectReadAsTheyShould()
        {
            Assert.AreEqual(ConcreteSystem.Reinforced, ConcreteSystems.Read("MRG_STR_CIP-RC SLAB-200"));
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("MRG_STR_PT SLAB-230"));
        }

        [TestMethod]
        public void PtAnywhereAndInAnyCaseIsPostTensioned()
        {
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("PT230"));
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("pt230"));
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("Slab-Pt-250"));
        }

        /// <summary>
        /// A PT name nearly always has an S in it as well - SLAB, STR - so the PT has to be
        /// asked first or every PT slab would read as reinforced.
        /// </summary>
        [TestMethod]
        public void PtWinsOverTheSInTheSameName()
        {
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("PTS230"));
            Assert.AreEqual(ConcreteSystem.PostTensioned, ConcreteSystems.Read("SLAB PT 230"));
        }

        [TestMethod]
        public void AnSWithoutPtIsReinforced()
        {
            Assert.AreEqual(ConcreteSystem.Reinforced, ConcreteSystems.Read("S200"));
            Assert.AreEqual(ConcreteSystem.Reinforced, ConcreteSystems.Read("slab1"));
        }

        [TestMethod]
        public void ANameWithNeitherIsNotGuessedAt()
        {
            Assert.AreEqual(ConcreteSystem.Unknown, ConcreteSystems.Read("D450"));
            Assert.AreEqual(ConcreteSystem.Unknown, ConcreteSystems.Read(""));
            Assert.AreEqual(ConcreteSystem.Unknown, ConcreteSystems.Read(null));
        }

        #endregion

        #region the verdict on a piece of slab

        [TestMethod]
        public void PtInEtabsAgainstRcInRevitFails()
        {
            SlabZone zone = Zone(SlabZoneState.Matched, "PT230", "MRG_STR_CIP-RC SLAB-200");

            CheckOutcome outcome = PtOrRc(zone);
            Assert.AreEqual(ResultLevel.Failed, outcome.Level);
            Assert.AreEqual("PT", outcome.Etabs);
            Assert.AreEqual("RC", outcome.Revit);
            StringAssert.Contains(outcome.Summary, "PT230");
            StringAssert.Contains(outcome.Summary, "MRG_STR_CIP-RC SLAB-200");
        }

        /// <summary>
        /// The two checks answer separately: a slab can be the right thickness and the wrong
        /// system, and the thickness saying so must not be lost under the system's failure.
        /// </summary>
        [TestMethod]
        public void TheThicknessKeepsItsOwnVerdictBesideIt()
        {
            SlabZone zone = Zone(SlabZoneState.Matched, "PT230", "MRG_STR_CIP-RC SLAB-200");

            Assert.AreEqual(ResultLevel.Passed,
                            zone.Outcomes.Single(o => o.Kind == CheckKind.SlabThickness).Level);
            Assert.AreEqual(ResultLevel.Failed, PtOrRc(zone).Level);
        }

        [TestMethod]
        public void PtInBothPasses()
        {
            SlabZone zone = Zone(SlabZoneState.ThicknessMismatch, "PT230", "MRG_STR_PT SLAB-230");

            Assert.AreEqual(ResultLevel.Passed, PtOrRc(zone).Level);
            Assert.AreEqual("PT", PtOrRc(zone).Etabs);
        }

        [TestMethod]
        public void RcInBothPasses()
        {
            SlabZone zone = Zone(SlabZoneState.Matched, "S200", "MRG_STR_CIP-RC SLAB-200");

            Assert.AreEqual(ResultLevel.Passed, PtOrRc(zone).Level);
        }

        /// <summary>
        /// A name that says neither is not claiming anything, so there is nothing to hold the
        /// other model's name against and the piece is not asked - whichever side it is on.
        /// </summary>
        [TestMethod]
        public void ARevitTypeThatSaysNeitherIsNotChecked()
        {
            SlabZone zone = Zone(SlabZoneState.Matched, "PT700", "DP7 (400x4000x700)");

            Assert.IsFalse(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem));
            Assert.IsFalse(zone.ResultFields.Any(f => f.Kind == CheckKind.SlabSystem));
            Assert.AreEqual(ResultLevel.Passed,
                            zone.Outcomes.Single(o => o.Kind == CheckKind.SlabThickness).Level,
                            "the thickness is still checked");
        }

        [TestMethod]
        public void AnEtabsPropertyThatSaysNeitherIsNotChecked()
        {
            SlabZone zone = Zone(SlabZoneState.Matched, "D450", "MRG_STR_PT SLAB-230");

            Assert.IsFalse(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem));
        }

        /// <summary>
        /// And an unread name is not something to count: a small patch whose only
        /// difference would have been an unreadable name is no more quiet than any other.
        /// </summary>
        [TestMethod]
        public void AnUncheckedPieceIsNotQuietenedByIt()
        {
            SlabZone small = Zone(SlabZoneState.Matched, "D450", "MRG_STR_PT SLAB-230",
                                  600, 600, 0.5e6);

            Assert.IsFalse(small.IsTooSmallToCount);
        }

        [TestMethod]
        public void APatchOnlyOneModelHasIsNotAsked()
        {
            SlabZone etabsOnly = Zone(SlabZoneState.MissingInRevit, "PT230", "");
            SlabZone revitOnly = Zone(SlabZoneState.MissingInEtabs, "", "MRG_STR_CIP-RC SLAB-200");

            Assert.IsFalse(etabsOnly.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem));
            Assert.IsFalse(revitOnly.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem));
        }

        [TestMethod]
        public void TheLoadMeshIsNotAsked()
        {
            SlabZone zone = new SlabZone(Rectangle(0, 0, 3000, 3000), SlabZoneState.NotChecked, Story)
            {
                Purpose = SlabMeshPurpose.Loads,
                EtabsName = "A1",
                EtabsProperty = "PT230",
                RevitType = "MRG_STR_CIP-RC SLAB-200"
            };
            zone.BuildResults();

            Assert.IsFalse(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem));
        }

        #endregion

        #region the switch

        [TestMethod]
        public void TheCheckIsOnUntilSomebodySwitchesItOff()
        {
            PrefrencesData defaults =
                (PrefrencesData)Activator.CreateInstance(typeof(PrefrencesData), nonPublic: true);

            Assert.IsTrue(defaults.RunSlabSystemCheck);
        }

        [TestMethod]
        public void SwitchedOffItPublishesNothing()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            bool was = pref.RunSlabSystemCheck;
            try
            {
                pref.RunSlabSystemCheck = false;
                SlabZone zone = Zone(SlabZoneState.Matched, "PT230", "MRG_STR_CIP-RC SLAB-200");

                Assert.IsFalse(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabSystem),
                               "a check that is off is not reported as anything, passing included");
                Assert.IsTrue(zone.Outcomes.Any(o => o.Kind == CheckKind.SlabThickness),
                              "and the thickness is untouched by it");
            }
            finally
            {
                pref.RunSlabSystemCheck = was;
            }
        }

        [TestMethod]
        public void TheSwitchIsOnTheSettingsPanel()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");
            settings.SearchText = "PT against RC";

            Assert.IsTrue(settings.Groups.SelectMany(group => group.Items)
                                  .Any(item => item.Key == "RunSlabSystemCheck"));
        }

        [TestMethod]
        public void ItHasAFilterRowOnTheSlabTab()
        {
            CheckFilters filters = new CheckFilters(() => { });
            CheckFilterGroup group = filters.Groups.Single(g => g.Kind == CheckKind.SlabSystem);

            Assert.AreEqual(ViewMode.Slabs, group.Mode, "it is read on the same tab as the thickness");
            Assert.IsTrue(group.Options.Select(option => option.Level)
                               .SequenceEqual(new[] { ResultLevel.Passed, ResultLevel.Failed }),
                          "same or different, and nothing in between now a name that says neither "
                          + "is left unchecked");
        }

        #endregion

        #region too small to count

        /// <summary>
        /// A sliver where a PT floor's edge runs onto an RC shell is the same drafting noise as a
        /// sliver of thickness difference, and goes by the same size.
        /// </summary>
        [TestMethod]
        public void ASmallPatchOfTheWrongSystemIsNotCounted()
        {
            SlabZone small = Zone(SlabZoneState.Matched, "PT230", "MRG_STR_CIP-RC SLAB-200",
                                  600, 600, 0.5e6);

            Assert.IsTrue(small.IsTooSmallToCount);
            StringAssert.Contains(PtOrRc(small).Summary, "not counted");
            Assert.IsFalse(small.Summary.Contains("not counted"),
                           "the thickness agrees, so its line has no count to explain: " + small.Summary);
        }

        [TestMethod]
        public void ALargePatchOfTheWrongSystemIsCounted()
        {
            SlabZone big = Zone(SlabZoneState.Matched, "PT230", "MRG_STR_CIP-RC SLAB-200",
                                2000, 2000, 0.5e6);

            Assert.IsFalse(big.IsTooSmallToCount);
            Assert.IsFalse(PtOrRc(big).Summary.Contains("not counted"));
        }

        #endregion

        #region through the real check

        /// <summary>
        /// A 6 m bay drawn PT in ETABS under an RC floor of the same depth in Revit. The thickness
        /// agrees and the system does not, and both are said about the one piece.
        /// </summary>
        [TestMethod]
        public void TheCheckReadsTheEtabsPropertyAndTheRevitType()
        {
            SlabCompareEngine engine = new SlabCompareEngine { OnlyStory = Story };
            engine.StartComparison(
                new List<Slab_E> { Shell("PT200") },
                new List<Slab_R> { Floor(500, Rectangle(0, 0, 6000, 6000), 200, "MRG_STR_CIP-RC SLAB-200") },
                new List<Story_E> { new Story_E(Story, 0, Form1.eLength.mm) });

            SlabZone zone = engine.Zones.Single();
            Assert.AreEqual(SlabZoneState.Matched, zone.State);
            Assert.AreEqual(ResultLevel.Failed, PtOrRc(zone).Level);

            ElementViewModel element = ElementViewModelFactory.FromSlabZone(zone);
            Assert.IsTrue(new ElementCard(element, null).Rows.Any(row => row.Kind == CheckKind.SlabSystem),
                          "the card says it beside the thickness");
        }

        #endregion

        #region building the inputs

        private static CheckOutcome PtOrRc(SlabZone zone)
        {
            return zone.Outcomes.Single(o => o.Kind == CheckKind.SlabSystem);
        }

        private static SlabZone Zone(SlabZoneState state, string etabsProperty, string revitType,
                                     double width = 3000, double height = 3000, double size = 0)
        {
            bool etabs = state != SlabZoneState.MissingInEtabs;
            bool revit = state != SlabZoneState.MissingInRevit;

            SlabZone zone = new SlabZone(Rectangle(0, 0, width, height), state, Story)
            {
                Purpose = SlabMeshPurpose.Slab,
                MinimumIssueArea = size,
                EtabsName = etabs ? "A1" : "",
                EtabsProperty = etabsProperty,
                EtabsThickness = etabs ? 200 : 0,
                RevitId = revit ? 500 : 0,
                RevitType = revitType,
                RevitThickness = !revit ? 0 : state == SlabZoneState.ThicknessMismatch ? 250 : 200
            };
            zone.BuildResults();
            return zone;
        }

        private static Slab_E Shell(string property)
        {
            List<Joint> joints = Corners(0, 0, 6000, 6000)
                .Select(point => new Joint("", point, Form1.eLength.mm)).ToList();

            return new Slab_E("A1", joints, Story, property, SlabKind.Slab, 200, 200, false);
        }

        private static Slab_R Floor(long revitId, PlanPolygon plan, double thickness, string type)
        {
            return new Slab_R(revitId, plan, 0, -thickness)
            {
                Story = Story,
                Parts = new List<PlanPolygon> { plan },
                TypeName = type,
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

        #endregion
    }
}
