using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// What tells a loading plan from the other plans of the same floor.
    ///
    /// The level name alone is not enough: a floor carries a GA plan, a setting-out plan and a
    /// loading plan, and all three are named after it. Picking among them by hand on forty-seven
    /// levels is the work the guess exists to save, so the guess has to read the part of the
    /// name that says what the drawing is.
    /// </summary>
    [TestClass]
    public class LoadingPlanMarkerTests
    {
        private const string Default = "Loading Plan";

        [TestMethod]
        public void AViewThatSaysItIsALoadingPlanIsFound()
        {
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "L03 - LOADING PLAN", Default));
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "loading plan L03", Default), "case is not what says what a drawing is");
        }

        /// <summary>
        /// Drawing offices write the same phrase every way there is, and all of them mean the
        /// same drawing. Reading them as different names would send a level back to being
        /// paired by hand for the sake of an underscore.
        /// </summary>
        [TestMethod]
        public void TheSpacesAndPunctuationInThePhraseDoNotMatter()
        {
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan("L03_LoadingPlan", Default));
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan("L03 - Loading-Plan", Default));
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "DCH-M-PODIUM-B1 FLOOR LOADING PLAN", Default));
        }

        [TestMethod]
        public void AnyOtherPlanOfTheSameFloorIsNot()
        {
            Assert.IsFalse(LoadingPlansViewModel.LooksLikeALoadingPlan("L03 - GA PLAN", Default));
            Assert.IsFalse(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "L03 - SETTING OUT PLAN", Default));
        }

        /// <summary>
        /// The phrase is a naming convention, not a fact about Revit, which is why it is a
        /// setting. A job that calls them loading layouts must be able to say so.
        /// </summary>
        [TestMethod]
        public void TheJobsOwnWordForItIsWhatIsUsed()
        {
            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "L03 LOADING LAYOUT", "Loading Layout"));
            Assert.IsFalse(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "L03 LOADING LAYOUT", Default),
                "the default phrase is not in this name, and guessing wider is how the wrong "
                + "plan gets read for a whole floor");

            Assert.IsTrue(LoadingPlansViewModel.LooksLikeALoadingPlan(
                "L03 LOADING LAYOUT", "Loading Plan, Loading Layout"),
                "several phrases can be listed, because one office uses both");
        }

        [TestMethod]
        public void WithNothingToLookForNothingMatches()
        {
            Assert.IsFalse(LoadingPlansViewModel.LooksLikeALoadingPlan("L03 - LOADING PLAN", ""));
            Assert.IsFalse(LoadingPlansViewModel.LooksLikeALoadingPlan("", Default));
        }
    }
}
