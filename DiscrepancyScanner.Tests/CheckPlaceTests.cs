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
    /// Checks that ask their question in more than one place, and how the answer says which
    /// place it was.
    ///
    /// A beam short of steel at one end, a piece of slab carrying the wrong live load: both
    /// used to reach the card as a single row named after the check, which said that something
    /// was wrong without ever saying what to go and look at. Each place now answers for itself,
    /// which means several results sharing one check kind - and everything downstream that used
    /// to be able to assume one result per check is what these are about.
    /// </summary>
    [TestClass]
    public class CheckPlaceTests
    {
        [TestMethod]
        public void AResultIsNamedAfterThePlaceItAsksAbout()
        {
            CheckOutcome station = Station("TopLeft", ResultLevel.Failed);

            Assert.AreEqual("Top steel, TopLeft", CheckNaming.LabelOf(station),
                            "the row has to say which end of the beam to look at");
        }

        /// <summary>A check that asks once is still named after itself.</summary>
        [TestMethod]
        public void AResultWithNoPlaceOfItsOwnIsNamedAfterTheCheck()
        {
            CheckOutcome wall = new CheckOutcome(CheckKind.WallThickness, ResultLevel.Failed,
                "600 against 700", etabs: "600", revit: "700");

            Assert.AreEqual(CheckNaming.LabelOf(CheckKind.WallThickness),
                            CheckNaming.LabelOf(wall));
        }

        /// <summary>
        /// Two places on one element are two findings. Filed under one key, fixing one of them
        /// would read as fixing both and the next run would report no change at all.
        /// </summary>
        [TestMethod]
        public void TwoPlacesOnOneElementAreFiledAsTwoFindings()
        {
            Assert.AreNotEqual(
                RunHistory.KeyOf("Beam|B-4", CheckKind.Beam, ResultLevel.Failed, "TopLeft"),
                RunHistory.KeyOf("Beam|B-4", CheckKind.Beam, ResultLevel.Failed, "TopMid"));
        }

        /// <summary>
        /// A check with no places to name writes the key it has always written, so the first
        /// run after this change does not read every column in the model as new.
        /// </summary>
        [TestMethod]
        public void ACheckWithNoPlacesKeepsTheKeyItAlwaysHad()
        {
            Assert.AreEqual(
                "Column|C-17|" + (int)CheckKind.Location + "|" + (int)ResultLevel.Failed,
                RunHistory.KeyOf("Column|C-17", CheckKind.Location, ResultLevel.Failed));
        }

        /// <summary>
        /// A review is held against the results it was made on. With the place left out of the
        /// fingerprint, a beam that was short at one end and is now short at the other reads as
        /// unchanged, and last week's review would quietly settle a finding nobody has seen.
        /// </summary>
        [TestMethod]
        public void MovingTheFailureToAnotherPlaceIsAChangeToTheElement()
        {
            Stub before = new Stub(Station("TopLeft", ResultLevel.Failed),
                                   Station("TopMid", ResultLevel.Passed));
            Stub after = new Stub(Station("TopLeft", ResultLevel.Passed),
                                  Station("TopMid", ResultLevel.Failed));

            Assert.AreNotEqual(before.VerdictFingerprint, after.VerdictFingerprint,
                               "the beam is not saying what it said last week");
        }

        /// <summary>
        /// The one that bites. The switches are per check AND per verdict, so with a check's
        /// passes hidden and its failures shown the check itself is neither: hiding it whole
        /// would take the failures off the plan and out of the drawer, which is the one place
        /// the work left to do is counted.
        /// </summary>
        [TestMethod]
        public void HidingOneVerdictOfACheckLeavesItsOtherResultsAlone()
        {
            Stub beam = new Stub(Station("TopLeft", ResultLevel.Failed),
                                 Station("TopMid", ResultLevel.Passed));

            // What the filters leave behind with "Beam check - Matched" switched off: the
            // passes are held back, the failures are not.
            CheckOutcome passed = beam.Outcomes.First(o => o.Level == ResultLevel.Passed);
            beam.HiddenResults = new HashSet<string> { ElementViewModel.ResultKey(passed) };

            FindingsList drawer = new FindingsList();
            drawer.Update(new List<ElementViewModel> { beam }, null, null);

            Assert.AreEqual(1, drawer.Rows.Count, "the failure is still work to do");
            Assert.AreEqual("Top steel, TopLeft", drawer.Rows[0].CheckLabel);

            ElementCard card = new ElementCard(beam, null);
            Assert.IsFalse(card.Rows.Single(row => row.Level == ResultLevel.Failed).IsHidden,
                           "the failure is not the row that was switched off");
            Assert.IsTrue(card.Rows.Single(row => row.Level == ResultLevel.Passed).IsHidden);
        }

        #region building the inputs

        private static CheckOutcome Station(string token, ResultLevel level)
        {
            return new CheckOutcome(CheckKind.Beam, level, "steel at " + token,
                etabs: "1240", revit: "900", label: "Top steel, " + token, token: token);
        }

        /// <summary>
        /// A beam with no geometry behind it. All of this is about what an element's results
        /// amount to, and none of it needs the shape the beam is drawn as.
        /// </summary>
        private sealed class Stub : ElementViewModel
        {
            public Stub(params CheckOutcome[] outcomes)
                : base(ElementKind.Beam, ElementSource.Paired, CheckState.Failed, "B-4",
                       "L03", new List<ResultField>())
            {
                EtabsUniqueName = "B-4";
                Outcomes = new List<CheckOutcome>(outcomes);
                DisplayState = CheckState.Failed;
            }

            public override Point LabelAnchor => new Point(0, 0);

            public override double DistanceTo(Point modelPoint) => 0;

            protected override Geometry BuildGeometry() => Geometry.Empty;
        }

        #endregion
    }
}
