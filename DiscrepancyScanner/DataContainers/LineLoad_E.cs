using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One distributed load assigned to an ETABS frame, brought into kN/m and turned the right
    /// way up.
    ///
    /// A frame can carry any number of these at once and each covers a stretch of the frame
    /// rather than the whole of it, which is how one beam ends up holding what the drawing
    /// shows as three separate line loads. The stretch is kept as the relative distances ETABS
    /// gives - nought to one along the frame from its I end - because those are unitless and
    /// so cannot be got wrong by a model built in feet.
    ///
    /// The two values are the load at each end of the stretch. They are equal on a uniform
    /// load, which is what a line load off a drawing always is; a trapezoid is somebody having
    /// done something else, and it is kept rather than flattened so it can be reported.
    ///
    /// Written into the saved model file along with everything else, which is why it carries
    /// the serialisation attributes and a constructor taking nothing: a saved model is how most
    /// runs are made, and a load left out of the file is a check that cannot run on one.
    /// </summary>
    [DataContract]
    public sealed class LineLoad_E
    {
        [DataMember]
        private string pattern = "";
        [DataMember]
        private double atStart;
        [DataMember]
        private double atEnd;
        [DataMember]
        private double relativeFrom;
        [DataMember]
        private double relativeTo = 1;
        [DataMember]
        private int direction;
        [DataMember]
        private bool gravity;
        [DataMember]
        private bool moment;

        public LineLoad_E()
        {
        }

        public LineLoad_E(string loadPattern, double downwardAtStart, double downwardAtEnd,
                          double relativeFrom, double relativeTo,
                          int etabsDirection, bool isGravity, bool isMoment)
        {
            pattern = loadPattern ?? "";
            atStart = downwardAtStart;
            atEnd = downwardAtEnd;
            this.relativeFrom = Math.Max(0, Math.Min(1, Math.Min(relativeFrom, relativeTo)));
            this.relativeTo = Math.Max(0, Math.Min(1, Math.Max(relativeFrom, relativeTo)));
            direction = etabsDirection;
            gravity = isGravity;
            moment = isMoment;
        }

        /// <summary>The ETABS load pattern this was assigned in, as it is named in the model.</summary>
        public string Pattern { get { return pattern ?? ""; } }

        /// <summary>The load at the start of the stretch, in kN/m, positive when it presses down.</summary>
        public double ValueAtStart { get { return atStart; } }

        /// <summary>The load at the end of the stretch, in kN/m.</summary>
        public double ValueAtEnd { get { return atEnd; } }

        /// <summary>Where the stretch starts, as a fraction of the frame from its I end.</summary>
        public double RelativeFrom { get { return relativeFrom; } }

        /// <summary>Where it ends, as a fraction of the frame from its I end.</summary>
        public double RelativeTo { get { return relativeTo; } }

        /// <summary>The ETABS direction code it was assigned in, kept so it can be reported.</summary>
        public int Direction { get { return direction; } }

        /// <summary>False for a load that does not plainly act downwards, which cannot be compared.</summary>
        public bool IsGravity { get { return gravity; } }

        /// <summary>True for a distributed moment, which is not a line load and is only reported.</summary>
        public bool IsMoment { get { return moment; } }

        /// <summary>True when the two ends differ, which a load off a drawing never does.</summary>
        public bool IsTrapezoidal
        {
            get { return Math.Abs(atStart - atEnd) > 1e-9; }
        }

        /// <summary>The load partway along the stretch, for reading a trapezoid at a point.</summary>
        public double ValueAt(double relative)
        {
            double span = relativeTo - relativeFrom;
            if (span <= 0)
            {
                return atStart;
            }

            double k = Math.Max(0, Math.Min(1, (relative - relativeFrom) / span));
            return atStart + (atEnd - atStart) * k;
        }

        /// <summary>
        /// The ETABS direction codes this reads as "down" on a frame.
        ///
        /// Deliberately shorter than the list <see cref="AreaLoad_E.IsDownwardDirection"/>
        /// uses, and the difference is not an oversight. A shell lies flat, so its local 3 is
        /// up and can be counted; a frame's local axes turn with the member and with whatever
        /// rotation was typed onto it, so which of local 1, 2 and 3 points down is a property
        /// of that one frame rather than of the code. Guessing would put a load in with the
        /// wrong sign - which reads as a load twice the size in the wrong place - so a load
        /// assigned in a local direction is reported instead of counted.
        ///
        /// In practice a line load off a drawing is assigned in Gravity or in global Z, so
        /// what is turned away here is the exception, and it is turned away out loud.
        /// </summary>
        public static bool IsDownwardDirection(int etabsDirection, out double sign)
        {
            switch (etabsDirection)
            {
                case 10: // gravity
                case 11: // projected gravity
                    sign = 1d;
                    return true;
                case 6:  // global Z
                case 9:  // projected global Z
                    sign = -1d;
                    return true;
                default:
                    sign = 0d;
                    return false;
            }
        }

        public override string ToString()
        {
            string value = IsTrapezoidal
                ? $"{LoadSet.Text(atStart)} to {LoadSet.Text(atEnd)} kN/m"
                : $"{LoadSet.Text(atStart)} kN/m";

            string over = relativeFrom <= 0 && relativeTo >= 1
                ? ""
                : $" over {Math.Round(relativeFrom * 100)}% to {Math.Round(relativeTo * 100)}%";

            return $"{Pattern} {value}{over}" + (gravity ? "" : $" (direction {direction})");
        }
    }

    /// <summary>
    /// One ETABS frame as far as the line load check is concerned: a line in plan, on a story,
    /// with whatever distributed loads are on it.
    ///
    /// Read on its own rather than taken off the beams the beam check already imported, and
    /// that is a correctness point rather than a convenience. The beam import drops frames
    /// whose material is a dummy or "other" - see the no-design filter - and a null frame
    /// drawn along a slab edge to hang a cladding load on is exactly such a frame. Checking
    /// line loads against the imported beams would therefore report every edge load in the
    /// building as sitting on nothing.
    /// </summary>
    [DataContract]
    public sealed class LineLoadFrame_E : IPlanLine
    {
        [DataMember]
        private List<LineLoad_E> loads = new List<LineLoad_E>();
        [DataMember]
        private string uniqueName = "";
        [DataMember]
        private string label = "";
        [DataMember]
        private string story = "";
        [DataMember]
        private MyPoint planStart;
        [DataMember]
        private MyPoint planEnd;

        public LineLoadFrame_E()
        {
        }

        public LineLoadFrame_E(string etabsUniqueName, string etabsLabel, string storyName,
                               MyPoint start, MyPoint end)
        {
            uniqueName = etabsUniqueName ?? "";
            label = etabsLabel ?? "";
            story = storyName ?? "";
            planStart = start;
            planEnd = end;
        }

        /// <summary>The name the API works with, which is what a load is recorded against.</summary>
        public string UniqueName { get { return uniqueName ?? ""; } }

        /// <summary>The name ETABS shows, which is what a person would look for.</summary>
        public string Label { get { return label ?? ""; } }

        public string Story { get { return story ?? ""; } }

        public MyPoint PlanStart { get { return planStart; } }

        public MyPoint PlanEnd { get { return planEnd; } }

        /// <summary>Every distributed load on this frame, in the order ETABS gave them.</summary>
        public IList<LineLoad_E> Loads
        {
            get { return loads ?? (loads = new List<LineLoad_E>()); }
        }

        /// <summary>How long the frame is in plan, in mm. Nought on a column.</summary>
        public double PlanLength
        {
            get { return WallGeometry.PlanLength(planStart, planEnd); }
        }

        /// <summary>True when there is a line in plan to compare anything against.</summary>
        public bool HasPlanLine { get { return PlanLength > 0; } }

        /// <summary>True when the frame carries a downward distributed load of any size.</summary>
        public bool HasGravityLoad
        {
            get { return Loads.Any(load => load != null && load.IsGravity && !load.IsMoment); }
        }

        public void Add(LineLoad_E load)
        {
            if (load != null)
            {
                Loads.Add(load);
            }
        }

        /// <summary>
        /// Puts what the model carries now in place of what was read at the import, after a
        /// load has been written back to it.
        /// </summary>
        public void ReplaceLoads(IEnumerable<LineLoad_E> now)
        {
            Loads.Clear();
            foreach (LineLoad_E load in now ?? Enumerable.Empty<LineLoad_E>())
            {
                Add(load);
            }
        }

        /// <summary>How this frame is named in a report: the label where it has one.</summary>
        public string Identifier
        {
            get { return Label.Length > 0 ? Label : UniqueName; }
        }

        public override string ToString()
        {
            return $"frame {Identifier} on {Story}, {Loads.Count} distributed load(s)";
        }
    }
}
