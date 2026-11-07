using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ToolLicensing
{
    /// <summary>
    /// The one place a product asks "may I run?".
    ///
    /// Call <see cref="EnsureLicensed"/> where a command or the tool starts, and again where the
    /// real work happens. The answer is never kept - every call reads the license files and checks
    /// the signature afresh, a few milliseconds - so removing one call does not unlock the other.
    ///
    /// For stronger protection, do not stop at the yes/no: open a protected resource with
    /// <see cref="GetProductKey"/> (see SecretBox), so the tool cannot work without a real license
    /// even if every call to EnsureLicensed is patched out.
    /// </summary>
    public static class LicenseGate
    {
        private static bool dialogOpen;
        private static bool refreshedThisRun;
        private static bool warnedThisRun;

        /// <summary>Checks silently. Never shows anything.</summary>
        public static LicenseResult Check(string productId)
        {
            LicenseVerifier verifier = CreateVerifier();
            LicenseResult best = null;

            foreach (string text in LicenseStore.ReadAll())
            {
                LicenseResult result = verifier.Verify(productId, text);
                if (result.IsValid)
                {
                    return result;
                }

                if (best == null || result.Status > best.Status)
                {
                    best = result;
                }
            }

            if (best != null)
            {
                return best;
            }

            if (string.IsNullOrWhiteSpace(EmbeddedKeys.PublicKeyXml))
            {
                return LicenseResult.Fail(LicenseStatus.NotConfigured, "This build has no license key configured.");
            }

            return LicenseResult.Fail(LicenseStatus.Missing, "No license is installed on this PC.");
        }

        /// <summary>
        /// True when this PC holds a valid license for the product. Otherwise shows the activation
        /// dialog, and returns whether the user ended up licensed.
        /// </summary>
        public static bool EnsureLicensed(string productId, string productName)
        {
            RefreshRevocation();

            LicenseResult result = Check(productId);
            if (result.IsValid)
            {
                WarnIfExpiringSoon(productName, result);
                return true;
            }

            // A second command started while the dialog is up must not open a second dialog.
            if (dialogOpen)
            {
                return false;
            }

            dialogOpen = true;
            try
            {
                using (ActivationForm form = new ActivationForm(productId, productName, Check(productId)))
                {
                    IWin32Window owner = MainWindow();
                    if (owner != null)
                    {
                        form.ShowDialog(owner);
                    }
                    else
                    {
                        form.ShowDialog();
                    }
                }
            }
            catch (Exception)
            {
                // A dialog that will not open must not take the host (Revit, ETABS) down with it.
                // The product stays locked, which is the right answer to a failed activation.
            }
            finally
            {
                dialogOpen = false;
            }

            return Check(productId).IsValid;
        }

        /// <summary>
        /// The product's content key, or null when this PC is not licensed. Use it with
        /// SecretBox.TryOpen to unlock data the tool cannot run without.
        /// </summary>
        public static byte[] GetProductKey(string productId)
        {
            LicenseResult result = Check(productId);
            return result.IsValid ? result.ProductKey : null;
        }

        /// <summary>Checks a license file's text, as the dialog does when one is imported.</summary>
        internal static LicenseResult VerifyText(string productId, string licenseText)
        {
            return CreateVerifier().Verify(productId, licenseText);
        }

        /// <summary>
        /// Downloads the owner's revocation list, at most once per run. While the PC is well inside
        /// its grace period this happens in the background, so an offline start does not wait; when
        /// it never succeeded, or the grace period is nearly used up, it waits (a few seconds at most).
        /// Cancelled license files are then removed from this PC.
        /// </summary>
        public static void RefreshRevocation()
        {
            RevocationGuard guard = CreateRevocationGuard();
            if (guard == null || refreshedThisRun)
            {
                return;
            }
            refreshedThisRun = true;

            DateTime now = DateTime.UtcNow;
            DateTime? last = guard.LastGoodCheckUtc;
            if (last.HasValue && now - last.Value < TimeSpan.FromHours(LicensingOptions.RecheckHours) && now >= last.Value)
            {
                return;
            }

            string url = LicensingOptions.RevocationUrl;
            bool urgent = !last.HasValue || now - last.Value > TimeSpan.FromDays(Math.Max(0, LicensingOptions.OnlineGraceDays - 2));
            Action refresh = () =>
            {
                if (guard.Refresh(url, DateTime.UtcNow, 8000))
                {
                    foreach (string id in guard.RevokedIds)
                    {
                        LicenseStore.Delete(id);
                    }
                }
            };

            if (urgent)
            {
                refresh();
            }
            else
            {
                System.Threading.Thread worker = new System.Threading.Thread(() => refresh()) { IsBackground = true };
                worker.Start();
            }
        }

        private static void WarnIfExpiringSoon(string productName, LicenseResult result)
        {
            if (warnedThisRun || !result.DaysLeft.HasValue || LicensingOptions.ExpiryWarningDays <= 0 ||
                result.DaysLeft.Value > LicensingOptions.ExpiryWarningDays)
            {
                return;
            }
            warnedThisRun = true;

            try
            {
                MessageBox.Show(
                    "Your license for " + productName + " ends on " + result.Payload.ExpiresAtUtc.Value.ToString("yyyy-MM-dd") +
                    " (" + result.DaysLeft.Value + " days left).\r\n\r\n" +
                    "To renew it, ask for a new license now: " + LicensingOptions.ContactText,
                    productName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception)
            {
                // No desktop (a service or a script): the warning is a courtesy, not a check.
            }
        }

        private static RevocationGuard CreateRevocationGuard()
        {
            return string.IsNullOrWhiteSpace(LicensingOptions.RevocationUrl)
                ? null
                : RevocationGuard.ForThisUser(EmbeddedKeys.FingerprintSalt, EmbeddedKeys.PublicKeyXml);
        }

        internal static MachineFingerprint ThisMachine()
        {
            return MachineFingerprint.ReadThisMachine(EmbeddedKeys.FingerprintSalt);
        }

        private static LicenseVerifier CreateVerifier()
        {
            return new LicenseVerifier(
                EmbeddedKeys.PublicKeyXml,
                ThisMachine(),
                Environment.UserName,
                () => DateTime.UtcNow,
                ClockGuard.ForThisUser(EmbeddedKeys.FingerprintSalt),
                CreateRevocationGuard(),
                TimeSpan.FromDays(LicensingOptions.OnlineGraceDays));
        }

        /// <summary>
        /// The host's main window, for the dialog to sit in front of. Taken from the process, not
        /// from Revit's UIApplication, so it works from a modeless window that is outside a Revit
        /// API context, and the same way in a standalone tool.
        /// </summary>
        private static IWin32Window MainWindow()
        {
            try
            {
                IntPtr handle = Process.GetCurrentProcess().MainWindowHandle;
                return handle != IntPtr.Zero ? new WindowHandle(handle) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; private set; }
        }
    }
}
