using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The one rule that overrides the chips being free to be on together.
    ///
    /// Slabs and Slab loads are two cuts of the same floor along different lines - one along
    /// the Revit floors, one along the loading plan regions. Both on lays the second mesh
    /// straight over the first: the colours underneath are hidden, the edges double up, and a
    /// click lands on whichever piece happens to be on top. So one turns the other off.
    /// </summary>
    [TestClass]
    public class ScopeChipTests
    {
        [TestMethod]
        public void TheTwoSlabChipsAreNeverOnAtOnceToBeginWith()
        {
            FindingTally tally = new FindingTally(() => { });

            Assert.IsFalse(tally.IsActive(ViewMode.Slabs) && tally.IsActive(ViewMode.SlabLoads),
                           "two shells over each other is the one thing the pair must not start as");
        }

        [TestMethod]
        public void SwitchingTheLoadChipOnSwitchesTheThicknessChipOff()
        {
            FindingTally tally = new FindingTally(() => { });
            Chip(tally, ViewMode.Slabs).IsActive = true;

            Chip(tally, ViewMode.SlabLoads).IsActive = true;

            Assert.IsTrue(tally.IsActive(ViewMode.SlabLoads));
            Assert.IsFalse(tally.IsActive(ViewMode.Slabs));
        }

        [TestMethod]
        public void SwitchingTheThicknessChipOnSwitchesTheLoadChipOff()
        {
            FindingTally tally = new FindingTally(() => { });
            Chip(tally, ViewMode.SlabLoads).IsActive = true;

            Chip(tally, ViewMode.Slabs).IsActive = true;

            Assert.IsTrue(tally.IsActive(ViewMode.Slabs));
            Assert.IsFalse(tally.IsActive(ViewMode.SlabLoads));
        }

        /// <summary>
        /// Both off is a state of its own, and a legitimate one: it is how the frames are
        /// looked at without a floor under them. Switching the last slab chip off must not
        /// bring the other one back to fill the gap.
        /// </summary>
        [TestMethod]
        public void SwitchingTheLastSlabChipOffLeavesBothOff()
        {
            FindingTally tally = new FindingTally(() => { });
            Chip(tally, ViewMode.SlabLoads).IsActive = true;

            Chip(tally, ViewMode.SlabLoads).IsActive = false;

            Assert.IsFalse(tally.IsActive(ViewMode.Slabs));
            Assert.IsFalse(tally.IsActive(ViewMode.SlabLoads));
        }

        /// <summary>
        /// The rule is about the slab pair and nothing else. Beams, columns and walls stay free
        /// to be read together, which is how a beam and the column it lands on are compared.
        /// </summary>
        [TestMethod]
        public void TheFrameChipsAreStillFreeToBeOnTogether()
        {
            FindingTally tally = new FindingTally(() => { });

            Assert.IsTrue(tally.IsActive(ViewMode.Beams));
            Assert.IsTrue(tally.IsActive(ViewMode.Columns));
            Assert.IsTrue(tally.IsActive(ViewMode.Walls));

            Chip(tally, ViewMode.Slabs).IsActive = true;

            Assert.IsTrue(tally.IsActive(ViewMode.Beams), "the slab rule does not reach the frames");
            Assert.IsTrue(tally.IsActive(ViewMode.Columns));
            Assert.IsTrue(tally.IsActive(ViewMode.Walls));
        }

        /// <summary>
        /// Turning the other chip off is a second switch, and the plan must not be rebuilt
        /// twice for one click - once against a half-settled pair of chips.
        /// </summary>
        [TestMethod]
        public void OneClickRebuildsThePlanOnce()
        {
            int rebuilds = 0;
            FindingTally tally = new FindingTally(() => rebuilds++);
            Chip(tally, ViewMode.Slabs).IsActive = true;
            rebuilds = 0;

            Chip(tally, ViewMode.SlabLoads).IsActive = true;

            Assert.AreEqual(1, rebuilds);
        }

        /// <summary>
        /// Switching the line load chip must not disturb the slab pair, which has its own rule.
        /// </summary>
        [TestMethod]
        public void TheLineLoadChipLeavesTheSlabChipsAlone()
        {
            FindingTally tally = new FindingTally(() => { });
            Chip(tally, ViewMode.Slabs).IsActive = true;

            Chip(tally, ViewMode.LineLoads).IsActive = true;

            Assert.IsTrue(tally.IsActive(ViewMode.Slabs), "the slab chip was not the one touched");
            Assert.IsTrue(tally.IsActive(ViewMode.LineLoads));
        }

        /// <summary>
        /// The raw data is a box beside the tabs rather than a tab of its own, so the strip is
        /// the six checks, after them the height zones the line loads read, and last the SIDL_C
        /// check.
        /// </summary>
        [TestMethod]
        public void TheStripIsTheSixChecksAndTheHeightZones()
        {
            FindingTally tally = new FindingTally(() => { });

            CollectionAssert.AreEqual(
                new[] { ViewMode.Beams, ViewMode.Columns, ViewMode.Walls, ViewMode.Slabs,
                        ViewMode.SlabLoads, ViewMode.LineLoads, ViewMode.SidlC, ViewMode.SidlCArea,
                        ViewMode.HeightZones },
                tally.Tabs.Select(tab => tab.Mode).ToList());
            Assert.IsFalse(tally.IsActive(ViewMode.HeightZones), "off to begin with, like the load tabs");
        }

        /// <summary>
        /// The strip is drawn in groups: the element checks under Geometry, the load checks
        /// under Loads, and the height zones bare at the end. Every tab is in one of them.
        /// </summary>
        [TestMethod]
        public void TheTabsAreGroupedIntoGeometryAndLoads()
        {
            FindingTally tally = new FindingTally(() => { });

            CollectionAssert.AreEqual(new[] { "GEOMETRY", "LOADS", "" },
                                      tally.Groups.Select(group => group.Title).ToList());
            CollectionAssert.AreEqual(
                new[] { ViewMode.Beams, ViewMode.Columns, ViewMode.Walls, ViewMode.Slabs },
                tally.Groups[0].Tabs.Select(tab => tab.Mode).ToList());
            CollectionAssert.AreEqual(
                new[] { ViewMode.SlabLoads, ViewMode.LineLoads, ViewMode.SidlC, ViewMode.SidlCArea },
                tally.Groups[1].Tabs.Select(tab => tab.Mode).ToList());
            CollectionAssert.AreEqual(tally.Tabs.ToList(),
                                      tally.Groups.SelectMany(group => group.Tabs).ToList(),
                                      "the groups, read along, are the strip");
        }

        private static ScopeTab Chip(FindingTally tally, ViewMode mode)
        {
            return tally.Tabs.First(tab => tab.Mode == mode);
        }
    }
}
