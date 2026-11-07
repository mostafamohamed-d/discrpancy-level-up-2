using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// A Revit column modelled in one piece from the ground to two floors up, against ETABS,
    /// which splits it at every story.
    /// </summary>
    [TestClass]
    public class ColumnStorySplitTests
    {
        private const double FeetToMM = 304.8;

        private static List<Story_E> Stories()
        {
            return new List<Story_E>
            {
                new Story_E("Base", 200, Form1.eLength.mm),
                new Story_E("1ST FLOOR", 4720, Form1.eLength.mm),
                new Story_E("2ND FLOOR", 9200, Form1.eLength.mm)
            };
        }

        private static Column_r RevitColumn(double bottomMM, double topMM)
        {
            Column_r column = new Column_r(7, new MyPoint { X = 0, Y = 0, Z = 0 }, 0)
            {
                BottomElevationInFeet = bottomMM / FeetToMM,
                TopElevationInFeet = topMM / FeetToMM
            };
            column.AssignStory(Stories());
            return column;
        }

        private static Column_e EtabsColumn(string name, string story)
        {
            FrameData_e frame = new FrameData_e(name)
            {
                Start = new MyPoint { X = 0, Y = 0, Z = 0 },
                End = new MyPoint { X = 0, Y = 0, Z = 3000 },
                Story = story
            };
            frame._width = 400;
            frame._depth = 1200;
            frame.Shape = SectionShape.Rectangular;
            return new Column_e(frame);
        }

        [TestMethod]
        public void ColumnRunningThroughAFloorIsOnePiecePerStory()
        {
            // Ground to 370 below level 2: through 1ST FLOOR, named after 2ND FLOOR.
            List<Column_r> pieces = RevitColumn(0, 8830).SplitAtStories(Stories());

            CollectionAssert.AreEquivalent(new[] { "2ND FLOOR", "1ST FLOOR" },
                                           pieces.Select(piece => piece.Story).ToArray());
            Assert.IsTrue(pieces.All(piece => piece.RevitID == 7));
        }

        [TestMethod]
        public void ColumnWithinOneStoryIsNotSplit()
        {
            // The base story 200 mm above the foot is not a floor the column runs through.
            List<Column_r> pieces = RevitColumn(0, 4350).SplitAtStories(Stories());

            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual("1ST FLOOR", pieces[0].Story);
        }

        [TestMethod]
        public void BothEtabsPiecesPairWithTheOneRevitColumn()
        {
            List<Column_r> pieces = RevitColumn(0, 8830).SplitAtStories(Stories());
            ColumnCompareEngine engine = new ColumnCompareEngine();
            engine.StartComparison(new List<Column_e> { EtabsColumn("7066", "1ST FLOOR"), EtabsColumn("7067", "2ND FLOOR") },
                                   pieces);

            Assert.AreEqual(2, engine.PairedColumns.Count);
            Assert.AreEqual(0, engine.UnmatchedEtabsColumns.Count);
            Assert.AreEqual(0, engine.UnmatchedRevitColumns.Count);
        }

        [TestMethod]
        public void MessagesOnAPieceStayOnThatPiece()
        {
            List<Column_r> pieces = RevitColumn(0, 8830).SplitAtStories(Stories());
            int before = pieces[0].Messages.Count;

            pieces[1].AddMessage(new MyPoint(), "only here", MessageType.State);

            Assert.AreEqual(before, pieces[0].Messages.Count);
        }
    }
}
