using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One Revit floor, read as the two things the slab check needs from it: the plan region
    /// its top face covers, openings and all, and how deep the concrete runs under that face.
    ///
    /// Both surfaces are used, and each answers a different question. The top face fixes where
    /// the floor is - it is the face that lines up with the plane an ETABS area is drawn at -
    /// and its inner loops are the openings. The bottom face fixes how thick it is. Reading
    /// the depth as top minus bottom rather than off the type is what makes a drop panel
    /// visible: however the drop was modelled, as a second floor underneath, as a thicker
    /// floor, or by editing the slab shape, it shows up as concrete reaching further down.
    ///
    /// Everything here is in mm, in the shared (ETABS) coordinate system, so a floor can be
    /// compared straight against a <see cref="Slab_E"/>.
    /// </summary>
    public class Slab_R
    {
        private readonly List<PlanPolygon> openings = new List<PlanPolygon>();
        private readonly List<PlanPolygon> parts = new List<PlanPolygon>();

        public Slab_R(long revitId, PlanPolygon plan, double topElevation, double bottomElevation)
        {
            RevitID = revitId;
            Plan = plan;
            TopElevation = topElevation;
            BottomElevation = bottomElevation;
            MinimumThickness = Math.Max(0, topElevation - bottomElevation);
            MaximumThickness = MinimumThickness;

            if (plan != null)
            {
                parts.Add(plan);
            }

            if (plan != null)
            {
                foreach (PlanRing hole in plan.Holes)
                {
                    PlanPolygon opening = PlanPolygon.Create(hole, null);
                    if (opening != null)
                    {
                        openings.Add(opening);
                    }
                }
            }
        }

        /// <summary>
        /// Moves the floor by a transform, for when the alignment changes after it was read.
        ///
        /// The plan is already in the shared frame, so what is handed in is the difference
        /// between the alignment it was read under and the one now in force - see
        /// <see cref="MyTransformation.Difference"/>. The elevations move with it: they were
        /// read off the faces after the alignment had been applied, so they carry whatever it
        /// does to Z. The thicknesses are differences between the two and do not move at all.
        /// </summary>
        public void MoveBy(MyTransformation move)
        {
            if (move == null)
            {
                return;
            }

            // The plan is one of the parts - the largest of them - and moving it twice would
            // leave two of it, so it is moved as a part and then picked back out.
            PlanPolygon movedPlan = null;
            List<PlanPolygon> movedParts = new List<PlanPolygon>();

            foreach (PlanPolygon part in parts)
            {
                PlanPolygon moved = Moved(part, move);
                if (moved == null)
                {
                    continue;
                }

                movedParts.Add(moved);
                if (ReferenceEquals(part, Plan))
                {
                    movedPlan = moved;
                }
            }

            parts.Clear();
            parts.AddRange(movedParts);
            Plan = movedPlan ?? Moved(Plan, move);

            // Rebuilt from the moved plan rather than moved themselves, which is how they were
            // arrived at in the first place and keeps the two from ever disagreeing.
            openings.Clear();
            if (Plan != null)
            {
                foreach (PlanRing hole in Plan.Holes)
                {
                    PlanPolygon opening = PlanPolygon.Create(hole, null);
                    if (opening != null)
                    {
                        openings.Add(opening);
                    }
                }
            }

            TopElevation += move.Translation.Z;
            BottomElevation += move.Translation.Z;
        }

        /// <summary>The same region, every point of it put through the move.</summary>
        private static PlanPolygon Moved(PlanPolygon polygon, MyTransformation move)
        {
            if (polygon == null)
            {
                return null;
            }

            PlanRing outer = Moved(polygon.Outer, move);
            if (outer == null)
            {
                return null;
            }

            List<PlanRing> holes = new List<PlanRing>();
            foreach (PlanRing hole in polygon.Holes)
            {
                PlanRing movedHole = Moved(hole, move);
                if (movedHole != null)
                {
                    holes.Add(movedHole);
                }
            }

            return PlanPolygon.Create(outer, holes);
        }

        private static PlanRing Moved(PlanRing ring, MyTransformation move)
        {
            return ring == null
                ? null
                : PlanRing.FromPoints(ring.Points.Select(point => move.ApplyToPointInMM(point)));
        }

        public long RevitID { get; private set; }

        public string TypeName { get; set; } = "";

        /// <summary>
        /// The floor's own name. The list of floors to leave out looks at it as well as the
        /// type name, so it is kept for a check to apply that list again.
        /// </summary>
        public string ElementName { get; set; } = "";

        public string FamilyName { get; set; } = "";

        /// <summary>Revit category the element came from, so foundation slabs read as such.</summary>
        public string CategoryName { get; set; } = "";

        public string Story { get; set; } = "";

        /// <summary>Read off the floor's solid - the ordinary way.</summary>
        public const string FromSolid = "Solid";
        /// <summary>Read off the floor's sketch, for a floor Revit gave no solid for.</summary>
        public const string FromSketch = "Sketch";

        /// <summary>Where the outline and elevations came from: <see cref="FromSolid"/> or <see cref="FromSketch"/>.</summary>
        public string GeometrySource { get; set; } = FromSolid;

        /// <summary>
        /// True for a floor read off its sketch when something cuts it that could not be taken
        /// out of the sketch - a void, or an opening crossing its edge. What the sketch says is
        /// slab may not be, so its zones are put up for review rather than called right or wrong.
        /// </summary>
        public bool OpeningsUncertain { get; set; }

        /// <summary>The plan region of the top face: the outline, with the openings as holes.</summary>
        public PlanPolygon Plan { get; private set; }

        /// <summary>
        /// Every separate region this floor covers on plan, the largest of which is
        /// <see cref="Plan"/>.
        ///
        /// One Revit floor is often more than one piece of slab: a shape edited floor arrives
        /// as a facet per slope, and a floor with an opening reaching its edge arrives as an
        /// outline slit open, which has to be cut into the rings it is really made of. All of
        /// them are the floor, and all of them are checked.
        /// </summary>
        public IList<PlanPolygon> Parts
        {
            get { return parts; }
            set
            {
                parts.Clear();
                if (value != null)
                {
                    foreach (PlanPolygon part in value)
                    {
                        if (part != null)
                        {
                            parts.Add(part);
                        }
                    }
                }
                if (parts.Count == 0 && Plan != null)
                {
                    parts.Add(Plan);
                }
            }
        }

        /// <summary>Each opening in this floor as a region of its own.</summary>
        public IList<PlanPolygon> Openings { get { return openings; } }

        /// <summary>Top of the concrete, in mm.</summary>
        public double TopElevation { get; private set; }

        /// <summary>Bottom of the concrete, in mm.</summary>
        public double BottomElevation { get; private set; }

        /// <summary>Thinnest the floor gets, in mm. The same as the thickness when it is flat.</summary>
        public double MinimumThickness { get; set; }

        /// <summary>Thickest the floor gets, in mm.</summary>
        public double MaximumThickness { get; set; }

        /// <summary>Top minus bottom, in mm - the depth of concrete under the top face.</summary>
        public double Thickness { get { return Math.Max(0, TopElevation - BottomElevation); } }

        /// <summary>
        /// The thickness the floor type is authored at, in mm - the whole of it, finishes
        /// included. Reported next to the measured one so a floor whose geometry disagrees
        /// with its own type is readable.
        /// </summary>
        public double NominalThickness { get; set; }

        /// <summary>
        /// The concrete the floor type declares, in mm: the sum of the layers it marks as
        /// structure, or the whole type thickness when it marks none.
        ///
        /// This is the number worth comparing against ETABS. It is read rather than measured,
        /// so a warped, shape edited or ramped floor gives it up as readily as a flat one, and
        /// a screed carried in the same floor is not counted as slab.
        /// </summary>
        public double StructuralThickness { get; set; }

        /// <summary>
        /// True when the floor type itself declares a layer of varying thickness. That is a
        /// floor which genuinely has no single thickness, as opposed to one whose faces merely
        /// sit at an angle.
        /// </summary>
        public bool HasVariableTypeThickness { get; set; }

        /// <summary>
        /// The thickness this floor is checked on, in mm, and whether it is a single number at
        /// all - which is the pair of questions the slab check actually asks.
        ///
        /// Reading it from the type answers both outright. Measuring answers neither for a
        /// floor whose faces are not parallel, which is why a measured sloped floor is put up
        /// for review while a read one is not: the type says 250 everywhere, and it is right.
        /// </summary>
        public double ComparedThickness
        {
            get
            {
                if (!PrefrencesData.getPrefrencesData().ReadSlabThicknessFromType)
                {
                    return Thickness;
                }
                return StructuralThickness > 0 ? StructuralThickness : Thickness;
            }
        }

        /// <summary>True when this floor's thickness cannot be given as one number.</summary>
        public bool ComparedThicknessVaries
        {
            get
            {
                if (!PrefrencesData.getPrefrencesData().ReadSlabThicknessFromType)
                {
                    return HasVariableThickness;
                }
                return StructuralThickness > 0 ? HasVariableTypeThickness : HasVariableThickness;
            }
        }

        /// <summary>
        /// True when the two faces are not parallel, which is a sloped floor, a warped one, or
        /// one whose shape has been edited. The thickness is then a range rather than a value
        /// and the zone is put up for review instead of being called right or wrong.
        /// </summary>
        public bool HasVariableThickness
        {
            get { return MaximumThickness - MinimumThickness > 1d; }
        }

        /// <summary>True when the element is a structural floor rather than an architectural one.</summary>
        public bool IsStructural { get; set; }

        public string Description
        {
            get { return $"Revit floor {RevitID} ({TypeName})"; }
        }

        public override string ToString()
        {
            return Description;
        }
    }

    /// <summary>
    /// A plan region and the total depth of concrete under it: what the Revit model actually
    /// says the slab is at that spot. One floor gives one of these, and floors stacked on top
    /// of each other - the usual way a drop panel arrives - are merged into one that reaches
    /// from the top of the highest to the bottom of the lowest.
    /// </summary>
    public sealed class SlabDepthRegion
    {
        private readonly List<long> revitIds = new List<long>();

        public SlabDepthRegion(PlanPolygon plan, double topElevation, double bottomElevation,
                               IEnumerable<long> sourceRevitIds, bool variableThickness,
                               double thickness)
        {
            Plan = plan;
            TopElevation = topElevation;
            BottomElevation = bottomElevation;
            HasVariableThickness = variableThickness;
            Thickness = thickness;
            if (sourceRevitIds != null)
            {
                revitIds.AddRange(sourceRevitIds);
            }
        }

        public PlanPolygon Plan { get; private set; }

        public double TopElevation { get; private set; }

        public double BottomElevation { get; private set; }

        /// <summary>
        /// Total depth of concrete here, in mm: every floor stacked at this spot added up.
        ///
        /// Carried rather than worked out from the top and bottom elevations, because where
        /// the thickness is read from the floor types the elevations no longer say what the
        /// concrete does - a floor can sit proud of its own level, and a shape edited one has
        /// a different depth at every point of it. The elevations are still kept, since
        /// finding what is stacked under what needs them.
        /// </summary>
        public double Thickness { get; private set; }

        /// <summary>True when any floor making up this depth is sloped or shape edited.</summary>
        public bool HasVariableThickness { get; private set; }

        /// <summary>Type of the floor at the story plane, for the results panel.</summary>
        public string TypeName { get; set; } = "";

        /// <summary>Every Revit floor that contributes to this depth, topmost first.</summary>
        public IList<long> RevitIds { get { return revitIds; } }

        /// <summary>The floor to select in Revit for this region, which is the topmost one.</summary>
        public long PrimaryRevitId { get { return revitIds.Count == 0 ? 0 : revitIds[0]; } }

        /// <summary>True when more than one floor is stacked here, which reads as a drop.</summary>
        public bool IsStacked { get { return revitIds.Count > 1; } }

        public override string ToString()
        {
            return $"{Math.Round(Thickness)} mm over {Math.Round(Plan?.Area / 1e6 ?? 0, 2)} m2";
        }
    }
}
