using System;
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
    /// Pairing by marking two elements on the plan: what that gesture means, who is allowed to
    /// take part in it, and how the element lying underneath another is reached at all.
    ///
    /// No distance is measured anywhere in here, and that is the point. The automatic pass
    /// already decides by distance; this exists for where it decided wrongly, so two elements
    /// someone has pointed at go together whatever the measurements suggest - and whichever of
    /// them was pointed at first.
    /// </summary>
    [TestClass]
    public class SelectionPairingTests
    {
        #region what marking two elements means

        /// <summary>
        /// The requirement in one test: which of the two was marked first must make no
        /// difference whatsoever. Every case below is asked both ways round and has to give the
        /// same answer, because a person pointing at two elements on a drawing is not also
        /// making a statement about the order they happened to click them in.
        /// </summary>
        [TestMethod]
        public void MarkingTheOtherOneFirstMakesNoDifference()
        {
            // An ETABS element and a Revit element, the plain case.
            AssertSameBothWaysRound("B-1", 0, "", 7702);

            // One already paired, one Revit element on its own.
            AssertSameBothWaysRound("B-1", 5501, "", 7702);

            // One already paired, one ETABS element on its own.
            AssertSameBothWaysRound("B-1", 5501, "B-2", 0);

            // Both already paired.
            AssertSameBothWaysRound("B-1", 5501, "B-2", 7702);

            // Two from the same model, which cannot pair either way round.
            AssertSameBothWaysRound("B-1", 0, "B-2", 0);
            AssertSameBothWaysRound("", 5501, "", 7702);
        }

        [TestMethod]
        public void AnEtabsElementAndARevitElementPairTheOnlyWayTheyCan()
        {
            IList<PairWrite> writes = TwoWayPairing.Between("B-1", 0, "", 7702);

            Assert.AreEqual(1, writes.Count);
            Assert.AreEqual("B-1", writes[0].EtabsUniqueName);
            Assert.AreEqual(7702, writes[0].RevitId);
        }

        /// <summary>
        /// A paired element marked with a spare Revit element means "this one goes with that
        /// one instead". The pairing is written from the paired element's ETABS name onto the
        /// new Revit id - not measured, because the distance is exactly what got it wrong.
        /// </summary>
        [TestMethod]
        public void APairedElementMovesOntoTheRevitElementMarkedWithIt()
        {
            IList<PairWrite> writes = TwoWayPairing.Between("B-1", 5501, "", 7702);

            Assert.AreEqual(1, writes.Count);
            Assert.AreEqual("B-1", writes[0].EtabsUniqueName);
            Assert.AreEqual(7702, writes[0].RevitId,
                            "it must move to the newly marked Revit element, not keep its own");
        }

        /// <summary>
        /// A paired element marked with a spare ETABS element means the counterpart changes
        /// hands: the unpaired ETABS element takes the Revit element the paired one was holding.
        /// </summary>
        [TestMethod]
        public void AnUnpairedEtabsElementTakesTheCounterpartOfThePairedOneMarkedWithIt()
        {
            IList<PairWrite> writes = TwoWayPairing.Between("B-1", 5501, "B-2", 0);

            Assert.AreEqual(1, writes.Count);
            Assert.AreEqual("B-2", writes[0].EtabsUniqueName);
            Assert.AreEqual(5501, writes[0].RevitId);
        }

        /// <summary>
        /// Two elements that are both paired already are not paired with each other. This used
        /// to swap their counterparts, which put down two pairings nobody had looked at - and
        /// two failing pairs side by side are far more often two wrong beams than two crossed
        /// ones. Each is re-paired on its own from its card instead.
        /// </summary>
        [TestMethod]
        public void TwoPairedElementsCannotBePairedWithEachOther()
        {
            Assert.AreEqual(0, TwoWayPairing.Between("B-1", 5501, "B-2", 7702).Count);
            StringAssert.Contains(TwoWayPairing.WhyNot("B-1", 5501, "B-2", 7702), "paired already");
        }

        [TestMethod]
        public void TwoElementsFromTheSameModelCannotPair()
        {
            // Two ETABS elements: nothing to pair with.
            Assert.AreEqual(0, TwoWayPairing.Between("B-1", 0, "B-2", 0).Count);
            Assert.IsTrue(TwoWayPairing.WhyNot("B-1", 0, "B-2", 0).Contains("Revit id"));

            // Two Revit elements: no ETABS side to write the pairing from.
            Assert.AreEqual(0, TwoWayPairing.Between("", 5501, "", 7702).Count);
            Assert.IsTrue(TwoWayPairing.WhyNot("", 5501, "", 7702).Contains("ETABS name"));
        }

        [TestMethod]
        public void APairThatWorksIsNotComplainedAbout()
        {
            Assert.AreEqual("", TwoWayPairing.WhyNot("B-1", 0, "", 7702));
        }

        /// <summary>
        /// The same case asked both ways round has to come back with the same decisions. Order
        /// of the writes is not part of the answer - which ETABS name ends up on which Revit id
        /// is.
        /// </summary>
        private static void AssertSameBothWaysRound(string nameA, long idA, string nameB, long idB)
        {
            string oneWay = Spell(TwoWayPairing.Between(nameA, idA, nameB, idB));
            string other = Spell(TwoWayPairing.Between(nameB, idB, nameA, idA));

            Assert.AreEqual(oneWay, other,
                            "marking them in the other order changed the answer");
        }

        private static string Spell(IList<PairWrite> writes)
        {
            return string.Join(", ", writes
                .Select(write => write.EtabsUniqueName + "=" + write.RevitId)
                .OrderBy(text => text, StringComparer.Ordinal));
        }

        #endregion

        #region who may take a side at all

        /// <summary>
        /// A slab zone is where two outlines overlap rather than an element paired with an
        /// element, so it can take no side in a pairing. The same exclusion the single-element
        /// editor makes, asked of the selection - written once so the two cannot drift apart.
        /// </summary>
        [TestMethod]
        public void ASlabCannotBeMarkedForPairing()
        {
            Assert.IsFalse(PairingEditor.CanBeMarked(
                new Stub(ElementKind.Slab, "S-1", revitId: 900)));
            Assert.IsFalse(PairingEditor.CanEdit(
                new Stub(ElementKind.Slab, "S-1", revitId: 900)));
        }

        [TestMethod]
        public void EitherSideOnItsOwnMayBeMarked()
        {
            // ETABS only: it can be the element whose pairing gets written.
            Assert.IsTrue(PairingEditor.CanBeMarked(
                new Stub(ElementKind.Column, "C-1", revitId: 0)));

            // Revit only: it cannot own a pairing, but it can be the thing paired with, which
            // is exactly what a selection needs it for.
            Assert.IsTrue(PairingEditor.CanBeMarked(
                new Stub(ElementKind.Column, "", revitId: 101)));
            Assert.IsFalse(PairingEditor.CanEdit(
                new Stub(ElementKind.Column, "", revitId: 101)));
        }

        [TestMethod]
        public void ContextIsOnThePlanToSayWhereThingsAreAndCannotBeMarked()
        {
            Stub greyed = new Stub(ElementKind.Column, "C-1", revitId: 101);
            greyed.IsContext = true;

            Assert.IsFalse(PairingEditor.CanBeMarked(greyed));
        }

        #endregion

        #region the names the positions are looked up by

        /// <summary>
        /// Pairing a selection places each element where its own model has it, and finds those
        /// positions by looking the element up in the imported data - by ETABS name for one
        /// side, by Revit id for the other.
        ///
        /// That only works while the name a view model carries is the same name the import
        /// files the element under. These pin that agreement per kind, because it is spelled
        /// differently in each of the three containers - EtabsUniqueName, EtabsUname,
        /// UniqueName - and a rename on one of them would not break a build, would not fail any
        /// other test, and would quietly send every paired element of that kind to the
        /// fallback position instead.
        /// </summary>
        [TestMethod]
        public void APairedColumnIsFiledUnderTheNameTheEtabsImportUses()
        {
            Column_e etabs = EtabsColumn("C-17", 1000, 2000);
            Column_r revit = RevitColumn(5501, 1100, 2000);

            ElementViewModel element =
                ElementViewModelFactory.FromPairedColumn(new PairedColumn(etabs, revit));

            Assert.AreEqual(etabs.EtabsUniqueName, element.EtabsUniqueName);
            Assert.AreEqual(revit.RevitID, element.RevitId);
        }

        [TestMethod]
        public void APairedBeamIsFiledUnderTheNameTheEtabsImportUses()
        {
            FrameData_e etabs = new FrameData_e("B-9")
            {
                Start_e = new MyPoint { X = 0, Y = 0, Z = 0 },
                END_e = new MyPoint { X = 6000, Y = 0, Z = 0 },
                Story = "L03"
            };
            FrameData_r revit = new FrameData_r(
                7702,
                new MyPoint { X = 100 / FeetToMM, Y = 0, Z = 0 },
                new MyPoint { X = 6100 / FeetToMM, Y = 0, Z = 0 },
                6000 / FeetToMM,
                new MyPoint { X = 1, Y = 0, Z = 0 });

            ElementViewModel element =
                ElementViewModelFactory.FromPairedFrame(new PairedFrame(etabs, revit));

            Assert.AreEqual(etabs.EtabsUname, element.EtabsUniqueName);
            Assert.AreEqual(revit.RevitID, element.RevitId);
        }

        [TestMethod]
        public void APairedWallIsFiledUnderTheNameTheEtabsImportUses()
        {
            Wall_E etabs = new Wall_E("W-4", new[]
            {
                At(0, 0, 0), At(0, 0, 3000), At(4000, 0, 3000), At(4000, 0, 0)
            }, "L03", 200);

            Wall_R revit = new Wall_R(8803,
                new MyPoint { X = 0, Y = 0, Z = 0 },
                new MyPoint { X = 4000 / FeetToMM, Y = 0, Z = 0 });

            ElementViewModel element =
                ElementViewModelFactory.FromPairedWall(new PairedWall(etabs, revit));

            Assert.AreEqual(etabs.UniqueName, element.EtabsUniqueName);
            Assert.AreEqual(revit.RevitID, element.RevitId);
        }

        #endregion

        #region helpers

        private const double FeetToMM = 304.8d;

        /// <summary>
        /// The tolerances these tests run against. The preferences are a singleton the whole
        /// tool shares, so this states what it depends on rather than trusting what ran before.
        /// </summary>
        [TestInitialize]
        public void SetTolerances()
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            pref.ColumnLocationTolerance = 250;
            pref.ColumnSearchRadius = 1000;
        }

        private static Column_e EtabsColumn(string uniqueName, double xMM, double yMM)
        {
            FrameData_e frame = new FrameData_e(uniqueName)
            {
                Start = new MyPoint { X = xMM, Y = yMM, Z = 0 },
                End = new MyPoint { X = xMM, Y = yMM, Z = 3000 }
            };
            return new Column_e(frame);
        }

        /// <summary>Revit geometry arrives in feet, the same way the importer hands it over.</summary>
        private static Column_r RevitColumn(long revitId, double xMM, double yMM)
        {
            return new Column_r(revitId,
                new MyPoint { X = xMM / FeetToMM, Y = yMM / FeetToMM, Z = 0 }, 0);
        }

        private static Joint At(double x, double y, double z)
        {
            return new Joint("p", new MyPoint(x, y, z), Form1.eLength.mm);
        }

        /// <summary>
        /// An element with no geometry behind it. Nothing here is about the shape it is drawn
        /// as - only about which side of a pairing it is allowed to take.
        /// </summary>
        private sealed class Stub : ElementViewModel
        {
            public Stub(ElementKind kind, string etabsUniqueName, long revitId)
                : base(kind, ElementSource.Paired, CheckState.Passed, etabsUniqueName, "L03",
                       new List<ResultField>())
            {
                EtabsUniqueName = etabsUniqueName;
                RevitId = revitId;
            }

            public override Point LabelAnchor => new Point(0, 0);

            public override double DistanceTo(Point modelPoint) => 0;

            protected override Geometry BuildGeometry() => Geometry.Empty;
        }

        #endregion
    }
}
