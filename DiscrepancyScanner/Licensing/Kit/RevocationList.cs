using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace ToolLicensing
{
    /// <summary>
    /// The owner's list of cancelled license ids, signed like a license so that only the owner can
    /// write one. The tool downloads it (see RevocationGuard); a list that is unsigned, edited or
    /// older than one already seen is ignored, so faking or replaying it gains nothing.
    ///
    ///   -----BEGIN TOOL REVOCATION LIST-----
    ///   base64(payload JSON) . base64(RSA signature)
    ///   -----END TOOL REVOCATION LIST-----
    /// </summary>
    [DataContract]
    public sealed class RevocationList
    {
        public const int CurrentFormat = 1;
        public const string Header = "-----BEGIN TOOL REVOCATION LIST-----";
        public const string Footer = "-----END TOOL REVOCATION LIST-----";

        public RevocationList()
        {
            Revoked = new string[0];
        }

        [DataMember(Name = "formatVersion", Order = 1)] public int FormatVersion { get; set; }

        /// <summary>Grows with every list the owner signs (UTC ticks); a smaller one is a replay.</summary>
        [DataMember(Name = "sequence", Order = 2)] public long Sequence { get; set; }

        [DataMember(Name = "revoked", Order = 3)] public string[] Revoked { get; set; }

        public bool Contains(string licenseId)
        {
            return !string.IsNullOrEmpty(licenseId) && Revoked != null &&
                   Revoked.Any(id => string.Equals(id, licenseId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Reads and checks a list file. False unless it is well formed and signed by the owner.</summary>
        public static bool TryVerify(string text, string publicKeyXml, out RevocationList list)
        {
            list = null;
            byte[] payload;
            byte[] signature;
            if (string.IsNullOrWhiteSpace(publicKeyXml) ||
                !LicenseFile.TryDecode(text, Header, Footer, out payload, out signature) || signature.Length == 0)
            {
                return false;
            }

            try
            {
                using (RSA rsa = RSA.Create())
                {
                    rsa.FromXmlString(publicKeyXml);
                    if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    {
                        return false;
                    }
                }

                list = LicenseJson.Deserialize<RevocationList>(payload);
            }
            catch (Exception)
            {
                list = null;
            }

            if (list == null || list.FormatVersion != CurrentFormat)
            {
                list = null;
                return false;
            }

            if (list.Revoked == null)
            {
                list.Revoked = new string[0];
            }
            return true;
        }
    }
}
