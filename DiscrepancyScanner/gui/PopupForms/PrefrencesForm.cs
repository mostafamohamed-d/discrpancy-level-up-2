using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscrepancyScanner
{
    public partial class PrefrencesForm : Form
    {
        PrefrencesData pref;
        public PrefrencesForm()
        {
            pref = PrefrencesData.getPrefrencesData();
            InitializeComponent();
            ReadPrefrencesData();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            updatePrefrencesData();

        }

        private void updatePrefrencesData()
        {
            pref.LocationTolerance = ((double)numericLocationTolerance.Value);
            pref.LengthTolerance = ((double)numericLengthTolerance.Value);

            pref.b1Factor = ((double)numericBotLeft.Value);
            pref.b2Factor = ((double)numericBotMid.Value);
            pref.b3Factor = ((double)numericBotRight.Value);

            pref.t1Factor = ((double)numericTopLeft.Value);
            pref.t2Factor = ((double)numericTopMid.Value);
            pref.t3Factor = ((double)numericTopRight.Value);

            pref.fyForHeighStrength = ((double)numericFy.Value);
            RevitEngine.IgnoredNameList = new string[] { richTextBox1.Text };
        }

        private void ReadPrefrencesData()
        {

           
            numericLocationTolerance.Value = (decimal)pref.LocationTolerance;
            numericLengthTolerance.Value = (decimal)pref.LengthTolerance;



            numericBotLeft.Value = (decimal)pref.b1Factor;
            numericBotMid.Value = (decimal)pref.b2Factor;
            numericBotRight.Value = (decimal)pref.b3Factor;



            numericTopLeft.Value = (decimal)pref.t1Factor;
            numericTopMid.Value = (decimal)pref.t2Factor;
            numericTopRight.Value = (decimal)pref.t3Factor;

            numericFy.Value = (decimal)pref.fyForHeighStrength;

            richTextBox1.Text = "";
            foreach (string str in RevitEngine.IgnoredNameList)
            {
                richTextBox1.Text += str;
                richTextBox1.Text += ", ";
            }

         
        }

        private void PrefrencesForm_Load(object sender, EventArgs e)
        {
            ReadPrefrencesData();
        }


    }
}
