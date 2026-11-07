using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ToolLicensing
{
    /// <summary>
    /// The dialog a PC without a license sees: it says why, lets the user save a request file to
    /// send to the issuer, and imports the license file that comes back. Built in code, with no
    /// designer file, so it can be linked into any product as one .cs file.
    /// </summary>
    internal sealed class ActivationForm : Form
    {
        private readonly string productId;
        private readonly string productName;
        private readonly Label statusLabel;

        public ActivationForm(string productId, string productName, LicenseResult current)
        {
            this.productId = productId;
            this.productName = productName;

            MachineFingerprint machine = LicenseGate.ThisMachine();

            Text = productName + " - activation";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 330);
            Font = new Font("Segoe UI", 9F);

            Label title = new Label
            {
                Text = productName + " needs a license for this PC",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(16, 14),
                Size = new Size(488, 26)
            };

            statusLabel = new Label
            {
                Text = current.Message,
                ForeColor = Color.Firebrick,
                Location = new Point(16, 44),
                Size = new Size(488, 36)
            };

            Label steps = new Label
            {
                Text = "1.  Save a request file and send it to the issuer.\r\n" +
                       "2.  Import the license file you get back.\r\n\r\n" + LicensingOptions.ContactText,
                Location = new Point(16, 88),
                Size = new Size(488, 76)
            };

            Label idCaption = new Label
            {
                Text = "This PC's id:",
                Location = new Point(16, 174),
                Size = new Size(90, 20)
            };

            TextBox idBox = new TextBox
            {
                Text = machine.DisplayId,
                ReadOnly = true,
                Location = new Point(110, 171),
                Size = new Size(200, 23)
            };

            Button copy = new Button { Text = "Copy id", Location = new Point(320, 170), Size = new Size(84, 26) };
            copy.Click += delegate { CopyId(machine.DisplayId); };

            bool canEmail = !string.IsNullOrEmpty(LicensingOptions.ContactEmail);
            int y = 214;
            if (canEmail)
            {
                Button email = new Button { Text = "Email request to " + LicensingOptions.ContactEmail + "...", Location = new Point(16, y), Size = new Size(488, 32) };
                email.Click += delegate { EmailRequest(machine); };
                Controls.Add(email);
                y += 40;
                ClientSize = new Size(520, 370);
            }

            Button save = new Button { Text = "Save request file...", Location = new Point(16, y), Size = new Size(488, 32) };
            save.Click += delegate { SaveRequest(machine); };

            Button import = new Button { Text = "Import license file...", Location = new Point(16, y + 40), Size = new Size(488, 32) };
            import.Click += delegate { ImportLicense(); };

            Button close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Location = new Point(420, y + 80),
                Size = new Size(84, 26)
            };

            Controls.AddRange(new Control[] { title, statusLabel, steps, idCaption, idBox, copy, save, import, close });
            CancelButton = close;
        }

        private static void CopyId(string id)
        {
            try
            {
                Clipboard.SetText(id);
            }
            catch (Exception)
            {
                // The clipboard can be held by another program; the id is on screen to read out.
            }
        }

        private void SaveRequest(MachineFingerprint machine)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save the request file";
                dialog.Filter = "License request (*.lreq)|*.lreq";
                dialog.FileName = productId + "-" + machine.DisplayId + ".lreq";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    ActivationRequest request = ActivationRequest.Create(productId, productName, machine, Environment.UserName);
                    File.WriteAllText(dialog.FileName, request.ToFileText());
                    SetStatus("Request saved. Send that file to the issuer, then import the license they return.", false);
                }
                catch (Exception ex)
                {
                    SetStatus("The request file could not be saved: " + ex.Message, true);
                }
            }
        }

        /// <summary>Saves the request to the Desktop, opens a ready email to the issuer and shows the file
        /// in Explorer so it can be dragged into the email (a mailto link cannot carry an attachment).</summary>
        private void EmailRequest(MachineFingerprint machine)
        {
            string path;
            try
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    productId + "-" + machine.DisplayId + ".lreq");
                ActivationRequest request = ActivationRequest.Create(productId, productName, machine, Environment.UserName);
                File.WriteAllText(path, request.ToFileText());
            }
            catch (Exception ex)
            {
                SetStatus("The request file could not be saved: " + ex.Message, true);
                return;
            }

            string to = LicensingOptions.ContactEmail;
            string subject = Uri.EscapeDataString("License request: " + productName + " (" + machine.DisplayId + ")");
            string body = Uri.EscapeDataString("Hello,\r\n\r\nPlease issue a license for " + productName + ".\r\n" +
                "Name: " + Environment.UserName + "\r\nPC: " + Environment.MachineName + " (" + machine.DisplayId + ")\r\n\r\n" +
                "The request file " + Path.GetFileName(path) + " is attached.\r\n");

            try
            {
                Process.Start("mailto:" + to + "?subject=" + subject + "&body=" + body);
            }
            catch (Exception)
            {
                // No mail app registered for mailto: use Gmail in the browser instead.
                try
                {
                    Process.Start("https://mail.google.com/mail/?view=cm&fs=1&to=" + Uri.EscapeDataString(to) + "&su=" + subject + "&body=" + body);
                }
                catch (Exception)
                {
                    CopyId(to);
                }
            }

            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception)
            {
                // The path is in the status line below.
            }

            SetStatus("Request saved to your Desktop. Drag it into the email and send it; " +
                      "when the license comes back, click Import license file.", false);
        }

        private void ImportLicense()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Open the license file";
                dialog.Filter = "License (*.lic)|*.lic|All files (*.*)|*.*";

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                string text;
                try
                {
                    text = File.ReadAllText(dialog.FileName);
                }
                catch (Exception ex)
                {
                    SetStatus("The file could not be read: " + ex.Message, true);
                    return;
                }

                LicenseResult result = LicenseGate.VerifyText(productId, text);
                if (!result.IsValid)
                {
                    SetStatus(result.Message, true);
                    return;
                }

                try
                {
                    LicenseStore.Save(result.Payload.LicenseId, text);
                }
                catch (Exception ex)
                {
                    SetStatus("The license is good but could not be kept: " + ex.Message, true);
                    return;
                }

                MessageBox.Show(this, result.Message + (result.DaysLeft.HasValue
                        ? "\r\nIt runs for another " + result.DaysLeft.Value + " days."
                        : string.Empty),
                    productName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private void SetStatus(string message, bool isProblem)
        {
            statusLabel.ForeColor = isProblem ? Color.Firebrick : Color.DarkGreen;
            statusLabel.Text = message;
        }
    }
}
