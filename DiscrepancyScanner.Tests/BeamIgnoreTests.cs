using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// The match behind every "leave this out" list in the tool, and the two beam lists that
    /// were the reason for pulling it out of the Revit engine - which is where it still runs,
    /// against a type name and against a reinforcement mark.
    ///
    /// Nothing here opens a model: the filter is a string test, and the beams it drops are
    /// dropped for what they are called rather than for where they are.
    /// </summary>
    [TestClass]
    public class BeamIgnoreTests
    {
        private static string[] Names(string typed)
        {
            return PrefrencesData.SplitTypeNamePhrases(typed);
        }

        private static bool Ignored(string text, string typed)
        {
            string matched;
            return PrefrencesData.MatchesAnyPhrase(text, Names(typed), out matched);
        }

        [TestMethod]
        public void TypeNamesCarryingTheEntryAreLeftOut()
        {
            Assert.IsTrue(Ignored("RB1", "RB"));
            Assert.IsTrue(Ignored("RB-250x400", "RB"));
            Assert.IsTrue(Ignored("rb2", "RB"), "the match ignores case");
        }

        [TestMethod]
        public void MarksAreMatchedTheSameWay()
        {
            Assert.IsTrue(Ignored("RB-01", "RB"));
            Assert.IsFalse(Ignored("B-01", "RB"));
            Assert.IsFalse(Ignored("", "RB"), "a beam carrying no mark is not a beam to leave out");
        }

        /// <summary>
        /// The cost of matching two letters anywhere in a name, written down rather than
        /// wished away: a kerb type goes with the rib beams. This is why the import names what
        /// each entry took out instead of only counting them.
        /// </summary>
        [TestMethod]
        public void TwoLettersMatchAnywhereIncludingWhereItIsNotWanted()
        {
            Assert.IsTrue(Ignored("KERB-200", "RB"));
            Assert.IsTrue(Ignored("Curb Upstand", "RB"));

            // Lengthening it to "RB-" is no escape, because "KERB-200" carries that too. The
            // way out is a name the kerb type has not got - the rib series itself.
            Assert.IsTrue(Ignored("KERB-200", "RB-"));
            Assert.IsFalse(Ignored("KERB-200", "RB1"));
            Assert.IsTrue(Ignored("RB1-250x400", "RB1"));
        }

        [TestMethod]
        public void TheFirstNameToMatchIsTheOneReported()
        {
            string matched;
            Assert.IsTrue(PrefrencesData.MatchesAnyPhrase(
                "Secondary RB-01", Names("Secondary Beam, RB"), out matched));
            Assert.AreEqual("RB", matched, "the list is walked in order and one name answers for it");
        }

        [TestMethod]
        public void ANameWithASpaceStaysOneName()
        {
            Assert.IsTrue(Ignored("Rib Beam 250", "Rib Beam"));
            Assert.IsFalse(Ignored("Transfer Beam 800", "Rib Beam"),
                           "split on spaces this would have taken every beam with it");
        }

        [TestMethod]
        public void AnEmptyListLeavesNothingOut()
        {
            Assert.IsFalse(Ignored("RB1", ""));
            Assert.IsFalse(Ignored("RB1", "   "));

            string matched;
            Assert.IsFalse(PrefrencesData.MatchesAnyPhrase("RB1", null, out matched));
            Assert.IsNull(matched);
        }

        /// <summary>
        /// Both lists ship holding "RB", so a beam named for it is left out of a project that
        /// has never been near the settings window.
        ///
        /// Built through the private constructor rather than through
        /// <see cref="DiscrepancyScanner.Wpf.UserSettings.Defaults"/>, which lays the user's
        /// own saved defaults over the compiled-in ones - this is about what the tool ships
        /// with, and it has to read the same on a machine where someone has saved their own.
        /// </summary>
        [TestMethod]
        public void BothListsShipHoldingRB()
        {
            PrefrencesData shipped = (PrefrencesData)System.Activator.CreateInstance(
                typeof(PrefrencesData), nonPublic: true);

            Assert.AreEqual("RB", shipped.BeamTypeNamesToIgnore);
            Assert.AreEqual("RB", shipped.BeamMarksToIgnore);
        }
    }
}
