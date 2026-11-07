using System.Collections.Generic;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Which checks are switched on, in one place.
    ///
    /// A check that is off publishes no result at all rather than a passing one. That matters:
    /// a passing result would colour the element green and say the two models were compared and
    /// agreed, which is exactly the thing that was not done. Publishing nothing leaves the
    /// element carrying only the checks that did run, and if that is none of them it is drawn
    /// grey and unlabelled, which is honest.
    ///
    /// This is deliberately not the same switch as the chips across the top of the check
    /// window. Those choose what is being read on the plan at this moment and are moved
    /// constantly; these say what the tool asks of the models at all, and are set once for a
    /// project under Set up.
    /// </summary>
    public static class CheckSwitches
    {
        /// <summary>True while this check is one the tool is being asked to make.</summary>
        public static bool IsOn(CheckKind kind)
        {
            PrefrencesData pref = PrefrencesData.getPrefrencesData();

            switch (kind)
            {
                case CheckKind.Location: return pref.RunColumnLocationCheck;
                case CheckKind.Orientation: return pref.RunColumnOrientationCheck;
                case CheckKind.Shape: return pref.RunColumnShapeCheck;
                case CheckKind.Dimensions: return pref.RunColumnDimensionsCheck;
                case CheckKind.TypeMark: return pref.RunColumnTypeMarkCheck;

                case CheckKind.Beam: return pref.RunBeamMarkCheck;

                case CheckKind.WallLocation: return pref.RunWallLocationCheck;
                case CheckKind.WallThickness: return pref.RunWallThicknessCheck;
                case CheckKind.WallLength: return pref.RunWallLengthCheck;

                case CheckKind.SlabThickness: return pref.RunSlabThicknessCheck;
                case CheckKind.SlabSystem: return pref.RunSlabSystemCheck;
                case CheckKind.SlabMissingInRevit:
                case CheckKind.SlabMissingInEtabs: return pref.RunSlabOutlineCheck;
                case CheckKind.SlabOpeningInRevit:
                case CheckKind.SlabOpeningInEtabs: return pref.RunSlabOpeningCheck;

                case CheckKind.LineLoads:
                case CheckKind.LineLoadMissingInEtabs:
                case CheckKind.LineLoadMissingInRevit: return pref.RunLineLoadCheck;

                case CheckKind.SidlC:
                case CheckKind.SidlCMissingInEtabs:
                case CheckKind.SidlCMissingInRevit: return pref.RunSidlCCheck;

                case CheckKind.SidlCArea:
                case CheckKind.SidlCAreaMissingInEtabs:
                case CheckKind.SidlCAreaMissingInRevit: return pref.RunSidlCAreaCheck;

                // Pairing has no switch of its own. An element with no counterpart is not the
                // output of a check that could be declined - it is the fact everything else is
                // built on, and hiding it would leave the element looking checked and clean.
                //
                // The slab load check has its own switch already, next to the loading plans it
                // needs, so it is not repeated here.
                default: return true;
            }
        }

        /// <summary>
        /// Drops the results of any check that is switched off. Called by each comparison once
        /// it has collected its outcomes, so a disabled check leaves nothing behind anywhere -
        /// no colour, no count, no row on the card, and no switch in the results panel.
        /// </summary>
        public static void RemoveSwitchedOff(List<CheckOutcome> outcomes)
        {
            if (outcomes == null)
            {
                return;
            }
            outcomes.RemoveAll(outcome => !IsOn(outcome.Kind));
        }
    }
}
