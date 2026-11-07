using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace DiscrepancyScanner
{
    [DataContract]
    public class FrameData : IDrawAble

    {
        [DataMember]
        public string Material = "";
        [DataMember]
        const float feetToMM = 304.8f;
        [DataMember]
        internal double _width = 0;
        [DataMember]
        internal double _depth = 0;
        [DataMember]
        private MyPoint _start;
        [DataMember]
        private MyPoint _END;

        [DataMember]
        private MyPoint _direction;
        [DataMember]
        public double _approxLength;
        [DataMember]
        public string mark;
        [DataMember]
        public long revitID;
        [DataMember]
        public string etabsUname;
        [DataMember]
        private double asBotLeft = 0;
        [DataMember]
        private double asBotMid = 0;
        [DataMember]
        private double asBotRight = 0;
        [DataMember]
        private double asTopLeft = 0;
        [DataMember]
        private double asTopMid = 0;
        [DataMember]
        private double asTopRight = 0;
        [DataMember]
        private double asLinksLeft_r = 0;
        [DataMember]
        private double asLinksMid_r = 0;
        [DataMember]
        private double asLinksRight_r = 0;

        [DataMember]
        private double asLinksLeft_e;

        public double AsLinksLeft_e
        {
            get { return asLinksLeft_e; }
            set { asLinksLeft_e = value; }
        }


        [DataMember]
        private double asLinksMid_e;

        public double AsLinksMid_e
        {
            get { return asLinksMid_e; }
            set { asLinksMid_e = value; }
        }

        [DataMember]
        private double asLinksRight_e;

        public double AsLinksRight_e
        {
            get { return asLinksRight_e; }
            set { asLinksRight_e = value; }
        }




        public FrameData(MyPoint start_r, MyPoint end_r, double length_r, MyPoint direction, ComparasonState state ) : base ( state )
        {
        
            Start_r = start_r;
            ApproxLength_r = length_r;
            Direction = direction;
            END_r = end_r;

        }
        public FrameData() : base(ComparasonState.EtabsUnmatched)
        {

            AddMessage(new MyMessage(this.MessagePoint, "Un Matched Etabs", MessageType.State));

        }


        public long RevitID { get => revitID; set => revitID = value; }
        public string EtabsUname { get => etabsUname; set => etabsUname = value; }
        public MyPoint Start_r
        {   get => _start;
            set
            { 
                _start = value * feetToMM;
                base.Start = _start;
            }
        }
        public MyPoint END_r { get => _END;
            set 
            { _END = value * feetToMM;
                base.End = _END;
            }
        
        }
       
        public MyPoint Start_e { get => _start;
            set
            { _start = value; base.Start = _start; } }
        public MyPoint END_e { get => _END;
            set
            { _END = value; base.End = _END; }
        }
       
        public double ApproxLength_r { get => _approxLength; set => _approxLength = value*feetToMM; }

        /// <summary>
        /// Replaces the stored ends with values that are already in mm, bypassing the unit
        /// conversion the Start_r / END_r setters apply.
        /// </summary>
        internal void setLocationInMM(MyPoint start, MyPoint end)
        {
            _start = start;
            _END = end;
            base.Start = _start;
            base.End = _END;
        }
       
        public MyPoint Direction { get => _direction; set => _direction = value; }
        public double AsBotLeft { get => asBotLeft; set => asBotLeft = value; }
        public double AsBotMid { get => asBotMid; set => asBotMid = value; }
        public double AsBotRight { get => asBotRight; set => asBotRight = value; }
        public double AsTopLeft { get => asTopLeft; set => asTopLeft = value; }
        public double AsTopMid { get => asTopMid; set => asTopMid = value; }
        public double AsTopRight { get => asTopRight; set => asTopRight = value; }
        public double AsLinksLeft_r { get => asLinksLeft_r; set => asLinksLeft_r = value; }
        public double AsLinksMid_r { get => asLinksMid_r; set => asLinksMid_r = value; }
        public double AsLinksRight_r { get => asLinksRight_r; set => asLinksRight_r = value; }
        public double width_r { get => _width; set => _width = Math.Round(value * feetToMM); }
        public double Depth_r { get => _depth; set => _depth = Math.Round(value * feetToMM); }
        

   

        public void translate(MyPoint translation)
        {
            Start_r.Translate(translation);
            END_r.Translate(translation);
            calculateMid();
            calculateDirectionRevit();


        }
        public void rotate (double angle)
        {
            Start_r.Rotate(angle);
            END_r.Rotate(angle);
            
            calculateDirectionRevit();

        }
        public void calculateDirectionRevit()
        {
            
                Direction = (Start_r - END_r).Normalize();
            
        }

        public void MirrorXY(double m_x, double m_y)
        {
            Start_r.MirrorXY(m_x,m_y);
            END_r.MirrorXY(m_x,m_y);
           
            calculateDirectionRevit();
        }
        public void MirrorX(double m_x)
        {
            Start_r.MirrorX(m_x);
            END_r.MirrorX(m_x);
           
            calculateDirectionRevit();

        }
        public void MirrorY(double m_y)
        {

            Start_r.MirrorY(m_y);
            END_r.MirrorY(m_y);
           
            calculateDirectionRevit();
        }
    }
}
