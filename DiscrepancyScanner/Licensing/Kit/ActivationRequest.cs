using System;
using System.Runtime.Serialization;

namespace ToolLicensing
{
    /// <summary>
    /// What a PC sends to ask for a license: its component hashes, the Windows user and the
    /// product. It is not signed - the issuer decides whether to trust it, by knowing who sent it.
    /// </summary>
    [DataContract]
    public sealed class ActivationRequest
    {
        [DataMember(Name = "formatVersion", Order = 1)] public int FormatVersion { get; set; }
        [DataMember(Name = "productId", Order = 2)] public string ProductId { get; set; }
        [DataMember(Name = "productName", Order = 3)] public string ProductName { get; set; }
        [DataMember(Name = "machineName", Order = 4)] public string MachineName { get; set; }
        [DataMember(Name = "windowsUser", Order = 5)] public string WindowsUser { get; set; }
        [DataMember(Name = "machineHashes", Order = 6)] public string[] MachineHashes { get; set; }
        [DataMember(Name = "displayId", Order = 7)] public string DisplayId { get; set; }
        [DataMember(Name = "createdUtc", Order = 8)] public string CreatedUtc { get; set; }

        public static ActivationRequest Create(string productId, string productName, MachineFingerprint machine, string userName)
        {
            return new ActivationRequest
            {
                FormatVersion = 1,
                ProductId = productId,
                ProductName = productName,
                MachineName = Environment.MachineName,
                WindowsUser = userName,
                MachineHashes = machine.ComponentHashes,
                DisplayId = machine.DisplayId,
                CreatedUtc = LicensePayload.FormatUtc(DateTime.UtcNow)
            };
        }

        public string ToFileText()
        {
            return LicenseFile.Encode(LicenseFile.RequestHeader, LicenseFile.RequestFooter,
                LicenseJson.Serialize(this), null);
        }

        public static bool TryParse(string text, out ActivationRequest request)
        {
            request = null;

            byte[] payload;
            byte[] signature;
            if (!LicenseFile.TryDecode(text, LicenseFile.RequestHeader, LicenseFile.RequestFooter, out payload, out signature))
            {
                return false;
            }

            try
            {
                request = LicenseJson.Deserialize<ActivationRequest>(payload);
            }
            catch (Exception)
            {
                request = null;
                return false;
            }

            if (request == null || request.FormatVersion != 1 || request.MachineHashes == null ||
                request.MachineHashes.Length == 0)
            {
                request = null;
                return false;
            }

            return true;
        }
    }
}
