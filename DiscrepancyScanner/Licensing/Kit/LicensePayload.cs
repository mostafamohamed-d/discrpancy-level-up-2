using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace ToolLicensing
{
    /// <summary>
    /// Everything a license says. The whole thing is signed, so none of it can be edited.
    /// </summary>
    [DataContract]
    public sealed class LicensePayload
    {
        public const int CurrentFormat = 1;

        public LicensePayload()
        {
            Products = new string[0];
            Features = new string[0];
            MachineHashes = new string[0];
            ProductKeys = new List<ProductKeyEntry>();
        }

        [DataMember(Name = "formatVersion", Order = 1)] public int FormatVersion { get; set; }

        /// <summary>A unique id for this license, for the issuer's own records.</summary>
        [DataMember(Name = "licenseId", Order = 2)] public string LicenseId { get; set; }

        [DataMember(Name = "licensee", Order = 3)] public string Licensee { get; set; }

        /// <summary>Product ids this license covers, or "*" for every product.</summary>
        [DataMember(Name = "products", Order = 4)] public string[] Products { get; set; }

        /// <summary>Optional switches inside a product, such as "export" or "pro".</summary>
        [DataMember(Name = "features", Order = 5)] public string[] Features { get; set; }

        /// <summary>The Windows user name this is tied to, or empty for any user on the PC.</summary>
        [DataMember(Name = "windowsUser", Order = 6)] public string WindowsUser { get; set; }

        /// <summary>The PC's component hashes, copied from the request file.</summary>
        [DataMember(Name = "machineHashes", Order = 7)] public string[] MachineHashes { get; set; }

        [DataMember(Name = "issuedUtc", Order = 8)] public string IssuedUtc { get; set; }

        /// <summary>When it stops working, or empty for a license that never expires.</summary>
        [DataMember(Name = "expiresUtc", Order = 9)] public string ExpiresUtc { get; set; }

        /// <summary>Per-product keys for decrypting the product's protected resources.</summary>
        [DataMember(Name = "productKeys", Order = 10)] public List<ProductKeyEntry> ProductKeys { get; set; }

        public DateTime? ExpiresAtUtc
        {
            get { return ParseUtc(ExpiresUtc); }
        }

        public DateTime? IssuedAtUtc
        {
            get { return ParseUtc(IssuedUtc); }
        }

        public bool CoversProduct(string productId)
        {
            return Products != null && Products.Any(p =>
                p == "*" || string.Equals(p, productId, StringComparison.OrdinalIgnoreCase));
        }

        public bool HasFeature(string feature)
        {
            return Features != null && Features.Any(f =>
                string.Equals(f, feature, StringComparison.OrdinalIgnoreCase));
        }

        public static string FormatUtc(DateTime value)
        {
            return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseUtc(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            DateTime parsed;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed;
            }

            return null;
        }
    }

    [DataContract]
    public sealed class ProductKeyEntry
    {
        [DataMember(Name = "product", Order = 1)] public string Product { get; set; }

        /// <summary>The 32-byte key, base64.</summary>
        [DataMember(Name = "key", Order = 2)] public string Key { get; set; }
    }
}
