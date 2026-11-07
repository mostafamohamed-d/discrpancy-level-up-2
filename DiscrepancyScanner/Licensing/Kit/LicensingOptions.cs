namespace ToolLicensing
{
    /// <summary>
    /// Settings a product may change before it first asks for a license.
    /// </summary>
    public static class LicensingOptions
    {
        /// <summary>
        /// The folder under %APPDATA% where license files are kept. Every product that uses this
        /// library shares it, so one license file that names several products unlocks all of them.
        /// </summary>
        public static string VendorFolder = "ToolLicensing";

        /// <summary>
        /// Shown in the activation dialog under the buttons - say who to send the request file to.
        /// </summary>
        public static string ContactText = "Send the request file to the person who issued you this tool.";

        /// <summary>
        /// When set, the dialog gets an "Email request" button that saves the request to the Desktop and
        /// opens a ready email to this address (default mail app, or Gmail in the browser if none).
        /// </summary>
        public static string ContactEmail = "";

        /// <summary>
        /// Where the owner publishes the signed revocation list (LicenseIssuer revoke), for example a
        /// gist's raw URL without a revision in it. Empty turns online checking off: licenses then
        /// end only by expiry. Once set, a PC must download the list at least every
        /// <see cref="OnlineGraceDays"/> days, or the tool stops until it can.
        /// </summary>
        public static string RevocationUrl = "https://gist.githubusercontent.com/mostafamohamed-d/16b6f9910e5898cfafd47f95909a429f/raw/revoked.lrev";

        /// <summary>Days a PC may stay offline before the license must be confirmed online again.</summary>
        public static int OnlineGraceDays = 14;

        /// <summary>Hours between background downloads of the revocation list.</summary>
        public static int RecheckHours = 6;

        /// <summary>Warn this many days before a license expires (0 = never warn).</summary>
        public static int ExpiryWarningDays = 14;
    }
}
