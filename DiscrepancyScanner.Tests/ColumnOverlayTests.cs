using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The second column outline: the ETABS section drawn under a paired column the two models
    /// disagree about geometrically.
    ///
    /// A paired column is drawn once, as the Revit section, coloured by its verdict. Red says a
    /// check failed and stops there - a column 300mm out of place, one turned thirty degrees and
    /// one 100mm too deep are the same red rectangle - so the difference itself has to be read
    /// off the card. Drawing the ETABS section underneath makes the gap between the two outlines
    /// the answer.
    ///
    /// What is pinned here is when the second outline appears and when it does not, because both
    /// halves cost something: missing where it was wanted, the plan says no more than it did
    /// before; drawn everywhere, every column on the floor is two rectangles and the plan reads
    /// as noise.
    /// </summary>
    [TestClass]
    public class ColumnOverlayTests
    {
        private const double FeetToMM = 304.8d;
        private const string Story = "L02";

        /// <summary>
        /// The buffers and switches these tests are written against. The preferences are a
        /// singleton the whole tool shares, so each test says what it depends on rather than
        /// trusting whatever ran before it.
        /// </summary>
        [TestInitialize]
        public void SetPreferences()
        {
            DataContainer.ModelTransformation = new MyTransformation();

            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.ColumnLocationTolerance = 250;
            pref.ColumnSearchRadius = 1000;
            pref.ColumnOrientationTolerance = 5;
            pref.ColumnDimensionTolerance = 5;
            pref.SwapRevitColumnWidthDepth = true;

            pref.RunColumnLocationCheck = true;
            pref.RunColumnOrientationCheck = true;
            pref.RunColumnShapeCheck = true;
            pref.RunColumnDimensionsCheck = true;
            pref.RunColumnTypeMarkCheck = true;

            pref.DrawBothColumnsOnMismatch = true;
        }

        // ------------------------------------------------- the differences worth drawing

        [TestMethod]
        public void AColumnInTheWrongPlaceIsDrawnAgainstTheEtabsOne()
        {
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(500, 0));

            Assert.IsNotNull(column.Counterpart,
                "500mm out of place is exactly the difference two outlines are there to show");
        }

        [TestMethod]
        public void AColumnTurnedTheWrongWayIsDrawnAgainstTheEtabsOne()
        {
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(0, 0, angleDegrees: 30));

            Assert.IsNotNull(column.Counterpart);
        }

        [TestMethod]
        public void AColumnOfTheWrongSizeIsDrawnAgainstTheEtabsOne()
        {
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(0, 0, side: 600));

            Assert.IsNotNull(column.Counterpart);
        }

        [TestMethod]
        public void AColumnOfTheWrongShapeIsDrawnAgainstTheEtabsOne()
        {
            // Round in Revit against rectangular in ETABS. Shape is in with the three the
            // setting names because a project can be checking the shape and not the size, and
            // a round column drawn square with nothing to say so is the worst of both.
            Column_r round = new Column_r(7, Feet(0, 0), 0);
            round.SetDimensions(SectionShape.Round, 0, 0, 400 / FeetToMM);
            round.Story = Story;

            ElementViewModel column = Drawn(Etabs(0, 0), round);

            Assert.IsNotNull(column.Counterpart);
        }

        // ------------------------------------------------- and the ones not worth drawing

        [TestMethod]
        public void AColumnTheTwoModelsAgreeAboutIsDrawnOnce()
        {
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(0, 0));

            Assert.IsNull(column.Counterpart,
                "two outlines on top of each other is twice the ink and no more information");
        }

        [TestMethod]
        public void AColumnInsideTheBuffersIsDrawnOnce()
        {
            // Inside every buffer, so nothing failed. The point of a buffer is that a
            // difference this small is not a difference, and the plan must not say it is.
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(200, 0, side: 403, angleDegrees: 3));

            Assert.IsNull(column.Counterpart);
        }

        [TestMethod]
        public void AWrongTypeMarkAloneIsDrawnOnce()
        {
            // The one check whose failure two outlines could not show: the column is in the
            // right place, the right way round and the right size, and what is wrong with it is
            // a name. Drawing the ETABS section under it would say nothing at all.
            Column_e etabs = Etabs(0, 0);
            etabs.Groups.Add("T-1");

            Column_r revit = Revit(0, 0);
            revit.TypeMark = "T-9";

            ElementViewModel column = Drawn(etabs, revit);

            Assert.IsTrue(column.Outcomes.Any(outcome => outcome.Kind == CheckKind.TypeMark
                                                         && outcome.Level == ResultLevel.Failed),
                          "the test is only worth anything if the type mark did fail");
            Assert.IsNull(column.Counterpart);
        }

        [TestMethod]
        public void ACheckThatWasSwitchedOffDrawsNothing()
        {
            // A switched-off check publishes no result at all, and the outline is drawn off the
            // results. So the column below has to come back drawn once: the tool was not asked
            // where the column stands, and an outline saying it is 500mm out would be answering
            // a question that was declined.
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            try
            {
                pref.RunColumnLocationCheck = false;

                ElementViewModel column = Drawn(Etabs(0, 0), Revit(500, 0));

                Assert.IsNull(column.Counterpart);
            }
            finally
            {
                pref.RunColumnLocationCheck = true;
            }
        }

        [TestMethod]
        public void NothingIsDrawnWhileTheSettingIsOff()
        {
            PrefrencesData.getPrefrencesData().DrawBothColumnsOnMismatch = false;

            ElementViewModel column = Drawn(Etabs(0, 0), Revit(500, 0));

            Assert.IsNull(column.Counterpart);
        }

        // ------------------------------------------------- what the second outline is

        [TestMethod]
        public void TheTwoOutlinesStandWhereTheirOwnModelPutTheColumn()
        {
            // The whole point: the gap between the two outlines is the difference, so one has to
            // be at the ETABS point and the other at the Revit point. Both at the same place and
            // the drawing says nothing.
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(500, 0));

            ColumnElementViewModel revit = (ColumnElementViewModel)column;
            ColumnElementViewModel etabs = (ColumnElementViewModel)column.Counterpart;

            Assert.AreEqual(500d, revit.Center.X, 1d, "the drawn column is the Revit one");
            Assert.AreEqual(0d, etabs.Center.X, 1d, "the outline under it is the ETABS one");
        }

        [TestMethod]
        public void TheOutlineIsTheEtabsSectionAndNotTheRevitOne()
        {
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(0, 0, side: 600));

            ColumnElementViewModel etabs = (ColumnElementViewModel)column.Counterpart;

            // 400 square, so the outline is 400 across. Drawn at the Revit 600 it would be
            // showing the difference as no difference at all.
            Assert.AreEqual(400d, etabs.Geometry.Bounds.Width, 1d);
        }

        [TestMethod]
        public void TheOutlineCarriesNoVerdictOfItsOwn()
        {
            ElementViewModel outline = Drawn(Etabs(0, 0), Revit(500, 0)).Counterpart;

            Assert.IsTrue(outline.IsContext,
                "context is what keeps it out of the picking, the labels and the chip counts");
            Assert.IsTrue(outline.IsCounterpart, "and this is what keeps it out of backdrop grey");
            Assert.AreEqual(0, outline.Outcomes.Count, "the column above it carries the results");
            Assert.IsFalse(outline.CanBeFiled,
                "there is nothing here to review that the column does not already answer for");
            Assert.AreEqual(0L, outline.RevitId, "and nothing of its own to select back in Revit");
        }

        [TestMethod]
        public void TheOutlineIsNotFilled()
        {
            // It is drawn under the column it explains, and a filled one would hide it.
            ElementViewModel outline = Drawn(Etabs(0, 0), Revit(500, 0)).Counterpart;

            Assert.IsFalse(outline.IsFilled);
        }

        [TestMethod]
        public void TheOutlineIsDrawnInTheEtabsColourRatherThanTheBackdropGrey()
        {
            // It is context, so without a colour of its own it would come out in the pale grey
            // a backdrop is drawn in and be lost under the column standing over it. The palette
            // borrows the colour the ETABS model is drawn in on its own, which is a field
            // declared above this one - reorder the two and this would quietly be null, and a
            // null brush draws nothing at all.
            ElementViewModel outline = Drawn(Etabs(0, 0), Revit(500, 0)).Counterpart;

            Assert.IsNotNull(ViewPalette.Counterpart);
            Assert.AreSame(ViewPalette.Counterpart, ViewPalette.ForState(outline));
            Assert.AreNotSame(ViewPalette.Context, ViewPalette.ForState(outline));
        }

        [TestMethod]
        public void TheOutlineIsOnTheSameStoryAsItsColumn()
        {
            // The plan draws one level at a time, and an outline filtered onto another level
            // would simply never appear.
            ElementViewModel column = Drawn(Etabs(0, 0), Revit(500, 0));

            Assert.AreEqual(column.Story, column.Counterpart.Story);
        }

        #region building the two columns

        /// <summary>The drawn element for one pair, as the plan builds it.</summary>
        private static ElementViewModel Drawn(Column_e etabs, Column_r revit)
        {
            return ElementViewModelFactory.FromPairedColumn(new PairedColumn(etabs, revit));
        }

        /// <summary>A square ETABS column standing at a point, in mm.</summary>
        private static Column_e Etabs(double xMM, double yMM, double side = 400,
                                      double angleDegrees = 0)
        {
            FrameData_e frame = new FrameData_e("C-17")
            {
                Start = new MyPoint { X = xMM, Y = yMM, Z = 0 },
                End = new MyPoint { X = xMM, Y = yMM, Z = 3000 },
                Story = Story,
                Rotaion = angleDegrees
            };
            frame._width = side;
            frame._depth = side;
            frame.Shape = SectionShape.Rectangular;
            return new Column_e(frame);
        }

        /// <summary>
        /// The same column read from Revit. Revit geometry arrives in feet and is brought into
        /// ETABS millimetres by the shared transformation, so the test hands over feet the way
        /// the importer does.
        /// </summary>
        private static Column_r Revit(double xMM, double yMM, double side = 400,
                                      double angleDegrees = 0)
        {
            Column_r column = new Column_r(7, Feet(xMM, yMM), angleDegrees * Math.PI / 180d);
            column.SetDimensions(SectionShape.Rectangular, side / FeetToMM, side / FeetToMM, 0);
            column.Story = Story;
            return column;
        }

        private static MyPoint Feet(double xMM, double yMM)
        {
            return new MyPoint { X = xMM / FeetToMM, Y = yMM / FeetToMM, Z = 0 };
        }

        #endregion
    }
}
