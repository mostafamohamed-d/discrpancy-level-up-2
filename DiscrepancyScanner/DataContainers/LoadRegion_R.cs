using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One filled region off a Revit loading plan: the piece of floor it covers and the area
    /// load label written on it - 7, or H1, or whatever the schedule calls that load.
    ///
    /// A loading plan is drawn as filled regions with a number on each, and the number is what
    /// the loading schedule is looked up by. The region is view specific, so it carries no
    /// level of its own - the view it was drawn in is what says which floor it belongs to,
    /// which is why the user pairs a view with a level rather than the tool guessing.
    /// </summary>
    public class LoadRegion_R
    {
        private readonly List<PlanPolygon> parts = new List<PlanPolygon>();

        public LoadRegion_R(long revitId, IEnumerable<PlanPolygon> regions, string numberText)
        {
            RevitID = revitId;
            NumberText = numberText ?? "";
            Number = LoadLabel.Read(NumberText);

            if (regions != null)
            {
                parts.AddRange(regions.Where(region => region != null));
            }
        }

        public long RevitID { get; }

        /// <summary>
        /// The area load label, or empty when the region carries none. A region with no label
        /// on it is still read and still reported, because a patch of loading plan nobody
        /// numbered is a gap in the plan rather than a piece of floor with no load on it.
        /// </summary>
        public string Number { get; }

        /// <summary>Whatever the parameter actually held, before it was tidied into a label.</summary>
        public string NumberText { get; }

        public bool HasNumber { get { return Number.Length > 0; } }

        /// <summary>The name of the filled region type, shown so a region is findable in Revit.</summary>
        public string TypeName { get; set; } = "";

        /// <summary>
        /// Every load number parameter the region carries, in the order they are asked, each as
        /// "name (instance) = value" or "name (type) = value". The first is the one read. Kept
        /// for the loading plan dump: a region carrying one number on its type and another on
        /// itself reads as the first of them, and nothing else says which that was.
        /// </summary>
        public IList<string> NumberSources { get; set; } = new List<string>();

        /// <summary>
        /// Every separate piece of plan this region covers. One filled region can be drawn as
        /// several islands, and each of them is the region.
        /// </summary>
        public IList<PlanPolygon> Parts { get { return parts; } }

        public double Area { get { return parts.Sum(part => part.Area); } }

        public override string ToString()
        {
            return HasNumber
                ? $"loading region {RevitID}, load {Number}"
                : $"loading region {RevitID}, no load number";
        }
    }
}
