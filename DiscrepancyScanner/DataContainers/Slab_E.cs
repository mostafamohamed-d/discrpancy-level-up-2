using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// What an ETABS area object is, as far as the slab check cares. A drop panel is an area
    /// object like any other, which is exactly how ETABS models one: a second, thicker area
    /// drawn over the slab and hanging below the same reference plane.
    /// </summary>
    public enum SlabKind
    {
        Unknown,
        Slab,
        /// <summary>A drop panel: thicker, and drawn over the slab it belongs to.</summary>
        Drop,
        Ribbed,
        Waffle,
        Mat,
        Footing,
        Deck,
        /// <summary>An area object flagged as an opening, so it has no thickness at all.</summary>
        Opening,
        /// <summary>
        /// A stiff area: how SAFE marks the column or wall standing in the slab, drawn over it
        /// and very thick. Last, so the kinds already written to saved files keep their numbers.
        /// </summary>
        Stiff,
        /// <summary>
        /// A null area - property None in ETABS, no property at all in SAFE - carrying load: no
        /// slab, only a load laid over the slab under it, which is how a load that acts over
        /// part of a floor is drawn. It has no thickness and no plan of its own; the load check
        /// adds its load to the slab it stands on and the thickness check never sees it. After
        /// Stiff for the same reason Stiff is last.
        /// </summary>
        LoadOnly
    }

    /// <summary>
    /// One ETABS floor area object, with the plan outline it was drawn to and the thickness
    /// of the property assigned to it. These are the base mesh the slab check works from: the
    /// outlines are only ever cut where the two models stop agreeing, never redrawn.
    ///
    /// The outline is stored as points rather than as a built region so the ETABS model still
    /// round trips through the saved json file; the region is built the first time it is
    /// asked for.
    /// </summary>
    [DataContract]
    public class Slab_E
    {
        [DataMember]
        private string uniqueName = "";
        [DataMember]
        private string story = "";
        [DataMember]
        private string propertyName = "";
        [DataMember]
        private double thickness;
        [DataMember]
        private double overallDepth;
        [DataMember]
        private bool isOpening;
        [DataMember]
        private SlabKind kind = SlabKind.Unknown;
        [DataMember]
        private MyPoint[] boundaryPoints = new MyPoint[0];
        [DataMember]
        private double topElevation;
        [DataMember]
        private AreaLoad_E[] loads = new AreaLoad_E[0];

        [IgnoreDataMember]
        private PlanPolygon plan;

        public Slab_E()
        {
        }

        public Slab_E(string etabsUniqueName, IList<Joint> joints, string storyName,
                      string areaPropertyName, SlabKind slabKind, double slabThickness,
                      double slabOverallDepth, bool opening)
        {
            uniqueName = etabsUniqueName ?? "";
            story = storyName ?? "";
            propertyName = areaPropertyName ?? "";
            kind = slabKind;
            thickness = slabThickness;
            overallDepth = slabOverallDepth;
            isOpening = opening;

            boundaryPoints = (joints ?? new List<Joint>())
                .Where(joint => joint != null && joint.Point != null)
                .Select(joint => joint.Point)
                .ToArray();

            topElevation = boundaryPoints.Length == 0 ? 0 : boundaryPoints.Max(point => point.Z);
        }

        public string UniqueName { get { return uniqueName; } }

        public string Story { get { return story; } set { story = value ?? ""; } }

        /// <summary>Name of the ETABS area property, shown so a wrong assignment is readable.</summary>
        public string PropertyName { get { return propertyName; } }

        public SlabKind Kind { get { return kind; } }

        /// <summary>True when this area is a hole in the slab rather than a slab.</summary>
        public bool IsOpening { get { return isOpening; } }

        /// <summary>
        /// True for a null area carrying load rather than a slab - see <see cref="SlabKind.LoadOnly"/>.
        /// </summary>
        public bool IsLoadOnly { get { return kind == SlabKind.LoadOnly; } }

        /// <summary>
        /// Thickness of the assigned property in mm. For a ribbed or waffle property this is
        /// the topping, which is usually what a Revit floor models, with the ribs drawn as
        /// beams; <see cref="OverallDepth"/> carries the full section depth for those.
        /// </summary>
        public double Thickness { get { return thickness; } }

        /// <summary>
        /// Full section depth in mm for a ribbed or waffle property, and the same as
        /// <see cref="Thickness"/> for everything else.
        /// </summary>
        public double OverallDepth { get { return overallDepth > 0 ? overallDepth : thickness; } }

        /// <summary>
        /// The thickness this area is checked against, which depends on how ribbed and waffle
        /// slabs are modelled in Revit. See
        /// <see cref="PrefrencesData.CompareRibbedSlabsByOverallDepth"/>.
        /// </summary>
        public double ComparedThickness
        {
            get
            {
                bool ribbed = kind == SlabKind.Ribbed || kind == SlabKind.Waffle;
                return ribbed && PrefrencesData.getPrefrencesData().CompareRibbedSlabsByOverallDepth
                    ? OverallDepth
                    : Thickness;
            }
        }

        /// <summary>Highest boundary point, which is the plane the area was drawn at, in mm.</summary>
        public double TopElevation { get { return topElevation; } }

        public MyPoint[] BoundaryPoints { get { return boundaryPoints; } }

        /// <summary>
        /// The uniform loads assigned to this area, in kN/m2 and positive downwards. Empty for
        /// an opening, and empty on a model read back from a json file written before the load
        /// check existed - which reads as an area with nothing on it, and is reported as one.
        /// </summary>
        public IList<AreaLoad_E> Loads { get { return loads; } }

        public void SetLoads(IEnumerable<AreaLoad_E> areaLoads)
        {
            loads = (areaLoads ?? new AreaLoad_E[0]).Where(load => load != null).ToArray();
        }

        /// <summary>The plan outline, built once and kept.</summary>
        public PlanPolygon Plan
        {
            get { return plan ?? (plan = PlanPolygon.FromPoints(boundaryPoints)); }
        }

        /// <summary>
        /// True when this area sits flat enough to be a floor. A sloped ramp read as a floor
        /// would have its plan outline compared against a thickness measured vertically,
        /// which would be wrong in a way that is hard to see, so it is left out instead.
        /// </summary>
        public bool IsFlat(double tolerance)
        {
            if (boundaryPoints.Length < 3)
            {
                return false;
            }
            return boundaryPoints.Max(point => point.Z) - boundaryPoints.Min(point => point.Z) <= tolerance;
        }

        /// <summary>How this area is named in the results and on the plan.</summary>
        public string Description
        {
            get
            {
                if (isOpening)
                {
                    return $"ETABS opening {uniqueName}";
                }
                if (IsLoadOnly)
                {
                    return $"ETABS load area {uniqueName}";
                }
                return $"ETABS {kind.ToString().ToLower()} {uniqueName} ({propertyName})";
            }
        }

        public override string ToString()
        {
            return Description;
        }
    }
}
