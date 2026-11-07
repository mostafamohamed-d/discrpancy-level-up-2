using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A wall read from Revit, expressed in the shared (ETABS) coordinate system so it can be
    /// compared straight against a <see cref="Wall_E"/>.
    ///
    /// The location line is what Revit is asked for, not the faces: it is the line the wall was
    /// drawn on, which is what an ETABS wall shell stands on too. Where the two models put that
    /// line differently - a wall dimensioned to its face in one model and to its centre in the
    /// other - is exactly what the location check is there to report.
    /// </summary>
    [DataContract]
    public class Wall_R : IWall
    {
        private const float feetToMM = 304.8f;

        [DataMember]
        private long revitID;
        [DataMember]
        private string typeName = "";
        [DataMember]
        private string elementName = "";
        [DataMember]
        private double thickness;
        [DataMember]
        private MyPoint planStart;
        [DataMember]
        private MyPoint planEnd;
        [DataMember]
        private string story = "";
        [DataMember]
        private string typeMark = "";
        [DataMember]
        private PointF[] planOutline = new PointF[0];

        /// <summary>Untransformed Revit ends, kept so the element can be traced back.</summary>
        public MyPoint revitStart;
        public MyPoint revitEnd;

        public Wall_R(long revitId, MyPoint startInFeet, MyPoint endInFeet)
        {
            revitID = revitId;
            revitStart = startInFeet;
            revitEnd = endInFeet;

            // Both ends go through the model transformation, so a rotated or mirrored Revit
            // model is compared in ETABS terms exactly as the columns are.
            MyTransformation transformation = DataContainer.ModelTransformation;
            planStart = transformation.ApplyToPointInMM(startInFeet * feetToMM);
            planEnd = transformation.ApplyToPointInMM(endInFeet * feetToMM);
        }

        /// <summary>
        /// Moves the wall by a transform, for when the alignment changes after it was read.
        ///
        /// The ends are already in the shared frame, so what is handed in is the difference
        /// between the alignment they were placed under and the one now in force - see
        /// <see cref="MyTransformation.Difference"/>. Moving what is in hand is what saves
        /// reading the whole Revit model again for a move of a few metres.
        /// </summary>
        public void MoveBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            planStart = move.ApplyToPointInMM(planStart);
            planEnd = move.ApplyToPointInMM(planEnd);
            planOutline = WallGeometry.PlanOutline(planStart, planEnd, thickness);
        }

        /// <summary>
        /// Puts the wall on a different centre line in plan, in mm and in the shared frame.
        ///
        /// What the corner snapping moves - see <see cref="WallCorners"/> - where a wall drawn
        /// past a junction, or short of one, is read as running to the point the two centre
        /// lines cross, which is where ETABS meshes the joint. The line Revit was actually
        /// asked for is left as it came in, in <see cref="revitStart"/> and
        /// <see cref="revitEnd"/>, so the wall can still be traced back to what is drawn.
        /// </summary>
        public void SetPlanLine(MyPoint start, MyPoint end)
        {
            if (start == null || end == null)
            {
                return;
            }

            planStart = start;
            planEnd = end;
            planOutline = WallGeometry.PlanOutline(planStart, planEnd, thickness);
        }

        public long RevitID { get => revitID; set => revitID = value; }

        public string TypeName { get => typeName; set => typeName = value ?? ""; }

        /// <summary>
        /// The wall's own name, which can differ from its type name and is looked at by the
        /// list of walls to leave out as well - kept so a check can apply that list again.
        /// </summary>
        public string ElementName { get => elementName; set => elementName = value ?? ""; }

        /// <summary>
        /// The mark the wall's type carries. Nothing is checked against it yet; it is read and
        /// reported so a wall can be named the way the drawings name it, and it is what the
        /// reinforcement check will be keyed on when it arrives.
        /// </summary>
        public string TypeMark { get => typeMark; set => typeMark = value ?? ""; }

        /// <summary>Top of the wall in Revit internal units, before the model translation.</summary>
        public double TopElevationInFeet { get; set; }

        /// <summary>Length of the wall in plan, in mm.</summary>
        public double Length { get { return WallGeometry.PlanLength(planStart, planEnd); } }

        public PointF[] PlanOutline { get => planOutline; }

        /// <summary>
        /// Stores the thickness, which arrives in mm already because how it is arrived at -
        /// the structural layers of the type, or the whole width - is the reader's business
        /// rather than this container's.
        /// </summary>
        public void SetThickness(double thicknessInMM)
        {
            thickness = Math.Round(thicknessInMM);
            planOutline = WallGeometry.PlanOutline(planStart, planEnd, thickness);
        }

        public bool HasValidThickness { get { return thickness > 0; } }

        /// <summary>
        /// Names the wall after the story it rises to, which is how ETABS names its own walls -
        /// an area is assigned to the nearest story to its highest joint - so the two can be
        /// compared story by story.
        /// </summary>
        /// <summary>
        /// The elevation this element is named after, in mm and in the ETABS frame: the level
        /// it rises to, shifted by whatever the alignment does to Z.
        ///
        /// One expression, read by the two things that must agree about it - which story the
        /// element is assigned to, and whether a single level run keeps it at all. Worked out
        /// twice they could drift, and an element narrowed onto a level it was then assigned
        /// off would vanish from the check without a word.
        /// </summary>
        public double StoryElevationMM
        {
            get
            {
                return TopElevationInFeet * feetToMM + DataContainer.ModelTransformation.Translation.Z;
            }
        }

        public void AssignStory(List<Story_E> stories)
        {
            if (stories == null || stories.Count == 0)
            {
                return;
            }

            double topInMM = StoryElevationMM;
            Story_E nearest = Story_E.getNearestStory(topInMM, stories);
            if (nearest != null)
            {
                story = nearest.Name;
            }
        }

        #region IWall

        public MyPoint PlanStart { get => planStart; }

        public MyPoint PlanEnd { get => planEnd; }

        public double Thickness { get => thickness; }

        public string Story { get => story; set => story = value ?? ""; }

        public string SectionName { get => typeName; }

        public string Identifier { get => revitID.ToString(); }

        #endregion

        public override string ToString()
        {
            return $"{revitID} ({story}, {Math.Round(thickness)}mm)";
        }
    }
}
