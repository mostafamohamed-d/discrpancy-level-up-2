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
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Point = System.Drawing.Point;
using TextBox = System.Windows.Forms.TextBox;

namespace DiscrepancyScanner
{
    public partial class GUI : System.Windows.Forms.Form
    {

        #region varriables

        #region REVIt Varriables
        private ExternalCommandData commandData;
        UIDocument uiDoc;
        Document doc;
        
        #endregion
        #region comparason Varriables
        CompareEngine com;
        EtabsEngine et;
        List<IDrawAble> visableFrames = new List<IDrawAble>();
        List<Column_e> visableColumns = new List<Column_e>();
        List<Wall_E> visableWalls = new List<Wall_E>();

        DataContainer dt;
        #endregion


        #region visaility Varriables

        private string selectedStory = "";
        private float Xscroll;
        private float Yscroll;
        private float ScaleFactor = .1f;
        private bool activeZoom = false;
        private bool zoomClick = false;
        private int curserIndex = 0;
        private float curserSize = 0;
        private Point firstZoomCorner;
        private MyPoint firstZoomCornerMM;
        private Point startPanLocation;
        private Point currentMouseLocation;
        private bool active_Pan = false;
        private bool selectionWindowActive = false;
        private Point panPoint;
        float heightInmm = 0;
        float widthtInmm = 0;
        MyPoint geometryBotLeft;
        MyPoint geometryTopright;
        Pen tempPen;
        #endregion

        #endregion
        public GUI(ExternalCommandData _commandData)
        {
            commandData = _commandData;
            uiDoc = _commandData.Application.ActiveUIDocument;
           
            InitializeComponent();

            ScaleToFit();
            calculateDrawingSizeinmm();
            InetializeMydata();
            dt = DataContainer.getDataContainer();
        }


        #region visability functions 

        private void ScaleToFit()
        {
            float minX = 0;
            float maxX = 0;
            float minY = 0;
            float maxY = 0;

            if (visableColumns.Count > 0 || visableFrames.Count >0)
            {



                foreach (var element in visableFrames)
                {
                    double minOfCurrentX = Math.Min(element.Start.X, element.End.X);
                    double maxOfCurrentX = Math.Max(element.Start.X, element.End.X);
                    double minOfCurrentY = Math.Min(element.Start.Y, element.End.Y);
                    double maxOfCurrentY = Math.Max(element.Start.Y, element.End.Y);

                    // check min x
                    if (minX > minOfCurrentX)
                    {
                        minX = (float)minOfCurrentX;
                    }

                    // check max x
                    if (maxX < maxOfCurrentX)
                    {
                        maxX = (float)maxOfCurrentX;
                    }

                    // check min Y
                    if (minY > minOfCurrentY)
                    {
                        minY = (float)minOfCurrentY;
                    }

                    // check max Y
                    if (maxY < maxOfCurrentY)
                    {
                        maxY = (float)maxOfCurrentY;
                    }


                }
                foreach (var element in visableColumns)
                {
                    double minOfCurrentX = Math.Min(element.Start.X, element.End.X);
                    double maxOfCurrentX = Math.Max(element.Start.X, element.End.X);
                    double minOfCurrentY = Math.Min(element.Start.Y, element.End.Y);
                    double maxOfCurrentY = Math.Max(element.Start.Y, element.End.Y);

                    // check min x
                    if (minX > minOfCurrentX)
                    {
                        minX = (float)minOfCurrentX;
                    }

                    // check max x
                    if (maxX < maxOfCurrentX)
                    {
                        maxX = (float)maxOfCurrentX;
                    }

                    // check min Y
                    if (minY > minOfCurrentY)
                    {
                        minY = (float)minOfCurrentY;
                    }

                    // check max Y
                    if (maxY < maxOfCurrentY)
                    {
                        maxY = (float)maxOfCurrentY;
                    }

                }
                float dx = maxX - minX;
                float dy = maxY - minY;



                ScaleFactor = Math.Min(pictureBox1.ClientSize.Width / (float)dx, pictureBox1.ClientSize.Height / (float)dy) / 2;

                geometryBotLeft = new MyPoint(minX, minY);
                geometryTopright = new MyPoint(maxX, maxY);

                Point firstCorner = cartesianToPoint(geometryBotLeft);
                Point secondCorner = cartesianToPoint(geometryTopright);
                setZoomwin(firstCorner, secondCorner);
            }
            

        }
        private void zoomIn()
        {
            int screenWidth = pictureBox1.ClientSize.Width;
            int screenHeight = pictureBox1.ClientSize.Height;
            Point firstCorner = new Point((int)(screenWidth * 0.1), (int)(screenHeight * 0.1));
            Point secondCorner = new Point((int)(screenWidth * 0.9), (int)(screenHeight * 0.9));

            setZoomwin(firstCorner, secondCorner);

        }
        private void zoomOut()
        {
            int screenWidth = pictureBox1.ClientSize.Width;
            int screenHeight = pictureBox1.ClientSize.Height;
            Point firstCorner = new Point((int)(screenWidth * -0.1), (int)(screenHeight * -0.1));
            Point secondCorner = new Point((int)(screenWidth * 1.1), (int)(screenHeight * 1.1));

            setZoomwin(firstCorner, secondCorner);

        }
        private void setZoomwin(Point firstCorner, Point secondCorner)
        {

            MyPoint p1 = pointToCartesian(firstCorner);
            MyPoint p2 = pointToCartesian(secondCorner);

            float botLeftX = (float)Math.Min(p1.X, p2.X);
            float botLeftY = (float)Math.Min(p1.Y, p2.Y);

            float widthOfDrawnBox = Math.Abs(firstCorner.X - secondCorner.X);
            float heightOfdrawnBox = Math.Abs(firstCorner.Y - secondCorner.Y);

            float widthScale = pictureBox1.ClientSize.Width / widthOfDrawnBox;
            float heightScale = pictureBox1.ClientSize.Height / heightOfdrawnBox;

            float newScale = Math.Min(widthScale, heightScale);

            ScaleFactor *= newScale;

            float wl = (pictureBox1.ClientSize.Width - widthOfDrawnBox * newScale) / 2;
            float hl = (pictureBox1.ClientSize.Height - heightOfdrawnBox * newScale) / 2;

            Xscroll = ScaleFactor * botLeftX - pixleToMm(wl);
            Yscroll = -ScaleFactor * botLeftY - pixleToMm(hl);


            pictureBox1.Refresh();



        }
        private void fitPic()
        {
            if (geometryBotLeft != null && geometryTopright != null)
            {
                Point firstCorner = cartesianToPoint(geometryBotLeft);
                Point secondCorner = cartesianToPoint(geometryTopright);
                setZoomwin(firstCorner, secondCorner);
            }
            else
            {
                ScaleToFit();
            }
        }

        #endregion

        #region visability calculations functoins

        private List<DrawAble> getRecTangleLines(MyPoint p1, MyPoint p2)
        {
            List<DrawAble> data = new List<DrawAble>();
            DrawAble L1 = new DrawAble(p1, new MyPoint(p2.X, p1.Y));
            DrawAble L2 = new DrawAble(L1.End, p2);
            DrawAble L3 = new DrawAble(p2, new MyPoint(p1.X, p2.Y));
            DrawAble L4 = new DrawAble(L3.End, p1);

            data.Add(L1);
            data.Add(L2);
            data.Add(L3);
            data.Add(L4);
            return data;

        }

        private float pixleToMm(float px)
        {
            return px * 25.4f / DPI;
        }
        private float mmToPixle(float px)
        {
            return px * DPI / 25.4f;
        }
        private float DPI
        {
            get
            {
                using (var g = CreateGraphics())
                    return g.DpiX;
            }
        }
        private MyPoint pointToCartesian(Point p)
        {
            return new MyPoint((pixleToMm(p.X) + Xscroll) / ScaleFactor,
                (pixleToMm(pictureBox1.Height - p.Y) - Yscroll) / ScaleFactor);
        }
        private Point cartesianToPoint(MyPoint p)
        {

            int x1 = (int)(DPI * (p.X * ScaleFactor - Xscroll) / 25.4f);

            int y1 = (int)(-DPI * (p.Y * ScaleFactor + Yscroll) / 25.4f) + pictureBox1.Height;
            return new Point(x1, y1);
        }
        private void calculateDrawingSizeinmm()
        {
            heightInmm = pixleToMm(pictureBox1.Height);
            widthtInmm = pixleToMm(pictureBox1.Width) / 2;
        }



        #endregion

        #region Draw Events


        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            Brush MessageBrush = new SolidBrush(Color.Black);
            e.Graphics.SetParameters(heightInmm, widthtInmm, Xscroll, Yscroll, ScaleFactor, pictureBox1.Width, pictureBox1.Height, MessageBrush);

            foreach (DrawAble item in visableFrames)
            {
                //if (item.Messages.Count>0)
                //{
                //    e.Graphics.DrawLine(item, item.Messages[0]);
                //}
                e.Graphics.DrawLine(item);
                
            }

            foreach (var item in visableColumns)
            {
                
                e.Graphics.FillMyPolygon(VisabilityController.columnsBrush, item.RectanglePoints);
                
                   
            }

            foreach (var item in visableWalls)
            {

                e.Graphics.FillMyPolygon(VisabilityController.wallBrush, item.DrawingPoints);


            }



            #region Draw temp Data

            // zoom box drawing

            if (zoomClick)
            {
                List<DrawAble> recLines = getRecTangleLines(firstZoomCornerMM, pointToCartesian(currentMouseLocation));

                foreach (var item in recLines)
                {
                    item.pen = tempPen;
                    e.Graphics.DrawLine(item);
                }
            }
            else if (selectionWindowActive)
            {

                List<DrawAble> recLines = getRecTangleLines(pointToCartesian(startPanLocation), pointToCartesian(currentMouseLocation));

                foreach (var item in recLines)
                {
                    item.pen = tempPen;
                    e.Graphics.DrawLine(item);
                }
            }


            #endregion
        }


        #endregion

        #region visability helping events 
        private void button1_Click(object sender, EventArgs e)
        {
            zoomIn();

        }

        private void button2_Click(object sender, EventArgs e)
        {
            zoomOut();
        }
        private void pictureBox1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
            {
                zoomIn();
                // setZoomInOut( 1);

            }
            else
            {
                zoomOut();
            }

            pictureBox1.Refresh();
        }
      

        private void zoomBtn_Click(object sender, EventArgs e)
        {
            activeZoom = true;
        }

        private void pictureBox1_Click(object sender, MouseEventArgs e)
        {
            

            if (e.Button == MouseButtons.Middle)
            {
                if (!active_Pan)
                {
                    startPanLocation = e.Location;
                }
                active_Pan = true;
                panPoint = currentMouseLocation;


            }
            if (e.Button == MouseButtons.Left)
            {
                if (activeZoom)
                {
                    switch (zoomClick)
                    {
                        case false:
                            firstZoomCorner = e.Location;
                            firstZoomCornerMM = pointToCartesian(firstZoomCorner);
                            zoomClick = true;
                            break;

                        case true:
                            setZoomwin(firstZoomCorner, e.Location);
                            activeZoom = false;
                            zoomClick = false;
                            break;
                    }

                }
                if (!selectionWindowActive)
                {
                    startPanLocation = e.Location;
                    selectionWindowActive = true;
                }


            }


            pictureBox1.Refresh();
        }

        private void updateSelectedElementVisability(IDrawAble nearestIdrawableFrame)
        {
           
            nearestIdrawableFrame.Selected = !nearestIdrawableFrame.Selected;
            nearestIdrawableFrame.pen = revercePenSelection(nearestIdrawableFrame);
        }

        private void selectElelemntsInWindow(Point pt1, Point pt2)
        {
            List<DrawAble> selectedFrames = getFramesInBox(pt1, pt2);
            foreach (var frame in selectedFrames)
            {
                if (!frame.Selected)
                {
                    updateSelectedElementVisability(frame);
                }
                
            }
        }

        private List<DrawAble> getFramesInBox(Point pt1, Point pt2)
        {
            List<DrawAble> inside = new List<DrawAble>();
            foreach (DrawAble line in visableFrames)
            {
                if (islineinterSectingTheBox(line, pt1, pt2) || islineInsideTheBox(line, pt1 , pt2))
                {
                    inside.Add(line);
                }
            }
            return inside;
        }

        private bool islineInsideTheBox(IDrawAble line, Point pt1, Point pt2)
        {
            List<DrawAble> box = getRecTangleLines(pointToCartesian(pt1), pointToCartesian(pt2));

            MyPoint botLeft = getBotLeft(box);
            MyPoint topRight = getTopRight(box);
            if (line.SourceIdentifier =="20")
            {
                int x = 0;
            }
            
            if (line.Start.X < botLeft.X || line.Start.X > topRight.X ||
                line.Start.Y < botLeft.Y || line.Start.Y > topRight.Y)

            {
                return false;
            }
            else if (line.End.X < botLeft.X || line.End.X > topRight.X ||
                line.End.Y < botLeft.Y || line.End.Y > topRight.Y)
            {
                return false;
            }
            
            return true;
        }

        private MyPoint getBotLeft(List<DrawAble> box)
        {
            List<MyPoint> listOfPoints = new List<MyPoint>();
            foreach (var Line in box)
            {
                listOfPoints.Add(Line.Start); 
            }
           return listOfPoints.OrderBy(p => p.Y).ThenBy(p => p.X).First();
        }
        private MyPoint getTopRight(List<DrawAble> box)
        {
            List<MyPoint> listOfPoints = new List<MyPoint>();
            foreach (var Line in box)
            {
                listOfPoints.Add(Line.Start);
            }
            return listOfPoints.OrderByDescending(p => p.Y).ThenByDescending(p => p.X).First();
        }

        private bool islineinterSectingTheBox(IDrawAble line, Point pt1, Point pt2)
        {
            List<DrawAble> rec = getRecTangleLines(pointToCartesian(pt1), pointToCartesian(pt2));

            foreach (var recLine in rec)
            {
                if (IsLineIntersecting(recLine.Start, recLine.End, line.Start, line.End))
                {
                    return true;
                }
            }
                

            return false;
        }
        bool IsLineIntersecting(MyPoint p1, MyPoint p2, MyPoint p3, MyPoint p4)
        {
            double denominator = ((p4.Y - p3.Y) * (p2.X - p1.X)) - ((p4.X - p3.X) * (p2.Y - p1.Y));
            double numerator1 = ((p4.X - p3.X) * (p1.Y - p3.Y)) - ((p4.Y - p3.Y) * (p1.X - p3.X));
            double numerator2 = ((p2.X - p1.X) * (p1.Y - p3.Y)) - ((p2.Y - p1.Y) * (p1.X - p3.X));

            if (denominator == 0)
            {
                return numerator1 == 0 && numerator2 == 0;
            }

            double r = numerator1 / denominator;
            double s = numerator2 / denominator;

            return (r >= 0 && r <= 1) && (s >= 0 && s <= 1);
        }

        private double DistanceBetween2PointsInPixle(Point pt1 , Point pt2)
        {
            double dx = pt2.X - pt1.X;
            double dy = pt2.Y - pt1.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private Pen revercePenSelection(IDrawAble frame)
        {
            if (frame.Selected)
            {
                switch (frame.Source)
                {
                    case IDrawAble.FrameSource.AllRevit:
                        return VisabilityController.SelectedAllRevitPen;
                    case IDrawAble.FrameSource.AllEtabs:
                        return VisabilityController.SelectedAllEtabsPen;

                    case IDrawAble.FrameSource.Paired:
                        if (frame.MyDesignState != IDrawAble.designState.ignore)
                        {
                            return VisabilityController.SelectedUnSafePen;
                        }
                        else
                        {
                            return VisabilityController.SelectedPairedFramesPen;
                        }
                    case IDrawAble.FrameSource.UnpairedEtabs:
                        return VisabilityController.SelectedUnMatchedEtabsPen;

                    case IDrawAble.FrameSource.UnPairedRevit:
                        return VisabilityController.SelectedUnMatchedRevitPen;
                    default:
                        return VisabilityController.warnningPen;


                }
            }
            else
            {
                switch (frame.Source)
                {
                    case IDrawAble.FrameSource.AllRevit:
                        return VisabilityController.allRevitPen;
                    case IDrawAble.FrameSource.AllEtabs:
                        return VisabilityController.allEtabsPen;

                    case IDrawAble.FrameSource.Paired:
                        if (frame.MyDesignState != IDrawAble.designState.ignore)
                        {
                            return VisabilityController.unSafePen;
                        }
                        else
                        {
                            return VisabilityController.PairedFramesPen;
                        }
                    case IDrawAble.FrameSource.UnpairedEtabs:
                        return VisabilityController.unMatchedEtabsPen;

                    case IDrawAble.FrameSource.UnPairedRevit:
                        return VisabilityController.unMatchedRevitPen;
                    default:
                        return VisabilityController.warnningPen;


                }
            }
           
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            currentMouseLocation = e.Location;

            if (active_Pan)
            {
                float dx = (currentMouseLocation.X - panPoint.X);
                float dy = (currentMouseLocation.Y - panPoint.Y);
                Xscroll -= pixleToMm(dx);
                Yscroll -= pixleToMm(dy);

                panPoint = currentMouseLocation;

            }
            pictureBox1.Refresh();



            //toolStripStatusLabel1.Text = $"x Scroll = {Xscroll}";
            //toolStripStatusLabel2.Text = $"x Scroll = {Yscroll}";
            //toolStripStatusLabel3.Text = $"cartezianToPoint  x = {cartesianToPoint(pointToCartesian(currentMouseLocation))}";
            //toolStripStatusLabel4.Text = $"mouse = {currentMouseLocation}";
            //toolStripStatusLabel5.Text = $"cartesian = {pointToCartesian(currentMouseLocation)}";
            //toolStripStatusLabel6.Text = $"pixle to mm x= {pixleToMm(currentMouseLocation.X)} y= {pixleToMm(currentMouseLocation.Y)}";
            //toolStripStatusLabel7.Text = $"mm to pixle x= { mmToPixle(pixleToMm(currentMouseLocation.X))} y= {mmToPixle(pixleToMm(currentMouseLocation.Y))}";
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {

            if (active_Pan)
            {
                active_Pan = false;

            }

           else if (selectionWindowActive && !activeZoom)
            {
                double MouseMovementDelta = DistanceBetween2PointsInPixle(currentMouseLocation, startPanLocation);
                if (MouseMovementDelta > 30)
                {
                    selectElelemntsInWindow(currentMouseLocation, startPanLocation);

                }
                else
                {
                    IDrawAble nearestIdrawableFrame = FindNearestIdrawableFrameToMouseClick(e.Location);
                    if (nearestIdrawableFrame != null)
                    {
                        updateSelectedElementVisability(nearestIdrawableFrame);

                    }

                }
                selectionWindowActive = false;

            }

            pictureBox1.Refresh();

        }

           

           

        

        private void GUI_SizeChanged(object sender, EventArgs e)
        {
            calculateDrawingSizeinmm();
        }

        private void Fit(object sender, EventArgs e)
        {
            fitPic();

        }
        #endregion

        #region UI Buttons events

        private void pictureBox1_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                IDrawAble nearestIdrawableFrame = FindNearestIdrawableFrameToMouseClick(e.Location);

                if (nearestIdrawableFrame != null)
                {
                    displaySelectedFrameData(nearestIdrawableFrame);
                }
            }
            if (e.Button == MouseButtons.Left)
            {
                IDrawAble nearestIdrawableFrame = FindNearestIdrawableFrameToMouseClick(e.Location);

                if (nearestIdrawableFrame != null)
                {
                    if (nearestIdrawableFrame.Selected)
                    {
                        clearToolTipsData();
                    }
                    else
                    {
                        UpdateLabelsData(nearestIdrawableFrame);
                    }
                  
                }
            }


        }

        private void clearToolTipsData()
        {
            toolStripStatusLabel1.Text = "";
            toolStripStatusLabel1.ForeColor = Color.Black;
            toolStripStatusLabel2.Text = "";
            toolStripStatusLabel2.ForeColor = Color.Black;
            toolStripStatusLabel3.Text = "";
            toolStripStatusLabel3.ForeColor = Color.Black;
            toolStripStatusLabel4.Text = "";
            toolStripStatusLabel4.ForeColor = Color.Black;
            toolStripStatusLabel5.Text = "";
            toolStripStatusLabel5.ForeColor = Color.Black;
            toolStripStatusLabel6.Text = "";
            toolStripStatusLabel6.ForeColor = Color.Black;
            toolStripStatusLabel7.Text = "";
            toolStripStatusLabel7.ForeColor = Color.Black;
            toolStripStatusLabel8.Text = "";
            toolStripStatusLabel8.ForeColor = Color.Black;
            toolStripStatusLabel9.Text = "";
            toolStripStatusLabel9.ForeColor = Color.Black;
            toolStripStatusLabel10.Text = "";
            toolStripStatusLabel10.ForeColor = Color.Black;
        }

        private void UpdateLabelsData(IDrawAble selectedFrame)
        {
            switch (selectedFrame.Source)
            {
                case IDrawAble.FrameSource.AllRevit:

                    break;
                case IDrawAble.FrameSource.AllEtabs:

                    break;
                case IDrawAble.FrameSource.Paired:
                    PairedFrame selectedPairedFrame = dt.PairedFrames.Where(x => x.revitFrame.revitID.ToString() == selectedFrame.SourceIdentifier).FirstOrDefault();
                    UpdateToolTiPData(selectedPairedFrame);
                    break;
                case IDrawAble.FrameSource.UnpairedEtabs:

                    break;
                case IDrawAble.FrameSource.UnPairedRevit:

                    break;

                default:
                    MessageBox.Show("Can not find Frame");
                    break;
            }
        }

        private void UpdateToolTiPData(PairedFrame selectedFrame)
        {
            var pref = PrefrencesData.getPrefrencesData();
            toolStripStatusLabel1.Text = selectedFrame.MyDesignState.ToString();
            toolStripStatusLabel1.ForeColor = Color.Black;
            if (selectedFrame.MyDesignState.ToString()!="ignore")
            {
                toolStripStatusLabel1.ForeColor = Color.Red;
            }

            toolStripStatusLabel2.Text = $"Top Left:{selectedFrame.T1_Ratio}";
            toolStripStatusLabel2.ForeColor = Color.Black;
            if (selectedFrame.T1_Ratio > pref.t1Factor)
            {
                toolStripStatusLabel2.ForeColor = Color.Red;
            }


            toolStripStatusLabel3.Text = $"Top Mid:{selectedFrame.T2_Ratio}";
             toolStripStatusLabel3.ForeColor = Color.Black;
            if (selectedFrame.T2_Ratio > pref.t2Factor)
            {
                toolStripStatusLabel3.ForeColor = Color.Red;
            }


            toolStripStatusLabel4.Text = $"Top Right:{selectedFrame.T3_Ratio}";
            toolStripStatusLabel4.ForeColor = Color.Black;
            if (selectedFrame.T3_Ratio > pref.t3Factor)
            {
                toolStripStatusLabel4.ForeColor = Color.Red;
            }


            toolStripStatusLabel5.Text = $"Bot. Left:{selectedFrame.B1_Ratio}";
            toolStripStatusLabel5.ForeColor = Color.Black;
            if (selectedFrame.B1_Ratio > pref.b1Factor)
            {
                toolStripStatusLabel5.ForeColor = Color.Red;
            }


            toolStripStatusLabel6.Text = $"Bot. Mid:{selectedFrame.B2_Ratio}";
            toolStripStatusLabel6.ForeColor = Color.Black;
            if (selectedFrame.B2_Ratio > pref.b2Factor)
            {
                toolStripStatusLabel6.ForeColor = Color.Red;
            }

            toolStripStatusLabel7.Text = $"Bot. Right:{selectedFrame.B3_Ratio}";
            toolStripStatusLabel7.ForeColor = Color.Black;
            if (selectedFrame.B3_Ratio > pref.b3Factor)
                {
                    toolStripStatusLabel7.ForeColor = Color.Red;
                }


            toolStripStatusLabel8.Text = $"Links Left:{selectedFrame.L_leftRatio}";
            toolStripStatusLabel8.ForeColor = Color.Black;
            if (selectedFrame.L_leftRatio > pref.LinksRatio)
            {
                toolStripStatusLabel8.ForeColor = Color.Red;
            }


            toolStripStatusLabel9.Text = $"Links Mid:{selectedFrame.L_midRatio}";
            toolStripStatusLabel9.ForeColor = Color.Black;
            if (selectedFrame.L_midRatio > pref.LinksRatio)
            {
                toolStripStatusLabel9.ForeColor = Color.Red;
            }


            toolStripStatusLabel10.Text = $"Links Right:{selectedFrame.L_rightRatio}";
            toolStripStatusLabel10.ForeColor = Color.Black;
            if (selectedFrame.L_rightRatio > pref.LinksRatio)
            {
                toolStripStatusLabel10.ForeColor = Color.Red;
            }

        }

        private void displaySelectedFrameData(IDrawAble selectedFrame)
        {
            var oldPen = visableFrames.Find(x => x == selectedFrame).pen;
            visableFrames.Find(x => x == selectedFrame).pen = VisabilityController.heighLightedPen;
            pictureBox1.Refresh();
            switch (selectedFrame.Source)
            {
                case IDrawAble.FrameSource.AllRevit:
                    FrameData_r selectedRevitFrame = dt.Frames_r.Where(x => x.revitID.ToString() == selectedFrame.SourceIdentifier).FirstOrDefault();
                    showRevitFrame(selectedRevitFrame);
                    break;
                case IDrawAble.FrameSource.AllEtabs:
                    FrameData_e selectedEtabsFrame = dt.Frames_e.Where(x => x.etabsUname.ToString() == selectedFrame.SourceIdentifier).FirstOrDefault();
                    showEtabsFrame(selectedEtabsFrame);
                    break;
                case IDrawAble.FrameSource.Paired:
                    PairedFrame selectedPairedFrame = dt.PairedFrames.Where(x => x.revitFrame.revitID.ToString() == selectedFrame.SourceIdentifier).FirstOrDefault();
                    showPairedFrame(selectedPairedFrame);
                    break;
                case IDrawAble.FrameSource.UnpairedEtabs:
                    FrameData_e selectedEtabsUnmatchedFrame = dt.UnmatchedEtabs.Where(x => x.etabsUname.ToString() == selectedFrame.SourceIdentifier).FirstOrDefault();
                    showEtabsFrame(selectedEtabsUnmatchedFrame);
                    break;
                case IDrawAble.FrameSource.UnPairedRevit:
                    FrameData_r revitFrame = dt.UnmatchedRevit.Where(x => x.revitID.ToString() == selectedFrame.SourceIdentifier).Cast<FrameData_r>().FirstOrDefault();
                    showRevitFrame(revitFrame);
                    break;

                default:
                    MessageBox.Show("Can not find Frame");
                    break;
            }

            visableFrames.Find(x => x == selectedFrame).pen = oldPen;
            pictureBox1.Refresh();

        }

        private void showPairedFrame(PairedFrame selectedPairedFrame)
        {
            PairedFrameForm pairedForm = new PairedFrameForm(selectedPairedFrame);
            pairedForm.ShowDialog();
        }

        private void showEtabsFrame(FrameData_e selectedEtabsFrame)
        {
            EtabsFrameForm form = new EtabsFrameForm(selectedEtabsFrame);
            form.ShowDialog();
        }

        private void showRevitFrame(FrameData_r revitFrame)
        {
            RevitFrameForm revitForm = new RevitFrameForm(revitFrame);
            revitForm.ShowDialog();
        }

        private IDrawAble FindNearestIdrawableFrameToMouseClick(Point pt1)
        {

            if (visableFrames.Count > 0)
            {
                double maxSelectionDistance = 250;
                MyPoint mouselocationInCartezian = pointToCartesian(pt1);
                double nearestDistance = visableFrames[0].distanceTo(mouselocationInCartezian);
                double currentDistance = -1;
                IDrawAble nearestFrame = new DrawAble(MyPoint.Zero, MyPoint.Zero);
                foreach (IDrawAble frame in visableFrames)
                {
                    currentDistance = frame.distanceTo(mouselocationInCartezian);

                    if (nearestDistance >= currentDistance && currentDistance < maxSelectionDistance)
                    {
                        nearestDistance = currentDistance;
                        nearestFrame = frame;

                    }
                }
                if (nearestFrame.comparasonState == ComparasonState.Uncategorized && currentDistance != -1)
                {
                    return null;
                }
                return nearestFrame;
            }

            return null;




        }


        private void pictureBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)27)
            {
                // Escape key pressed
                deselectAllframes();
            }
        }

        private void deselectAllframes()
        {
            foreach (var item in visableFrames)
            {
                if (item.Selected)
                {
                    item.Selected = false;
                    item.pen = revercePenSelection(item);

                }
            }
            pictureBox1.Refresh();
        }

        private void GUI_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)27)
            {
                // Escape key pressed
                deselectAllframes();
            }
        }

        private void GUI_PreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // Escape key pressed
                deselectAllframes();
            }
        }

        private void GUI_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // Escape key pressed
                deselectAllframes();
            }
        }

        private void butClearSelection_Click(object sender, EventArgs e)
        {
            deselectAllframes();
        }

        private void ButHideSelection_Click(object sender, EventArgs e)
        {
            HideSelected();
        }

        private void HideSelected()
        {
            var selectedMembers = visableFrames.Where(x => x.Selected == true);
            if (selectedMembers != null)
            {
                foreach (var item in selectedMembers)
                {
                    item.Visable = false;
                }

            }
            visableFrames.RemoveAll(x => x.Selected == true);
            pictureBox1.Refresh();
        }

        private void ButUnHideall_Click(object sender, EventArgs e)
        {
            updateAllDrawableData();
        }

        private void button2_Click_1(object sender, EventArgs e)
        {
            Selection selection = uiDoc.Selection;
            ICollection<ElementId> selectedElemnts = getSelectedElemntsID();
            selection.SetElementIds(selectedElemnts);
        }

        private ICollection<ElementId> getSelectedElemntsID()
        {
            // Element ids are Int64 from Revit 2024 on, and the Int32 constructor that used to
            // take them was removed in 2026, so they are parsed and passed as long throughout.
            long id = -1;
            System.Collections.ObjectModel.Collection<ElementId> ids = new System.Collections.ObjectModel.Collection<ElementId>();
            var revitElements = visableFrames.FindAll(x => ((x.Selected == true) &&
            ((x.Source == IDrawAble.FrameSource.AllRevit) || (x.Source == IDrawAble.FrameSource.Paired) || (x.Source == IDrawAble.FrameSource.UnPairedRevit))));
            foreach (var item in revitElements)
            {
                id = -1;
                if (long.TryParse(item.SourceIdentifier, out id))
                {
                    ids.Add(new ElementId(id));

                }

            }
            return ids;
        }

        private void comboStories_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSeletedStory();
            updateAllDrawableData();
            ScaleToFit();
        }

        private void UpdateSeletedStory()
        {
            try
            {
                selectedStory = ((comboStories.SelectedItem) as Story_E).Name;
            }
            catch (Exception)
            {

                selectedStory = dt.Stories[0].Name;
            }

        }

        private void butConfig_Click(object sender, EventArgs e)
        {
            PrefrencesForm prefForm = new PrefrencesForm();

            prefForm.ShowDialog();
        }
        private void ColorPicker(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            changeColor(btn);
        }

        private void changeColor(Button btn)
        {
            ColorDialog MyDialog = new ColorDialog();
            if (MyDialog.ShowDialog() == DialogResult.OK)
            {
                btn.ForeColor = MyDialog.Color;
                btn.BackColor = MyDialog.Color;
                updateVisabilitySettings();
            }

        }

        private void updateVisabilitySettings()
        {
            VisabilityController.allEtabsPen = new Pen(butAllEtabsColor.BackColor, VisabilityController.LineWeight);
            VisabilityController.allRevitPen = new Pen(butAllRevitColor.BackColor, VisabilityController.LineWeight);
            VisabilityController.PairedFramesPen = new Pen(butSafeColor.BackColor, VisabilityController.LineWeight);
            VisabilityController.unSafePen = new Pen(butUnsafeColor.BackColor, VisabilityController.LineWeight);
            VisabilityController.unMatchedEtabsPen = new Pen(butUnmatchedEtabsColor.BackColor, VisabilityController.LineWeight);
            VisabilityController.unMatchedRevitPen = new Pen(butUnmatchedRevitColor.BackColor, VisabilityController.LineWeight);

            VisabilityController.allEtabsVisable = checkAllEtabs.Checked;
            VisabilityController.allRevitVisable = checkAllRevit.Checked;
            VisabilityController.PairedframesVisable = checkPaired.Checked;
            VisabilityController.unMatchedEtabsVisable = checkUnmatcehdEtabs.Checked;
            VisabilityController.unMatchedRevitVisable = checkUnmatcehdRevit.Checked;

            updateAllDrawableData();
        }

        private void checkAllEtabs_CheckedChanged(object sender, EventArgs e)
        {
            updateVisabilitySettings();
        }

        private void checkUnmatcehdEtabs_CheckedChanged(object sender, EventArgs e)
        {
            updateVisabilitySettings();
        }

        private void checkUnmatcehdRevit_CheckedChanged(object sender, EventArgs e)
        {
            updateVisabilitySettings();
        }
        private void InetializeMydata()
        {

            tempPen = new Pen(Color.Gray);
            tempPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            updateSelectedPensData();
            
            setToolTip();
        }

        private void updateSelectedPensData()
        {
            VisabilityController.SelectedAllEtabsPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedAllRevitPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedPairedFramesPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedUnMatchedEtabsPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedUnMatchedRevitPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedUnSafePen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
            VisabilityController.SelectedWarnningPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;

        }

        private void setToolTip()
        {
            ToolTip TranslationTip = new ToolTip();
            string toolTipCaption = "The translation form Revit Model To Etabs Model";
            foreach (Control control in groupEtabsLocation.Controls)
            {
                TranslationTip.SetToolTip(control, toolTipCaption);
            }

        }

        private void butImportEtabs_Click(object sender, EventArgs e)
        {
            bool dataFromfile = radioFromFile.Checked;
            switch (dataFromfile)
            {
                case true:
                    dt.getEtabsDataFromFile();
                    break;
                case false:
                    et = new EtabsEngine();
                    dt.getEtabsDataFromEtabs(ref et);
                    break;
            }
            if (dt.Frames_e.Count > 0)
            {
                VisabilityController.allEtabsVisable= true;
                UpdateStoriesList();
                updateAllDrawableData();
               
                
                updateUI();
               
            }

        }

        private void UpdateStoriesList()
        {
            comboStories.Items.Clear();
            foreach (var story in dt.Stories)
            {
                comboStories.Items.Add(story);
            }
            if (comboStories.Items.Count >1)
            {
                comboStories.SelectedIndex = 1;
                selectedStory = ((comboStories.SelectedItem) as Story_E).Name;
            }
            else
            {
                try
                {
                    comboStories.SelectedIndex = 0;

                }
                catch (Exception)
                {
                    MessageBox.Show("No Stories Found");
                   
                }
             
            }
        }

        private void updateAllDrawableData()
        {
            visableFrames = new List<IDrawAble>();
            visableColumns = new List<Column_e>();
            visableWalls = new List<Wall_E>();


            if (VisabilityController.allEtabsVisable)
            {

                foreach (FrameData_e item in dt.Frames_e)
                {
                    
                    if (item.Visable&& item.Story == selectedStory )
                    {

                        item.pen = VisabilityController.allEtabsPen;
                        visableFrames.Add(item.asDrawable(MessageType.EtabsUnquiName,IDrawAble.FrameSource.AllEtabs,item.etabsUname));
                       // visableData.Add(item.asDrawable());
                    }
                    
                   
                }
            }

            if (VisabilityController.allRevitVisable)
            {
                foreach (var item in dt.Frames_r)
                {
                    if (item.Visable && item.Story == selectedStory)
                    {
                        item.pen = VisabilityController.allRevitPen;
                        visableFrames.Add(item.asDrawable(MessageType.RevtID, IDrawAble.FrameSource.AllRevit,item.revitID.ToString()));
                       // visableData.Add(item.asDrawable());
                    }
                  
                }
            }

            if (VisabilityController.PairedframesVisable)
            {
                foreach (PairedFrame item in dt.PairedFrames)
                {
                    if (item.Visable && item.Story == selectedStory)
                    {
                        Pen mypen;
                        switch (item.MyDesignState)
                        {
                            case DrawAble.designState.Check_Concrete:
                                mypen = VisabilityController.unSafePen;
                                break;
                            case DrawAble.designState.warning:
                                mypen = VisabilityController.warnningPen;
                                break;
                            case DrawAble.designState.ignore:
                                mypen = VisabilityController.PairedFramesPen;
                                break;
                            case DrawAble.designState.isUnsafe:
                                mypen = VisabilityController.unSafePen;
                                break;
                            default:
                                mypen = new Pen(Color.Black);
                                break;
                        }
                        item.pen = mypen;
                       visableFrames.Add(item.asDrawable(MessageType.PMMRatioMax, IDrawAble.FrameSource.Paired,item.RevitID.ToString()));
                       // visableData.Add(item.asDrawable());
                    }
                    
                }
            }
            if (VisabilityController.unMatchedEtabsVisable)
            {
                foreach (FrameData_e item in dt.UnmatchedEtabs)
                {
                    if (item.Visable && item.Story == selectedStory)
                    {
                        item.pen = VisabilityController.unMatchedEtabsPen;
                       visableFrames.Add(item.asDrawable(MessageType.State, IDrawAble.FrameSource.UnpairedEtabs,item.etabsUname));
                       // visableData.Add(item.asDrawable());
                    }
                }
            }
            if (VisabilityController.unMatchedRevitVisable)
            {
                foreach (FrameData_r item in dt.UnmatchedRevit)
                {
                    if (item.Visable && item.Story == selectedStory)
                    {
                        item.pen = VisabilityController.unMatchedRevitPen;
                        visableFrames.Add(item.asDrawable(MessageType.State,IDrawAble.FrameSource.UnPairedRevit,item.revitID.ToString()));
                       // visableData.Add(item.asDrawable());
                    }
                }
            }
            if (VisabilityController.showColumns)
            {
                
                foreach (Column_e item in dt.Columns)
                {
                    //if (item.Story == selectedStory)
                    //{
                    //    int x = 0;
                    //}
                    if (item.Visable && item.Story == selectedStory)
                    {
                        item.pen = VisabilityController.unMatchedRevitPen;
                        
                        visableColumns.Add(item);
                        // visableData.Add(item.asDrawable());
                    }
                }
            }
            if (VisabilityController.showWalls)
            {
                foreach (Wall_E item in dt.Walls)
                {
                    if (item.Visable && item.Story == selectedStory)
                    {
                        if (item.DrawingPoints?.Where(x=> x.X == 0).Count() >0)
                        {
                            int x = 0;
                        }
                      //  item.generateWallDrawingPoints();
                        visableWalls.Add(item);
                        // visableData.Add(item.asDrawable());
                    }
                }
            }


            pictureBox1.Refresh();
        }

        private void updateUI()
        {
            comboStories.Enabled = true;
            groupEtabsLocation.Enabled = true;
            enableCompareBut();
            ScaleToFit();


        }

        private void enableCompareBut()
        {
            if (dt.Frames_e.Count > 0 && dt.Frames_r.Count > 0)
            {
                butCompare.Enabled = true;
            }
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            MyTransformation trans = new MyTransformation();
            trans.Translation.X = Form1.ConvertToDouble(Xtext.Text);
            trans.Translation.Y = Form1.ConvertToDouble(Ytext.Text);
            trans.Translation.Z = Form1.ConvertToDouble(Ztext.Text);

            DataContainer.ModelTransformation = trans;
           
        }

        private void PickPt_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
            XYZ translationPointXYZ = uiDoc.Selection.PickPoint("Select The Point where The Etabs Origin Exists");
            MyTransformation trans = new MyTransformation();
            trans.Translation = Form1.getMyPtFromXYZ(translationPointXYZ).getInvertValues();
            PickLabelX.Text = $"X:";
            Xtext.Text = $" {Math.Round(trans.Translation.X, 2)}";
            PickLabelY.Text = $"Y:";
            Ytext.Text = $" {Math.Round(trans.Translation.Y, 2)}";

            PickLabelZ.Text = $"Z:";
            Ztext.Text = $" {Math.Round(trans.Translation.Z, 2)}";
            WindowState = FormWindowState.Normal;
        }
        private void angleText_Leave(object sender, EventArgs e)
        {
            if (angleText.Text == "")
            {
                angleText.Text = "Angle:";
                angleText.ForeColor = Color.Gray;
            }

            else
            {

                angleText.ForeColor = Color.Black;
            }
        }


        private void butRevitImport_Click(object sender, EventArgs e)
        {
            VisabilityController.allRevitVisable = true;
            importRevit();
            
            updateAllDrawableData();
            ScaleToFit();

        }
        private void butCompare_Click(object sender, EventArgs e)
        {
            VisabilityController.allRevitVisable = false;
            VisabilityController.allEtabsVisable = false;
            compare();
            updateAllDrawableData();

        }

        

        #endregion

        #region RevitFunctions
        private void importRevit()
        {
            try
            {
               
                getAllRevitData();
               
                
                enableCompareBut();
            }
            catch (Exception ex)
            {

                MessageBox.Show($"can not import\n {ex.Message}");
            }

        }

        private void getAllRevitData()
        {
            RevitEngine revitEngine = new RevitEngine(commandData, DataContainer.ModelTransformation);
            dt.Frames_r = revitEngine.getFilterdFrames();
            assigenRevitFramesToStories();

        }

        private void assigenRevitFramesToStories()
        {
            foreach (var item in dt.Frames_r)
            {
               
                item.Story = getNearestStory(item.Start.Z).Name;
            }
        }

        private Story_E getNearestStory(double z)
        {
            if (dt.Stories.Count<1)
            {
                return null;
            }
          
            Story_E nearestStory = dt.Stories[0];
            double distance = nearestStory.DistanceTo(z);
            foreach (var item in dt.Stories)
            {
                if (item.DistanceTo(z) < distance)
                {
                    nearestStory = item;
                    distance = nearestStory.DistanceTo(z);
                }
            }
            return nearestStory;
        }
        

       
        #endregion

        #region compare Function
        private void compare()
        {
            com = CompareEngine.GetCompareEngine();
            com.startComparison(dt.Frames_r, dt.Frames_e, ref et);
            getDataFromCompareEngine();
        }

        private void getDataFromCompareEngine()
        {
            dt.UnmatchedEtabs = com.RemainingEtabsframes;
            dt.UnmatchedRevit = com.UnmatchedRevit;
            dt.PairedFrames = com.PairedFrames;
        }



        #endregion

        private void pictureBox1_MouseHover(object sender, EventArgs e)
        {
        }
    }
}

