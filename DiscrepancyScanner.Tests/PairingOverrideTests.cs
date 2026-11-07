using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The pairs a person sets by hand, and whether they survive the next run.
    ///
    /// That is the whole point of the store: the automatic pass is right nearly always and
    /// wrong in exactly the cases worth arguing about, and a decision someone made by looking
    /// at the model must not have to be made again after every check.
    /// </summary>
    [TestClass]
    public class PairingOverrideTests
    {
        private const string Project = @"C:\models\tower.rvt";

        /// <summary>
        /// The buffers these tests are written against. The preferences are a singleton the
        /// whole tool shares, so each test states what it depends on rather than trusting
        /// whatever ran before it.
        /// </summary>
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.ColumnLocationTolerance = 250;
            pref.ColumnSearchRadius = 1000;
        }

        #region the store itself

        [TestMethod]
        public void APairingSurvivesBeingWrittenOutAndReadBackIn()
        {
            PairingOverrides written = new PairingOverrides();
            written.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"), 1284501);
            written.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Beam, "B-4"),
                        PairingOverrides.Unpaired);

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { PairingOverrides.Branch, written.ToJson() }
            };

            // Through the real serialiser, because a store that only round-trips in memory is
            // one that quietly loses its contents the first time the tool is closed.
            Dictionary<string, object> reread = Json.Read(Json.Write(view));

            PairingOverrides read = new PairingOverrides();
            read.ReadFrom(reread);

            Assert.AreEqual(1284501L,
                read.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17")));
            Assert.AreEqual(PairingOverrides.Unpaired,
                read.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Beam, "B-4")),
                "paired with nothing on purpose is an answer, and has to come back as one");
            Assert.IsNull(read.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-99")),
                "no opinion is not the same as paired with nothing");
        }

        [TestMethod]
        public void TheSameNameInTwoTablesIsTwoDifferentPairings()
        {
            // ETABS unique names are only unique within their own table, so a column and a beam
            // can both be called "17". Without the kind in the key one would overwrite the
            // other and a beam would silently take a column's pairing.
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "17"), 100);
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Beam, "17"), 200);

            Assert.AreEqual(100L, overrides.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "17")));
            Assert.AreEqual(200L, overrides.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Beam, "17")));
        }

        [TestMethod]
        public void PairingsAreKeptPerProject()
        {
            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"), 100);

            Assert.IsNull(overrides.Find(@"C:\models\podium.rvt",
                                         PairingOverrides.KeyOf(ElementKindKey.Column, "C-17")),
                          "another project's decision is not this one's");
        }

        #endregion

        #region what the comparison does with them

        /// <summary>
        /// The case the store exists for: two Revit columns near one ETABS column, the nearest
        /// is not the right one, and someone says so.
        ///
        /// The assertion that matters is the second one. Pinning the far column must not leave
        /// the near one paired as well - the override has to take both sides out of the pool
        /// rather than merely being preferred, or the run comes back with the same column
        /// counted twice.
        /// </summary>
        [TestMethod]
        public void APairingSetByHandBeatsTheNearestAndLeavesItUnmatched()
        {
            Column_e etabs = EtabsColumn("C-17", 0, 0);
            Column_r near = RevitColumn(500, 100, 0);
            Column_r far = RevitColumn(900, 200, 0);

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"), 900);

            ColumnCompareEngine engine = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            engine.StartComparison(new List<Column_e> { etabs },
                                   new List<Column_r> { near, far });

            Assert.AreEqual(1, engine.PairedColumns.Count);
            Assert.AreEqual(900L, engine.PairedColumns[0].revitColumn.RevitID,
                            "the column that was pinned is the one that got paired");

            Assert.IsTrue(engine.UnmatchedRevitColumns.Any(c => c.RevitID == 500),
                          "the nearer column was passed over, so it is unmatched rather than "
                          + "quietly paired with something else as well");
        }

        /// <summary>
        /// Paired with nothing, on purpose. The element is reported unmatched and no later pass
        /// is allowed to pair it after all.
        /// </summary>
        [TestMethod]
        public void APairingSetToNothingLeavesTheElementUnmatched()
        {
            Column_e etabs = EtabsColumn("C-17", 0, 0);
            Column_r near = RevitColumn(500, 50, 0);

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"),
                          PairingOverrides.Unpaired);

            ColumnCompareEngine engine = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            engine.StartComparison(new List<Column_e> { etabs }, new List<Column_r> { near });

            Assert.AreEqual(0, engine.PairedColumns.Count,
                            "it was said to pair with nothing, and 50mm away is still nothing");
            Assert.AreEqual(1, engine.UnmatchedEtabsColumns.Count);
            Assert.AreEqual(1, engine.UnmatchedRevitColumns.Count,
                            "and the Revit column it was not paired with is free, not consumed");
        }

        /// <summary>
        /// A pairing naming a Revit element that is not in the model this time is skipped rather
        /// than obeyed, and the automatic pass decides that element as it always would.
        ///
        /// It stays in the store: a column deleted for one run and put back the next should not
        /// cost the decision that was made about it.
        /// </summary>
        [TestMethod]
        public void APairingNamingSomethingAbsentFallsBackToTheAutomaticPass()
        {
            Column_e etabs = EtabsColumn("C-17", 0, 0);
            Column_r near = RevitColumn(500, 100, 0);

            PairingOverrides overrides = new PairingOverrides();
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"), 7777);

            ColumnCompareEngine engine = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            engine.StartComparison(new List<Column_e> { etabs }, new List<Column_r> { near });

            Assert.AreEqual(1, engine.PairedColumns.Count);
            Assert.AreEqual(500L, engine.PairedColumns[0].revitColumn.RevitID);
            Assert.AreEqual(7777L,
                overrides.Find(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17")),
                "the pairing is kept, in case the element comes back");
        }

        /// <summary>With no store handed in, the engine pairs by distance exactly as it did.</summary>
        [TestMethod]
        public void WithNoStoreThePairingIsExactlyWhatItAlwaysWas()
        {
            Column_e etabs = EtabsColumn("C-17", 0, 0);
            Column_r near = RevitColumn(500, 100, 0);
            Column_r far = RevitColumn(900, 200, 0);

            ColumnCompareEngine engine = new ColumnCompareEngine();
            engine.StartComparison(new List<Column_e> { etabs },
                                   new List<Column_r> { near, far });

            Assert.AreEqual(1, engine.PairedColumns.Count);
            Assert.AreEqual(500L, engine.PairedColumns[0].revitColumn.RevitID,
                            "the nearest one, as before");
        }

        /// <summary>
        /// What lets a hand-set pairing skip the rest of the check.
        ///
        /// Setting a pairing on the card runs that one kind's pass again and leaves the beams,
        /// the walls and - the slow one - the whole slab mesh standing as the last run left
        /// them. That is only honest if the pass reads nothing but its own inputs and the
        /// store: pairing again after an override must come back with exactly what a run from
        /// scratch with that override would have said, carrying nothing over from the pass
        /// that ran before it.
        /// </summary>
        [TestMethod]
        public void PairingAgainAfterAnOverrideSaysWhatAWholeRunWouldHaveSaid()
        {
            List<Column_e> etabs = new List<Column_e>
            {
                EtabsColumn("C-17", 0, 0),
                EtabsColumn("C-18", 3000, 0)
            };
            List<Column_r> revit = new List<Column_r>
            {
                RevitColumn(500, 100, 0),
                RevitColumn(900, 200, 0),
                RevitColumn(1300, 3100, 0)
            };

            // The run as it happened: nobody had said anything yet.
            PairingOverrides overrides = new PairingOverrides();
            ColumnCompareEngine first = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            first.StartComparison(etabs, revit);
            Assert.AreEqual(500L, first.PairedColumns.Single(c => c.etabsColumn.EtabsUniqueName == "C-17")
                                       .revitColumn.RevitID,
                            "the nearest, which is the pairing about to be corrected");

            // Somebody corrects one column on the card, and only the column pass runs again.
            overrides.Set(Project, PairingOverrides.KeyOf(ElementKindKey.Column, "C-17"), 900);
            ColumnCompareEngine again = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            again.StartComparison(etabs, revit);

            // The whole run, had they pressed the check button instead.
            ColumnCompareEngine whole = new ColumnCompareEngine
            {
                Overrides = overrides,
                ProjectKey = Project
            };
            whole.StartComparison(etabs, revit);

            CollectionAssert.AreEqual(
                whole.PairedColumns.Select(Pairing).ToList(),
                again.PairedColumns.Select(Pairing).ToList(),
                "the same pairs, in the same order");
            CollectionAssert.AreEqual(
                whole.UnmatchedEtabsColumns.Select(c => c.EtabsUniqueName).ToList(),
                again.UnmatchedEtabsColumns.Select(c => c.EtabsUniqueName).ToList(),
                "and the same ETABS columns left over");
            CollectionAssert.AreEqual(
                whole.UnmatchedRevitColumns.Select(c => c.RevitID).ToList(),
                again.UnmatchedRevitColumns.Select(c => c.RevitID).ToList(),
                "and the same Revit columns left over - the one that was passed over included");
        }

        /// <summary>One pair, as a string, so two runs can be compared as a whole.</summary>
        private static string Pairing(PairedColumn pair)
        {
            return pair.etabsColumn.EtabsUniqueName + "->" + pair.revitColumn.RevitID;
        }

        #endregion

        #region reviews and notes

        /// <summary>
        /// A review is recorded against the verdicts it was made about, and comes back only
        /// while those are still what the element says.
        /// </summary>
        [TestMethod]
        public void AReviewSurvivesBeingWrittenOutAndReadBackIn()
        {
            ReviewNotes written = new ReviewNotes();
            written.SetVerdict(Project, "Column|C-17", HumanVerdict.Reviewed, "2:1,3:0");
            written.SetNote(Project, "Column|C-17", "chase with GD");

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { ReviewNotes.Branch, written.ToJson() }
            };

            ReviewNotes read = new ReviewNotes();
            read.ReadFrom(Json.Read(Json.Write(view)));

            ReviewNote said = read.Find(Project, "Column|C-17");
            Assert.IsNotNull(said);
            Assert.IsTrue(said.Reviewed);
            Assert.AreEqual("chase with GD", said.Note);
            Assert.AreEqual("2:1,3:0", said.Fingerprint,
                            "what it was reviewed against has to come back too, or staleness "
                            + "cannot be worked out at all");
        }

        /// <summary>
        /// The rule the whole thing turns on: a review says "I looked at this and it is fine",
        /// which is only true of the thing that was looked at. If a later run changes any
        /// verdict on the element, carrying the review forward as clean would mark a finding
        /// nobody has seen as already handled.
        /// </summary>
        [TestMethod]
        public void AReviewGoesStaleWhenTheVerdictsChange()
        {
            ReviewNotes notes = new ReviewNotes();
            notes.SetVerdict(Project, "Column|C-17", HumanVerdict.Reviewed, "2:1");

            ReviewNote said = notes.Find(Project, "Column|C-17");

            Assert.AreEqual("2:1", said.Fingerprint);
            Assert.AreNotEqual("2:0", said.Fingerprint,
                "a different set of verdicts is a different thing, and the card reads that as "
                + "wanting another look rather than as reviewed");
        }

        /// <summary>
        /// Taking a review back clears what it was made against as well. Leaving the old
        /// fingerprint behind would let a later re-review look like it had been made against
        /// verdicts nobody checked it against.
        /// </summary>
        [TestMethod]
        public void TakingAReviewBackClearsWhatItWasMadeAgainst()
        {
            ReviewNotes notes = new ReviewNotes();
            notes.SetVerdict(Project, "Column|C-17", HumanVerdict.Reviewed, "2:1");
            notes.SetNote(Project, "Column|C-17", "keep this");
            notes.SetVerdict(Project, "Column|C-17", HumanVerdict.None, null);

            ReviewNote said = notes.Find(Project, "Column|C-17");
            Assert.IsFalse(said.Reviewed);
            Assert.AreEqual("", said.Fingerprint);
            Assert.AreEqual("keep this", said.Note,
                            "the note is a separate thing and outlives the review");
        }

        /// <summary>
        /// An element nobody says anything about any more leaves nothing behind, so the file
        /// does not fill up with the ghosts of reviews that were taken back.
        /// </summary>
        [TestMethod]
        public void AnElementWithNothingLeftToSayIsNotWrittenAtAll()
        {
            ReviewNotes notes = new ReviewNotes();
            notes.SetVerdict(Project, "Column|C-17", HumanVerdict.Reviewed, "2:1");
            notes.SetVerdict(Project, "Column|C-17", HumanVerdict.None, null);

            Assert.AreEqual(0, notes.ToJson().Count);
        }

        /// <summary>
        /// Accepted is a different statement from reviewed, and the file has to keep them
        /// apart: one says somebody looked, the other says the difference is real and agreed.
        /// </summary>
        [TestMethod]
        public void AcceptedAndReviewedAreKeptApart()
        {
            ReviewNotes written = new ReviewNotes();
            written.SetVerdict(Project, "Column|C-17", HumanVerdict.Reviewed, "2:1");
            written.SetVerdict(Project, "Wall|W-4", HumanVerdict.Accepted, "8:1");

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { ReviewNotes.Branch, written.ToJson() }
            };

            ReviewNotes read = new ReviewNotes();
            read.ReadFrom(Json.Read(Json.Write(view)));

            Assert.AreEqual(HumanVerdict.Reviewed, read.Find(Project, "Column|C-17").Verdict);

            ReviewNote accepted = read.Find(Project, "Wall|W-4");
            Assert.AreEqual(HumanVerdict.Accepted, accepted.Verdict);
            Assert.IsTrue(accepted.Accepted, "accepted is settled");
            Assert.IsTrue(accepted.Reviewed, "and accepting something means having looked at it");
        }

        /// <summary>
        /// An accepted difference goes stale the same way a review does. Somebody agreeing that
        /// a slab is 100 thicker in Revit says nothing about the same slab once it is 300
        /// thicker, and carrying the agreement forward would settle a difference nobody has
        /// seen.
        /// </summary>
        [TestMethod]
        public void AnAcceptedDifferenceGoesStaleWhenTheVerdictsChange()
        {
            ReviewNotes notes = new ReviewNotes();
            notes.SetVerdict(Project, "Slab|S-1", HumanVerdict.Accepted, "10:1");

            ReviewNote said = notes.Find(Project, "Slab|S-1");
            Assert.AreEqual("10:1", said.Fingerprint,
                "an accepted difference records what it was accepted against, exactly as a "
                + "review does");
        }

        /// <summary>
        /// A file written before there was anything to say but "reviewed" still reads as a
        /// review rather than as nothing said at all.
        /// </summary>
        [TestMethod]
        public void AnOlderFileWithABareReviewedFlagStillReads()
        {
            Dictionary<string, object> view = new Dictionary<string, object>
            {
                { ReviewNotes.Branch, new Dictionary<string, object>
                    {
                        { Project, new Dictionary<string, object>
                            {
                                { "Column|C-17", new Dictionary<string, object>
                                    {
                                        { "reviewed", true },
                                        { "note", "" },
                                        { "against", "2:1" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            ReviewNotes read = new ReviewNotes();
            read.ReadFrom(Json.Read(Json.Write(view)));

            Assert.AreEqual(HumanVerdict.Reviewed, read.Find(Project, "Column|C-17").Verdict);
        }

        #endregion

        #region forgetting what was decided by hand

        /// <summary>
        /// Forgetting is done one project at a time. A reset on one job that quietly took the
        /// pairings of another with it would be found out months later, on the other job.
        /// </summary>
        [TestMethod]
        public void ForgettingPairingsTouchesOnlyThatProject()
        {
            PairingOverrides store = new PairingOverrides();
            store.Set("tower.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B1"), 5501);
            store.Set("tower.rvt", PairingOverrides.KeyOf(ElementKindKey.Column, "C1"), PairingOverrides.Unpaired);
            store.Set("podium.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B1"), 7702);

            Assert.AreEqual(2, store.ClearProject("tower.rvt"), "\"paired with nothing\" is a decision too");

            Assert.AreEqual(0, store.CountFor("tower.rvt"));
            Assert.AreEqual(7702L, store.Find("podium.rvt", PairingOverrides.KeyOf(ElementKindKey.Beam, "B1")));
            Assert.AreEqual(0, store.ClearProject("nowhere.rvt"));
        }

        /// <summary>
        /// Every decision goes, accepted and reviewed alike, and the notes stay: a note is what
        /// somebody wrote about the element, and it is still worth reading once the decision
        /// it came with has been taken back.
        /// </summary>
        [TestMethod]
        public void ForgettingDecisionsKeepsTheNotes()
        {
            ReviewNotes store = new ReviewNotes();
            store.SetVerdict("tower.rvt", "Beam|B1", HumanVerdict.Accepted, "f1");
            store.SetNote("tower.rvt", "Beam|B1", "screed not in the analysis");
            store.SetVerdict("tower.rvt", "Beam|B2", HumanVerdict.Reviewed, "f2");
            store.SetVerdict("podium.rvt", "Beam|B1", HumanVerdict.Accepted, "f3");

            Assert.AreEqual(2, store.ClearVerdicts("tower.rvt"));

            Assert.AreEqual(0, store.CountFor("tower.rvt", HumanVerdict.Accepted));
            Assert.AreEqual(0, store.CountFor("tower.rvt", HumanVerdict.Reviewed));
            Assert.AreEqual("screed not in the analysis", store.Find("tower.rvt", "Beam|B1").Note);
            Assert.IsFalse(store.Find("tower.rvt", "Beam|B1").Reviewed);
            Assert.IsNull(store.Find("tower.rvt", "Beam|B2"), "nothing left to keep");
            Assert.AreEqual(1, store.CountFor("podium.rvt", HumanVerdict.Accepted), "another project is left alone");
        }

        /// <summary>
        /// The acceptances can be forgotten on their own, and what was only marked reviewed
        /// stays reviewed - they are separate decisions and the forget button asks about each.
        /// </summary>
        [TestMethod]
        public void ForgettingTheAcceptancesLeavesTheReviews()
        {
            ReviewNotes store = new ReviewNotes();
            store.SetVerdict("tower.rvt", "Beam|B1", HumanVerdict.Accepted, "f1");
            store.SetVerdict("tower.rvt", "Beam|B2", HumanVerdict.Reviewed, "f2");
            store.SetNote("tower.rvt", "Beam|B1", "screed not in the analysis");

            Assert.AreEqual(1, store.ClearVerdicts("tower.rvt", HumanVerdict.Accepted));

            Assert.AreEqual(0, store.CountFor("tower.rvt", HumanVerdict.Accepted));
            Assert.AreEqual(1, store.CountFor("tower.rvt", HumanVerdict.Reviewed));
            Assert.AreEqual("screed not in the analysis", store.Find("tower.rvt", "Beam|B1").Note);
        }

        /// <summary>
        /// The notes can be forgotten too, and the decisions they were written beside stay. An
        /// entry that was only ever a note has nothing left, and goes.
        /// </summary>
        [TestMethod]
        public void ForgettingTheNotesKeepsTheDecisions()
        {
            ReviewNotes store = new ReviewNotes();
            store.SetVerdict("tower.rvt", "Beam|B1", HumanVerdict.Accepted, "f1");
            store.SetNote("tower.rvt", "Beam|B1", "screed not in the analysis");
            store.SetNote("tower.rvt", "Beam|B3", "ask the architect");
            store.SetNote("podium.rvt", "Beam|B1", "another job's note");

            Assert.AreEqual(2, store.CountNotes("tower.rvt"));
            Assert.AreEqual(2, store.ClearNotes("tower.rvt"));

            Assert.AreEqual(0, store.CountNotes("tower.rvt"));
            Assert.AreEqual(HumanVerdict.Accepted, store.Find("tower.rvt", "Beam|B1").Verdict);
            Assert.IsNull(store.Find("tower.rvt", "Beam|B3"), "nothing left to keep");
            Assert.AreEqual(1, store.CountNotes("podium.rvt"), "another project is left alone");
        }

        [TestMethod]
        public void ForgettingTheLoadingPlansTouchesOnlyThatProject()
        {
            LoadingPlanLinks links = new LoadingPlanLinks();
            links.Set("tower.rvt", new LoadingPlanLink { Story = "L03", ViewId = 1000 });
            links.Set("tower.rvt", new LoadingPlanLink { Story = "L04", ViewId = 1001 });
            links.Set("podium.rvt", new LoadingPlanLink { Story = "P01", ViewId = 2000 });

            Assert.AreEqual(2, links.ClearProject("tower.rvt"));

            Assert.AreEqual(0, links.CountFor("tower.rvt"));
            Assert.AreEqual(1, links.CountFor("podium.rvt"));
            Assert.AreEqual(0, links.ClearProject("nowhere.rvt"));
        }

        /// <summary>
        /// The forget dialog starts with nothing ticked, so whatever is forgotten was chosen,
        /// and a kind with nothing to forget cannot be ticked at all.
        /// </summary>
        [TestMethod]
        public void NothingIsForgottenUntilItIsTicked()
        {
            ForgetChoices choices = new ForgetChoices(acceptances: 3, reviews: 0, notes: 2,
                                                      handPairings: 1, loadingPlans: 4);

            Assert.IsFalse(choices.CanForget);
            Assert.AreEqual(false, choices.Everything);

            ForgetOption reviews = choices.Options.Single(option => option.What == ForgetWhat.Reviews);
            reviews.IsSelected = true;
            Assert.IsFalse(reviews.IsSelected, "no reviews on this project, so nothing to tick");
            Assert.IsFalse(choices.CanForget);

            choices.Options.Single(option => option.What == ForgetWhat.Acceptances).IsSelected = true;
            Assert.IsTrue(choices.CanForget);
            Assert.IsNull(choices.Everything, "some ticked reads as neither all nor none");
            Assert.IsTrue(choices.IsChosen(ForgetWhat.Acceptances));
            Assert.IsFalse(choices.IsChosen(ForgetWhat.LoadingPlans), "the loading plans were not ticked");
            Assert.AreEqual("FORGET", choices.ForgetButtonText);
        }

        /// <summary>Everything ticks every kind there is something of, and the button says so.</summary>
        [TestMethod]
        public void EverythingTicksEveryKindThereIsSomethingOf()
        {
            ForgetChoices choices = new ForgetChoices(acceptances: 3, reviews: 0, notes: 2,
                                                      handPairings: 1, loadingPlans: 4);

            choices.Everything = true;

            Assert.AreEqual(true, choices.Everything);
            Assert.IsTrue(choices.IsChosen(ForgetWhat.LoadingPlans));
            Assert.IsTrue(choices.IsChosen(ForgetWhat.Notes));
            Assert.IsFalse(choices.IsChosen(ForgetWhat.Reviews), "there were none");
            Assert.AreEqual("FORGET EVERYTHING", choices.ForgetButtonText);

            choices.Everything = false;
            Assert.IsFalse(choices.CanForget);
        }

        #endregion

        #region building the inputs

        private const double FeetToMM = 304.8d;

        private static Column_e EtabsColumn(string uniqueName, double xMM, double yMM)
        {
            FrameData_e frame = new FrameData_e(uniqueName)
            {
                Start = new MyPoint { X = xMM, Y = yMM, Z = 0 },
                End = new MyPoint { X = xMM, Y = yMM, Z = 3000 }
            };
            return new Column_e(frame);
        }

        /// <summary>
        /// Revit geometry arrives in feet and is brought into ETABS millimetres by the shared
        /// transformation, so the test hands over feet the same way the importer does.
        /// </summary>
        private static Column_r RevitColumn(long revitId, double xMM, double yMM)
        {
            return new Column_r(revitId,
                new MyPoint { X = xMM / FeetToMM, Y = yMM / FeetToMM, Z = 0 }, 0);
        }

        #endregion
    }
}
