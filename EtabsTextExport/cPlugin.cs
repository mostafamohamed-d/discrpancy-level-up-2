using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using DiscrepancyScanner;
using ETABSv1;

namespace EtabsTextExport
{
    /// <summary>
    /// Reads the open ETABS model and writes it as the design model file (.json) the
    /// Discrepancy Scanner opens with "read from a saved file".
    ///
    /// ETABS hands a plugin ETABSv1 types, while the scanner's reader is written against
    /// CSiAPIv1 - the API shared with SAFE. So the reader gets its own CSiAPIv1 handle on this
    /// same ETABS, by process id, rather than the one ETABS handed over.
    /// </summary>
    public class cPlugin
    {
        private const string Title = "Discrepancy Scanner - design model file";

        public void Main(ref cSapModel SapModel, ref cPluginCallback ISapPlugin)
        {
            // ETABS waits on Finish whatever happens; an exception that skipped it would leave
            // the plugin looking busy until ETABS is closed.
            int result = 1;
            try
            {
                result = Export(SapModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show("The design model file could not be written.\r\n\r\n" + ex,
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ISapPlugin.Finish(result);
            }
        }

        public int Info(ref string Text)
        {
            Text = "Writes the open model as a design model file (.json) for the Discrepancy "
                   + "Scanner to read in Revit without ETABS.";
            return 0;
        }

        private static int Export(cSapModel etabsModel)
        {
            string modelPath = etabsModel.GetModelFilename(true) ?? "";

            CSiAPIv1.cSapModel model = Attach(modelPath, out string problem);
            if (model == null)
            {
                MessageBox.Show(problem, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            string target;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save the design model file";
                dialog.Filter = EtabsModelFile.FileFilter;
                dialog.DefaultExt = EtabsModelFile.DefaultExtension;
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                if (Path.IsPathRooted(modelPath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(modelPath);
                    dialog.FileName = Path.GetFileNameWithoutExtension(modelPath) + EtabsModelFile.DefaultExtension;
                }
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return 1;
                }
                target = dialog.FileName;
            }

            EtabsDataContainer data;
            Cursor previous = Cursor.Current;
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                data = new EtabsEngine(model, CsiProduct.Etabs, null).getEtabsData();
            }
            finally
            {
                Cursor.Current = previous;
            }

            if (EtabsModelFile.IsEmpty(data))
            {
                MessageBox.Show("Nothing was read from the model - no beams, columns, walls or slabs.\r\n"
                                + "The file was not written.",
                                Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            EtabsModelFile.Write(target, data);

            MessageBox.Show("Saved " + EtabsModelFile.Describe(data) + " to\r\n" + target
                            + "\r\n\r\nIn Revit, tick \"read from a saved file\" and open it.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        /// <summary>
        /// A CSiAPIv1 handle on the ETABS this plugin is running inside: by this process's id,
        /// so a second ETABS open on the machine cannot be picked up by mistake.
        /// </summary>
        private static CSiAPIv1.cSapModel Attach(string modelPath, out string problem)
        {
            int processId = Process.GetCurrentProcess().Id;
            string lastProblem = null;

            foreach (string typeName in new[] { "CSI.ETABS.API.ETABSObject", "CSI.ETABS.API.SapObject" })
            {
                CsiInstance instance = new CsiInstance(CsiProduct.Etabs, processId, typeName, modelPath);
                CSiAPIv1.cSapModel model = CsiProducts.Attach(instance, out lastProblem);
                if (model != null)
                {
                    problem = null;
                    return model;
                }
            }

            problem = "Could not reach this ETABS through the CSI API.\r\n" + lastProblem;
            return null;
        }
    }
}
