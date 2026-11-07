
using System;
using System.Drawing;

namespace DiscrepancyScanner
{
    public static class GraphicsExtension
    {
        private static float heightInMm; private static float windthInmm;
        private static float XScroll;
        private static float YScroll;
        private static float ScaleFactor;
        private static int imageCenterX;
        private static int imageCeterY;
        private static Brush messageBrush;
        private static float FontSize = 50;
        private static FontFamily fntFmly = new FontFamily("Arial");
        private static Font fnt = new Font(fntFmly, VisabilityController.textHeight);

        public static void SetParameters(this Graphics g, float height, float width , float xScroll, float yScroll, float scaleFactor, int width1, int height1, Brush MessageBrush)
        {
            imageCenterX = width1;
            imageCeterY = height1;
            windthInmm = width;
            heightInMm = height;
            XScroll = xScroll;
            YScroll = yScroll;
            ScaleFactor = scaleFactor;
            messageBrush = MessageBrush;
           

        }
        public static void SetTransform(this Graphics g)
        {
            
                g.PageUnit = GraphicsUnit.Millimeter;

                g.TranslateTransform(0, heightInMm);

                g.ScaleTransform(ScaleFactor, -ScaleFactor);

                g.TranslateTransform(-XScroll / ScaleFactor, YScroll / ScaleFactor);
            

        }

        public static void DrawPoint(this Graphics g, Pen p, MyPoint dp)
        {
            g.SetTransform();
            PointF pt = dp.AsPointF;

            g.DrawEllipse(p, pt.X - 1f, pt.Y - 1f, 2f, 2f);

            g.ResetTransform();
        }
        public static void DrawLine(this Graphics g, Pen p, IDrawAble L)
        {
            g.SetTransform();
            g.DrawLine(p, L.Start.AsPointF, L.End.AsPointF);
            g.ResetTransform();
        }
        public static void DrawLine(this Graphics g, IDrawAble L)
        {
            g.DrawLine(L.pen, L);
           
        }
        public static void FillMyPolygon(this Graphics g,Brush columnsBrush , PointF[] RectanglePoints)
        {
            g.SetTransform();
            try
            {
                g.FillPolygon(columnsBrush, RectanglePoints);
            }
            catch (Exception)
            {

                
            }
            
            g.ResetTransform();
        }
        public static void DrawLine(this Graphics g, IDrawAble L, MyMessage message)
        {
            g.DrawLine(L.pen, L);
            g.DrawMessage(message);
        }

        public static void DrawMessage(this Graphics g, MyMessage message)
        {
            g.SetTransform();
           
            g.DrawString(message.Message,fnt,messageBrush,message.Location.AsPointF);
            g.ResetTransform();
        }

        public static void DrawRectangle(this Graphics g,Pen pn, MyPoint p1 , MyPoint p2)
        {
            g.SetTransform();
            
            Point zoomTopLeftcorner = GetTopLeftCorner(p1, p2);
            Size size = getSize(p1, p2);
            
            if (size.Width>10000)
            {

            }
            g.DrawRectangle(pn, zoomTopLeftcorner.X, zoomTopLeftcorner.Y, size.Width,size.Height);
           
            g.ResetTransform();

        }

        private static Size getSize(MyPoint p1, MyPoint p2)
        {

            int dx = (int)(p2.X - p1.X);
            int dy = (int)(p2.Y - p1.Y);
            return new Size(dx, dy);
        }

        private static Point GetTopLeftCorner(MyPoint p1, MyPoint p2)
        {
            int minX =(int) p1.X;
            int maxY = (int)p1.Y;
            if (p2.X<minX)
            {
                minX = (int)p2.X;
            }

            if (p2.Y> maxY)
            {
                maxY = (int)p2.Y;
            }
            return new Point(minX, maxY);
        }
    }
}
