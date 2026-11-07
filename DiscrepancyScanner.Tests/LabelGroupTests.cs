using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The label picker, which decides what can be stamped on the plan.
    ///
    /// The four kinds name their results the same things. A column and a beam both publish
    /// "Check state"; all four publish "Revit id". Listed in one flat list those are identical
    /// lines with nothing to tell them apart, and ticking one of them stamps a value on one kind
    /// of element while looking as though it should have stamped it on all of them. The values
    /// themselves were never mixed up - the keys read Column.State and Frame.State - so this is
    /// about the picker, not the drawing.
    /// </summary>
    [TestClass]
    public class LabelGroupTests
    {
        [TestMethod]
        public void TwoKindsCallingAResultTheSameThingGetARowEach()
        {
            IList<LabelFieldGroup> groups = Build(
                Column(Field(ColumnResultKeys.State, "Check state")),
                Beam(Field(FrameResultKeys.State, "Check state")));

            Assert.AreEqual(2, groups.Count);
            Assert.AreEqual("Columns", groups[0].Title);
            Assert.AreEqual("Beams", groups[1].Title);

            Assert.AreEqual(ColumnResultKeys.State, groups[0].Options.Single().Key);
            Assert.AreEqual(FrameResultKeys.State, groups[1].Options.Single().Key);
        }

        /// <summary>
        /// The point of the grouping: ticking one leaves the other alone. Before, the two were
        /// one row, so a beam's check state could not be stamped without a column's.
        /// </summary>
        [TestMethod]
        public void TickingOneOfThemDoesNotTickTheOther()
        {
            IList<LabelFieldGroup> groups = Build(
                Column(Field(ColumnResultKeys.State, "Check state")),
                Beam(Field(FrameResultKeys.State, "Check state")));

            groups[0].Options[0].IsSelected = true;

            Assert.AreEqual(1, groups[0].SelectedCount);
            Assert.AreEqual(0, groups[1].SelectedCount, "the beams were not asked for");
            Assert.IsFalse(groups[1].HasSelection);
        }

        /// <summary>
        /// Groups come out in the order the chips along the top put them, whatever order the
        /// elements happened to arrive in, so the picker and the scope bar read the same way.
        /// </summary>
        [TestMethod]
        public void GroupsAreInTheOrderTheChipsAreIn()
        {
            IList<LabelFieldGroup> groups = Build(
                Slab(Field(SlabResultKeys.Thickness, "Thickness")),
                Beam(Field(FrameResultKeys.Width, "Width")),
                Column(Field(ColumnResultKeys.LongSide, "Long side R / E")),
                Wall(Field(WallResultKeys.Thickness, "Thickness R / E")));

            CollectionAssert.AreEqual(
                new[] { "Columns", "Beams", "Walls", "Slabs" },
                groups.Select(group => group.Title).ToArray());
        }

        /// <summary>
        /// Context is what is drawn grey behind the kind being checked. It carries no result
        /// that is on show and the canvas leaves it unlabelled, so a group for it would be a
        /// set of ticks that do nothing whatever is done to them.
        /// </summary>
        [TestMethod]
        public void WhatIsDrawnAsContextIsNotOfferedToLabel()
        {
            Stub beam = Beam(Field(FrameResultKeys.Width, "Width"));
            beam.IsContext = true;

            IList<LabelFieldGroup> groups = Build(
                Column(Field(ColumnResultKeys.LongSide, "Long side R / E")), beam);

            Assert.AreEqual(1, groups.Count);
            Assert.AreEqual("Columns", groups.Single().Title);
        }

        /// <summary>
        /// A tick is remembered by key, and the keys were already kept apart per kind, so what
        /// was saved against an older version comes back on the right group.
        /// </summary>
        [TestMethod]
        public void ATickIsRestoredOntoItsOwnKind()
        {
            HashSet<string> remembered = new HashSet<string> { FrameResultKeys.State };

            IList<LabelFieldGroup> groups = LabelGrouping.Build(
                new List<ElementViewModel>
                {
                    Column(Field(ColumnResultKeys.State, "Check state")),
                    Beam(Field(FrameResultKeys.State, "Check state"))
                },
                Title, remembered.Contains, null);

            Assert.AreEqual(0, groups[0].SelectedCount, "the columns were never ticked");
            Assert.AreEqual(1, groups[1].SelectedCount);
        }

        /// <summary>
        /// Clearing one group is how a picker with four groups in it stays usable: the whole
        /// point of separating them is undone if the only way back is to clear the lot.
        /// </summary>
        [TestMethod]
        public void ClearingOneGroupLeavesTheOthersStamped()
        {
            IList<LabelFieldGroup> groups = Build(
                Column(Field(ColumnResultKeys.State, "Check state")),
                Beam(Field(FrameResultKeys.State, "Check state")));

            groups[0].Options[0].IsSelected = true;
            groups[1].Options[0].IsSelected = true;

            groups[0].ClearCommand.Execute(null);

            Assert.AreEqual(0, groups[0].SelectedCount);
            Assert.AreEqual(1, groups[1].SelectedCount);
        }

        /// <summary>
        /// One element per kind is enough to offer the field; a level with two thousand columns
        /// on it must not produce two thousand identical rows.
        /// </summary>
        [TestMethod]
        public void AFieldEveryElementCarriesIsOfferedOnce()
        {
            IList<LabelFieldGroup> groups = Build(
                Column(Field(ColumnResultKeys.State, "Check state")),
                Column(Field(ColumnResultKeys.State, "Check state")),
                Column(Field(ColumnResultKeys.State, "Check state")));

            Assert.AreEqual(1, groups.Single().Options.Count);
        }

        #region building the inputs

        private static IList<LabelFieldGroup> Build(params ElementViewModel[] elements)
        {
            return LabelGrouping.Build(elements, Title, key => false, null);
        }

        /// <summary>
        /// The real title comes off the scope chips, which need a live view model. These tests
        /// are about which fields land in which group, so a fixed name is enough.
        /// </summary>
        private static string Title(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Column: return "Columns";
                case ElementKind.Beam: return "Beams";
                case ElementKind.Wall: return "Walls";
                default: return "Slabs";
            }
        }

        private static ResultField Field(string key, string label)
        {
            return new ResultField(key, label, "whatever");
        }

        private static Stub Column(params ResultField[] fields)
        {
            return new Stub(ElementKind.Column, fields);
        }

        private static Stub Beam(params ResultField[] fields)
        {
            return new Stub(ElementKind.Beam, fields);
        }

        private static Stub Wall(params ResultField[] fields)
        {
            return new Stub(ElementKind.Wall, fields);
        }

        private static Stub Slab(params ResultField[] fields)
        {
            return new Stub(ElementKind.Slab, fields);
        }

        /// <summary>An element with no geometry: none of this needs the shape it is drawn as.</summary>
        private sealed class Stub : ElementViewModel
        {
            public Stub(ElementKind kind, params ResultField[] fields)
                : base(kind, ElementSource.Paired, CheckState.Passed, "E-1", "L03",
                       new List<ResultField>(fields))
            {
            }

            public override Point LabelAnchor => new Point(0, 0);

            public override double DistanceTo(Point modelPoint) => 0;

            protected override Geometry BuildGeometry() => Geometry.Empty;
        }

        #endregion
    }
}
