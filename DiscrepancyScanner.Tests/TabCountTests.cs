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
    /// The numbers on the tabs are the open findings on the level on show - the coloured
    /// things the plan under the tab has on it - and the level rail is what counts the whole
    /// building.
    /// </summary>
    [TestClass]
    public class TabCountTests
    {
        [TestMethod]
        public void ATabCountsOnlyTheLevelOnShow()
        {
            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new[] { new Story_E("L01", 0, Form1.eLength.mm), new Story_E("L02", 3000, Form1.eLength.mm) });

            List<ElementViewModel> elements = new List<ElementViewModel>
            {
                new Stub(ElementKind.Beam, "L01"),
                new Stub(ElementKind.Beam, "L01"),
                new Stub(ElementKind.Beam, "L02"),
                new Stub(ElementKind.LineLoad, "L02")
            };

            tally.Update(elements, "L01");
            Assert.AreEqual(2, Open(tally, ViewMode.Beams));
            Assert.AreEqual(0, Open(tally, ViewMode.LineLoads));

            tally.Update(elements, "L02");
            Assert.AreEqual(1, Open(tally, ViewMode.Beams));
            Assert.AreEqual(1, Open(tally, ViewMode.LineLoads));

            Assert.AreEqual(2, tally.Levels.Single(row => row.Name == "L01").Failed,
                            "the rail still counts every level");
        }

        /// <summary>
        /// An element drawn grey behind another tab's check still counts on its own tab, which
        /// stays up with its number while switched off. It used to be marked as context to be
        /// drawn grey, and the next count - after an acceptance, say - left it out, so every tab
        /// not on show dropped to nothing.
        /// </summary>
        [TestMethod]
        public void AnElementDrawnBehindAnotherTabStillCountsOnItsOwn()
        {
            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new[] { new Story_E("L01", 0, Form1.eLength.mm) });

            Stub behind = new Stub(ElementKind.Column, "L01") { IsBackdrop = true, EtabsUniqueName = "C-1" };
            Assert.IsTrue(PairingEditor.CanBeMarked(new Stub(ElementKind.Column, "L01") { EtabsUniqueName = "C-1" }),
                          "the same column on its own tab can be");
            Stub outline = new Stub(ElementKind.Column, "L01") { IsContext = true };

            tally.Update(new List<ElementViewModel> { behind, outline }, "L01");

            Assert.AreEqual(1, Open(tally, ViewMode.Columns), "the one drawn behind counts; the outline never does");
            Assert.AreSame(ViewPalette.Context, ViewPalette.ForState(behind), "and it is still drawn grey");
            Assert.IsFalse(PairingEditor.CanBeMarked(behind), "and still cannot be picked for pairing");
        }

        /// <summary>
        /// Both cuts of the floor are built together now, so moving between the slab tab and
        /// the load tab only changes which is shown. The load cut carries the thickness verdict
        /// as well as its loads, so each piece counts on its own cut's tab and nowhere else -
        /// or every thickness finding would be counted twice.
        /// </summary>
        [TestMethod]
        public void EachCutOfTheFloorCountsOnItsOwnTab()
        {
            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new[] { new Story_E("L01", 0, Form1.eLength.mm) });

            Stub thicknessCut = new Stub(ElementKind.Slab, "L01") { Tab = ViewMode.Slabs };
            thicknessCut.Outcomes = new List<CheckOutcome> { new CheckOutcome(CheckKind.SlabThickness, ResultLevel.Failed) };

            // The same thickness finding, carried by the load cut over the same floor, with the
            // loads agreeing: nothing for the load tab, and already counted on the slab tab.
            Stub loadCutLoadsAgree = new Stub(ElementKind.Slab, "L01") { Tab = ViewMode.SlabLoads };
            loadCutLoadsAgree.Outcomes = new List<CheckOutcome>
            {
                new CheckOutcome(CheckKind.SlabThickness, ResultLevel.Failed),
                new CheckOutcome(CheckKind.SlabLoads, ResultLevel.Passed)
            };

            Stub loadCutLoadsWrong = new Stub(ElementKind.Slab, "L01") { Tab = ViewMode.SlabLoads };
            loadCutLoadsWrong.Outcomes = new List<CheckOutcome> { new CheckOutcome(CheckKind.SlabLoads, ResultLevel.Failed) };

            tally.Update(new List<ElementViewModel> { thicknessCut, loadCutLoadsAgree, loadCutLoadsWrong }, "L01");

            Assert.AreEqual(1, Open(tally, ViewMode.Slabs));
            Assert.AreEqual(1, Open(tally, ViewMode.SlabLoads));
        }

        /// <summary>
        /// A tab's key switches it on and off the same as a click - including the rule that the
        /// two slab tabs are never both up - and out of the box the keys are 1 to 9, along the
        /// strip but for the height zones (7), drawn last since they are not a check.
        /// </summary>
        [TestMethod]
        public void TheTabKeysSwitchTheTabsTheWayAClickDoes()
        {
            FindingTally tally = new FindingTally(() => { });

            CollectionAssert.AreEqual(new[] { "1", "2", "3", "4", "5", "6", "8", "9", "7" },
                                      tally.Tabs.Select(tab => tab.KeyText).ToList());

            Assert.IsTrue(tally.IsActive(ViewMode.Beams));
            Assert.IsTrue(tally.ToggleTab(ViewMode.Beams));
            Assert.IsFalse(tally.IsActive(ViewMode.Beams), "switched off");
            tally.ToggleTab(ViewMode.Beams);
            Assert.IsTrue(tally.IsActive(ViewMode.Beams), "and on again");

            Assert.IsTrue(tally.IsActive(ViewMode.Slabs));
            tally.ToggleTab(ViewMode.SlabLoads);
            Assert.IsTrue(tally.IsActive(ViewMode.SlabLoads));
            Assert.IsFalse(tally.IsActive(ViewMode.Slabs), "slab loads up takes slabs down, as a click does");

            StringAssert.Contains(tally.Tabs[1].ToolTip, "Press 2");
        }

        private static int Open(FindingTally tally, ViewMode mode)
        {
            return tally.Tabs.Single(tab => tab.Mode == mode).Open;
        }

        private sealed class Stub : ElementViewModel
        {
            public Stub(ElementKind kind, string story)
                : base(kind, ElementSource.Paired, CheckState.Failed, "X", story, new List<ResultField>())
            {
                DisplayState = CheckState.Failed;
            }

            public override Point LabelAnchor => new Point(0, 0);

            public override double DistanceTo(Point modelPoint) => 0;

            protected override Geometry BuildGeometry() => Geometry.Empty;
        }
    }
}
