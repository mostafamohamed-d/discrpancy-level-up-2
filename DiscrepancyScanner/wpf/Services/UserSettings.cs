using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// The settings file, kept per user under the roaming profile so it follows whoever is
    /// signed in rather than the machine, and so it survives a Revit restart or a re-install
    /// of the add-in.
    ///
    /// Everything on <see cref="PrefrencesData"/> is written by reflecting over its fields
    /// instead of listing them here. That is deliberate: a tolerance added to the preferences
    /// later is saved and read back without anyone remembering to come and edit this file, and
    /// a field that is renamed or dropped simply stops matching and falls back to its default
    /// rather than throwing.
    /// </summary>
    internal static class UserSettings
    {
        private const string PreferencesBranch = "preferences";
        private const string ViewBranch = "view";
        private const string DefaultsBranch = "defaults";

        /// <summary>
        /// The values the user has nominated as their own defaults, or empty when they never
        /// have and the compiled-in ones stand.
        ///
        /// Preferences are one file for the user and were always shared by every project, so
        /// this is not about carrying settings between projects - it is the second tier under
        /// them: the numbers a practice has settled on, kept apart from the numbers being
        /// tried on the model open right now, so "put it back" means back to the house
        /// standard rather than back to whatever the tool shipped with.
        /// </summary>
        private static Dictionary<string, object> storedDefaults =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>%APPDATA%\DiscrepancyScanner\settings.json</summary>
        public static string FilePath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DiscrepancyScanner",
                    "settings.json");
            }
        }

        /// <summary>Set when the last save failed, so the window can say so instead of going quiet.</summary>
        public static string LastError { get; private set; }

        #region the preferences themselves

        /// <summary>
        /// Every instance field of <see cref="PrefrencesData"/> that a settings file can carry,
        /// which is all of the numbers, switches and name lists on it. Ordered by name so the
        /// file is stable from one save to the next and reads as a diff when it changes.
        /// </summary>
        public static IEnumerable<FieldInfo> PreferenceFields()
        {
            return typeof(PrefrencesData)
                   .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   .Where(field => !field.IsInitOnly && !field.IsLiteral)
                   .Where(field => field.FieldType == typeof(double)
                                   || field.FieldType == typeof(int)
                                   || field.FieldType == typeof(bool)
                                   || field.FieldType == typeof(string))
                   .OrderBy(field => field.Name, StringComparer.Ordinal);
        }

        /// <summary>
        /// A preferences object holding nothing but the values compiled into it, for the
        /// restore-defaults button and for telling a changed setting from an untouched one.
        /// Built through the private constructor, so the field initialisers still run.
        /// </summary>
        public static PrefrencesData Defaults()
        {
            PrefrencesData defaults =
                (PrefrencesData)Activator.CreateInstance(typeof(PrefrencesData), nonPublic: true);

            // The user's own defaults sit over the compiled-in ones, so everything that reads
            // "what should this be?" - the restore button, and whether a setting counts as
            // changed - answers with the house standard once one has been saved.
            ApplyPreferences(storedDefaults, defaults);
            return defaults;
        }

        /// <summary>True once the user has nominated defaults of their own.</summary>
        public static bool HasSavedDefaults => storedDefaults.Count > 0;

        /// <summary>
        /// Takes the settings as they stand and makes them the defaults every project starts
        /// from. Only the preferences are captured: the alignment and the loading plan
        /// pairings are per project by nature - one names points in a particular model and the
        /// other names views that exist in no other - and carrying either between projects
        /// would be wrong rather than convenient.
        /// </summary>
        public static bool SaveAsDefaults(PrefrencesData preferences, IDictionary<string, object> view)
        {
            Dictionary<string, object> captured =
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (FieldInfo field in PreferenceFields())
            {
                captured[field.Name] = field.GetValue(preferences);
            }

            storedDefaults = captured;
            return Save(preferences, view);
        }

        /// <summary>
        /// Drops the saved defaults, so the compiled-in ones stand again. The settings on
        /// screen are not touched - this says what "default" means, not what anything is.
        /// </summary>
        public static bool ForgetSavedDefaults(PrefrencesData preferences, IDictionary<string, object> view)
        {
            storedDefaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            return Save(preferences, view);
        }

        #endregion

        #region load and save

        /// <summary>
        /// Reads the file over the live preferences and hands back whatever was stored under
        /// the view branch. A missing, unreadable or half written file leaves every default
        /// exactly where it was.
        /// </summary>
        public static Dictionary<string, object> Load(PrefrencesData preferences)
        {
            LastError = null;

            Dictionary<string, object> root;
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                }
                root = Json.Read(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                LastError = "Could not read " + FilePath + ": " + e.Message;
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            }

            // The defaults are read before the preferences so that anything asking what a
            // default is during load already has the saved answer.
            storedDefaults = new Dictionary<string, object>(
                Json.Branch(root, DefaultsBranch), StringComparer.OrdinalIgnoreCase);

            ApplyPreferences(Json.Branch(root, PreferencesBranch), preferences);
            return Json.Branch(root, ViewBranch);
        }

        private static void ApplyPreferences(IDictionary<string, object> stored, PrefrencesData preferences)
        {
            foreach (FieldInfo field in PreferenceFields())
            {
                object value;
                if (!stored.TryGetValue(field.Name, out value) || value == null)
                {
                    continue;
                }

                object converted;
                if (TryConvert(value, field.FieldType, out converted))
                {
                    field.SetValue(preferences, converted);
                }
            }
        }

        /// <summary>
        /// Writes the file. Goes out to a temporary file first and then moves it into place,
        /// so a crash part way through leaves the previous settings rather than a truncated
        /// file that reads as no settings at all.
        /// </summary>
        /// <summary>
        /// Writes the file. A null <paramref name="view"/> means "leave the view branch as it
        /// is on disk" rather than "the view branch is empty" - the alignments and the loading
        /// plan pairings live in there, and a caller that only wants to change a preference
        /// must not take a project's alignment with it.
        /// </summary>
        public static bool Save(PrefrencesData preferences, IDictionary<string, object> view)
        {
            LastError = null;

            Dictionary<string, object> stored = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (FieldInfo field in PreferenceFields())
            {
                stored[field.Name] = field.GetValue(preferences);
            }

            Dictionary<string, object> root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { PreferencesBranch, stored },
                { ViewBranch, view ?? ViewBranchOnDisk() }
            };

            // Written only once there are some, so a file from before this existed does not
            // gain an empty branch every time it is saved.
            if (storedDefaults.Count > 0)
            {
                root[DefaultsBranch] = storedDefaults;
            }

            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string temporary = path + ".tmp";
                File.WriteAllText(temporary, Json.Write(root), new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);
                return true;
            }
            catch (Exception e)
            {
                LastError = "Could not save to " + FilePath + ": " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// The view branch as the file currently holds it, for a save that is not about the
        /// view. An unreadable file gives an empty one, which is no worse than the state a
        /// caller with nothing to write would have left anyway.
        /// </summary>
        internal static IDictionary<string, object> ViewBranchOnDisk()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    return new Dictionary<string, object>();
                }
                return Json.Branch(Json.Read(File.ReadAllText(path, Encoding.UTF8)), ViewBranch);
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        #endregion

        #region value conversion

        /// <summary>
        /// Turns a value read out of the file, or typed into a box, into the type the field
        /// wants. Numbers are read invariantly so a file written on a machine that writes
        /// decimal commas is still read the same everywhere.
        /// </summary>
        public static bool TryConvert(object value, Type wanted, out object converted)
        {
            converted = null;
            if (value == null)
            {
                return false;
            }

            if (wanted.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            if (wanted == typeof(string))
            {
                converted = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }

            if (wanted == typeof(bool))
            {
                if (value is bool)
                {
                    converted = value;
                    return true;
                }
                bool flag;
                if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out flag))
                {
                    converted = flag;
                    return true;
                }
                return false;
            }

            if (wanted == typeof(double) || wanted == typeof(int))
            {
                double number;
                if (value is double)
                {
                    number = (double)value;
                }
                else if (!double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                                          NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    return false;
                }

                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    return false;
                }

                converted = wanted == typeof(int) ? (object)(int)Math.Round(number) : number;
                return true;
            }

            return false;
        }

        /// <summary>A value as it should appear in a text box: invariant, and no trailing zeroes.</summary>
        public static string ToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is double)
            {
                return ((double)value).ToString("0.############", CultureInfo.InvariantCulture);
            }
            if (value is int)
            {
                return ((int)value).ToString(CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
