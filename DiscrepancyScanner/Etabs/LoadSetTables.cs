using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Reading the shell uniform load sets out of the model's own tables.
    ///
    /// A shell in ETABS can carry uniform loads assigned straight to it, or a uniform load set
    /// assigned to it, or both. The API answers for the first and has no word at all for the
    /// second - there is no member anywhere in ETABSv1 that mentions a load set - so a shell
    /// carrying only a set read as carrying nothing, and was reported as failing its loading
    /// plan rather than as not having been read.
    ///
    /// The tables have it. Two of them: one saying which set each area carries, and one saying
    /// what each set is worth, per load pattern. Neither is keyed by anything but names, so
    /// the join is by name and the columns are found by their own field keys rather than by
    /// counting along the row - a table that gains a column in the next version of ETABS then
    /// still reads.
    ///
    /// Nothing here touches the ETABS API. What arrives is the flat array a table comes back
    /// as, which is the part worth checking.
    /// </summary>
    internal static class LoadSetTables
    {
        /// <summary>What each set is worth: a row per load pattern in it.</summary>
        public const string DefinitionsTable = "Shell Uniform Load Sets";

        /// <summary>Which set each area carries.</summary>
        public const string AssignmentsTable = "Area Load Assignments - Uniform Load Sets";

        /// <summary>One line of a load set: a pattern, and how much of it the set puts on.</summary>
        public struct PatternValue
        {
            public string Pattern;
            public double Value;
        }

        /// <summary>
        /// Letters and digits only, folded to upper case, so a field key can be matched however
        /// the version being read spaces or punctuates it.
        /// </summary>
        public static string Normalise(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            char[] kept = new char[text.Length];
            int at = 0;

            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    kept[at++] = char.ToUpperInvariant(c);
                }
            }

            return new string(kept, 0, at);
        }

        /// <summary>
        /// Which column holds one of these fields, or -1. The first candidate that matches
        /// wins, so the names are given most specific first.
        /// </summary>
        public static int ColumnOf(string[] fieldKeys, params string[] candidates)
        {
            if (fieldKeys == null)
            {
                return -1;
            }

            foreach (string candidate in candidates)
            {
                string wanted = Normalise(candidate);

                for (int i = 0; i < fieldKeys.Length; i++)
                {
                    if (Normalise(fieldKeys[i]) == wanted)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// What each load set is worth, from the definitions table. A set names one row per
        /// load pattern it puts on, so the rows are gathered by set name.
        /// </summary>
        public static Dictionary<string, List<PatternValue>> Definitions(
            string[] fieldKeys, string[] data, int records, IList<string> issues)
        {
            Dictionary<string, List<PatternValue>> sets =
                new Dictionary<string, List<PatternValue>>(StringComparer.OrdinalIgnoreCase);

            int nameColumn = ColumnOf(fieldKeys, "Name", "LoadSet", "SetName");
            int patternColumn = ColumnOf(fieldKeys, "LoadPattern", "Pattern");
            int valueColumn = ColumnOf(fieldKeys, "LoadValue", "Value", "Load");

            if (nameColumn < 0 || patternColumn < 0 || valueColumn < 0)
            {
                Complain(issues, DefinitionsTable, fieldKeys);
                return sets;
            }

            foreach (string[] row in Rows(fieldKeys, data, records))
            {
                string name = (row[nameColumn] ?? "").Trim();
                string pattern = (row[patternColumn] ?? "").Trim();

                double value;
                if (name.Length == 0 || pattern.Length == 0 || !TryNumber(row[valueColumn], out value))
                {
                    continue;
                }

                List<PatternValue> lines;
                if (!sets.TryGetValue(name, out lines))
                {
                    lines = new List<PatternValue>(4);
                    sets[name] = lines;
                }

                lines.Add(new PatternValue { Pattern = pattern, Value = value });
            }

            return sets;
        }

        /// <summary>
        /// Which set each area carries, by the area's unique name - which is what the rest of
        /// the import knows an area by. The label and the story are in the table too and are
        /// not used: two areas on different stories share a label, and only the unique name is
        /// unique.
        /// </summary>
        public static Dictionary<string, string> Assignments(
            string[] fieldKeys, string[] data, int records, IList<string> issues)
        {
            Dictionary<string, string> byArea =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            int areaColumn = ColumnOf(fieldKeys, "UniqueName", "Unique Name", "AreaName");
            // Deliberately not "Name": this table names the area as well as the set, and a
            // column called Name could be either. Binding to the wrong one would not fail, it
            // would quietly read rubbish and then report every set as undefined.
            int setColumn = ColumnOf(fieldKeys, "LoadSet", "UniformLoadSet");

            if (areaColumn < 0 || setColumn < 0)
            {
                Complain(issues, AssignmentsTable, fieldKeys);
                return byArea;
            }

            foreach (string[] row in Rows(fieldKeys, data, records))
            {
                string area = (row[areaColumn] ?? "").Trim();
                string set = (row[setColumn] ?? "").Trim();

                if (area.Length > 0 && set.Length > 0)
                {
                    byArea[area] = set;
                }
            }

            return byArea;
        }

        /// <summary>
        /// The flat array a table comes back as, cut into rows. Everything is one long array
        /// of strings, a row at a time, as many across as there are fields; a short last row
        /// is dropped rather than read off the end.
        /// </summary>
        public static IEnumerable<string[]> Rows(string[] fieldKeys, string[] data, int records)
        {
            if (fieldKeys == null || data == null || fieldKeys.Length == 0)
            {
                yield break;
            }

            int width = fieldKeys.Length;

            for (int record = 0; record < records; record++)
            {
                int start = record * width;
                if (start + width > data.Length)
                {
                    yield break;
                }

                string[] row = new string[width];
                Array.Copy(data, start, row, 0, width);
                yield return row;
            }
        }

        private static void Complain(IList<string> issues, string table, string[] fieldKeys)
        {
            if (issues == null)
            {
                return;
            }

            issues.Add($"The ETABS table \"{table}\" does not have the columns this expects, so the " +
                       "shell load sets on this model were not read and the shells carrying them will " +
                       "read as carrying nothing. Its columns are: " +
                       (fieldKeys == null ? "(none)" : string.Join(", ", fieldKeys)));
        }

        private static bool TryNumber(string text, out double value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        #region what the numbers are in

        /// <summary>
        /// How many kN/m2 one unit of the table's own units is, or -1 when the units are not
        /// recognised.
        ///
        /// The table hands its numbers over already formatted, in whatever units the model is
        /// being shown in, and says which those are beside the column. Reading that rather than
        /// assuming the database units is the difference between a load of 10 and a load of
        /// 10000, so an unrecognised unit is reported rather than guessed at.
        /// </summary>
        public static double AreaLoadFactorFrom(string units)
        {
            if (string.IsNullOrEmpty(units))
            {
                return -1;
            }

            string[] halves = units.Split('/');
            if (halves.Length != 2)
            {
                return -1;
            }

            double force = ForceInKilonewtons(Normalise(halves[0]));
            double length = LengthInMetres(SquaredLength(Normalise(halves[1])));

            return force <= 0 || length <= 0 ? -1 : force / (length * length);
        }

        /// <summary>
        /// The same thing for a force and a length given apart from each other, which is how
        /// the model text file writes them:
        ///
        ///   UNITS  "KN"  "M"  "C"
        ///
        /// It is the same question - how many kN/m2 one unit of area load is - reached without
        /// the "kN/m2" string in the middle, so it goes through the same two tables. There is
        /// no second opinion about what a kip is depending on which way the model was read.
        /// </summary>
        public static double AreaLoadFactorFrom(string force, string length)
        {
            double inKilonewtons = ForceInKilonewtons(Normalise(force));
            double inMetres = LengthInMetres(Normalise(length));

            return inKilonewtons <= 0 || inMetres <= 0 ? -1 : inKilonewtons / (inMetres * inMetres);
        }

        /// <summary>"M2", "M^2" and "M²" all normalise to a length with a 2 on the end.</summary>
        private static string SquaredLength(string normalised)
        {
            return normalised.EndsWith("2", StringComparison.Ordinal)
                ? normalised.Substring(0, normalised.Length - 1)
                : normalised;
        }

        private static double ForceInKilonewtons(string force)
        {
            switch (force)
            {
                case "N": return 0.001d;
                case "KN": return 1d;
                case "KGF": case "KG": return 0.00980665d;
                case "TONF": case "TON": case "T": return 9.80665d;
                case "LB": case "LBF": return 0.00444822162d;
                case "KIP": return 4.44822162d;
                default: return -1;
            }
        }

        private static double LengthInMetres(string length)
        {
            switch (length)
            {
                case "M": return 1d;
                case "CM": return 0.01d;
                case "MM": return 0.001d;
                case "FT": return 0.3048d;
                case "IN": case "INCH": return 0.0254d;
                default: return -1;
            }
        }

        #endregion
    }
}
