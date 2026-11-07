using System;
using System.Collections.Generic;
using System.IO;

namespace ToolLicensing
{
    /// <summary>
    /// Where license files live: %APPDATA%\&lt;VendorFolder&gt;\*.lic. Any number of files may be
    /// there - one per product, or one that covers several - and every one is tried.
    ///
    /// A user can copy, delete or edit these files. None of that matters for security: a copied
    /// file fails the machine check on another PC, an edited one fails its signature, and a
    /// deleted one just means the activation dialog comes back.
    /// </summary>
    public static class LicenseStore
    {
        public static string Directory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    LicensingOptions.VendorFolder);
            }
        }

        public static IEnumerable<string> ReadAll()
        {
            List<string> texts = new List<string>();

            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    return texts;
                }

                foreach (string file in System.IO.Directory.GetFiles(Directory, "*.lic"))
                {
                    try
                    {
                        texts.Add(File.ReadAllText(file));
                    }
                    catch (IOException)
                    {
                        // One unreadable file must not hide the others.
                    }
                }
            }
            catch (Exception)
            {
                // No folder, no access: the same as no license.
            }

            return texts;
        }

        /// <summary>Keeps a verified license, named after its id so a renewal replaces nothing by accident.</summary>
        public static string Save(string licenseId, string licenseText)
        {
            System.IO.Directory.CreateDirectory(Directory);

            string path = PathFor(licenseId ?? Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, licenseText);
            return path;
        }

        /// <summary>Removes the file of a cancelled license, if this PC has it. Never throws.</summary>
        public static void Delete(string licenseId)
        {
            try
            {
                string path = PathFor(licenseId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // It stays unusable anyway: the verifier refuses a revoked id.
            }
        }

        private static string PathFor(string licenseId)
        {
            string name = licenseId;
            foreach (char bad in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(bad, '_');
            }
            return Path.Combine(Directory, name + ".lic");
        }
    }
}
