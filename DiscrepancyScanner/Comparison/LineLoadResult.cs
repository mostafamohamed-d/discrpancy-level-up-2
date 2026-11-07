using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DiscrepancyScanner
{
    /// <summary>What the line load check made of one drawn load line.</summary>
    public enum LineLoadState
    {
        /// <summary>The check did not run over this line.</summary>
        NotChecked,

        /// <summary>The frames under it carry what the schedule asks for, all the way along.</summary>
        Matched,

        /// <summary>They carry a load, and it is not the one the schedule asks for.</summary>
        ValueMismatch,

        /// <summary>Part of the line has a load under it and part of it has none.</summary>
        PartlyCovered,

        /// <summary>There is a frame under the line and it carries no distributed load at all.</summary>
        NoEtabsLoad,

        /// <summary>Nothing in ETABS runs under this line, so there is nothing to carry it.</summary>
        NoEtabsFrame,

        /// <summary>
        /// The ETABS load under the line is what another line over the same stretch asks for
        /// on its own, so that line takes it - and no other frame along this line carries this
        /// one.
        /// </summary>
        TakenByOther,

        /// <summary>The line style ends in no number, so nothing says which load it is.</summary>
        NoNumber,

        /// <summary>The style's number is not on the schedule sheet this story reads.</summary>
        NotInSchedule,

        /// <summary>
        /// The schedule value could not be turned into a load per metre run - almost always a
        /// load on elevation on the topmost story, which has nothing above it to give a wall
        /// height.
        /// </summary>
        NoExpectedValue
    }

    /// <summary>
    /// One drawn load line, and what the ETABS frames under it had to say.
    ///
    /// The verdict belongs to the drawn line rather than to the frame on purpose: this check
    /// is read on a plan of the loading drawing, and what the person looking at it wants to
    /// know is which of the lines they drew are wrong. The frames are the answer, not the
    /// question, and they are only put on the plan where there is something about one that
    /// cannot be seen from the line alone.
    /// </summary>
    public sealed class LineLoadResult
    {
        public LineLoadResult(LineLoad_R line)
        {
            Line = line;
        }

        public LineLoad_R Line { get; }

        public LineLoadState State { get; set; } = LineLoadState.NotChecked;

        /// <summary>The run of frames this line was read against, or null when nothing was.</summary>
        public LineLoadRun Run { get; set; }

        /// <summary>
        /// Every run this line was read against, in order along it. One, for a line lying along
        /// one run; several, for a line split over the frames it runs across - and then
        /// <see cref="Run"/> is the one most of it lies along. Empty when nothing was.
        /// </summary>
        public List<LineLoadRun> Runs { get; } = new List<LineLoadRun>();

        /// <summary>
        /// Where this line lies on the ETABS frames it was read against: each frame by its
        /// unique name, and the stretch of that frame the line covers. What a load written back
        /// to ETABS is assigned over. Empty on a line nothing in ETABS runs under.
        /// </summary>
        public List<LineLoadPlacement> Placements { get; } = new List<LineLoadPlacement>();

        /// <summary>
        /// True when this line can be written back to the design model: it was paired with an
        /// ETABS frame and it has a value of its own to put there.
        /// </summary>
        public bool CanWriteBack
        {
            get { return Placements.Count > 0 && Placements.All(placement => placement.Value > 0); }
        }

        /// <summary>The frames this line was read against, as a report names them.</summary>
        public string Frames
        {
            get
            {
                List<string> names = (Runs.Count > 0 ? Runs : Run == null ? new List<LineLoadRun>() : new List<LineLoadRun> { Run })
                    .Select(run => run.Identifier)
                    .Where(name => name.Length > 0)
                    .Distinct()
                    .ToList();

                return names.Count <= 4
                    ? string.Join(", ", names)
                    : string.Join(", ", names.Take(4)) + $" and {names.Count - 4} more";
            }
        }

        /// <summary>The schedule row its number looked up, or null when it looked up none.</summary>
        public LineLoadEntry Entry { get; set; }

        /// <summary>What the schedule asks the frame to carry here, in kN/m.</summary>
        public double Expected { get; set; }

        /// <summary>
        /// What the frames carry under this line, in kN/m, as read - never an average. Where
        /// they carry one value, that value; where they carry several, the one furthest from
        /// what the line asks for, which is the one that decides. Every value is in
        /// <see cref="Carried"/>.
        /// </summary>
        public double Actual { get; set; }

        /// <summary>
        /// Every value the ETABS frames carry under this line, exactly as read, with the frame
        /// it is on, in order along the line. A frame is read as it is: a line over a frame
        /// carrying 1.5 and one carrying 3 carries 1.5 and 3, not 2.25. Loads another line took
        /// are not in it, being that line's.
        /// </summary>
        public List<KeyValuePair<string, double>> Carried { get; } = new List<KeyValuePair<string, double>>();

        internal void AddCarried(string frame, double value)
        {
            frame = frame ?? "";
            if (value <= 0 || Carried.Any(one => one.Key == frame && Math.Abs(one.Value - value) < 1e-6))
            {
                return;
            }
            Carried.Add(new KeyValuePair<string, double>(frame, value));
        }

        /// <summary>"1.5 kN/m", or "1.5 / 3 kN/m" where the frames carry several. Empty for nothing.</summary>
        public string CarriedText
        {
            get
            {
                List<string> values = Carried.Select(one => LoadSet.Text(one.Value)).Distinct().ToList();
                if (values.Count == 0)
                {
                    return Actual > 0 ? LoadSet.Text(Actual) + " kN/m" : "";
                }
                return string.Join(" / ", values) + " kN/m";
            }
        }

        /// <summary>"1.5 kN/m on B12, 3 kN/m on B13" - what each frame carries under the line.</summary>
        public string CarriedByFrameText
        {
            get
            {
                if (Carried.Count == 0)
                {
                    return CarriedText;
                }

                List<string> frames = Carried.Select(one => one.Key).Distinct().ToList();
                if (frames.Count == 1)
                {
                    return CarriedText + (frames[0].Length > 0 ? " on " + frames[0] : "");
                }

                return string.Join(", ", frames.Select(frame =>
                    string.Join(" / ", Carried.Where(one => one.Key == frame)
                                                .Select(one => LoadSet.Text(one.Value)).Distinct())
                    + " kN/m" + (frame.Length > 0 ? " on " + frame : "")));
            }
        }

        /// <summary>The working behind <see cref="Expected"/>, including the wall height used.</summary>
        public string How { get; set; } = "";

        /// <summary>
        /// The height zone drawn by hand that gave the line its height instead of its level, in
        /// words; empty where the level's own height was used. See <see cref="HeightZone"/>.
        /// </summary>
        public string HeightZone { get; set; } = "";

        /// <summary>The same zone as the store files it, for counting the lines a zone gives its height to.</summary>
        public string HeightZoneId { get; set; } = "";

        /// <summary>How much of the line has any downward load under it at all, in mm.</summary>
        public double CoveredLength { get; set; }

        /// <summary>The whole length of the drawn line, in mm.</summary>
        public double Length { get { return Line == null ? 0 : Line.Length; } }

        /// <summary>
        /// True when the line could not be laid along the run to be compared stretch by
        /// stretch - a bent or stepped run of lines beside a straight frame - so it was judged
        /// whole, against every value ETABS carries under it as it is.
        /// </summary>
        public bool Bundled { get; set; }

        /// <summary>
        /// How much of the line, in mm, is off by more than the buffer when read stretch by
        /// stretch. Zero on a line that was not.
        /// </summary>
        public double MismatchLength { get; set; }

        /// <summary>
        /// On the stretch furthest off: what the plan asks for there, in kN/m. More than this
        /// line's own value where another drawn line overlaps it on the same frame, because the
        /// plan's side at any point is every line covering it added together.
        /// </summary>
        public double MismatchWanted { get; set; }

        /// <summary>On the stretch furthest off: what the ETABS frame carries there, in kN/m.</summary>
        public double MismatchCarried { get; set; }

        /// <summary>
        /// Where a failed line fails, in words: the average on the card can match the schedule
        /// while one stretch does not, and without this a red line reads as a contradiction.
        /// Empty on a line that is not a value mismatch.
        /// </summary>
        public string MismatchText
        {
            get
            {
                if (State != LineLoadState.ValueMismatch || MismatchLength <= 0)
                {
                    return "";
                }

                string text = $"{Metres(MismatchLength)} of the line: the plan asks for " +
                              $"{LoadSet.Text(MismatchWanted)} kN/m there and ETABS carries " +
                              $"{LoadSet.Text(MismatchCarried)} kN/m";

                if (MismatchWanted > Expected + 1e-6)
                {
                    text += " - another drawn line overlaps this one on the same frame, and the " +
                            "two are added together";
                }
                else if (MismatchCarried > Expected + 1e-6 && Actual > 0)
                {
                    text += " - the frame there carries more than this line asks for";
                }

                return text;
            }
        }

        /// <summary>
        /// The stretches the line reads as, when it was checked in steps: every stretch of it,
        /// the ones that agree as well as the ones that do not, in order along the line. Empty
        /// when it was judged whole - checked in steps with the setting off, judged on its total,
        /// or settled before anything was compared.
        /// </summary>
        public List<LineLoadStretch> Stretches { get; } = new List<LineLoadStretch>();

        /// <summary>
        /// Another ETABS run lying alongside this line and carrying a load no drawn line claims,
        /// said in words. Empty when there is none.
        ///
        /// The line was read against one run and the other was left over, which on the plan is
        /// a line drawn right beside an ETABS-only frame with nothing to connect them: the load is
        /// on two frames, or the line was read against the wrong one of the two.
        /// </summary>
        public string Alongside { get; set; } = "";

        /// <summary>Anything worth saying in words rather than in numbers.</summary>
        public string Note { get; set; } = "";

        /// <summary>The colour this line is drawn in.</summary>
        public ResultLevel Level
        {
            get
            {
                switch (State)
                {
                    case LineLoadState.Matched:
                        return ResultLevel.Passed;

                    case LineLoadState.ValueMismatch:
                    case LineLoadState.PartlyCovered:
                    case LineLoadState.NoEtabsLoad:
                    case LineLoadState.NoEtabsFrame:
                    case LineLoadState.TakenByOther:
                        return ResultLevel.Failed;

                    case LineLoadState.NotChecked:
                        return ResultLevel.NotChecked;

                    // Everything left is something the tool cannot settle on its own: the
                    // totals agree while the stretches do not, the line carries no number,
                    // the number is not on the sheet, the height is not knowable. None of
                    // them is a wrong model and none of them is a right one.
                    default:
                        return ResultLevel.Review;
                }
            }
        }

        /// <summary>One line saying what the check found here.</summary>
        public string Summary
        {
            get { return StateSummary + Aside; }
        }

        private string StateSummary
        {
            get
            {
                switch (State)
                {
                    case LineLoadState.Matched:
                        return $"{Name} matches: {CarriedText}";

                    case LineLoadState.ValueMismatch:
                        return MismatchLength > 0 && MismatchLength < Length - 1
                            ? $"{Name} does not match along {Metres(MismatchLength)} of it: " +
                              $"{Asker} {LoadSet.Text(MismatchWanted)} kN/m there and " +
                              $"the ETABS frame carries {LoadSet.Text(MismatchCarried)} kN/m" + Working
                            : $"{Name} does not match: {(Weighed ? Asker : "the schedule asks for")} " +
                              $"{LoadSet.Text(Expected)} kN/m and the ETABS frame carries " +
                              CarriedText + Working;

                    case LineLoadState.PartlyCovered:
                        return $"{Name} is on {Metres(CoveredLength)} of this " +
                               $"{Metres(Length)} line and the rest of it carries nothing";

                    case LineLoadState.NoEtabsLoad:
                        return $"{Name} is {Placed} here and the ETABS frame under it " +
                               "carries no distributed load at all";

                    case LineLoadState.NoEtabsFrame:
                        return $"{Name} is {Placed} here and ETABS has no frame under it " +
                               "to carry it";

                    case LineLoadState.TakenByOther:
                        return $"the ETABS load under {Name} is what another line drawn over it " +
                               "asks for, so that line takes it - and no other frame along this one carries it";

                    case LineLoadState.NoNumber:
                        return $"the line style \"{Style}\" ends in no number, so nothing says " +
                               "which load it is";

                    case LineLoadState.NotInSchedule:
                        return $"the line style says load {Number}, which the schedule sheet " +
                               "does not define";

                    case LineLoadState.NoExpectedValue:
                        return $"load {Number} could not be worked out here" +
                               (Note.Length > 0 ? ": " + Note : "");

                    default:
                        return "line loads not checked";
                }
            }
        }

        /// <summary>
        /// The working, on the results that are about a number. Most arguments about this
        /// check will be about the wall height rather than about the load, and the height is
        /// not visible anywhere else.
        /// </summary>
        private string Working
        {
            get { return How.Length > 0 ? $" ({How})" : ""; }
        }

        /// <summary>
        /// What was left out, said out loud. A line whose frames also carry something the
        /// check could not use - a load in a local direction, a distributed moment - reads as
        /// right or wrong on the part that was compared, and this is the only thing that says
        /// so.
        /// </summary>
        private string Aside
        {
            get
            {
                return Note.Length > 0 && State != LineLoadState.NoExpectedValue
                    ? " - " + Note
                    : "";
            }
        }

        private int Number { get { return Line == null ? 0 : Line.Number; } }

        /// <summary>True on a wall or fill strip read as its own weight rather than off the plan.</summary>
        private bool Weighed { get { return Line != null && Line.HasOwnValue; } }

        /// <summary>"load 3", or the wall or fill strip a weighed line stands for.</summary>
        private string Name { get { return Line == null ? "load " + Number : Line.LoadName; } }

        /// <summary>Who is asking for the value: the plan, or the element's own weight.</summary>
        private string Asker { get { return Weighed ? "its weight is" : "the plan asks for"; } }

        private string Placed { get { return Weighed ? "standing" : "drawn"; } }

        private string Style { get { return Line == null ? "" : Line.StyleName; } }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / 1000d, 2).ToString("0.##") + " m";
        }

        public override string ToString()
        {
            return $"{Line}: {State}";
        }
    }

    /// <summary>
    /// One stretch of a drawn line that reads the same all the way along, when the line is
    /// checked in steps: consecutive steps that agreed with each other, merged.
    ///
    /// Distances are measured along the drawn line from its start, which is how somebody
    /// standing at the drawing finds the place; the two points are on the line itself.
    /// </summary>
    public sealed class LineLoadStretch
    {
        /// <summary>Where the stretch starts, in mm along the drawn line.</summary>
        public double From { get; set; }

        /// <summary>Where it ends, in mm along the drawn line.</summary>
        public double To { get; set; }

        public MyPoint Start { get; set; }
        public MyPoint End { get; set; }

        /// <summary>
        /// Matched, ValueMismatch, NoEtabsLoad or TakenByOther, and
        /// NoEtabsFrame on the part of a curve that no ETABS run lies along.
        /// </summary>
        public LineLoadState State { get; set; }

        /// <summary>What the plan asks for on this stretch, in kN/m.</summary>
        public double Wanted { get; set; }

        /// <summary>What the ETABS frames carry on this stretch, in kN/m - here, not on average.</summary>
        public double Carried { get; set; }

        /// <summary>
        /// On a stretch whose ETABS load another line took: which line that was, as it is
        /// named on the plan. Empty everywhere else.
        /// </summary>
        public string TakenBy { get; set; } = "";

        /// <summary>
        /// The ETABS frames this stretch was read against, as a report names them: one frame,
        /// unless frames are chained into runs. Empty on a stretch nothing in ETABS runs along.
        /// </summary>
        public string Frames { get; set; } = "";

        public double Length { get { return To - From; } }

        public bool IsFailure
        {
            get
            {
                return State == LineLoadState.ValueMismatch || State == LineLoadState.NoEtabsLoad
                       || State == LineLoadState.NoEtabsFrame || State == LineLoadState.TakenByOther;
            }
        }

        public ResultLevel Level
        {
            get
            {
                switch (State)
                {
                    case LineLoadState.Matched: return ResultLevel.Passed;
                    default: return ResultLevel.Failed;
                }
            }
        }

        /// <summary>"2.25 to 3 m along the line".</summary>
        public string Where
        {
            get { return $"{Metres(From)} to {Metres(To)} along the line"; }
        }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / 1000d, 2).ToString("0.##") + " m";
        }

        public override string ToString()
        {
            return $"{Where}: {State}, {Wanted} against {Carried}";
        }
    }

    /// <summary>
    /// One stretch of one ETABS frame a drawn line was laid on, and what the line is worth.
    ///
    /// Kept as relative distances from the frame's I end - the way ETABS states where a
    /// distributed load runs - so it can be handed back to ETABS as it is, and cannot be got
    /// wrong by a model built in feet.
    /// </summary>
    public sealed class LineLoadPlacement
    {
        public LineLoadPlacement(string frameUniqueName, string frameLabel, double relativeFrom,
                                 double relativeTo, double value, double frameLength)
        {
            FrameUniqueName = frameUniqueName ?? "";
            FrameLabel = frameLabel ?? "";
            RelativeFrom = Math.Max(0, Math.Min(1, Math.Min(relativeFrom, relativeTo)));
            RelativeTo = Math.Max(0, Math.Min(1, Math.Max(relativeFrom, relativeTo)));
            Value = value;
            FrameLength = frameLength;
        }

        /// <summary>The name the API knows the frame by, which is what a load is assigned to.</summary>
        public string FrameUniqueName { get; }

        /// <summary>The name ETABS shows, for saying which frame it is.</summary>
        public string FrameLabel { get; }

        /// <summary>Where the stretch starts, as a fraction of the frame from its I end.</summary>
        public double RelativeFrom { get; }

        /// <summary>Where it ends, as a fraction of the frame from its I end.</summary>
        public double RelativeTo { get; }

        /// <summary>What the line asks for, in kN/m, or nought where that could not be worked out.</summary>
        public double Value { get; }

        /// <summary>How long the frame is in plan, in mm.</summary>
        public double FrameLength { get; }

        /// <summary>How the frame is named in a report: the label where it has one.</summary>
        public string FrameIdentifier
        {
            get { return FrameLabel.Length > 0 ? FrameLabel : FrameUniqueName; }
        }

        public override string ToString()
        {
            return $"{FrameIdentifier} {Math.Round(RelativeFrom, 3)}-{Math.Round(RelativeTo, 3)}: {Value} kN/m";
        }
    }

    /// <summary>The fields a line load shows on its card, named so the viewer can order them.</summary>
    public static class LineLoadResultKeys
    {
        public const string RevitId = "LineLoad.RevitId";
        public const string Style = "LineLoad.Style";
        public const string Number = "LineLoad.Number";
        public const string Description = "LineLoad.Description";
        public const string Length = "LineLoad.Length";
        public const string Frames = "LineLoad.Frames";
        public const string Expected = "LineLoad.Expected";
        public const string Actual = "LineLoad.Actual";
        public const string Working = "LineLoad.Working";
        public const string Mismatch = "LineLoad.Mismatch";
        public const string Alongside = "LineLoad.Alongside";
        public const string Stretch = "LineLoad.Stretch";
        public const string Force = "LineLoad.Force";
        public const string HeightZone = "LineLoad.HeightZone";
    }

    /// <summary>
    /// A stretch of ETABS frame carrying a distributed line load, and whether the loading plan
    /// asked for it.
    ///
    /// The other side of the check, and the one the plan of the loading drawing cannot show by
    /// itself: a line nobody drew leaves nothing on the drawing to colour. So this carries its
    /// own two ends, and the viewer draws the ETABS frame there.
    ///
    /// Both the claimed and the unclaimed ones are made. The check itself only reports the
    /// unclaimed - a stretch the plan did ask for is answered by the drawn line over it - but
    /// a claimed one is how an acceptance filed while it was unclaimed is found again.
    /// </summary>
    public sealed class EtabsLineLoad
    {
        public EtabsLineLoad(LineLoadRun run, MyPoint start, MyPoint end,
                                 double perMetre, double force)
        {
            Run = run;
            Start = start;
            End = end;
            PerMetre = perMetre;
            Force = force;
            FrameName = run == null ? "" : run.FrameMostlyUnder(start, end);
        }

        /// <summary>
        /// The ETABS frame most of this stretch lies on.
        ///
        /// The frame rather than the run, and it does two jobs: it is what a pairing set by
        /// hand is written against, and it is what a review filed on this stretch is filed
        /// under. Both need a name that comes back next run, and a run has none - it is chained
        /// afresh out of whatever frames stood end to end this time.
        ///
        /// The frame most of it lies on rather than the first, so two separate stretches on one
        /// run - a beam loaded at both ends and bare in the middle - are told apart wherever
        /// they sit on different frames.
        /// </summary>
        public string FrameName { get; }

        /// <summary>
        /// True when a drawn line on the loading plan covers this stretch.
        ///
        /// Not a verdict about whether the value is right - only whether the plan asks for a
        /// load here at all.
        /// </summary>
        public bool Claimed { get; set; }

        public LineLoadRun Run { get; }

        public MyPoint Start { get; }

        public MyPoint End { get; }

        /// <summary>What the frames carry along this stretch, in kN/m.</summary>
        public double PerMetre { get; }

        /// <summary>What that comes to over the stretch, in kN.</summary>
        public double Force { get; }

        public double Length { get { return WallGeometry.PlanLength(Start, End); } }

        public string Story { get { return Run == null ? "" : Run.Story; } }

        /// <summary>
        /// How this stretch is named when someone reviews it or pairs it: the frame it lies on.
        ///
        /// It used to be named by the run and the chainage either end of it, which was stable
        /// enough for a review and no use at all for a pairing - a pairing has to be found again
        /// next run, and the moment the stretch was cut differently the name it was filed under
        /// no longer existed. A frame's own name survives.
        /// </summary>
        public string Key { get { return FrameName; } }

        /// <summary>
        /// A drawn line lying alongside this stretch that was read against a different run,
        /// said in words. Empty when there is none.
        /// </summary>
        public string Alongside { get; set; } = "";

        public string Summary
        {
            get
            {
                return $"the ETABS frame carries {LoadSet.Text(PerMetre)} kN/m over " +
                       $"{Math.Round(Length / 1000d, 2).ToString("0.##")} m here and the loading " +
                       "plan draws no line load along it";
            }
        }

        /// <summary>The same, on the SIDL_C tab, where what is missing is a wall rather than a drawn line.</summary>
        public string SidlCSummary
        {
            get
            {
                return $"the ETABS frame carries {LoadSet.Text(PerMetre)} kN/m of SIDL_C over " +
                       $"{Math.Round(Length / 1000d, 2).ToString("0.##")} m here and no wall or fill " +
                       "strip in Revit stands along it";
            }
        }

        public override string ToString()
        {
            return $"unclaimed line load on {Story}: {Summary}";
        }
    }
}
