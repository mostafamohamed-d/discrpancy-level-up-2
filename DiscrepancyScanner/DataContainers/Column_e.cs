using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    [DataContract]
    public class Column_e : IDrawAble, IColumn
    {
        [DataMember]
        private System.Drawing.PointF[] rectanglePoints;
        [DataMember]
        string etabsUniqueName = "";
        [DataMember]
        double width;
        [DataMember]
        double height;
        [DataMember]
        private SectionShape shape = SectionShape.Rectangular;
        [DataMember]
        private double diameter;
        [DataMember]
        private string sectionName = "";
        [DataMember]
        private List<string> groups = new List<string>();

        public Column_e(FrameData_e frame) : base(ComparasonState.Uncategorized, frame.Start, frame.End)
        {
            EtabsUniqueName= frame.EtabsUname;
            groups = new List<string>(frame.Groups);
            width = frame._width;
            height = frame._depth;
            shape = frame.Shape;
            diameter = frame.Diameter;
            sectionName = frame.etabsSecName;
            Story = frame.Story;
            Rotaion = frame.Rotaion;
            rectanglePoints = ColumnGeometry.PlanOutline(this);
        }

        public string EtabsUniqueName { get => etabsUniqueName; set => etabsUniqueName = value; }
        public PointF[] RectanglePoints { get => rectanglePoints; set => rectanglePoints = value; }

        /// <summary>
        /// ETABS frame groups this column is assigned to. Checked against the Revit column
        /// type mark; see <see cref="ColumnGroupCheck"/>.
        /// </summary>
        public List<string> Groups
        {
            get => groups ?? (groups = new List<string>());
            set => groups = value ?? new List<string>();
        }

        #region IColumn

        /// <summary>Section dimension along the local 2 axis (ETABS T2).</summary>
        public double Width { get => width; }

        /// <summary>Section dimension along the local 3 axis (ETABS T3).</summary>
        public double Depth { get => height; }

        public double Diameter { get => diameter; }

        public SectionShape Shape { get => shape; }

        public string SectionName { get => sectionName; }

        /// <summary>Rotation of the section local axes from global X, in degrees.</summary>
        public double AngleDegrees { get => Rotaion; }

        /// <summary>Plan insertion point of the column, in mm.</summary>
        public MyPoint PlanPoint { get => Start; }

        public string Identifier { get => etabsUniqueName; }

        #endregion
    }
}
