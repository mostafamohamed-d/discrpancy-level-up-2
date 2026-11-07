using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The length under which a line load finding stops being work - the line twin of the
    /// slab's smallest patch. The same bargain read from both ends: what must stop is the
    /// counting, and what must keep happening is everything else, because a length that
    /// quietened the plan as well would be hiding differences rather than ranking them.
    /// </summary>
    [TestClass]
    public class LineLoadCountTests
    {
        private const string Story = "L03";

        private double shortestCounted;
        private bool runLineLoadCheck;

        [TestInitialize]
        public void SetUp()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            shortestCounted = pref.LineLoadMinimumCountedLength;
            runLineLoadCheck = pref.RunLineLoadCheck;

            pref.LineLoadMinimumCountedLength = 0.5;
            pref.RunLineLoadCheck = true;
        }

        [TestCleanup]
        public void TearDown()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.LineLoadMinimumCountedLength = shortestCounted;
            pref.RunLineLoadCheck = runLineLoadCheck;
        }

        #region the length itself

        [TestMethod]
        public void HalfAMetreIsTheDefault()
        {
            PrefrencesData defaults =
                (PrefrencesData)System.Activator.CreateInstance(typeof(PrefrencesData), nonPublic: true);

            Assert.AreEqual(0.5, defaults.LineLoadMinimumCountedLength, 1e-9);
        }

        /// <summary>
        /// A line running 300 past the end of the load on its frame: found, drawn in the colour
        /// of what was found, and not counted.
        /// </summary>
        [TestMethod]
        public void AShortStretchIsNotCountedAndKeepsItsColour()
        {
            ElementViewModel end = Stretches(7700, 8000).Last();

            Assert.IsTrue(end.IsTooSmallToCount);
            Assert.AreEqual(CheckState.Unpaired, end.State, "still drawn as a load ETABS does not carry");
            Assert.AreEqual(ResultLevel.Failed, end.Outcomes.Single().Level, "the verdict is untouched");
            StringAssert.Contains(end.Outcomes.Single().Summary, "not counted");
            StringAssert.Contains(end.Outcomes.Single().Summary, "0.3 m");
        }

        [TestMethod]
        public void AStretchLongerThanTheLengthIsCounted()
        {
            ElementViewModel end = Stretches(7000, 8000).Last();

            Assert.IsFalse(end.IsTooSmallToCount);
            Assert.IsFalse(end.Outcomes.Single().Summary.Contains("not counted"));
        }

        [TestMethod]
        public void AStretchExactlyOnTheLengthIsCounted()
        {
            Assert.IsFalse(Stretches(7500, 8000).Last().IsTooSmallToCount);
        }

        [TestMethod]
        public void ZeroCountsEveryFindingHoweverShort()
        {
            PrefrencesData.getPrefrencesData().LineLoadMinimumCountedLength = 0;

            Assert.IsFalse(Stretches(7900, 8000).Last().IsTooSmallToCount);
        }

        /// <summary>A stretch that agrees is not a finding, so there is nothing to leave out.</summary>
        [TestMethod]
        public void AShortStretchThatAgreesIsNeverMarked()
        {
            LineLoadResult result = Result(LineLoadState.PartlyCovered);
            result.Stretches.Add(Stretch(0, 300, LineLoadState.Matched, 1.5));
            result.Stretches.Add(Stretch(300, 8000, LineLoadState.NoEtabsLoad, 0));

            ElementViewModel start = ElementViewModelFactory.FromLineLoadResult(result).First();

            Assert.IsFalse(start.IsTooSmallToCount);
        }

        #endregion

        #region what is measured

        /// <summary>
        /// A line drawn whole is one colour along all of it, but its finding is the part that is
        /// wrong: seven metres out over thirty centimetres is a thirty centimetre finding.
        /// </summary>
        [TestMethod]
        public void ALineDrawnWholeIsMeasuredOnThePartThatIsWrong()
        {
            LineLoadResult result = Result(LineLoadState.ValueMismatch);
            result.MismatchLength = 300;
            result.MismatchWanted = 1.5;
            result.MismatchCarried = 2.5;

            ElementViewModel element = ElementViewModelFactory.FromLineLoadResult(result).Single();

            Assert.IsTrue(element.IsTooSmallToCount);
            Assert.AreEqual(CheckState.Failed, element.State, "still drawn red");
        }

        [TestMethod]
        public void ALineDrawnWholeAndWrongAllAlongIsCounted()
        {
            LineLoadResult result = Result(LineLoadState.NoEtabsLoad);

            Assert.IsFalse(ElementViewModelFactory.FromLineLoadResult(result).Single().IsTooSmallToCount);
        }

        [TestMethod]
        public void AShortEtabsLoadNobodyDrewIsNotCounted()
        {
            EtabsLineLoad shortOne = new EtabsLineLoad(null, At(0, 0), At(400, 0), 1.5, 0.6);
            EtabsLineLoad longOne = new EtabsLineLoad(null, At(0, 0), At(2000, 0), 1.5, 3.0);

            Assert.IsTrue(ElementViewModelFactory.FromUnclaimedLineLoad(shortOne).IsTooSmallToCount);
            Assert.IsFalse(ElementViewModelFactory.FromUnclaimedLineLoad(longOne).IsTooSmallToCount);
        }

        #endregion

        #region what the window does with it

        [TestMethod]
        public void AShortStretchIsNotCountedOnTheChipOrTheRail()
        {
            List<ElementViewModel> elements = Stretches(7700, 8000)
                .Concat(Stretches(7000, 8000, revitId: 902))
                .ToList();
            foreach (ElementViewModel element in elements)
            {
                element.DisplayState = element.State;
            }

            FindingTally tally = new FindingTally(() => { });
            tally.SetStories(new List<Story_E> { new Story_E(Story, 10000, Form1.eLength.mm) });
            tally.Update(elements, Story);

            ScopeTab lineLoads = tally.Tabs.First(tab => tab.Mode == ViewMode.LineLoads);
            Assert.AreEqual(1, lineLoads.Failed, "only the metre-long end is worth going to");
            Assert.AreEqual(1, tally.Levels[0].Failed);
        }

        [TestMethod]
        public void AShortStretchIsLeftOutOfTheFindingsList()
        {
            List<ElementViewModel> elements = Stretches(7700, 8000)
                .Concat(Stretches(7000, 8000, revitId: 902))
                .ToList();

            FindingsList drawer = new FindingsList();
            drawer.Update(elements, null, null);

            Assert.AreEqual(1, drawer.Rows.Count, "the list is the job list, and this is not a job");
            Assert.IsFalse(drawer.Rows[0].Element.IsTooSmallToCount);
        }

        [TestMethod]
        public void TheLengthIsOnTheSettingsPanel()
        {
            SettingsViewModel settings = new SettingsViewModel(() => { }, () => "");
            settings.SearchText = "Shortest finding that is counted";

            Assert.IsTrue(settings.Groups.SelectMany(group => group.Items)
                                  .Any(item => item.Key == "LineLoadMinimumCountedLength"));
        }

        #endregion

        #region building the inputs

        /// <summary>
        /// An 8 m line carried as it should be up to <paramref name="loadStops"/> and carrying
        /// nothing from there to its end, as the plan draws it: one element per stretch.
        /// </summary>
        private static List<ElementViewModel> Stretches(double loadStops, double lineEnds, long revitId = 901)
        {
            LineLoadResult result = Result(LineLoadState.PartlyCovered, revitId, lineEnds);
            result.Stretches.Add(Stretch(0, loadStops, LineLoadState.Matched, 1.5));
            result.Stretches.Add(Stretch(loadStops, lineEnds, LineLoadState.NoEtabsLoad, 0));

            return ElementViewModelFactory.FromLineLoadResult(result).ToList();
        }

        private static LineLoadResult Result(LineLoadState state, long revitId = 901, double length = 8000)
        {
            LineLoad_R line = new LineLoad_R(revitId, "MRG_STR-Line Load-TL5",
                                             new[] { At(0, 0), At(length, 0) }, 5, Story);
            return new LineLoadResult(line) { State = state, Expected = 1.5 };
        }

        private static LineLoadStretch Stretch(double from, double to, LineLoadState state, double carried)
        {
            return new LineLoadStretch
            {
                From = from,
                To = to,
                Start = At(from, 0),
                End = At(to, 0),
                State = state,
                Wanted = 1.5,
                Carried = carried,
                Frames = "B1"
            };
        }

        private static MyPoint At(double x, double y)
        {
            return new MyPoint(x, y, 0);
        }

        #endregion
    }
}
