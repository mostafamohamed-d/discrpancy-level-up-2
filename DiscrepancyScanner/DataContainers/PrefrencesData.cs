using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    public class PrefrencesData
    {
        public double LocationTolerance = 2000;
        public double LengthTolerance = 1000;

        #region beam pairing

        /// <summary>
        /// Pair beams on their two end points rather than on their midpoints. Two ends fix the
        /// direction and the length as well, so <see cref="LocationTolerance"/> and
        /// <see cref="LengthTolerance"/> above, and the angle check that went with them, are
        /// not used in this mode; <see cref="BeamPlanTolerance"/> and
        /// <see cref="BeamVerticalTolerance"/> take their place. See
        /// <see cref="EndpointCompareEngine"/>.
        /// </summary>
        public bool UseEndpointPairing = true;

        /// <summary>
        /// How far apart, in mm, two beam ends may sit in plan and still be the same end.
        /// Measured as a radius from the ETABS end, so it reads the same in every direction.
        /// </summary>
        public double BeamPlanTolerance = 1000;

        /// <summary>
        /// How far apart, in mm, two beam ends may sit in level and still be the same end.
        /// Kept apart from the plan buffer because a model is usually far better placed in
        /// plan than it is in level, where a structural depth or an offset moves a beam.
        /// </summary>
        public double BeamVerticalTolerance = 2000;

        /// <summary>
        /// How far, in mm, a paired beam's width or depth may differ between the two models and
        /// still count as the same section. Five is what the check always used.
        /// </summary>
        public double BeamSizeTolerance = 5;

        #endregion

        #region column check tolerances

        /// <summary>
        /// How far, in degrees, a frame may lean off plumb and still be read as a column when
        /// the ETABS model is imported.
        ///
        /// ETABS hands back where a frame's two ends are and nothing that says whether it
        /// meant a column or a beam, so the import decides from the ends. The rule used to be
        /// the rise alone - ends more than 50mm apart in level meant column - and that reads
        /// every inclined beam in the model as a column: a ramp beam, a stair beam, a roof
        /// beam, a six metre beam falling sixty millimetres to a drain. A rise says nothing
        /// without the plan run it is spread over, so the two are measured together as an
        /// angle, and this is where that angle is cut.
        ///
        /// Thirty degrees sits in the gap the two kinds of member leave: a raking column leans
        /// ten or twenty, a stair flight sixty or more. Raise it towards the 60 degree ceiling
        /// for a model whose columns rake hard, lower it towards nought for one where the only
        /// columns are plumb and a steep beam is being taken for one. A frame whose ends are
        /// within 50mm of each other in level is a beam whatever this says.
        ///
        /// See <see cref="ModelIndex.IsColumn"/>.
        /// </summary>
        public double ColumnMaxLeanFromVertical = 30;

        /// <summary>Plan buffer, in mm, inside which an ETABS and a Revit column count as the same column.</summary>
        public double ColumnLocationTolerance = 400;

        /// <summary>Orientation buffer, in degrees, for a column to count as correctly rotated.</summary>
        public double ColumnOrientationTolerance = 5;

        /// <summary>Section dimension buffer, in mm, for width / depth / diameter.</summary>
        public double ColumnDimensionTolerance = 1;

        /// <summary>
        /// How far, in mm, the pairing pass will reach to find a counterpart once the
        /// location buffer has failed. Anything found between the buffer and this radius is
        /// reported as a location mismatch rather than as an unmatched column.
        /// </summary>
        public double ColumnSearchRadius = 1000;

        /// <summary>
        /// Draw the other model's column beside a paired column the two models disagree about,
        /// so a difference in size, in place or in rotation can be seen rather than only read.
        ///
        /// A paired column is drawn once, as the Revit section, coloured by how it did. That
        /// says a column is wrong and says nothing about how: a column 300mm out and a column
        /// turned 30 degrees are the same red rectangle. Where a geometric check has failed,
        /// this draws the ETABS section under the Revit one as an outline, and the difference
        /// is the gap between the two.
        ///
        /// Only where something failed, and only the geometric checks - a type mark that does
        /// not match its groups is not a thing two outlines could show. Columns that passed are
        /// drawn once as before, or the plan would be twice as much line for nothing.
        /// </summary>
        public bool DrawBothColumnsOnMismatch = true;

        #endregion

        #region wall check

        /// <summary>
        /// How far, in mm, an ETABS wall panel may stand off the line its Revit wall runs
        /// along and still count as being in the right place.
        ///
        /// Measured at both ends of the panel, so this covers a wall moved sideways and a wall
        /// drawn at an angle with the one number. Wider than the column buffer on purpose: the
        /// two models routinely dimension a wall to different faces, and half a wall thickness
        /// of difference is a modelling convention rather than a mistake.
        /// </summary>
        public double WallLocationTolerance = 100;

        /// <summary>Thickness buffer, in mm, for two walls to count as the same thickness.</summary>
        public double WallThicknessTolerance = 1;

        /// <summary>
        /// How far, in mm, the pairing will reach to find a counterpart once the location
        /// buffer has failed. A wall found between the buffer and this is reported as being in
        /// the wrong place rather than as a wall the other model has not got.
        /// </summary>
        public double WallSearchRadius = 1000;

        /// <summary>
        /// How far out of parallel, in degrees, two walls may be and still be the same wall.
        /// Folded into 0..90, so a wall drawn from the other end reads as the same wall.
        /// </summary>
        public double WallAngleTolerance = 15;

        /// <summary>
        /// How much of two walls has to run alongside each other, in mm, before they can be
        /// paired - never more than half the length of the shorter one, so a short panel is not
        /// held to a length it does not have.
        ///
        /// This is what tells a wall from the wall on the other side of a corridor: being near
        /// in perpendicular distance means nothing if the two do not actually stand alongside.
        /// </summary>
        public double WallMinimumOverlap = 300;

        /// <summary>
        /// How much shorter than its Revit wall the ETABS may run, in mm, before the wall is
        /// warned about.
        ///
        /// The plan draws the ETABS walls, so a Revit wall that ETABS only half covers would
        /// otherwise be half a wall on the drawing with nothing to say the other half exists.
        /// Rather than drawing both models over each other, the wall carries a warning saying
        /// how much of it ETABS actually stands along.
        ///
        /// Measured as the stretch of the Revit wall covered, so panels stacked up its height
        /// count once rather than once each, and a gap between two panels is not counted.
        /// </summary>
        public double WallLengthTolerance = 500;

        /// <summary>
        /// How much of a Revit wall ETABS may leave alone, in mm, before the stretch it leaves
        /// is drawn on the plan in its own right.
        ///
        /// The warning above says a wall is short; this is where being short is worth seeing.
        /// Past this, the stretches nothing stands along are drawn as the pieces of Revit wall
        /// they are - not the whole wall, which would only be laid over the panels that are
        /// already there. Set it at or above the warning buffer: below it, walls would be drawn
        /// that were never warned about.
        /// </summary>
        public double WallUncoveredDrawLength = 1000;

        /// <summary>
        /// Pair a column in one model with a wall in the other where both stand in the same
        /// place, rather than reporting the two as unpaired. Off by default: most projects keep
        /// the two kinds apart, and pairing across them there would only hide a real omission.
        /// See <see cref="CrossPairEngine"/>.
        /// </summary>
        public bool CrossPairColumnsAndWalls = false;

        /// <summary>
        /// Leave out Revit walls that are not marked as bearing or shear walls, which is how a
        /// partition is kept from reading as a wall ETABS is missing.
        /// </summary>
        public bool StructuralWallsOnly = true;

        /// <summary>
        /// Shortest Revit wall worth checking, in mm. Below this a wall is a jamb, a fin or a
        /// leftover sliver rather than something ETABS was ever going to model.
        /// </summary>
        public double WallMinimumLength = 200;

        /// <summary>
        /// A Revit wall whose name contains any of these is left out of the wall check
        /// altogether. Both the wall's own name and its type name are looked at, and the match
        /// is anywhere in the name and without regard to case.
        ///
        /// Comma separated rather than comma or space separated, unlike the floor list: a name
        /// here may have a space in it. "Second Cast" is why - it is two words and neither of
        /// them can be matched on its own, since "Cast" alone would take out every
        /// "Cast In Situ" wall in the model. See <see cref="SplitTypeNamePhrases"/>.
        /// </summary>
        public string WallTypeNamesToIgnore = "Partition, Blockwork, Cladding, Upstand, Second Cast, non, planter, pool";

        /// <summary>
        /// Take a Revit wall's thickness from the structural layers of its type rather than
        /// from its whole width.
        ///
        /// A wall type carries its finishes as layers, and the whole width would read those as
        /// structure - a 200 wall with 20 of plaster either side reads as 240 and fails against
        /// the ETABS 200 every time. Only the layers the type marks as structure are counted; a
        /// type that marks nothing gets its whole width, since a wall with no structural layer
        /// named is far more often a single concrete layer left unflagged than a wall with no
        /// concrete in it.
        /// </summary>
        public bool ReadWallThicknessFromStructuralLayers = true;

        /// <summary>
        /// Parameter names searched, in order, for the mark a wall's type carries. Read and
        /// reported rather than checked: it is what the reinforcement check will be keyed on.
        /// </summary>
        public string WallTypeMarkParameterNames = "Wall Type Mark, Type Mark, Mark";

        /// <summary>
        /// Read a Revit wall that meets another one as running to the point their two centre
        /// lines cross, wherever it was actually drawn to.
        ///
        /// Revit leaves the corner to whoever drew it - one wall taken the full width and the
        /// other butted into its face, both mitred to meet, one run through to the far face -
        /// and ETABS meshes the joint on the crossing whatever the drawing did. Left alone
        /// that is half a wall thickness of length and half a wall thickness of position out
        /// at every corner in the building, none of it a difference anyone would want
        /// reported. See <see cref="WallCorners"/>.
        /// </summary>
        public bool SnapWallsToCorners = true;

        /// <summary>
        /// How far past half the other wall's thickness the corner search reaches, in mm.
        ///
        /// The half thickness is the convention itself: a wall butted into another one's face
        /// stops exactly that far short of the crossing, and one run through to its far face
        /// goes exactly that far past it. This is the drafting on top of it - a wall taken to
        /// the finish face of a type whose structure is narrower than its width, an end left a
        /// few millimetres out. Keep it small: past this a wall stopping short of the one it
        /// should meet is a gap the check is there to find, not a corner drawn a second way.
        /// </summary>
        public double WallCornerBuffer = 50;

        #endregion

        #region slab check

        /// <summary>
        /// Thickness buffer, in mm. A piece of slab whose two models are further apart than
        /// this is the thing the check colours red.
        /// </summary>
        public double SlabThicknessTolerance = 1;

        /// <summary>
        /// Smallest patch of floor a thickness difference is counted as work on, in m2.
        ///
        /// Not the same thing as <see cref="SlabMinimumZoneArea"/>, which is far smaller and
        /// throws the piece away: this keeps it, colours it and files it exactly as before, and
        /// only stops it being added up. A hand's width of slab that reads 5 mm out is a real
        /// difference and worth seeing on the plan, but it is not a job, and a hundred of them
        /// standing in the counts is how a level that has three things wrong with it reads as a
        /// level with a hundred and three.
        ///
        /// Measured on the piece, which is the whole shell wherever the two models agreed all
        /// the way across it. Zero counts everything, which is what this always did.
        /// </summary>
        public double SlabThicknessMinimumIssueArea = 0.5;

        /// <summary>
        /// Reads the slab as solid through the columns and walls of both models, and accepts
        /// whatever the slab check still finds inside one.
        ///
        /// The concrete inside a column or a wall is the support's, and the two models draw it
        /// differently as a matter of course: a rigid zone or stiff area a thousand thick in
        /// ETABS, a hole the column cuts in the Revit floor. Read as slab it is a thickness or
        /// opening difference at every column on every level. On, the holes are filled, ETABS
        /// shells lying inside a column or wall are read as the support, and any piece still
        /// found wrong inside one is accepted automatically - kept on the plan, drawn as settled,
        /// and left out of the counts. Off checks the slab there like anywhere else.
        /// </summary>
        public bool SlabAcceptInsideColumnsAndWalls = true;

        /// <summary>
        /// How near the story plane a Revit floor's top face has to sit to be read as that
        /// story's slab, in mm.
        ///
        /// An ETABS area is drawn on one plane and its thickness hangs below it, which in
        /// normal practice is the top of the slab, so that is the face the two models are
        /// registered on. Keep this well under a story height and comfortably over whatever
        /// the top of a floor is offset by in the Revit model.
        /// </summary>
        public double SlabTopElevationTolerance = 3000;

        /// <summary>
        /// How far below the story plane to look for a drop, in mm.
        ///
        /// A drop panel drawn as a second floor hung under the slab has its top face a slab
        /// thickness below the story plane, so it would be missed entirely if only the floors
        /// at the plane were read. It is the thickest floor covering a point that answers for
        /// it, whichever of the two it turns out to be.
        /// </summary>
        public double SlabStackSearchDepth = 1500;

        /// <summary>
        /// Smallest piece of slab worth reporting, in mm squared. Two outlines drawn to the
        /// same grid line rarely land on exactly the same coordinate, and without a floor here
        /// every one of those edges would be reported as a mismatch.
        /// </summary>
        public double SlabMinimumZoneArea = 10000;

        /// <summary>
        /// Narrowest piece of slab worth reporting, in mm. Works with the area above rather
        /// than instead of it: a hairline running the length of a long edge can be wide enough
        /// in area to pass that test on its own.
        /// </summary>
        public double SlabMinimumZoneWidth = 20;

        /// <summary>
        /// How near the slab edge a mismatched piece has to run, in mm, to be read as the two
        /// outlines disagreeing rather than as an opening one model has and the other has not.
        /// </summary>
        public double SlabEdgeTolerance = 25;

        /// <summary>
        /// Read a floor Revit gives no solid for off its sketch instead: its outline from the
        /// sketch with the openings cut through it taken out, its thickness from its type's
        /// structural layers, its top at its level plus its offset. Flat floors only - a sloped
        /// or shape edited floor is still left out. A floor cut by something its sketch cannot
        /// show is put up for review rather than passed or failed. Off by default while it is
        /// on trial; while on, every floor that does have a solid is also checked against its
        /// sketch, and the import report says how many agree.
        /// </summary>
        public bool FloorSketchFallback = false;

        /// <summary>
        /// Flatness buffer, in mm, for an ETABS area to be read as a floor. A ramp compared on
        /// its plan outline against a thickness measured vertically would be wrong in a way
        /// that is hard to see, so a sloped area is left out of the check instead.
        /// </summary>
        public double SlabFlatnessTolerance = 50;

        /// <summary>
        /// Check a ribbed or a waffle area on its full section depth rather than on its
        /// topping. Off suits the usual arrangement, where Revit carries the topping as a
        /// floor and the ribs as beams.
        /// </summary>
        public bool CompareRibbedSlabsByOverallDepth = false;

        /// <summary>
        /// Take a Revit floor's thickness from its type rather than by measuring the concrete
        /// between its two faces.
        ///
        /// The type is the more reliable of the two and is what ETABS is actually being
        /// compared against. Only the layers the type marks as structure are counted, so a
        /// screed or a finish carried in the same floor does not read as extra slab; and
        /// because nothing is measured, a floor whose faces are warped - shape edited, given a
        /// fall, or built as a ramp - reads its thickness as easily as a flat one, where
        /// measuring gives a different answer at every point on it.
        ///
        /// Turn this off to go back to measuring, which is worth doing when the model's floor
        /// types are not to be trusted and the geometry is.
        /// </summary>
        public bool ReadSlabThicknessFromType = true;

        /// <summary>
        /// Write a file recording every piece of geometry the check could not work with: what
        /// was being attempted, what the kernel said, what is wrong with the outline as far as
        /// the ring tests can tell, and the coordinates themselves.
        ///
        /// Off by default. The one line issues say that a floor was refused; this says which
        /// vertex, and gives the shape back so it can be drawn out or fed in again.
        /// </summary>
        public bool WriteGeometryDump = false;

        /// <summary>
        /// Saves what the slab check reads for the level on show - the ETABS shells, the Revit
        /// floors with their holes, the columns and walls, the settings - to a file beside the
        /// user settings each time the slab check runs, so a finding can be run again on it
        /// without Revit or ETABS. See <see cref="SlabCheckInputs"/>. Off by default.
        /// </summary>
        public bool SaveSlabCheckInputs = false;

        /// <summary>
        /// Saves what the line load check reads for the level on show - the drawn lines, the
        /// ETABS frames with their loads, the schedule sheet, the pairings and height zones set
        /// by hand, the settings - to a file beside the user settings each time the line load
        /// check runs. See <see cref="LineLoadCheckInputs"/>. Off by default.
        /// </summary>
        public bool SaveLineLoadCheckInputs = false;

        // ------------------------------------------------------------------------------------
        // Which checks run.
        //
        // Every check on every element type has a switch of its own, all on to start with. This
        // is not the same as the chips across the top of the check window: those choose what is
        // being read on the plan right now and are reached for constantly, while these say what
        // the tool asks of the models at all. A practice that does not model column rotation in
        // Revit wants the orientation check off for good, not off every time it opens the
        // window - and a check that is off publishes nothing, so it cannot colour anything,
        // cannot be counted, and leaves no switch behind in the results panel.

        /// <summary>How far the Revit column stands off the ETABS node.</summary>
        public bool RunColumnLocationCheck = true;
        /// <summary>The rotation of the two column sections against each other.</summary>
        public bool RunColumnOrientationCheck = false;
        /// <summary>Round against rectangular.</summary>
        public bool RunColumnShapeCheck = true;
        /// <summary>The two section sizes against each other.</summary>
        public bool RunColumnDimensionsCheck = true;
        /// <summary>The Revit type mark against the ETABS group assignments.</summary>
        public bool RunColumnTypeMarkCheck = false;

        /// <summary>The Revit beam reinforcement mark against the ETABS frame label.</summary>
        public bool RunBeamMarkCheck = true;

        /// <summary>Whether the ETABS panel sits on the line the Revit wall runs along.</summary>
        public bool RunWallLocationCheck = false;
        /// <summary>The two wall thicknesses against each other.</summary>
        public bool RunWallThicknessCheck = true;

        /// <summary>
        /// Check how far the ETABS panels run against how far the Revit wall runs. Off leaves
        /// the plan saying nothing about a wall ETABS stops short of, which is the one thing a
        /// drawing made from the ETABS model cannot show for itself.
        /// </summary>
        public bool RunWallLengthCheck = true;

        /// <summary>Slab depth where both models have slab.</summary>
        public bool RunSlabThicknessCheck = true;
        /// <summary>
        /// PT against RC where both models have slab, read off the ETABS property name and the
        /// Revit floor type name. See <see cref="ConcreteSystems.Read"/>.
        /// </summary>
        public bool RunSlabSystemCheck = true;
        /// <summary>Slab reaching past the edge of the other model's floor.</summary>
        public bool RunSlabOutlineCheck = true;
        /// <summary>Holes one model has and the other does not.</summary>
        public bool RunSlabOpeningCheck = true;

        /// <summary>
        /// Check the line loads drawn on the loading plan against the distributed loads on the
        /// ETABS frames. Off by default, like the slab load check, because it needs a loading
        /// plan view and a schedule sheet paired to every story before it can say anything.
        /// </summary>
        public bool RunLineLoadCheck = true;

        /// <summary>Read Revit structural foundation slabs as floors too, rafts included.</summary>
        public bool IncludeStructuralFoundations = false;

        /// <summary>
        /// Leave out Revit floors that are not marked structural, which is how an
        /// architectural finish sitting on the slab is kept from reading as extra depth.
        /// </summary>
        public bool StructuralFloorsOnly = false;

        /// <summary>
        /// A Revit floor whose type name contains any of these is left out of the slab check
        /// altogether. Comma separated, matched anywhere in the name and without regard to
        /// case, so "fill" catches "Light Weight Fill Variable-300".
        ///
        /// This is for the floors that are not structure: a fill making up a level, a screed,
        /// a finish. ETABS has nothing for them to be compared against, so left in they read
        /// as slab the ETABS model is missing, and where they sit on the real slab they add
        /// their depth to it. Leave it empty to check every floor.
        /// </summary>
        public string SlabTypeNamesToIgnore = "Fill, plinth, eps, void";

        #endregion

        #region slab load check

        /// <summary>
        /// How far a load on the ETABS shell may differ from what the loading schedule asks
        /// for, in kN/m2, and still count as the same load.
        ///
        /// Small on purpose. A schedule is written to one decimal place and a load set is
        /// typed in from it, so anything past rounding is somebody having typed a different
        /// number rather than the two drifting apart.
        /// </summary>
        public double SlabLoadTolerance = 0.05;

        /// <summary>
        /// Smallest patch of floor a load difference is counted as work on, in m2. The load
        /// twin of <see cref="SlabThicknessMinimumIssueArea"/>, and larger than it for the same
        /// reason the two checks have separate buffers: they are cut on different edges and
        /// asked different questions, so what is too small to be worth chasing differs too. A
        /// sliver of slab left between two loading regions carries whichever of them the middle
        /// of it fell in, and being told about it every time is noise rather than work.
        ///
        /// Zero counts everything, which is what this always did.
        /// </summary>
        public double SlabLoadMinimumIssueArea = 1;

        /// <summary>
        /// Parameter names searched, in order, for the area load number on a filled region of
        /// the loading plan. Comma separated. The instance is searched before the type, and
        /// the match ignores case.
        ///
        /// "Loading Type Mark" leads because that is what the loading plans this was written
        /// for actually use, and it is a type parameter there - one filled region type per
        /// area load, rather than a number typed onto each region. Both are searched either
        /// way, so a project that keeps it on the instance needs no change here.
        ///
        /// This is the one setting the load check cannot work without: it is what ties a patch
        /// of the loading plan to a row of the loading schedule.
        /// </summary>
        public string LoadRegionNumberParameterNames =
            "Loading Type Mark, Area Load Number, Load Number, Load No, Number";

        /// <summary>
        /// What a view name says when the view is a loading plan rather than any other plan of
        /// the same floor. Comma separated; matched anywhere in the name, ignoring case.
        ///
        /// Pairing by name needs this because a floor has more than one plan. "L03" alone
        /// matches the GA plan, the setting-out plan and the loading plan equally, and picking
        /// among them by hand is the work the guess was meant to save. With this, the one that
        /// says what it is wins and the rest are ignored.
        ///
        /// It is a preference rather than a fixed word because it is a drawing office's naming
        /// convention, not a fact about Revit - a job that says "Loading Layout" or writes it in
        /// another language would otherwise be back to pairing forty-seven levels by hand.
        /// </summary>
        public string LoadingPlanViewNamePatterns = "Loading Plan";

        /// <summary>
        /// Text a loading plan view's name must also carry, every piece of it. Comma separated;
        /// matched anywhere in the name, ignoring case, spaces, dashes and underscores.
        ///
        /// The phrases above are alternatives - any one of them says "loading plan". These are
        /// not: a job with a loading plan per tower, or an SDL plan beside a live load plan of
        /// the same floor, has several views that all say "loading plan" and all name the
        /// level, and the one wanted is the one that also says "TOWER A" and "SDL". Empty asks
        /// for nothing more.
        /// </summary>
        public string LoadingPlanViewNameRequired = "";

        /// <summary>
        /// ETABS load pattern names read as the superimposed dead load, which is what the
        /// schedule's TOTAL column carries. Comma separated; a name is matched whole first and
        /// then as a word inside a longer name, and case, spaces and dashes are ignored.
        /// </summary>
        public string SdlLoadPatternNames = "SDL, SIDL, Super Dead, Superimposed Dead";

        /// <summary>ETABS load pattern names read as live load that is not reducible.</summary>
        public string LiveLoadPatternNames = "LL, Live, Live Load";

        /// <summary>ETABS load pattern names read as reducible live load.</summary>
        public string ReducibleLiveLoadPatternNames = "LLR, LR, Live Reducible, Reducible Live, Roof Live";

        /// <summary>ETABS load pattern names read as mechanical and plant live load.</summary>
        public string MepLoadPatternNames = "MEP, MECH, Mechanical, Plant";

        /// <summary>
        /// ETABS and SAFE load pattern names that are none of the four whatever the rows above
        /// read them as. Comma separated, matched the same way, and answered before the four
        /// are asked.
        ///
        /// SIDL_C is here because it is a pattern in its own right and not the SIDL, yet the
        /// name SIDL is sitting inside it, so the match inside a longer name would otherwise
        /// count it as superimposed dead load and hand the slab a total nobody put there. A
        /// pattern named here is reported on the zone as unmapped like any other, so it is left
        /// out of the sum rather than made to disappear.
        ///
        /// The SIDL_C checks' own patterns, <see cref="SidlCLoadPatternNames"/>, are excluded
        /// as well whether or not they are written here - see <see cref="NotSidl"/>.
        /// </summary>
        public string ExcludedLoadPatternNames = "SIDL_C";

        /// <summary>
        /// The pattern names no check reads as the SIDL: the excluded ones, and the ones the
        /// SIDL_C checks read, which are a load of their own and not the SIDL however their
        /// name reads. One list, in the same form as either.
        /// </summary>
        public static string NotSidl(string excluded, string sidlC)
        {
            return string.Join(", ", SplitParameterNames(excluded).Concat(SplitParameterNames(sidlC)));
        }

        // The loading schedule's side of the same mapping: what the workbook calls each load.
        // The ETABS names above are the model author's; these are the loads engineer's, and a
        // schedule headed SDL rather than SIDL, or marking its live loads LR rather than LLR,
        // is the same schedule written by somebody else.

        /// <summary>
        /// What the schedule heads its superimposed dead load with. On the area load sheet it
        /// is the band of breakdown values beside the TOTAL; on the line load sheet it is the
        /// value column itself. Comma separated; a heading that starts with any of them
        /// matches, ignoring case, spaces and dots.
        /// </summary>
        public string ScheduleSidlHeadingNames = "SIDL";

        /// <summary>
        /// What the area load schedule heads the column the superimposed dead load total is in.
        /// Comma separated, matched the way <see cref="ScheduleSidlHeadingNames"/> is.
        /// </summary>
        public string ScheduleTotalHeadingNames = "TOTAL";

        /// <summary>What the area load schedule heads its live load column with.</summary>
        public string ScheduleLiveHeadingNames = "LIVE LOAD";

        /// <summary>
        /// How the schedule marks a live load as plain live load in its narrow LL column. A
        /// marking the three rows do not name, or no marking at all, is read as this one - the
        /// safe way round.
        /// </summary>
        public string ScheduleLiveMarkings = "LL, Live";

        /// <summary>How the schedule marks a live load as reducible.</summary>
        public string ScheduleReducibleLiveMarkings = "LLR, LR, Red, Roof";

        /// <summary>How the schedule marks a live load as mechanical and plant.</summary>
        public string ScheduleMepMarkings = "MECH, MEP, Plant";

        /// <summary>
        /// Read the shell uniform load sets out of the model text file - the .$et ETABS writes
        /// beside the model on every save - rather than out of the model's own tables.
        ///
        /// This is a speed setting and nothing else. Both routes read the same two tables and
        /// give the same answer; the difference is that the table saying which shell carries
        /// which load set has a row per shell, and ETABS hands it over one string at a time
        /// across a COM boundary. On a tower with a hundred and fifty thousand shells the same
        /// rows come off the disk in a fraction of a second.
        ///
        /// On, because it is the faster of the two and it is safe to leave on: a model with no
        /// text file beside it, or one the reader cannot make sense of, goes to the tables on
        /// its own without being asked.
        ///
        /// What it costs is that the text file is written on save, so it is as old as the last
        /// save. The import says how many of the shells it names this model has not got, so a
        /// load that looks wrong can be explained rather than puzzled over.
        /// Turn this off to read only the model that is open.
        /// </summary>
        public bool ReadFromEtabsTextFile = true;

        #endregion

        #region line load check

        /// <summary>
        /// What a line style has to say to be read as a line load off the loading plan.
        /// Comma separated; matched anywhere in the style name, ignoring case and accents.
        ///
        /// A loading plan carries ordinary detail lines as well - hatch boundaries, leaders,
        /// the odd bit of annotation - and without this every one of them would be read as a
        /// load with no number on it and reported. Which is a drawing office's naming
        /// convention rather than a fact about Revit, so it is a setting: a job that calls
        /// them "LL-" or writes them in another language would otherwise be unable to use the
        /// check at all.
        /// </summary>
        public string LineLoadStyleNamePatterns = "Line Load";

        /// <summary>
        /// The ETABS load patterns the line load check reads. Comma separated; matched whole
        /// first and then as a word inside a longer name, with case, spaces and dashes ignored.
        ///
        /// SIDL because that is what the line load schedules are headed and all they hold. The
        /// same frame commonly carries a live load and a wind load as well, and those are not
        /// what the drawing is asking about - counting them would report a correctly loaded
        /// beam as carrying several times what the plan asks for.
        ///
        /// Names in <see cref="ExcludedLoadPatternNames"/> are answered before this, which is
        /// what keeps SIDL_C - a pattern of its own, with SIDL sitting inside its name - out.
        ///
        /// Left empty it reads every downward distributed load whatever pattern it is in, which
        /// is the way to see what is on the frames while the right name is being worked out.
        /// </summary>
        public string LineLoadPatternNames = "SIDL";

        /// <summary>
        /// How much comes off the story height to get the height of the wall a line load
        /// stands over, in mm.
        ///
        /// The gap between two levels is not all wall: the slab and its finish take the bottom
        /// of it. Two hundred is the allowance the schedules this was written for assume, and
        /// it is a round number somebody chose rather than a measurement, which is why it can
        /// be changed.
        /// </summary>
        public double LineLoadHeightReduction = 200;

        /// <summary>
        /// What the load a Revit line asks for is rounded up to, in kN/m, once it has been
        /// worked out. A load on elevation times a wall height comes out as 26.04, and nobody
        /// types that into ETABS: the designer rounds it up to 26.1. Rounding the plan's side
        /// the same way keeps a correctly loaded beam from reading as six hundredths out.
        ///
        /// Up rather than to the nearest, because a load is rounded to the safe side. Zero
        /// leaves the value as it was worked out.
        /// </summary>
        public double LineLoadRoundUpTo = 0.1;

        /// <summary>
        /// Line loads whose schedule description says any of these run the full story height,
        /// with no allowance taken off. Comma separated, matched anywhere in the description,
        /// ignoring case and accents.
        ///
        /// Ignoring accents is not politeness. The schedule this was written against types
        /// FACADE with a cedilla on the C, so a plain comparison misses every facade on the
        /// sheet and quietly takes two hundred off a height that should be full - which comes
        /// out as a load a few per cent light on the lines carrying most of it, and looks
        /// exactly like a correct answer. See <see cref="LineLoadHeight.Fold"/>.
        /// </summary>
        public string FullHeightLineLoadNames = "facade, cladding, glazing";

        /// <summary>
        /// How far a drawn load line may sit off the frame under it and still be read as being
        /// on that frame, in mm.
        ///
        /// Generous next to the wall check's buffer on purpose. A load line is drawn to sit on
        /// the face of the wall it stands for, or along a slab edge, while the frame it lands
        /// on runs down the middle of a beam - so a few hundred between them is the normal
        /// case rather than a mistake, and the check is not asking whether the line is in the
        /// right place. It is asking whether the load is there at all.
        /// </summary>
        public double LineLoadLocationTolerance = 700;

        /// <summary>
        /// How far out the check looks for a frame to put a drawn line against, in mm. Past
        /// this there is nothing under the line and it is reported as a load ETABS has no
        /// frame for.
        /// </summary>
        public double LineLoadSearchRadius = 700;

        /// <summary>
        /// How far a drawn line may run off the frame's direction and still be read as running
        /// along it, in degrees.
        ///
        /// A line past this is still kept where it is inside the search radius, but as a
        /// bundled line rather than an aligned one: its load counts towards the frame's total
        /// and is not compared stretch by stretch, because a line at forty degrees to the
        /// frame has no stretch of it to be compared against.
        /// </summary>
        public double LineLoadAngleTolerance = 20;

        /// <summary>
        /// Walk each drawn line in steps and report only the stretches that differ, rather than
        /// giving the whole line one verdict.
        ///
        /// A fifteen metre line with half a metre wrong is half a metre of finding, not fifteen,
        /// and the value printed for a stretch is the one ETABS carries there - never an average
        /// over the line, which on a load that stops part way along a frame is a number nobody
        /// modelled. A step is always forced where an ETABS frame starts or ends, and where a
        /// load starts or stops along one, so no step straddles two different loads.
        /// </summary>
        public bool LineLoadCheckInSteps = true;

        /// <summary>How far apart the steps along a drawn line are, in mm.</summary>
        public double LineLoadStepLength = 250;

        /// <summary>
        /// The shortest stretch of a difference worth reporting, in mm. The line twin of
        /// <see cref="SlabLoadMinimumIssueArea"/>, and needed for the same reason: where two
        /// drawn lines meet end to end they leave a sliver a few millimetres long, and being
        /// told about it every time is noise rather than work.
        /// </summary>
        public double LineLoadMinimumIssueLength = 250;

        /// <summary>
        /// The shortest line load finding counted as work, in m, or zero to count every one.
        /// The line twin of <see cref="SlabThicknessMinimumIssueArea"/>, and it does the same
        /// thing: a shorter finding is still found, still drawn in the colour of what was found
        /// and still opens a card saying so - it is only left out of the chip counts, the level
        /// rail and the findings list. A line overshooting its frame by a hand's width is worth
        /// seeing and is not a job.
        ///
        /// Measured on what the plan draws: a stretch where a line is walked in steps, the part
        /// of the line that is wrong where it is drawn whole, and the ETABS load itself where
        /// nothing on the plan asks for it. Unlike <see cref="LineLoadMinimumIssueLength"/>,
        /// which decides what the comparison reports at all, this changes nothing about any
        /// verdict.
        /// </summary>
        public double LineLoadMinimumCountedLength = 0.5;

        /// <summary>
        /// How far a load on the ETABS frame may differ from what the schedule asks for, in
        /// kN/m, and still count as the same load.
        ///
        /// Larger than the slab's buffer because the number it is held against is a product
        /// rather than a figure typed off a sheet: a load on elevation times a wall height,
        /// where the height came from two story elevations and a round allowance. A centimetre
        /// of disagreement about where a level sits is a couple of hundredths of a kN/m, and
        /// reporting that as a load difference would bury the real ones.
        /// </summary>
        public double LineLoadTolerance = 0.3;

        /// <summary>
        /// Write down every line on each loading plan the check reads, whatever style it is
        /// drawn in, beside the other diagnostics.
        ///
        /// On, because the one thing that cannot be seen from an empty result is why it is
        /// empty. A line is read as a load only when its style name says so, and a drawing
        /// office whose styles are named differently - a different prefix, a trailing space,
        /// another language - gets a plan with nothing on it and no way to find out what
        /// happened. The dump lists what is actually there, tallied by style name, and says
        /// what the check made of each one.
        ///
        /// It is a few pages per view. Turn it off once the styles are set up and reading.
        /// </summary>
        public bool WriteLineLoadDump = true;

        /// <summary>
        /// Write down, for each level the slab load check reads, everything it compared: the
        /// schedule rows as read, every filled region with its outline and every load number
        /// parameter it carries, and each piece of slab with the shells whose loads were added,
        /// the load number it was held against and what was found. One file per level, beside
        /// the other diagnostics.
        ///
        /// On, because a load check that disagrees everywhere can be the plan, the schedule or
        /// the model, and only what was actually read tells which. Turn it off once the plans
        /// read as expected.
        /// </summary>
        public bool WriteSlabLoadDump = true;

        #endregion

        #region SIDL_C line load check

        /// <summary>
        /// Check the SIDL_C line loads on the ETABS frames against the weight of the Revit walls
        /// and fill strips the wall and slab checks leave out.
        ///
        /// These are not off a loading plan. An upstand, a planter wall or a strip of fill slab
        /// is not structure, so the wall and slab checks drop it - and what it is instead is a
        /// load: its own weight, put on the frame under it as a line load in its own pattern.
        /// Read the same way as the line loads off the plan, frame by frame and stretch by
        /// stretch; only where the Revit side's value comes from is different.
        /// </summary>
        public bool RunSidlCCheck = true;

        /// <summary>
        /// The ETABS or SAFE load patterns the SIDL_C checks - the line one and the area one -
        /// read. Comma separated, and matched whole only: case, spaces, dashes and underscores
        /// are ignored, but a name is never found inside a longer one, so what is read is
        /// exactly the patterns typed here. The pattern is the model author's to name and is
        /// renamed from one model to the next, which is why it is a list.
        ///
        /// Whatever is named here is never read as the SIDL either - not by the area load check
        /// and not by the line load check - just as if it were on
        /// <see cref="ExcludedLoadPatternNames"/>. A pattern renamed here is kept out of the
        /// SIDL without anyone having to remember the second list - unless that check's own list
        /// names it outright, for a model whose wall weights and line loads share one pattern.
        /// </summary>
        public string SidlCLoadPatternNames = "SIDL_C, SIDL_LWC";

        /// <summary>
        /// The patterns the SIDL_C area check reads, for a model that keeps the fill's weight in
        /// a pattern of its own (SDLF) apart from the walls' (SDLP). Empty reads the same as
        /// <see cref="SidlCLoadPatternNames"/>.
        /// </summary>
        public string SidlCAreaLoadPatternNames = "";

        /// <summary>Both SIDL_C checks' patterns, as one list - what is kept out of the SIDL.</summary>
        public string AllSidlCLoadPatternNames
        {
            get { return SidlCLoadPatternNames + ", " + SidlCAreaLoadPatternNames; }
        }

        /// <summary>
        /// A Revit wall whose name contains any of these is read as a SIDL_C load: its own
        /// weight per metre run. The same names the wall check leaves out to begin with,
        /// because those are exactly the walls that are carried as load rather than modelled.
        /// Comma separated, matched anywhere in the wall's own name or its type name.
        /// </summary>
        public string SidlCWallNames = "Partition, Blockwork, Cladding, Upstand, Second Cast, non, planter, pool";

        /// <summary>
        /// A Revit floor whose name contains any of these, and is narrower than
        /// <see cref="SidlCFillSlabMaximumWidth"/>, is read as a SIDL_C line load: a strip of
        /// fill along an edge, whose weight is thickness times width.
        /// </summary>
        public string SidlCFillSlabNames = "Fill SLAB";

        /// <summary>
        /// The widest floor read as a fill strip, in mm. A wider fill is an area of fill, which
        /// is the slab load check's business rather than a line load.
        /// </summary>
        public double SidlCFillSlabMaximumWidth = 500;

        /// <summary>The weight of the walls and fill strips, in kN/m3.</summary>
        public double SidlCConcreteDensity = 25;

        /// <summary>
        /// How far ETABS may carry less than the wall weighs, in kN/m, and still agree. Small:
        /// a load short of the weight is a load short.
        /// </summary>
        public double SidlCToleranceBelow = 0.3;

        /// <summary>
        /// How far ETABS may carry more than the wall weighs, in kN/m, and still agree. Larger,
        /// because a load modelled a little heavy - rounded up, or a finish allowed for - is on
        /// the safe side.
        /// </summary>
        public double SidlCToleranceAbove = 3;

        /// <summary>
        /// How far a wall or fill strip may sit off the frame under it, in mm. Past this it has
        /// no frame under it. The SIDL_C check's own, apart from the line loads off the plan.
        /// </summary>
        public double SidlCLocationTolerance = 700;

        /// <summary>
        /// The lightest wall or fill strip worth checking, in kN/m. Anything that weighs less
        /// is left out of the check.
        /// </summary>
        public double SidlCMinimumLoad = 1;

        /// <summary>
        /// Loads the weight off the geometry is stepped up to, in kN/m, comma separated - "2, 6,
        /// 10, 14, 18": a wall weighing 3 is checked as 6, one weighing more than the last as
        /// what it weighs. Empty, the weight is checked as it is.
        /// </summary>
        public string SidlCLoadIntervals = "";

        /// <summary>
        /// Take a wall's or fill strip's load from its instance comment where the comment states
        /// one - "6", "6 kN/m" - and from its geometry where it does not.
        /// </summary>
        public bool SidlCLoadFromComment = false;

        #endregion

        #region SIDL_C area load check

        /// <summary>
        /// Check the SIDL_C area loads on the ETABS slabs against the weight of the Revit light
        /// weight fill floors over them.
        ///
        /// A fill is not structure and is not on a loading plan: it is modelled in Revit as a
        /// floor, and carried in ETABS as an area load in SIDL_C. A ramp of it has no one depth,
        /// so the engineer averages it - whole, or in pieces - and the check reads the average
        /// back off the slab under it, area by area, however it was split.
        /// </summary>
        public bool RunSidlCAreaCheck = true;

        /// <summary>
        /// A Revit floor whose name contains any of these is weighed as a SIDL_C area load.
        /// Comma separated, matched anywhere in the floor's own name or its type name. The ETABS
        /// patterns read are the SIDL_C line check's, <see cref="SidlCLoadPatternNames"/>, so
        /// SIDL_LWC there is read as the fill's load as well as SIDL_C.
        /// </summary>
        public string SidlCAreaFloorNames = "Light Weight Fill";

        /// <summary>The light weight concrete the fill is made of, in kN/m3.</summary>
        public double SidlCAreaLightweightDensity = 10;

        /// <summary>The topping over the fill, in kN/m3.</summary>
        public double SidlCAreaToppingDensity = 25;

        /// <summary>
        /// The topping, in mm: the top of the fill, which is at the topping density rather than
        /// the light weight one. Counted as the difference between the two over this depth.
        /// </summary>
        public double SidlCAreaToppingThickness = 100;

        /// <summary>
        /// What the fill's weight is rounded up to, in kN/m2, before it is compared: the engineer
        /// types 4.56 into ETABS as 5. Up, never down. Nought leaves it unrounded.
        /// </summary>
        public double SidlCAreaRoundUpTo = 0.5;

        /// <summary>How far ETABS may carry less than the fill weighs, in kN/m2, and still agree.</summary>
        public double SidlCAreaToleranceBelow = 0.5;

        /// <summary>
        /// How far ETABS may carry more than the fill weighs, in kN/m2, and still agree. Larger:
        /// a load a little heavy is on the safe side.
        /// </summary>
        public double SidlCAreaToleranceAbove = 1;

        /// <summary>
        /// How far from a fill with no SIDL_C under it an ETABS area carrying SIDL_C, with no
        /// fill over it, may be and still be read as that fill's load drawn in the wrong place,
        /// in mm. Nought turns it off: the two are then reported apart.
        /// </summary>
        public double SidlCAreaMisplacedDistance = 2000;

        #endregion

        #region single level models

        /// <summary>
        /// How far off its level a Revit element may stand and still be counted as part of it,
        /// in mm, when the run is against a model that holds one floor.
        ///
        /// SAFE models one floor and says nothing about which floor it is, so the level is
        /// paired by hand and everything in the Revit model that stands at that level is what
        /// gets compared. Everything, not the whole building: with one level to be nearest to,
        /// every element in the project would otherwise be named after it.
        ///
        /// A metre and a half because a floor is not drawn on its level. A slab hangs below it,
        /// a beam is dropped for an upstand, a column stops short of it - all of them belong to
        /// the floor and none of them sit on its plane. What it must not reach is the floor
        /// above or below, so anything approaching a storey height is too much; the tightest
        /// thing worth having is a band comfortably inside half a storey.
        /// </summary>
        public double SingleLevelBand = 1500;

        #endregion

        #region column section parameter names

        // The size mappings below are the user's, entry by entry: "b" looks on the type then
        // the instance, "Type:b" on the type only, "Instance:b" on the instance only, several
        // tried in the order typed. Only a parameter Revit keeps as a Length is read. Empty by
        // default - nothing is guessed - so an unmapped size is measured off the solid (see
        // MeasureSizeFromGeometry) or reported. See ParameterMapping.

        /// <summary>The parameter(s) mapped to a round column's diameter.</summary>
        public string ColumnDiameterParameterNames = "";

        /// <summary>The parameter(s) mapped to a rectangular column's width.</summary>
        public string ColumnWidthParameterNames = "";

        /// <summary>The parameter(s) mapped to a rectangular column's depth.</summary>
        public string ColumnDepthParameterNames = "";

        /// <summary>
        /// Parameter names searched, in order, for the mark a column's type carries. This is
        /// what the ETABS frame group assignments are compared against.
        /// </summary>
        public string ColumnTypeMarkParameterNames = "Column Type Mark, Type Mark, Mark";

        /// <summary>The parameter(s) mapped to a beam's width - the same form as the column ones.</summary>
        public string BeamWidthParameterNames = "";

        /// <summary>The parameter(s) mapped to a beam's depth.</summary>
        public string BeamDepthParameterNames = "";

        /// <summary>
        /// When a beam or column carries none of the size parameter names above, measure its
        /// section off its own solid instead - the uncut solid, so a slab joined into a beam
        /// does not take its depth away. Works whatever a family calls its parameters. Where
        /// a member has both, the two are compared and the ones that disagree are listed in
        /// the import report.
        /// </summary>
        public bool MeasureSizeFromGeometry = true;

        /// <summary>
        /// The words to match a floor type name against, from what was typed in the box.
        ///
        /// Split on spaces as well as commas, because that is how anyone types a list of
        /// words. Parameter names cannot be split that way - "Structural Material" is one
        /// name with a space in it - and neither can the wall list, which is why
        /// <see cref="SplitTypeNamePhrases"/> is there; but the floor words have always been
        /// single words and splitting them on spaces is what anyone typing them expects.
        /// </summary>
        public static string[] SplitTypeNameWords(string words)
        {
            if (string.IsNullOrEmpty(words))
            {
                return new string[0];
            }

            char[] separators = { ',', ';', ' ', '\t', '\r', '\n' };
            return words.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(word => word.Trim())
                        .Where(word => word.Length > 0)
                        .ToArray();
        }

        /// <summary>
        /// The names to match a wall name against, from what was typed in the box.
        ///
        /// Split on commas, semicolons and line breaks - not on spaces - so a name keeps the
        /// spaces in it. "Second Cast" is one thing to leave out and not two: split on spaces
        /// it would become "Cast", which drops every wall whose name carries that anywhere in
        /// it. The cost of keeping the spaces is that a list typed with spaces alone reads as
        /// a single long name; the settings box says to separate them with commas.
        /// </summary>
        public static string[] SplitTypeNamePhrases(string names)
        {
            // The same separators, and the same trimming, as the parameter names: both are
            // lists of things that may have a space inside one entry.
            return SplitParameterNames(names);
        }

        /// <summary>
        /// Whether a piece of text carries any of the phrases, saying which one it was.
        ///
        /// The one match every "leave this out" list in the tool is made on: anywhere in the
        /// text, without regard to case. The phrase that answers for the text is the first one
        /// to match rather than all of them, because an element named for two of them was
        /// going to be left out either way and one name against it is what makes the counts
        /// add up.
        /// </summary>
        public static bool MatchesAnyPhrase(string text, string[] phrases, out string matched)
        {
            matched = null;

            if (string.IsNullOrEmpty(text) || phrases == null)
            {
                return false;
            }

            foreach (string phrase in phrases)
            {
                if (string.IsNullOrEmpty(phrase))
                {
                    continue;
                }

                if (text.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matched = phrase;
                    return true;
                }
            }

            return false;
        }

        public static string[] SplitParameterNames(string names)
        {
            if (string.IsNullOrEmpty(names))
            {
                return new string[0];
            }

            char[] separators = { ',', ';', '\r', '\n' };
            return names.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => name.Trim())
                        .Where(name => name.Length > 0)
                        .ToArray();
        }

        /// <summary>
        /// The Revit rectangle family measures "Width" across the family X axis while ETABS
        /// measures T3 (depth) across its local 3 axis, so at the same rotation the Revit
        /// "Width" lines up with the ETABS depth. Turn this off if your family is authored
        /// the other way round.
        /// </summary>
        public bool SwapRevitColumnWidthDepth = true;

        #endregion

        #region beam parameter names

        /// <summary>
        /// Parameter names searched, in order, for the reinforcement mark a beam carries.
        /// Comma separated. The type is searched before the instance and the match ignores
        /// case, the same way the column parameters are read.
        /// </summary>
        public string BeamMarkParameterNames = "Beam Reinf. Mark, Beam Reinf Mark, BEAM MARK";

        #endregion

        #region beams left out of the check

        /// <summary>
        /// A Revit beam whose type name contains any of these is not collected at all. Comma
        /// separated, matched anywhere in the name and without regard to case, and a name may
        /// have a space in it - the same rule as the wall list, see
        /// <see cref="SplitTypeNamePhrases"/>.
        ///
        /// This is for the beams the two models were never going to agree on: a beam Revit
        /// draws that ETABS does not frame has nothing to be compared against, so left in it
        /// reads as a beam ETABS is missing, every time the check is run.
        ///
        /// "RB" ships as the default. It is two letters matched anywhere in a name, so it
        /// takes a "KERB" or a "CURB" type with it, and lengthening it to "RB-" does not help
        /// - "KERB-200" carries that too. Naming the series itself, "RB1", is the way out. The
        /// import says how many beams each name took and which name it was, so an entry that
        /// reaches further than it was meant to shows up rather than going quiet.
        ///
        /// Leave empty to collect every beam.
        /// </summary>
        public string BeamTypeNamesToIgnore = "RB";

        /// <summary>
        /// ETABS frame sections that are not beams: lines drawn in ETABS only to carry a line
        /// load, under a section of their own. A frame whose section name contains any of these
        /// is left out of the beam check - not paired, not drawn after the check - but its loads
        /// are still read, so the line load checks see it as before. Comma separated, matched
        /// anywhere in the name without regard to case. Applied at the check, so it works on a
        /// saved design model file and needs no new read.
        ///
        /// Empty by default: the None section is left out anyway.
        /// </summary>
        public string EtabsBeamSectionsToIgnore = "";

        /// <summary>
        /// ETABS frame sections not read at all, beams and columns alike: dummies, rigid links,
        /// steel sections in a concrete check. Comma separated, matched anywhere in the name
        /// without regard to case. Empty by default, so no template's names are assumed.
        /// </summary>
        public string EtabsSectionsToSkip = "";

        /// <summary>ETABS frame materials whose frames are not read, matched the same way. Empty by default.</summary>
        public string EtabsMaterialsToSkip = "";

        /// <summary>
        /// Revit columns left out of the column check by family or type name - tapered
        /// columns whose section changes up the height, which have no one size to compare.
        /// </summary>
        public string ColumnTypeNamesToIgnore = "Varies";

        /// <summary>
        /// Compare a curved Revit beam by the straight line between its ends. Off leaves curved
        /// beams out of the beam check, since an ETABS frame is straight.
        /// </summary>
        public bool CompareCurvedBeamsByChord = true;

        /// <summary>
        /// Words in an ETABS slab property or Revit floor type name that mark it post-tensioned.
        /// Asked before <see cref="RcSlabNames"/>.
        /// </summary>
        public string PtSlabNames = "PT";

        /// <summary>
        /// Words that mark a slab reinforced, once it is not PT. "S" catches S200 and SLAB. A
        /// name with neither list's words is not checked.
        /// </summary>
        public string RcSlabNames = "RC, S";

        /// <summary>
        /// The same, against the mark rather than the type name: a beam whose reinforcement
        /// mark contains any of these is not collected.
        ///
        /// The mark is the one read through <see cref="BeamMarkParameterNames"/> - the value
        /// shown on the beam and sent out in the Excel export - so this leaves out exactly the
        /// beams that would have been reported under that mark. Kept apart from the type name
        /// list because a project that carries the distinction in the mark rather than in the
        /// type name wants one of these and not the other.
        /// </summary>
        public string BeamMarksToIgnore = "RB";

        #endregion

        #region column type mark against ETABS groups

        /// <summary>
        /// A group is only taken into account by the type mark check when its name contains
        /// one of these, which is how the naming groups are told apart from the housekeeping
        /// ones a model collects. Comma separated, matched without regard to case, and
        /// whitespace is ignored on both sides so "T - 1" reads the same as "T-1".
        ///
        /// Any group that does not contain one of them is dropped before the check, so it can
        /// neither match a column nor make it look like it belongs to more than one group.
        /// Leave this empty to take every group into account.
        /// </summary>
        public string EtabsGroupNamePatterns = "T-, B-, P-";

        #endregion

        // utilication Factors bot (from left to right)
        public double b1Factor = 0.95;
        public double b2Factor = 0.95;
        public double b3Factor = 0.95;

        // utilization factors top (from left to right)

        public double t1Factor = 0.95;
        public double t2Factor = 1.05;
        public double t3Factor = 0.95;

        public double fyForHeighStrength = 400;
        public double fyForMield = 240;

        #region keyboard shortcuts

        // The keys that work anywhere in the check window, one per thing they do. Each is held
        // as it is written - "Page Up", "1", "Ctrl+R", or two keys one after the other, "A, A"
        // - so the settings file reads the way the settings window shows it; see Shortcuts for
        // how one is read and matched. Empty means the thing has no key.

        /// <summary>Steps to the level above.</summary>
        public string ShortcutLevelUp = "Page Up";

        /// <summary>Steps to the level below.</summary>
        public string ShortcutLevelDown = "Page Down";

        /// <summary>Switches the beam tab on or off.</summary>
        public string ShortcutBeams = "1";

        /// <summary>Switches the column tab on or off.</summary>
        public string ShortcutColumns = "2";

        /// <summary>Switches the wall tab on or off.</summary>
        public string ShortcutWalls = "3";

        /// <summary>Switches the slab tab on or off.</summary>
        public string ShortcutSlabs = "4";

        /// <summary>Switches the slab load tab on or off.</summary>
        public string ShortcutSlabLoads = "5";

        /// <summary>Switches the line load tab on or off.</summary>
        public string ShortcutLineLoads = "6";

        /// <summary>Switches the height zone tab on or off.</summary>
        public string ShortcutHeightZones = "7";

        public string ShortcutSidlC = "8";

        public string ShortcutSidlCArea = "9";

        /// <summary>Switches the plan between the check and the raw data of both models.</summary>
        public string ShortcutRawData = "R";

        /// <summary>
        /// Accepts what is selected, or takes the acceptance back - the ACCEPT button on the
        /// card. A letter pressed twice, so a stray key never accepts anything.
        /// </summary>
        public string ShortcutAccept = "A, A";

        /// <summary>Pairs the two elements selected - the PAIR THESE button. Pressed twice, like accepting.</summary>
        public string ShortcutPair = "P, P";

        #endregion


        private static PrefrencesData prefrencesData ;

        internal bool IncludeAttachedLinks = true;
        internal bool IncludeOverlayLinks = true;
        internal double LinksRatio = 0.95;

        private PrefrencesData()
        {
       
        }
        public static PrefrencesData getPrefrencesData()
        {
            if (prefrencesData != null)
            {
                
                
                return prefrencesData;
            }
            prefrencesData = new PrefrencesData();
            return prefrencesData;
        }

    }
}
