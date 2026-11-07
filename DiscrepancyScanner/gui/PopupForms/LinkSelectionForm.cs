using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Point = System.Drawing.Point;

namespace DiscrepancyScanner
{
    public partial class LinkSelectionForm : System.Windows.Forms.Form
    {
        private CheckedListBox checkedListBoxLinks;
        private Button btnOk;
        private Button btnCancel;
        private Label lblDescription;
        private CheckBox chkShowNested;
        private CheckBox chkShowOverlay;
        private CheckBox chkShowAttached;

        public List<Document> SelectedDocuments { get; private set; }

        /// <summary>
        /// The titles of <see cref="SelectedDocuments"/>, for ticking the same ones next time.
        /// </summary>
        public List<string> SelectedTitles { get; private set; } = new List<string>();

        private List<RevitEngine.LinkInfo> allDocuments;

        /// <summary>
        /// What was picked the last time the dialog was answered, or null the first time,
        /// when the ticks are guessed from the names instead.
        /// </summary>
        private readonly HashSet<string> previousChoice;

        public LinkSelectionForm(List<RevitEngine.LinkInfo> documents, IEnumerable<string> previousChoice = null)
        {
            this.previousChoice = previousChoice == null ? null : new HashSet<string>(previousChoice);

            InitializeComponent();
            allDocuments = new List<RevitEngine.LinkInfo>();

            // Deduplicate logic
            HashSet<string> seenTitles = new HashSet<string>();
            foreach (var doc in documents)
            {
                if (!seenTitles.Contains(doc.Title))
                {
                    allDocuments.Add(doc);
                    seenTitles.Add(doc.Title);
                }
            }

            SelectedDocuments = new List<Document>();

            // Load Preferences
            var pref = PrefrencesData.getPrefrencesData();
            chkShowOverlay.Checked = pref.IncludeOverlayLinks;
            chkShowAttached.Checked = pref.IncludeAttachedLinks;

            PopulateList();
        }

        private void InitializeComponent()
        {
            this.checkedListBoxLinks = new CheckedListBox();
            this.btnOk = new Button();
            this.btnCancel = new Button();
            this.lblDescription = new Label();
            this.chkShowNested = new CheckBox();
            this.chkShowOverlay = new CheckBox();
            this.chkShowAttached = new CheckBox();
            this.SuspendLayout();
            // 
            // lblDescription
            // 
            this.lblDescription.AutoSize = true;
            this.lblDescription.Location = new Point(12, 12);
            this.lblDescription.Name = "lblDescription";
            this.lblDescription.Size = new Size(224, 16);
            this.lblDescription.TabIndex = 0;
            this.lblDescription.Text = "Select Linked Models to Include:";
            // 
            // checkedListBoxLinks
            // 
            this.checkedListBoxLinks.CheckOnClick = true;
            this.checkedListBoxLinks.FormattingEnabled = true;
            this.checkedListBoxLinks.Location = new Point(15, 35);
            this.checkedListBoxLinks.Name = "checkedListBoxLinks";
            this.checkedListBoxLinks.Size = new Size(350, 180);
            this.checkedListBoxLinks.TabIndex = 1;
            // 
            // chkShowNested
            // 
            this.chkShowNested.AutoSize = true;
            this.chkShowNested.Location = new Point(15, 225);
            this.chkShowNested.Name = "chkShowNested";
            this.chkShowNested.Size = new Size(135, 20);
            this.chkShowNested.TabIndex = 4;
            this.chkShowNested.Text = "Show Nested Links";
            this.chkShowNested.UseVisualStyleBackColor = true;
            this.chkShowNested.Checked = true;
            this.chkShowNested.CheckedChanged += new EventHandler(this.ForceRepopulate_CheckedChanged);
            // 
            // chkShowOverlay
            // 
            this.chkShowOverlay.AutoSize = true;
            this.chkShowOverlay.Location = new Point(15, 245);
            this.chkShowOverlay.Name = "chkShowOverlay";
            this.chkShowOverlay.Size = new Size(95, 20);
            this.chkShowOverlay.TabIndex = 5;
            this.chkShowOverlay.Text = "Show Overlay Links";
            this.chkShowOverlay.UseVisualStyleBackColor = true;
            this.chkShowOverlay.CheckedChanged += new EventHandler(this.ForceRepopulate_CheckedChanged);
            // 
            // chkShowAttached
            // 
            this.chkShowAttached.AutoSize = true;
            this.chkShowAttached.Location = new Point(15, 265);
            this.chkShowAttached.Name = "chkShowAttached";
            this.chkShowAttached.Size = new Size(95, 20);
            this.chkShowAttached.TabIndex = 6;
            this.chkShowAttached.Text = "Show Attached Links";
            this.chkShowAttached.UseVisualStyleBackColor = true;
            this.chkShowAttached.CheckedChanged += new EventHandler(this.ForceRepopulate_CheckedChanged);
            //
            // btnOk
            // 
            this.btnOk.Location = new Point(209, 245);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new Size(75, 25);
            this.btnOk.TabIndex = 2;
            this.btnOk.Text = "OK";
            this.btnOk.UseVisualStyleBackColor = true;
            this.btnOk.Click += new EventHandler(this.btnOk_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = DialogResult.Cancel;
            this.btnCancel.Location = new Point(290, 245);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new Size(75, 25);
            this.btnCancel.TabIndex = 3;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new EventHandler(this.btnCancel_Click);
            // 
            // LinkSelectionForm
            // 
            this.AcceptButton = this.btnOk;
            this.AutoScaleDimensions = new SizeF(8F, 16F);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new Size(380, 295);
            this.Controls.Add(this.chkShowAttached);
            this.Controls.Add(this.chkShowOverlay);
            this.Controls.Add(this.chkShowNested);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnOk);
            this.Controls.Add(this.checkedListBoxLinks);
            this.Controls.Add(this.lblDescription);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "LinkSelectionForm";
            this.StartPosition = FormStartPosition.CenterParent;
            this.Text = "Revit Links Selection";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void ForceRepopulate_CheckedChanged(object sender, EventArgs e)
        {
            // Save state of currently checked before repopulating
            var previouslyChecked = new HashSet<string>();
            for (int i = 0; i < checkedListBoxLinks.Items.Count; i++)
            {
                if (checkedListBoxLinks.GetItemChecked(i))
                {
                    RevitEngine.LinkInfo info = (RevitEngine.LinkInfo)checkedListBoxLinks.Items[i];
                    previouslyChecked.Add(info.Title);
                }
            }

            checkedListBoxLinks.Items.Clear();
            
            foreach (var linkInfo in allDocuments)
            {
                if (!chkShowNested.Checked && linkInfo.IsNested)
                    continue;
                if (!chkShowOverlay.Checked && linkInfo.IsOverlay)
                    continue;
                if (!chkShowAttached.Checked && linkInfo.IsAttached)
                    continue;

                // Restore previous check state or reapply logic if not present
                bool shouldCheck = false;
                if (previouslyChecked.Contains(linkInfo.Title))
                {
                    shouldCheck = true;
                }
                else
                {
                    shouldCheck = DetermineDefaultCheck(linkInfo);
                }

                checkedListBoxLinks.Items.Add(linkInfo, shouldCheck);
            }
            checkedListBoxLinks.DisplayMember = "Title";
        }

        private void PopulateList()
        {
            checkedListBoxLinks.Items.Clear();
            foreach (var linkInfo in allDocuments)
            {
                if (!chkShowNested.Checked && linkInfo.IsNested)
                    continue;
                if (!chkShowOverlay.Checked && linkInfo.IsOverlay)
                    continue;
                if (!chkShowAttached.Checked && linkInfo.IsAttached)
                    continue;

                bool shouldCheck = DetermineDefaultCheck(linkInfo);
                checkedListBoxLinks.Items.Add(linkInfo, shouldCheck);
            }
            checkedListBoxLinks.DisplayMember = "Title";
        }

        private bool DetermineDefaultCheck(RevitEngine.LinkInfo linkInfo)
        {
            // Asked again on a later import: what was picked then, not the guess.
            if (previousChoice != null) return previousChoice.Contains(linkInfo.Title);

            if (linkInfo.Doc == null || string.IsNullOrEmpty(linkInfo.Doc.Title)) return true; // Base document (usually doesn't have a title till saved, or can be null conditionally, but handles main)
            if (linkInfo.Title == "Current Document") return true; // Hardcoded fallback match

            return linkInfo.Title.ToUpper().Contains("-STR-");
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            // Save Preferences
            var pref = PrefrencesData.getPrefrencesData();
            pref.IncludeOverlayLinks = chkShowOverlay.Checked;
            pref.IncludeAttachedLinks = chkShowAttached.Checked;

            SelectedDocuments.Clear();
            SelectedTitles.Clear();
            for (int i = 0; i < checkedListBoxLinks.Items.Count; i++)
            {
                if (checkedListBoxLinks.GetItemChecked(i))
                {
                    RevitEngine.LinkInfo info = (RevitEngine.LinkInfo)checkedListBoxLinks.Items[i];
                    SelectedDocuments.Add(info.Doc);
                    SelectedTitles.Add(info.Title);
                }
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
