using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ToolLicensing
{
    /// <summary>
    /// Who this PC is, as a few salted hashes.
    ///
    /// Three things are read - the Windows MachineGuid, the motherboard serial and the CPU id -
    /// and each is hashed on its own with the vendor salt. A license stores all of them, and a PC
    /// counts as the licensed one when at least two match. That is deliberate: a single hardware
    /// id would lock a user out the day they swap a motherboard, while requiring all three would
    /// do the same for a Windows reinstall. Two out of three survives either one.
    ///
    /// Nothing readable leaves the PC: the request file carries only the hashes, so the salt is
    /// what stops anyone computing them for a PC they have not touched.
    /// </summary>
    public sealed class MachineFingerprint
    {
        private static readonly string[] JunkValues =
        {
            "", "none", "default string", "to be filled by o.e.m.", "system serial number",
            "not specified", "not applicable", "n/a", "unknown", "0", "123456789"
        };

        private static readonly object gate = new object();
        private static MachineFingerprint cached;
        private static string cachedSalt;

        /// <summary>Each component as "name:hash", the form a license stores.</summary>
        public string[] ComponentHashes { get; private set; }

        /// <summary>A short id to show a person, such as 3F9A-0C21-77D4-B1E8. Not used to decide anything.</summary>
        public string DisplayId { get; private set; }

        /// <summary>
        /// Builds a fingerprint from component values already read. The real PC goes through
        /// <see cref="ReadThisMachine"/>; this exists so a test can stand in for another PC.
        /// </summary>
        public MachineFingerprint(string salt, IEnumerable<KeyValuePair<string, string>> rawComponents)
        {
            List<string> hashes = new List<string>();
            foreach (KeyValuePair<string, string> pair in rawComponents)
            {
                if (IsUsable(pair.Value))
                {
                    hashes.Add(Hash(salt, pair.Key, pair.Value));
                }
            }

            ComponentHashes = hashes.ToArray();
            DisplayId = MakeDisplayId(ComponentHashes);
        }

        /// <summary>This PC's fingerprint. Read once per process, because WMI is slow.</summary>
        public static MachineFingerprint ReadThisMachine(string salt)
        {
            lock (gate)
            {
                if (cached != null && cachedSalt == salt)
                {
                    return cached;
                }

                List<KeyValuePair<string, string>> raw = new List<KeyValuePair<string, string>>();
                raw.Add(new KeyValuePair<string, string>("guid", ReadMachineGuid()));

                string board = ReadWmi("Win32_BaseBoard", "SerialNumber");
                if (!IsUsable(board))
                {
                    board = ReadWmi("Win32_BIOS", "SerialNumber");
                }
                raw.Add(new KeyValuePair<string, string>("board", board));
                raw.Add(new KeyValuePair<string, string>("cpu", ReadWmi("Win32_Processor", "ProcessorId")));

                cached = new MachineFingerprint(salt, raw);
                cachedSalt = salt;
                return cached;
            }
        }

        /// <summary>
        /// True when this PC is the one a license was issued for: at least two of its components
        /// are in the license - or all of them, for a license that only ever had one.
        /// </summary>
        public bool Matches(IEnumerable<string> licensedHashes)
        {
            if (licensedHashes == null)
            {
                return false;
            }

            HashSet<string> licensed = new HashSet<string>(licensedHashes, StringComparer.OrdinalIgnoreCase);
            if (licensed.Count == 0)
            {
                return false;
            }

            int matches = ComponentHashes.Count(h => licensed.Contains(h));
            return matches >= Math.Min(2, licensed.Count);
        }

        private static bool IsUsable(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length < 4)
            {
                return false;
            }

            if (JunkValues.Contains(trimmed.ToLowerInvariant()))
            {
                return false;
            }

            // "00000000" and "FFFFFFFF" are what a BIOS reports when nobody filled the field in.
            return trimmed.Any(c => c != '0' && c != 'F' && c != 'f' && c != '-');
        }

        private static string Hash(string salt, string name, string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(salt + "|" + name + "|" + value.Trim().ToUpperInvariant());
                byte[] hash = sha.ComputeHash(bytes);
                return name + ":" + ToHex(hash, 16);
            }
        }

        private static string MakeDisplayId(string[] hashes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string joined = string.Join("|", hashes.OrderBy(h => h, StringComparer.Ordinal));
                string hex = ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(joined)), 8);
                return hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4);
            }
        }

        private static string ToHex(byte[] bytes, int count)
        {
            StringBuilder sb = new StringBuilder(count * 2);
            for (int i = 0; i < count && i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static string ReadMachineGuid()
        {
            try
            {
                // The 64-bit view on purpose: a 32-bit host would otherwise read a redirected key
                // and see a different value from a 64-bit one on the same PC.
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    return key == null ? null : key.GetValue("MachineGuid") as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ReadWmi(string className, string property)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT " + property + " FROM " + className))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject item in results)
                    {
                        using (item)
                        {
                            object value = item[property];
                            if (value != null)
                            {
                                return value.ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // WMI can be off, slow or blocked. A missing component only means fewer to match.
            }

            return null;
        }
    }
}
