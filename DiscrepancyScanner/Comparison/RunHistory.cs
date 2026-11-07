using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What the last run found, so this run can say what has changed since.
    ///
    /// "38 new since the last run, 12 fixed" is the difference between a list you have to read
    /// from the top every time and one you can work down. It is also the only thing that tells
    /// a model that is getting better from one that is merely still wrong.
    ///
    /// Kept in a file of its own rather than in the settings file. A large model produces tens
    /// of thousands of findings and the settings file is small, hand-editable and read on every
    /// window open; putting a run in it would make it none of those things.
    /// </summary>
    public sealed class RunHistory
    {
        private readonly HashSet<string> previous = new HashSet<string>(StringComparer.Ordinal);
        private bool loaded;

        /// <summary>%APPDATA%\DiscrepancyScanner\runs</summary>
        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DiscrepancyScanner", "runs");
            }
        }

        /// <summary>Set when the last read or write failed, so the window can say so.</summary>
        public string LastError { get; private set; }

        /// <summary>
        /// True once a previous run has been read. Before that nothing is new and nothing is
        /// fixed - a first run has nothing to be different from, and saying "everything is new"
        /// would be noise rather than news.
        /// </summary>
        public bool HasPrevious { get; private set; }

        /// <summary>
        /// One finding, named so the same finding on the same element reads the same next time.
        ///
        /// The verdict is part of it: a location check that went from failed to needs-a-look is
        /// a different finding, not the same one carried forward, and reading it as unchanged
        /// would hide the very thing that moved.
        ///
        /// So is the place, where the check has one: two stations of the same beam short of
        /// steel are two findings, and without the token fixing one of them would read as
        /// fixing both. It is left off the key entirely where there is none, so the keys every
        /// other check has been writing since the first run still read the same.
        /// </summary>
        public static string KeyOf(string elementKey, CheckKind kind, ResultLevel level,
                                   string token = null)
        {
            // No element, no finding. A key made without one names a check and a verdict and
            // nothing they happened to, so every element that has no name would share it - and
            // one of them being fixed would read as all of them being fixed.
            if (string.IsNullOrEmpty(elementKey))
            {
                return "";
            }

            string key = elementKey + "|" + (int)kind + "|" + (int)level;
            return string.IsNullOrEmpty(token) ? key : key + "|" + token;
        }

        public void Load(string projectKey)
        {
            previous.Clear();
            HasPrevious = false;
            loaded = true;
            LastError = null;

            if (string.IsNullOrEmpty(projectKey))
            {
                return;
            }

            try
            {
                string path = PathFor(projectKey);
                if (!File.Exists(path))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string key = line.Trim();
                    if (key.Length > 0)
                    {
                        previous.Add(key);
                    }
                }
                HasPrevious = true;
            }
            catch (Exception e)
            {
                // An unreadable history is no worse than no history: the run still happens, it
                // just cannot say what changed.
                LastError = "Could not read the last run: " + e.Message;
                previous.Clear();
                HasPrevious = false;
            }
        }

        /// <summary>True where this finding was not in the last run.</summary>
        public bool IsNew(string key)
        {
            return HasPrevious && !previous.Contains(key);
        }

        /// <summary>
        /// How many of the last run's findings are not in this one. Not a list, because a fixed
        /// finding has no element on this run's plan to point at - it is only a number, and a
        /// welcome one.
        /// </summary>
        public int FixedCount(ISet<string> current)
        {
            return HasPrevious ? previous.Count(key => !current.Contains(key)) : 0;
        }

        /// <summary>
        /// Records this run as the one to compare the next against. One key per line rather
        /// than JSON: it is a set of strings nobody reads by hand, and a large model makes a lot
        /// of them.
        /// </summary>
        public void Save(string projectKey, ISet<string> current)
        {
            if (string.IsNullOrEmpty(projectKey) || current == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Folder);
                string path = PathFor(projectKey);
                string temporary = path + ".tmp";

                File.WriteAllLines(temporary, current.OrderBy(key => key, StringComparer.Ordinal),
                                   new UTF8Encoding(false));

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temporary, path);

                previous.Clear();
                foreach (string key in current)
                {
                    previous.Add(key);
                }
                HasPrevious = true;
            }
            catch (Exception e)
            {
                LastError = "Could not record this run: " + e.Message;
            }
        }

        public bool IsLoaded => loaded;

        /// <summary>
        /// One file per project, named by a hash of its path. The path itself cannot be a file
        /// name - it has separators and a drive letter in it - and hashing keeps two projects
        /// with the same file name in different folders apart, which using the name alone would
        /// not.
        /// </summary>
        private static string PathFor(string projectKey)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(projectKey));
                string name = BitConverter.ToString(hash, 0, 10).Replace("-", "");
                return Path.Combine(Folder, name + ".run");
            }
        }
    }
}
