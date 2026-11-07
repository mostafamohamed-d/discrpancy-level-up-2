using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>Outcome of checking a Revit column type mark against the ETABS frame groups.</summary>
    public enum GroupCheckResult
    {
        /// <summary>
        /// The ETABS column carries no naming group, so there is nothing to check the type
        /// mark against and nothing is claimed either way.
        /// </summary>
        NotChecked,
        /// <summary>One group, and it is the type mark. Drawn green.</summary>
        Matched,
        /// <summary>
        /// A group carries the type mark, but the column sits in further groups on top of it.
        /// Drawn orange: the column is named correctly, the extra groups want a look.
        /// </summary>
        MatchedWithExtraGroups,
        /// <summary>No group carries the type mark. Drawn red.</summary>
        Mismatched
    }

    /// <summary>
    /// Checks the "Column Type Mark" of a Revit column against the frame groups its matched
    /// ETABS column is assigned to.
    ///
    /// The column counts as matched as soon as any one of its groups carries the same name as
    /// the type mark, which is what lets a column belong to several groups and still read as
    /// correctly named. Whatever groups are left over after that one are reported separately
    /// rather than as an error, because a column in more than one group is a modelling
    /// decision to review and not in itself wrong.
    ///
    /// Only the groups whose name carries one of
    /// <see cref="PrefrencesData.EtabsGroupNamePatterns"/> take part; the rest are dropped
    /// before any of this, so a housekeeping group can neither make a column match nor make
    /// it look like it belongs to more than one group. Whitespace is ignored throughout, so
    /// a group written "T - 1" reads the same as "T-1".
    ///
    /// A column left with no naming group at all is not checked rather than mismatched. The
    /// groups are what the type mark is read against, so without one there is nothing to
    /// disagree with, and a model that does not carry naming groups on every column - or
    /// does not use them at all - would otherwise read as one long row of red.
    /// </summary>
    public sealed class ColumnGroupCheck
    {
        private static readonly IList<string> noGroups = new string[0];

        private ColumnGroupCheck(GroupCheckResult result, string typeMark, string matchedGroup,
                                 IList<string> groups, IList<string> extraGroups, string description)
        {
            Result = result;
            TypeMark = typeMark;
            MatchedGroup = matchedGroup;
            Groups = groups;
            ExtraGroups = extraGroups;
            Description = description;
        }

        public GroupCheckResult Result { get; }

        /// <summary>The Revit type mark that was compared, trimmed. Empty when the column has none.</summary>
        public string TypeMark { get; }

        /// <summary>The ETABS group that carries the type mark, or an empty string.</summary>
        public string MatchedGroup { get; }

        /// <summary>The ETABS groups taken into account, with the ignored names removed.</summary>
        public IList<string> Groups { get; }

        /// <summary>Groups the column is in besides <see cref="MatchedGroup"/>.</summary>
        public IList<string> ExtraGroups { get; }

        /// <summary>One line saying what was found, for the note and the results table.</summary>
        public string Description { get; }

        /// <summary>True when nothing about the group assignment counts against the column.</summary>
        public bool Passed { get => Result != GroupCheckResult.Mismatched; }

        /// <summary>True when the column is in more groups than the one that matched.</summary>
        public bool HasExtraGroups { get => Result == GroupCheckResult.MatchedWithExtraGroups; }

        public string GroupsText { get => Join(Groups); }

        public string ExtraGroupsText { get => Join(ExtraGroups); }

        /// <summary>Short verdict shown in the results table and stampable on the model.</summary>
        public string ResultText
        {
            get
            {
                switch (Result)
                {
                    case GroupCheckResult.Matched:
                        return "Matched";
                    case GroupCheckResult.MatchedWithExtraGroups:
                        return "Multiple groups";
                    case GroupCheckResult.Mismatched:
                        return "Mismatched";
                    default:
                        return "Not checked";
                }
            }
        }

        public static ColumnGroupCheck Compare(Column_r revitColumn, Column_e etabsColumn)
        {
            return Compare(revitColumn?.TypeMark, etabsColumn?.Groups);
        }

        public static ColumnGroupCheck Compare(string typeMark, IEnumerable<string> etabsGroups)
        {
            string mark = (typeMark ?? "").Trim();
            IList<string> groups = Considered(etabsGroups);

            if (groups.Count == 0)
            {
                // Without a group in ETABS there is nothing for the type mark to be checked
                // against, so the check stands aside instead of calling the column wrong.
                return new ColumnGroupCheck(GroupCheckResult.NotChecked, mark, "", groups, noGroups,
                    mark.Length == 0
                        ? "no column type mark and no naming group"
                        : $"column type mark {mark}, no naming group assigned in ETABS");
            }

            if (mark.Length == 0)
            {
                return new ColumnGroupCheck(GroupCheckResult.Mismatched, mark, "", groups, noGroups,
                    $"no column type mark in Revit, ETABS group {Join(groups)}");
            }

            string matchedGroup = groups.FirstOrDefault(group => Same(group, mark));
            if (matchedGroup == null)
            {
                return new ColumnGroupCheck(GroupCheckResult.Mismatched, mark, "", groups, noGroups,
                    $"column type mark {mark} is not an ETABS group of this column ({Join(groups)})");
            }

            IList<string> extraGroups = groups.Where(group => !Same(group, mark)).ToList();
            if (extraGroups.Count == 0)
            {
                return new ColumnGroupCheck(GroupCheckResult.Matched, mark, matchedGroup, groups, noGroups,
                    $"column type mark {mark} matches the ETABS group");
            }

            return new ColumnGroupCheck(GroupCheckResult.MatchedWithExtraGroups, mark, matchedGroup,
                groups, extraGroups,
                $"column type mark {mark} matches, also in ETABS group {Join(extraGroups)}");
        }

        /// <summary>
        /// The assigned groups worth comparing: trimmed, de-duplicated, and cut down to the
        /// ones whose name carries a naming pattern.
        /// </summary>
        private static IList<string> Considered(IEnumerable<string> etabsGroups)
        {
            if (etabsGroups == null)
            {
                return new List<string>();
            }

            string[] patterns = PrefrencesData.SplitParameterNames(
                PrefrencesData.getPrefrencesData().EtabsGroupNamePatterns);

            return etabsGroups
                .Where(group => !string.IsNullOrEmpty(group))
                .Select(group => group.Trim())
                .Where(group => group.Length > 0)
                .Where(group => IsNamingGroup(group, patterns))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// True when the group name carries one of the naming patterns. With no pattern
        /// configured every group counts, which is the check's behaviour without the filter.
        /// </summary>
        private static bool IsNamingGroup(string group, string[] patterns)
        {
            if (patterns.Length == 0)
            {
                return true;
            }

            string name = WithoutWhitespace(group);
            return patterns.Any(pattern =>
            {
                string wanted = WithoutWhitespace(pattern);
                return wanted.Length > 0
                       && name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        /// <summary>
        /// Compares two names as the same, ignoring case and any whitespace inside them, so
        /// a type mark "T-1" matches a group typed as "T - 1".
        /// </summary>
        private static bool Same(string left, string right)
        {
            return string.Equals(WithoutWhitespace(left), WithoutWhitespace(right),
                                 StringComparison.OrdinalIgnoreCase);
        }

        private static string WithoutWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return new string(text.Where(character => !char.IsWhiteSpace(character)).ToArray());
        }

        private static string Join(IEnumerable<string> names)
        {
            return string.Join(", ", names.ToArray());
        }
    }
}
