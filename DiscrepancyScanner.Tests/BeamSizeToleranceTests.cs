using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A paired beam's width and depth judged by the beam size buffer under Set up, rather than
    /// by the five millimetres the check used to have written into it.
    /// </summary>
    [TestClass]
    public class BeamSizeToleranceTests
    {
        private const double FeetToMM = 304.8d;

        private double before;
        private bool runBefore;

        [TestInitialize]
        public void Remember()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            before = pref.BeamSizeTolerance;
            runBefore = pref.RunBeamMarkCheck;
            pref.RunBeamMarkCheck = true;
        }

        [TestCleanup]
        public void PutBack()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.BeamSizeTolerance = before;
            pref.RunBeamMarkCheck = runBefore;
        }

        [TestMethod]
        public void ADepthInsideTheBufferAgrees()
        {
            PrefrencesData.getPrefrencesData().BeamSizeTolerance = 25;

            PairedFrame pair = Pair(etabsDepth: 600, revitDepth: 620);

            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, "Depth"));
            Assert.AreEqual(IDrawAble.designState.ignore, pair.MyDesignState);
        }

        [TestMethod]
        public void ADepthPastTheBufferFails()
        {
            PrefrencesData.getPrefrencesData().BeamSizeTolerance = 10;

            PairedFrame pair = Pair(etabsDepth: 600, revitDepth: 620);

            Assert.AreEqual(ResultLevel.Failed, LevelOf(pair, "Depth"));
            Assert.AreEqual(ResultLevel.Passed, LevelOf(pair, "Width"));
            Assert.AreEqual(IDrawAble.designState.Check_Concrete, pair.MyDesignState);
        }

        private static ResultLevel LevelOf(PairedFrame pair, string token)
        {
            CheckOutcome outcome = pair.Outcomes.FirstOrDefault(o => o.Token == token);
            Assert.IsNotNull(outcome, $"the beam publishes a {token} result");
            return outcome.Level;
        }

        private static PairedFrame Pair(double etabsDepth, double revitDepth)
        {
            FrameData_e etabs = new FrameData_e("B1")
            {
                Start_e = new MyPoint { X = 0, Y = 0, Z = 0 },
                END_e = new MyPoint { X = 6000, Y = 0, Z = 0 },
                Story = "L01"
            };
            etabs._width = 300;
            etabs._depth = etabsDepth;

            FrameData_r revit = new FrameData_r(
                501,
                new MyPoint { X = 0, Y = 0, Z = 0 },
                new MyPoint { X = 6000 / FeetToMM, Y = 0, Z = 0 },
                6000 / FeetToMM,
                new MyPoint { X = 1, Y = 0, Z = 0 });
            revit._width = 300;
            revit._depth = revitDepth;

            return new PairedFrame(etabs, revit);
        }
    }
}
