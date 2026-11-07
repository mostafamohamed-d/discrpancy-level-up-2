using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One ETABS wall area, as the shell was drawn, with the centre line it stands on in plan.
    ///
    /// ETABS has no wall element: a wall is an area object whose design orientation is Wall,
    /// which in plan collapses onto a line. That line and the thickness of the section property
    /// assigned to it are what the wall check compares, so both are worked out here once, when
    /// the area is read.
    /// </summary>
    [DataContract]
    public class Wall_E : IWall
    {
        [DataMember]
        string uniqueName;
        [DataMember]
        Joint[] wallJoints;
        [DataMember]
        string story;

        [DataMember]
        double thickness;

        [DataMember]
        string propertyName = "";

        [DataMember]
        PointF[] drawingPoints;

        [DataMember]
        MyPoint planStart;
        [DataMember]
        MyPoint planEnd;

        [DataMember]
        private bool visable = true;

        public bool Visable
        {
            get { return visable; }
            set { visable = value; }
        }

        public Wall_E(string _uniqueName, Joint[] _wallPoints, string _wallStory, double thickness,
                      string _propertyName = "")
        {
            uniqueName = _uniqueName;
            wallJoints = _wallPoints;
            story = _wallStory;
            this.thickness = thickness;
            propertyName = _propertyName ?? "";
            visable = true;

            generateWallDrawingPoints();
        }

        /// <summary>
        /// Works out the centre line and the plan outline from the joints. Called when the area
        /// is read, and again for a wall that arrives without them - an ETABS model saved to
        /// file by a build that did not know about the centre line still has its joints, and
        /// those are what everything else is worked out from.
        /// </summary>
        public void generateWallDrawingPoints()
        {
            MyPoint[] span = WallGeometry.PlanSpan(PlanPoints());

            if (span == null)
            {
                // A wall whose footprint is a single point has no line to compare and nothing
                // to draw. It stays in the list, marked invisible, so it can still be counted
                // and reported rather than disappearing without a word.
                planStart = null;
                planEnd = null;
                drawingPoints = null;
                visable = false;
                return;
            }

            planStart = span[0];
            planEnd = span[1];
            drawingPoints = WallGeometry.PlanOutline(planStart, planEnd, thickness);
            visable = drawingPoints.Length > 0;
        }

        /// <summary>
        /// A wall read back from a saved ETABS model. Files written before the wall check
        /// existed carry the joints but neither the centre line nor the property name, so the
        /// line is worked out again from the joints rather than left at nothing - which would
        /// put every wall in the file at the origin.
        /// </summary>
        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (propertyName == null)
            {
                propertyName = "";
            }

            if (planStart == null || planEnd == null)
            {
                generateWallDrawingPoints();
            }
        }

        /// <summary>Every joint of the area, in plan.</summary>
        private List<MyPoint> PlanPoints()
        {
            if (wallJoints == null)
            {
                return new List<MyPoint>();
            }

            return wallJoints.Where(joint => joint != null && joint.Point != null)
                             .Select(joint => joint.Point)
                             .ToList();
        }

        /// <summary>True once the centre line has been worked out, which is all a check needs.</summary>
        public bool HasCentreLine
        {
            get { return planStart != null && planEnd != null; }
        }

        /// <summary>Length of the wall in plan, in mm.</summary>
        public double Length
        {
            get { return WallGeometry.PlanLength(planStart, planEnd); }
        }

        /// <summary>Highest joint of the area, in mm. This is the level ETABS names the wall after.</summary>
        public double TopElevation
        {
            get { return Elevation(true); }
        }

        /// <summary>Lowest joint of the area, in mm.</summary>
        public double BaseElevation
        {
            get { return Elevation(false); }
        }

        private double Elevation(bool highest)
        {
            List<MyPoint> points = PlanPoints();
            if (points.Count == 0)
            {
                return 0;
            }

            double found = points[0].Z;
            foreach (MyPoint point in points)
            {
                if (highest ? point.Z > found : point.Z < found)
                {
                    found = point.Z;
                }
            }
            return found;
        }

        public string UniqueName { get => uniqueName; }
        public Joint[] WallPoints { get => wallJoints; }
        public double Thickness { get => thickness; }

        /// <summary>The ETABS wall section property assigned to the area.</summary>
        public string PropertyName { get => propertyName; }

        public PointF[] DrawingPoints { get => drawingPoints; set => drawingPoints = value; }

        #region IWall

        public MyPoint PlanStart { get => planStart; }

        public MyPoint PlanEnd { get => planEnd; }

        public string Story { get => story; }

        public string SectionName { get => propertyName; }

        public string Identifier { get => uniqueName; }

        #endregion

        public override string ToString()
        {
            return $"{uniqueName} ({story}, {Math.Round(thickness)}mm)";
        }
    }
}
