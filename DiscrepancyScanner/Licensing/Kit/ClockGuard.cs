using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ToolLicensing
{
    /// <summary>
    /// Notices a PC clock that was put back to stretch an expiring license.
    ///
    /// Each time a license with an expiry date is checked, the latest time ever seen is written to
    /// a small file, signed with a key derived from the vendor salt so it cannot be edited by hand.
    /// A clock earlier than that - by more than a day, which covers time zones and a clock that
    /// was merely wrong - is refused.
    ///
    /// Be honest about what it is: someone who deletes the file starts again. It stops the
    /// obvious trick, not a determined one.
    /// </summary>
    public sealed class ClockGuard
    {
        private static readonly TimeSpan Tolerance = TimeSpan.FromHours(24);

        private readonly string path;
        private readonly byte[] key;

        public ClockGuard(string path, string salt)
        {
            this.path = path;
            using (SHA256 sha = SHA256.Create())
            {
                key = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "|clock-guard"));
            }
        }

        public static ClockGuard ForThisUser(string salt)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LicensingOptions.VendorFolder);
            return new ClockGuard(Path.Combine(folder, "clock.dat"), salt);
        }

        /// <summary>False when the clock is earlier than a time seen before.</summary>
        public bool Check(DateTime utcNow)
        {
            DateTime? last = Read();

            if (last.HasValue && utcNow < last.Value - Tolerance)
            {
                return false;
            }

            if (!last.HasValue || utcNow > last.Value)
            {
                Write(utcNow);
            }

            return true;
        }

        private DateTime? Read()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string[] parts = File.ReadAllText(path).Trim().Split('.');
                if (parts.Length != 2)
                {
                    return null;
                }

                byte[] mac = Convert.FromBase64String(parts[1]);
                if (!Equal(mac, Sign(parts[0])))
                {
                    return null;
                }

                long ticks;
                if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out ticks))
                {
                    return null;
                }

                return new DateTime(ticks, DateTimeKind.Utc);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Write(DateTime utcNow)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string ticks = utcNow.Ticks.ToString(CultureInfo.InvariantCulture);
                File.WriteAllText(path, ticks + "." + Convert.ToBase64String(Sign(ticks)));
            }
            catch (Exception)
            {
                // A folder that cannot be written must not lock the tool; the guard just does nothing.
            }
        }

        private byte[] Sign(string text)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(Encoding.ASCII.GetBytes(text));
            }
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }
            return difference == 0;
        }
    }
}
