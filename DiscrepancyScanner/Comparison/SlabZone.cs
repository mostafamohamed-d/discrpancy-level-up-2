using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>
    /// Which question a pass of the slab mesh is asking, and so which edges it cuts along.
    /// The two produce separate meshes over the same ETABS areas; see SlabMeshCompare.Purpose.
    /// </summary>
    public enum SlabMeshPurpose
    {
        /// <summary>Thickness, outlines and openings, cut along the Revit floor edges.</summary>
        Slab,
        /// <summary>Area loads against the loading plan, cut along the loading region edges.</summary>
        Loads
    }

    /// <summary>What one piece of slab turned out to be.</summary>
    public enum SlabZoneState
    {
        /// <summary>Imported but not compared.</summary>
        NotChecked,
        /// <summary>Both models have slab here and agree on how thick it is.</summary>
        Matched,
        /// <summary>Both models have slab here and disagree on how thick it is.</summary>
        ThicknessMismatch,
        /// <summary>
        /// Both models have slab here, but the Revit floor is sloped or shape edited, so its
        /// thickness is a range rather than a value and a human has to say whether it agrees.
        /// </summary>
        ThicknessReview,
        /// <summary>ETABS has slab here and Revit has none, along a slab edge.</summary>
        MissingInRevit,
        /// <summary>Revit has floor here and ETABS has none, along a slab edge.</summary>
        MissingInEtabs,
        /// <summary>A hole inside the slab that only the Revit model has.</summary>
        OpeningInRevitOnly,
        /// <summary>A hole inside the slab that only the ETABS model has.</summary>
        OpeningInEtabsOnly,
        /// <summary>The geometry could not be worked out, so nothing is claimed about it.</summary>
        Unresolved
    }

    /// <summary>
    /// What the load check made of a piece of slab. Kept apart from
    /// <see cref="SlabZoneState"/> rather than folded into it, because the two answer
    /// different questions about the same piece and a zone carries both at once: a slab can
    /// be the right thickness and carry the wrong load, and saying so is the point.
    /// </summary>
    public enum SlabLoadState
    {
        /// <summary>The load check was not run over this zone.</summary>
        NotChecked,
        /// <summary>The four loads on the ETABS shell match what the loading plan asks for.</summary>
        Matched,
        /// <summary>At least one of the four is out by more than the buffer.</summary>
        Mismatch,
        /// <summary>The loading plan has no region over this piece of slab.</summary>
        NoLoadingRegion,
        /// <summary>The region here carries no area load number.</summary>
        NoLoadNumber,
        /// <summary>The number on the region is not in the schedule sheet that was picked.</summary>
        NotInSchedule,
        /// <summary>Two loading regions with different numbers cover this piece.</summary>
        ConflictingRegions,
        /// <summary>The ETABS shell has no uniform load on it at all.</summary>
        NoEtabsLoad
    }

    /// <summary>How a slab is reinforced, as far as its name says.</summary>
    public enum ConcreteSystem
    {
        /// <summary>The name says neither, or there is no name to read.</summary>
        Unknown,
        PostTensioned,
        Reinforced
    }

    /// <summary>
    /// PT or RC, read off a name: the ETABS area property on one side, the Revit floor type on
    /// the other. Neither model carries it as data - the only place the two say it is the name
    /// the section or the type was given, so the name is what is read.
    /// </summary>
    public static class ConcreteSystems
    {
        /// <summary>
        /// A name carrying any of <see cref="PrefrencesData.PtSlabNames"/> is post-tensioned;
        /// failing that, any of <see cref="PrefrencesData.RcSlabNames"/> is reinforced. PT is
        /// asked first because a PT name nearly always has an S in it too. A name with neither
        /// - a drop panel "DP7" - is unknown, and the piece is not checked.
        /// </summary>
        public static ConcreteSystem Read(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ConcreteSystem.Unknown;
            }
            PrefrencesData pref = PrefrencesData.getPrefrencesData();
            if (PrefrencesData.MatchesAnyPhrase(name, PrefrencesData.SplitTypeNamePhrases(pref.PtSlabNames), out _))
            {
                return ConcreteSystem.PostTensioned;
            }
            if (PrefrencesData.MatchesAnyPhrase(name, PrefrencesData.SplitTypeNamePhrases(pref.RcSlabNames), out _))
            {
                return ConcreteSystem.Reinforced;
            }
            return ConcreteSystem.Unknown;
        }

        /// <summary>The two letters the drawings use, or a question mark for a name that says neither.</summary>
        public static string Text(ConcreteSystem system)
        {
            switch (system)
            {
                case ConcreteSystem.PostTensioned: return "PT";
                case ConcreteSystem.Reinforced: return "RC";
                default: return "?";
            }
        }
    }

    /// <summary>
    /// One piece of slab with one verdict on it: the unit the slab check reports and the
    /// viewer draws.
    ///
    /// A zone is normally a whole ETABS area object, untouched. It is only ever a piece of one
    /// when the two models stop agreeing part way across it - a drop panel in Revit that ETABS
    /// does not have, an opening on one side only, an edge the two models drew differently -
    /// and then the area is cut exactly along where the answer changes, so what is coloured is
    /// the part that is actually wrong rather than the whole shell.
    /// </summary>
    public class SlabZone : IHasResultFields
    {
        private readonly List<ResultField> resultFields = new List<ResultField>();
        private readonly List<CheckOutcome> outcomes = new List<CheckOutcome>();

        public SlabZone(PlanPolygon plan, SlabZoneState state, string story)
        {
            Plan = plan;
            State = state;
            Story = story ?? "";
        }

        public PlanPolygon Plan { get; private set; }

        public SlabZoneState State { get; private set; }

        public string Story { get; private set; }

        /// <summary>The ETABS area this piece came out of, empty when only Revit has slab here.</summary>
        public string EtabsName { get; set; } = "";

        public string EtabsProperty { get; set; } = "";

        /// <summary>ETABS thickness in mm, or zero when ETABS has nothing here.</summary>
        public double EtabsThickness { get; set; }

        /// <summary>The Revit floor to select, or zero when only ETABS has slab here.</summary>
        public long RevitId { get; set; }

        public string RevitType { get; set; } = "";

        /// <summary>Where the Revit floor here was read from - Solid or Sketch.</summary>
        public string RevitSource { get; set; } = "";

        /// <summary>Depth of Revit concrete in mm, or zero when Revit has nothing here.</summary>
        public double RevitThickness { get; set; }

        /// <summary>True when the Revit depth here comes from more than one stacked floor.</summary>
        public bool IsStackedInRevit { get; set; }

        /// <summary>
        /// True when this zone is a whole ETABS area object rather than a piece of one, which
        /// is the normal case and the one the base mesh is left untouched for.
        /// </summary>
        public bool IsWholeEtabsArea { get; set; }

        /// <summary>Why the geometry could not be worked out, for an unresolved zone.</summary>
        public string Note { get; set; } = "";

        /// <summary>
        /// Which mesh cut this piece. Part of how it is filed: the two meshes cover the same
        /// floor, and a shell that comes through both of them unbroken would otherwise give a
        /// thickness zone and a load zone the same name.
        /// </summary>
        public SlabMeshPurpose Purpose { get; set; } = SlabMeshPurpose.Slab;

        /// <summary>
        /// How this piece is filed when somebody reviews it, accepts it or writes a note on it.
        ///
        /// A zone has no name of its own. It is cut afresh every run, and the shell it came out
        /// of can come through in any number of pieces, so it is named instead by the things
        /// that do not move while the two models do not: the shell or floor it was cut from,
        /// which mesh cut it, and where the piece sits and how big it is.
        ///
        /// The centroid rather than a point inside it, because an interior point is found by
        /// walking sample lines across the shape and can jump right across a zone when an edge
        /// moves a millimetre - where a centroid moves the millimetre. The area is in as well
        /// as the position because the one collision the position alone allows is a real one: an
        /// opening sits at the centre of the piece it is a hole in.
        ///
        /// Both are rounded, coarsely enough to ride out the last bits of a double and finely
        /// enough that neighbouring pieces cannot land on one name. A cut that really does move
        /// leaves the review behind rather than handing it to whatever was cut there instead,
        /// which is the safe way round: a lost acceptance comes back as open work, where an
        /// inherited one would quietly settle a finding nobody has ever seen.
        /// </summary>
        public string ReviewKey
        {
            get
            {
                string cutFrom = !string.IsNullOrEmpty(EtabsName)
                    ? EtabsName
                    : (RevitId > 0 ? "R" + RevitId.ToString(CultureInfo.InvariantCulture) : "");

                // Geometry on its own is too thin a name to hang an agreement on: it says where
                // something was without saying what it was a piece of.
                if (cutFrom.Length == 0 || Plan == null)
                {
                    return "";
                }

                MyPoint centre = Plan.Centroid();
                return "Slab|" + (Purpose == SlabMeshPurpose.Loads ? "L" : "T")
                       + "|" + cutFrom
                       + "|" + Stepped(centre.X, 10d)
                       + "|" + Stepped(centre.Y, 10d)
                       + "|" + Stepped(Area / 1e6, 0.001d);
            }
        }

        /// <summary>
        /// A number to the nearest step, as a whole number that reads the same everywhere. A
        /// double would print differently under another culture and in exponents once it got
        /// big, and a name is only worth having if it comes back the same on every machine.
        /// </summary>
        private static string Stepped(double value, double step)
        {
            return ((long)Math.Round(value / step)).ToString(CultureInfo.InvariantCulture);
        }

        #region the load check

        /// <summary>
        /// What the load check made of this piece, or
        /// <see cref="SlabLoadState.NotChecked"/> when it was not run. A zone that was not
        /// load checked publishes no load result at all, so the switch for it is simply never
        /// reached rather than showing rows that say nothing.
        /// </summary>
        public SlabLoadState LoadState { get; set; } = SlabLoadState.NotChecked;

        /// <summary>The area load label the loading plan puts here, empty when it puts none.</summary>
        public string LoadNumber { get; set; } = "";

        /// <summary>What the schedule calls that number.</summary>
        public string LoadDescription { get; set; } = "";

        /// <summary>The four loads the loading plan and its schedule ask for, in kN/m2.</summary>
        public LoadSet PlannedLoads { get; set; }

        /// <summary>The four loads the ETABS shell actually carries, in kN/m2.</summary>
        public LoadSet EtabsLoads { get; set; }

        /// <summary>The Revit filled region that answered, for selecting it back in Revit.</summary>
        public long LoadRegionId { get; set; }

        /// <summary>
        /// Every ETABS shell standing over this piece, the one it was cut from first: the
        /// shells whose loads were added together into <see cref="EtabsLoads"/>. Loads add,
        /// whichever shell is on top, and a sum nobody can see the parts of cannot be checked.
        /// </summary>
        public IList<string> LoadShells { get; set; } = new List<string>();

        /// <summary>Anything about the loads worth saying in words rather than in numbers.</summary>
        public string LoadNote { get; set; } = "";

        /// <summary>True when this zone went through the load check at all.</summary>
        public bool WasLoadChecked { get { return LoadState != SlabLoadState.NotChecked; } }

        /// <summary>Which of the four are out by more than the buffer. Empty when none are.</summary>
        public IList<LoadCategory> MismatchedLoads { get; set; } = new List<LoadCategory>();

        public ResultLevel LoadLevel
        {
            get
            {
                switch (LoadState)
                {
                    case SlabLoadState.Matched:
                        return ResultLevel.Passed;
                    case SlabLoadState.Mismatch:
                        return ResultLevel.Failed;
                    case SlabLoadState.NotChecked:
                        return ResultLevel.NotChecked;
                    default:
                        return ResultLevel.Review;
                }
            }
        }

        /// <summary>One line saying what the load check found here.</summary>
        public string LoadSummary
        {
            get { return LoadStateSummary + SizeAside(SlabMeshPurpose.Loads, LoadLevel); }
        }

        private string LoadStateSummary
        {
            get
            {
                switch (LoadState)
                {
                    case SlabLoadState.Matched:
                        return $"load {LoadNumber} matches: {EtabsLoads}" + Aside;
                    case SlabLoadState.Mismatch:
                        return $"load {LoadNumber} does not match on " +
                               string.Join(" and ", MismatchedLoads.Select(LoadSet.NameOf)) + ": " +
                               string.Join(", ", MismatchedLoads.Select(category =>
                                   $"{LoadSet.NameOf(category)} plan {LoadSet.Text(PlannedLoads[category])}, " +
                                   $"ETABS {LoadSet.Text(EtabsLoads[category])}")) + Aside;
                    case SlabLoadState.NoLoadingRegion:
                        return "the loading plan has nothing drawn over this piece of slab";
                    case SlabLoadState.NoLoadNumber:
                        return "the loading region here carries no area load number";
                    case SlabLoadState.NotInSchedule:
                        return $"the loading plan says load {LoadNumber}, which the schedule sheet does " +
                               "not define";
                    case SlabLoadState.ConflictingRegions:
                        return "loading regions drawn on top of each other cover this piece and they " +
                               $"disagree: load {LoadNumber} on region {LoadRegionId}, the smallest " +
                               $"here and so the one used, against {LoadNote}";
                    case SlabLoadState.NoEtabsLoad:
                        return $"the loading plan asks for load {LoadNumber} here and the ETABS shell " +
                               "carries no uniform load at all";
                    default:
                        return "loads not checked";
                }
            }
        }

        /// <summary>
        /// What was left out of the comparison, said out loud on the end of the verdict.
        ///
        /// A load on a pattern the settings do not name as one of the four is not counted, and
        /// a zone whose four numbers then match reads as right. That is the one way this check
        /// can be quietly wrong, so the zone that it happened on says so.
        /// </summary>
        private string Aside
        {
            get { return string.IsNullOrEmpty(LoadNote) ? "" : " - " + LoadNote; }
        }

        /// <summary>
        /// Everything a piece of the load mesh says. It is answered by the loading plan and by
        /// nothing else, so it is described in the plan's terms - the area load asked for here,
        /// the region that asked for it, the shells whose loads were added to meet it - and
        /// says nothing about slab thickness or Revit floors, which this cut is never asked
        /// about. It used to carry the slab pass's rows regardless: "slab check: not checked",
        /// and a Revit floor and floor type taken from whatever lay under one corner of it.
        /// </summary>
        private void BuildLoadPieceResults()
        {
            if (!string.IsNullOrEmpty(EtabsName))
            {
                resultFields.Add(new ResultField(SlabResultKeys.EtabsName, "ETABS area", EtabsName));
            }
            if (!string.IsNullOrEmpty(EtabsProperty))
            {
                resultFields.Add(new ResultField(SlabResultKeys.EtabsProperty, "ETABS property", EtabsProperty));
            }
            if (LoadShells.Count > 1)
            {
                resultFields.Add(new ResultField(SlabResultKeys.LoadShells, "ETABS shells added",
                    string.Join(" + ", LoadShells)));
            }

            resultFields.Add(new ResultField(SlabResultKeys.LoadPlan, "Loading plan", PlanSays));

            if (LoadRegionId > 0)
            {
                resultFields.Add(new ResultField(SlabResultKeys.LoadRegion, "Loading region",
                    LoadRegionId.ToString(CultureInfo.CurrentCulture)));
            }

            resultFields.Add(ResultField.Number(SlabResultKeys.Area, "Zone area (m2)", Area / 1e6, 2));

            if (WasLoadChecked)
            {
                BuildLoadResults();
            }

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        /// <summary>What the loading plan asks for over this piece, in words.</summary>
        private string PlanSays
        {
            get
            {
                if (!WasLoadChecked)
                {
                    return "not read";
                }
                if (LoadState == SlabLoadState.NoLoadingRegion)
                {
                    return "nothing drawn here";
                }
                if (string.IsNullOrEmpty(LoadNumber))
                {
                    return "no area load number";
                }
                return string.IsNullOrEmpty(LoadDescription)
                    ? $"load {LoadNumber}"
                    : $"load {LoadNumber} - {LoadDescription}";
            }
        }

        /// <summary>
        /// Adds the load results to the zone. Called from <see cref="BuildResults"/>, and only
        /// when the load check actually ran over this zone.
        /// </summary>
        private void BuildLoadResults()
        {
            bool failed = LoadState == SlabLoadState.Mismatch;

            resultFields.Add(new ResultField(SlabResultKeys.LoadState, "Load check", LoadSummary,
                failed, CheckKind.SlabLoads));

            resultFields.Add(new ResultField(SlabResultKeys.LoadNumber, "Area load number",
                string.IsNullOrEmpty(LoadNumber) ? "-" : LoadNumber,
                failed, CheckKind.SlabLoads));

            if (!string.IsNullOrEmpty(LoadDescription))
            {
                resultFields.Add(new ResultField(SlabResultKeys.LoadDescription, "Area load",
                    LoadDescription, false, CheckKind.SlabLoads));
            }

            foreach (LoadCategory category in LoadSet.Categories)
            {
                resultFields.Add(new ResultField(KeyFor(category),
                    $"{LoadSet.NameOf(category)} R / E (kN/m2)",
                    $"{LoadSet.Text(PlannedLoads[category])} / {LoadSet.Text(EtabsLoads[category])}",
                    MismatchedLoads.Contains(category), CheckKind.SlabLoads));
            }

            // Where there are two sets of numbers, each of the four loads answers for itself.
            // Where there are not - no region drawn here, no load number on it, nothing in the
            // schedule - there is only the one thing to say, and it is said in words.
            if (LoadState == SlabLoadState.Matched || LoadState == SlabLoadState.Mismatch)
            {
                AddLoadOutcomes();
            }
            else
            {
                outcomes.Add(new CheckOutcome(CheckKind.SlabLoads, LoadLevel, LoadSummary));
            }
        }

        /// <summary>
        /// The four loads, one result each: what the loading plan asks for over this piece of
        /// slab against what the ETABS shell is carrying.
        ///
        /// One result for all four says the loads disagree without saying which of them does,
        /// and a zone that is right on three and wrong on the live load is a different piece of
        /// work from one that is wrong on every one of them.
        /// </summary>
        private void AddLoadOutcomes()
        {
            foreach (LoadCategory category in LoadSet.Categories)
            {
                string name = LoadSet.NameOf(category);
                string planned = LoadSet.Text(PlannedLoads[category]);
                string actual = LoadSet.Text(EtabsLoads[category]);

                outcomes.Add(new CheckOutcome(CheckKind.SlabLoads,
                    MismatchedLoads.Contains(category) ? ResultLevel.Failed : ResultLevel.Passed,
                    $"load {LoadNumber} {name}: plan {planned}, ETABS {actual}" + Aside,
                    etabs: actual, revit: planned,
                    label: name + " (kN/m2)", token: name));
            }

            // What was left out, as a result of its own. The four rows above have two numbers
            // each, and a row with numbers prints them instead of its sentence - so without
            // this the one thing that says the comparison may be quietly wrong would be
            // readable in the findings list and nowhere on the card.
            //
            // It is not a verdict and does not carry one: the four loads have already said
            // whether they agree, and colouring the zone orange for a load pattern the
            // settings do not name would be this check answering a question nobody asked it.
            if (LoadNote.Length > 0)
            {
                outcomes.Add(new CheckOutcome(CheckKind.SlabLoads, ResultLevel.Passed,
                    LoadNote, label: "Not counted", token: "Note"));
            }
        }

        private static string KeyFor(LoadCategory category)
        {
            switch (category)
            {
                case LoadCategory.Sdl: return SlabResultKeys.LoadSdl;
                case LoadCategory.Ll: return SlabResultKeys.LoadLl;
                case LoadCategory.Lr: return SlabResultKeys.LoadLr;
                default: return SlabResultKeys.LoadMep;
            }
        }

        #endregion

        #region PT or RC

        /// <summary>What the ETABS property name says this slab is.</summary>
        public ConcreteSystem EtabsSystem { get { return ConcreteSystems.Read(EtabsProperty); } }

        /// <summary>What the Revit floor type name says this slab is.</summary>
        public ConcreteSystem RevitSystem { get { return ConcreteSystems.Read(RevitType); } }

        /// <summary>
        /// True when this piece is one the PT or RC question is asked of: where both models
        /// have slab, which is exactly the pieces the thickness is asked of, and where both
        /// names say PT or RC.
        ///
        /// Not a patch only one model has - there is nothing to hold it against, and the
        /// outline check has already said so. Not a piece of the load mesh, which was cut for
        /// the loads and is never told about the Revit floors. And not where either name says
        /// neither: a drop panel typed "DP7 (400x4000x700)" is not claiming to be anything, so
        /// there is no claim to hold the other model's against, and putting every one of them
        /// up for a look would bury the pieces where the two really do disagree.
        /// </summary>
        public bool WasSystemChecked
        {
            get
            {
                return Kind == CheckKind.SlabThickness
                       && EtabsSystem != ConcreteSystem.Unknown
                       && RevitSystem != ConcreteSystem.Unknown;
            }
        }

        public ResultLevel SystemLevel
        {
            get
            {
                if (!WasSystemChecked)
                {
                    return ResultLevel.NotChecked;
                }
                return EtabsSystem == RevitSystem ? ResultLevel.Passed : ResultLevel.Failed;
            }
        }

        /// <summary>One line saying what the PT or RC check found here.</summary>
        public string SystemSummary
        {
            get { return SystemStateSummary + SizeAside(SlabMeshPurpose.Slab, SystemLevel); }
        }

        private string SystemStateSummary
        {
            get
            {
                switch (SystemLevel)
                {
                    case ResultLevel.Passed:
                        return $"{ConcreteSystems.Text(EtabsSystem)} in both models";
                    case ResultLevel.Failed:
                        return $"Revit {ConcreteSystems.Text(RevitSystem)} ({RevitType}), " +
                               $"ETABS {ConcreteSystems.Text(EtabsSystem)} ({EtabsProperty})";
                    default:
                        return "PT or RC not checked";
                }
            }
        }

        /// <summary>
        /// Adds the PT or RC result to the zone. Called from <see cref="BuildResults"/>, and only
        /// where the question was asked.
        /// </summary>
        private void BuildSystemResults()
        {
            ResultLevel level = SystemLevel;
            string etabs = ConcreteSystems.Text(EtabsSystem);
            string revit = ConcreteSystems.Text(RevitSystem);

            resultFields.Add(new ResultField(SlabResultKeys.PtOrRc, "PT or RC",
                $"{revit} / {etabs}", level == ResultLevel.Failed, CheckKind.SlabSystem,
                etabs: etabs, revit: revit));

            outcomes.Add(new CheckOutcome(CheckKind.SlabSystem, level, SystemSummary,
                etabs: etabs, revit: revit));
        }

        #endregion

        public double Area { get { return Plan == null ? 0 : Plan.Area; } }

        #region too small a patch to be work

        /// <summary>
        /// Smallest patch this zone's finding is counted as work on, in mm squared, or zero to
        /// count it whatever its size.
        ///
        /// Set by the check out of the settings, and set differently on the two meshes: the
        /// thickness size on a piece cut for the slab, the load size on a piece cut for the
        /// loads. They are two questions asked of two different shapes, so what is too small to
        /// be worth chasing is answered twice as well.
        /// </summary>
        public double MinimumIssueArea { get; set; }

        /// <summary>
        /// True when this piece really does differ, but is too small a patch of floor for the
        /// difference to be work.
        ///
        /// Nothing about the verdict changes. The piece is cut where it always was, coloured on
        /// the plan in the colour of what was found, opens a card that says what differs, and
        /// can be reviewed, accepted and noted like any other. What it stops doing is being
        /// added up - the chips, the level rail and the drawer count what is left to do, and a
        /// hand's width of slab reading 5 mm out is a difference worth seeing rather than a job.
        /// Left counted, a hundred of them are how a level with three things wrong with it
        /// reads as a level with a hundred and three.
        ///
        /// Only the checks the sizes were written for. A patch one model has and the other has
        /// not is a different question, and the slivers two outlines drawn to one grid line
        /// leave behind are already <see cref="PrefrencesData.SlabMinimumZoneArea"/>'s to throw
        /// away rather than this one's to quieten.
        ///
        /// The PT or RC check goes by the thickness size. It is asked of the same pieces, and
        /// a sliver where a PT floor's edge runs a hand's width onto an RC shell is the same
        /// drafting noise whichever of the two questions it is answering.
        /// </summary>
        public bool IsTooSmallToCount
        {
            get
            {
                if (MinimumIssueArea <= 0 || Area >= MinimumIssueArea)
                {
                    return false;
                }

                return Purpose == SlabMeshPurpose.Loads
                    ? IsWork(LoadLevel)
                    : Kind == CheckKind.SlabThickness && (IsWork(Level) || IsWork(SystemLevel));
            }
        }

        /// <summary>A verdict that would be counted as something left to do.</summary>
        private static bool IsWork(ResultLevel level)
        {
            return level == ResultLevel.Failed || level == ResultLevel.Review;
        }

        /// <summary>
        /// Said on the end of the verdict, so a patch coloured on the plan and absent from
        /// every count explains the difference itself rather than looking like a miscount.
        ///
        /// Asked for by the mesh it belongs to, because a zone carries a line for each check
        /// and only one of them is the check the size was measured against: a piece of the load
        /// mesh says nothing about thickness, and the line where it says so must not pick up a
        /// sentence about what is not being counted. And only on a line that is itself a
        /// finding: a piece can be quietened by its PT or RC verdict while its thickness agrees,
        /// and "200 in both models - not counted" would be explaining a count nobody made.
        /// </summary>
        private string SizeAside(SlabMeshPurpose asking, ResultLevel verdict)
        {
            return IsTooSmallToCount && Purpose == asking && IsWork(verdict)
                ? $" - not counted: {SquareMetres(Area)} m2 is under the "
                  + $"{SquareMetres(MinimumIssueArea)} m2 this check counts from"
                : "";
        }

        private static string SquareMetres(double squareMillimetres)
        {
            return (squareMillimetres / 1e6).ToString("0.##", CultureInfo.CurrentCulture);
        }

        #endregion

        #region inside a column or a wall

        /// <summary>
        /// The column or wall this piece lies inside, named as the user would know it, or empty
        /// when it lies inside neither - or when nothing was found wrong with it, since a piece
        /// that agrees has nothing to accept. Set by the check, see
        /// <see cref="SlabSupports.Holding"/>.
        /// </summary>
        public string InsideSupport { get; set; } = "";

        /// <summary>
        /// True when what this piece found is inside a column or a wall, and so taken as
        /// accepted without anybody having to say so.
        ///
        /// The concrete there is the column or the wall. Whatever the slab check reads inside
        /// one - a rigid zone a thousand thick, the hole the column cuts in the Revit floor, the
        /// ETABS slab running on through the wall - is how the two models draw a support rather
        /// than a slab that is wrong, and it came back at every column on every level. Like an
        /// acceptance, the piece stays on the plan and still opens a card saying what differs;
        /// it is drawn as settled and leaves the counts and the worklist.
        /// </summary>
        public bool IsAcceptedAsInsideSupport
        {
            get { return Purpose == SlabMeshPurpose.Slab && !string.IsNullOrEmpty(InsideSupport); }
        }

        private string SupportAside
        {
            get
            {
                return IsAcceptedAsInsideSupport
                    ? $" - accepted automatically: inside {InsideSupport}"
                    : "";
            }
        }

        #endregion

        public IList<ResultField> ResultFields { get { return resultFields; } }

        public IList<CheckOutcome> Outcomes { get { return outcomes; } }

        /// <summary>Difference in thickness in mm, signed so Revit thicker reads positive.</summary>
        public double ThicknessDelta { get { return RevitThickness - EtabsThickness; } }

        /// <summary>The check this zone's verdict belongs to, which is what the filters work on.</summary>
        public CheckKind Kind
        {
            get
            {
                switch (State)
                {
                    case SlabZoneState.Matched:
                    case SlabZoneState.ThicknessMismatch:
                    case SlabZoneState.ThicknessReview:
                        return CheckKind.SlabThickness;
                    case SlabZoneState.MissingInRevit:
                        return CheckKind.SlabMissingInRevit;
                    case SlabZoneState.MissingInEtabs:
                        return CheckKind.SlabMissingInEtabs;
                    case SlabZoneState.OpeningInRevitOnly:
                        return CheckKind.SlabOpeningInRevit;
                    case SlabZoneState.OpeningInEtabsOnly:
                        return CheckKind.SlabOpeningInEtabs;
                    default:
                        return CheckKind.None;
                }
            }
        }

        public ResultLevel Level
        {
            get
            {
                switch (State)
                {
                    case SlabZoneState.Matched:
                        return ResultLevel.Passed;
                    case SlabZoneState.ThicknessReview:
                    case SlabZoneState.Unresolved:
                        return ResultLevel.Review;
                    case SlabZoneState.NotChecked:
                        return ResultLevel.NotChecked;
                    default:
                        return ResultLevel.Failed;
                }
            }
        }

        /// <summary>One line saying what this zone is, used as its status text and its title.</summary>
        public string Summary
        {
            get
            {
                return StateSummary + (IsAcceptedAsInsideSupport
                    ? SupportAside
                    : SizeAside(SlabMeshPurpose.Slab, Level));
            }
        }

        private string StateSummary
        {
            get
            {
                switch (State)
                {
                    case SlabZoneState.Matched:
                        return $"{Millimetres(EtabsThickness)} in both models";
                    case SlabZoneState.ThicknessMismatch:
                        return $"Revit {Millimetres(RevitThickness)}, ETABS {Millimetres(EtabsThickness)} " +
                               $"({(ThicknessDelta > 0 ? "+" : "")}{Millimetres(ThicknessDelta)})";
                    case SlabZoneState.ThicknessReview:
                        return $"the Revit floor is not flat here, so its {Millimetres(RevitThickness)} " +
                               $"cannot be measured against the ETABS {Millimetres(EtabsThickness)}";
                    case SlabZoneState.MissingInRevit:
                        return "ETABS has slab here and Revit has no floor: the two outlines do not agree";
                    case SlabZoneState.MissingInEtabs:
                        return "Revit has a floor here and ETABS has no slab: the two outlines do not agree";
                    case SlabZoneState.OpeningInRevitOnly:
                        return "opening in Revit, no opening in ETABS";
                    case SlabZoneState.OpeningInEtabsOnly:
                        return "opening in ETABS, no opening in Revit";
                    case SlabZoneState.Unresolved:
                        return string.IsNullOrEmpty(Note) ? "could not be checked" : Note;
                    default:
                        return "not checked";
                }
            }
        }

        /// <summary>How this zone is named in the selection panel.</summary>
        public string Identifier
        {
            get
            {
                if (!string.IsNullOrEmpty(EtabsName))
                {
                    return EtabsName;
                }
                return RevitId > 0 ? RevitId.ToString() : "zone";
            }
        }

        /// <summary>
        /// Fills in the named results the viewer can stamp on the plan, and the single outcome
        /// the filters switch on and off. Called once the zone is complete.
        /// </summary>
        public void BuildResults()
        {
            resultFields.Clear();
            outcomes.Clear();

            if (Purpose == SlabMeshPurpose.Loads)
            {
                BuildLoadPieceResults();
                return;
            }

            resultFields.Add(new ResultField(SlabResultKeys.State, "Slab check", Summary,
                Level == ResultLevel.Failed, Kind));

            if (!string.IsNullOrEmpty(EtabsName))
            {
                resultFields.Add(new ResultField(SlabResultKeys.EtabsName, "ETABS area", EtabsName));
            }
            if (!string.IsNullOrEmpty(EtabsProperty))
            {
                resultFields.Add(new ResultField(SlabResultKeys.EtabsProperty, "ETABS property", EtabsProperty));
            }
            if (RevitId > 0)
            {
                resultFields.Add(new ResultField(SlabResultKeys.RevitId, "Revit id", RevitId.ToString()));
            }
            if (!string.IsNullOrEmpty(RevitType))
            {
                resultFields.Add(new ResultField(SlabResultKeys.RevitType, "Revit floor type", RevitType));
            }
            if (RevitSource == Slab_R.FromSketch)
            {
                resultFields.Add(new ResultField(SlabResultKeys.RevitSource, "Revit floor read from",
                    "its sketch - Revit gave no solid for it"));
            }

            bool thicknessFailed = State == SlabZoneState.ThicknessMismatch;
            resultFields.Add(new ResultField(SlabResultKeys.Thickness, "Thickness",
                $"{Millimetres(RevitThickness)} / {Millimetres(EtabsThickness)}", thicknessFailed,
                CheckKind.SlabThickness,
                etabs: Millimetres(EtabsThickness), revit: Millimetres(RevitThickness)));

            if (RevitThickness > 0 && EtabsThickness > 0)
            {
                resultFields.Add(new ResultField(SlabResultKeys.ThicknessDelta, "Thickness difference",
                    $"{(ThicknessDelta > 0 ? "+" : "")}{Millimetres(ThicknessDelta)}", thicknessFailed,
                    CheckKind.SlabThickness));
            }

            resultFields.Add(ResultField.Number(SlabResultKeys.Area, "Zone area (m2)", Area / 1e6, 2));

            if (IsStackedInRevit)
            {
                // Which floor answered, where it stands and what it displaced, when that was
                // worked out - a 600 nobody can see in the plan is found from this.
                resultFields.Add(new ResultField(SlabResultKeys.Stacked, "Revit stack",
                    string.IsNullOrEmpty(Note) ? "floor plus a drop or a second floor below it" : Note));
            }

            // The two depths go in their own columns rather than only into the sentence: the
            // card puts them under ETABS and Revit headings, which is where the eye compares
            // two numbers. Only the thickness check has a pair to show - an outline or an
            // opening is one model having something the other has not, and there is no second
            // number to print against it.
            bool hasDepths = Kind == CheckKind.SlabThickness
                             && (EtabsThickness > 0 || RevitThickness > 0);
            outcomes.Add(new CheckOutcome(Kind, Level, Summary,
                etabs: hasDepths ? Millimetres(EtabsThickness) : null,
                revit: hasDepths ? Millimetres(RevitThickness) : null));

            if (WasSystemChecked)
            {
                BuildSystemResults();
            }

            if (WasLoadChecked)
            {
                BuildLoadResults();
            }

            CheckSwitches.RemoveSwitchedOff(outcomes);
        }

        /// <summary>
        /// A thickness in mm. Zero means the model has no slab at all here rather than a slab
        /// of no depth, so it reads as a dash: "200 / -" says which model is missing it, where
        /// "200 / 0" would look like a thickness someone got wrong.
        /// </summary>
        private static string Millimetres(double value)
        {
            return Math.Abs(value) < 0.5d ? "-" : Math.Round(value).ToString(CultureInfo.CurrentCulture);
        }

        public override string ToString()
        {
            return $"{State} {Identifier}: {Summary}";
        }
    }

    /// <summary>Keys the viewer uses to pick which slab results get stamped on the plan.</summary>
    public static class SlabResultKeys
    {
        public const string State = "Slab.State";
        public const string EtabsName = "Slab.EtabsName";
        public const string EtabsProperty = "Slab.EtabsProperty";
        public const string RevitId = "Slab.RevitId";
        public const string RevitType = "Slab.RevitType";
        public const string RevitSource = "Slab.RevitSource";
        public const string Thickness = "Slab.Thickness";
        public const string ThicknessDelta = "Slab.ThicknessDelta";
        public const string PtOrRc = "Slab.PtOrRc";
        public const string Area = "Slab.Area";
        public const string Stacked = "Slab.Stacked";

        public const string LoadState = "Slab.LoadState";
        public const string LoadNumber = "Slab.LoadNumber";
        public const string LoadDescription = "Slab.LoadDescription";
        public const string LoadSdl = "Slab.LoadSdl";
        public const string LoadLl = "Slab.LoadLl";
        public const string LoadLr = "Slab.LoadLr";
        public const string LoadMep = "Slab.LoadMep";
        public const string LoadShells = "Slab.LoadShells";
        public const string LoadPlan = "Slab.LoadPlan";
        public const string LoadRegion = "Slab.LoadRegion";
    }
}
