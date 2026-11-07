using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DiscrepancyScanner
{
    /// <summary>
    /// One uniform load assigned to an ETABS area object, brought into kN/m2 and turned the
    /// right way up.
    ///
    /// ETABS lets a downward load be written either way round depending on which direction it
    /// was assigned in - positive down when the direction is gravity, negative down when it is
    /// the global Z axis - so the sign is settled here, once, and everything downstream reads
    /// a positive number as load pressing on the slab.
    ///
    /// A load assigned in any other direction is kept rather than dropped, marked as not
    /// gravity, so it can be reported instead of quietly counting for nothing.
    /// </summary>
    [DataContract]
    public class AreaLoad_E
    {
        [DataMember]
        private string pattern = "";
        [DataMember]
        private double value;
        [DataMember]
        private int direction;
        [DataMember]
        private bool gravity;
        [DataMember]
        private bool toFrame;

        public AreaLoad_E()
        {
        }

        public AreaLoad_E(string loadPattern, double downwardValue, int etabsDirection,
                          bool isGravity, bool transferredToFrames)
        {
            pattern = loadPattern ?? "";
            value = downwardValue;
            direction = etabsDirection;
            gravity = isGravity;
            toFrame = transferredToFrames;
        }

        /// <summary>The ETABS load pattern this was assigned in, as it is named in the model.</summary>
        public string Pattern { get { return pattern; } }

        /// <summary>The load in kN/m2, positive when it presses down on the slab.</summary>
        public double Value { get { return value; } }

        /// <summary>The ETABS direction code it was assigned in, kept so it can be reported.</summary>
        public int Direction { get { return direction; } }

        /// <summary>False for a load that does not act downwards, which the check cannot use.</summary>
        public bool IsGravity { get { return gravity; } }

        /// <summary>
        /// True for a load assigned as "uniform to frames" rather than to the shell itself,
        /// which is how a membrane slab hands its load straight to the beams around it. Both
        /// are read, because which one a model uses is a modelling choice rather than a
        /// difference in what the slab carries.
        /// </summary>
        public bool IsToFrame { get { return toFrame; } }

        /// <summary>
        /// The ETABS direction codes that mean "down". Gravity is positive downwards and the
        /// global and local Z axes are positive upwards, which is why the sign is settled on
        /// the way in rather than left to whoever reads the value.
        /// </summary>
        public static bool IsDownwardDirection(int etabsDirection, out double sign)
        {
            switch (etabsDirection)
            {
                case 10: // gravity
                case 11: // projected gravity
                    sign = 1d;
                    return true;
                case 3:  // local 3, which on a flat slab points up
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
            return $"{pattern} {Math.Round(value, 3)} kN/m2" + (gravity ? "" : $" (direction {direction})");
        }
    }

    /// <summary>
    /// The uniform loads on one ETABS area, added up into the four the check compares.
    ///
    /// Added up rather than taken one at a time, because a load set assigned twice - once to
    /// the shell and once to the frames around it, or in two goes - is two entries in the
    /// model and one load on the slab.
    /// </summary>
    public sealed class AreaLoadSummary
    {
        private readonly List<string> unmapped = new List<string>();
        private readonly List<AreaLoad_E> sideways = new List<AreaLoad_E>();

        public LoadSet Loads;

        /// <summary>True when the area carries no uniform load at all.</summary>
        public bool IsEmpty { get; private set; } = true;

        /// <summary>
        /// Load patterns on this area that the settings do not name as one of the four. Their
        /// values are not counted, so they are reported instead: a load put on a pattern
        /// nobody mapped is exactly the load that goes missing without a word.
        /// </summary>
        public IList<string> UnmappedPatterns { get { return unmapped; } }

        /// <summary>Loads on this area that do not act downwards, which cannot be compared.</summary>
        public IList<AreaLoad_E> NonGravityLoads { get { return sideways; } }

        public static AreaLoadSummary From(IEnumerable<AreaLoad_E> loads, LoadPatternMap.Mapping mapping)
        {
            AreaLoadSummary summary = new AreaLoadSummary();

            if (loads == null || mapping == null)
            {
                return summary;
            }

            foreach (AreaLoad_E load in loads)
            {
                if (load == null)
                {
                    continue;
                }

                summary.IsEmpty = false;

                if (!load.IsGravity)
                {
                    summary.sideways.Add(load);
                    continue;
                }

                LoadCategory category = mapping.Categorise(load.Pattern);
                if (category == LoadCategory.None)
                {
                    if (!summary.unmapped.Contains(load.Pattern, StringComparer.OrdinalIgnoreCase))
                    {
                        summary.unmapped.Add(load.Pattern);
                    }
                    continue;
                }

                summary.Loads = summary.Loads.Add(category, load.Value);
            }

            return summary;
        }
    }
}
