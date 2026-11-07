using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The sheet that comes up when the check is asked for before it is ready.
    ///
    /// The thing it must never become is a refusal. A run against two models nobody has aligned
    /// still says something true - that nothing pairs - and a tool that will not run is worse
    /// than one that runs and explains itself. What it must not do is hand back two thousand
    /// unpaired elements with no word about why.
    /// </summary>
    [TestClass]
    public class PreflightTests
    {
        [TestMethod]
        public void NothingWrongMeansNoSheetAtAll()
        {
            PreflightReport report = Report(
                new PreflightItem(PreflightLevel.Done, "ETABS model read", "47 levels"),
                new PreflightItem(PreflightLevel.Done, "Revit model read", "1842 beams"));

            Assert.IsTrue(report.IsClear, "a run with nothing to say about it goes straight through");
        }

        /// <summary>
        /// The blockers are numbered so the headline's count can be followed down the list. The
        /// lines that are only worth knowing, and the ones already dealt with, take no number -
        /// numbering them would make "two things are missing" name four rows.
        /// </summary>
        [TestMethod]
        public void OnlyTheBlockersAreNumberedAndTheyAreCounted()
        {
            PreflightReport report = Report(
                new PreflightItem(PreflightLevel.Blocker, "The Revit model has not been read", ""),
                new PreflightItem(PreflightLevel.Warning, "Nothing has been aligned", ""),
                new PreflightItem(PreflightLevel.Blocker, "No level has been picked", ""),
                new PreflightItem(PreflightLevel.Done, "ETABS model read", ""));

            Assert.AreEqual(2, report.BlockerCount);
            Assert.AreEqual(1, report.WarningCount);
            Assert.AreEqual("Two things are missing", report.Headline);

            List<string> numbers = report.Items.Select(item => item.Number).ToList();
            CollectionAssert.AreEqual(new List<string> { "1", "", "2", "" }, numbers);
        }

        [TestMethod]
        public void OneMissingThingIsSaidInTheSingular()
        {
            PreflightReport report = Report(
                new PreflightItem(PreflightLevel.Blocker, "The Revit model has not been read", ""));

            Assert.AreEqual("One thing is missing", report.Headline);
            Assert.AreEqual("DO IT, THEN RUN", report.FixEverythingText);
        }

        /// <summary>
        /// Warnings alone do not read as things missing. A model that is read, aligned and
        /// simply has no loading plans paired is not half set up - that is most of a building.
        /// </summary>
        [TestMethod]
        public void WarningsOnTheirOwnAreNotAMissingThing()
        {
            PreflightReport report = Report(
                new PreflightItem(PreflightLevel.Warning, "No level is paired with a loading plan", ""));

            Assert.AreEqual(0, report.BlockerCount);
            Assert.IsFalse(report.IsClear, "it still has something to say");
            Assert.AreEqual("One thing is worth a look", report.Headline);
            Assert.IsFalse(report.CanFixEverything, "there is nothing here it could do for you");
        }

        /// <summary>
        /// The offer to deal with everything is only made where everything can actually be
        /// dealt with. Aligning two models needs somebody to pick two points in Revit, so a
        /// button claiming to do it and then not doing it would be worse than no button.
        /// </summary>
        [TestMethod]
        public void ItOnlyOffersToDoTheLotWhenItCanFinishTheJob()
        {
            PreflightReport canDo = Report(
                new PreflightItem(PreflightLevel.Blocker, "ETABS not read", "", "READ IT NOW",
                                  new RelayCommand(_ => { }), fixesItself: true),
                new PreflightItem(PreflightLevel.Blocker, "Revit not read", "", "READ IT NOW",
                                  new RelayCommand(_ => { }), fixesItself: true));

            Assert.IsTrue(canDo.CanFixEverything);

            PreflightReport cannot = Report(
                new PreflightItem(PreflightLevel.Blocker, "ETABS not read", "", "READ IT NOW",
                                  new RelayCommand(_ => { }), fixesItself: true),
                new PreflightItem(PreflightLevel.Blocker, "No level has been picked", ""));

            Assert.IsFalse(cannot.CanFixEverything,
                           "one blocker nobody can do from here means the offer is not made");
        }

        /// <summary>
        /// With neither model read there is nothing to compare, so "run anyway" is not offered:
        /// it would close the sheet and produce nothing, which reads as the tool being broken.
        /// </summary>
        [TestMethod]
        public void RunAnywayIsNotOfferedWhenARunWouldSayNothing()
        {
            PreflightReport nothingToRun = new PreflightReport(
                new[] { new PreflightItem(PreflightLevel.Blocker, "ETABS not read", "") },
                canRunAnyway: false);

            Assert.IsFalse(nothingToRun.CanRunAnyway);
        }

        private static PreflightReport Report(params PreflightItem[] items)
        {
            return new PreflightReport(items, canRunAnyway: true);
        }
    }
}
