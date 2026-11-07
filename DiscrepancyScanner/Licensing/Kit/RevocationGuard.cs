using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace ToolLicensing
{
    /// <summary>
    /// Remembers, on this PC, the owner's latest revocation list and when it was last fetched.
    ///
    /// The state is one small file signed with a key derived from the vendor salt (as ClockGuard
    /// does), so it cannot be edited by hand. Deleting it does not reset the grace period: the
    /// verifier then counts from the license's issue date, and the cancelled ids come back with
    /// the next successful download.
    ///
    /// Honest limits: someone who cracks the exe can remove the check, and a PC that never goes
    /// online stops after the grace period rather than at the moment a license is cancelled.
    /// </summary>
    public sealed class RevocationGuard
    {
        private static readonly object FileLock = new object();

        private readonly string path;
        private readonly string publicKeyXml;
        private readonly byte[] key;

        public RevocationGuard(string path, string salt, string publicKeyXml)
        {
            this.path = path;
            this.publicKeyXml = publicKeyXml;
            using (SHA256 sha = SHA256.Create())
            {
                key = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + "|revocation"));
            }
        }

        public static RevocationGuard ForThisUser(string salt, string publicKeyXml)
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                LicensingOptions.VendorFolder);
            return new RevocationGuard(Path.Combine(folder, "revocation.dat"), salt, publicKeyXml);
        }

        /// <summary>When a list was last downloaded and accepted, or null if never (or the file was removed).</summary>
        public DateTime? LastGoodCheckUtc
        {
            get
            {
                State state = Read();
                return state == null ? (DateTime?)null : new DateTime(state.LastGoodTicks, DateTimeKind.Utc);
            }
        }

        /// <summary>
        /// The newest time the download server reported (its Date header) on a successful check, or
        /// null. Used as trusted time: a clock set back cannot go earlier than this.
        /// </summary>
        public DateTime? ServerTimeUtc
        {
            get
            {
                State state = Read();
                return state == null || state.ServerTicks == 0
                    ? (DateTime?)null
                    : new DateTime(state.ServerTicks, DateTimeKind.Utc);
            }
        }

        public bool IsRevoked(string licenseId)
        {
            State state = Read();
            return state != null && !string.IsNullOrEmpty(licenseId) && state.Revoked != null &&
                   state.Revoked.Any(id => string.Equals(id, licenseId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The cancelled ids this PC knows of.</summary>
        public string[] RevokedIds
        {
            get
            {
                State state = Read();
                return state == null || state.Revoked == null ? new string[0] : state.Revoked;
            }
        }

        /// <summary>
        /// Takes a downloaded list. False (nothing changes) when it is not signed by the owner or is
        /// older than one already accepted - a replayed old list must not undo a cancellation.
        /// </summary>
        public bool Apply(string listText, DateTime utcNow)
        {
            return Apply(listText, utcNow, null);
        }

        /// <param name="serverTimeUtc">
        /// The time the download server reported, or null. When given, it is recorded as the time of
        /// the check instead of the PC's clock, so a wrong PC clock cannot move the grace period.
        /// </param>
        public bool Apply(string listText, DateTime utcNow, DateTime? serverTimeUtc)
        {
            RevocationList list;
            if (!RevocationList.TryVerify(listText, publicKeyXml, out list))
            {
                LicenseLog.Error("revocation",
                    "The downloaded revocation list was rejected: it is not signed by the vendor or it was edited. Nothing changed.");
                return false;
            }

            lock (FileLock)
            {
                State current = Read();
                if (current != null && list.Sequence < current.Sequence)
                {
                    LicenseLog.Error("revocation",
                        "The downloaded revocation list (sequence " + list.Sequence + ") is older than the one already accepted (" +
                        current.Sequence + "). It was ignored.");
                    return false;
                }

                DateTime checkedAt = serverTimeUtc ?? utcNow;
                long previousServer = current == null ? 0 : current.ServerTicks;
                long newServer = serverTimeUtc.HasValue ? serverTimeUtc.Value.Ticks : 0;
                Write(new State
                {
                    LastGoodTicks = checkedAt.Ticks,
                    Sequence = list.Sequence,
                    Revoked = list.Revoked,
                    ServerTicks = Math.Max(previousServer, newServer)
                });
                return true;
            }
        }

        /// <summary>Downloads the list from <paramref name="url"/> and applies it. False when offline or rejected.</summary>
        public bool Refresh(string url, DateTime utcNow, int timeoutMs)
        {
            try
            {
                // .NET Framework 4.7.2 may still default to TLS 1.0, which GitHub refuses.
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string separator = url.Contains("?") ? "&" : "?";
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url + separator + "t=" + utcNow.Ticks);
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.UserAgent = "ToolLicensing";
                // Office networks often sit behind a proxy that wants the Windows sign-in.
                request.Proxy = WebRequest.GetSystemWebProxy();
                request.Proxy.Credentials = CredentialCache.DefaultCredentials;

                using (WebResponse response = request.GetResponse())
                {
                    DateTime? serverTime = ServerDate(response);
                    using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    {
                        return Apply(reader.ReadToEnd(), utcNow, serverTime);
                    }
                }
            }
            catch (Exception ex)
            {
                LicenseLog.Error("revocation",
                    "Could not download the revocation list from " + url + ": " + LicenseLog.Describe(ex) +
                    ". Licenses keep working until the grace period ends.");
                return false;
            }
        }

        private static DateTime? ServerDate(WebResponse response)
        {
            string header = response.Headers["Date"];
            DateTime parsed;
            if (!string.IsNullOrEmpty(header) &&
                DateTime.TryParse(header, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed;
            }

            return null;
        }

        private State Read()
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                string text;
                lock (FileLock)
                {
                    text = File.ReadAllText(path);
                }

                string[] parts = text.Trim().Split('.');
                if (parts.Length != 2)
                {
                    return null;
                }

                byte[] body = Convert.FromBase64String(parts[0]);
                if (!Equal(Convert.FromBase64String(parts[1]), Sign(body)))
                {
                    LicenseLog.Error("revocation",
                        "The saved revocation state was edited or damaged. It is treated as never checked.");
                    return null;
                }

                return LicenseJson.Deserialize<State>(body);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void Write(State state)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                byte[] body = LicenseJson.Serialize(state);
                // Written aside and swapped in, so a reader never sees half a file.
                string temp = path + ".tmp";
                File.WriteAllText(temp, Convert.ToBase64String(body) + "." + Convert.ToBase64String(Sign(body)));
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            catch (Exception ex)
            {
                // Not saved: the next start downloads again, and the grace period keeps counting.
                LicenseLog.Error("revocation", "Could not save the revocation state: " + LicenseLog.Describe(ex));
            }
        }

        private byte[] Sign(byte[] body)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(body);
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

        [DataContract]
        private sealed class State
        {
            [DataMember(Name = "lastGood", Order = 1)] public long LastGoodTicks { get; set; }
            [DataMember(Name = "sequence", Order = 2)] public long Sequence { get; set; }
            [DataMember(Name = "revoked", Order = 3)] public string[] Revoked { get; set; }
            [DataMember(Name = "serverTime", Order = 4)] public long ServerTicks { get; set; }
        }
    }
}
