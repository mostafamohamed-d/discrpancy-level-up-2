using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
   public static class VisabilityController
    {
        
        public static bool allEtabsVisable = true;
        public static bool allRevitVisable = true;
        public static bool unMatchedEtabsVisable = true;
        public static bool unMatchedRevitVisable = false;
        public static bool PairedframesVisable = true;
        public static bool showColumns = true;
        public static bool showWalls = true;

        public static int LineWeight = 200;
        public static Pen allEtabsPen = new Pen(Color.Black, LineWeight);
        public static Pen allRevitPen = new Pen(Color.Blue, LineWeight);
        public static Pen PairedFramesPen = new Pen(Color.Gray, LineWeight);
        public static Pen unMatchedEtabsPen = new Pen(Color.Magenta, LineWeight);
        public static Pen unMatchedRevitPen = new Pen(Color.DarkBlue, LineWeight);
        public static Pen unSafePen = new Pen(Color.Red, LineWeight);
        public static Pen warnningPen = new Pen(Color.Orange, LineWeight);
        public static Pen heighLightedPen = new Pen(Color.Yellow, 2*LineWeight);

        public static Brush columnsBrush = Brushes.Black;
        internal static Brush wallBrush = Brushes.Black;
        
        public static Pen SelectedAllEtabsPen = new Pen(Color.Black, LineWeight);
        

        public static Pen SelectedAllRevitPen = new Pen(Color.Blue,2* LineWeight);
        public static Pen SelectedPairedFramesPen = new Pen(Color.Gray,2* LineWeight);
        public static Pen SelectedUnMatchedEtabsPen = new Pen(Color.Magenta,2* LineWeight);
        public static Pen SelectedUnMatchedRevitPen = new Pen(Color.DarkBlue,2* LineWeight);
        public static Pen SelectedUnSafePen = new Pen(Color.Red, 2*LineWeight);
        public static Pen SelectedWarnningPen = new Pen(Color.PaleVioletRed, LineWeight);
        
        public static double textOffset = 500;
        public static float textHeight = 1000;
       
    }
}
