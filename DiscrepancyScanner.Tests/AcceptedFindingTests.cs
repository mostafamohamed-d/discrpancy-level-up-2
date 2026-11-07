using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// What accepting a difference does to the rest of the window.
    ///
    /// Accepted means the two models differ here and that is agreed - a screed the analysis
    /// leaves out, a mark the design team has confirmed. The difference is real, so it stays on
    /// the plan and stays in the drawer; what must stop is it reading as a failure and counting
    /// as work, because on a model where every remaining difference has been agreed the number
    /// would otherwise never come down.
    /// </summary>
    [TestClass]
    public class AcceptedFindingTests
    {
        [TestMethod]
        public void AnAcceptedFailureIsNotDrawnAsAFailure()
        {
            Stub column = Failing();
            Assert.AreEqual(ViewPalette.Failed, ViewPalette.ForState(column));

            column.IsAccepted = true;
            Assert.AreEqual(ViewPalette.Accepted, ViewPalette.ForState(column),
                            "an agreed difference must stop shouting");
        }

        /// <summary>
        /// A result the filters have switched off is grey whatever anybody said about it. The
        /// plan is showing nothing about that check, and painting it the accepted colour would
        /// be showing a verdict on a check that is not on screen.
        /// </summary>
        [TestMethod]
        public void AResultSwitchedOffStaysGreyEvenWhenAccepted()
        {
            Stub column = Failing();
            column.IsAccepted = true;
            column.IsMuted = true;

            Assert.AreEqual(ViewPalette.Muted, ViewPalette.ForState(column));
        }

        [TestMethod]
        public void AnAcceptedFailureIsNotCountedOnTheChipOrTheRail()
        {
            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new List<Story_E> { Story("L03") });

            Stub open = Failing();
            Stub agreed = Failing();
            agreed.IsAccepted = true;

            tally.Update(new List<ElementViewModel> { open, agreed }, "L03");

            ScopeTab columns = Chip(tally, ViewMode.Columns);
            Assert.AreEqual(1, columns.Failed, "the agreed one is not work left to do");
            Assert.AreEqual(1, tally.Levels[0].Failed, "and the rail says which floor to go to");
        }

        /// <summary>
        /// The one rule that keeps an agreement honest: it is recorded against the results it
        /// was made on, and a run that changes any of them leaves it standing but no longer
        /// settled. Otherwise last month's agreement about a 100 mm difference would quietly
        /// bury this month's 300 mm one.
        /// </summary>
        [TestMethod]
        public void AnAgreementMadeAgainstDifferentResultsDoesNotStillSettleThem()
        {
            Stub column = Failing();

            ReviewNote said = new ReviewNote
            {
                Verdict = HumanVerdict.Accepted,
                Fingerprint = column.VerdictFingerprint
            };
            Assert.AreEqual(HumanVerdict.Accepted, CheckNaming.VerdictOn(said, column));

            Stub changed = new Stub("C-17",
                new CheckOutcome(CheckKind.Dimensions, ResultLevel.Failed, "300 wider"),
                new CheckOutcome(CheckKind.Location, ResultLevel.Failed, "and moved"));

            Assert.AreEqual(HumanVerdict.None, CheckNaming.VerdictOn(said, changed),
                            "the element is not saying what it said when somebody agreed to it");
            Assert.IsTrue(CheckNaming.IsStale(said, changed));
        }

        [TestMethod]
        public void ReviewedIsNotAcceptedAndLeavesTheFindingOpen()
        {
            Stub column = Failing();
            ReviewNote said = new ReviewNote
            {
                Verdict = HumanVerdict.Reviewed,
                Fingerprint = column.VerdictFingerprint
            };

            Assert.AreEqual(HumanVerdict.Reviewed, CheckNaming.VerdictOn(said, column));
            Assert.AreEqual(ViewPalette.Failed, ViewPalette.ForState(column),
                            "having looked at something does not settle it");
        }

        #region building the inputs

        private static Stub Failing()
        {
            return new Stub("C-17",
                new CheckOutcome(CheckKind.Dimensions, ResultLevel.Failed, "100 wider"));
        }

        private static ScopeTab Chip(FindingTally tally, ViewMode mode)
        {
            foreach (ScopeTab tab in tally.Tabs)
            {
                if (tab.Mode == mode)
                {
                    return tab;
                }
            }
            return null;
        }

        private static Story_E Story(string name)
        {
            return new Story_E(name, 10000, Form1.eLength.mm);
        }

        /// <summary>
        /// A column with no geometry behind it. Everything here is about what an element's
        /// verdicts amount to, and none of it needs the shape it is drawn as.
        /// </summary>
        private sealed class Stub : ElementViewModel
        {
            public Stub(string uniqueName, params CheckOutcome[] outcomes)
                : base(ElementKind.Column, ElementSource.Paired, CheckState.Failed, uniqueName,
                       "L03", new List<ResultField>())
            {
                EtabsUniqueName = uniqueName;
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
