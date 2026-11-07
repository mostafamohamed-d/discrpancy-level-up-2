using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The size parameters the user maps by hand - b, Type:b, Instance:b - and how each
    /// project keeps its own mapping.
    /// </summary>
    [TestClass]
    public class ParameterMappingTests
    {
        [TestMethod]
        public void ABareNameLooksOnTheTypeThenTheInstance()
        {
            MappedParameter mapped = ParameterMapping.Parse("b").Single();
            Assert.AreEqual("b", mapped.Name);
            Assert.AreEqual(ParameterScope.TypeThenInstance, mapped.Scope);
        }

        [TestMethod]
        public void APrefixSaysWhereToLook()
        {
            List<MappedParameter> mapped = ParameterMapping.Parse("Type:b, instance: h ; INSTANCE:D");

            Assert.AreEqual(3, mapped.Count);
            Assert.AreEqual("b", mapped[0].Name);
            Assert.AreEqual(ParameterScope.Type, mapped[0].Scope);
            Assert.AreEqual("h", mapped[1].Name);
            Assert.AreEqual(ParameterScope.Instance, mapped[1].Scope);
            Assert.AreEqual("D", mapped[2].Name);
            Assert.AreEqual(ParameterScope.Instance, mapped[2].Scope);
        }

        [TestMethod]
        public void SeveralAreTriedInTheOrderTyped()
        {
            CollectionAssert.AreEqual(new[] { "b", "Type:Width", "Instance:bw" },
                ParameterMapping.Parse("b, Type:Width, Instance:bw").Select(m => m.ToString()).ToArray());
        }

        [TestMethod]
        public void NothingMappedReadsNothing()
        {
            Assert.AreEqual(0, ParameterMapping.Parse("").Count);
            Assert.AreEqual(0, ParameterMapping.Parse(null).Count);
            Assert.AreEqual(0, ParameterMapping.Parse("Type: , Instance:").Count, "a prefix with no name maps nothing");
        }

        [TestMethod]
        public void NoDefaultIsGuessed()
        {
            PrefrencesData fresh = Fresh();
            Assert.AreEqual("", fresh.ColumnWidthParameterNames);
            Assert.AreEqual("", fresh.ColumnDepthParameterNames);
            Assert.AreEqual("", fresh.ColumnDiameterParameterNames);
            Assert.AreEqual("", fresh.BeamWidthParameterNames);
            Assert.AreEqual("", fresh.BeamDepthParameterNames);
        }

        /// <summary>A preferences object of its own, with the shipped defaults - not the shared one.</summary>
        private static PrefrencesData Fresh()
        {
            return (PrefrencesData)System.Activator.CreateInstance(typeof(PrefrencesData), nonPublic: true);
        }

        #region per project

        private static PrefrencesData With(string width)
        {
            PrefrencesData pref = Fresh();
            pref.ColumnWidthParameterNames = width;
            pref.BeamDepthParameterNames = width + "-beam";
            return pref;
        }

        [TestMethod]
        public void TheFirstProjectEverKeepsTheMappingAlreadyThere()
        {
            ProjectParameterMappings mappings = new ProjectParameterMappings();
            mappings.ReadFrom(null, branchPresent: false);

            PrefrencesData pref = With("b");
            mappings.ApplyTo(pref, "A.rvt");

            Assert.AreEqual("b", pref.ColumnWidthParameterNames, "nobody's existing mapping is lost");
        }

        [TestMethod]
        public void EachProjectKeepsItsOwn()
        {
            ProjectParameterMappings mappings = new ProjectParameterMappings();
            mappings.ReadFrom(null, branchPresent: false);

            mappings.StoreFrom(With("b"), "A.rvt");
            mappings.StoreFrom(With("Width"), "B.rvt");

            PrefrencesData pref = With("anything");
            mappings.ApplyTo(pref, "A.rvt");
            Assert.AreEqual("b", pref.ColumnWidthParameterNames);
            Assert.AreEqual("b-beam", pref.BeamDepthParameterNames);

            mappings.ApplyTo(pref, "B.rvt");
            Assert.AreEqual("Width", pref.ColumnWidthParameterNames);
        }

        [TestMethod]
        public void ANewProjectStartsWithNoMappingOnceAnyProjectHasOne()
        {
            ProjectParameterMappings mappings = new ProjectParameterMappings();
            mappings.ReadFrom(null, branchPresent: false);
            mappings.StoreFrom(With("b"), "A.rvt");

            PrefrencesData pref = With("b");
            mappings.ApplyTo(pref, "New.rvt");

            Assert.AreEqual("", pref.ColumnWidthParameterNames, "A's mapping is not carried into another project");
            Assert.AreEqual("", pref.BeamDepthParameterNames);
        }

        [TestMethod]
        public void SavingOneProjectLeavesTheOthersAlone()
        {
            ProjectParameterMappings mappings = new ProjectParameterMappings();
            mappings.ReadFrom(null, branchPresent: false);
            mappings.StoreFrom(With("b"), "A.rvt");
            mappings.StoreFrom(With("Width"), "B.rvt");

            // Round trip through the settings file's shape, then edit B only.
            ProjectParameterMappings reread = new ProjectParameterMappings();
            reread.ReadFrom(mappings.ToJson(), branchPresent: true);
            reread.StoreFrom(With("bw"), "B.rvt");

            PrefrencesData pref = Fresh();
            reread.ApplyTo(pref, "A.rvt");
            Assert.AreEqual("b", pref.ColumnWidthParameterNames);
            reread.ApplyTo(pref, "B.rvt");
            Assert.AreEqual("bw", pref.ColumnWidthParameterNames);
        }

        [TestMethod]
        public void TwoWindowsSavingInTurnKeepEachOthersProject()
        {
            // Both windows read the same file - A's mapping only - and open different projects.
            ProjectParameterMappings onFile = new ProjectParameterMappings();
            onFile.ReadFrom(null, branchPresent: false);
            onFile.StoreFrom(With("b"), "A.rvt");
            Dictionary<string, object> file = onFile.ToJson();

            ProjectParameterMappings first = new ProjectParameterMappings();
            first.ReadFrom(file, branchPresent: true);
            ProjectParameterMappings second = new ProjectParameterMappings();
            second.ReadFrom(file, branchPresent: true);

            // The first window maps B and saves; the second then maps C and saves over the file.
            first.StoreFrom(With("Width"), "B.rvt");
            file = first.ToJsonOver(file, "B.rvt");
            second.StoreFrom(With("bw"), "C.rvt");
            file = second.ToJsonOver(file, "C.rvt");

            ProjectParameterMappings after = new ProjectParameterMappings();
            after.ReadFrom(file, branchPresent: true);
            PrefrencesData pref = Fresh();
            after.ApplyTo(pref, "A.rvt");
            Assert.AreEqual("b", pref.ColumnWidthParameterNames);
            after.ApplyTo(pref, "B.rvt");
            Assert.AreEqual("Width", pref.ColumnWidthParameterNames, "the second save did not undo the first window's project");
            after.ApplyTo(pref, "C.rvt");
            Assert.AreEqual("bw", pref.ColumnWidthParameterNames);
        }

        #endregion
    }
}
