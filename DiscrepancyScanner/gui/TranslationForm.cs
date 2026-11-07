using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace DiscrepancyScanner
{
    public partial class TranslationForm : System.Windows.Forms.Form
    {
        MyTransformation trans;
        Document doc;
        UIDocument uiDoc;
       
        
        public TranslationForm(Document _doc, UIDocument _uiDoc )
        {
           
            uiDoc = _uiDoc;
            doc = _doc;
            InitializeComponent();
            trans = new MyTransformation();
            
            
        }

    
   

        private void PickPt_Click(object sender, EventArgs e)
        {
            Owner.WindowState = FormWindowState.Minimized;
            
            XYZ translationPointXYZ = uiDoc.Selection.PickPoint("Select The Point where The Etabs Origin Exists");
            trans.Translation = Form1.getMyPtFromXYZ(translationPointXYZ).getInvertValues();
            PickLabelX.Text = $"X:";
            Xtext.Text = $" {Math.Round(trans.Translation.X,2)}";
            PickLabelY.Text = $"Y:";
            Ytext.Text = $" {Math.Round(trans.Translation.Y,2)}";
            PickLabelZ.Text = $"Z:";
            Ztext.Text = $" {Math.Round(trans.Translation.Z,2)}";
            Owner.WindowState = FormWindowState.Normal;

        }

        private void Xtext_TextChanged(object sender, EventArgs e)
        {
            updateTranslation();
        }

        private void updateTranslation()
        {
            trans.Translation.X = Form1.ConvertToDouble(Xtext.Text);
            trans.Translation.Y = Form1.ConvertToDouble(Ytext.Text);
            trans.Translation.Z = Form1.ConvertToDouble(Ztext.Text);

        }

        private void Okbut_Click(object sender, EventArgs e)
        {
            Ok();
        }

        private void Ok()
        {
            Form1.ModelTransformation=trans;
            
            this.Close();
        }

        private void CancelBut_Click(object sender, EventArgs e)
        {
            Cancel();
        }

        private void Cancel()
        {
            this.Close();
        }

        private void TranslationForm_KeyPress(object sender, KeyEventArgs e)
        {
            if (e.KeyCode ==Keys.Escape)
            {
                Cancel();
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            updateRotation(); 
           
        }

        private void updateRotation()
        {
            trans.Rotation = Form1.ConvertToDouble(angleText.Text);
        }

        private void mirroredCheckX_CheckedChanged(object sender, EventArgs e)
        {
            updateMirrorVisability();
            if (mirroredCheckX.Checked || mirroredCheckY.Checked)
            {
                
                trans.Rotation = 0;
                
            }
            trans.IsMirroredAboutX = mirroredCheckX.Checked;
            trans.IsMirroredAboutY = mirroredCheckY.Checked;
          
        }

        private void updateMirrorVisability()
        {
            if (!mirroredCheckX.Checked && !mirroredCheckY.Checked)
            {
                foreach (System.Windows.Forms.Control item in AngleGroupBox.Controls)
                {
                    item.Enabled = true;
                }

            }
            if (mirroredCheckX.Checked || mirroredCheckY.Checked)
            {

                foreach (System.Windows.Forms.Control item in AngleGroupBox.Controls)
                {
                    item.Enabled = false;
                }

            }
        }

        private void angleText_EnabledChanged(object sender, EventArgs e)
        {
            updateRotation();
        }

        private void groupBox1_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {

            if (e.KeyCode == Keys.Escape)
            {
                Cancel();
            }
        }

        private void Ytext_Leave(object sender, EventArgs e)
        {
            updateTranslation();
        }
    }
}
