# Graph Report - Discrepancy-Scanner  (2026-11-07)

## Corpus Check
- 312 files · ~493,855 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 25 file(s) not represented in the graph (top: .resx 9, .ttf 8, .config 3)

## Summary
- 7355 nodes · 21524 edges · 241 communities (177 shown, 64 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 2074 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `8ed482c8`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Level and Safe Pairing
- Command and Pick UI
- ModelCanvas
- Solution Layout and Check Files
- Check Switches and Result Fields
- Level Binding View Models
- Line Load Checks
- Thickening Detection
- ETABS Beam and Area Import
- FindingRow
- Slab Loads and Mesh
- SIDL_C Checks
- Plan Edge Geometry
- Alignment and Categories
- Legacy WinForms GUI
- CheckKind
- system_collections_generic
- ConstrainedDelaunay
- Form1
- ViewModelBase
- system_text
- LineLoadWriteBackTests
- FrameData_e
- DiscrepancyScanner.Wpf
- LoadingPlanGuessViewModel
- PlanRing
- WallCheckTests
- ElementCard
- LoadSchedule
- RawDataTests
- LoadingPlanLink
- TestMethod
- ElementViewModel
- LineLoadCompareEngine
- IDrawAble
- LoadingPlansViewModel
- SelectionPairingTests
- PairedFrame
- IDictionary
- .readLoadSetsFromTextFile
- SlabLoadCheckTests
- PlanRegionMath
- ScheduleSheetViewModel
- EtabsForm
- LineLoadResult
- ElementKind
- LineLoadFrame_E
- Window
- PairPlace
- LineLoad_R
- SidlCAreaResult
- HeightZoneElementViewModel
- SettingsViewModel
- FrameData_r
- CrossPair
- PairingOverrides
- Slab_R
- CheckWindow
- ReviewNotes
- .getPrefrencesData
- Point
- .readSlab
- SectionDesignerTables
- PreflightItem
- SidlCAreaCheckTests
- RevitRequestHandler
- .Create
- DataContainer
- Element
- SettingItem
- PairedFrameForm
- IList
- PairedWall
- MyPoint
- Window
- CsiInstance
- FrameData
- .TheCheckSaysTheSameOnTheFileAsOnTheModel
- ScheduleColumnOverrides
- SidlCAreaCompareEngine
- AddinAssemblies
- XlsxSheet
- Column_e
- PairedColumn
- .Regions
- SlabCoverage
- Story_E
- LoadPatternsViewModel
- RevitEngine
- .Merge
- .ForgetDecisions
- Shortcuts
- .Update
- SnapKind
- Window
- SidlCAreaSource_R
- VerdictColour
- SlabSystemCheckTests
- MyTransformation
- EtabsDataContainer
- ModelIndexTests
- LineLoadEntry
- ScheduleCellViewModel
- Func
- Window
- Window
- .ImportRevit
- WallCompareEngine
- ReviewNote
- Document
- LineLoadElementViewModel
- SmallZoneCountTests
- Window
- LicensePayload
- LineLoadStretch
- SidlCAreaPiece
- LoadCategory
- CrossPairTests
- SupportInsideSlabTests
- Window
- ColumnGroupCheck
- CompareEngine
- Wall_E
- EtabsTextFileTests
- HeightZones
- MessageType
- Slab_E
- Wall_R
- FindingTally
- TextLine
- Window
- Column_r
- ShortcutTests
- IColumn
- HeightZoneTests
- PlanPolygon
- XlsxCellWriter
- TranslationForm
- .ApplyToPointInMM
- ColumnOverlayTests
- RoutedEventArgs
- SlabBoundaryCharacterizationTests
- WallCornerTests
- GraphicsExtension
- .ImportEtabsOnce
- FindingsList
- Resources
- .detectAgainstFloor
- system_io
- HeightZone
- LoadRegion_R
- ProjectParameterMappings
- SlabKind
- .FromPoints
- MachineFingerprint
- .IsLineLoadStyle
- HeightZoneRequest
- LoadPatternMappingTests
- EtabsLineLoad
- .JunctionAt
- WallProfile
- UserSettings
- LoadPatternRow
- LabelGroupTests
- Window
- ModelChooserWindow
- RevocationGuard
- LoadingPlanRow
- Window
- DateTime
- XlsxCellEdit
- .Read
- LicenseGate
- RevocationList
- List
- LicenseStatus
- .Measure
- SectionShape
- ScopeTab
- Joint
- SlabMeshCurvedEdgeTests
- ViewPalette
- SettingsWindow
- GeometryDump
- SlabZoneState
- .LoadsAt
- SafeLevelTests
- ViewMode
- .TakeClosestPairsFirst
- .WriteLoadBack
- SlabDepthRegion
- Window
- PlanRingCheck
- CheckState
- Discrepancy Scanner
- TextHandling
- Stub
- LineLoadPlacement
- SlabElementViewModel
- Architecture
- ExcelWorkbookWriter
- eLength
- eForce
- ActivationRequest
- LoadCoverage
- .RevitColumn
- PairCandidate
- SettingCategory
- Badge
- SidlCAreaState
- SlabZone.cs
- .ForState
- BadgeText
- .SetStories
- SidlCAreaBand
- .Group
- labelButton
- DiscrepancyScanner.sln
- ColumnCheckState
- .BuildRows
- Overlap
- system
- AreaLoadSummary
- asLocation
- Chrome
- MappedParameter
- EtabsTextExport.csproj
- FrameSource
- SecretBox
- RevitLevel
- .ImportEtabs
- SidlCAreaUnclaimed
- mergeCondittion
- LicenseStore
- canvasHost

## God Nodes (most connected - your core abstractions)
1. `ComparisonViewModel` - 370 edges
2. `MyPoint` - 356 edges
3. `ElementViewModel` - 211 edges
4. `DiscrepancyScanner` - 201 edges
5. `Window` - 177 edges
6. `RevitEngine` - 172 edges
7. `EtabsEngine` - 158 edges
8. `PlanPolygon` - 143 edges
9. `SlabZone` - 138 edges
10. `GUI` - 115 edges

## Surprising Connections (you probably didn't know these)
- `Architecture` --references--> `CheckKind`  [INFERRED]
  CLAUDE.md → DiscrepancyScanner/Comparison/CheckOutcome.cs
- `Architecture` --references--> `CheckOutcome`  [INFERRED]
  CLAUDE.md → DiscrepancyScanner/Comparison/CheckOutcome.cs
- `Architecture` --references--> `LineLoadResult`  [INFERRED]
  CLAUDE.md → DiscrepancyScanner/Comparison/LineLoadResult.cs
- `Architecture` --references--> `PairingOverrides`  [INFERRED]
  CLAUDE.md → DiscrepancyScanner/Comparison/PairingOverrides.cs
- `Architecture` --references--> `PlanRegionMath`  [INFERRED]
  CLAUDE.md → DiscrepancyScanner/Comparison/PlanRegionMath.cs

## Import Cycles
- None detected.

## Communities (241 total, 64 thin omitted)

### Community 0 - "Level and Safe Pairing"
Cohesion: 0.01
Nodes (155): ComparisonViewModel, AcceptedCount, AcceptPickedCommand, AcceptToolTip, AddNoteCommand, AddNoteToPickedCommand, Alignment, AskForLevels (+147 more)

### Community 1 - "Command and Pick UI"
Cohesion: 0.01
Nodes (156): AcceptButtonText, AcceptPickedCommand, AddNoteToPickedCommand, CancelCommand, CanPairSelection, Card, CheckLabel, CheckProgress (+148 more)

### Community 2 - "ModelCanvas"
Cohesion: 0.06
Nodes (17): ModelCanvas, Background, ChooseFrom, DeselectRequested, DrawingZoneChanged, Elements, IsDrawingZone, IsMeasuring (+9 more)

### Community 3 - "Solution Layout and Check Files"
Cohesion: 0.07
Nodes (3): DiscrepancyScanner, DiscrepancyScanner.Tests, LicenseInfo

### Community 4 - "Check Switches and Result Fields"
Cohesion: 0.05
Nodes (22): CheckOutcome, Etabs, HasSides, Kind, Label, Level, Revit, Summary (+14 more)

### Community 5 - "Level Binding View Models"
Cohesion: 0.04
Nodes (47): AlignmentViewModel, AppliedLevelId, AppliedLevelName, AppliedLevelOffsetMM, ApplyCommand, CanApply, CanPick, CanPickOnPlan (+39 more)

### Community 7 - "Thickening Detection"
Cohesion: 0.06
Nodes (16): ThickeningCandidate, ThickeningClassifier, ThickeningConfidence, High, Low, Medium, ThickeningHost, ThickeningOptions (+8 more)

### Community 8 - "ETABS Beam and Area Import"
Cohesion: 0.04
Nodes (11): BeamDesign, EtabsEngine, IgnoredNameList, ImportIssues, IsModelLocked, IsSingleLevel, ModelName, ModelUnitsPerKilonewtonMetre (+3 more)

### Community 9 - "FindingRow"
Cohesion: 0.06
Nodes (30): FindingRow, CheckLabel, Element, ElementLabel, HistoryKey, IsAccepted, IsFail, IsNew (+22 more)

### Community 10 - "Slab Loads and Mesh"
Cohesion: 0.03
Nodes (63): SlabLoadState, ConflictingRegions, Matched, Mismatch, NoEtabsLoad, NoLoadingRegion, NoLoadNumber, NotChecked (+55 more)

### Community 11 - "SIDL_C Checks"
Cohesion: 0.05
Nodes (19): SidlCSource_R, BaseElevation, Comment, CommentLoad, Description, ElementName, Kind, Length (+11 more)

### Community 12 - "Plan Edge Geometry"
Cohesion: 0.09
Nodes (7): EdgeKey, EtabsFace, FaceGrid, Group, Provisional, SlabMeshCompare, CuttingForSlab

### Community 13 - "Alignment and Categories"
Cohesion: 0.03
Nodes (69): Alignment.Status, Applies, Blurb, BoolValue, CategoriesView, ChangedSummary, CheckEveryStory, CheckOneStoryOnly (+61 more)

### Community 15 - "CheckKind"
Cohesion: 0.04
Nodes (48): CheckKind, Beam, Dimensions, LineLoadMissingInEtabs, LineLoadMissingInRevit, LineLoads, Location, None (+40 more)

### Community 16 - "system_collections_generic"
Cohesion: 0.09
Nodes (6): LineLoadResultKeys, SidlCAreaResultKeys, SidlCSourceKind, FillSlab, Wall, RawResultKeys

### Community 17 - "ConstrainedDelaunay"
Cohesion: 0.09
Nodes (10): ConstrainedDelaunay, ConstrainedEdges, Constraints, CrossingStageDidNotConverge, Points, RealPointCount, Triangles, SpatialGrid (+2 more)

### Community 18 - "Form1"
Cohesion: 0.07
Nodes (10): Form1, eLength, cm, ft, inch, m, micron, mm (+2 more)

### Community 19 - "ViewModelBase"
Cohesion: 0.03
Nodes (63): CheckFilterGroup, IsSelected, Kind, Label, Mode, Options, CheckFilters, AllShown (+55 more)

### Community 22 - "LineLoadWriteBackTests"
Cohesion: 0.07
Nodes (20): LineLoadPatterns, Description, IsEmpty, Names, FrameDistributedLoad, CoordinateSystem, Direction, LoadType (+12 more)

### Community 23 - "FrameData_e"
Cohesion: 0.10
Nodes (9): FrameData_e, Diameter, EtabsLabel, Groups, Joints, Shape, SteelLocations, TotalBotSteel (+1 more)

### Community 25 - "LoadingPlanGuessViewModel"
Cohesion: 0.05
Nodes (35): LoadingPlanView, DisplayName, Id, Kind, Name, LoadingPlanGuessTests, LoadingPlanGuessRow, BorrowedFrom (+27 more)

### Community 26 - "PlanRing"
Cohesion: 0.07
Nodes (17): PlanBounds, Height, IsEmpty, MaxX, MaxY, MinX, MinY, Width (+9 more)

### Community 28 - "ElementCard"
Cohesion: 0.04
Nodes (48): MultiSelectionCardTests, Stub, LabelAnchor, ElementCard, AcceptButtonText, CanAccept, CanReview, Count (+40 more)

### Community 29 - "LoadSchedule"
Cohesion: 0.15
Nodes (17): LoadSchedule, ColumnRoles, Count, Description, Entries, FilePath, Issues, SheetName (+9 more)

### Community 30 - "RawDataTests"
Cohesion: 0.10
Nodes (3): Region, RawDataTests, RawElements

### Community 31 - "LoadingPlanLink"
Cohesion: 0.10
Nodes (3): LoadingPlanLinksTests, LoadingPlanLink, LoadingPlanLinks

### Community 32 - "TestMethod"
Cohesion: 0.07
Nodes (4): BeamIgnoreTests, ColumnDetectionTests, LineLoadCountTests, LoadingPlanMarkerTests

### Community 33 - "ElementViewModel"
Cohesion: 0.04
Nodes (45): ElementViewModel, CanBeFiled, Caption, Counterpart, Description, DisplayState, Drawn, EtabsUniqueName (+37 more)

### Community 34 - "LineLoadCompareEngine"
Cohesion: 0.07
Nodes (31): Assignment, Beside, Contribution, LineLoadCompareEngine, Band, EtabsStretches, HeightZones, Issues (+23 more)

### Community 35 - "IDrawAble"
Cohesion: 0.06
Nodes (26): ComparasonState, EtabsUnmatched, Matched, RevitUnmatched, Uncategorized, DrawAble, designState, Check_Concrete (+18 more)

### Community 36 - "LoadingPlansViewModel"
Cohesion: 0.05
Nodes (40): LoadingPlansViewModel, ClearSearchCommand, CopyToCommand, EditorHeading, GuessCommand, GuessSummary, HasNoSchedule, HasNoStories (+32 more)

### Community 37 - "SelectionPairingTests"
Cohesion: 0.16
Nodes (5): PairWrite, EtabsUniqueName, RevitId, TwoWayPairing, SelectionPairingTests

### Community 38 - "PairedFrame"
Cohesion: 0.05
Nodes (32): IHasResultFields, ResultFields, PairedFrame, B1_Ratio, B2_Ratio, B3_Ratio, ET_bottom, ET_bottomLeft (+24 more)

### Community 39 - "IDictionary"
Cohesion: 0.14
Nodes (7): Contents, Decisions, HandPairings, HasAlignment, HeightZones, LoadingPlans, ProjectFile

### Community 41 - "SlabLoadCheckTests"
Cohesion: 0.12
Nodes (8): AreaLoad_E, Direction, IsGravity, IsToFrame, Pattern, Value, LevelSetup, SlabLoadCheckTests

### Community 43 - "ScheduleSheetViewModel"
Cohesion: 0.04
Nodes (32): VisabilityController, ScheduleSaveTarget, Cancel, ToolOnly, Workbook, ScheduleSheetViewModel, AskWhereToSave, CanSetColumns (+24 more)

### Community 44 - "EtabsForm"
Cohesion: 0.06
Nodes (6): EtabsForm, EtabsIssuesForm, LinkSelectionForm, SelectedDocuments, SelectedTitles, PrefrencesForm

### Community 45 - "LineLoadResult"
Cohesion: 0.05
Nodes (38): LineLoadResult, Actual, Alongside, Aside, Asker, Bundled, CanWriteBack, Carried (+30 more)

### Community 46 - "ElementKind"
Cohesion: 0.09
Nodes (20): ElementKind, Beam, Column, HeightZone, LineLoad, Slab, Wall, LabelFieldGroup (+12 more)

### Community 47 - "LineLoadFrame_E"
Cohesion: 0.07
Nodes (22): LineLoad_E, Direction, IsGravity, IsMoment, IsTrapezoidal, Pattern, RelativeFrom, RelativeTo (+14 more)

### Community 48 - "Window"
Cohesion: 0.05
Nodes (40): CopyToCommand, EditorHeading, GuessCommand, GuessSummary, HasNoSchedule, HasNoStories, HasViewMissing, LineSheetName (+32 more)

### Community 49 - "PairPlace"
Cohesion: 0.09
Nodes (7): ElementKindKey, Beam, Column, LineLoad, Wall, PairPlace, Story

### Community 50 - "LineLoad_R"
Cohesion: 0.08
Nodes (17): LineLoad_R, HasLine, HasNumber, HasOwnValue, IsOnSeveralStories, Length, LoadName, Midpoint (+9 more)

### Community 51 - "SidlCAreaResult"
Cohesion: 0.06
Nodes (30): SidlCAreaResult, Actual, AveragedOverWholeFill, Bands, BandText, CoveredShare, EtabsForce, EtabsLoadedArea (+22 more)

### Community 52 - "HeightZoneElementViewModel"
Cohesion: 0.15
Nodes (9): HeightZoneElementViewModel, Caption, Colour, Height, IsFilled, IsHidden, LabelAnchor, Name (+1 more)

### Community 53 - "SettingsViewModel"
Cohesion: 0.06
Nodes (30): SettingsPanelTests, SettingGroup, Blurb, HasBlurb, Header, Items, SettingsViewModel, AlignmentRequested (+22 more)

### Community 54 - "FrameData_r"
Cohesion: 0.09
Nodes (16): Candidate, Cell, EndGrid, EndpointCompareEngine, Overrides, PairedFrames, ProjectKey, UnmatchedEtabs (+8 more)

### Community 55 - "CrossPair"
Cohesion: 0.05
Nodes (33): CrossPair, EtabsAsWall, EtabsColumn, EtabsIsColumn, EtabsLine, EtabsWall, LengthDelta, LocationDelta (+25 more)

### Community 56 - "PairingOverrides"
Cohesion: 0.15
Nodes (4): PairingOverrides, Pin, IsPlaced, HandDecisionPlaceTests

### Community 57 - "Slab_R"
Cohesion: 0.05
Nodes (31): FloorRecord, PatchRecord, SlabCheckInputs, SupportRecord, Slab_R, BottomElevation, CategoryName, ComparedThickness (+23 more)

### Community 58 - "CheckWindow"
Cohesion: 0.10
Nodes (7): CheckWindow, DialogOwner, Handle, MinMaxInfo, MonitorInfo, NativePoint, NativeRect

### Community 61 - "Point"
Cohesion: 0.08
Nodes (19): Measurement, From, To, PlanPickRequest, OnCancelled, OnPicked, Prompt, Side (+11 more)

### Community 63 - "SectionDesignerTables"
Cohesion: 0.08
Nodes (9): AreaSectionTables, LoadSetTables, PatternValue, Gathered, Outline, SectionDesignerTables, Table, LoadSetTableTests (+1 more)

### Community 64 - "PreflightItem"
Cohesion: 0.07
Nodes (30): PreflightTests, PreflightItem, Action, ActionText, Detail, FixesItself, HasAction, IsDone (+22 more)

### Community 68 - "DataContainer"
Cohesion: 0.05
Nodes (37): DataContainer, Columns, Columns_r, CrossPairs, EtabsLineLoads, EtabsReadsNullLoadAreas, EtabsSidlC, EtabsSourceName (+29 more)

### Community 69 - "Element"
Cohesion: 0.09
Nodes (3): SizeSources, ParameterData, SizeTally

### Community 70 - "SettingItem"
Cohesion: 0.07
Nodes (22): SettingItem, Applies, BoolValue, Category, Description, EditorKind, HasApplies, HasDescription (+14 more)

### Community 71 - "PairedFrameForm"
Cohesion: 0.08
Nodes (3): EtabsFrameForm, PairedFrameForm, RevitFrameForm

### Community 72 - "IList"
Cohesion: 0.09
Nodes (14): CheckTiming, Phase, SlabCompareEngine, Issues, LoadsOnly, LoadZones, OnlyStory, ReportProgress (+6 more)

### Community 73 - "PairedWall"
Cohesion: 0.07
Nodes (26): PairedWall, AngleDelta, CoveredLength, Displaced, LengthMatched, LengthShortfall, LocationDelta, LocationMatched (+18 more)

### Community 74 - "MyPoint"
Cohesion: 0.20
Nodes (4): PlanClip, MyPoint, AsPointF, Zero

### Community 75 - "Window"
Cohesion: 0.05
Nodes (35): ClearHeightCommand, EtabsBaseX, EtabsBaseY, EtabsMirrorX, EtabsMirrorY, EtabsRotationX, EtabsRotationY, HeightSummary (+27 more)

### Community 76 - "CsiInstance"
Cohesion: 0.10
Nodes (14): cPlugin, CsiInstance, DisplayName, Location, ModelName, ModelPath, ProcessId, Product (+6 more)

### Community 77 - "FrameData"
Cohesion: 0.07
Nodes (23): FrameData, ApproxLength_r, AsBotLeft, AsBotMid, AsBotRight, AsLinksLeft_e, AsLinksLeft_r, AsLinksMid_e (+15 more)

### Community 78 - ".TheCheckSaysTheSameOnTheFileAsOnTheModel"
Cohesion: 0.15
Nodes (6): EntryRecord, HeightZoneRecord, LineLoadCheckInputs, LineRecord, ScheduleRecord, LineLoadCheckInputsTests

### Community 79 - "ScheduleColumnOverrides"
Cohesion: 0.16
Nodes (10): ScheduleColumnOverrides, ActiveProject, Current, ScheduleRole, Description, Live, LiveKind, Number (+2 more)

### Community 80 - "SidlCAreaCompareEngine"
Cohesion: 0.08
Nodes (19): LoadBand, Above, Below, IsSymmetric, Reading, SidlCAreaCompareEngine, Band, Issues (+11 more)

### Community 82 - "XlsxSheet"
Cohesion: 0.07
Nodes (17): Columns, Found, LineLoadScheduleReader, Columns, Found, LoadScheduleReader, Pending, ScheduleNames (+9 more)

### Community 83 - "Column_e"
Cohesion: 0.11
Nodes (19): ColumnCompareEngine, HeldUnpaired, Overrides, PairedColumns, ProjectKey, UnmatchedEtabsColumns, UnmatchedRevitColumns, Column_e (+11 more)

### Community 84 - "PairedColumn"
Cohesion: 0.09
Nodes (17): PairedColumn, DiameterDelta, DimensionsMatched, GroupCheck, LocationDelta, LocationMatched, LongSideDelta, Note (+9 more)

### Community 86 - "SlabCoverage"
Cohesion: 0.15
Nodes (7): Reading, Region, SlabCoverage, AddStackedDepths, Regions, SlabEtabsOpeningTests, SlabShellCutTests

### Community 87 - "Story_E"
Cohesion: 0.27
Nodes (5): Story_E, DisplayName, Elevation, Name, SimilarTo

### Community 88 - "LoadPatternsViewModel"
Cohesion: 0.08
Nodes (21): LoadPatternsViewModel, Choices, HasNoPatterns, HasPatterns, LineNames, LiveHeading, LiveMarking, LiveNames (+13 more)

### Community 89 - "RevitEngine"
Cohesion: 0.07
Nodes (9): paraType, INSTANCE, TYPE, DocumentTitle, IgnoredNameList, ImportIssues, ReportProgress, ShouldStop (+1 more)

### Community 91 - ".ForgetDecisions"
Cohesion: 0.08
Nodes (22): AskWhatToForget, ForgetChoices, CanForget, Everything, ForgetButtonText, HasAny, Options, ForgetOption (+14 more)

### Community 92 - "Shortcuts"
Cohesion: 0.13
Nodes (4): KeyStroke, Key, Modifiers, Shortcuts

### Community 93 - ".Update"
Cohesion: 0.10
Nodes (12): LevelRow, Description, Elevation, Failed, FailedBarWidth, HasFindings, IsSelected, Name (+4 more)

### Community 94 - "SnapKind"
Cohesion: 0.25
Nodes (8): SnapKind, Centre, Corner, Edge, Endpoint, Free, Intersection, Midpoint

### Community 95 - "Window"
Cohesion: 0.07
Nodes (26): AreaChoice, Choices, Frames, HasNoPatterns, HasPatterns, LineNames, LineRead, LiveHeading (+18 more)

### Community 96 - "SidlCAreaSource_R"
Cohesion: 0.10
Nodes (16): DepthFace, SidlCAreaSource_R, Area, AverageThickness, BaseElevation, Description, ElementName, HasFaces (+8 more)

### Community 97 - "VerdictColour"
Cohesion: 0.07
Nodes (22): OneModelColourTests, VerdictColour, Accepted, EtabsOnly, Failed, Passed, Review, RevitOnly (+14 more)

### Community 99 - "MyTransformation"
Cohesion: 0.09
Nodes (7): MyTransformation, Diagnostics, IsMirrored, IsMirroredAboutX, IsMirroredAboutY, Rotation, Translation

### Community 100 - "EtabsDataContainer"
Cohesion: 0.18
Nodes (3): EtabsDataContainer, EtabsModelFile, SavedModelFileTests

### Community 102 - "LineLoadEntry"
Cohesion: 0.07
Nodes (14): ExpectedLineLoad, LineLoadHeight, LineLoadEntry, Description, HeightIndependent, Number, Summary, Value (+6 more)

### Community 103 - "ScheduleCellViewModel"
Cohesion: 0.08
Nodes (19): ScheduleEdits, Current, ScheduleCellViewModel, Changed, Column, DiffersFromFile, FileText, IsFormula (+11 more)

### Community 104 - "Func"
Cohesion: 0.11
Nodes (15): ShortcutAction, Accept, Beams, Columns, HeightZones, LevelDown, LevelUp, LineLoads (+7 more)

### Community 105 - "Window"
Cohesion: 0.09
Nodes (21): Action, ActionText, CanFixEverything, CanRunAnyway, FixEverythingText, HasAction, Headline, IsDone (+13 more)

### Community 106 - "Window"
Cohesion: 0.08
Nodes (22): HasCandidates, LoadingPlanMarkers, MatchText, PickedView, RequiredText, SelectedAreaSheet, SelectedLineSheet, Window (+14 more)

### Community 107 - ".ImportRevit"
Cohesion: 0.12
Nodes (6): RevitLeaveOut, BeamMarks, BeamTypeNames, SlabNames, WallNames, RevitLeaveOutTests

### Community 108 - "WallCompareEngine"
Cohesion: 0.10
Nodes (17): UncoveredWallPiece, Covered, End, Length, Start, Uncovered, Wall, WallCompareEngine (+9 more)

### Community 109 - "ReviewNote"
Cohesion: 0.10
Nodes (11): HumanVerdict, Accepted, None, Reviewed, ReviewNote, Accepted, IsEmpty, Reviewed (+3 more)

### Community 110 - "Document"
Cohesion: 0.12
Nodes (15): ImportStoppedException, LinkInfo, Title, LinkPlacement, Doc, HostInstanceId, IsAttached, IsNested (+7 more)

### Community 111 - "LineLoadElementViewModel"
Cohesion: 0.04
Nodes (38): SnapPoint, At, Kind, BeamElementViewModel, End, LabelAnchor, LabelDirection, SnapEdges (+30 more)

### Community 113 - "Window"
Cohesion: 0.09
Nodes (18): CanForget, Everything, ForgetButtonText, HasAny, Window, CountText, Detail, IsSelected (+10 more)

### Community 114 - "LicensePayload"
Cohesion: 0.08
Nodes (23): LicensePayload, ExpiresAtUtc, ExpiresUtc, Features, FormatVersion, IssuedAtUtc, IssuedUtc, Licensee (+15 more)

### Community 115 - "LineLoadStretch"
Cohesion: 0.07
Nodes (25): LineLoadState, Matched, NoEtabsFrame, NoEtabsLoad, NoExpectedValue, NoNumber, NotChecked, NotInSchedule (+17 more)

### Community 116 - "SidlCAreaPiece"
Cohesion: 0.10
Nodes (18): Group, SidlCAreaCutter, SidlCAreaPiece, Band, Fill, Index, IsTooSmallToCount, Key (+10 more)

### Community 117 - "LoadCategory"
Cohesion: 0.14
Nodes (8): LoadPatternMap, Mapping, LoadCategory, Ll, Lr, Mep, None, Sdl

### Community 120 - "Window"
Cohesion: 0.08
Nodes (18): CopySheets, CopyView, SelectSimilarCommand, SourceSheets, SourceView, Window, ApplyCommand, CurrentState (+10 more)

### Community 121 - "ColumnGroupCheck"
Cohesion: 0.11
Nodes (17): ColumnGroupCheck, Description, ExtraGroups, ExtraGroupsText, Groups, GroupsText, HasExtraGroups, MatchedGroup (+9 more)

### Community 122 - "CompareEngine"
Cohesion: 0.15
Nodes (5): candidatePair, CompareEngine, PairedFrames, RemainingEtabsframes, UnmatchedRevit

### Community 123 - "Wall_E"
Cohesion: 0.09
Nodes (16): Wall_E, BaseElevation, DrawingPoints, HasCentreLine, Identifier, Length, PlanEnd, PlanStart (+8 more)

### Community 126 - "MessageType"
Cohesion: 0.10
Nodes (18): MessageType, EtabsSectionName, EtabsUnquiName, PMMRatio1, PMMRatio2, PMMRatio3, PMMRatio4, PMMRatio5 (+10 more)

### Community 127 - "Slab_E"
Cohesion: 0.10
Nodes (17): Slab_E, BoundaryPoints, ComparedThickness, Description, IsLoadOnly, IsOpening, Kind, Loads (+9 more)

### Community 128 - "Wall_R"
Cohesion: 0.08
Nodes (20): IWall, Identifier, SectionName, Thickness, Wall_R, ElementName, HasValidThickness, Identifier (+12 more)

### Community 129 - "FindingTally"
Cohesion: 0.18
Nodes (5): ScopeChipTests, FindingTally, Groups, Levels, Tabs

### Community 130 - "TextLine"
Cohesion: 0.07
Nodes (26): AreaAssignRow, EtabsTable, AreaAssigns, Controls, None, ShellLoadSetAssignments, ShellUniformLoadSets, EtabsTextFile (+18 more)

### Community 131 - "Window"
Cohesion: 0.09
Nodes (19): DiscardKeptCommand, OpenInExcelCommand, ReadAs, ReloadCommand, ResetColumnsCommand, RevertCommand, SaveCommand, SelectedSheet (+11 more)

### Community 133 - "Column_r"
Cohesion: 0.08
Nodes (22): Column_r, AngleDegrees, BottomElevationInFeet, Depth, Diameter, FamilyName, HasValidDimensions, Identifier (+14 more)

### Community 135 - "IColumn"
Cohesion: 0.10
Nodes (19): ColumnAsWall, Column, Identifier, Length, PlanEnd, PlanStart, SectionName, Story (+11 more)

### Community 137 - "PlanPolygon"
Cohesion: 0.12
Nodes (9): PlanRegionQuick, PolygonRecord, PlanPolygon, Area, Bounds, Holes, Outer, TypicalWidth (+1 more)

### Community 138 - "XlsxCellWriter"
Cohesion: 0.15
Nodes (3): XlsxCellWriter, XlsxWorkbook, SheetNames

### Community 142 - "RoutedEventArgs"
Cohesion: 0.10
Nodes (11): ZoneShape, Polygon, Rectangle, maximiseButton, headline, issues, Window, IssueListWindow (+3 more)

### Community 146 - ".ImportEtabsOnce"
Cohesion: 0.07
Nodes (16): SafeLevelLink, AlignedElevationMM, IsPaired, ModelSource, Description, Dot, IsRead, IsStale (+8 more)

### Community 147 - "FindingsList"
Cohesion: 0.07
Nodes (24): RunHistory, Folder, HasPrevious, IsLoaded, LastError, FindingsList, ChangeSummary, Failed (+16 more)

### Community 148 - "Resources"
Cohesion: 0.11
Nodes (14): DiscrepancyScanner.Properties, Resources, Check_, Culture, eye_30, icons8_hide_30, icons8_hide_60, ResourceManager (+6 more)

### Community 149 - ".detectAgainstFloor"
Cohesion: 0.15
Nodes (5): PlacedElement, Element, Placement, RevitId, SurveyBody

### Community 150 - "system_io"
Cohesion: 0.05
Nodes (7): EtabsTextExport, ToolLicensing, EtabsTextModel, EmbeddedKeys, LicensingOptions, ContentKeys, Sections

### Community 151 - "HeightZone"
Cohesion: 0.15
Nodes (11): HeightZone, Area, Description, Height, Id, Label, Name, Outline (+3 more)

### Community 152 - "LoadRegion_R"
Cohesion: 0.18
Nodes (10): LoadRegion_R, Area, HasNumber, Number, NumberSources, NumberText, Parts, RevitID (+2 more)

### Community 153 - "ProjectParameterMappings"
Cohesion: 0.21
Nodes (3): ParameterMapping, ProjectParameterMappings, ParameterMappingTests

### Community 154 - "SlabKind"
Cohesion: 0.14
Nodes (13): SlabKind, Deck, Drop, Footing, LoadOnly, Mat, Opening, Ribbed (+5 more)

### Community 156 - "MachineFingerprint"
Cohesion: 0.18
Nodes (4): ActivationForm, MachineFingerprint, ComponentHashes, DisplayId

### Community 157 - ".IsLineLoadStyle"
Cohesion: 0.14
Nodes (6): Candidate, Lines, Styles, Wording, LineLoadStyleSuggestion, LineLoadStyleSuggestionTests

### Community 158 - "HeightZoneRequest"
Cohesion: 0.14
Nodes (12): HeightZoneRequest, About, CanName, FloorHeightMM, HasProblem, Heading, HeightMM, HeightText (+4 more)

### Community 160 - "EtabsLineLoad"
Cohesion: 0.12
Nodes (14): EtabsLineLoad, Alongside, Claimed, End, Force, FrameName, Key, Length (+6 more)

### Community 161 - ".JunctionAt"
Cohesion: 0.21
Nodes (6): Drawn, End, Start, Wall, PlanSquares, WallCorners

### Community 162 - "WallProfile"
Cohesion: 0.22
Nodes (7): Part, From, Height, Length, To, Varies, WallProfile

### Community 163 - "UserSettings"
Cohesion: 0.14
Nodes (4): UserSettings, FilePath, HasSavedDefaults, LastError

### Community 164 - "LoadPatternRow"
Cohesion: 0.10
Nodes (18): LoadChoice, Category, Name, LoadPatternRow, AreaChoice, Choices, Frames, IsUnused (+10 more)

### Community 165 - "LabelGroupTests"
Cohesion: 0.34
Nodes (3): LabelGroupTests, Stub, LabelAnchor

### Community 166 - "Window"
Cohesion: 0.13
Nodes (13): CanName, HeightText, IsValid, heightBox, Window, About, ShowWhenTrue, HasProblem (+5 more)

### Community 167 - "ModelChooserWindow"
Cohesion: 0.16
Nodes (9): DisplayName, Location, instanceList, problemText, Window, ModelChooserWindow, Chosen, ListBox (+1 more)

### Community 168 - "RevocationGuard"
Cohesion: 0.20
Nodes (7): RevocationGuard, LastGoodCheckUtc, RevokedIds, State, LastGoodTicks, Revoked, Sequence

### Community 169 - "LoadingPlanRow"
Cohesion: 0.12
Nodes (15): LoadingPlanRow, IsReady, LineSheetName, SheetName, State, StatusText, Story, Swatch (+7 more)

### Community 170 - "Window"
Cohesion: 0.14
Nodes (12): AllCommand, ApplyText, CanApply, Levels, NoneCommand, Window, About, Detail (+4 more)

### Community 172 - "XlsxCellEdit"
Cohesion: 0.16
Nodes (6): XlsxCellEdit, Column, Reference, Row, Text, ScheduleEditTests

### Community 174 - "LicenseGate"
Cohesion: 0.24
Nodes (3): LicenseGate, WindowHandle, Handle

### Community 175 - "RevocationList"
Cohesion: 0.17
Nodes (6): LicenseFile, LicenseJson, RevocationList, FormatVersion, Revoked, Sequence

### Community 176 - "List"
Cohesion: 0.09
Nodes (11): LineLoadSchedule, ColumnRoles, Count, Description, Entries, FilePath, Issues, SheetName (+3 more)

### Community 177 - "LicenseStatus"
Cohesion: 0.15
Nodes (13): LicenseStatus, BadSignature, CheckOverdue, ClockRolledBack, Expired, Malformed, Missing, NotConfigured (+5 more)

### Community 179 - "SectionShape"
Cohesion: 0.26
Nodes (5): SectionShape, Rectangular, Round, Unknown, SectionDimensions

### Community 180 - "ScopeTab"
Cohesion: 0.13
Nodes (14): ScopeGroup, HasTitle, Tabs, Title, ScopeTab, Failed, HasOpen, IsActive (+6 more)

### Community 183 - "ViewPalette"
Cohesion: 0.25
Nodes (7): ViewPalette, Accepted, Failed, Passed, Review, UnpairedEtabs, UnpairedRevit

### Community 185 - "GeometryDump"
Cohesion: 0.24
Nodes (4): GeometryDump, Entries, FilePath, IsOpen

### Community 186 - "SlabZoneState"
Cohesion: 0.12
Nodes (11): SlabZoneState, Matched, MissingInEtabs, MissingInRevit, NotChecked, OpeningInEtabsOnly, OpeningInRevitOnly, ThicknessMismatch (+3 more)

### Community 189 - "ViewMode"
Cohesion: 0.15
Nodes (10): ViewMode, Beams, Columns, HeightZones, LineLoads, SidlC, SidlCArea, SlabLoads (+2 more)

### Community 192 - "SlabDepthRegion"
Cohesion: 0.18
Nodes (10): SlabDepthRegion, BottomElevation, HasVariableThickness, IsStacked, Plan, PrimaryRevitId, RevitIds, Thickness (+2 more)

### Community 193 - "Window"
Cohesion: 0.15
Nodes (12): PickHit, DistanceMM, Element, list, Window, DistanceText, Note, Swatch (+4 more)

### Community 195 - "CheckState"
Cohesion: 0.17
Nodes (6): CheckState, Failed, NotChecked, Passed, Review, Unpaired

### Community 196 - "Discrepancy Scanner"
Cohesion: 0.17
Nodes (11): Building, Discrepancy Scanner, Installing, Layout, One API, two programs, PT or RC, Requirements, SAFE models (+3 more)

### Community 199 - "LineLoadPlacement"
Cohesion: 0.22
Nodes (8): LineLoadPlacement, FrameIdentifier, FrameLabel, FrameLength, FrameUniqueName, RelativeFrom, RelativeTo, Value

### Community 200 - "SlabElementViewModel"
Cohesion: 0.20
Nodes (7): SlabElementViewModel, AreaInSquareMetres, IsFilled, LabelAnchor, Plan, SnapEdges, SnapPoints

### Community 201 - "Architecture"
Cohesion: 0.27
Nodes (6): Architecture, Build and test, Notes, What this is, BeamsCheckCommand, ModelCheckCommand

### Community 203 - "eLength"
Cohesion: 0.25
Nodes (8): eLength, cm, ft, inch, m, micron, mm, NotApplicable

### Community 204 - "eForce"
Cohesion: 0.25
Nodes (8): eForce, kgf, kip, kN, lb, N, NotApplicable, tonf

### Community 205 - "ActivationRequest"
Cohesion: 0.22
Nodes (9): ActivationRequest, CreatedUtc, DisplayId, FormatVersion, MachineHashes, MachineName, ProductId, ProductName (+1 more)

### Community 206 - "LoadCoverage"
Cohesion: 0.18
Nodes (5): LoadCoverage, IsEmpty, Regions, Reading, LoadLabel

### Community 208 - "PairCandidate"
Cohesion: 0.17
Nodes (8): PairCandidate, Description, DistanceMM, DistanceText, IsCurrent, Note, RevitId, Title

### Community 209 - "SettingCategory"
Cohesion: 0.25
Nodes (8): SettingCategory, Blurb, HasMatches, IsPane, MatchCount, Name, Pane, Section

### Community 210 - "Badge"
Cohesion: 0.29
Nodes (7): HasOpen, Badge, Chip, Chrome, Row, ShowWhenTrue, Border

### Community 211 - "SidlCAreaState"
Cohesion: 0.29
Nodes (7): SidlCAreaState, EquivalentTotal, Matched, Misplaced, NoEtabsLoad, NoEtabsSlab, ValueMismatch

### Community 212 - "SlabZone.cs"
Cohesion: 0.29
Nodes (6): ConcreteSystem, PostTensioned, Reinforced, Unknown, ConcreteSystems, SlabResultKeys

### Community 214 - "BadgeText"
Cohesion: 0.33
Nodes (6): Open, BadgeText, Label, Text, Label, TextBlock

### Community 215 - ".SetStories"
Cohesion: 0.44
Nodes (3): Stub, LabelAnchor, TabCountTests

### Community 217 - "SidlCAreaBand"
Cohesion: 0.25
Nodes (7): SidlCAreaBand, Agrees, Area, Depth, Share, Value, Weighs

### Community 220 - "labelButton"
Cohesion: 0.40
Nodes (5): HasLabelFields, labelButton, measureButton, showButton, ToggleButton

### Community 222 - "ColumnCheckState"
Cohesion: 0.40
Nodes (5): ColumnCheckState, Matched, Mismatched, Review, Unpaired

### Community 224 - "Overlap"
Cohesion: 0.40
Nodes (5): Overlap, None, Same, StartsTheOther, StartsWithTheOther

### Community 226 - "AreaLoadSummary"
Cohesion: 0.50
Nodes (4): AreaLoadSummary, IsEmpty, NonGravityLoads, UnmappedPatterns

### Community 227 - "asLocation"
Cohesion: 0.50
Nodes (4): asLocation, left, mid, right

### Community 228 - "Chrome"
Cohesion: 0.50
Nodes (3): Chrome, ResourceDictionary, Border

### Community 229 - "MappedParameter"
Cohesion: 0.25
Nodes (7): MappedParameter, Name, Scope, ParameterScope, Instance, Type, TypeThenInstance

### Community 233 - "FrameSource"
Cohesion: 0.29
Nodes (7): FrameSource, AllEtabs, AllRevit, Paired, unknown, UnpairedEtabs, UnPairedRevit

### Community 235 - "RevitLevel"
Cohesion: 0.29
Nodes (6): RevitLevel, DisplayName, ElevationMM, Id, Name, ProjectElevationMM

### Community 237 - "SidlCAreaUnclaimed"
Cohesion: 0.40
Nodes (5): SidlCAreaUnclaimed, Load, Shell, Story, Summary

### Community 238 - "mergeCondittion"
Cohesion: 0.40
Nodes (5): mergeCondittion, EndToEnd, EndToStart, StartToEnd, StartToStart

## Knowledge Gaps
- **2431 isolated node(s):** `LabelAnchor`, `LabelAnchor`, `LabelAnchor`, `LabelAnchor`, `LabelAnchor` (+2426 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 2710 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **64 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ComparisonViewModel` connect `Level and Safe Pairing` to `FindingTally`, `Check Switches and Result Fields`, `Level Binding View Models`, `ETABS Beam and Area Import`, `Slab Loads and Mesh`, `Legacy WinForms GUI`, `.ImportEtabsOnce`, `ViewModelBase`, `FindingsList`, `HeightZone`, `ProjectParameterMappings`, `ElementCard`, `RawDataTests`, `LoadingPlanLink`, `TestMethod`, `ElementViewModel`, `HeightZoneRequest`, `LineLoadCompareEngine`, `LoadingPlansViewModel`, `SelectionPairingTests`, `IDictionary`, `LineLoadResult`, `ElementKind`, `List`, `LineLoad_R`, `SettingsViewModel`, `CrossPair`, `PairingOverrides`, `Slab_R`, `SlabZoneState`, `ReviewNotes`, `CheckWindow`, `.WriteLoadBack`, `PreflightItem`, `CheckState`, `DataContainer`, `IList`, `ExcelWorkbookWriter`, `CsiInstance`, `LoadCoverage`, `PairCandidate`, `SidlCAreaCompareEngine`, `XlsxSheet`, `Story_E`, `RevitEngine`, `.ForgetDecisions`, `Shortcuts`, `.Update`, `system`, `VerdictColour`, `MyTransformation`, `EtabsDataContainer`, `Func`, `.ImportRevit`, `RevitLevel`, `.ImportEtabs`, `LineLoadElementViewModel`, `Wall_E`, `HeightZones`, `Slab_E`?**
  _High betweenness centrality (0.153) - this node is a cross-community bridge._
- **Are the 121 inferred relationships involving `MyPoint` (e.g. with `.ReadProject()` and `.Replay()`) actually correct?**
  _`MyPoint` has 121 INFERRED edges - model-reasoned connections that need verification._
- **What connects `LabelAnchor`, `LabelAnchor`, `LabelAnchor` to the rest of the system?**
  _2431 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Level and Safe Pairing` be split into smaller, more focused modules?**
  _Cohesion score 0.013482587064676617 - nodes in this community are weakly interconnected._
- **Why does `MyPoint` connect `MyPoint` to `Check Switches and Result Fields`, `Level Binding View Models`, `Line Load Checks`, `Thickening Detection`, `ETABS Beam and Area Import`, `SIDL_C Checks`, `Plan Edge Geometry`, `Legacy WinForms GUI`, `.addIssue`, `system_text`, `LineLoadWriteBackTests`, `FrameData_e`, `PlanRing`, `WallCheckTests`, `LoadSchedule`, `RawDataTests`, `TestMethod`, `LineLoadCompareEngine`, `IDrawAble`, `SelectionPairingTests`, `PairedFrame`, `IDictionary`, `SlabLoadCheckTests`, `PlanRegionMath`, `LineLoadFrame_E`, `PairPlace`, `LineLoad_R`, `HeightZoneElementViewModel`, `FrameData_r`, `CrossPair`, `PairingOverrides`, `Slab_R`, `.getPrefrencesData`, `.readSlab`, `SidlCAreaCheckTests`, `.Create`, `IList`, `PairedWall`, `FrameData`, `.TheCheckSaysTheSameOnTheFileAsOnTheModel`, `Column_e`, `.Regions`, `SlabCoverage`, `.Merge`, `SidlCAreaSource_R`, `SlabSystemCheckTests`, `MyTransformation`, `EtabsDataContainer`, `.ImportRevit`, `WallCompareEngine`, `ReviewNote`, `LineLoadElementViewModel`, `SmallZoneCountTests`, `LineLoadStretch`, `SidlCAreaPiece`, `CrossPairTests`, `SupportInsideSlabTests`, `CompareEngine`, `Wall_E`, `HeightZones`, `MessageType`, `Slab_E`, `Wall_R`, `Column_r`, `IColumn`, `HeightZoneTests`, `PlanPolygon`, `.ApplyToPointInMM`, `ColumnOverlayTests`, `RoutedEventArgs`, `SlabBoundaryCharacterizationTests`, `WallCornerTests`, `GraphicsExtension`, `.detectAgainstFloor`, `HeightZone`, `LoadRegion_R`, `.FromPoints`, `EtabsLineLoad`, `.JunctionAt`, `WallProfile`, `Joint`, `SlabMeshCurvedEdgeTests`, `SlabZoneState`, `.LoadsAt`, `SafeLevelTests`, `PlanRingCheck`, `.RevitColumn`, `PairCandidate`, `.Group`?**
  _High betweenness centrality (0.068) - this node is a cross-community bridge._
- **Should `Command and Pick UI` be split into smaller, more focused modules?**
  _Cohesion score 0.012738853503184714 - nodes in this community are weakly interconnected._
- **Why does `CheckWindow` connect `CheckWindow` to `Level and Safe Pairing`, `Command and Pick UI`, `ModelCanvas`, `ShortcutTests`, `RoutedEventArgs`, `ViewModelBase`, `HeightZone`, `DiscrepancyScanner.Wpf`, `Window`, `ModelChooserWindow`, `IDictionary`, `Window`, `SettingsWindow`, `PreflightItem`, `Window`, `Window`, `VerdictColour`, `Window`, `Window`?**
  _High betweenness centrality (0.059) - this node is a cross-community bridge._