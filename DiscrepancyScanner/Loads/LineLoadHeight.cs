using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What one line of the schedule should read as on an ETABS frame, once the height of the
    /// wall standing on it has been taken into account.
    ///
    /// <see cref="Known"/> is false where the height was needed and could not be had - the
    /// topmost story has nothing above it to measure to - and the check then puts the line up
    /// for review rather than failing it against a number nobody has.
    /// </summary>
    public struct ExpectedLineLoad
    {
        /// <summary>False when the expected value could not be worked out at all.</summary>
        public bool Known;

        /// <summary>What the ETABS frame should carry, in kN/m.</summary>
        public double PerMetre;

        /// <summary>The wall height used, in mm. Zero on a load that does not depend on one.</summary>
        public double HeightMM;

        /// <summary>
        /// The working, written out: what was multiplied by what, and where the height came
        /// from. Put on the result so a value that looks wrong can be argued with rather than
        /// only disbelieved - most of the arguments about this check will be about the height.
        /// </summary>
        public string How;

        /// <summary>Why it is not known, when it is not.</summary>
        public string Why;
    }

    /// <summary>
    /// The height a line load is spread over, and the load that follows from it.
    ///
    /// The schedule writes most line loads per square metre of elevation, because that is how
    /// a blockwork allowance is worked out - a density times a thickness. What ETABS carries
    /// is a load per metre run along the beam, so the two are a wall height apart, and that
    /// height is a fact about the building rather than about either sheet.
    ///
    /// The height is the story above's: a wall sitting on this floor rises to the floor over
    /// it, so what it is is the gap between the two levels. Off that comes a default
    /// allowance - the slab and its finish are not wall - except where the thing being
    /// allowed for genuinely does run the full height, which is what a facade, a cladding
    /// system or a glazing system does by definition.
    ///
    /// Told apart on the schedule's own description, because that is the only place either
    /// model says so. Which means the matching has to survive how the description was typed,
    /// and the schedule this was written against types FACADE with a cedilla on the C. Folded
    /// naively, every facade in the building quietly takes the reduction and comes out light
    /// on exactly the lines carrying the most load - so the fold is part of the rule and is
    /// pinned by a test.
    /// </summary>
    public static class LineLoadHeight
    {
        /// <summary>
        /// True when this description names something that runs the whole way up, so no
        /// allowance comes off its height.
        /// </summary>
        public static bool WantsFullHeight(string description, string fullHeightNames)
        {
            if (string.IsNullOrEmpty(description))
            {
                return false;
            }

            string folded = Fold(description);

            return PrefrencesData.SplitParameterNames(fullHeightNames)
                                 .Select(Fold)
                                 .Any(name => name.Length > 0 && folded.Contains(name));
        }

        /// <summary>
        /// A description as it is matched: upper case, with the accents taken off the letters
        /// that carry them, so FACADE, Facade and FAÇADE are one word.
        ///
        /// Decomposing and dropping the combining marks rather than listing the letters,
        /// because the list is never finished: the same schedule written by a different office
        /// will have its own.
        /// </summary>
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string decomposed = text.Normalize(NormalizationForm.FormD);

            StringBuilder folded = new StringBuilder(decomposed.Length);

            foreach (char letter in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
                {
                    folded.Append(letter);
                }
            }

            return folded.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant().Trim();
        }

        /// <summary>
        /// The wall height a line load of this description stands over, in mm, given the gap
        /// to the story above.
        ///
        /// Never less than nothing: a story above closer than the allowance is a model fault
        /// rather than a negative wall, and a negative height would turn a load upside down
        /// without saying anything.
        /// </summary>
        public static double WallHeight(string description, double storyAboveHeightMM,
                                        double reductionMM, string fullHeightNames)
        {
            if (WantsFullHeight(description, fullHeightNames))
            {
                return Math.Max(0, storyAboveHeightMM);
            }

            return Math.Max(0, storyAboveHeightMM - Math.Max(0, reductionMM));
        }

        /// <summary>
        /// What the ETABS frame under this line should carry, in kN/m.
        ///
        /// A height of zero or less is not passed off as a load of nought: it means the story
        /// above was not found, or sits below this one, and a load of nought would read as
        /// "ETABS has too much here" on every line on the floor.
        /// </summary>
        /// <param name="heightZone">
        /// Where the height came from, in words, when it was a zone drawn by hand rather than the
        /// level's own floor to floor height - so the working says which it was. Null for the
        /// level's own.
        /// </param>
        public static ExpectedLineLoad Expected(LineLoadEntry entry, double storyAboveHeightMM,
                                                double reductionMM, string fullHeightNames,
                                                double roundUpTo = 0, string heightZone = null)
        {
            ExpectedLineLoad expected = Unrounded(entry, storyAboveHeightMM, reductionMM, fullHeightNames,
                                                  heightZone);

            if (expected.Known && roundUpTo > 0)
            {
                double rounded = RoundUp(expected.PerMetre, roundUpTo);
                if (Math.Abs(rounded - expected.PerMetre) > 1e-9)
                {
                    expected.How += $" = {LoadSet.Text(expected.PerMetre)} kN/m, rounded up to " +
                                    $"{LoadSet.Text(rounded)}";
                }
                expected.PerMetre = rounded;
            }

            return expected;
        }

        /// <summary>
        /// A load rounded up to the next step, the way a designer rounds it before typing it
        /// into ETABS. The hair taken off first is floating point: 26.0 stored as 26.0000001
        /// is still 26.0 and must not come out as 26.1.
        /// </summary>
        public static double RoundUp(double value, double step)
        {
            if (step <= 0)
            {
                return value;
            }

            double steps = Math.Ceiling(value / step - 1e-6);
            return Math.Round(steps * step, 6);
        }

        private static ExpectedLineLoad Unrounded(LineLoadEntry entry, double storyAboveHeightMM,
                                                  double reductionMM, string fullHeightNames,
                                                  string heightZone)
        {
            // What the height is called in the working: the story above, or the zone drawn by
            // hand that stood in for it.
            string heightIs = string.IsNullOrEmpty(heightZone)
                ? "the story above is"
                : heightZone + " makes the storey";
            ExpectedLineLoad expected = new ExpectedLineLoad { How = "", Why = "" };

            if (entry == null)
            {
                expected.Why = "there is no schedule row to read a value off";
                return expected;
            }

            if (entry.HeightIndependent)
            {
                expected.Known = true;
                expected.PerMetre = entry.Value;
                expected.How = $"{LoadSet.Text(entry.Value)} kN/m off the schedule, which does not " +
                               "depend on the wall height";
                return expected;
            }

            if (storyAboveHeightMM <= 0)
            {
                expected.Why = "the height of the story above is not known, so a load written per " +
                               "square metre of elevation cannot be turned into a load per metre run";
                return expected;
            }

            bool full = WantsFullHeight(entry.Description, fullHeightNames);
            double reduction = full ? 0 : Math.Max(0, reductionMM);
            double height = Math.Max(0, storyAboveHeightMM - reduction);

            if (height <= 0)
            {
                expected.Why = $"{heightIs} {Metres(storyAboveHeightMM)} and the " +
                               $"allowance taken off is {LoadSet.Text(reduction)} mm, which leaves no " +
                               "wall to put a load on";
                return expected;
            }

            expected.Known = true;
            expected.HeightMM = height;
            expected.PerMetre = entry.Value * height / 1000d;
            expected.How = $"{LoadSet.Text(entry.Value)} kN/m2 on elevation over " +
                           $"{Metres(height)} of wall" +
                           (full
                               ? (string.IsNullOrEmpty(heightZone)
                                   ? " (the full story above, because the schedule calls this a full height item)"
                                   : $" (the full height of {heightZone}, because the schedule calls this a "
                                     + "full height item)")
                               : $" ({heightIs} {Metres(storyAboveHeightMM)}, less " +
                                 $"{LoadSet.Text(reduction)} mm)");

            return expected;
        }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / 1000d, 3).ToString("0.###", CultureInfo.CurrentCulture) + " m";
        }
    }
}
