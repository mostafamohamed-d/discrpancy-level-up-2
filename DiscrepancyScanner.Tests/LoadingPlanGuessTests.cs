using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The rule behind "guess the rest by name".
    ///
    /// Pairing forty-seven levels by hand is an afternoon nobody should spend, so the tool
    /// offers to do it from the view names. That offer is only worth taking if it cannot pair
    /// the wrong plan: reading the loads for the tenth floor off the first floor's plan is a
    /// mistake that would never be noticed, because every number in it looks reasonable.
    /// </summary>
    [TestClass]
    public class LoadingPlanGuessTests
    {
        [TestMethod]
        public void AViewNamedAfterTheLevelMatches()
        {
            Assert.IsTrue(LoadingPlansViewModel.NamesTheLevel("L03 - LOADING PLAN", "L03"));
            Assert.IsTrue(LoadingPlansViewModel.NamesTheLevel("LOADING PLAN (L03)", "L03"));
            Assert.IsTrue(LoadingPlansViewModel.NamesTheLevel("l03 loading plan", "L03"),
                          "case is not what tells two levels apart");
        }

        /// <summary>
        /// The case the whole rule exists for. "L1" appears inside "L10", so a plain substring
        /// test would pair the first floor with the tenth floor's plan.
        /// </summary>
        [TestMethod]
        public void ALevelNameInsideALongerOneDoesNotMatch()
        {
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel("L10 - LOADING PLAN", "L1"),
                           "L1 must not take L10's plan");
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel("SL1 - LOADING PLAN", "L1"),
                           "nor a name that merely ends with it");
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel("L1A - LOADING PLAN", "L1"));
        }

        [TestMethod]
        public void PunctuationAndSpacesStillCountAsABoundary()
        {
            Assert.IsTrue(LoadingPlansViewModel.NamesTheLevel("PODIUM_L1_LOADING", "L1"),
                          "an underscore separates names as well as a space does");
            Assert.IsTrue(LoadingPlansViewModel.NamesTheLevel("L1-LOADING", "L1"));
        }

        /// <summary>
        /// The phrase is a requirement: a floor whose only view is its general arrangement plan
        /// is left unpaired rather than read off the wrong drawing.
        /// </summary>
        [TestMethod]
        public void OnlyViewsCarryingThePhraseAreOffered()
        {
            LoadingPlanView[] views =
            {
                new LoadingPlanView(1, "L01 GA PLAN", "FloorPlan"),
                new LoadingPlanView(2, "L01 LOADING PLAN", "FloorPlan"),
                new LoadingPlanView(3, "L02 GA PLAN", "FloorPlan"),
                new LoadingPlanView(4, "L10_Loading-Plan", "FloorPlan")
            };

            var l01 = LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "Loading Plan");
            Assert.AreEqual(1, l01.Count);
            Assert.AreEqual(2L, l01[0].Id);

            Assert.AreEqual(0, LoadingPlanGuessViewModel.CandidatesFor(views, "L02", "Loading Plan").Count,
                            "a GA plan is not a loading plan, even when it is the only view of the level");
            Assert.AreEqual(1, LoadingPlanGuessViewModel.CandidatesFor(views, "L10", "Loading Plan").Count,
                            "spaces, dashes and underscores do not change the phrase");
            Assert.AreEqual(2, LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "").Count,
                            "with no phrase the level name is all that is asked for");
        }

        /// <summary>
        /// "L01_LEVEL 1" in ETABS, "LEVEL 1 LOADING PLAN" in Revit: the part after the prefix is
        /// what the drawing carries, and it is still bounded so level 1 does not take level 10.
        /// </summary>
        [TestMethod]
        public void APrefixedLevelIsFoundByItsParts()
        {
            LoadingPlanView[] views =
            {
                new LoadingPlanView(1, "LEVEL 1 LOADING PLAN", "EngineeringPlan"),
                new LoadingPlanView(2, "LEVEL 10 LOADING PLAN", "EngineeringPlan"),
                new LoadingPlanView(3, "LEVEL 1 GA PLAN", "EngineeringPlan")
            };

            var l01 = LoadingPlanGuessViewModel.CandidatesFor(views, "L01_LEVEL 1", "Loading Plan");
            Assert.AreEqual(1, l01.Count);
            Assert.AreEqual(1L, l01[0].Id);

            var l10 = LoadingPlanGuessViewModel.CandidatesFor(views, "L10_LEVEL 10", "Loading Plan");
            Assert.AreEqual(1, l10.Count);
            Assert.AreEqual(2L, l10[0].Id);
        }

        /// <summary>
        /// A floor with several loading plans that all say so: one per tower, SDL apart from
        /// live load. The phrase picks out loading plans; the required text picks out the one,
        /// and every piece of it has to be there.
        /// </summary>
        [TestMethod]
        public void EveryRequiredPieceHasToBeInTheName()
        {
            LoadingPlanView[] views =
            {
                new LoadingPlanView(1, "L01 LOADING PLAN - TOWER A - SDL", "FloorPlan"),
                new LoadingPlanView(2, "L01 LOADING PLAN - TOWER B - SDL", "FloorPlan"),
                new LoadingPlanView(3, "L01 LOADING PLAN - TOWER A - LL", "FloorPlan"),
                new LoadingPlanView(4, "L01 GA PLAN - TOWER A - SDL", "FloorPlan")
            };

            var picked = LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "Loading Plan", "Tower A, SDL");
            Assert.AreEqual(1, picked.Count, "only the one carrying both");
            Assert.AreEqual(1L, picked[0].Id);

            Assert.AreEqual(2, LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "Loading Plan", "tower-a").Count,
                            "matched ignoring case, spaces and dashes, like the phrase");
            Assert.AreEqual(3, LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "Loading Plan", " , ").Count,
                            "nothing required asks for nothing more");
            Assert.AreEqual(0, LoadingPlanGuessViewModel.CandidatesFor(views, "L01", "Loading Plan", "Tower A, SDL, Tower C").Count,
                            "one piece missing is enough to leave a view out");
        }

        /// <summary>
        /// The search box in the pairing window: with commas it asks for every phrase, the same
        /// rule as the guess; without, for every word in any order, as it always did.
        /// </summary>
        [TestMethod]
        public void TheSearchBoxTakesCommaSeparatedPhrasesThatMustAllBeThere()
        {
            const string towerA = "L01 LOADING PLAN - TOWER A - SDL";
            const string towerB = "L01 LOADING PLAN - TOWER B - SDL";

            Assert.IsTrue(LoadingPlansViewModel.SearchMatches(towerA, "Tower A, SDL"));
            Assert.IsFalse(LoadingPlansViewModel.SearchMatches(towerB, "Tower A, SDL"),
                           "Tower B has an A in its name, but not the phrase TOWER A");
            Assert.IsFalse(LoadingPlansViewModel.SearchMatches(towerA, "Tower A, LL"),
                           "every phrase has to be there");

            Assert.IsTrue(LoadingPlansViewModel.SearchMatches(towerA, "l01 sdl"),
                          "without commas, words in any order, as before");
            Assert.IsTrue(LoadingPlansViewModel.SearchMatches(towerA, ""), "an empty search lists everything");
        }

        /// <summary>
        /// A typical floor is one view named after its run of levels. The levels in the middle
        /// of the run are never in the name, and are still that view's.
        /// </summary>
        [TestMethod]
        public void ATypicalFloorViewNamedAfterItsRunCoversEveryLevelInIt()
        {
            LoadingPlanView[] views =
            {
                new LoadingPlanView(1, "L01 LOADING PLAN", "FloorPlan"),
                new LoadingPlanView(2, "TYPICAL L05-L20 LOADING PLAN", "FloorPlan"),
                new LoadingPlanView(3, "L21 LOADING PLAN", "FloorPlan")
            };

            foreach (string story in new[] { "L05", "L12", "L20" })
            {
                var found = LoadingPlanGuessViewModel.CandidatesFor(views, story, "Loading Plan");
                Assert.AreEqual(1, found.Count, story);
                Assert.AreEqual(2L, found[0].Id, story);
            }

            Assert.AreEqual(3L, LoadingPlanGuessViewModel.CandidatesFor(views, "L21", "Loading Plan")[0].Id,
                            "the level after the run is not in it");
            Assert.AreEqual(0, LoadingPlanGuessViewModel.CandidatesFor(views, "L04", "Loading Plan").Count,
                            "nor the level before");
        }

        [TestMethod]
        public void ARunIsReadHoweverItIsWritten()
        {
            Assert.IsTrue(LoadingPlansViewModel.CoversTheLevel("LEVEL 5 TO 20 LOADING PLAN", "LEVEL 12"));
            Assert.IsTrue(LoadingPlansViewModel.CoversTheLevel("L5~L20 LOADING PLAN", "L07"),
                          "L5 and L07 are numbered alike");
            Assert.IsTrue(LoadingPlansViewModel.CoversTheLevel("L05 - 20 LOADING PLAN", "L10"));
            Assert.IsTrue(LoadingPlansViewModel.CoversTheLevel("L05 THRU L20 LOADING PLAN", "L19"));
            Assert.IsTrue(LoadingPlansViewModel.CoversTheLevel("L5 LOADING PLAN", "L05"),
                          "a level of one is read by its number as well");

            var parts = LoadingPlanGuessViewModel.CandidatesFor(
                new[] { new LoadingPlanView(1, "LEVEL 5-20 LOADING PLAN", "FloorPlan") },
                "L07_LEVEL 7", "Loading Plan");
            Assert.AreEqual(1, parts.Count, "a prefixed level is found in a run by its parts");
        }

        [TestMethod]
        public void OnlyALevelNumberedLikeTheRunIsInIt()
        {
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("B1-L3 LOADING PLAN", "L02"),
                           "two kinds of level are no run");
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("L05-L20 LOADING PLAN", "B10"));
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("SL05-SL20 LOADING PLAN", "L10"),
                           "SL is not L");
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("L20-L05 LOADING PLAN", "L10"),
                           "a run is written low to high; anything else is not read as one");
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("L05 TOWER 20 LOADING PLAN", "L10"));
            Assert.IsFalse(LoadingPlansViewModel.CoversTheLevel("L10A LOADING PLAN", "L10"),
                           "L10A is another level");
        }

        /// <summary>
        /// A typical floor's plan named after its master story alone: the similar levels read
        /// the master's view, and say so.
        /// </summary>
        [TestMethod]
        public void ALevelSimilarToAMasterInEtabsIsOfferedTheMastersView()
        {
            LoadingPlanView[] views =
            {
                new LoadingPlanView(1, "L05 LOADING PLAN", "FloorPlan"),
                new LoadingPlanView(2, "L06 LOADING PLAN", "FloorPlan")
            };

            var guess = new LoadingPlanGuessViewModel(
                new[]
                {
                    new LoadingPlanGuessRow("L05", "", true),
                    new LoadingPlanGuessRow("L06", "", true, "L05"),
                    new LoadingPlanGuessRow("L07", "", true, "L05"),
                    new LoadingPlanGuessRow("L08", "", true)
                },
                views, "Loading Plan", "", "", "");

            Assert.AreEqual(1L, guess.Rows[0].PickedView.Id);
            Assert.AreEqual(2L, guess.Rows[1].PickedView.Id, "a level with its own view keeps it");
            Assert.AreEqual("", guess.Rows[1].BorrowedFrom);
            Assert.AreEqual(1L, guess.Rows[2].PickedView.Id, "a similar level with none reads its master's");
            Assert.AreEqual("L05", guess.Rows[2].BorrowedFrom);
            Assert.IsNull(guess.Rows[3].PickedView, "a level similar to nothing borrows nothing");
        }

        [TestMethod]
        public void CopyingAPairingGivesTheViewTheSheetsOrBoth()
        {
            LoadingPlanLink source = new LoadingPlanLink
            {
                Story = "L05", ViewId = 7, ViewName = "TYPICAL", WorkbookPath = @"C:\s.xlsx",
                SheetName = "TOWER", LineLoadSheetName = "TOWER LL"
            };
            LoadingPlanLink own = new LoadingPlanLink
            {
                Story = "L06", ViewId = 9, ViewName = "L06 PLAN", WorkbookPath = @"C:\s.xlsx",
                SheetName = "PODIUM"
            };

            LoadingPlanLink both = LoadingPlanCopyViewModel.Merge(source, own, "L06", true, true);
            Assert.AreEqual("L06", both.Story);
            Assert.AreEqual(7L, both.ViewId);
            Assert.AreEqual("TOWER", both.SheetName);
            Assert.AreEqual("TOWER LL", both.LineLoadSheetName);

            LoadingPlanLink sheets = LoadingPlanCopyViewModel.Merge(source, own, "L06", false, true);
            Assert.AreEqual(9L, sheets.ViewId, "its own view is kept");
            Assert.AreEqual("TOWER", sheets.SheetName);

            LoadingPlanLink view = LoadingPlanCopyViewModel.Merge(source, own, "L06", true, false);
            Assert.AreEqual(7L, view.ViewId);
            Assert.AreEqual("PODIUM", view.SheetName, "its own sheet is kept");

            Assert.IsNull(LoadingPlanCopyViewModel.Merge(source, null, "L07", false, true),
                          "sheets alone do not pair a level that has no view");
            Assert.AreEqual(7L, LoadingPlanCopyViewModel.Merge(source, null, "L07", true, true).ViewId);
        }

        [TestMethod]
        public void NothingMatchesAnEmptyName()
        {
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel("L03 - LOADING PLAN", ""));
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel("", "L03"));
            Assert.IsFalse(LoadingPlansViewModel.NamesTheLevel(null, "L03"));
        }
    }
}
