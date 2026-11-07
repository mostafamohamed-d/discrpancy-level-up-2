using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using DiscrepancyScanner.Wpf;
using CSiAPIv1;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DiscrepancyScanner
{
    public partial class EtabsForm : Form
    {
        
        cSapModel sp;
        cPluginCallback callBack;
        DataContainer dt;
        EtabsDataContainer etData;
        EtabsEngine et;
        public EtabsForm(ref cSapModel sapModel, ref cPluginCallback _callBack)
        {

            InitializeComponent();
            sp = sapModel;
            callBack = _callBack;
             et = new EtabsEngine(sp);
            dt = DataContainer.getDataContainer();
            etData = new EtabsDataContainer();
            this.Name = "1";

        }

        private void button1_Click(object sender, EventArgs e)
        {
            
            getAllData();
            
        }

        private void getAllData()
        {
            EtabsEngine.IgnoredNameList = new string[] { richIgnoreList.Text };

            etData = et.getEtabsData();
            if (etData.frames.Count<1)
            {
                MessageBox.Show("No Design data Found");
                dataGridView1.DataSource = etData.frames;

            }
            else
            {
                dataGridView1.DataSource = etData.frames;
            }

            showImportIssues();
        }

        /// <summary>
        /// Everything the read had to say, shown here rather than only on the Revit side.
        ///
        /// The engine gathers areas it could not make sense of, walls with no readable section,
        /// how old the text file was, and the timings and verdicts from reading the same thing
        /// two ways. A read run from inside ETABS used to collect the lot and drop it, which put
        /// the one place those checks are worth watching out of reach of the person watching.
        ///
        /// Shown after the grid is filled rather than before, so the numbers are already on
        /// screen behind the dialog.
        /// </summary>
        private void showImportIssues()
        {
            IList<string> issues = et.ImportIssues;

            if (issues == null || issues.Count == 0)
            {
                return;
            }

            using (EtabsIssuesForm window = new EtabsIssuesForm("ETABS read", issues))
            {
                window.ShowDialog(this);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (etData.frames.Count < 1)
            {
                MessageBox.Show("No Frames Found In Etabs Model");
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog();
            dialog.RestoreDirectory = true;
            dialog.Filter = EtabsModelFile.FileFilter;
            dialog.DefaultExt = EtabsModelFile.DefaultExtension;
            dialog.AddExtension = true;

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    EtabsModelFile.Write(dialog.FileName, etData);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Save failed", MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning);
                }
            }

            dataGridView1.DataSource = etData.frames;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            dt.getEtabsDataFromFile();
            dataGridView1.DataSource = dt.Frames_e;
        }

        private void EtabsForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            callBack.Finish(0);
        }

        private void richIgnoreList_TextChanged(object sender, EventArgs e)
        {
            getAllData();
        }

        private void EtabsForm_Load(object sender, EventArgs e)
        {
            showLoadSetSettings();
        }

        #region the load set settings

        /// <summary>
        /// Whatever else the settings file holds, kept so that saving from here puts it back
        /// rather than dropping it.
        ///
        /// The file has two halves: the preferences, and where the windows on the Revit side
        /// were left. Only the first is any of this form's business, and writing the file means
        /// writing both - so the second is read here and handed straight back untouched.
        /// </summary>
        private IDictionary<string, object> otherSettings =
            new Dictionary<string, object>();

        /// <summary>
        /// True while the boxes are being set from the file, so that doing so does not read as
        /// the user having clicked them and write the file straight back.
        /// </summary>
        private bool showingSettings;

        /// <summary>
        /// Puts the two load set boxes where the settings file left them.
        ///
        /// This is the first thing on the ETABS side to read that file at all: until now these
        /// preferences were only ever loaded on the Revit side, so this form ran on the
        /// built-in defaults. Reading it here is what makes the boxes tell the truth - a box
        /// that always came up ticked whatever you last chose would be worse than no box - and
        /// it means the rest of the preferences are honoured on this side too.
        /// </summary>
        private void showLoadSetSettings()
        {
            PrefrencesData preferences = PrefrencesData.getPrefrencesData();

            try
            {
                otherSettings = UserSettings.Load(preferences);
            }
            catch (Exception)
            {
                // No settings file, or one that cannot be read. The defaults stand, and the
                // boxes below will show them.
            }

            showingSettings = true;

            chkReadFromTextFile.Checked = preferences.ReadFromEtabsTextFile;

            showingSettings = false;

            toolTips.SetToolTip(chkReadFromTextFile,
                "Reads the shell uniform load sets out of the .$et file ETABS writes beside the model on"
                + " every save, rather than asking ETABS for the same two tables.\r\n\r\n"
                + "It is the same answer reached far quicker: the table saying which shell carries which"
                + " load set has a row per shell, and on a large model that is well over a hundred"
                + " thousand rows handed over one string at a time.\r\n\r\n"
                + "Safe to leave on - a model with no text file beside it falls back to the tables on its"
                + " own. The catch is that the file is only as new as the last save, so the import says"
                + " how many of the shells it names this model has not got.");

            toolTips.SetToolTip(groupBox3, "Kept in " + UserSettings.FilePath
                                           + ", the same settings the Revit side uses.");
        }

        /// <summary>
        /// The box clicked. It writes to the one settings file the Revit side reads, so a choice
        /// made here is the same choice over there - which is the point of the setting appearing
        /// in both places rather than two settings that happen to share a name.
        /// </summary>
        private void loadSetSettingChanged(object sender, EventArgs e)
        {
            if (showingSettings)
            {
                return;
            }

            PrefrencesData preferences = PrefrencesData.getPrefrencesData();

            preferences.ReadFromEtabsTextFile = chkReadFromTextFile.Checked;

            if (!UserSettings.Save(preferences, otherSettings))
            {
                // Said once, where it was clicked, rather than left to fail quietly and have the
                // box come back unticked next time with no reason given.
                MessageBox.Show(UserSettings.LastError, "Discrepancy Scanner",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        #endregion
    }
}
