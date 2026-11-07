using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// The label an area load goes by - what the loading plan writes on a region and the
    /// schedule writes in its serial column - and how two of them are told to be the same.
    ///
    /// Not a number. Most schedules do number their loads one, two, three, but some letter
    /// them by the part of the building they belong to - H1 for the hotel, L1 for the
    /// landscape - and those have to look each other up just the same. So a label is text,
    /// and the one thing kept from it being a number is that a whole number reads the same
    /// however it was typed: 7, 07 and 7.0 are all load 7.
    /// </summary>
    public static class LoadLabel
    {
        /// <summary>
        /// The longest word read as a label in the schedule's serial column. Labels are short
        /// codes; anything longer is a note or a heading that wandered into the column.
        /// </summary>
        public const int LongestOnSheet = 12;

        /// <summary>
        /// A label as it is kept: a whole number written plainly, anything else trimmed with
        /// its inner spaces squeezed to one. Empty when the text carries no label, nought
        /// included - an integer parameter left at its default is a region nobody numbered.
        /// </summary>
        public static string Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string trimmed = text.Trim();

            // Only text made of digits is tried as a number, so a label such as 1E2 is kept as
            // written rather than read as a hundred.
            int number;
            if (trimmed.All(letter => char.IsDigit(letter) || letter == '.')
                && LoadScheduleReader.TryWholeNumber(trimmed, out number))
            {
                return number > 0 ? number.ToString(CultureInfo.InvariantCulture) : "";
            }

            return string.Join(" ", trimmed.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// True when a cell in the schedule's serial column looks like a label rather than a
        /// note: one short word of letters and digits, with the odd dash, dot, slash or
        /// underscore - H1, L-2, 3A, 4.1. A sentence, or a heading, is not one.
        /// </summary>
        public static bool IsLabelShaped(string text)
        {
            string trimmed = (text ?? "").Trim();

            return trimmed.Length > 0
                   && trimmed.Length <= LongestOnSheet
                   && trimmed.Any(char.IsLetterOrDigit)
                   && trimmed.All(letter => char.IsLetterOrDigit(letter) || "-_./".IndexOf(letter) >= 0);
        }

        /// <summary>
        /// What two labels are matched on: case and spaces do not count, so h1, H1 and "H 1"
        /// are the same load. Nothing else is folded - H-1 and H1 are kept apart, because a
        /// schedule could mean them as two.
        /// </summary>
        public static string Key(string label)
        {
            StringBuilder key = new StringBuilder();
            foreach (char letter in Read(label))
            {
                if (!char.IsWhiteSpace(letter))
                {
                    key.Append(char.ToUpperInvariant(letter));
                }
            }
            return key.ToString();
        }

        public static bool Same(string first, string second)
        {
            return string.Equals(Key(first), Key(second), StringComparison.Ordinal);
        }

        /// <summary>
        /// The order labels are listed in: the way a person would count them, so 2 comes before
        /// 10 and H2 before H10, and plain numbers before lettered ones.
        /// </summary>
        public static int Compare(string first, string second)
        {
            string a = Key(first);
            string b = Key(second);

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                bool aDigit = char.IsDigit(a[i]);
                bool bDigit = char.IsDigit(b[j]);

                if (aDigit != bDigit)
                {
                    return aDigit ? -1 : 1;
                }

                int aEnd = i, bEnd = j;
                while (aEnd < a.Length && char.IsDigit(a[aEnd]) == aDigit) aEnd++;
                while (bEnd < b.Length && char.IsDigit(b[bEnd]) == bDigit) bEnd++;

                string aRun = a.Substring(i, aEnd - i);
                string bRun = b.Substring(j, bEnd - j);

                int order;
                if (aDigit)
                {
                    // Compared as numbers of any length: fewer digits, once the leading
                    // noughts are off, is the smaller number.
                    string aDigits = aRun.TrimStart('0');
                    string bDigits = bRun.TrimStart('0');
                    order = aDigits.Length != bDigits.Length
                        ? aDigits.Length.CompareTo(bDigits.Length)
                        : string.CompareOrdinal(aDigits, bDigits);
                }
                else
                {
                    order = string.CompareOrdinal(aRun, bRun);
                }

                if (order != 0)
                {
                    return order;
                }

                i = aEnd;
                j = bEnd;
            }

            return (a.Length - i).CompareTo(b.Length - j);
        }

        /// <summary><see cref="Compare"/> as a comparer, for OrderBy.</summary>
        public static readonly IComparer<string> Order = Comparer<string>.Create(Compare);
    }
}
