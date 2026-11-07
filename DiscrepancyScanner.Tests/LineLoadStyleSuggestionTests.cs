using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiscrepancyScanner;

namespace DiscrepancyScanner.Tests
{
    /// <summary>
    /// Working out what the line load style setting should say, from the style names on a real
    /// loading plan.
    ///
    /// The styles here are the seven actually found on BASEMENT 1 - LOADING PLAN of the
    /// reference project, with the line counts they actually had. That matters: the whole point
    /// of this is to answer a question nobody could answer from an empty plan, and a made up
    /// set of names would not have the one feature that makes it hard - a style drawn on the
    /// same view, in the same house convention, that is not a load at all.
    /// </summary>
    [TestClass]
    public class LineLoadStyleSuggestionTests
    {
        /// <summary>What was on that view, style name against how many lines carried it.</summary>
        private static Dictionary<string, int> RealView()
        {
            return new Dictionary<string, int>
            {
                { "MRG_STR-Line Load-TL1", 173 },
                { "MRG_STR-Line Load-TL2", 106 },
                { "MRG_STR-Line Load-TL3", 60 },
                { "MRG_STR-Line Load-TL10", 31 },
                { "MRG_STR-Line Load-TL5", 25 },
                { "MRG_STR-Void", 22 },
                { "MRG_STR-Line Load-TL6", 22 }
            };
        }

        /// <summary>
        /// The answer the tool should have handed over instead of an empty plan: "Line Load"
        /// catches six of the seven styles and 417 of the 439 lines, leaving the void lines out.
        /// </summary>
        [TestMethod]
        public void TheWordingThatCatchesTheLoadsAndNotTheVoidLinesIsOffered()
        {
            IList<LineLoadStyleSuggestion.Candidate> ranked =
                LineLoadStyleSuggestion.Rank(RealView());

            LineLoadStyleSuggestion.Candidate lineLoad = ranked.FirstOrDefault(
                candidate => candidate.Wording == "Line Load");

            Assert.IsNotNull(lineLoad, "\"Line Load\" should be one of the wordings offered: "
                                       + string.Join(" | ", ranked.Select(c => c.Wording)));
            Assert.AreEqual(6, lineLoad.Styles, "it should catch the six load styles");
            Assert.AreEqual(417, lineLoad.Lines, "and the 417 lines they account for");
        }

        /// <summary>
        /// The wide wording is offered too, and its score is what says not to use it: it catches
        /// the void lines as well. That is the judgement the counts exist to support.
        /// </summary>
        [TestMethod]
        public void TheHouseWideWordingIsOfferedWithAScoreThatShowsItCatchesTooMuch()
        {
            IList<LineLoadStyleSuggestion.Candidate> ranked =
                LineLoadStyleSuggestion.Rank(RealView());

            LineLoadStyleSuggestion.Candidate wide = ranked.FirstOrDefault(
                candidate => candidate.Wording == "MRG_STR");

            Assert.IsNotNull(wide, "the house prefix should be offered as well");
            Assert.AreEqual(7, wide.Styles, "it catches every style on the view, void lines and all");
        }

        /// <summary>
        /// Every wording offered has to work when it is typed in, which means it has to carry
        /// the name's own punctuation. A wording printed as "MRG STR" for a style called
        /// "MRG_STR" would read perfectly and match nothing.
        /// </summary>
        [TestMethod]
        public void EveryWordingOfferedActuallyMatchesSomethingOnTheView()
        {
            Dictionary<string, int> view = RealView();

            foreach (LineLoadStyleSuggestion.Candidate candidate in LineLoadStyleSuggestion.Rank(view))
            {
                Assert.IsTrue(
                    view.Keys.Any(style => LineLoad_R.IsLineLoadStyle(style, candidate.Wording)),
                    $"\"{candidate.Wording}\" was offered and matches nothing");
            }
        }

        /// <summary>
        /// Widest meaning most words, not most characters: "Line Load" is longer than "MRG_STR"
        /// and says less about the drawing. Word count is what tracks how specific a wording is,
        /// so the narrowest match is read first and the catch-all is at the foot of the table.
        /// </summary>
        [TestMethod]
        public void TheWordingsAreOfferedWidestFirst()
        {
            IList<LineLoadStyleSuggestion.Candidate> ranked =
                LineLoadStyleSuggestion.Rank(RealView());

            Assert.IsTrue(ranked.Count > 1);

            for (int i = 1; i < ranked.Count; i++)
            {
                Assert.IsTrue(WordCount(ranked[i - 1].Wording) >= WordCount(ranked[i].Wording),
                              $"\"{ranked[i - 1].Wording}\" should not come before "
                              + $"\"{ranked[i].Wording}\"");
            }
        }

        private static int WordCount(string wording)
        {
            return wording.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        /// <summary>Two letters is a coincidence, not a convention.</summary>
        [TestMethod]
        public void VeryShortWordingsAreNotOffered()
        {
            foreach (string wording in LineLoadStyleSuggestion.WordRuns("MRG_STR-Line Load-TL1"))
            {
                Assert.IsTrue(wording.Length >= 3, $"\"{wording}\" is too short to be a convention");
            }
        }

        [TestMethod]
        public void NothingIsOfferedForAViewWithNoLinesOnIt()
        {
            Assert.AreEqual(0, LineLoadStyleSuggestion.Rank(new Dictionary<string, int>()).Count);
            Assert.AreEqual(0, LineLoadStyleSuggestion.Rank(null).Count);
        }

        /// <summary>The table is what actually reaches the person, so it is worth seeing built.</summary>
        [TestMethod]
        public void TheTableNamesTheWordingAndWhatItWouldCatch()
        {
            StringBuilder report = new StringBuilder();
            LineLoadStyleSuggestion.Write(report, RealView());

            string written = report.ToString();

            Assert.IsTrue(written.Contains("Line Load"), written);
            Assert.IsTrue(written.Contains("6 of 7"), "the style count should be written out: " + written);
            Assert.IsTrue(written.Contains("417 of 439"), "and the line count: " + written);
        }
    }
}
