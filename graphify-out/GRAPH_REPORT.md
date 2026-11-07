# Graph Report - Discrepancy-Scanner  (2026-11-07)

## Corpus Check
- 307 files · ~486,525 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 25 file(s) not represented in the graph (top: .resx 9, .ttf 8, .config 3)

## Summary
- 7174 nodes · 21151 edges · 234 communities (172 shown, 62 thin omitted)
- Extraction: 90% EXTRACTED · 10% INFERRED · 0% AMBIGUOUS · INFERRED: 2042 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Level and Safe Pairing
- Command and Pick UI
- WPF Dependency Properties
- Solution Layout and Check Files
- Check Switches and Result Fields
- Level Binding View Models
- Line Load Checks
- Thickening Detection
- ETABS Beam and Area Import
- Check Outcomes
- Slab Loads and Mesh
- SIDL_C Checks
- Plan Edge Geometry
- Alignment and Categories
- Legacy WinForms GUI
- Community 15
- Community 16
- Community 17
- Community 18
- Community 19
- Community 20
- Community 21
- Community 22
- Community 23
- Community 24
- Community 25
- Community 26
- Community 27
- Community 28
- Community 29
- Community 30
- Community 31
- Community 32
- Community 33
- Community 34
- Community 35
- Community 36
- Community 37
- Community 38
- Community 39
- Community 40
- Community 41
- Community 42
- Community 43
- Community 44
- Community 45
- Community 46
- Community 47
- Community 48
- Community 49
- Community 50
- Community 51
- Community 52
- Community 53
- Community 54
- Community 55
- Community 56
- Community 57
- Community 58
- Community 59
- Community 60
- Community 61
- Community 62
- Community 63
- Community 64
- Community 65
- Community 66
- Community 67
- Community 68
- Community 69
- Community 70
- Community 71
- Community 72
- Community 73
- Community 74
- Community 75
- Community 76
- Community 77
- Community 78
- Community 79
- Community 80
- Community 81
- Community 82
- Community 83
- Community 84
- Community 85
- Community 86
- Community 87
- Community 88
- Community 89
- Community 90
- Community 91
- Community 92
- Community 93
- Community 94
- Community 95
- Community 96
- Community 97
- Community 98
- Community 99
- Community 100
- Community 101
- Community 102
- Community 103
- Community 104
- Community 105
- Community 106
- Community 107
- Community 108
- Community 109
- Community 110
- Community 111
- Community 112
- Community 113
- Community 114
- Community 115
- Community 116
- Community 117
- Community 118
- Community 119
- Community 120
- Community 121
- Community 122
- Community 123
- Community 124
- Community 125
- Community 126
- Community 127
- Community 128
- Community 129
- Community 130
- Community 131
- Community 132
- Community 133
- Community 134
- Community 135
- Community 136
- Community 137
- Community 138
- Community 139
- Community 140
- Community 141
- Community 142
- Community 143
- Community 144
- Community 145
- Community 147
- Community 148
- Community 149
- Community 150
- Community 151
- Community 152
- Community 153
- Community 154
- Community 155
- Community 156
- Community 157
- Community 158
- Community 159
- Community 160
- Community 161
- Community 162
- Community 163
- Community 164
- Community 165
- Community 166
- Community 167
- Community 168
- Community 169
- Community 170
- Community 171
- Community 172
- Community 173
- Community 174
- Community 175
- Community 176
- Community 177
- Community 178
- Community 179
- Community 180
- Community 181
- Community 182
- Community 183
- Community 184
- Community 185
- Community 186
- Community 187
- Community 188
- Community 189
- Community 190
- Community 191
- Community 192
- Community 193
- Community 194
- Community 195
- Community 196
- Community 197
- Community 199
- Community 200
- Community 201
- Community 202
- Community 203
- Community 204
- Community 205
- Community 206
- Community 207
- Community 208
- Community 209
- Community 210
- Community 211
- Community 212
- Community 213
- Community 214
- Community 215
- Community 217
- Community 218
- Community 219
- Community 220
- Community 221
- Community 222
- Community 223
- Community 224
- Community 225
- Community 226
- Community 227
- Community 228
- Community 229
- Community 230

## God Nodes (most connected - your core abstractions)
1. `ComparisonViewModel` - 370 edges
2. `MyPoint` - 356 edges
3. `ElementViewModel` - 211 edges
4. `DiscrepancyScanner` - 200 edges
5. `Window` - 177 edges
6. `RevitEngine` - 172 edges
7. `EtabsEngine` - 158 edges
8. `PlanPolygon` - 143 edges
9. `SlabZone` - 137 edges
10. `GUI` - 115 edges

## Surprising Connections (you probably didn't know these)
- `Stub` --inherits--> `ElementViewModel`  [EXTRACTED]
  DiscrepancyScanner.Tests/AcceptedFindingTests.cs → DiscrepancyScanner/wpf/ViewModels/ElementViewModel.cs
- `Stub` --inherits--> `ElementViewModel`  [EXTRACTED]
  DiscrepancyScanner.Tests/CheckPlaceTests.cs → DiscrepancyScanner/wpf/ViewModels/ElementViewModel.cs
- `Lists` --references--> `Column_e`  [EXTRACTED]
  DiscrepancyScanner.Tests/CrossPairTests.cs → DiscrepancyScanner/DataContainers/Column_e.cs
- `Lists` --references--> `Column_r`  [EXTRACTED]
  DiscrepancyScanner.Tests/CrossPairTests.cs → DiscrepancyScanner/DataContainers/Column_r.cs
- `Lists` --references--> `Wall_E`  [EXTRACTED]
  DiscrepancyScanner.Tests/CrossPairTests.cs → DiscrepancyScanner/DataContainers/Wall_E.cs

## Import Cycles
- None detected.

## Communities (234 total, 62 thin omitted)

### Community 0 - "Level and Safe Pairing"
Cohesion: 0.01
Nodes (157): SafeLevelLink, AlignedElevationMM, IsPaired, ComparisonViewModel, AcceptedCount, AcceptPickedCommand, AcceptToolTip, AddNoteCommand (+149 more)

### Community 1 - "Command and Pick UI"
Cohesion: 0.01
Nodes (158): AcceptButtonText, AcceptPickedCommand, AddNoteToPickedCommand, CancelCommand, CanPairSelection, Card, CheckLabel, CheckProgress (+150 more)

### Community 2 - "WPF Dependency Properties"
Cohesion: 0.03
Nodes (36): Measurement, From, To, ModelCanvas, Background, ChooseFrom, DeselectRequested, DrawingZoneChanged (+28 more)

### Community 4 - "Check Switches and Result Fields"
Cohesion: 0.05
Nodes (19): CheckSwitches, ResultField, Etabs, HasSides, IsFailure, Key, Kind, Label (+11 more)

### Community 5 - "Level Binding View Models"
Cohesion: 0.03
Nodes (53): RevitLevel, DisplayName, ElevationMM, Id, Name, ProjectElevationMM, AlignmentViewModel, AppliedLevelId (+45 more)

### Community 7 - "Thickening Detection"
Cohesion: 0.07
Nodes (16): ThickeningCandidate, ThickeningClassifier, ThickeningConfidence, High, Low, Medium, ThickeningHost, ThickeningOptions (+8 more)

### Community 8 - "ETABS Beam and Area Import"
Cohesion: 0.04
Nodes (12): BeamDesign, EtabsEngine, IgnoredNameList, ImportIssues, IsModelLocked, IsSingleLevel, ModelName, ModelUnitsPerKilonewtonMetre (+4 more)

### Community 9 - "Check Outcomes"
Cohesion: 0.04
Nodes (43): CheckOutcome, Etabs, HasSides, Kind, Label, Level, Revit, Summary (+35 more)

### Community 10 - "Slab Loads and Mesh"
Cohesion: 0.03
Nodes (57): IHasResultFields, ResultFields, SlabMeshPurpose, Loads, Slab, SlabZone, Area, Aside (+49 more)

### Community 11 - "SIDL_C Checks"
Cohesion: 0.05
Nodes (22): SidlCSource_R, BaseElevation, Comment, CommentLoad, Description, ElementName, Kind, Length (+14 more)

### Community 12 - "Plan Edge Geometry"
Cohesion: 0.07
Nodes (12): Reading, EdgeKey, EtabsFace, FaceGrid, Group, Provisional, SlabMeshCompare, CuttingForSlab (+4 more)

### Community 13 - "Alignment and Categories"
Cohesion: 0.03
Nodes (69): Alignment.Status, Applies, Blurb, BoolValue, CategoriesView, ChangedSummary, CheckEveryStory, CheckOneStoryOnly (+61 more)

### Community 15 - "Community 15"
Cohesion: 0.04
Nodes (48): CheckKind, Beam, Dimensions, LineLoadMissingInEtabs, LineLoadMissingInRevit, LineLoads, Location, None (+40 more)

### Community 16 - "Community 16"
Cohesion: 0.07
Nodes (6): EtabsTextModel, LineLoadResultKeys, SidlCAreaResultKeys, SlabResultKeys, RawResultKeys, Sections

### Community 17 - "Community 17"
Cohesion: 0.09
Nodes (10): ConstrainedDelaunay, ConstrainedEdges, Constraints, CrossingStageDidNotConverge, Points, RealPointCount, Triangles, SpatialGrid (+2 more)

### Community 18 - "Community 18"
Cohesion: 0.06
Nodes (3): Form1, ModelTransformation, TranslationForm

### Community 19 - "Community 19"
Cohesion: 0.04
Nodes (49): ShowPreflight, LevelChoice, Detail, IsSelected, Name, LevelChoices, About, AllCommand (+41 more)

### Community 20 - "Community 20"
Cohesion: 0.09
Nodes (6): DocumentTitle, IgnoredNameList, ImportIssues, ReportProgress, ShouldStop, RevitEngine

### Community 22 - "Community 22"
Cohesion: 0.08
Nodes (17): FrameDistributedLoad, CoordinateSystem, Direction, LoadType, Pattern, PressesDown, RelativeFrom, RelativeTo (+9 more)

### Community 23 - "Community 23"
Cohesion: 0.07
Nodes (14): FrameData_e, Diameter, EtabsLabel, Groups, Joints, Shape, SteelLocations, TotalBotSteel (+6 more)

### Community 25 - "Community 25"
Cohesion: 0.05
Nodes (35): LoadingPlanView, DisplayName, Id, Kind, Name, LoadingPlanGuessTests, LoadingPlanGuessRow, BorrowedFrom (+27 more)

### Community 26 - "Community 26"
Cohesion: 0.08
Nodes (9): PlanRing, Area, Bounds, IsCounterClockwise, Points, SignedArea, PlanRegions, PlanPolygonTests (+1 more)

### Community 28 - "Community 28"
Cohesion: 0.04
Nodes (48): MultiSelectionCardTests, Stub, LabelAnchor, ElementCard, AcceptButtonText, CanAccept, CanReview, Count (+40 more)

### Community 29 - "Community 29"
Cohesion: 0.14
Nodes (18): Region, LoadSchedule, ColumnRoles, Count, Description, Entries, FilePath, Issues (+10 more)

### Community 31 - "Community 31"
Cohesion: 0.09
Nodes (3): LoadingPlanLinksTests, LoadingPlanLink, LoadingPlanLinks

### Community 32 - "Community 32"
Cohesion: 0.09
Nodes (3): BeamIgnoreTests, ColumnDetectionTests, LoadingPlanMarkerTests

### Community 33 - "Community 33"
Cohesion: 0.04
Nodes (48): PickHit, DistanceMM, Element, ElementViewModel, CanBeFiled, Caption, Counterpart, Description (+40 more)

### Community 34 - "Community 34"
Cohesion: 0.11
Nodes (18): Assignment, Beside, Contribution, LineLoadCompareEngine, Band, EtabsStretches, HeightZones, Issues (+10 more)

### Community 35 - "Community 35"
Cohesion: 0.05
Nodes (33): ComparasonState, EtabsUnmatched, Matched, RevitUnmatched, Uncategorized, DrawAble, designState, Check_Concrete (+25 more)

### Community 36 - "Community 36"
Cohesion: 0.04
Nodes (40): LoadingPlansViewModel, ClearSearchCommand, CopyToCommand, EditorHeading, GuessCommand, GuessSummary, HasNoSchedule, HasNoStories (+32 more)

### Community 37 - "Community 37"
Cohesion: 0.07
Nodes (19): PairWrite, EtabsUniqueName, RevitId, TwoWayPairing, SelectionPairingTests, Stub, LabelAnchor, PairingEditor (+11 more)

### Community 38 - "Community 38"
Cohesion: 0.06
Nodes (30): PairedFrame, B1_Ratio, B2_Ratio, B3_Ratio, ET_bottom, ET_bottomLeft, ET_bottomRight, ET_topLeft (+22 more)

### Community 39 - "Community 39"
Cohesion: 0.10
Nodes (11): Contents, Decisions, HandPairings, HasAlignment, HeightZones, LoadingPlans, ProjectFile, UserSettings (+3 more)

### Community 40 - "Community 40"
Cohesion: 0.09
Nodes (6): AreaSectionTables, LoadSetRead, Answered, LoadSetTables, PatternValue, LoadSetTableTests

### Community 41 - "Community 41"
Cohesion: 0.11
Nodes (8): AreaLoad_E, Direction, IsGravity, IsToFrame, Pattern, Value, LevelSetup, SlabLoadCheckTests

### Community 42 - "Community 42"
Cohesion: 0.12
Nodes (9): PlanRegionException, PlanRegionMath, SolidOf, PlanPolygon, Area, Bounds, Holes, Outer (+1 more)

### Community 43 - "Community 43"
Cohesion: 0.06
Nodes (30): ScheduleRowViewModel, Cells, Number, ScheduleSheetViewModel, AskWhereToSave, CanSetColumns, ColumnCount, ColumnRoles (+22 more)

### Community 44 - "Community 44"
Cohesion: 0.06
Nodes (6): EtabsForm, EtabsIssuesForm, LinkSelectionForm, SelectedDocuments, SelectedTitles, PrefrencesForm

### Community 45 - "Community 45"
Cohesion: 0.05
Nodes (38): LineLoadResult, Actual, Alongside, Aside, Asker, Bundled, CanWriteBack, Carried (+30 more)

### Community 46 - "Community 46"
Cohesion: 0.09
Nodes (23): LabelGroupTests, Stub, LabelAnchor, ElementKind, Beam, Column, HeightZone, LineLoad (+15 more)

### Community 47 - "Community 47"
Cohesion: 0.07
Nodes (22): LineLoad_E, Direction, IsGravity, IsMoment, IsTrapezoidal, Pattern, RelativeFrom, RelativeTo (+14 more)

### Community 48 - "Community 48"
Cohesion: 0.05
Nodes (40): CopyToCommand, EditorHeading, GuessCommand, GuessSummary, HasNoSchedule, HasNoStories, HasViewMissing, LineSheetName (+32 more)

### Community 49 - "Community 49"
Cohesion: 0.09
Nodes (7): ElementKindKey, Beam, Column, LineLoad, Wall, PairPlace, Story

### Community 50 - "Community 50"
Cohesion: 0.08
Nodes (17): LineLoad_R, HasLine, HasNumber, HasOwnValue, IsOnSeveralStories, Length, LoadName, Midpoint (+9 more)

### Community 51 - "Community 51"
Cohesion: 0.05
Nodes (32): SidlCAreaResult, Actual, AveragedOverWholeFill, Bands, BandText, CoveredShare, EtabsForce, EtabsLoadedArea (+24 more)

### Community 52 - "Community 52"
Cohesion: 0.06
Nodes (16): HeightZoneElementViewModel, Caption, Colour, Height, IsFilled, IsHidden, LabelAnchor, Name (+8 more)

### Community 53 - "Community 53"
Cohesion: 0.05
Nodes (30): SettingsPanelTests, SettingGroup, Blurb, HasBlurb, Header, Items, SettingsViewModel, AlignmentRequested (+22 more)

### Community 54 - "Community 54"
Cohesion: 0.09
Nodes (16): Candidate, Cell, EndGrid, EndpointCompareEngine, Overrides, PairedFrames, ProjectKey, UnmatchedEtabs (+8 more)

### Community 55 - "Community 55"
Cohesion: 0.07
Nodes (26): CrossPair, EtabsAsWall, EtabsColumn, EtabsIsColumn, EtabsLine, EtabsWall, LengthDelta, LocationDelta (+18 more)

### Community 56 - "Community 56"
Cohesion: 0.18
Nodes (4): PairingOverrides, Pin, IsPlaced, HandDecisionPlaceTests

### Community 57 - "Community 57"
Cohesion: 0.06
Nodes (27): Slab_R, BottomElevation, CategoryName, ComparedThickness, ComparedThicknessVaries, Description, ElementName, FamilyName (+19 more)

### Community 58 - "Community 58"
Cohesion: 0.07
Nodes (11): ZoneShape, Polygon, Rectangle, AlignWindow, CheckWindow, DialogOwner, Handle, MinMaxInfo (+3 more)

### Community 59 - "Community 59"
Cohesion: 0.14
Nodes (10): HumanVerdict, Accepted, None, Reviewed, ReviewNote, Accepted, IsEmpty, Reviewed (+2 more)

### Community 61 - "Community 61"
Cohesion: 0.12
Nodes (13): LineLoadRun, Carries, FrameNames, Frames, HasGravityLoad, Identifier, Length, PlanEnd (+5 more)

### Community 63 - "Community 63"
Cohesion: 0.17
Nodes (5): Gathered, Outline, SectionDesignerTables, Table, SectionDesignerTableTests

### Community 64 - "Community 64"
Cohesion: 0.07
Nodes (30): PreflightTests, PreflightItem, Action, ActionText, Detail, FixesItself, HasAction, IsDone (+22 more)

### Community 66 - "Community 66"
Cohesion: 0.07
Nodes (21): RevitRequestHandler, CheckFilterGroup, IsSelected, Kind, Label, Mode, Options, CheckFilters (+13 more)

### Community 68 - "Community 68"
Cohesion: 0.05
Nodes (37): DataContainer, Columns, Columns_r, CrossPairs, EtabsLineLoads, EtabsReadsNullLoadAreas, EtabsSidlC, EtabsSourceName (+29 more)

### Community 69 - "Community 69"
Cohesion: 0.09
Nodes (3): SizeSources, ParameterData, SizeTally

### Community 70 - "Community 70"
Cohesion: 0.06
Nodes (22): SettingItem, Applies, BoolValue, Category, Description, EditorKind, HasApplies, HasDescription (+14 more)

### Community 71 - "Community 71"
Cohesion: 0.08
Nodes (3): EtabsFrameForm, PairedFrameForm, RevitFrameForm

### Community 72 - "Community 72"
Cohesion: 0.10
Nodes (12): CheckTiming, Phase, SlabCompareEngine, Issues, LoadsOnly, LoadZones, OnlyStory, ReportProgress (+4 more)

### Community 73 - "Community 73"
Cohesion: 0.07
Nodes (26): PairedWall, AngleDelta, CoveredLength, Displaced, LengthMatched, LengthShortfall, LocationDelta, LocationMatched (+18 more)

### Community 74 - "Community 74"
Cohesion: 0.13
Nodes (4): PlanClip, MyPoint, AsPointF, Zero

### Community 75 - "Community 75"
Cohesion: 0.06
Nodes (34): ClearHeightCommand, EtabsBaseX, EtabsBaseY, EtabsMirrorX, EtabsMirrorY, EtabsRotationX, EtabsRotationY, HeightSummary (+26 more)

### Community 76 - "Community 76"
Cohesion: 0.10
Nodes (14): cPlugin, CsiInstance, DisplayName, Location, ModelName, ModelPath, ProcessId, Product (+6 more)

### Community 77 - "Community 77"
Cohesion: 0.07
Nodes (23): FrameData, ApproxLength_r, AsBotLeft, AsBotMid, AsBotRight, AsLinksLeft_e, AsLinksLeft_r, AsLinksMid_e (+15 more)

### Community 78 - "Community 78"
Cohesion: 0.09
Nodes (15): EntryRecord, HeightZoneRecord, LineLoadCheckInputs, LineRecord, ScheduleRecord, LineLoadSchedule, ColumnRoles, Count (+7 more)

### Community 79 - "Community 79"
Cohesion: 0.12
Nodes (11): ScheduleColumnOverrides, ActiveProject, Current, ScheduleRole, Description, Live, LiveKind, Number (+3 more)

### Community 80 - "Community 80"
Cohesion: 0.08
Nodes (23): LoadBand, Above, Below, IsSymmetric, SidlCAreaCompareEngine, Band, Issues, LightweightDensity (+15 more)

### Community 81 - "Community 81"
Cohesion: 0.14
Nodes (3): AddinAssemblies, BeamsCheckCommand, ModelCheckCommand

### Community 82 - "Community 82"
Cohesion: 0.15
Nodes (9): Columns, Found, LoadScheduleReader, Pending, XlsxSheet, ColumnCount, Name, RowCount (+1 more)

### Community 83 - "Community 83"
Cohesion: 0.11
Nodes (19): ColumnCompareEngine, HeldUnpaired, Overrides, PairedColumns, ProjectKey, UnmatchedEtabsColumns, UnmatchedRevitColumns, Column_e (+11 more)

### Community 84 - "Community 84"
Cohesion: 0.09
Nodes (17): PairedColumn, DiameterDelta, DimensionsMatched, GroupCheck, LocationDelta, LocationMatched, LongSideDelta, Note (+9 more)

### Community 85 - "Community 85"
Cohesion: 0.12
Nodes (10): PlanBounds, Height, IsEmpty, MaxX, MaxY, MinX, MinY, Width (+2 more)

### Community 86 - "Community 86"
Cohesion: 0.20
Nodes (6): Region, SlabCoverage, AddStackedDepths, Regions, SlabEtabsOpeningTests, SlabShellCutTests

### Community 87 - "Community 87"
Cohesion: 0.12
Nodes (7): Story_E, DisplayName, Elevation, Name, SimilarTo, Joint, Point

### Community 88 - "Community 88"
Cohesion: 0.10
Nodes (21): LoadPatternsViewModel, Choices, HasNoPatterns, HasPatterns, LineNames, LiveHeading, LiveMarking, LiveNames (+13 more)

### Community 89 - "Community 89"
Cohesion: 0.08
Nodes (8): MappedParameter, Name, Scope, ParameterMapping, ParameterScope, Instance, Type, TypeThenInstance

### Community 90 - "Community 90"
Cohesion: 0.15
Nodes (3): SlabSupport, SlabSupports, TriangleMerge

### Community 91 - "Community 91"
Cohesion: 0.08
Nodes (22): AskWhatToForget, ForgetChoices, CanForget, Everything, ForgetButtonText, HasAny, Options, ForgetOption (+14 more)

### Community 92 - "Community 92"
Cohesion: 0.17
Nodes (4): KeyStroke, Key, Modifiers, Shortcuts

### Community 93 - "Community 93"
Cohesion: 0.09
Nodes (15): Stub, LabelAnchor, TabCountTests, LevelRow, Description, Elevation, Failed, FailedBarWidth (+7 more)

### Community 94 - "Community 94"
Cohesion: 0.07
Nodes (27): SnapKind, Centre, Corner, Edge, Endpoint, Free, Intersection, Midpoint (+19 more)

### Community 95 - "Community 95"
Cohesion: 0.07
Nodes (26): AreaChoice, Choices, Frames, HasNoPatterns, HasPatterns, LineNames, LineRead, LiveHeading (+18 more)

### Community 96 - "Community 96"
Cohesion: 0.10
Nodes (17): Reading, DepthFace, SidlCAreaSource_R, Area, AverageThickness, BaseElevation, Description, ElementName (+9 more)

### Community 97 - "Community 97"
Cohesion: 0.14
Nodes (9): VerdictColour, Accepted, EtabsOnly, Failed, Passed, Review, RevitOnly, VerdictColours (+1 more)

### Community 99 - "Community 99"
Cohesion: 0.12
Nodes (7): MyTransformation, Diagnostics, IsMirrored, IsMirroredAboutX, IsMirroredAboutY, Rotation, Translation

### Community 100 - "Community 100"
Cohesion: 0.19
Nodes (3): EtabsDataContainer, EtabsModelFile, SavedModelFileTests

### Community 103 - "Community 103"
Cohesion: 0.15
Nodes (7): ScheduleEdits, Current, XlsxCellEdit, Column, Reference, Row, Text

### Community 104 - "Community 104"
Cohesion: 0.10
Nodes (15): ShortcutAction, Accept, Beams, Columns, HeightZones, LevelDown, LevelUp, LineLoads (+7 more)

### Community 105 - "Community 105"
Cohesion: 0.09
Nodes (21): Action, ActionText, CanFixEverything, CanRunAnyway, FixEverythingText, HasAction, Headline, IsDone (+13 more)

### Community 106 - "Community 106"
Cohesion: 0.08
Nodes (22): HasCandidates, LoadingPlanMarkers, MatchText, PickedView, RequiredText, SelectedAreaSheet, SelectedLineSheet, Window (+14 more)

### Community 107 - "Community 107"
Cohesion: 0.14
Nodes (6): RevitLeaveOut, BeamMarks, BeamTypeNames, SlabNames, WallNames, RevitLeaveOutTests

### Community 108 - "Community 108"
Cohesion: 0.09
Nodes (17): UncoveredWallPiece, Covered, End, Length, Start, Uncovered, Wall, WallCompareEngine (+9 more)

### Community 109 - "Community 109"
Cohesion: 0.12
Nodes (10): ExpectedLineLoad, LineLoadHeight, LineLoadEntry, Description, HeightIndependent, Number, Summary, Value (+2 more)

### Community 110 - "Community 110"
Cohesion: 0.10
Nodes (15): ImportStoppedException, LinkInfo, Title, LinkPlacement, Doc, HostInstanceId, IsAttached, IsNested (+7 more)

### Community 111 - "Community 111"
Cohesion: 0.09
Nodes (19): LineLoadElementViewModel, CanWriteBack, LabelAnchor, LabelDirection, Points, Result, SnapEdges, SnapPoints (+11 more)

### Community 113 - "Community 113"
Cohesion: 0.09
Nodes (18): CanForget, Everything, ForgetButtonText, HasAny, Window, CountText, Detail, IsSelected (+10 more)

### Community 114 - "Community 114"
Cohesion: 0.18
Nodes (5): IPlanLine, PlanEnd, PlanStart, Story, WallGeometry

### Community 115 - "Community 115"
Cohesion: 0.10
Nodes (14): LineLoadStretch, Carried, End, Frames, From, IsFailure, Length, Level (+6 more)

### Community 116 - "Community 116"
Cohesion: 0.08
Nodes (23): SidlCAreaBand, Agrees, Area, Depth, Share, Value, Weighs, SidlCAreaPiece (+15 more)

### Community 117 - "Community 117"
Cohesion: 0.14
Nodes (8): LoadPatternMap, Mapping, LoadCategory, Ll, Lr, Mep, None, Sdl

### Community 120 - "Community 120"
Cohesion: 0.08
Nodes (18): CopySheets, CopyView, SelectSimilarCommand, SourceSheets, SourceView, Window, ApplyCommand, CurrentState (+10 more)

### Community 121 - "Community 121"
Cohesion: 0.11
Nodes (17): ColumnGroupCheck, Description, ExtraGroups, ExtraGroupsText, Groups, GroupsText, HasExtraGroups, MatchedGroup (+9 more)

### Community 122 - "Community 122"
Cohesion: 0.14
Nodes (5): candidatePair, CompareEngine, PairedFrames, RemainingEtabsframes, UnmatchedRevit

### Community 123 - "Community 123"
Cohesion: 0.09
Nodes (16): Wall_E, BaseElevation, DrawingPoints, HasCentreLine, Identifier, Length, PlanEnd, PlanStart (+8 more)

### Community 126 - "Community 126"
Cohesion: 0.10
Nodes (18): MessageType, EtabsSectionName, EtabsUnquiName, PMMRatio1, PMMRatio2, PMMRatio3, PMMRatio4, PMMRatio5 (+10 more)

### Community 127 - "Community 127"
Cohesion: 0.10
Nodes (17): Slab_E, BoundaryPoints, ComparedThickness, Description, IsLoadOnly, IsOpening, Kind, Loads (+9 more)

### Community 128 - "Community 128"
Cohesion: 0.09
Nodes (16): Wall_R, ElementName, HasValidThickness, Identifier, Length, PlanEnd, PlanOutline, PlanStart (+8 more)

### Community 129 - "Community 129"
Cohesion: 0.21
Nodes (5): ScopeChipTests, FindingTally, Groups, Levels, Tabs

### Community 130 - "Community 130"
Cohesion: 0.21
Nodes (3): EtabsTextFile, StringPool, TextLine

### Community 131 - "Community 131"
Cohesion: 0.09
Nodes (19): DiscardKeptCommand, OpenInExcelCommand, ReadAs, ReloadCommand, ResetColumnsCommand, RevertCommand, SaveCommand, SelectedSheet (+11 more)

### Community 132 - "Community 132"
Cohesion: 0.15
Nodes (5): FloorRecord, PatchRecord, PolygonRecord, SlabCheckInputs, SupportRecord

### Community 133 - "Community 133"
Cohesion: 0.09
Nodes (21): Column_r, AngleDegrees, BottomElevationInFeet, Depth, Diameter, FamilyName, HasValidDimensions, Identifier (+13 more)

### Community 135 - "Community 135"
Cohesion: 0.10
Nodes (19): ColumnAsWall, Column, Identifier, Length, PlanEnd, PlanStart, SectionName, Story (+11 more)

### Community 139 - "Community 139"
Cohesion: 0.15
Nodes (5): ScheduleSaveTarget, Cancel, ToolOnly, Workbook, ScheduleSheetWindow

### Community 142 - "Community 142"
Cohesion: 0.14
Nodes (8): maximiseButton, headline, issues, Window, IssueListWindow, ListBox, TextBlock, Button

### Community 147 - "Community 147"
Cohesion: 0.11
Nodes (19): FindingsList, ChangeSummary, Failed, FixedCount, HasAny, HasChanges, HasPicked, IsOpen (+11 more)

### Community 148 - "Community 148"
Cohesion: 0.11
Nodes (14): DiscrepancyScanner.Properties, Resources, Check_, Culture, eye_30, icons8_hide_30, icons8_hide_60, ResourceManager (+6 more)

### Community 149 - "Community 149"
Cohesion: 0.15
Nodes (5): PlacedElement, Element, Placement, RevitId, SurveyBody

### Community 151 - "Community 151"
Cohesion: 0.15
Nodes (10): HeightZone, Area, Description, Height, Id, Label, Name, Outline (+2 more)

### Community 152 - "Community 152"
Cohesion: 0.18
Nodes (10): LoadRegion_R, Area, HasNumber, Number, NumberSources, NumberText, Parts, RevitID (+2 more)

### Community 154 - "Community 154"
Cohesion: 0.14
Nodes (13): SlabKind, Deck, Drop, Footing, LoadOnly, Mat, Opening, Ribbed (+5 more)

### Community 157 - "Community 157"
Cohesion: 0.21
Nodes (6): Candidate, Lines, Styles, Wording, LineLoadStyleSuggestion, LineLoadStyleSuggestionTests

### Community 158 - "Community 158"
Cohesion: 0.12
Nodes (13): AskForHeightZone, HeightZoneRequest, About, CanName, FloorHeightMM, HasProblem, Heading, HeightMM (+5 more)

### Community 160 - "Community 160"
Cohesion: 0.12
Nodes (14): EtabsLineLoad, Alongside, Claimed, End, Force, FrameName, Key, Length (+6 more)

### Community 161 - "Community 161"
Cohesion: 0.21
Nodes (6): Drawn, End, Start, Wall, PlanSquares, WallCorners

### Community 162 - "Community 162"
Cohesion: 0.22
Nodes (7): Part, From, Height, Length, To, Varies, WallProfile

### Community 164 - "Community 164"
Cohesion: 0.12
Nodes (15): LoadPatternRow, AreaChoice, Choices, Frames, IsUnused, LineRead, Name, Note (+7 more)

### Community 165 - "Community 165"
Cohesion: 0.12
Nodes (14): AreaAssignRow, EtabsTextTables, AreaAssigns, Found, Issues, Path, ShellLoadSetAssignments, ShellUniformLoadSets (+6 more)

### Community 166 - "Community 166"
Cohesion: 0.13
Nodes (13): CanName, HeightText, IsValid, heightBox, Window, About, ShowWhenTrue, HasProblem (+5 more)

### Community 167 - "Community 167"
Cohesion: 0.17
Nodes (9): DisplayName, Location, instanceList, problemText, Window, ModelChooserWindow, Chosen, ListBox (+1 more)

### Community 168 - "Community 168"
Cohesion: 0.23
Nodes (5): ScheduleNames, Current, LiveDisplay, SidlDisplay, TotalDisplay

### Community 169 - "Community 169"
Cohesion: 0.12
Nodes (15): LoadingPlanRow, IsReady, LineSheetName, SheetName, State, StatusText, Story, Swatch (+7 more)

### Community 170 - "Community 170"
Cohesion: 0.14
Nodes (12): AllCommand, ApplyText, CanApply, Levels, NoneCommand, Window, About, Detail (+4 more)

### Community 171 - "Community 171"
Cohesion: 0.21
Nodes (3): Columns, Found, LineLoadScheduleReader

### Community 174 - "Community 174"
Cohesion: 0.14
Nodes (13): ScheduleCellViewModel, Changed, Column, DiffersFromFile, FileText, IsFormula, IsKept, IsPending (+5 more)

### Community 176 - "Community 176"
Cohesion: 0.20
Nodes (4): LineLoadPatterns, Description, IsEmpty, Names

### Community 177 - "Community 177"
Cohesion: 0.19
Nodes (5): RunHistory, Folder, HasPrevious, IsLoaded, LastError

### Community 179 - "Community 179"
Cohesion: 0.23
Nodes (5): SectionShape, Rectangular, Round, Unknown, SectionDimensions

### Community 180 - "Community 180"
Cohesion: 0.14
Nodes (10): ScopeTab, Failed, HasOpen, IsActive, KeyText, Label, Mode, Open (+2 more)

### Community 181 - "Community 181"
Cohesion: 0.23
Nodes (6): ElementSource, EtabsOnly, Paired, RevitOnly, UnmatchedEtabs, UnmatchedRevit

### Community 183 - "Community 183"
Cohesion: 0.23
Nodes (7): ViewPalette, Accepted, Failed, Passed, Review, UnpairedEtabs, UnpairedRevit

### Community 185 - "Community 185"
Cohesion: 0.22
Nodes (4): GeometryDump, Entries, FilePath, IsOpen

### Community 186 - "Community 186"
Cohesion: 0.15
Nodes (10): SlabZoneState, Matched, MissingInEtabs, MissingInRevit, NotChecked, OpeningInEtabsOnly, OpeningInRevitOnly, ThicknessMismatch (+2 more)

### Community 189 - "Community 189"
Cohesion: 0.18
Nodes (10): ViewMode, Beams, Columns, HeightZones, LineLoads, SidlC, SidlCArea, SlabLoads (+2 more)

### Community 191 - "Community 191"
Cohesion: 0.18
Nodes (11): LineLoadState, Matched, NoEtabsFrame, NoEtabsLoad, NoExpectedValue, NoNumber, NotChecked, NotInSchedule (+3 more)

### Community 192 - "Community 192"
Cohesion: 0.18
Nodes (10): SlabDepthRegion, BottomElevation, HasVariableThickness, IsStacked, Plan, PrimaryRevitId, RevitIds, Thickness (+2 more)

### Community 193 - "Community 193"
Cohesion: 0.22
Nodes (9): list, Window, DistanceText, Note, Swatch, Title, ElementChooserWindow, Chosen (+1 more)

### Community 195 - "Community 195"
Cohesion: 0.20
Nodes (9): SlabLoadState, ConflictingRegions, Matched, Mismatch, NoEtabsLoad, NoLoadingRegion, NoLoadNumber, NotChecked (+1 more)

### Community 196 - "Community 196"
Cohesion: 0.20
Nodes (3): LoadChoice, Category, Name

### Community 199 - "Community 199"
Cohesion: 0.22
Nodes (8): LineLoadPlacement, FrameIdentifier, FrameLabel, FrameLength, FrameUniqueName, RelativeFrom, RelativeTo, Value

### Community 203 - "Community 203"
Cohesion: 0.25
Nodes (8): eLength, cm, ft, inch, m, micron, mm, NotApplicable

### Community 204 - "Community 204"
Cohesion: 0.25
Nodes (8): eForce, kgf, kip, kN, lb, N, NotApplicable, tonf

### Community 205 - "Community 205"
Cohesion: 0.25
Nodes (8): eLength, cm, ft, inch, m, micron, mm, NotApplicable

### Community 208 - "Community 208"
Cohesion: 0.25
Nodes (8): PairCandidate, Description, DistanceMM, DistanceText, IsCurrent, Note, RevitId, Title

### Community 209 - "Community 209"
Cohesion: 0.25
Nodes (8): SettingCategory, Blurb, HasMatches, IsPane, MatchCount, Name, Pane, Section

### Community 210 - "Community 210"
Cohesion: 0.29
Nodes (7): HasOpen, Badge, Chip, Chrome, Row, ShowWhenTrue, Border

### Community 211 - "Community 211"
Cohesion: 0.29
Nodes (7): SidlCAreaState, EquivalentTotal, Matched, Misplaced, NoEtabsLoad, NoEtabsSlab, ValueMismatch

### Community 212 - "Community 212"
Cohesion: 0.29
Nodes (5): ConcreteSystem, PostTensioned, Reinforced, Unknown, ConcreteSystems

### Community 214 - "Community 214"
Cohesion: 0.33
Nodes (6): Open, BadgeText, Label, Text, Label, TextBlock

### Community 215 - "Community 215"
Cohesion: 0.33
Nodes (6): Piece, Length, Line, PlanEnd, PlanStart, Story

### Community 217 - "Community 217"
Cohesion: 0.33
Nodes (6): VerdictKeyEntry, Colour, IsFixed, Label, Swatch, ToolTip

### Community 219 - "Community 219"
Cohesion: 0.33
Nodes (6): EtabsTable, AreaAssigns, Controls, None, ShellLoadSetAssignments, ShellUniformLoadSets

### Community 220 - "Community 220"
Cohesion: 0.40
Nodes (5): HasLabelFields, labelButton, measureButton, showButton, ToggleButton

### Community 222 - "Community 222"
Cohesion: 0.40
Nodes (5): ColumnCheckState, Matched, Mismatched, Review, Unpaired

### Community 224 - "Community 224"
Cohesion: 0.40
Nodes (5): Overlap, None, Same, StartsTheOther, StartsWithTheOther

### Community 226 - "Community 226"
Cohesion: 0.50
Nodes (4): AreaLoadSummary, IsEmpty, NonGravityLoads, UnmappedPatterns

### Community 227 - "Community 227"
Cohesion: 0.50
Nodes (4): asLocation, left, mid, right

### Community 228 - "Community 228"
Cohesion: 0.50
Nodes (3): Chrome, ResourceDictionary, Border

### Community 229 - "Community 229"
Cohesion: 0.67
Nodes (3): paraType, INSTANCE, TYPE

## Knowledge Gaps
- **2379 isolated node(s):** `LabelAnchor`, `LabelAnchor`, `LabelAnchor`, `LabelAnchor`, `LabelAnchor` (+2374 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 2638 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **62 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ComparisonViewModel` connect `Level and Safe Pairing` to `Community 129`, `Check Switches and Result Fields`, `Level Binding View Models`, `Community 132`, `ETABS Beam and Area Import`, `Plan Edge Geometry`, `Community 15`, `Community 146`, `Community 19`, `Community 20`, `Community 147`, `Community 22`, `Community 151`, `Community 24`, `Community 153`, `Community 18`, `Community 28`, `Community 158`, `Community 31`, `Community 30`, `Community 33`, `Community 34`, `Community 36`, `Community 37`, `Community 39`, `Community 45`, `Community 46`, `Community 176`, `Community 177`, `Community 50`, `Community 53`, `Community 55`, `Community 56`, `Community 186`, `Community 59`, `Community 58`, `Community 64`, `Community 66`, `Community 195`, `Community 68`, `Community 202`, `Community 76`, `Community 78`, `Community 79`, `Community 80`, `Community 81`, `Community 87`, `Community 216`, `Community 217`, `Community 90`, `Community 91`, `Community 93`, `Community 97`, `Community 99`, `Community 100`, `Community 104`, `Community 107`, `Community 111`, `Community 123`, `Community 125`, `Community 127`?**
  _High betweenness centrality (0.144) - this node is a cross-community bridge._
- **Are the 121 inferred relationships involving `MyPoint` (e.g. with `.ReadProject()` and `.Replay()`) actually correct?**
  _`MyPoint` has 121 INFERRED edges - model-reasoned connections that need verification._
- **What connects `LabelAnchor`, `LabelAnchor`, `LabelAnchor` to the rest of the system?**
  _2379 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Level and Safe Pairing` be split into smaller, more focused modules?**
  _Cohesion score 0.01266085512660855 - nodes in this community are weakly interconnected._
- **Why does `MyPoint` connect `Community 74` to `Level and Safe Pairing`, `Check Switches and Result Fields`, `Level Binding View Models`, `Line Load Checks`, `Thickening Detection`, `Check Outcomes`, `Slab Loads and Mesh`, `SIDL_C Checks`, `Plan Edge Geometry`, `Legacy WinForms GUI`, `Community 18`, `Community 20`, `Community 21`, `Community 22`, `Community 23`, `Community 26`, `Community 27`, `Community 29`, `Community 30`, `Community 34`, `Community 35`, `Community 37`, `Community 38`, `Community 39`, `Community 41`, `Community 42`, `Community 47`, `Community 49`, `Community 50`, `Community 51`, `Community 54`, `Community 55`, `Community 56`, `Community 57`, `Community 60`, `Community 61`, `Community 62`, `Community 65`, `Community 67`, `Community 73`, `Community 77`, `Community 78`, `Community 83`, `Community 85`, `Community 86`, `Community 87`, `Community 90`, `Community 96`, `Community 98`, `Community 99`, `Community 100`, `Community 107`, `Community 108`, `Community 111`, `Community 112`, `Community 114`, `Community 115`, `Community 118`, `Community 119`, `Community 122`, `Community 123`, `Community 125`, `Community 126`, `Community 127`, `Community 128`, `Community 133`, `Community 135`, `Community 136`, `Community 137`, `Community 140`, `Community 141`, `Community 142`, `Community 143`, `Community 144`, `Community 145`, `Community 149`, `Community 151`, `Community 152`, `Community 158`, `Community 160`, `Community 161`, `Community 162`, `Community 163`, `Community 182`, `Community 187`, `Community 188`, `Community 194`, `Community 200`, `Community 207`, `Community 215`, `Community 218`?**
  _High betweenness centrality (0.077) - this node is a cross-community bridge._
- **Should `Command and Pick UI` be split into smaller, more focused modules?**
  _Cohesion score 0.012578616352201259 - nodes in this community are weakly interconnected._
- **Why does `ElementViewModel` connect `Community 33` to `Level and Safe Pairing`, `WPF Dependency Properties`, `Check Switches and Result Fields`, `Check Outcomes`, `Slab Loads and Mesh`, `Community 141`, `Community 15`, `Community 19`, `Community 147`, `Community 151`, `Community 24`, `Community 28`, `Community 30`, `Community 160`, `Community 163`, `Community 37`, `Community 45`, `Community 46`, `Community 177`, `Community 52`, `Community 181`, `Community 183`, `Community 59`, `Community 189`, `Community 193`, `Community 90`, `Community 93`, `Community 94`, `Community 111`, `Community 112`?**
  _High betweenness centrality (0.066) - this node is a cross-community bridge._