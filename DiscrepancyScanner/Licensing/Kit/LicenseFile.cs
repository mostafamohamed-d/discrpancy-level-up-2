using System;
using System.Collections.Generic;
using System.Text;

namespace ToolLicensing
{
    /// <summary>
    /// The text format of a license file and of a request file.
    ///
    ///   -----BEGIN TOOL LICENSE-----
    ///   base64(payload JSON) . base64(RSA signature)     wrapped at 64 columns
    ///   -----END TOOL LICENSE-----
    ///
    /// Text rather than binary so it survives being emailed, pasted into a chat or opened in
    /// Notepad. A request file has the same shape with an empty signature after the dot.
    /// </summary>
    public static class LicenseFile
    {
        public const string LicenseHeader = "-----BEGIN TOOL LICENSE-----";
        public const string LicenseFooter = "-----END TOOL LICENSE-----";
        public const string RequestHeader = "-----BEGIN TOOL LICENSE REQUEST-----";
        public const string RequestFooter = "-----END TOOL LICENSE REQUEST-----";

        public static string Encode(string header, string footer, byte[] payload, byte[] signature)
        {
            string body = Convert.ToBase64String(payload) + "." +
                          (signature == null ? string.Empty : Convert.ToBase64String(signature));

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(header);
            for (int i = 0; i < body.Length; i += 64)
            {
                sb.AppendLine(body.Substring(i, Math.Min(64, body.Length - i)));
            }
            sb.AppendLine(footer);
            return sb.ToString();
        }

        /// <summary>
        /// Reads a file made by <see cref="Encode"/>. False for anything else: wrong header,
        /// missing footer, bad base64. The signature is empty (not null) for a request file.
        /// </summary>
        public static bool TryDecode(string text, string header, string footer, out byte[] payload, out byte[] signature)
        {
            payload = null;
            signature = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            StringBuilder body = new StringBuilder();
            bool inside = false;
            bool closed = false;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (!inside)
                {
                    if (line == header)
                    {
                        inside = true;
                    }
                    continue;
                }

                if (line == footer)
                {
                    closed = true;
                    break;
                }

                body.Append(line);
            }

            if (!closed)
            {
                return false;
            }

            string[] parts = body.ToString().Split('.');
            if (parts.Length != 2 || parts[0].Length == 0)
            {
                return false;
            }

            try
            {
                payload = Convert.FromBase64String(parts[0]);
                signature = parts[1].Length == 0 ? new byte[0] : Convert.FromBase64String(parts[1]);
                return true;
            }
            catch (FormatException)
            {
                payload = null;
                signature = null;
                return false;
            }
        }
    }
}
