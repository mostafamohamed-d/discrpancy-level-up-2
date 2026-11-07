using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace DiscrepancyScanner
{
    [DataContract]
    public class Joint
    {
        [DataMember]
        public string uniqueName;

        [DataMember]
        public List<string> connectedFrames;

        [DataMember]
        public MyPoint point;

        [DataMember]
        public Form1.eLength unit;

        public Joint(string un, MyPoint pt, Form1.eLength eLength)
        {
            this.uniqueName = un;
            unit = eLength;
            Point = pt ;
           
        }

        public MyPoint Point { get => point; set => point = getPointFromUnit(value); }


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
        internal MyPoint getPointFromUnit(MyPoint v)
        {
           return new MyPoint(getLengthFromUnit(unit, v.X), getLengthFromUnit(unit, v.Y), getLengthFromUnit(unit, v.Z));
        }
        public override string ToString()
        {
            return $"{uniqueName}";
        }

    }

}
