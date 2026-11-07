# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A Revit 2025 add-in (.NET Framework 4.7.2, C# 7.3 on the x86 configs) that compares a Revit structural model against its ETABS 22 or SAFE 22 design model and reports discrepancies (beams, columns, walls, slabs, slab loads, line loads, SIDL_C line/area). `README.md` is the detailed behavioural spec for each check (tolerances, rounding rules, what a verdict means) - read the relevant section before changing a check's logic.

## Build and test

Old-style (non-SDK) csproj files, Windows only. Revit and one of ETABS/SAFE must be installed: their assemblies are referenced from the local install (`RevitApiDir`, `EtabsApiDir`, `SafeApiDir`, `CsiApiDir` MSBuild properties; `RevitVersion`/`EtabsVersion`/`SafeVersion` default to 2025/22/22).

```
msbuild DiscrepancyScanner.sln /t:Restore
msbuild DiscrepancyScanner.sln /p:Configuration=Debug
```

Tests are MSTest 2.1.2 (`DiscrepancyScanner.Tests`, net472, packages in `packages/`). Run with Visual Studio Test Explorer or `vstest.console.exe DiscrepancyScanner.Tests\bin\Debug\DiscrepancyScanner.Tests.dll`; one test or class via `/TestCaseFilter:"FullyQualifiedName~SidlCCheckTests"` (or `Name=TestMethodName`). Tests never need Revit or ETABS running - see below.

**New `.cs` files must be added to the csproj by hand** (`<Compile Include="..." />`) - both `DiscrepancyScanner.csproj` and `DiscrepancyScanner.Tests.csproj` list every file explicitly, so an unlisted file silently isn't compiled.

`tools/Check-CsiAttach.ps1` attaches to a running ETABS/SAFE and prints model name and units (checks that COM attach works).

## Architecture

Four projects in `DiscrepancyScanner.sln`:

- **DiscrepancyScanner** - the add-in DLL. Entry points are the two `IExternalCommand`s in `Revit/` (`ModelCheckCommand` opens the WPF `CheckWindow`; `BeamsCheckCommand` is the older beam-only check). Both are registered in `DiscrepancyScanner.addin`. The `.crproj` is the obfuscation project used by the external installer build (it ships inside the Slab Reinforcement installer, not on its own).
- **EtabsTextModel** - reads a saved ETABS text model file (tables) without ETABS running.
- **EtabsTextExport** - an ETABS plugin (`cPlugin`) that writes the open model to the saved model file the add-in can "read from a saved file".
- **DiscrepancyScanner.Tests** - MSTest.

Data flow in the main project:

1. **Import** - `Revit/RevitEngine.cs` (Revit API; ~4k lines) and `Etabs/EtabsEngine.cs` (CSiAPIv1 COM; ~3.5k lines) read each model into plain data classes in `DataContainers/` (`Wall_E`/`Wall_R`, `Slab_E`/`Slab_R`, `Column_e`/`Column_r`, `FrameData_e`/`_r`, ...; suffix `_E`/`_e` = ETABS, `_R`/`_r` = Revit). `DataContainer` is the singleton-ish holder of everything imported and every result.
2. **Compare** - `Comparison/*CompareEngine.cs` pair elements one-to-one by geometry and level, then emit results (`CheckOutcome`/`CheckKind`, `LineLoadResult`, `SlabZone`, ...). Pairing overrides made by hand live in `PairingOverrides`; level/story mapping comes from the user's *alignment*.
3. **Loads** - `Loads/` reads the Revit loading plan/schedules and maps load patterns (`LoadPatternMap`, `LineLoadPatterns`) that the slab-load, line-load and SIDL_C checks compare against.
4. **UI** - `wpf/` is MVVM: `ViewModels/ComparisonViewModel` drives the check window, `Rendering/ModelCanvas` draws the pannable plan, `Services/` holds settings (`%APPDATA%\DiscrepancyScanner\settings.json`), `.dsproject` project files, and `RevitRequestHandler` (an `ExternalEvent` wrapper - any Revit API call from the modeless WPF window must go through it). `gui/` is legacy WinForms.

Key design points that span files:

- **One CSI API for ETABS and SAFE.** `CSiAPIv1.dll` is shared; `CsiProduct` only says which program to attach to. The real difference is the model: ETABS has many stories, SAFE has one floor with no story name, so the SAFE story is named after the Revit level it is paired with in the alignment and the Revit side is narrowed to elements at that level (README "SAFE models").
- **Testability by separation.** Tests run without Revit/ETABS because logic is pulled out of the interop layers into pure helpers (e.g. `Etabs/ModelIndex.cs`, `*CheckInputs.cs`, `PlanRegionMath`). Keep new logic in such helpers and the interop layer thin. Tests share `PrefrencesData.getPrefrencesData()` (a static singleton), so tests set the tolerances they depend on in `[TestInitialize]`.
- **Add-in assembly loading.** `Revit/AddinAssemblies.cs` plus the `EmbedAddinManagerDependencies`/`RecordBuildFolder` targets in the csproj embed `CSiAPIv1.dll` and `EtabsTextModel.dll` and record the build folder, so the add-in still resolves them when Add-in Manager loads the DLL alone from a temp folder. Don't break `AddinAssemblies.Install()` in the command static constructors.
- **ETABS values are read as they are, never averaged** (line loads, SIDL_C area bands); the compare is stretch by stretch/band by band. This is a deliberate invariant, not an oversight.

## Notes

- `test/` holds sample `.rvt` models; `TestResults`, `bin`, `obj` and `packages` are build output, not source to edit.
- Commit messages in this repo are single descriptive sentences in plain prose (see `git log`).
