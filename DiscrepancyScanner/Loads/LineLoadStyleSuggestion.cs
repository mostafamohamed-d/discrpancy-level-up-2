using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Works out what the line load style setting could say, from the style names actually on
    /// a loading plan.
    ///
    /// The check reads a line as a load only when its style name says what the setting says,
    /// which leaves one way to fail that nobody can recover from unaided: a drawing whose
    /// styles are named in some way the setting does not describe produces a plan with nothing
    /// on it, and an empty plan says nothing about which of the many possible reasons it was.
    ///
    /// So rather than describe that, this works out the answer. Every wording the most common
    /// style name contains is tried against every style on the view, and each is scored by what
    /// it would catch.
    ///
    /// Scored rather than narrowed to one answer, because the choice is a judgement nobody here
    /// can make: the widest wording catches the void lines drawn beside the loads, the narrowest
    /// catches one load type out of six. Only somebody who knows the drawing can pick, and the
    /// counts are what they need to pick with.
    ///
    /// Nothing here touches Revit, which is what lets it be tested against a real project's
    /// style names without a model open.
    /// </summary>
    public static class LineLoadStyleSuggestion
    {
        /// <summary>One wording that could go in the setting, and what it would catch.</summary>
        public sealed class Candidate
        {
            public Candidate(string wording, int styles, int lines)
            {
                Wording = wording ?? "";
                Styles = styles;
                Lines = lines;
            }

            /// <summary>The text to type into the setting, exactly as it would be typed.</summary>
            public string Wording { get; }

            /// <summary>How many of the view's styles this would read as line loads.</summary>
            public int Styles { get; }

            /// <summary>How many lines those styles account for.</summary>
            public int Lines { get; }
        }

        /// <summary>
        /// The wordings worth offering, widest first, judged with the very matcher the check
        /// uses - so a wording that reads well here is one that works when it is typed in.
        /// </summary>
        public static IList<Candidate> Rank(IDictionary<string, int> linesByStyle)
        {
            List<Candidate> candidates = new List<Candidate>();

            if (linesByStyle == null || linesByStyle.Count == 0)
            {
                return candidates;
            }

            string busiest = linesByStyle.OrderByDescending(entry => entry.Value).First().Key;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string wording in WordRuns(busiest))
            {
                if (!seen.Add(wording))
                {
                    continue;
                }

                List<KeyValuePair<string, int>> caught = linesByStyle
                    .Where(entry => LineLoad_R.IsLineLoadStyle(entry.Key, wording))
                    .ToList();

                if (caught.Count == 0)
                {
                    continue;
                }

                candidates.Add(new Candidate(wording, caught.Count, caught.Sum(entry => entry.Value)));
            }

            return candidates;
        }

        /// <summary>Writes the ranked wordings into a report as a table.</summary>
        public static void Write(StringBuilder report, IDictionary<string, int> linesByStyle)
        {
            IList<Candidate> candidates = Rank(linesByStyle);

            if (candidates.Count == 0)
            {
                return;
            }

            int styles = linesByStyle.Count;
            int lines = linesByStyle.Values.Sum();

            report.AppendLine(string.Format("  {0,-14}  {1,-14}  {2}",
                                            "styles caught", "lines caught", "wording to use"));

            foreach (Candidate candidate in candidates)
            {
                report.AppendLine(string.Format("  {0,-14}  {1,-14}  {2}",
                                                candidate.Styles + " of " + styles,
                                                candidate.Lines + " of " + lines,
                                                candidate.Wording));
            }

            report.AppendLine();
        }

        /// <summary>
        /// Every run of whole words inside a style name, widest first, with the name's own
        /// punctuation left exactly as it was between them.
        ///
        /// Its own punctuation and not a tidied version of it, because what comes out of here is
        /// going to be typed into a setting that matches on the text as written. A wording
        /// printed as "MRG STR" when the name says "MRG_STR" would read perfectly and match
        /// nothing, which is the failure this whole thing exists to end rather than repeat.
        /// </summary>
        internal static IEnumerable<string> WordRuns(string styleName)
        {
            if (string.IsNullOrEmpty(styleName))
            {
                yield break;
            }

            List<int[]> words = new List<int[]>();

            int at = 0;
            while (at < styleName.Length)
            {
                while (at < styleName.Length && !char.IsLetterOrDigit(styleName[at]))
                {
                    at++;
                }

                int from = at;
                while (at < styleName.Length && char.IsLetterOrDigit(styleName[at]))
                {
                    at++;
                }

                if (at > from)
                {
                    words.Add(new[] { from, at });
                }
            }

            for (int length = words.Count; length >= 1; length--)
            {
                for (int start = 0; start + length <= words.Count; start++)
                {
                    int from = words[start][0];
                    int to = words[start + length - 1][1];

                    // Two letters is a coincidence rather than a convention, and offering one
                    // would catch half the styles in the project.
                    if (to - from >= 3)
                    {
                        yield return styleName.Substring(from, to - from);
                    }
                }
            }
        }
    }
}
