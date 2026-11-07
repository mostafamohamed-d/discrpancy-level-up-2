
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
using Autodesk.Revit.Attributes;
using System.Collections.ObjectModel;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Reflection;
using CSiAPIv1;
//using ETABS2015;

namespace DiscrepancyScanner
{
   
    public partial class Form1 : System.Windows.Forms.Form
    {
        private ExternalCommandData commandData;
        UIDocument uiDoc;
        Document doc;
        public IList<FrameData_r> frames_r;
        public static bool isDataFromFile = false;
        
        private static MyTransformation modelTransformation;

        DataContainer dt;
       
        
        CompareEngine com;
        EtabsEngine et;

        public static MyTransformation ModelTransformation { get => modelTransformation; set => modelTransformation = value; }

       

        public enum eLength
        {
            NotApplicable,
            inch,
            ft,
            micron,
            mm,
            cm,
            m

        }
        
        public enum eForce
        {
            NotApplicable,
            kN,
            tonf,
            kgf,
            kip,
            lb,
            N

        }

        public Form1(ExternalCommandData _commandData)
        {
            commandData = _commandData;
            uiDoc = _commandData.Application.ActiveUIDocument;
            
            doc = _commandData.Application.ActiveUIDocument.Document;

            
            InitializeComponent();
            dt = DataContainer.getDataContainer();
            
           
        }

        internal static void showMassage(string v)
        {
            MessageBox.Show(v);
        }

      
        private void getAllRevitData()
        {
            
            //RevitEngine revitEngine = new RevitEngine(doc, ModelTransformation);
            //frames_r = revitEngine.getFilterdFrames();

            //richTextBox1.Text += revitEngine.warnings;
        }

        

        private void UpdateDataViewr<T>(List<T> list)
        {
            try
            {
                DataTable dt = dataTableFromList(list);
                dataGridView1.DataSource = dt;
                updateUnsafeElements("Note");

                textBox1.Text = dt.Rows.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
               
            }
           
           

        }
   

 
        private void button2_Click(object sender, EventArgs e)
        {
            isDataFromFile = false;
            dt. Frames_e = getAllDataFromEtabs();
            UpdateDataViewr(dt.Frames_e );

        }

        public List<FrameData_e> getAllDataFromEtabs()
        {
            IList<FrameData_e> fr;
            et = new EtabsEngine();
            fr = (et.getEtabsData()).frames;
            return fr.ToList<FrameData_e>();
            

        }

        private void button3_Click(object sender, EventArgs e)
        {
            try
            {
              
                getAllRevitData();
                com =  CompareEngine.GetCompareEngine ();
                com.startComparison(frames_r, dt.Frames_e, ref et);
                UpdateDataViewr(com.PairedFrames);
            }
            catch (Exception ex)
            {

                MessageBox.Show($"can not compair\n {ex.Message}");
            }

        }

      
       
        private void Form1_Load(object sender, EventArgs e)
        {
            newGui();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            UpdateDataViewr(com.RemainingEtabsframes);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            UpdateDataViewr(com.UnmatchedRevit);
        }

        private void button6_Click(object sender, EventArgs e)
        {
           Selection selection = uiDoc.Selection;
           ICollection<ElementId> selectedElemnts =getSelectedElemntsID();
           selection.SetElementIds(selectedElemnts);
        }

        private ICollection<ElementId> getSelectedElemntsID()
        {
            try
            {
                Collection  <ElementId> selectedElemnts = new Collection<ElementId>() ;
                
                int count = dataGridView1.SelectedCells.Count;

                for (int i = 0; i < count; i++)
                {
                    int selectedElementIndex = dataGridView1.SelectedCells[i].RowIndex;
                    long revitID = getRevitIDofIndex(selectedElementIndex);
                    if (revitID != 0)
                    {
                        ElementId id = new ElementId(revitID);
                        selectedElemnts.Add(id);
                    }

                }
                
                    return selectedElemnts;

            }
            catch (Exception)
            {

                return null;
            }


            

        }

        private long getRevitIDofIndex(int selectedElementIndex)
        {
            string ColName = "RevitID";
            // Read as long: element ids are Int64 from Revit 2024 on, and parsing one into an
            // int would quietly answer 0 for anything that did not fit.
            long id = 0;
            try
            {
                if (dataGridView1.Rows[0].Cells[ColName].Value.ToString() != null)
                {
                    long.TryParse( dataGridView1.Rows[selectedElementIndex].Cells[ColName].Value.ToString(),out id);
                }
            }
            catch (Exception)
            {
            }
            return id;
        }
    
           
        private void button7_Click(object sender, EventArgs e)
        {
            try
            {
                dt.getEtabsDataFromFile();
                UpdateDataViewr(dt.Frames_e);
               
                
               
                isDataFromFile = true;
             }
            catch (Exception exp)
            {
                MessageBox.Show("Can not optain data from File \n"+ exp.Message);
            }

}       


        
   

        private void updateUnsafeElements(string ColName)
        {
            try
            {
                if (dataGridView1.Rows[0].Cells[ColName].Value.ToString() != null)
                {

                    for (int i = 0; i < dataGridView1.Rows.Count; i++)
                    {
                        try
                        {
                            if (dataGridView1.Rows[i].Cells[ColName].Value.ToString() != "")
                            {
                                dataGridView1.Rows[i].DefaultCellStyle.BackColor = System.Drawing.Color.Red;
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }
                }

            }
            catch (Exception)
            {

                
            }
            
        }

  

        private void button8_Click(object sender, EventArgs e)
        {
            UpdateDataViewr(com.PairedFrames);
        }

        private void button9_Click(object sender, EventArgs e)
        {
            
            TranslationForm transForm = new TranslationForm(doc,uiDoc);
            
            transForm.Show(this);
            
        }
        public static MyPoint getMyPtFromXYZ(XYZ pt1)
        {
            return new MyPoint(pt1.X, pt1.Y, pt1.Z);
        }
        public static double ConvertToDouble(string Value)
        {
            if (Value == null)
            {
                return 0;
            }
            else
            {
                double OutVal;
                double.TryParse(Value, out OutVal);

                if (double.IsNaN(OutVal) || double.IsInfinity(OutVal))
                {
                    return 0;
                }
                return OutVal;
            }
        }
        public static DataTable dataTableFromList<T>(List<T> items)
        {

            DataTable dataTable = new DataTable(typeof(T).Name);
            //Get all the properties
            PropertyInfo[] Props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo prop in Props)
            {
                //Setting column names as Property names
                dataTable.Columns.Add(prop.Name);
                dataTable.Columns[prop.Name].DataType = prop.PropertyType;
            }
            foreach (T item in items)
            {
                var values = new object[Props.Length];
                for (int i = 0; i < Props.Length; i++)
                {
                    //inserting property values to datatable rows
                    values[i] = Props[i].GetValue(item, null);
                }
                dataTable.Rows.Add(values);
            }
            //put a breakpoint here and check datatable
            return dataTable;
        }

        private void dataGridView1_Sorted(object sender, EventArgs e)
        {
            updateUnsafeElements("Note");
        }

        private void NewUI_Click(object sender, EventArgs e)
        {
            newGui();
            

        }

        private void newGui()
        {
            GUI guiForm = new GUI(commandData);
            guiForm.KeyPreview = true;

            guiForm.Show();
            this.Close();
        }
    }
}
