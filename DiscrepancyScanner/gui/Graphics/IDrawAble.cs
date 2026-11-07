using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;

using System.Linq;
using System.Runtime.Serialization;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    [DataContract]
    public abstract class IDrawAble
    {

        [DataMember]
        private string _story;

        private bool selected;
        [DataMember]
        private bool visable;
        private ComparasonState state;

        [DataMember]
        private MyPoint start;
        [DataMember]
        private MyPoint end;
        [DataMember]
        private MyPoint mid;
        [DataMember]
        private double rotaion;

        public double Rotaion
        {
            get { return rotaion; }
            set { rotaion = value; }
        }


        private designState myDesignState = designState.ignore;
        public designState MyDesignState { get => myDesignState; set => myDesignState = value; }

        [DataMember]
        List <MyMessage> messages = new List<MyMessage>();

        public enum FrameSource
        {
            AllRevit,
            AllEtabs,
            Paired,
            UnpairedEtabs,
            UnPairedRevit,
            unknown
        }

        private string sourceIdentifier = "";

        public string Story { get => _story; set => _story = value; }

        private FrameSource source = FrameSource.unknown;

           public MyPoint MessagePoint
            {
                get {
               
                    if (Horizontal)
                    {
                        double newY = this.Mid.Y + VisabilityController.textOffset;
                    
                        return new MyPoint(Mid.X, newY);
                    }
                    else
                    {

                        double newX = this.Mid.X - VisabilityController.textOffset;

                        return new MyPoint(newX, mid.Y);
                    }
                    
                    }
            }
        internal void calculateMid()
        {
            double x = (start.X + end.X) / 2d;
            double y = (start.Y + end.Y) / 2d;
            double z = (start.Z + end.Z) / 2d;

            Mid = new MyPoint(x, y, z);
        }
        public bool Horizontal
        {
            get
            {
                if (Math.Abs(Start.X - End.X) < 0.001)
                {
                    return true;
                }
                return false;
            }
        }
        public void AddMessage(MyMessage message)
        {
            // have only one unique message of each type

            if (messages.Count > 0)
            {
                bool doseNotHaveAnother = true;
                foreach (var item in messages)
                {
                    if (item.Type == message.Type)
                    {
                        doseNotHaveAnother = false;
                        messages.Remove(item);
                        messages.Add(message);
                        break;
                    }
                }
                if (doseNotHaveAnother)
                {
                    messages.Add(message);  
                }

            }
            else
            {
                messages.Add(message);
            }
          
        }
        public void AddMessage(MyPoint messagePoint, string message, MessageType messageType)
        {
           AddMessage( new MyMessage( messagePoint, message,  messageType));
        }

        public void DeleteAllMessages()
        {
            messages.Clear();
        }

        /// <summary>
        /// Gives a memberwise copy a message list of its own, so what the check writes on the
        /// copy is not written on the element it was copied from as well.
        /// </summary>
        protected void TakeOwnMessages()
        {
            messages = new List<MyMessage>(messages);
        }
        public enum designState
        {
            Check_Concrete,
            warning,
            ignore,
            isUnsafe
        }

        public System.Drawing.Pen pen;
        public MyPoint End
        {
            get { return end; }
            set { end = value; }
        }



        public MyPoint Start
        {
            get { return start; }
            set { start = value; }
        }


        public ComparasonState comparasonState
        {
            get { return state; }
            set { state = value; }
        }


        public bool Visable
        {
            get { return visable; }
            set { visable = value; }
        }


        public bool Selected
        {
            get { return selected; }
            set { selected = value; }
        }

        public MyPoint Mid { get
            {
                calculateMid();
                return mid;
            }
            set => mid = value; }
        public List<MyMessage> Messages { get => messages;  }
        public FrameSource Source { get => source;}
        public string SourceIdentifier { get => sourceIdentifier;  }

        public IDrawAble (ComparasonState state,MyPoint start, MyPoint end)
        {
            this.state = state;
            this.visable = true;
            this.selected = false;
            this.start = start;
            this.end = end;
            this.pen = new System.Drawing.Pen(System.Drawing.Color.Black);
            calculateMid();
        }
        public IDrawAble(ComparasonState state)
        {
            this.state = state;
            this.visable = true;
            this.selected = false;
            this.start =  new MyPoint();
            this.end = new MyPoint();
        }
         public DrawAble asDrawable(FrameSource srs, string srsID)
        {
          var ret = new DrawAble(this.start, this.end);
            ret.pen = this.pen;
            ret.Selected = this.selected;
            ret.visable = this.visable;
            ret.comparasonState = this.comparasonState;
            ret.myDesignState = this.myDesignState;
            ret.messages= this.messages;
            ret.source = srs;
            ret.sourceIdentifier = srsID;
            return ret;
        }
        public DrawAble asDrawable(MessageType messageType, FrameSource srs, string srsID)
        {
          var ret = asDrawable(srs, srsID);
            foreach (MyMessage message in ret.messages)
            {
                if (message.Type == messageType)
                {
                    ret.messages.Clear();
                    ret.messages.Add(message);
                    break;
                }

            }
          
          return ret;
        }

        public double distanceTo(MyPoint toBeCheckedPoint)
        {
            MyPoint nearestPointInLine = new MyPoint();
            double x1 = Start.X;
            double y1 = Start.Y;
            double x2 = End.X;
            double y2 = End.Y;

            double dx = x2 - x1;
            double dy = y2 - y1;


            double k = ((toBeCheckedPoint.X - x1) * dx
                       + (toBeCheckedPoint.Y - y1) * dy)
                       / (dx * dx + dy * dy);
            nearestPointInLine = new MyPoint(x1 + k * dx, y1 + k * dy);

            dx = toBeCheckedPoint.X - nearestPointInLine.X;
            dy = toBeCheckedPoint.Y - nearestPointInLine.Y;

            if (k<0)
            {
                nearestPointInLine = Start;
                dx = toBeCheckedPoint.X - x1;
                dy = toBeCheckedPoint.Y - y1;
            }
            else if (k>1)
            {
                nearestPointInLine = End;
                dx = toBeCheckedPoint.X - x2;
                dy = toBeCheckedPoint.Y - y2;
            }

            return nearestPointInLine.DistanceTo(toBeCheckedPoint);
        }
    }
}
