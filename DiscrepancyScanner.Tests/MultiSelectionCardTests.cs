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
    /// The card for several elements selected at once. What they agree on is printed as it is,
    /// what they differ on reads "Varies", and a decision reads as standing only where every
    /// one of them carries it - so the button that takes it back never takes it back from an
    /// element that never had it.
    /// </summary>
    [TestClass]
    public class MultiSelectionCardTests
    {
        [TestMethod]
        public void AValueTheyShareIsShownAndOneTheyDoNotReadsVaries()
        {
            Stub first = new Stub("B-1", "L03", "300x600",
                                  new CheckOutcome(CheckKind.Dimensions, ResultLevel.Failed, "",
                                                   etabs: "300x600", revit: "300x700"));
            Stub second = new Stub("B-2", "L03", "300x600",
                                   new CheckOutcome(CheckKind.Dimensions, ResultLevel.Passed, "",
                                                    etabs: "300x600", revit: "300x600"));

            ElementCard card = new ElementCard(new List<ElementViewModel> { first, second }, _ => null);

            Assert.IsTrue(card.IsMultiple);
            Assert.AreEqual("2 BEAMS", card.Kind);
            Assert.AreEqual("L03", card.Subtitle, "one level shared by both");

            CheckRow row = card.Rows.Single();
            Assert.AreEqual("300x600", row.Etabs, "the ETABS side agrees");
            Assert.AreEqual("Varies", row.Revit, "the Revit side does not");
            Assert.AreEqual(ResultLevel.Failed, row.Level, "the worst verdict is the colour");
            StringAssert.Contains(row.Summary, "1 fail");
            StringAssert.Contains(row.Summary, "1 agree");

            Assert.AreEqual("300x600", card.Facts.Single(fact => fact.Label == "Section").Value);
            Assert.AreEqual(1, card.Failed, "counted in elements, not checks");
        }

        [TestMethod]
        public void ADecisionStandsOnlyWhereEveryOneCarriesIt()
        {
            Stub first = new Stub("B-1", "L03", "300x600");
            Stub second = new Stub("B-2", "L04", "300x700");
            List<ElementViewModel> both = new List<ElementViewModel> { first, second };

            ReviewNote accepted = new ReviewNote { Verdict = HumanVerdict.Accepted, Note = "screed" };

            ElementCard oneAccepted = new ElementCard(both, element => element == first ? accepted : null);
            Assert.AreEqual(HumanVerdict.None, oneAccepted.Verdict);
            Assert.AreEqual("ACCEPT ALL", oneAccepted.AcceptButtonText);
            Assert.AreEqual("", oneAccepted.NoteToEdit, "different notes are not offered for editing");
            Assert.AreEqual("2 levels", oneAccepted.Subtitle);
            Assert.AreEqual("Varies", oneAccepted.Facts.Single(fact => fact.Label == "Section").Value);

            ElementCard allAccepted = new ElementCard(both, element => accepted);
            Assert.AreEqual(HumanVerdict.Accepted, allAccepted.Verdict);
            Assert.AreEqual("ALL ACCEPTED ✓", allAccepted.AcceptButtonText);
            Assert.AreEqual("screed", allAccepted.NoteToEdit, "a note they all share is the one edited");
        }

        [TestMethod]
        public void OneElementStillGetsTheCardItAlwaysHad()
        {
            ElementCard card = new ElementCard(new Stub("B-1", "L03", "300x600"), null);

            Assert.IsTrue(card.IsSingle);
            Assert.AreEqual("ACCEPT", card.AcceptButtonText);
            Assert.AreEqual("ADD A NOTE", card.NoteButtonText);
        }

        private sealed class Stub : ElementViewModel
        {
            // Failed rather than passed so there is a difference to accept: an acceptance of an
            // element that agrees settles nothing and is not read as one.
            public Stub(string name, string story, string section, params CheckOutcome[] outcomes)
                : base(ElementKind.Beam, ElementSource.Paired, CheckState.Failed, name, story,
                       new List<ResultField> { new ResultField("Section", "Section", section) })
            {
                EtabsUniqueName = name;
                Outcomes = outcomes.ToList();
            }

            public override Point LabelAnchor => new Point(0, 0);

            public override double DistanceTo(Point modelPoint) => 0;

            protected override Geometry BuildGeometry() => Geometry.Empty;
        }
    }
}
