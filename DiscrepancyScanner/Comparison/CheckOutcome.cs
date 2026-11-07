namespace DiscrepancyScanner
{
    /// <summary>One of the comparisons the tool runs, named so its result can be filtered.</summary>
    public enum CheckKind
    {
        /// <summary>Not the output of any one check; carries no filter of its own.</summary>
        None,
        /// <summary>Whether the element was paired with a counterpart at all.</summary>
        Pairing,
        Location,
        Orientation,
        Shape,
        Dimensions,
        /// <summary>The Revit column type mark against the ETABS frame group assignments.</summary>
        TypeMark,
        /// <summary>The beam comparison, which reports a single verdict rather than per-check ones.</summary>
        Beam,

        // The wall checks. A wall is a line and a thickness, so it has a check for each: how
        // far the ETABS panel stands off the line the Revit wall runs along, and whether the
        // two are the same thickness.

        /// <summary>How far the ETABS wall sits off the Revit wall's line.</summary>
        WallLocation,
        /// <summary>The two wall thicknesses against each other.</summary>
        WallThickness,

        /// <summary>
        /// How much of the Revit wall the ETABS panels along it actually cover. A warning
        /// rather than a failure: the two models are agreed that there is a wall here, and
        /// what is in question is how far it runs.
        /// </summary>
        WallLength,

        // The slab checks. Each reports on a piece of slab rather than on a whole element,
        // and each has a switch of its own so the openings can be looked at without the
        // thickness differences in the way, or the other way round.

        /// <summary>Slab depth where both models have slab.</summary>
        SlabThickness,
        /// <summary>ETABS slab reaching past the edge of the Revit floor.</summary>
        SlabMissingInRevit,
        /// <summary>Revit floor reaching past the edge of the ETABS slab.</summary>
        SlabMissingInEtabs,
        /// <summary>A hole inside the slab that only the Revit model has.</summary>
        SlabOpeningInRevit,
        /// <summary>A hole inside the slab that only the ETABS model has.</summary>
        SlabOpeningInEtabs,

        /// <summary>
        /// The area loads assigned to the ETABS shell against what the loading plan and its
        /// schedule say the slab there should carry.
        /// </summary>
        SlabLoads,

        /// <summary>
        /// The line loads drawn on the loading plan against the distributed loads on the ETABS
        /// frames under them.
        ///
        /// Reported on the drawn line rather than on the frame, which is the other way round
        /// from every other check here. This one is read on a plan of the loading drawing, and
        /// the question it answers is which of the lines somebody drew the model disagrees
        /// with - the frames are the answer rather than the question.
        /// </summary>
        LineLoads,

        // The two directions of the line load check where only one model has a load at all.
        // Kept apart from LineLoads itself for the same reason the slab outline checks are kept
        // apart from the thickness: a finding that one model has something and the other has
        // nothing is not a disagreement about a number, and colouring it red says the two were
        // compared and differed when one of them had nothing to compare.

        /// <summary>A line load is drawn on the plan and ETABS carries none there.</summary>
        LineLoadMissingInEtabs,

        /// <summary>An ETABS frame carries a line load the plan does not ask for.</summary>
        LineLoadMissingInRevit,

        // Added at the end rather than beside the other slab checks: a review is filed against
        // the number each result's check carries, and putting this in the middle would renumber
        // the load checks under every review already written against them.

        /// <summary>
        /// Post-tensioned or reinforced, read off the ETABS property name and the Revit floor
        /// type name, where both models have slab. On the same pieces as the thickness check.
        /// </summary>
        SlabSystem,

        // The SIDL_C check, at the end for the same reason. The line load check's three
        // results over again, for the walls and fill strips weighed as load: kept as kinds of
        // their own so their switches, counts and reviews never mix with the loading plan's.

        /// <summary>A wall or fill strip's weight against the SIDL_C load on the frame under it.</summary>
        SidlC,

        /// <summary>A wall or fill strip weighed as a load, and ETABS carries no SIDL_C there.</summary>
        SidlCMissingInEtabs,

        /// <summary>An ETABS frame carries a SIDL_C load no wall or fill strip accounts for.</summary>
        SidlCMissingInRevit,

        // The SIDL_C area check, after the line one: the light weight fill floors, weighed,
        // against the SIDL_C area load on the slab under them.

        /// <summary>A fill floor's weight against the SIDL_C ETABS carries under it, averaged.</summary>
        SidlCArea,

        /// <summary>A fill floor in Revit, and no SIDL_C - or no slab - under it in ETABS.</summary>
        SidlCAreaMissingInEtabs,

        /// <summary>An ETABS area carries SIDL_C and no fill floor stands over it.</summary>
        SidlCAreaMissingInRevit
    }

    /// <summary>What one check concluded, which is also the colour it is drawn in.</summary>
    public enum ResultLevel
    {
        /// <summary>Imported but not compared.</summary>
        NotChecked,
        /// <summary>Green.</summary>
        Passed,
        /// <summary>Red.</summary>
        Failed,
        /// <summary>Orange: passed, but something about it wants a human look.</summary>
        Review
    }

    /// <summary>
    /// The result of one check on one element. An element carries one of these per check it
    /// went through, which is what lets the viewer show or hide the checks one at a time
    /// without ever losing the element or the results of the checks still switched on.
    /// </summary>
    public sealed class CheckOutcome
    {
        public CheckOutcome(CheckKind kind, ResultLevel level, string summary = "",
                            string etabs = null, string revit = null,
                            string label = null, string token = null)
        {
            Kind = kind;
            Level = level;
            Summary = summary ?? "";
            Etabs = etabs ?? "";
            Revit = revit ?? "";
            Label = label ?? "";
            Token = token ?? "";
        }

        public CheckKind Kind { get; }

        // Where a check asks its question in more than one place, the place has to be part of
        // the result. A check that asks once - is this column where Revit says it is? - is
        // named after itself and leaves both of these empty.

        /// <summary>
        /// What exactly this result is about, in the words the card prints: "Top steel, left
        /// end", "SDL". Empty on a check whose own name already says it, and the viewer then
        /// falls back to that name.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// The same place in a word that never changes. Everything filed against a result - the
        /// run history that says what is new, the fingerprint a review is held against - is
        /// keyed on this rather than on the label, so rewording a label does not turn every
        /// standing finding into a new one and every review into a stale one.
        /// </summary>
        public string Token { get; }

        public ResultLevel Level { get; }

        /// <summary>One line saying what this check found, for the status text.</summary>
        public string Summary { get; }

        // The two numbers the check actually compared, each formatted as it should be read.
        // They are filled in by the check itself because it is the only thing that knows which
        // two of the many values on an element this particular question was about - a location
        // check compares a distance against a tolerance, a dimensions check compares two
        // widths, and nothing downstream can tell those apart from the element alone.
        //
        // Empty on a check that has no two sides to show: an element with nothing to pair with
        // has only one model to read, and a check reporting a single verdict has no pair.

        /// <summary>What the ETABS side reads, or empty where the check has no pair to show.</summary>
        public string Etabs { get; }

        /// <summary>What the Revit side reads, or empty.</summary>
        public string Revit { get; }

        /// <summary>True when there are two sides worth printing side by side.</summary>
        public bool HasSides => Etabs.Length > 0 || Revit.Length > 0;

        public override string ToString()
        {
            return $"{Kind}: {Level}";
        }
    }
}
