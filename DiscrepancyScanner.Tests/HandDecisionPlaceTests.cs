using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Decisions made by hand hold only while the elements they were made about stand where
    /// they stood.
    ///
    /// The case this was written for: an ETABS model renumbered after a beam on L01 had been
    /// paired by hand, so the unique name the pairing was set on now belonged to a beam on L03
    /// - and the pairing, obeyed before any distance is measured, pinned the L03 beam to the
    /// L01 one.
    /// </summary>
    [TestClass]
    public class HandDecisionPlaceTests
    {
        private const string Project = @"C:\models\tower.rvt";
        private const double FeetToMM = 304.8d;

        private MyTransformation alignmentBefore;

        [TestInitialize]
        public void SetUp()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.BeamPlanTolerance = 500;
            pref.BeamVerticalTolerance = 500;

            // The alignment is shared by the whole tool; each test starts from none and puts
            // back whatever was there.
            alignmentBefore = DataContainer.ModelTransformation;
            DataContainer.ModelTransformation = new MyTransformation();
        }

        [TestCleanup]
        public void PutTheAlignmentBack()
        {
            DataContainer.ModelTransformation = alignmentBefore;
        }

        #region hand-set pairings

        /// <summary>
        /// The reported case, whole: paired by hand on L01, then the ETABS name handed to a beam
        /// on L03. The pairing is dropped, the L03 beam is not pinned to the L01 one, and the
        /// person running the check is told.
        /// </summary>
        [TestMethod]
        public void APairingWhoseEtabsNameMovedToAnotherBeamIsDropped()
        {
            // 800 off in plan, past the 500 buffer, which is why it had to be paired by hand.
            FrameData_r revitL01 = RevitBeam(2308763, "L01", 0, 800, 6000, 800, 3000);

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, BeamKey("18926"), 2308763);

            EndpointCompareEngine first = Engine(overrides);
            first.StartComparison(new List<FrameData_r> { revitL01 },
                                  new List<FrameData_e> { EtabsBeam("18926", "L01", 0, 0, 6000, 0, 3000) });
            Assert.AreEqual("18926->2308763", Pairs(first).Single(), "the pairing that was set");

            // Renumbered: the L01 beam is now 20001, and 18926 is a beam two floors up.
            EndpointCompareEngine second = Engine(overrides);
            second.StartComparison(new List<FrameData_r> { revitL01 },
                                   new List<FrameData_e>
                                   {
                                       EtabsBeam("20001", "L01", 0, 0, 6000, 0, 3000),
                                       EtabsBeam("18926", "L03", 20000, 8000, 26000, 8000, 9000)
                                   });

            Assert.IsFalse(Pairs(second).Contains("18926->2308763"),
                           "the L03 beam that inherited the name is not pinned to the L01 beam");
            Assert.IsNull(overrides.Find(Project, BeamKey("18926")), "the pairing is forgotten");

            IList<string> dropped = overrides.TakeDropped();
            Assert.AreEqual(1, dropped.Count, "and somebody is told");
            StringAssert.Contains(dropped[0], "18926");
            StringAssert.Contains(dropped[0], "2308763");
            Assert.IsTrue(overrides.TakeChanged(), "which the settings file has to hear about");
        }

        [TestMethod]
        public void APairingWhoseRevitBeamMovedIsDropped()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, BeamKey("B1"), 501);

            FrameData_e etabs = EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000);
            Engine(overrides).StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { etabs });

            // The Revit beam moved a bay over in the Revit model.
            EndpointCompareEngine again = Engine(overrides);
            again.StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 6800, 6000, 6800, 3000) },
                new List<FrameData_e> { etabs });

            Assert.AreEqual(0, again.PairedFrames.Count, "decided by distance now, and 6.8 m is nothing");
            Assert.IsNull(overrides.Find(Project, BeamKey("B1")));
        }

        /// <summary>
        /// A few millimetres is the rounding a model picks up being written out and read back,
        /// not a move, and must not cost anybody a decision.
        /// </summary>
        [TestMethod]
        public void APairingSurvivesAMoveOfAFewMillimetres()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, BeamKey("B1"), 501);

            Engine(overrides).StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000) });

            EndpointCompareEngine again = Engine(overrides);
            again.StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 4, 803, 6004, 803, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L01", 6000, 0, 0, 0, 3000) });

            Assert.AreEqual("B1->501", Pairs(again).Single(),
                            "still paired - and drawing the ETABS beam from its other end is not a move");
            Assert.AreEqual(0, overrides.TakeDropped().Count);
        }

        /// <summary>
        /// Realigning the two models moves every Revit element in the shared system, and none of
        /// them in the Revit model. It is not a move of anything anybody paired.
        /// </summary>
        [TestMethod]
        public void RealigningTheModelsDropsNoPairing()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, BeamKey("B1"), 501);

            FrameData_e etabs = EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000);
            FrameData_r revit = RevitBeam(501, "L01", 0, 800, 6000, 800, 3000);
            Engine(overrides).StartComparison(new List<FrameData_r> { revit }, new List<FrameData_e> { etabs });

            MyTransformation moved = new MyTransformation();
            moved.SetAlignment(new MyPoint(150, -90, 0), 0.5);
            revit.MoveBy(MyTransformation.Difference(moved, DataContainer.ModelTransformation));
            DataContainer.ModelTransformation = moved;

            EndpointCompareEngine again = Engine(overrides);
            again.StartComparison(new List<FrameData_r> { revit }, new List<FrameData_e> { etabs });

            Assert.AreEqual("B1->501", Pairs(again).Single());
            Assert.AreEqual(0, overrides.TakeDropped().Count);
        }

        /// <summary>Where the two stood has to outlive the tool being closed, or it protects nothing.</summary>
        [TestMethod]
        public void WhereTheyStoodSurvivesTheSettingsFile()
        {
            PairingOverrides written = new PairingOverrides();
            written.Set(Project, BeamKey("B1"), 501);
            Engine(written).StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000) });

            PairingOverrides read = ThroughTheFile(written);

            EndpointCompareEngine next = Engine(read);
            next.StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L03", 0, 0, 6000, 0, 9000) });

            Assert.AreEqual(0, next.PairedFrames.Count);
            Assert.IsNull(read.Find(Project, BeamKey("B1")),
                          "read back, it still knows the ETABS beam was on L01");
        }

        /// <summary>
        /// A pairing from a file written before places were kept cannot say whether it has
        /// moved. One whose two sides are on different floors is not believable as a pairing
        /// at all, and goes.
        /// </summary>
        [TestMethod]
        public void AnOlderPairingAcrossStoriesIsDropped()
        {
            PairingOverrides older = Older(BeamKey("18926"), 2308763);

            EndpointCompareEngine engine = Engine(older);
            engine.StartComparison(
                new List<FrameData_r> { RevitBeam(2308763, "L01", 0, 0, 6000, 0, 3000) },
                new List<FrameData_e> { EtabsBeam("18926", "L03", 0, 0, 6000, 0, 9000) });

            Assert.AreEqual(0, engine.PairedFrames.Count);
            Assert.IsNull(older.Find(Project, BeamKey("18926")));
            StringAssert.Contains(older.TakeDropped().Single(), "different stories");
        }

        [TestMethod]
        public void AnOlderPairingFarApartInPlanIsDropped()
        {
            PairingOverrides older = Older(BeamKey("18926"), 2308763);

            Engine(older).StartComparison(
                new List<FrameData_r> { RevitBeam(2308763, "L01", 0, 0, 6000, 0, 3000) },
                new List<FrameData_e> { EtabsBeam("18926", "L01", 30000, 20000, 36000, 20000, 3000) });

            Assert.IsNull(older.Find(Project, BeamKey("18926")));
        }

        /// <summary>
        /// One that is believable is kept, and where its two sides stand now is written into it,
        /// so from here on it is held to that like any other.
        /// </summary>
        [TestMethod]
        public void AnOlderPairingThatIsBelievableIsKeptAndPlaced()
        {
            PairingOverrides older = Older(BeamKey("B1"), 501);

            EndpointCompareEngine engine = Engine(older);
            engine.StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000) });

            Assert.AreEqual("B1->501", Pairs(engine).Single());
            Assert.IsTrue(older.TakeChanged(), "its places are new, so the file needs saving");
            Assert.IsTrue(older.PlacesToJson().ContainsKey(Project));
        }

        /// <summary>
        /// A record of places made for a pairing that has since been set to something else - by
        /// a build that did not keep places - is not about the pairing there now.
        /// </summary>
        [TestMethod]
        public void PlacesRecordedForAnotherRevitElementAreIgnored()
        {
            PairingOverrides written = new PairingOverrides();
            written.Set(Project, BeamKey("B1"), 501);
            Engine(written).StartComparison(
                new List<FrameData_r> { RevitBeam(501, "L01", 0, 800, 6000, 800, 3000) },
                new List<FrameData_e> { EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000) });

            Dictionary<string, object> pairs = new Dictionary<string, object>
            {
                { Project, new Dictionary<string, object> { { BeamKey("B1"), 777L } } }
            };
            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { PairingOverrides.Branch, pairs },
                { PairingOverrides.PlacesBranch, written.PlacesToJson() }
            };

            PairingOverrides read = new PairingOverrides();
            read.ReadFrom(Json.Read(Json.Write(view)));

            Assert.AreEqual(0, read.PlacesToJson().Count);
        }

        #endregion

        #region accepted and reviewed

        /// <summary>
        /// A beam's acceptance is held to where it stands and how big it is in both models, so
        /// moving it or resizing it in either one leaves the acceptance stale.
        /// </summary>
        [TestMethod]
        public void ABeamsFingerprintChangesWhenEitherModelMovesOrResizesIt()
        {
            string asAccepted = Fingerprint(EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000, 300, 600),
                                            RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 600));

            Assert.AreNotEqual(asAccepted,
                Fingerprint(EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000, 300, 600),
                            RevitBeam(501, "L01", 0, 200, 6000, 200, 3000, 300, 600)),
                "moved in Revit");
            Assert.AreNotEqual(asAccepted,
                Fingerprint(EtabsBeam("B1", "L01", 0, 0, 6500, 0, 3000, 300, 600),
                            RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 600)),
                "made longer in ETABS");
            Assert.AreNotEqual(asAccepted,
                Fingerprint(EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000, 300, 600),
                            RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 750)),
                "made deeper in Revit");
            Assert.AreNotEqual(asAccepted,
                Fingerprint(EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000, 400, 600),
                            RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 600)),
                "made wider in ETABS");

            Assert.AreEqual(asAccepted,
                Fingerprint(EtabsBeam("B1", "L01", 6000, 0, 0, 0, 3000, 300, 600),
                            RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 600)),
                "drawn from its other end is the same beam");
        }

        [TestMethod]
        public void RealigningTheModelsLeavesABeamsFingerprintAlone()
        {
            FrameData_e etabs = EtabsBeam("B1", "L01", 0, 0, 6000, 0, 3000, 300, 600);
            FrameData_r revit = RevitBeam(501, "L01", 0, 0, 6000, 0, 3000, 300, 600);
            string before = Fingerprint(etabs, revit);

            MyTransformation moved = new MyTransformation();
            moved.SetAlignment(new MyPoint(150, -90, 0), 0.5);
            revit.MoveBy(MyTransformation.Difference(moved, DataContainer.ModelTransformation));
            DataContainer.ModelTransformation = moved;

            Assert.AreEqual(before, Fingerprint(etabs, revit));
        }

        /// <summary>
        /// Accepting says a difference is real and agreed. An element that agrees has nothing
        /// to accept - and one that has come to agree since it was accepted is not asking for
        /// another look.
        /// </summary>
        /// <summary>
        /// A failure an element carries is something to accept even where its overall verdict
        /// says otherwise, so no kind of element can have ACCEPT greyed out over a failure -
        /// which is what a piece of the slab load cut once had. A result that passed, or one
        /// never checked, still gives nothing to accept.
        /// </summary>
        [TestMethod]
        public void AFailureCarriedIsSomethingToAcceptWhateverTheVerdictSays()
        {
            ElementViewModel failing = Beam(CheckState.NotChecked, "E0,0,3000;6000,0,3000@300x600",
                new CheckOutcome(CheckKind.Beam, ResultLevel.Failed, "top steel short"));
            ElementViewModel looking = Beam(CheckState.Passed, "E0,0,3000;6000,0,3000@300x600",
                new CheckOutcome(CheckKind.Beam, ResultLevel.Review, "worth a look"));
            ElementViewModel fine = Beam(CheckState.NotChecked, "E0,0,3000;6000,0,3000@300x600",
                new CheckOutcome(CheckKind.Beam, ResultLevel.Passed, "as designed"),
                new CheckOutcome(CheckKind.Pairing, ResultLevel.NotChecked, "not checked"));

            Assert.IsTrue(CheckNaming.Disagrees(failing));
            Assert.IsTrue(CheckNaming.Disagrees(looking));
            Assert.IsFalse(CheckNaming.Disagrees(fine));
        }

        [TestMethod]
        public void AnElementThatAgreesHasNothingToAcceptAndNothingStale()
        {
            ElementViewModel agrees = Beam(CheckState.Passed, "E0,0,3000;6000,0,3000@300x600");
            ElementViewModel differs = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600");

            Assert.IsFalse(CheckNaming.Disagrees(agrees));
            Assert.IsTrue(CheckNaming.Disagrees(differs));
            Assert.IsFalse(new ElementCard(agrees, null).CanAccept, "no accept offered on one that agrees");
            Assert.IsTrue(new ElementCard(differs, null).CanAccept);

            ReviewNote acceptedAsIs = new ReviewNote
            {
                Verdict = HumanVerdict.Accepted,
                Fingerprint = agrees.VerdictFingerprint
            };
            Assert.AreEqual(HumanVerdict.None, CheckNaming.VerdictOn(acceptedAsIs, agrees),
                            "an acceptance of an element that agrees settles nothing");

            ReviewNote acceptedElsewhere = new ReviewNote
            {
                Verdict = HumanVerdict.Accepted,
                Fingerprint = "somewhere else"
            };
            Assert.IsFalse(CheckNaming.IsStale(acceptedElsewhere, agrees),
                           "it agrees now, so there is nothing to look at again");
            Assert.IsTrue(CheckNaming.IsStale(acceptedElsewhere, differs),
                          "one that still differs is asked about again");
        }

        /// <summary>
        /// A beam accepted, then moved or resized in either model: the acceptance is removed,
        /// not left standing as stale.
        /// </summary>
        [TestMethod]
        public void AnAcceptedBeamThatMovedHasItsAcceptanceTakenBack()
        {
            ElementViewModel then = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600 R0,0,3000;6000,0,3000@300x600");
            ReviewNote accepted = new ReviewNote { Verdict = HumanVerdict.Accepted, Fingerprint = then.VerdictFingerprint };

            ElementViewModel moved = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600 R0,200,3000;6000,200,3000@300x600");
            Assert.IsNotNull(CheckNaming.WhyAcceptanceIsOvertaken(accepted, moved));

            Assert.IsNull(CheckNaming.WhyAcceptanceIsOvertaken(accepted, then), "where it was, it stands");
        }

        /// <summary>
        /// Accepted while it had no counterpart, and paired since: there is a value to check
        /// now that nobody has looked at, so the acceptance goes.
        /// </summary>
        [TestMethod]
        public void AnAcceptanceMadeWhileUnpairedIsTakenBackOnceItIsPaired()
        {
            ElementViewModel unpaired = LineLoad(CheckState.Unpaired,
                new CheckOutcome(CheckKind.LineLoadMissingInEtabs, ResultLevel.Failed));
            ReviewNote accepted = new ReviewNote
            {
                Verdict = HumanVerdict.Accepted,
                Fingerprint = unpaired.VerdictFingerprint
            };

            Assert.IsTrue(CheckNaming.SaidUnpaired(accepted.Fingerprint));
            Assert.IsNull(CheckNaming.WhyAcceptanceIsOvertaken(accepted, unpaired),
                          "still unpaired, so still what was accepted");

            ElementViewModel paired = LineLoad(CheckState.Failed,
                new CheckOutcome(CheckKind.LineLoads, ResultLevel.Failed));
            StringAssert.Contains(CheckNaming.WhyAcceptanceIsOvertaken(accepted, paired), "paired now");
        }

        /// <summary>
        /// Any other change to the results leaves an acceptance stale rather than gone: a check
        /// switched off under Set up changes the results too, and must not cost anybody what
        /// they agreed.
        /// </summary>
        [TestMethod]
        public void AResultChangeAloneLeavesTheAcceptanceStaleRatherThanGone()
        {
            ElementViewModel then = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600",
                                         new CheckOutcome(CheckKind.Beam, ResultLevel.Failed, token: "TopLeft"));
            ReviewNote accepted = new ReviewNote { Verdict = HumanVerdict.Accepted, Fingerprint = then.VerdictFingerprint };

            ElementViewModel now = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600",
                                        new CheckOutcome(CheckKind.Beam, ResultLevel.Failed, token: "BotMid"));

            Assert.IsNull(CheckNaming.WhyAcceptanceIsOvertaken(accepted, now));
            Assert.IsTrue(CheckNaming.IsStale(accepted, now));
        }

        /// <summary>An element read but not compared yet has nothing an acceptance could be held to.</summary>
        [TestMethod]
        public void NothingIsTakenBackOffAnElementNotYetChecked()
        {
            ElementViewModel then = Beam(CheckState.Failed, "E0,0,3000;6000,0,3000@300x600 R0,0,3000;6000,0,3000@300x600");
            ReviewNote accepted = new ReviewNote { Verdict = HumanVerdict.Accepted, Fingerprint = then.VerdictFingerprint };

            ElementViewModel imported = Beam(CheckState.NotChecked, "E0,0,3000;6000,0,3000@300x600",
                                             new CheckOutcome(CheckKind.Pairing, ResultLevel.NotChecked));

            Assert.IsNull(CheckNaming.WhyAcceptanceIsOvertaken(accepted, imported));
            Assert.IsFalse(CheckNaming.SaidUnpaired(imported.VerdictFingerprint),
                           "not compared yet is not the same as found to have no counterpart");
        }

        #endregion

        #region building the inputs

        private static ElementViewModel LineLoad(CheckState state, params CheckOutcome[] outcomes)
        {
            return new LineLoadElementViewModel(ElementSource.RevitOnly, state, "line 17132", "L01",
                                                new List<ResultField>(),
                                                new[] { new MyPoint(0, 0, 3000), new MyPoint(6000, 0, 3000) })
            {
                RevitId = 17132,
                Outcomes = outcomes.ToList()
            };
        }

        private static string BeamKey(string etabsName)
        {
            return PairingOverrides.KeyOf(ElementKindKey.Beam, etabsName);
        }

        private static EndpointCompareEngine Engine(PairingOverrides overrides)
        {
            return new EndpointCompareEngine { Overrides = overrides, ProjectKey = Project };
        }

        private static List<string> Pairs(EndpointCompareEngine engine)
        {
            return engine.PairedFrames
                         .Select(pair => pair.etabsFrame.EtabsUname + "->" + pair.revitFrame.RevitID)
                         .ToList();
        }

        /// <summary>A store as a file from before places were kept reads back: the ids and nothing else.</summary>
        private static PairingOverrides Older(string key, long revitId)
        {
            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { PairingOverrides.Branch, new Dictionary<string, object>
                    {
                        { Project, new Dictionary<string, object> { { key, revitId } } }
                    }
                }
            };

            PairingOverrides read = new PairingOverrides();
            read.ReadFrom(Json.Read(Json.Write(view)));
            return read;
        }

        private static PairingOverrides ThroughTheFile(PairingOverrides written)
        {
            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { PairingOverrides.Branch, written.ToJson() },
                { PairingOverrides.PlacesBranch, written.PlacesToJson() }
            };

            PairingOverrides read = new PairingOverrides();
            read.ReadFrom(Json.Read(Json.Write(view)));
            return read;
        }

        private static string Fingerprint(FrameData_e etabs, FrameData_r revit)
        {
            return ElementViewModelFactory.FromPairedFrame(new PairedFrame(etabs, revit)).PlaceFingerprint;
        }

        private static ElementViewModel Beam(CheckState state, string place, params CheckOutcome[] outcomes)
        {
            return new BeamElementViewModel(ElementSource.Paired, state, "B1", "L01",
                                            new List<ResultField>(),
                                            new MyPoint(0, 0, 3000), new MyPoint(6000, 0, 3000))
            {
                EtabsUniqueName = "B1",
                PlaceFingerprint = place,
                Outcomes = outcomes.ToList()
            };
        }

        private static FrameData_e EtabsBeam(string name, string story, double x1, double y1,
                                             double x2, double y2, double z,
                                             double width = 300, double depth = 600)
        {
            FrameData_e frame = new FrameData_e(name) { Story = story };
            frame.Start_e = new MyPoint(x1, y1, z);
            frame.END_e = new MyPoint(x2, y2, z);
            frame._width = width;
            frame._depth = depth;
            return frame;
        }

        /// <summary>In mm here, handed over in feet the way the importer hands it over.</summary>
        private static FrameData_r RevitBeam(long revitId, string story, double x1, double y1,
                                             double x2, double y2, double z,
                                             double width = 300, double depth = 600)
        {
            MyPoint start = new MyPoint(x1 / FeetToMM, y1 / FeetToMM, z / FeetToMM);
            MyPoint end = new MyPoint(x2 / FeetToMM, y2 / FeetToMM, z / FeetToMM);

            FrameData_r frame = new FrameData_r(revitId, start, end, start.DistanceTo(end),
                                                new MyPoint(1, 0, 0))
            {
                Story = story
            };
            frame._width = width;
            frame._depth = depth;
            return frame;
        }

        #endregion
    }
}
