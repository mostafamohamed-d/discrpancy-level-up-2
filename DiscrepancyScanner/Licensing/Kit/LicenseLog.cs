using System;
using System.Globalization;
using System.IO;

namespace ToolLicensing
{
    /// <summary>
    /// Appends licensing problems to %APPDATA%\ToolLicensing\licensing.log, so a failed download or
    /// a rejected list is visible instead of silently ignored. Writing the log never throws: a
    /// folder that cannot be written must not stop the tool.
    /// </summary>
    public static class LicenseLog
    {
        private static readonly object FileLock = new object();

        /// <summary>Set by tests to write somewhere else. Leave null in products.</summary>
        public static string PathOverride;

        public static string FilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(PathOverride))
                {
                    return PathOverride;
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LicensingOptions.VendorFolder,
                    "licensing.log");
            }
        }

        public static void Error(string area, string message)
        {
            Write("ERROR", area, message);
        }

        public static void Info(string area, string message)
        {
            Write("INFO", area, message);
        }

        /// <summary>A short description of an exception and its inner exception, for the log.</summary>
        public static string Describe(Exception ex)
        {
            if (ex == null)
            {
                return "unknown error";
            }

            string text = ex.GetType().Name + ": " + ex.Message;
            if (ex.InnerException != null)
            {
                text += " (inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message + ")";
            }
            return text;
        }

        private static void Write(string level, string area, string message)
        {
            try
            {
                string path = FilePath;
                string line = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) +
                              " " + level + " [" + area + "] " + message + Environment.NewLine;

                lock (FileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line);
                }
            }
            catch (Exception)
            {
                // Logging must never take the tool down.
            }
        }
    }
}
