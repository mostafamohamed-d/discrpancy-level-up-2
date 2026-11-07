using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Placing a SAFE model, which holds one floor and says nothing about which floor it is.
    ///
    /// Everything the check does is keyed by story, so the model is given one, standing where
    /// its floor is drawn. Which Revit level that floor is comes from the alignment, which
    /// pairs the story with a level for its height; the pairing names the story and says what
    /// height of the Revit model to keep.
    /// </summary>
    [TestClass]
    public class SafeLevelTests
    {
        [TestMethod]
        public void TheFloorIsWhereMostAreaCornersAre()
        {
            // A slab's four corners at nought, a wall hung under it with its top two there and
            // its bottom two a storey down. The floor is nought, not the bottom of the wall.
            double[] z = { 0, 0, 0, 0, 0, 0, -3.2, -3.2 };

            Assert.AreEqual(0, EtabsEngine.FloorElevation(z), 1e-9);
        }

        [TestMethod]
        public void AModelDrawnAtItsLevelStandsThere()
        {
            // The case the old reference Z was typed in for. Read off the model, a SAFE model
            // drawn at its real elevation needs nobody to say so.
            double[] z = { 10.5, 10.5, 10.5, 10.5, 7.3, 7.3, 10.5, 10.5 };

            Assert.AreEqual(10.5, EtabsEngine.FloorElevation(z), 1e-9);
        }

        [TestMethod]
        public void HeightsARoundingErrorApartAreOneHeight()
        {
            double[] z = { 2.9999999, 3.0000001, 3, -1, -1 };

            Assert.AreEqual(3, EtabsEngine.FloorElevation(z), 1e-9);
        }

        [TestMethod]
        public void AModelWithNoAreasStandsAtNought()
        {
            Assert.AreEqual(0, EtabsEngine.FloorElevation(new double[0]));
            Assert.AreEqual(0, EtabsEngine.FloorElevation(null));
        }

        [TestMethod]
        public void AnUnpairedLinkIsNotTakenForAPairedOne()
        {
            Assert.IsFalse(new SafeLevelLink().IsPaired);
            Assert.IsFalse(new SafeLevelLink { LevelName = "   " }.IsPaired);
            Assert.IsTrue(new SafeLevelLink { LevelName = "L03" }.IsPaired);
        }

        #region the alignment is read now, not when the pairing was made

        [TestMethod]
        public void TheLevelFollowsTheAlignmentZ()
        {
            // The level's elevation is stored as Revit gives it, and the alignment is added
            // where it is used, so the Revit model is narrowed to where the alignment now puts
            // the level rather than where it put it once.
            SafeLevelLink link = new SafeLevelLink { LevelName = "L03", LevelElevationMM = 10500 };

            MyTransformation before = DataContainer.ModelTransformation;
            try
            {
                DataContainer.ModelTransformation = new MyTransformation();
                Assert.AreEqual(10500, link.AlignedElevationMM, 1e-9, "no alignment, no shift");

                // The height a pairing of a SAFE story at nought with this level gives.
                MyTransformation paired = new MyTransformation();
                paired.SetAlignment(new MyPoint(0, 0, MyTransformation.HeightFromStoryAndLevel(0, 10500, 0)), 0);
                DataContainer.ModelTransformation = paired;

                Assert.AreEqual(0, link.AlignedElevationMM, 1e-9,
                    "the level lands on the SAFE floor, which is where the Revit model is narrowed to");
            }
            finally
            {
                DataContainer.ModelTransformation = before;
            }
        }

        #endregion

        #region narrowing the Revit model onto the one level

        [TestMethod]
        public void OnlyTheFloorsAtTheLevelAreKept()
        {
            DataContainer data = FreshContainer();
            data.Slabs_r = new List<Slab_R>
            {
                Floor(1, 10500),   // on the level
                Floor(2, 10300),   // dropped a little, still this floor
                Floor(3, 7000),    // the floor below
                Floor(4, 14000)    // the floor above
            };

            string dropped = data.NarrowRevitToLevel(10500, 1500);

            CollectionAssert.AreEquivalent(new List<long> { 1, 2 },
                data.Slabs_r.Select(slab => slab.RevitID).ToList(),
                "a slab hung below its level belongs to it; the floors above and below do not");
            StringAssert.Contains(dropped, "2 floor(s)");
        }

        [TestMethod]
        public void TheBandIsWhatDecidesRatherThanTheNearestLevel()
        {
            // The whole point of the band. Every Revit element is nearest to the only story a
            // SAFE model has, so nearest is no test at all - without a band the entire building
            // would be named after the one floor SAFE holds.
            DataContainer data = FreshContainer();
            data.Slabs_r = new List<Slab_R> { Floor(1, 10500), Floor(2, 40000) };

            data.NarrowRevitToLevel(10500, 1500);

            Assert.AreEqual(1, data.Slabs_r.Count);
            Assert.AreEqual(1, data.Slabs_r[0].RevitID, "the floor thirty metres up is not this floor");
        }

        /// <summary>
        /// A container of its own rather than the shared one, so a test that throws most of a
        /// Revit model away does not throw it away for whatever runs next.
        /// </summary>
        private static DataContainer FreshContainer()
        {
            return (DataContainer)Activator.CreateInstance(typeof(DataContainer), nonPublic: true);
        }

        private static Slab_R Floor(long id, double topElevation)
        {
            PlanPolygon plan = PlanPolygon.FromPoints(new List<MyPoint>
            {
                new MyPoint(0, 0, topElevation),
                new MyPoint(1000, 0, topElevation),
                new MyPoint(1000, 1000, topElevation),
                new MyPoint(0, 1000, topElevation)
            });

            return new Slab_R(id, plan, topElevation, topElevation - 200);
        }

        #endregion

        [TestMethod]
        public void OnlySafeIsReadAsASingleLevelProgram()
        {
            Assert.IsTrue(CsiProducts.IsSingleLevel(CsiProduct.Safe));
            Assert.IsFalse(CsiProducts.IsSingleLevel(CsiProduct.Etabs),
                "an ETABS model is a building and says what floor everything is on");

            Assert.AreEqual("SAFE", CsiProducts.Name(CsiProduct.Safe));
            Assert.AreEqual("ETABS", CsiProducts.Name(CsiProduct.Etabs));
        }
    }
}
