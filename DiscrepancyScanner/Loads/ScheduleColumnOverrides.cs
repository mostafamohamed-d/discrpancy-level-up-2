using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscrepancyScanner.Wpf;

namespace DiscrepancyScanner
{
    /// <summary>What a column of a loading schedule holds, as far as the check is concerned.</summary>
    public enum ScheduleRole
    {
        /// <summary>The serial number a load is looked up by.</summary>
        Number,

        Description,

        /// <summary>
        /// The superimposed dead load: the area schedule's TOTAL column, the line schedule's
        /// value column.
        /// </summary>
        Sidl,

        /// <summary>The area schedule's SIDL breakdown values, added up where the total is empty.</summary>
        SidlBreakdown,

        /// <summary>The area schedule's live load value.</summary>
        Live,

        /// <summary>The area schedule's narrow column marking which live load it is.</summary>
        LiveKind
    }

    /// <summary>
    /// Columns of a loading schedule said by hand, for a sheet whose columns the reader picked
    /// wrongly - one rearranged, or given a column the headings do not tell apart.
    ///
    /// A column said here wins over the one the heading row would have given, and the rest are
    /// still found from the headings. Where the headings cannot be found at all, the columns
    /// said here are enough on their own once they include the ones the reader cannot do
    /// without: the number and the SIDL, and for an area schedule the live load too.
    ///
    /// Kept per project, like everything else decided by hand about one, and within that by
    /// the workbook's file name rather than its path, so it still holds for the same schedule
    /// opened from another folder or another machine.
    /// </summary>
    public sealed class ScheduleColumnOverrides
    {
        /// <summary>Where the overrides sit in the settings file and in a project file.</summary>
        public const string Branch = "scheduleColumns";

        /// <summary>The one set every read of a schedule consults.</summary>
        public static ScheduleColumnOverrides Current { get; } = new ScheduleColumnOverrides();

        // project -> workbook file name -> sheet -> role -> column
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<ScheduleRole, int>>>> byProject =
            new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<ScheduleRole, int>>>>(
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The project open now, whose overrides a read uses. Set by the panel as it reads the
        /// project, because the readers are asked for a sheet and know nothing of which project
        /// asked.
        /// </summary>
        public string ActiveProject { get; set; } = "";

        /// <summary>The columns said by hand for one sheet in the open project.</summary>
        public IDictionary<ScheduleRole, int> For(string workbook, string sheet)
        {
            return For(ActiveProject, workbook, sheet);
        }

        public IDictionary<ScheduleRole, int> For(string project, string workbook, string sheet)
        {
            Dictionary<ScheduleRole, int> roles = Roles(project, workbook, sheet, false);
            return roles == null ? new Dictionary<ScheduleRole, int>() : new Dictionary<ScheduleRole, int>(roles);
        }

        /// <summary>
        /// Says which column holds a role, or - with zero - hands the role back to the headings.
        /// A column holds one role: saying it holds another takes the first away.
        /// </summary>
        public void Set(string project, string workbook, string sheet, ScheduleRole role, int column)
        {
            Dictionary<ScheduleRole, int> roles = Roles(project, workbook, sheet, column > 0);
            if (roles == null)
            {
                return;
            }

            if (column <= 0)
            {
                roles.Remove(role);
            }
            else
            {
                foreach (ScheduleRole other in roles.Where(pair => pair.Value == column).Select(pair => pair.Key).ToList())
                {
                    roles.Remove(other);
                }
                roles[role] = column;
            }

            Tidy(project, workbook, sheet);
        }

        /// <summary>Hands every column of one sheet back to the headings; answers how many were said.</summary>
        public int ClearSheet(string project, string workbook, string sheet)
        {
            Dictionary<ScheduleRole, int> roles = Roles(project, workbook, sheet, false);
            if (roles == null)
            {
                return 0;
            }

            int count = roles.Count;
            roles.Clear();
            Tidy(project, workbook, sheet);
            return count;
        }

        /// <summary>Forgets one project's overrides, for a project being cleared or read in afresh.</summary>
        public void ClearProject(string project)
        {
            byProject.Remove(Key(project));
        }

        #region the settings file

        public void ReadFrom(IDictionary<string, object> view)
        {
            foreach (KeyValuePair<string, object> project in Json.Branch(view, Branch))
            {
                foreach (KeyValuePair<string, object> workbook in AsMap(project.Value))
                {
                    foreach (KeyValuePair<string, object> sheet in AsMap(workbook.Value))
                    {
                        foreach (KeyValuePair<string, object> role in AsMap(sheet.Value))
                        {
                            ScheduleRole parsed;
                            if (Enum.TryParse(role.Key, true, out parsed) && role.Value is double)
                            {
                                Set(project.Key, workbook.Key, sheet.Key, parsed, (int)(double)role.Value);
                            }
                        }
                    }
                }
            }
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> projects = new Dictionary<string, object>(StringComparer.Ordinal);

            foreach (var project in byProject.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Dictionary<string, object> workbooks = new Dictionary<string, object>(StringComparer.Ordinal);

                foreach (var workbook in project.Value)
                {
                    Dictionary<string, object> sheets = new Dictionary<string, object>(StringComparer.Ordinal);

                    foreach (var sheet in workbook.Value.Where(s => s.Value.Count > 0))
                    {
                        sheets[sheet.Key] = sheet.Value.OrderBy(r => r.Key)
                                                 .ToDictionary(r => r.Key.ToString(), r => (object)(double)r.Value,
                                                               StringComparer.Ordinal);
                    }

                    if (sheets.Count > 0)
                    {
                        workbooks[workbook.Key] = sheets;
                    }
                }

                if (workbooks.Count > 0)
                {
                    projects[project.Key] = workbooks;
                }
            }

            return projects;
        }

        private static Dictionary<string, object> AsMap(object value)
        {
            return value as Dictionary<string, object> ?? new Dictionary<string, object>();
        }

        #endregion

        private Dictionary<ScheduleRole, int> Roles(string project, string workbook, string sheet, bool create)
        {
            string p = Key(project);
            string book = WorkbookKey(workbook);
            string tab = Key(sheet);

            if (p.Length == 0 || book.Length == 0 || tab.Length == 0)
            {
                return null;
            }

            Dictionary<string, Dictionary<string, Dictionary<ScheduleRole, int>>> workbooks;
            if (!byProject.TryGetValue(p, out workbooks))
            {
                if (!create) return null;
                workbooks = new Dictionary<string, Dictionary<string, Dictionary<ScheduleRole, int>>>(StringComparer.OrdinalIgnoreCase);
                byProject[p] = workbooks;
            }

            Dictionary<string, Dictionary<ScheduleRole, int>> sheets;
            if (!workbooks.TryGetValue(book, out sheets))
            {
                if (!create) return null;
                sheets = new Dictionary<string, Dictionary<ScheduleRole, int>>(StringComparer.OrdinalIgnoreCase);
                workbooks[book] = sheets;
            }

            Dictionary<ScheduleRole, int> roles;
            if (!sheets.TryGetValue(tab, out roles))
            {
                if (!create) return null;
                roles = new Dictionary<ScheduleRole, int>();
                sheets[tab] = roles;
            }

            return roles;
        }

        private void Tidy(string project, string workbook, string sheet)
        {
            Dictionary<string, Dictionary<string, Dictionary<ScheduleRole, int>>> workbooks;
            if (!byProject.TryGetValue(Key(project), out workbooks))
            {
                return;
            }

            Dictionary<string, Dictionary<ScheduleRole, int>> sheets;
            if (workbooks.TryGetValue(WorkbookKey(workbook), out sheets))
            {
                Dictionary<ScheduleRole, int> roles;
                if (sheets.TryGetValue(Key(sheet), out roles) && roles.Count == 0)
                {
                    sheets.Remove(Key(sheet));
                }
                if (sheets.Count == 0)
                {
                    workbooks.Remove(WorkbookKey(workbook));
                }
            }

            if (workbooks.Count == 0)
            {
                byProject.Remove(Key(project));
            }
        }

        private static string Key(string value)
        {
            return (value ?? "").Trim();
        }

        /// <summary>The workbook's file name, so the same schedule in another folder is the same schedule.</summary>
        private static string WorkbookKey(string workbook)
        {
            string path = Key(workbook);
            if (path.Length == 0)
            {
                return "";
            }

            try
            {
                return Path.GetFileName(path);
            }
            catch (Exception)
            {
                return path;
            }
        }
    }
}
