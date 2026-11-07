using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// A structural column read from Revit, expressed in the shared (ETABS) coordinate
    /// system so it can be compared straight against a <see cref="Column_e"/>.
    /// </summary>
    [DataContract]
    public class Column_r : IDrawAble, IColumn
    {
        private const float feetToMM = 304.8f;

        [DataMember]
        private long revitID;
        [DataMember]
        private string familyName = "";
        [DataMember]
        private string typeName = "";
        [DataMember]
        private SectionShape shape = SectionShape.Unknown;
        [DataMember]
        private double width;
        [DataMember]
        private double depth;
        [DataMember]
        private double diameter;
        [DataMember]
        private PointF[] planOutline = new PointF[0];
        [DataMember]
        private string typeMark = "";

        /// <summary>Untransformed Revit location, kept so the element can be traced back.</summary>
        public MyPoint revitPoint;

        public Column_r(long revitId, MyPoint planPointInFeet, double rotationRadians)
            : base(ComparasonState.RevitUnmatched, planPointInFeet, planPointInFeet)
        {
            revitID = revitId;
            revitPoint = planPointInFeet;

            // Bring the insertion point and the section orientation into the ETABS coordinate
            // system, so a rotated Revit model is compared in ETABS terms.
            MyTransformation transformation = DataContainer.ModelTransformation;
            MyPoint pointInMM = transformation.ApplyToPointInMM(planPointInFeet * feetToMM);
            Start = pointInMM;
            End = pointInMM;

            Rotaion = transformation.ApplyToAngleInDegrees(rotationRadians * 180d / Math.PI);

            AddMessage(new MyMessage(MessagePoint, revitID.ToString(), MessageType.RevtID));
        }

        /// <summary>
        /// Moves the column by a transform, for when the alignment changes after it was read.
        /// The point is already in the shared frame, so what is handed in is the difference
        /// between the two alignments - see <see cref="MyTransformation.Difference"/>.
        ///
        /// The section turns with the model: a difference that flips or rotates the plan
        /// leaves a rectangular column facing the wrong way otherwise.
        /// </summary>
        public void MoveBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            MyPoint moved = move.ApplyToPointInMM(Start);
            Start = moved;
            End = new MyPoint(moved.X, moved.Y, moved.Z);
            Rotaion = move.ApplyToAngleInDegrees(Rotaion);

            planOutline = ColumnGeometry.PlanOutline(this);
        }

        public long RevitID { get => revitID; set => revitID = value; }
        public string FamilyName { get => familyName; set => familyName = value; }
        public string TypeName { get => typeName; set => typeName = value; }

        public PointF[] PlanOutline { get => planOutline; }

        /// <summary>
        /// The mark the column's type carries, read from the first of
        /// <see cref="PrefrencesData.ColumnTypeMarkParameterNames"/> that the column answers
        /// to. This is the name checked against the ETABS frame group assignments.
        /// </summary>
        public string TypeMark { get => typeMark; set => typeMark = value ?? ""; }

        /// <summary>
        /// Where each dimension came from - "Width: Type 'b'; Depth: Type 'h'", "Diameter:
        /// measured from the solid" - for the element card. Empty for a column read before
        /// this was kept.
        /// </summary>
        [DataMember]
        public string SizeSource { get; set; } = "";

        /// <summary>Top of the column in Revit internal units, before the model translation.</summary>
        public double TopElevationInFeet { get; set; }

        /// <summary>
        /// Names the column after the story it rises to, which is how ETABS names its own
        /// columns, so the two can be compared story by story.
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

        /// <summary>
        /// Foot of the column in Revit internal units, before the model translation. NaN for a
        /// column read without one, which is then never split across stories.
        /// </summary>
        public double BottomElevationInFeet { get; set; } = double.NaN;

        /// <summary>
        /// How far above the foot of a column a story has to be before the column is taken to
        /// run through it. Keeps a base story a few hundred mm above the Revit ground level
        /// from turning every ground floor column into two.
        /// </summary>
        public const double RunsThroughMarginMM = 500;

        /// <summary>
        /// The column as ETABS would have it: one piece per story it rises through.
        ///
        /// ETABS splits a column at every story, while a Revit column commonly runs from the
        /// ground to two floors up in one piece. Named only after the story it rises to, that
        /// column pairs with the ETABS column under its top and leaves the one below with
        /// nothing to pair with. Each story it passes on the way up gets a copy named after
        /// that story - same Revit id, same place and section - so each ETABS piece finds it.
        ///
        /// A column standing within one story comes back as itself.
        /// </summary>
        public List<Column_r> SplitAtStories(List<Story_E> stories)
        {
            List<Column_r> pieces = new List<Column_r> { this };
            if (stories == null || stories.Count == 0 || double.IsNaN(BottomElevationInFeet)
                || string.IsNullOrEmpty(Story))
            {
                return pieces;
            }

            Story_E top = stories.FirstOrDefault(story => story.Name == Story);
            if (top == null)
            {
                return pieces;
            }

            double bottomInMM = BottomElevationInFeet * feetToMM + DataContainer.ModelTransformation.Translation.Z;
            foreach (Story_E story in stories)
            {
                if (story.Elevation > bottomInMM + RunsThroughMarginMM && story.Elevation < top.Elevation
                    && story.Name != Story)
                {
                    Column_r piece = (Column_r)MemberwiseClone();
                    piece.TakeOwnMessages();
                    piece.Story = story.Name;
                    pieces.Add(piece);
                }
            }
            return pieces;
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
                Story = nearest.Name;
            }
        }

        /// <summary>
        /// Stores the type dimensions. Values arrive in Revit internal units (feet) and are
        /// kept in mm. <paramref name="revitWidth"/> and <paramref name="revitDepth"/> are the
        /// "Width" / "Depth" type parameters of the rectangle family; <paramref name="revitDiameter"/>
        /// is the "Diameter" type parameter of the round family.
        /// </summary>
        public void SetDimensions(SectionShape sectionShape, double revitWidth, double revitDepth, double revitDiameter)
        {
            shape = sectionShape;
            width = Math.Round(revitWidth * feetToMM);
            depth = Math.Round(revitDepth * feetToMM);
            diameter = Math.Round(revitDiameter * feetToMM);
            planOutline = ColumnGeometry.PlanOutline(this);
        }

        public bool HasValidDimensions
        {
            get
            {
                if (shape == SectionShape.Round)
                {
                    return diameter > 0;
                }
                return width > 0 && depth > 0;
            }
        }

        #region IColumn

        /// <summary>
        /// Revit "Width" mapped onto the local 2 axis so it lines up with the ETABS T2
        /// dimension. See <see cref="PrefrencesData.SwapRevitColumnWidthDepth"/>.
        /// </summary>
        public double Width
        {
            get => PrefrencesData.getPrefrencesData().SwapRevitColumnWidthDepth ? depth : width;
        }

        /// <summary>Revit "Depth" mapped onto the local 3 axis (ETABS T3).</summary>
        public double Depth
        {
            get => PrefrencesData.getPrefrencesData().SwapRevitColumnWidthDepth ? width : depth;
        }

        /// <summary>The raw "Width" type parameter, as authored in Revit.</summary>
        public double RevitWidthParameter { get => width; }

        /// <summary>The raw "Depth" type parameter, as authored in Revit.</summary>
        public double RevitDepthParameter { get => depth; }

        public double Diameter { get => diameter; }

        public SectionShape Shape { get => shape; }

        public string SectionName { get => typeName; }

        public double AngleDegrees { get => Rotaion; }

        public MyPoint PlanPoint { get => Start; }

        public string Identifier { get => revitID.ToString(); }

        #endregion
    }
}
