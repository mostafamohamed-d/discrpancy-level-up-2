using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Everything the ETABS read had to say, as one list.
    ///
    /// The engine has always gathered these - areas it could not make sense of, walls with no
    /// readable section, how old the text file was, and the timings and verdicts from reading
    /// the same thing two ways - into <see cref="EtabsEngine.ImportIssues"/>. Until now only the
    /// Revit side showed them, so a read run from inside ETABS collected the lot and dropped it,
    /// which made the both-routes check impossible to see from the place it is most useful.
    ///
    /// Built in code rather than in the designer: it is a title, a box and two buttons, and one
    /// more .resx to keep in step is not worth it.
    ///
    /// A text box rather than a list, because these are sentences and some of them are long -
    /// the both-routes verdict names five areas - and a list would cut them off at the right
    /// edge with no way to read the rest.
    /// </summary>
    internal sealed class EtabsIssuesForm : Form
    {
        private readonly IList<string> issues;

        public EtabsIssuesForm(string title, IList<string> issueList)
        {
            issues = issueList ?? new List<string>();

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowIcon = false;
            ClientSize = new Size(760, 420);
            MinimumSize = new Size(420, 260);

            Label headline = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Padding = new Padding(8, 8, 8, 0),
                Text = $"The ETABS read reported {issues.Count} line(s)."
            };

            TextBox box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Font = new Font(FontFamily.GenericSansSerif, 9f),
                Text = AsText()
            };

            // Selection starts at the top rather than over the whole text, which is what a read
            // only box does by default and reads as an error.
            box.HandleCreated += (sender, e) => box.Select(0, 0);

            Panel buttons = new Panel { Dock = DockStyle.Bottom, Height = 42 };

            Button copy = new Button
            {
                Text = "Copy",
                Width = 90,
                Height = 26,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Location = new Point(ClientSize.Width - 194, 8)
            };
            copy.Click += (sender, e) => CopyToClipboard();

            Button close = new Button
            {
                Text = "Close",
                Width = 90,
                Height = 26,
                Anchor = AnchorStyles.Right | AnchorStyles.Top,
                Location = new Point(ClientSize.Width - 98, 8),
                DialogResult = DialogResult.OK
            };

            buttons.Controls.Add(copy);
            buttons.Controls.Add(close);

            // Added fill first, then the docked edges, so the text box takes what is left over
            // rather than sitting under them.
            Controls.Add(box);
            Controls.Add(buttons);
            Controls.Add(headline);

            AcceptButton = close;
            CancelButton = close;
        }

        /// <summary>
        /// The lines numbered and separated by a blank one. A read of a large model raises
        /// enough of these that a wall of text is unreadable, and the numbers make it possible
        /// to say which one is being asked about.
        /// </summary>
        private string AsText()
        {
            StringBuilder text = new StringBuilder();

            for (int i = 0; i < issues.Count; i++)
            {
                text.Append(i + 1).Append(". ").Append(issues[i]).Append("\r\n\r\n");
            }

            return text.ToString();
        }

        private void CopyToClipboard()
        {
            try
            {
                Clipboard.SetText(AsText());
            }
            catch (Exception)
            {
                // Another application can hold the clipboard open. Nothing useful to do, and
                // not worth a second dialog on top of this one.
            }
        }
    }
}
