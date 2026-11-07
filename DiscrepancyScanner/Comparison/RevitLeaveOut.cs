using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The four "leave out" lists - walls, floors, beam type names, beam marks - and what the
    /// Revit model in hand has already had taken out of it by them.
    ///
    /// The lists are applied as the model is read, so a wall left out is never read at all.
    /// That used to make every change to a list wait for the next import, which on a large
    /// model is minutes to drop one more wall type. It need not: every element a longer list
    /// would drop is already in hand, and carries the names the list is matched against, so
    /// a check can take it out on its own. A shorter list is another matter - what it would
    /// bring back was never read - and that, only that, still needs an import.
    ///
    /// So an instance is the lists in force on the model in memory: what it was read under,
    /// and every name a check has taken out of it since.
    /// </summary>
    public sealed class RevitLeaveOut
    {
        private RevitLeaveOut(string[] wallNames, string[] slabNames,
                              string[] beamTypeNames, string[] beamMarks)
        {
            WallNames = wallNames ?? new string[0];
            SlabNames = slabNames ?? new string[0];
            BeamTypeNames = beamTypeNames ?? new string[0];
            BeamMarks = beamMarks ?? new string[0];
        }

        public string[] WallNames { get; }
        public string[] SlabNames { get; }
        public string[] BeamTypeNames { get; }
        public string[] BeamMarks { get; }

        /// <summary>
        /// The lists as the settings have them now, split the way the import splits them: the
        /// floor list on spaces as well as commas, the others on commas only.
        /// </summary>
        public static RevitLeaveOut FromPreferences(PrefrencesData pref)
        {
            return new RevitLeaveOut(
                PrefrencesData.SplitTypeNamePhrases(pref.WallTypeNamesToIgnore),
                PrefrencesData.SplitTypeNameWords(pref.SlabTypeNamesToIgnore),
                PrefrencesData.SplitTypeNamePhrases(pref.BeamTypeNamesToIgnore),
                PrefrencesData.SplitTypeNamePhrases(pref.BeamMarksToIgnore));
        }

        /// <summary>
        /// The match behind the wall, floor and beam type lists: the type name first and the
        /// element's own name after it, anywhere in either and without regard to case. One
        /// rule, used by the import against a Revit element and by a check against what the
        /// import kept of it, so the two cannot drift apart.
        /// </summary>
        public static bool Matches(string typeName, string elementName, string[] names, out string matched)
        {
            matched = null;

            if (names == null || names.Length == 0)
            {
                return false;
            }

            return PrefrencesData.MatchesAnyPhrase(typeName, names, out matched)
                   || PrefrencesData.MatchesAnyPhrase(elementName, names, out matched);
        }

        public bool LeavesOut(Wall_R wall)
        {
            string matched;
            return Matches(wall.TypeName, wall.ElementName, WallNames, out matched);
        }

        public bool LeavesOut(Slab_R slab)
        {
            string matched;
            return Matches(slab.TypeName, slab.ElementName, SlabNames, out matched);
        }

        public bool LeavesOut(FrameData_r beam)
        {
            string matched;
            return Matches(beam.TypeName, beam.RevitName, BeamTypeNames, out matched)
                   || PrefrencesData.MatchesAnyPhrase(beam.BeamMark, BeamMarks, out matched);
        }

        /// <summary>
        /// The elements these lists keep, and how many they drop.
        /// </summary>
        public static List<T> Keep<T>(IEnumerable<T> elements, Func<T, bool> leftOut, out int dropped)
        {
            List<T> kept = new List<T>();
            dropped = 0;

            foreach (T element in elements ?? Enumerable.Empty<T>())
            {
                if (leftOut(element))
                {
                    dropped++;
                }
                else
                {
                    kept.Add(element);
                }
            }

            return kept;
        }

        /// <summary>
        /// What is in force once <paramref name="now"/> has been applied on top of this: every
        /// name in either. A check only ever takes elements out, so a name it drops for stays
        /// dropped for until the model is read again.
        /// </summary>
        public RevitLeaveOut With(RevitLeaveOut now)
        {
            return new RevitLeaveOut(Union(WallNames, now.WallNames),
                                     Union(SlabNames, now.SlabNames),
                                     Union(BeamTypeNames, now.BeamTypeNames),
                                     Union(BeamMarks, now.BeamMarks));
        }

        /// <summary>
        /// The names in force on the model in hand that <paramref name="now"/> no longer leaves
        /// out, each said with the list it came off. These are what only an import can answer:
        /// whatever they took out was never read, so a check has nothing to put back.
        ///
        /// A name still covered by a shorter one is not counted. "Blockwork-200" taken out of
        /// the list with "Blockwork" left in it brings nothing back, because every name that
        /// carries the first carries the second.
        /// </summary>
        public IList<string> NoLongerLeftOutBy(RevitLeaveOut now)
        {
            List<string> lines = new List<string>();
            AddUncovered(lines, WallNames, now.WallNames, "walls");
            AddUncovered(lines, SlabNames, now.SlabNames, "floors");
            AddUncovered(lines, BeamTypeNames, now.BeamTypeNames, "beam type names");
            AddUncovered(lines, BeamMarks, now.BeamMarks, "beam marks");
            return lines;
        }

        private static void AddUncovered(List<string> lines, string[] inForce, string[] now, string list)
        {
            foreach (string name in inForce)
            {
                string matched;
                if (!PrefrencesData.MatchesAnyPhrase(name, now, out matched))
                {
                    lines.Add("\"" + name + "\" (" + list + ")");
                }
            }
        }

        private static string[] Union(string[] first, string[] second)
        {
            return first.Concat(second)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
        }
    }
}
