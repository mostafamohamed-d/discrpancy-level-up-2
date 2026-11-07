using System;
using System.Linq;
using System.Security.Cryptography;

namespace ToolLicensing
{
    /// <summary>
    /// How far a license got. Declared in the order the checks run, so a bigger value means a
    /// failure later in the checks - which is the more informative one to show a person who has
    /// several license files.
    /// </summary>
    public enum LicenseStatus
    {
        NotConfigured,
        Missing,
        Malformed,
        BadSignature,
        WrongProduct,
        Expired,
        ClockRolledBack,
        WrongMachine,
        WrongUser,
        Revoked,
        CheckOverdue,
        Valid
    }

    public sealed class LicenseResult
    {
        public LicenseStatus Status { get; private set; }
        public string Message { get; private set; }
        public LicensePayload Payload { get; private set; }

        /// <summary>The product's content key from the license, or null when it carries none.</summary>
        public byte[] ProductKey { get; private set; }

        /// <summary>Whole days until it expires, or null for a license that never does.</summary>
        public int? DaysLeft { get; private set; }

        public bool IsValid
        {
            get { return Status == LicenseStatus.Valid; }
        }

        public bool HasFeature(string feature)
        {
            return IsValid && Payload != null && Payload.HasFeature(feature);
        }

        internal static LicenseResult Fail(LicenseStatus status, string message)
        {
            return new LicenseResult { Status = status, Message = message };
        }

        internal static LicenseResult Ok(LicensePayload payload, byte[] productKey, int? daysLeft)
        {
            return new LicenseResult
            {
                Status = LicenseStatus.Valid,
                Message = "Licensed to " + payload.Licensee + ".",
                Payload = payload,
                ProductKey = productKey,
                DaysLeft = daysLeft
            };
        }
    }

    /// <summary>
    /// Decides whether one license file is good for one product on this PC, right now.
    ///
    /// The order is the point: signature first, so that nothing inside an unsigned file is ever
    /// acted on, then what the license covers, then when, then who and where.
    /// </summary>
    public sealed class LicenseVerifier
    {
        private readonly string publicKeyXml;
        private readonly MachineFingerprint machine;
        private readonly string userName;
        private readonly Func<DateTime> utcNow;
        private readonly ClockGuard clock;
        private readonly RevocationGuard revocation;
        private readonly TimeSpan onlineGrace;

        /// <param name="clock">May be null (tests); then expiry is judged by the clock alone.</param>
        public LicenseVerifier(string publicKeyXml, MachineFingerprint machine, string userName,
            Func<DateTime> utcNow, ClockGuard clock)
            : this(publicKeyXml, machine, userName, utcNow, clock, null, TimeSpan.Zero)
        {
        }

        /// <param name="revocation">May be null: online checking is off and only expiry ends a license.</param>
        /// <param name="onlineGrace">How long a license stays good without a successful download of the list.</param>
        public LicenseVerifier(string publicKeyXml, MachineFingerprint machine, string userName,
            Func<DateTime> utcNow, ClockGuard clock, RevocationGuard revocation, TimeSpan onlineGrace)
        {
            this.publicKeyXml = publicKeyXml;
            this.machine = machine;
            this.userName = userName ?? string.Empty;
            this.utcNow = utcNow;
            this.clock = clock;
            this.revocation = revocation;
            this.onlineGrace = onlineGrace;
        }

        public LicenseResult Verify(string productId, string licenseText)
        {
            if (string.IsNullOrWhiteSpace(publicKeyXml))
            {
                return LicenseResult.Fail(LicenseStatus.NotConfigured,
                    "This build has no license key configured.");
            }

            byte[] payloadBytes;
            byte[] signature;
            if (!LicenseFile.TryDecode(licenseText, LicenseFile.LicenseHeader, LicenseFile.LicenseFooter,
                    out payloadBytes, out signature) || signature.Length == 0)
            {
                return LicenseResult.Fail(LicenseStatus.Malformed,
                    "The file is damaged, or it is not a license file.");
            }

            if (!SignatureIsGood(payloadBytes, signature))
            {
                return LicenseResult.Fail(LicenseStatus.BadSignature,
                    "The license was not issued by the vendor, or it has been changed.");
            }

            LicensePayload payload;
            try
            {
                payload = LicenseJson.Deserialize<LicensePayload>(payloadBytes);
            }
            catch (Exception)
            {
                payload = null;
            }

            if (payload == null || payload.FormatVersion != LicensePayload.CurrentFormat)
            {
                return LicenseResult.Fail(LicenseStatus.Malformed,
                    "The license is in a format this version does not understand.");
            }

            if (!payload.CoversProduct(productId))
            {
                return LicenseResult.Fail(LicenseStatus.WrongProduct,
                    "This license does not cover this product.");
            }

            DateTime now = utcNow();
            DateTime? expires = payload.ExpiresAtUtc;
            int? daysLeft = null;

            if (expires.HasValue)
            {
                if (now > expires.Value)
                {
                    return LicenseResult.Fail(LicenseStatus.Expired,
                        "The license expired on " + expires.Value.ToString("yyyy-MM-dd") + ".");
                }

                if (clock != null && !clock.Check(now))
                {
                    return LicenseResult.Fail(LicenseStatus.ClockRolledBack,
                        "The PC clock is earlier than a date it has already shown. Correct the clock.");
                }

                daysLeft = (int)Math.Floor((expires.Value - now).TotalDays);
            }

            if (!machine.Matches(payload.MachineHashes))
            {
                return LicenseResult.Fail(LicenseStatus.WrongMachine,
                    "This license was issued for a different PC.");
            }

            if (!string.IsNullOrEmpty(payload.WindowsUser) &&
                !string.Equals(payload.WindowsUser, userName, StringComparison.OrdinalIgnoreCase))
            {
                return LicenseResult.Fail(LicenseStatus.WrongUser,
                    "This license was issued for a different Windows user.");
            }

            if (revocation != null)
            {
                if (revocation.IsRevoked(payload.LicenseId))
                {
                    return LicenseResult.Fail(LicenseStatus.Revoked,
                        "This license has been cancelled by the vendor.");
                }

                // Counted from the last good download, or from the issue date when there has been
                // none - so deleting the local state never buys more offline time.
                DateTime lastGood = Later(revocation.LastGoodCheckUtc, payload.IssuedAtUtc);
                if (clock != null && !clock.Check(now))
                {
                    return LicenseResult.Fail(LicenseStatus.ClockRolledBack,
                        "The PC clock is earlier than a date it has already shown. Correct the clock.");
                }

                if (now - lastGood > onlineGrace)
                {
                    return LicenseResult.Fail(LicenseStatus.CheckOverdue,
                        "The license must be confirmed online every " + (int)onlineGrace.TotalDays +
                        " days (last confirmed " + lastGood.ToString("yyyy-MM-dd") +
                        "). Connect this PC to the internet and start the tool again.");
                }
            }

            return LicenseResult.Ok(payload, FindProductKey(payload, productId), daysLeft);
        }

        private bool SignatureIsGood(byte[] data, byte[] signature)
        {
            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        private static DateTime Later(DateTime? a, DateTime? b)
        {
            DateTime x = a ?? DateTime.MinValue;
            DateTime y = b ?? DateTime.MinValue;
            return x > y ? x : y;
        }

        private static byte[] FindProductKey(LicensePayload payload, string productId)
        {
            if (payload.ProductKeys == null)
            {
                return null;
            }

            ProductKeyEntry entry = payload.ProductKeys.FirstOrDefault(e =>
                e != null && string.Equals(e.Product, productId, StringComparison.OrdinalIgnoreCase));
            if (entry == null || string.IsNullOrEmpty(entry.Key))
            {
                return null;
            }

            try
            {
                byte[] key = Convert.FromBase64String(entry.Key);
                return key.Length == 32 ? key : null;
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
