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
    public partial class EtabsFrameForm : Form
    {
        public EtabsFrameForm(FrameData_e frm)
        {
            InitializeComponent();
            DataTable dt = datatablefromFrame(frm);

            dataGridView1.DataSource = dt;
            
            foreach (DataGridViewColumn col in dataGridView1.Columns) 
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }
            // UpdateDataGridViewColors("State");    
        }

        private void UpdateDataGridViewColors(string columnName)
        {


            for (int i = 0; i < dataGridView1.Rows.Count - 1; i++)
            {

                if (dataGridView1.Rows[i].Cells[0].Value.ToString().Contains("Ratio"))
                {
                    double currentRatio = -1;
                    double.TryParse(dataGridView1.Rows[i].Cells[columnName].Value.ToString(), out currentRatio);

                    if (currentRatio > 1)
                    {
                        dataGridView1.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                    }
                }

            }



        }

        public static DataTable datatablefromFrame(FrameData_e frameData)
        {

            DataTable dataTable = new DataTable(frameData.revitID.ToString());
            //Get all the properties

            //
            dataTable.Columns.Add("Item");
            dataTable.Columns.Add("Etabs");
            //
            dataTable.Rows.Add("01-ID", frameData.etabsUname);
            dataTable.Rows.Add("02-Section Name", frameData.etabsSecName);
            dataTable.Rows.Add("02-1 Width", frameData._width);
            dataTable.Rows.Add("02-2 Height", frameData._depth);

            dataTable.Rows.Add("03-Top Left", frameData.AsTopLeft);
            dataTable.Rows.Add("04-Top Mid", frameData.AsTopMid);
            dataTable.Rows.Add("05-Top Right", frameData.AsTopRight);
            dataTable.Rows.Add("06-Bot Left", frameData.AsBotLeft);
            dataTable.Rows.Add("07-Bot Mid", frameData.AsBotMid);
            dataTable.Rows.Add("08-Bot Right", frameData.AsBotRight);

            dataTable.Rows.Add("09-Links Left (Scaled to Y '8')",frameData.AsLinksLeft_e);
            dataTable.Rows.Add("10-Links Mid (Scaled to Y '8')", frameData.AsLinksMid_e);
            dataTable.Rows.Add("11-Links Right (Scaled to Y '8')", frameData.AsLinksRight_e);


            dataTable.Rows.Add("12-Start Point", frameData.Start);
            dataTable.Rows.Add("13-End Point", frameData.End);




            return dataTable;
        }

        private void EtabsFrameForm_KeyPress(object sender, KeyPressEventArgs e)
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

        private void dataGridView1_KeyPress(object sender, KeyPressEventArgs e)
        {
            checkKeyPressed(e);
        }
    }
}
