using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

// Revit has a Point of its own and this file names both worlds. A point here is always a
// point on the plan the viewer draws.
using Point = System.Windows.Point;

namespace DiscrepancyScanner.Wpf
{
    /// <summary>
    /// Lines the Revit model up with the ETABS model from two matched points.
    ///
    /// The base pair fixes the position: pick a point in Revit and type the coordinate ETABS
    /// reports for that same physical point. The rotation pair fixes the heading, so pick a
    /// second point along a direction you can identify in both models â€” a grid intersection
    /// down the same gridline is the usual choice â€” and type its ETABS coordinate too.
    /// </summary>
    public class AlignmentViewModel : ViewModelBase
    {
        /// <summary>Snaps offered while picking, so the point lands exactly on model geometry.</summary>
        private const ObjectSnapTypes SnapTypes =
            ObjectSnapTypes.Endpoints | ObjectSnapTypes.Midpoints | ObjectSnapTypes.Intersections |
            ObjectSnapTypes.Centers | ObjectSnapTypes.Quadrants | ObjectSnapTypes.Points |
            ObjectSnapTypes.Perpendicular | ObjectSnapTypes.Tangents | ObjectSnapTypes.Nearest |
            ObjectSnapTypes.WorkPlaneGrid;

        private readonly RevitRequestHandler requestHandler;
        private readonly Dispatcher dispatcher;
        private readonly Action onAlignmentApplied;

        private MyPoint revitBasePoint;
        private MyPoint revitRotationPoint;
        private MyPoint revitMirrorPoint;

        private string etabsBaseX = "0";
        private string etabsBaseY = "0";
        private string etabsRotationX = "";
        private string etabsRotationY = "";
        private string etabsMirrorX = "";
        private string etabsMirrorY = "";

        private string status = "Pick a base point in Revit, then type its ETABS coordinate in meters.";
        private bool isPicking;
        private bool isMirrored;
        private string mirrorDetection = "";

        /// <summary>
        /// The angle to hold on to when the base pair is the only pair given: the one already
        /// in force, and nought until an alignment has been applied or restored.
        ///
        /// One pair of points says where the model goes and nothing whatever about which way
        /// it faces, so nought there would not mean "leave the angle alone" - it would mean
        /// "spin the model back to north", which is never what somebody nudging a model half
        /// a metre sideways is asking for. The angle changes when a rotation pair is given,
        /// when it is typed in, and when the alignment is cleared.
        /// </summary>
        private double keptRotation;

        private bool useTypedTransform;
        private string typedRotation = "0";
        private string typedDx = "0";
        private string typedDy = "0";

        // The height: an ETABS story and the Revit level it stands for. See HeightFromPairing.
        private IList<Story_E> stories = new List<Story_E>();
        private IList<RevitLevel> revitLevels = new List<RevitLevel>();
        private Story_E selectedStory;
        private RevitLevel selectedLevel;
        private string levelOffset = "0";

        // A pairing saved with the project, waiting for the story and level lists to be read
        // before it can be shown as a selection.
        private string pendingStoryName;
        private long? pendingLevelId;
        private string pendingLevelName;

        // The pairing behind the alignment in force, for saving with it. Null when the height
        // in force was not set from a pairing.
        private string appliedStoryName;
        private long appliedLevelId;
        private string appliedLevelName;
        private double appliedLevelOffsetMM;

        public AlignmentViewModel(RevitRequestHandler requestHandler, Action onAlignmentApplied)
        {
            this.requestHandler = requestHandler;
            this.onAlignmentApplied = onAlignmentApplied;
            dispatcher = Dispatcher.CurrentDispatcher;

            PickBasePointCommand = new RelayCommand(_ => PickBasePoint(), _ => CanPick);
            PickRotationPointCommand = new RelayCommand(_ => PickRotationPoint(), _ => CanPick);
            PickMirrorPointCommand = new RelayCommand(_ => PickMirrorPoint(), _ => CanPick);
            PickBasePairOnPlanCommand = new RelayCommand(_ => PickBasePairOnPlan(), _ => CanPickOnPlan);
            PickRotationPairOnPlanCommand = new RelayCommand(_ => PickRotationPairOnPlan(), _ => CanPickOnPlan);
            PickMirrorPairOnPlanCommand = new RelayCommand(_ => PickMirrorPairOnPlan(), _ => CanPickOnPlan);
            ApplyCommand = new RelayCommand(_ => Apply(), _ => CanApply);
            ResetCommand = new RelayCommand(_ => Reset());
            ClearHeightCommand = new RelayCommand(_ => ClearHeightPairing(), _ => HasAnyHeightChoice);
        }

        /// <summary>Set by the view so the window can get out of the way during a pick.</summary>
        public Action HideShell { get; set; }
        public Action ShowShell { get; set; }

        /// <summary>
        /// Set by the view so a point can be picked off the plan the viewer draws instead of
        /// in Revit. Both models are already on that plan, so a pair of matched points is two
        /// clicks rather than a pick in one program and a coordinate read out of another.
        /// </summary>
        public Action<PlanPickRequest> PickOnPlan { get; set; }

        /// <summary>Set by the view, to call off a plan pick left running when it closes.</summary>
        public Action StopPlanPick { get; set; }

        /// <summary>
        /// Set by the owner: the Revit levels to pair a story with. Read through the owner so
        /// the panel and the single level link share one list of levels.
        /// </summary>
        public Func<IList<RevitLevel>> LevelsSource { get; set; }

        #region bound state

        public string EtabsBaseX { get => etabsBaseX; set { if (SetProperty(ref etabsBaseX, value)) OnInputsChanged(); } }
        public string EtabsBaseY { get => etabsBaseY; set { if (SetProperty(ref etabsBaseY, value)) OnInputsChanged(); } }
        public string EtabsRotationX { get => etabsRotationX; set { if (SetProperty(ref etabsRotationX, value)) OnInputsChanged(); } }
        public string EtabsRotationY { get => etabsRotationY; set { if (SetProperty(ref etabsRotationY, value)) OnInputsChanged(); } }
        public string EtabsMirrorX { get => etabsMirrorX; set { if (SetProperty(ref etabsMirrorX, value)) OnInputsChanged(); } }
        public string EtabsMirrorY { get => etabsMirrorY; set { if (SetProperty(ref etabsMirrorY, value)) OnInputsChanged(); } }

        /// <summary>
        /// Type the move in rather than picking for it. Worth having when the two models were
        /// set out from a known offset and a known angle, when there is no point in either
        /// model convenient to pick, or when a picked alignment is nearly right and wants
        /// nudging by a round number.
        /// </summary>
        public bool UseTypedTransform
        {
            get => useTypedTransform;
            set
            {
                if (!SetProperty(ref useTypedTransform, value))
                {
                    return;
                }

                // Switching on carries the picked alignment across, so typing becomes a way
                // of adjusting what the points gave rather than starting from nothing.
                if (value && revitBasePoint != null)
                {
                    SeedTypedFromPoints();
                }

                OnPropertyChanged(nameof(UsePickedTransform));
                OnInputsChanged();
            }
        }

        /// <summary>The other side of <see cref="UseTypedTransform"/>, for enabling the picking half.</summary>
        public bool UsePickedTransform => !useTypedTransform;

        /// <summary>Rotation about the Revit model origin, in degrees, counter clockwise.</summary>
        public string TypedRotation
        {
            get => typedRotation;
            set { if (SetProperty(ref typedRotation, value)) OnInputsChanged(); }
        }

        public string TypedDx { get => typedDx; set { if (SetProperty(ref typedDx, value)) OnInputsChanged(); } }
        public string TypedDy { get => typedDy; set { if (SetProperty(ref typedDy, value)) OnInputsChanged(); } }

        /// <summary>The ETABS stories to choose from, as the model lists them.</summary>
        public IList<Story_E> Stories => stories;

        /// <summary>The Revit levels to choose from, lowest first.</summary>
        public IList<RevitLevel> RevitLevels => revitLevels;

        /// <summary>The ETABS story the chosen Revit level stands for.</summary>
        public Story_E SelectedStory
        {
            get => selectedStory;
            set { if (SetProperty(ref selectedStory, value)) OnHeightChanged(); }
        }

        /// <summary>The Revit level that stands for the chosen story.</summary>
        public RevitLevel SelectedLevel
        {
            get => selectedLevel;
            set { if (SetProperty(ref selectedLevel, value)) OnHeightChanged(); }
        }

        /// <summary>
        /// Millimetres added to the level to reach the story: where the slab the story stands
        /// for actually sits relative to the Revit level. Negative is below it.
        /// </summary>
        public string LevelOffset
        {
            get => levelOffset;
            set { if (SetProperty(ref levelOffset, value)) OnHeightChanged(); }
        }

        /// <summary>What the height pairing gives, or why it gives nothing.</summary>
        public string HeightSummary
        {
            get
            {
                double dz;
                string note;
                if (TryHeightFromPairing(out dz, out note))
                {
                    return note == null
                        ? $"Not paired - the height in force is kept (dz {Math.Round(dz)} mm)."
                        : $"dz {Math.Round(dz)} mm: " + note + ".";
                }
                return note;
            }
        }

        /// <summary>
        /// The Revit level behind the height in force, or null where the height was not set
        /// from a pairing. For a SAFE model this is the whole of which floor it is.
        /// </summary>
        public string AppliedLevelName => appliedStoryName == null ? null : appliedLevelName;

        public long AppliedLevelId => appliedLevelId;

        /// <summary>The level offset the height in force was worked out with, in mm.</summary>
        public double AppliedLevelOffsetMM => appliedLevelOffsetMM;

        public bool HasAnyHeightChoice => selectedStory != null || selectedLevel != null
                                          || !string.IsNullOrWhiteSpace(levelOffset) && levelOffset.Trim() != "0";

        public string RevitBasePointText => Describe(revitBasePoint);
        public string RevitRotationPointText => Describe(revitRotationPoint);
        public string RevitMirrorPointText => Describe(revitMirrorPoint);

        /// <summary>
        /// Whether the Revit model has to be flipped to match ETABS. Tick it directly if you
        /// already know, or fill in the third point pair and let it be detected.
        /// </summary>
        public bool IsMirrored
        {
            get => isMirrored;
            set { if (SetProperty(ref isMirrored, value)) RefreshPreview(); }
        }

        /// <summary>Result of the last automatic detection, empty when it has not been run.</summary>
        public string MirrorDetection
        {
            get => mirrorDetection;
            private set => SetProperty(ref mirrorDetection, value);
        }

        public string Status
        {
            get => status;
            set => SetProperty(ref status, value);
        }

        /// <summary>The transform the current inputs would produce, shown before it is applied.</summary>
        public string Preview
        {
            get
            {
                MyTransformation candidate = BuildTransformation();
                if (candidate == null)
                {
                    double dz;
                    string note;
                    if (!TryHeightFromPairing(out dz, out note))
                    {
                        return note;
                    }
                    return useTypedTransform
                        ? "Type an angle and a displacement."
                        : "Pick a base point to begin.";
                }
                return candidate + Environment.NewLine + candidate.Diagnostics;
            }
        }

        public bool CanPick => !isPicking;

        /// <summary>Picking off the plan needs a plan to pick off, which only the viewer has.</summary>
        public bool CanPickOnPlan => !isPicking && PickOnPlan != null;

        public bool CanApply => !isPicking && BuildTransformation() != null;

        #endregion

        public ICommand PickBasePointCommand { get; }
        public ICommand PickRotationPointCommand { get; }
        public ICommand PickMirrorPointCommand { get; }
        public ICommand PickBasePairOnPlanCommand { get; }
        public ICommand PickRotationPairOnPlanCommand { get; }
        public ICommand PickMirrorPairOnPlanCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ClearHeightCommand { get; }

        private void PickBasePoint()
        {
            PickPoint("Pick the base point in Revit - the point whose ETABS coordinate you know",
                      point =>
                      {
                          revitBasePoint = point;
                          OnPropertyChanged(nameof(RevitBasePointText));
                          Status = "Base point captured. Type its ETABS coordinate in meters.";
                          OnInputsChanged();
                      });
        }

        private void PickRotationPoint()
        {
            PickPoint("Pick a second point in Revit to set the rotation - the same physical point you will locate in ETABS",
                      point =>
                      {
                          revitRotationPoint = point;
                          OnPropertyChanged(nameof(RevitRotationPointText));
                          Status = "Rotation point captured. Type its ETABS coordinate in meters.";
                          OnInputsChanged();
                      });
        }

        private void PickMirrorPoint()
        {
            PickPoint("Pick a third point well off the line through the first two, to check for mirroring",
                      point =>
                      {
                          revitMirrorPoint = point;
                          OnPropertyChanged(nameof(RevitMirrorPointText));
                          Status = "Mirror check point captured. Type its ETABS coordinate in meters.";
                          OnInputsChanged();
                      });
        }

        /// <summary>
        /// Hides the window, asks Revit for a snapped point on its own thread, then comes back
        /// to the UI thread with the result.
        /// </summary>
        private void PickPoint(string prompt, Action<MyPoint> onPicked)
        {
            isPicking = true;
            RefreshCommands();
            Status = "Picking in Revit...";
            HideShell?.Invoke();

            requestHandler.Raise(application =>
            {
                MyPoint picked = null;
                string message = null;

                try
                {
                    XYZ point = application.ActiveUIDocument.Selection.PickPoint(SnapTypes, prompt);
                    picked = new MyPoint(point.X, point.Y, point.Z);
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    message = "Pick cancelled.";
                }
                catch (Exception e)
                {
                    message = "Cannot pick a point: " + e.Message;
                }

                dispatcher.Invoke(new Action(() =>
                {
                    isPicking = false;
                    ShowShell?.Invoke();
                    RefreshCommands();

                    if (picked != null)
                    {
                        // Picking a point is aligning by points, whatever the panel was set to
                        // before - a restored alignment comes back typed in, and picking on top
                        // of it and seeing nothing change would be baffling.
                        useTypedTransform = false;
                        OnPropertyChanged(nameof(UseTypedTransform));
                        OnPropertyChanged(nameof(UsePickedTransform));

                        onPicked(picked);
                    }
                    else
                    {
                        Status = message;
                    }
                }));
            });
        }

        #region picking a matched pair off the plan

        private void PickBasePairOnPlan()
        {
            PickPairOnPlan(
                "the base point, the one whose place in the ETABS model you can see",
                plan =>
                {
                    revitBasePoint = ToRevitFeet(plan, revitBasePoint);
                    OnPropertyChanged(nameof(RevitBasePointText));
                },
                landing =>
                {
                    etabsBaseX = InMetres(landing.X);
                    etabsBaseY = InMetres(landing.Y);
                    OnPropertyChanged(nameof(EtabsBaseX));
                    OnPropertyChanged(nameof(EtabsBaseY));
                    Status = "Base pair taken off the plan. Apply it, or pick the rotation pair too.";
                });
        }

        private void PickRotationPairOnPlan()
        {
            PickPairOnPlan(
                "a second point, well away from the first, that you can find in both models",
                plan =>
                {
                    revitRotationPoint = ToRevitFeet(plan, revitRotationPoint);
                    OnPropertyChanged(nameof(RevitRotationPointText));
                },
                landing =>
                {
                    etabsRotationX = InMetres(landing.X);
                    etabsRotationY = InMetres(landing.Y);
                    OnPropertyChanged(nameof(EtabsRotationX));
                    OnPropertyChanged(nameof(EtabsRotationY));
                    Status = "Rotation pair taken off the plan. The angle now comes from the two pairs.";
                });
        }

        private void PickMirrorPairOnPlan()
        {
            PickPairOnPlan(
                "a third point, well off the line through the first two",
                plan =>
                {
                    revitMirrorPoint = ToRevitFeet(plan, revitMirrorPoint);
                    OnPropertyChanged(nameof(RevitMirrorPointText));
                },
                landing =>
                {
                    etabsMirrorX = InMetres(landing.X);
                    etabsMirrorY = InMetres(landing.Y);
                    OnPropertyChanged(nameof(EtabsMirrorX));
                    OnPropertyChanged(nameof(EtabsMirrorY));
                    Status = "Third pair taken off the plan. The handedness follows from it.";
                });
        }

        /// <summary>
        /// The two clicks a matched pair really is: the point on the Revit model, and then the
        /// point on the ETABS model it belongs on top of.
        ///
        /// Both models are already drawn on the same plan, which is what makes this possible
        /// at all - there is nothing to read out of one program and type into another, and so
        /// nothing to go wrong between the two.
        /// </summary>
        private void PickPairOnPlan(string what, Action<Point> takeRevitPoint, Action<Point> takeLanding)
        {
            if (PickOnPlan == null)
            {
                Status = "The plan is not open, so there is nothing to pick off.";
                return;
            }

            isPicking = true;
            RefreshCommands();
            Status = "Click " + what + ", on the Revit model.";

            PickOnPlan(new PlanPickRequest(
                "Click " + what + " - on the Revit model",
                PlanSide.Revit,
                revitPlanPoint =>
                {
                    takeRevitPoint(revitPlanPoint);
                    Status = "Now click the same point on the ETABS model.";

                    PickOnPlan(new PlanPickRequest(
                        "Now click that same point on the ETABS model",
                        PlanSide.Etabs,
                        etabsPlanPoint =>
                        {
                            // The plan draws the Revit model already turned and flipped into
                            // place, so both clicks were made against the alignment in force,
                            // and that is what this new one is built on top of.
                            keptRotation = DataContainer.ModelTransformation.Rotation;
                            isMirrored = DataContainer.ModelTransformation.IsMirroredAboutX;
                            useTypedTransform = false;

                            takeLanding(etabsPlanPoint);
                            FinishPlanPick();
                        },
                        CancelPlanPick));
                },
                CancelPlanPick));
        }

        private void FinishPlanPick()
        {
            isPicking = false;
            OnPropertyChanged(nameof(IsMirrored));
            OnPropertyChanged(nameof(UseTypedTransform));
            OnPropertyChanged(nameof(UsePickedTransform));
            OnInputsChanged();
            RefreshCommands();
        }

        private void CancelPlanPick()
        {
            isPicking = false;
            Status = "Pick stopped. Anything picked before it is still here.";
            RefreshCommands();
        }

        /// <summary>
        /// A point clicked on the plan, back in the raw Revit coordinates an alignment is
        /// worked out in.
        ///
        /// Everything on that plan is in the ETABS frame, the Revit model included, because
        /// the alignment in force was baked into each element as it was read. Undoing that
        /// alignment is what turns a click on a Revit wall into the Revit coordinate of it.
        ///
        /// The level is carried over from whatever the point was before rather than taken as
        /// nought: a plan has no Z to give, and nought would quietly drop a base point that
        /// had been picked in Revit at a real level.
        /// </summary>
        private MyPoint ToRevitFeet(Point planPointInMM, MyPoint before)
        {
            MyPoint inRevitMM = DataContainer.ModelTransformation.InvertPointInMM(
                new MyPoint(planPointInMM.X, planPointInMM.Y, 0));

            return new MyPoint(inRevitMM.X / feetToMM,
                               inRevitMM.Y / feetToMM,
                               before == null ? 0 : before.Z);
        }

        /// <summary>The plan is drawn in millimetres; the boxes beside it are read in metres.</summary>
        private static string InMetres(double millimetres)
        {
            return Math.Round(millimetres / metersToMM, 4).ToString();
        }

        #endregion

        /// <summary>
        /// The transform the inputs give: the plan part from the points or the typed move, and
        /// the height from the story and level - never from a picked point, whose height is
        /// only the work plane of whichever view it was picked in.
        /// </summary>
        private MyTransformation BuildTransformation()
        {
            double dz;
            string heightNote;
            if (!TryHeightFromPairing(out dz, out heightNote))
            {
                return null;
            }

            MyTransformation inPlan = useTypedTransform
                ? MyTransformation.FromAngleAndDisplacement(
                      ToDouble(typedRotation),
                      new MyPoint(ToDouble(typedDx) * metersToMM, ToDouble(typedDy) * metersToMM, 0),
                      IsMirrored)
                : BuildFromPickedPoints();

            // A story and a level on their own are an alignment too: the height, with the
            // model left where it is in plan. Setting the height should not mean picking the
            // plan position all over again.
            if (inPlan == null && heightNote != null)
            {
                inPlan = PlanInForce();
            }

            return inPlan?.WithHeight(dz, heightNote ?? "height kept as it is");
        }

        /// <summary>The turn, flip and plan move in force, with no height and no history.</summary>
        private static MyTransformation PlanInForce()
        {
            MyTransformation inForce = DataContainer.ModelTransformation ?? new MyTransformation();
            MyTransformation plan = new MyTransformation();
            plan.SetAlignment(new MyPoint(inForce.Translation.X, inForce.Translation.Y, 0),
                              inForce.Rotation, inForce.IsMirroredAboutX,
                              "plan position kept as it is");
            plan.IsMirroredAboutY = inForce.IsMirroredAboutY;
            return plan;
        }

        /// <summary>
        /// The height the alignment should have, in mm. With a story and a level paired it is
        /// the move that puts the level, raised by the offset, on the story; with neither it
        /// is the height already in force, so a nudge in plan never moves the model up or down.
        ///
        /// False only when the pairing cannot be worked out - half of it chosen, or an offset
        /// that is not a number - with the reason in <paramref name="note"/>. When it is true,
        /// <paramref name="note"/> says where the height came from, or is null when it was
        /// simply kept.
        /// </summary>
        private bool TryHeightFromPairing(out double dzMM, out string note)
        {
            dzMM = HeightInForceMM;
            note = null;

            if (selectedStory == null && selectedLevel == null)
            {
                return true;
            }

            if (selectedStory == null || selectedLevel == null)
            {
                note = selectedStory == null
                    ? "Choose the ETABS story the level stands for, or clear the pairing."
                    : "Choose the Revit level that stands for the story, or clear the pairing.";
                return false;
            }

            double offset;
            if (!TryParseNumber(levelOffset, out offset))
            {
                note = "The level offset is not a number - type it in millimetres.";
                return false;
            }

            dzMM = MyTransformation.HeightFromStoryAndLevel(selectedStory.Elevation,
                                                            selectedLevel.ProjectElevationMM, offset);
            note = $"story {selectedStory.Name} ({Metres(selectedStory.Elevation)}) is level "
                 + $"{selectedLevel.Name} ({Metres(selectedLevel.ProjectElevationMM)})"
                 + (Math.Abs(offset) > 1e-9 ? $" {(offset < 0 ? "-" : "+")} {Math.Abs(offset)} mm" : "");
            return true;
        }

        /// <summary>The height move in force, in mm.</summary>
        private static double HeightInForceMM
        {
            get
            {
                MyTransformation inForce = DataContainer.ModelTransformation;
                return inForce?.Translation?.Z ?? 0d;
            }
        }

        private static string Metres(double millimetres)
        {
            return Math.Round(millimetres / metersToMM, 3) + " m";
        }

        /// <summary>
        /// The transform the picked pairs give. Null until there is a base pair, which is the
        /// least an alignment can be built from.
        /// </summary>
        private MyTransformation BuildFromPickedPoints()
        {
            if (revitBasePoint == null)
            {
                return null;
            }

            // No height in here: the Z these two give is thrown away by BuildTransformation,
            // which takes the height from the story and level instead.
            MyPoint etabsBase = new MyPoint(ToDouble(etabsBaseX), ToDouble(etabsBaseY), 0);

            bool hasRotationPair = revitRotationPoint != null
                                   && !string.IsNullOrWhiteSpace(etabsRotationX)
                                   && !string.IsNullOrWhiteSpace(etabsRotationY);

            if (!hasRotationPair)
            {
                // One pair says where the model goes and nothing about which way it faces, so
                // the angle already in force is carried rather than flattened to nought. For a
                // pair picked in Revit that angle is nought anyway; for one picked off the
                // plan it is the angle those clicks were made against.
                return MyTransformation.FromBaseAndRotation(revitBasePoint, etabsBase,
                                                            keptRotation, IsMirrored);
            }

            // The checkbox is what actually drives the transform; the third point pair only
            // ticks it for you when you would rather not work out the handedness yourself.
            return MyTransformation.FromPointPairs(revitBasePoint, etabsBase,
                                                   revitRotationPoint,
                                                   new MyPoint(ToDouble(etabsRotationX),
                                                               ToDouble(etabsRotationY), 0),
                                                   IsMirrored);
        }

        /// <summary>
        /// Runs the handedness check when all three point pairs are present and ticks the
        /// mirrored box accordingly, leaving the user free to override it afterwards.
        /// </summary>
        private void DetectMirrorIfPossible()
        {
            bool hasRotationPair = revitRotationPoint != null
                                   && !string.IsNullOrWhiteSpace(etabsRotationX)
                                   && !string.IsNullOrWhiteSpace(etabsRotationY);

            bool hasMirrorPair = revitMirrorPoint != null
                                 && !string.IsNullOrWhiteSpace(etabsMirrorX)
                                 && !string.IsNullOrWhiteSpace(etabsMirrorY);

            if (useTypedTransform || revitBasePoint == null || !hasRotationPair || !hasMirrorPair)
            {
                MirrorDetection = "";
                return;
            }

            bool detected;
            string diagnostics;
            bool succeeded = MyTransformation.TryDetectMirror(
                revitBasePoint,
                new MyPoint(ToDouble(etabsBaseX), ToDouble(etabsBaseY), 0),
                revitRotationPoint,
                new MyPoint(ToDouble(etabsRotationX), ToDouble(etabsRotationY), 0),
                revitMirrorPoint,
                new MyPoint(ToDouble(etabsMirrorX), ToDouble(etabsMirrorY), 0),
                out detected, out diagnostics);

            MirrorDetection = diagnostics;

            if (succeeded && detected != isMirrored)
            {
                isMirrored = detected;
                OnPropertyChanged(nameof(IsMirrored));
            }
        }

        private void Apply()
        {
            MyTransformation transformation = BuildTransformation();
            if (transformation == null)
            {
                return;
            }

            DataContainer.ModelTransformation = transformation;

            // From here on this is the angle in force, and a later pair given on its own is
            // read as a move at that angle rather than as a request to spin the model back.
            keptRotation = transformation.Rotation;

            // And this is the pairing the height in force came from, saved with it.
            if (selectedStory != null && selectedLevel != null)
            {
                appliedStoryName = selectedStory.Name;
                appliedLevelId = selectedLevel.Id;
                appliedLevelName = selectedLevel.Name;
                TryParseNumber(levelOffset, out appliedLevelOffsetMM);
            }
            else
            {
                appliedStoryName = null;
            }

            Status = "Alignment applied: " + transformation;
            onAlignmentApplied?.Invoke();
        }

        private void Reset()
        {
            revitBasePoint = null;
            revitRotationPoint = null;
            revitMirrorPoint = null;
            etabsBaseX = etabsBaseY = "0";
            etabsRotationX = etabsRotationY = "";
            etabsMirrorX = etabsMirrorY = "";
            isMirrored = false;
            MirrorDetection = "";
            keptRotation = 0;
            useTypedTransform = false;
            typedRotation = "0";
            typedDx = typedDy = "0";
            selectedStory = null;
            selectedLevel = null;
            levelOffset = "0";
            pendingStoryName = null;
            pendingLevelId = null;
            pendingLevelName = null;
            appliedStoryName = null;

            DataContainer.ModelTransformation = new MyTransformation();

            OnPropertyChanged(nameof(EtabsBaseX));
            OnPropertyChanged(nameof(EtabsBaseY));
            OnPropertyChanged(nameof(SelectedStory));
            OnPropertyChanged(nameof(SelectedLevel));
            OnPropertyChanged(nameof(LevelOffset));
            OnPropertyChanged(nameof(HeightSummary));
            OnPropertyChanged(nameof(EtabsRotationX));
            OnPropertyChanged(nameof(EtabsRotationY));
            OnPropertyChanged(nameof(EtabsMirrorX));
            OnPropertyChanged(nameof(EtabsMirrorY));
            OnPropertyChanged(nameof(RevitBasePointText));
            OnPropertyChanged(nameof(RevitRotationPointText));
            OnPropertyChanged(nameof(RevitMirrorPointText));
            OnPropertyChanged(nameof(IsMirrored));
            OnPropertyChanged(nameof(UseTypedTransform));
            OnPropertyChanged(nameof(UsePickedTransform));
            OnPropertyChanged(nameof(TypedRotation));
            OnPropertyChanged(nameof(TypedDx));
            OnPropertyChanged(nameof(TypedDy));

            Status = "Alignment cleared. The Revit model has gone back to where Revit draws it.";
            RefreshPreview();
            onAlignmentApplied?.Invoke();
        }

        /// <summary>
        /// A point or a typed coordinate changed, so the handedness is worked out again.
        /// Ticking the box by hand deliberately does not come through here, otherwise the
        /// detection would immediately undo a manual override.
        /// </summary>
        private void OnInputsChanged()
        {
            DetectMirrorIfPossible();
            RefreshPreview();
        }

        private void OnHeightChanged()
        {
            OnPropertyChanged(nameof(HeightSummary));
            OnPropertyChanged(nameof(HasAnyHeightChoice));
            RefreshPreview();
        }

        private void ClearHeightPairing()
        {
            selectedStory = null;
            selectedLevel = null;
            levelOffset = "0";
            pendingStoryName = null;
            pendingLevelId = null;
            pendingLevelName = null;

            OnPropertyChanged(nameof(SelectedStory));
            OnPropertyChanged(nameof(SelectedLevel));
            OnPropertyChanged(nameof(LevelOffset));
            Status = "Story and level unpaired. The height already in force is kept.";
            OnHeightChanged();
        }

        /// <summary>
        /// Reads the stories and levels again, keeping whatever was chosen - by name for a
        /// story and by id for a level, since a fresh import brings new objects for the same
        /// ones. Called as the window comes up, so a model imported since shows its stories.
        /// </summary>
        public void RefreshHeightChoices()
        {
            string storyName = selectedStory?.Name ?? pendingStoryName;
            long? levelId = selectedLevel?.Id ?? pendingLevelId;
            string levelName = selectedLevel?.Name ?? pendingLevelName;

            List<Story_E> readStories = DataContainer.getDataContainer()?.Stories;
            storiesReadFrom = readStories;
            stories = readStories != null ? new List<Story_E>(readStories) : new List<Story_E>();

            IList<RevitLevel> readLevels = null;
            try
            {
                readLevels = LevelsSource?.Invoke();
            }
            catch (Exception e)
            {
                Status = "The Revit levels could not be read: " + e.Message;
            }
            revitLevels = readLevels != null ? new List<RevitLevel>(readLevels) : new List<RevitLevel>();

            OnPropertyChanged(nameof(Stories));
            OnPropertyChanged(nameof(RevitLevels));

            // A model with one story - a SAFE model - has it renamed after the level it was
            // paired with on the next import, so the name saved with the pairing no longer
            // finds it. It is still the story: there is no other.
            Story_E story = storyName == null ? null
                : stories.FirstOrDefault(s => string.Equals(s.Name, storyName, StringComparison.OrdinalIgnoreCase))
                  ?? (stories.Count == 1 ? stories[0] : null);
            RevitLevel level = levelId == null && levelName == null ? null
                : revitLevels.FirstOrDefault(l => levelId != null && l.Id == levelId.Value)
                  ?? revitLevels.FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));

            // Held back until both lists have them: a saved pairing whose story is not in the
            // model imported yet is still the pairing, and still comes back once it is.
            pendingStoryName = story == null ? storyName : null;
            pendingLevelId = level == null ? levelId : null;
            pendingLevelName = level == null ? levelName : null;

            selectedStory = story;
            selectedLevel = level;
            OnPropertyChanged(nameof(SelectedStory));
            OnPropertyChanged(nameof(SelectedLevel));
            OnHeightChanged();
        }

        /// <summary>The story list the choices were last read from, to tell when an import replaced it.</summary>
        private List<Story_E> storiesReadFrom;

        /// <summary>
        /// <see cref="RefreshHeightChoices"/>, but only when there is something new to read: an
        /// ETABS import since, or a list still empty. Re-reading every time the window is
        /// clicked into would reset the drop-downs under the user for nothing.
        /// </summary>
        public void RefreshHeightChoicesIfStale()
        {
            List<Story_E> current = DataContainer.getDataContainer()?.Stories;
            bool storiesReplaced = !ReferenceEquals(current, storiesReadFrom)
                                   || (current?.Count ?? 0) != stories.Count;
            if (storiesReplaced || revitLevels.Count == 0)
            {
                RefreshHeightChoices();
            }
        }

        /// <summary>
        /// Adds the pairing behind the alignment in force to what is saved for the project, so
        /// it comes back paired rather than as a bare dz nobody can read.
        /// </summary>
        public void WriteHeightPairing(IDictionary<string, object> saved)
        {
            if (saved == null || appliedStoryName == null)
            {
                return;
            }

            saved["heightStory"] = appliedStoryName;
            saved["heightLevelId"] = appliedLevelId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            saved["heightLevelName"] = appliedLevelName ?? "";
            saved["heightLevelOffset"] = appliedLevelOffsetMM;
        }

        /// <summary>The other half of <see cref="WriteHeightPairing"/>, after <see cref="LoadSaved"/>.</summary>
        public void ReadHeightPairing(IDictionary<string, object> saved)
        {
            string story = saved == null ? "" : Json.GetString(saved, "heightStory", "");
            if (story.Length == 0)
            {
                return;
            }

            long id;
            appliedStoryName = story;
            appliedLevelId = long.TryParse(Json.GetString(saved, "heightLevelId", ""), out id) ? id : -1;
            appliedLevelName = Json.GetString(saved, "heightLevelName", "");
            appliedLevelOffsetMM = Json.GetDouble(saved, "heightLevelOffset", 0);

            selectedStory = null;
            selectedLevel = null;
            pendingStoryName = appliedStoryName;
            pendingLevelId = appliedLevelId;
            pendingLevelName = appliedLevelName;
            levelOffset = Math.Round(appliedLevelOffsetMM, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(LevelOffset));

            if (stories.Count > 0 || revitLevels.Count > 0)
            {
                RefreshHeightChoices();
            }
        }

        private void RefreshPreview()
        {
            OnPropertyChanged(nameof(Preview));
            RefreshCommands();
        }

        private void RefreshCommands()
        {
            OnPropertyChanged(nameof(CanPick));
            OnPropertyChanged(nameof(CanPickOnPlan));
            OnPropertyChanged(nameof(CanApply));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// Puts a transformation saved from an earlier session on the panel as typed values.
        /// The picked points themselves are never saved - only where they ended up putting the
        /// model - so a restored alignment is shown typed in, ready to be nudged or reset.
        /// </summary>
        public void LoadSaved(MyTransformation transformation)
        {
            if (transformation == null)
            {
                return;
            }

            useTypedTransform = true;
            keptRotation = transformation.Rotation;
            typedRotation = Math.Round(transformation.Rotation, 6).ToString();
            typedDx = Math.Round(transformation.Translation.X / metersToMM, 6).ToString();
            typedDy = Math.Round(transformation.Translation.Y / metersToMM, 6).ToString();
            isMirrored = transformation.IsMirroredAboutX;
            Status = "Alignment restored from the last time this project was aligned.";

            OnPropertyChanged(nameof(UseTypedTransform));
            OnPropertyChanged(nameof(UsePickedTransform));
            OnPropertyChanged(nameof(TypedRotation));
            OnPropertyChanged(nameof(TypedDx));
            OnPropertyChanged(nameof(TypedDy));
            OnPropertyChanged(nameof(IsMirrored));
            OnPropertyChanged(nameof(HeightSummary));
            RefreshPreview();
        }

        /// <summary>
        /// Fills the typed boxes from the alignment the picked points currently give, so the
        /// numbers on screen are the ones already in force the moment the mode is switched.
        /// </summary>
        private void SeedTypedFromPoints()
        {
            MyTransformation fromPoints = BuildFromPickedPoints();
            if (fromPoints == null)
            {
                return;
            }

            typedRotation = Math.Round(fromPoints.Rotation, 6).ToString();
            typedDx = Math.Round(fromPoints.Translation.X / metersToMM, 6).ToString();
            typedDy = Math.Round(fromPoints.Translation.Y / metersToMM, 6).ToString();

            OnPropertyChanged(nameof(TypedRotation));
            OnPropertyChanged(nameof(TypedDx));
            OnPropertyChanged(nameof(TypedDy));
        }

        /// <summary>The typed displacement is in meters, matching the ETABS coordinates above it.</summary>
        private const double metersToMM = 1000d;

        /// <summary>Revit works in feet internally, which is what a picked Revit point is in.</summary>
        private const double feetToMM = 304.8d;

        /// <summary>Revit reports internal units in feet; show meters too, since ETABS is typed in meters.</summary>
        private static string Describe(MyPoint pointInFeet)
        {
            if (pointInFeet == null)
            {
                return "not picked";
            }

            // Plan only: the height of a picked point is the work plane of the view it was
            // picked in, and it takes no part in the alignment - the story and level do.
            const double feetToMeters = 0.3048d;
            return $"{Math.Round(pointInFeet.X * feetToMeters, 3)}, " +
                   $"{Math.Round(pointInFeet.Y * feetToMeters, 3)} m";
        }

        private static double ToDouble(string text)
        {
            double value;
            return double.TryParse(text, out value) ? value : 0d;
        }

        /// <summary>A blank box is nought; anything else has to read as a number.</summary>
        private static bool TryParseNumber(string text, out double value)
        {
            value = 0d;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.CurrentCulture, out value)
                   || double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}

