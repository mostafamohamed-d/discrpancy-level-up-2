using System;

using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public class MyPoint
    {
        const float feetToMM = 304.8f;
        [DataMember]
        public double X;
        [DataMember]
        public double Y;
        [DataMember]
        public double Z;

        

       


        public static MyPoint operator *(MyPoint pt1, double d)
        {
            MyPoint pt = new MyPoint(pt1.X * d, pt1.Y * d, pt1.Z * d);
            return pt;
        }
        public static MyPoint operator -(MyPoint pt1, MyPoint pt2)
        {
            MyPoint pt = new MyPoint(pt1.X - pt2.X, pt1.Y - pt2.Y, pt1.Z - pt2.Z);
            return pt;
        }

        internal  MyPoint fromFeetTomm()
        {
            return new MyPoint(X * feetToMM, Y * feetToMM, Z * feetToMM);
        }

        public static double getLengthFromUnit(Form1.eLength uni, double v)
        {
            switch (uni)
            {
                case Form1.eLength.NotApplicable:
                    return v;
                case Form1.eLength.inch:
                    return v * 25.4d;
                case Form1.eLength.ft:
                    return v * 304.8d;
                case Form1.eLength.micron:
                    return v * 0.001;
                case Form1.eLength.mm:
                    return v;
                case Form1.eLength.cm:
                    return v * 10d;
                case Form1.eLength.m:
                    return v * 1000d;
                default:
                    return v;

            }

        }
        public MyPoint(double x1, double y1, double z1)
        {
            X = x1;
            Y = y1;
            Z = z1;
        }
        public MyPoint(double x1, double y1)
        {
            X = x1;
            Y = y1;
            Z = 0;
        }
        public MyPoint()
        {
            X = 0;
            Y = 0;
            Z = 0;
        }

        public MyPoint(float x1, float y1) : this((double)x1, (double) y1)
        {
        }

       
        public double DistanceTo(MyPoint pt)
        {
            double distance =Math.Sqrt( Math.Pow((X - pt.X), 2) + Math.Pow((Y - pt.Y), 2) + Math.Pow((Z - pt.Z), 2));

            return distance;
        }
        public MyPoint Normalize()
        {
            double length = Math.Sqrt(Math.Pow(X, 2) + Math.Pow(Y, 2) + Math.Pow(Z, 2));
            return new MyPoint(X / length, Y / length, Z / length);
        }

        internal bool IsAlmostEqualTo(MyPoint d2, double v)
        {
            if ((Math.Abs(X)-Math.Abs( d2.X))>v)
            {
                return false;
            }
            if ((Math.Abs(Y) - Math.Abs(d2.Y)) > v)
            {
                return false;
            }
            if ((Math.Abs(Z )- Math.Abs(d2.Z)) > v)
            {
                return false;
            }


            return true;
        }
        public override string ToString()
        {
            return "("+(Math.Round (X,2))+", "+(Math.Round(Y,2)) + ", "+(Math.Round(Z,2)) + ")";
        }
        public void Translate(MyPoint translation)
        {
            X += translation.X;
            Y += translation.Y;
            Z += translation.Z;
        }
        public void Rotate(double angle)
        {
            double radians = (Math.PI / 180) * angle;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double x = X * cos - Y * sin;
            double y = X * sin + Y * cos;
            X = x;
            Y = y;
        }
        public void MirrorXY(double m_x, double m_y)
        {
            X = 2 * m_x - X;
            Y = 2 * m_y - Y;
        }
        public void MirrorX(double m_x)
        {
            X = 2 * m_x - X;
           
        }
        public void MirrorY( double m_y)
        {
            
            Y = 2 * m_y - Y;
        }
        public MyPoint getInvertValues()
        {
            return new MyPoint(-X, -Y, -Z);
        }
        public System.Drawing.PointF AsPointF
        {
            get
            {
               return new System.Drawing.PointF((float)X, (float)Y);
            }
        }

        public static MyPoint Zero { get { return new MyPoint(); }  }

    }
}

