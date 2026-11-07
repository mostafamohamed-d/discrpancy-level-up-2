using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DiscrepancyScanner
{
    public partial class PairedFrameForm :  Form
    {
        double[] utilizationFactors = new double[14] ;
        public PairedFrameForm( PairedFrame frm)
        {
            InitializeComponent();
            fillUtilizationFactors();
            DataTable dt = datatablefromFrame(frm);
           
            dataGridView1.DataSource = dt;
            
            foreach (DataGridViewColumn col in dataGridView1.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            
        }

        private void fillUtilizationFactors()
        {
           var Pref = PrefrencesData.getPrefrencesData();
            utilizationFactors[0] = 0;
            utilizationFactors[1] = 0;
            utilizationFactors[2] = 0;
            utilizationFactors[3] = 0;
            utilizationFactors[4] = 0;
            utilizationFactors[5] = Pref.t1Factor;
            utilizationFactors[6] = Pref.t2Factor;
            utilizationFactors[7] = Pref.t3Factor;
            utilizationFactors[8] = Pref.b1Factor;
            utilizationFactors[9] = Pref.b2Factor;
            utilizationFactors[10] = Pref.b3Factor;
            utilizationFactors[11] = Pref.LinksRatio;
            utilizationFactors[12] = Pref.LinksRatio;
            utilizationFactors[13] = Pref.LinksRatio;
        }

        private void UpdateDataGridViewColors(string columnName)
        {
             

            for (int i = 0; i < dataGridView1.Rows.Count; i++)
                    {


                if (dataGridView1.Rows[i].Cells[0].Value.ToString().Contains("Ratio"))
                {
                    double currentRatio = -1;
                        double.TryParse(dataGridView1.Rows[i].Cells[1].Value.ToString(), out currentRatio);

                            if (currentRatio > utilizationFactors[i])
                            {
                             // dataGridView1.Rows[i].Cells[columnName].DefaultCellStyle.ForeColor = Color.Red;
                                dataGridView1.Rows[i].Cells[columnName].Style.ForeColor = Color.Red;
                               

                            }
                }
                        
            }
            var val = dataGridView1.Rows[4].Cells[columnName].Value;
            if (val.ToString() != "ignore")
            {
                dataGridView1.Rows[4].Cells[columnName].Style.ForeColor= Color.Red;
            }
            dataGridView1.Update();
                

           
        }

        public static DataTable datatablefromFrame(PairedFrame frameData)
        {

            DataTable dataTable = new DataTable(frameData.EtabsUniqueName);
          
            //
            dataTable.Columns.Add("Comparasion");
            dataTable.Columns.Add("State");
            dataTable.Columns.Add("Etabs");
            dataTable.Columns.Add("Revit");

            //
            dataTable.Rows.Add("01-ID","---",frameData.etabsFrame.EtabsUname,frameData.RevitID);
            dataTable.Rows.Add("02-Section Name","" ,frameData.etabsFrame.etabsSecName, frameData.revitFrame.RevitName);
            dataTable.Rows.Add("02-1 Width","", frameData.etabsFrame._width,frameData.revitFrame._width);
            dataTable.Rows.Add("02-2 Height","" ,frameData.etabsFrame._depth,frameData.revitFrame._depth);
            dataTable.Rows.Add("03-Design State", frameData.MyDesignState);
            dataTable.Rows.Add("04-Top Left Ratio", frameData.T1_Ratio, frameData.ET_topLeft,frameData.RVT_topLeft);
            dataTable.Rows.Add("05-Top Mid Ratio", frameData.T2_Ratio, frameData.ET_topMid,frameData.RVT_topMid);
            dataTable.Rows.Add("06-Top Right Ratio", frameData.T3_Ratio,frameData.ET_topRight,frameData.RVT_topRight);
            dataTable.Rows.Add("07-Bot Left Ratio", frameData.B1_Ratio, frameData.ET_bottomLeft, frameData.RVT_bottomLeft);
            dataTable.Rows.Add("08-Bot Mid Ratio", frameData.B2_Ratio, frameData.ET_bottom, frameData.RVT_bottom);
            dataTable.Rows.Add("09-Bot Right Ratio", frameData.B3_Ratio, frameData.ET_bottomRight, frameData.RVT_bottomRight);

           
            string[] LinksEtabsText = frameData.getEtabsRequiredLinksString();
            string[] LinksRevitText = frameData.getRevitRequiredLinksString();

            dataTable.Rows.Add("10-Links Left Ratio", frameData.L_leftRatio, LinksEtabsText[0], LinksRevitText[0]);
            dataTable.Rows.Add("11-Links Mid Ratio", frameData.L_midRatio, LinksEtabsText[1], LinksRevitText[1]);
            dataTable.Rows.Add("12-Links Right Ratio", frameData.L_rightRatio, LinksEtabsText[2], LinksRevitText[2]);



            return dataTable;
        }

        private void dataGridView1_KeyPress(object sender, KeyPressEventArgs e)
        {
            checkKeyPressed(e);
        }
        private void checkKeyPressed(KeyPressEventArgs e)
        {
            if (e.KeyChar == ((char)Keys.Escape))
            {
                this.Close();
            }
        }

        private void PairedFrameForm_Load(object sender, EventArgs e)
        {
            UpdateDataGridViewColors("State");
        }
    }
}
