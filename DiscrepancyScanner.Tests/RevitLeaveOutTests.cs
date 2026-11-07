using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The leave-out lists applied by a check to a Revit model already read, rather than only
    /// as it is read. A name added to a list drops what it names from what is in hand; a name
    /// taken off a list is told apart, because what it would bring back was never read.
    /// </summary>
    [TestClass]
    public class RevitLeaveOutTests
    {
        private string wallNames, slabNames, beamTypeNames, beamMarks;

        [TestInitialize]
        public void KeepTheLists()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            wallNames = pref.WallTypeNamesToIgnore;
            slabNames = pref.SlabTypeNamesToIgnore;
            beamTypeNames = pref.BeamTypeNamesToIgnore;
            beamMarks = pref.BeamMarksToIgnore;
        }

        [TestCleanup]
        public void PutTheListsBack()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.WallTypeNamesToIgnore = wallNames;
            pref.SlabTypeNamesToIgnore = slabNames;
            pref.BeamTypeNamesToIgnore = beamTypeNames;
            pref.BeamMarksToIgnore = beamMarks;
        }

        private static RevitLeaveOut Lists(string walls = "", string slabs = "",
                                           string beamTypes = "", string marks = "")
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.WallTypeNamesToIgnore = walls;
            pref.SlabTypeNamesToIgnore = slabs;
            pref.BeamTypeNamesToIgnore = beamTypes;
            pref.BeamMarksToIgnore = marks;
            return RevitLeaveOut.FromPreferences(pref);
        }

        private static Wall_R Wall(long id, string typeName, string elementName)
        {
            return new Wall_R(id, new MyPoint(0, 0, 0), new MyPoint(10, 0, 0))
            {
                TypeName = typeName,
                ElementName = elementName
            };
        }

        private static FrameData_r Beam(long id, string typeName, string mark)
        {
            MyPoint start = new MyPoint(0, 0, 0);
            MyPoint end = new MyPoint(10, 0, 0);
            return new FrameData_r(id, start, end, start.DistanceTo(end), new MyPoint(1, 0, 0))
            {
                TypeName = typeName,
                RevitName = typeName,
                BeamMark = mark
            };
        }

        [TestMethod]
        public void AWallTypeAddedToTheListIsTakenOutOfWhatIsInHand()
        {
            List<Wall_R> walls = new List<Wall_R>
            {
                Wall(1, "Basic Wall", "Concrete-300"),
                Wall(2, "Blockwork-200", "Blockwork-200"),
                Wall(3, "Basic Wall", "Second Cast 200"),
            };

            int dropped;
            List<Wall_R> kept = RevitLeaveOut.Keep(walls, Lists(walls: "Blockwork, Second Cast").LeavesOut,
                                                   out dropped);

            Assert.AreEqual(2, dropped);
            CollectionAssert.AreEqual(new long[] { 1 }, kept.Select(wall => wall.RevitID).ToArray(),
                "the wall's own name is looked at as well as its type name, as the import does");
        }

        [TestMethod]
        public void FloorWordsAreSplitOnSpacesAsTheImportSplitsThem()
        {
            RevitLeaveOut lists = Lists(slabs: "fill screed");

            Slab_R fill = new Slab_R(1, null, 0, -300) { TypeName = "Light Weight Fill Variable-300" };
            Slab_R screed = new Slab_R(2, null, 0, -50) { TypeName = "Floor", ElementName = "Screed 50" };
            Slab_R slab = new Slab_R(3, null, 0, -250) { TypeName = "RC Slab 250" };

            Assert.IsTrue(lists.LeavesOut(fill));
            Assert.IsTrue(lists.LeavesOut(screed));
            Assert.IsFalse(lists.LeavesOut(slab));
        }

        [TestMethod]
        public void BeamsGoOnEitherTheTypeNameOrTheMark()
        {
            RevitLeaveOut lists = Lists(beamTypes: "RB1", marks: "SB");

            Assert.IsTrue(lists.LeavesOut(Beam(1, "RB1-250x400", "B-01")));
            Assert.IsTrue(lists.LeavesOut(Beam(2, "B-300x600", "SB-04")));
            Assert.IsFalse(lists.LeavesOut(Beam(3, "B-300x600", "B-02")));
        }

        [TestMethod]
        public void AddingANameNeedsNoImport()
        {
            RevitLeaveOut readUnder = Lists(walls: "Partition");
            RevitLeaveOut now = Lists(walls: "Partition, Blockwork");

            Assert.AreEqual(0, readUnder.NoLongerLeftOutBy(now).Count);
        }

        [TestMethod]
        public void TakingANameOutNeedsAnImport()
        {
            RevitLeaveOut readUnder = Lists(walls: "Partition, Blockwork", marks: "RB");
            RevitLeaveOut now = Lists(walls: "Partition");

            IList<string> comeBack = readUnder.NoLongerLeftOutBy(now);

            Assert.AreEqual(2, comeBack.Count);
            Assert.IsTrue(comeBack.Any(line => line.Contains("Blockwork") && line.Contains("walls")));
            Assert.IsTrue(comeBack.Any(line => line.Contains("RB") && line.Contains("beam marks")));
        }

        /// <summary>
        /// A name narrowed to a shorter one brings nothing back: whatever carries the longer
        /// name carries the shorter one too, so it is still left out.
        /// </summary>
        [TestMethod]
        public void ANameStillCoveredByAShorterOneNeedsNoImport()
        {
            RevitLeaveOut readUnder = Lists(walls: "Blockwork-200");
            RevitLeaveOut now = Lists(walls: "blockwork");

            Assert.AreEqual(0, readUnder.NoLongerLeftOutBy(now).Count);
        }

        /// <summary>
        /// Once a check has dropped elements for a name, taking that name off again cannot put
        /// them back without an import, so it has to be said just as it is for a name the
        /// import ran under.
        /// </summary>
        [TestMethod]
        public void ANameAddedByACheckAndTakenOffAgainNeedsAnImport()
        {
            RevitLeaveOut inForce = Lists(walls: "Partition").With(Lists(walls: "Partition, Cladding"));

            IList<string> comeBack = inForce.NoLongerLeftOutBy(Lists(walls: "Partition"));

            Assert.AreEqual(1, comeBack.Count);
            StringAssert.Contains(comeBack[0], "Cladding");
        }
    }
}
