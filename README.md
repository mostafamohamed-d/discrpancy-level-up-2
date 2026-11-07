# Discrepancy Scanner

A Revit add-in that compares a Revit structural model against its ETABS or SAFE design model
and reports where the two disagree.

The two models are built by different people for different purposes, and they drift. This
add-in pairs the elements up and reports the differences that matter:

- **Beams** — reinforcement marks against the ETABS design output.
- **Columns** — location, orientation, and section.
- **Walls** — thickness, and how far off the other model's line they stand.
- **Slabs** — thickness, PT or RC, extent, and openings.
- **Slab loads** — the SDL, LL, LR and MEP on each ETABS shell against a Revit loading
  plan and its schedule.
- **SIDL_C Line** — the SIDL_C line loads on the ETABS frames against the weight of the
  Revit walls and fill strips that are carried as load rather than modelled.
- **SIDL_C Area** — the SIDL_C area loads on the ETABS slabs against the weight of the Revit
  light weight fill floors over them, ramps included.

Results are shown in a plan view you can pan, zoom, and filter, with each finding selectable
back in the Revit model.

The six colours in the key along the top - fails, needs a look, agrees, ETABS only, Revit only
and agreed - are yours to change: right-click one to pick another. The plan, the switches, the
findings list and the counts all follow, and the choice is kept in your settings file. The
defaults are in the picker's custom colours, so one click puts a colour back.

**Raw data**, the box beside the tabs, swaps the check for the two models as they were read:
every ETABS element in the ETABS colour and every Revit one in the Revit colour, laid over each
other with nothing paired and nothing judged. The tabs still choose what is drawn, and LABEL
offers what each element was read as - name, section, size, thickness, loads - so the two can be
read against each other by eye. It is for what the check cannot say about itself: a pairing that
went wrong, a level paired with the wrong view, a size read off the wrong parameter. Nothing is
counted, reviewed, accepted or paired while it is on. The loading plans are read when the check
runs, so until then the two load tabs have only the ETABS side on them.

Labels on anything drawn as a line - beams, line loads, walls - run along it and sit beside it,
the ETABS label on one side and the Revit label on the other, so two models' labels on the same
line do not cover each other. They are turned to read left to right, or upwards, never upside
down.

The keys are set under Settings, Keyboard shortcuts: click a box and press the key, with Ctrl,
Shift or Alt if wanted, or two keys one straight after the other for a pair; Backspace takes a
key away. Out of the box Page Up and Page Down step a level up and down, 1 to 9 switch the tabs
on and off - beams, columns, walls and slabs under Geometry, slab loads and line loads under Loads,
height zones (7, drawn last on its own), SIDL_C Line (8) and SIDL_C Area (9, both under Loads) - R switches the raw data, A A accepts what is selected (or takes it back) and P P
pairs the two selected. The second key of a pair has to follow within a second and a half, and a
key held down never counts as pressed twice. None of them works while something is being typed.

**Height zones** are patches of a level where the line loads take a floor to floor height of
their own - a double height lobby, a taller plant storey. With the Height zones tab up, draw one
on the level on show as a rectangle (two opposite corners) or a polygon (every corner, then the
first again, a double click or Enter), snapped to the model like a measurement, and give it a
height in metres and a name if you like. The height replaces the level's floor to floor height
and nothing else: the allowance for the slab still comes off it, and a full height load still
runs the whole of it. A line takes the height of whatever covers most of its length - a zone, or
the level itself - and where two zones overlap the smaller one answers inside it (two the same
size: the one drawn first). A zone's colour is its height's - one colour per height, the same on
every level, listed in the key while the tab is on - and each zone is coloured in only where it
is the zone in control: where a smaller one overlaps it, that part shows the smaller one's
colour alone. A zone's card lists the zones it overlaps and which of them is used there. The
zones are only on the plan while their tab is on. A line load's
card says which zone it took its height from. Select a zone to change its height, copy it to
other levels, or delete it (the Delete key too). Only the line load check reads the zones; they
are saved with the project, and after a change the line loads are checked again straight away,
off the loading plans already read.

**ETABS line loads are read as they are, never averaged.** Each frame is read on its own and each
load on it where it is, and a drawn line is compared stretch by stretch against the value ETABS
carries there. A load spread or averaged differently from the plan - several drawn loads merged
into one, a stepped run modelled as one straight frame with the value scaled up - is a difference
like any other, however well the total forces agree; there is no setting that accepts it. A line
over frames carrying different loads says each of them, frame by frame ("1.5 kN/m on B12, 3 kN/m
on B13"), and never a figure in between that no frame carries.

**SIDL_C Line** is the line load check for the walls the wall check leaves out. An upstand, a planter
or pool wall, a partition - anything named under Settings, SIDL_C Line, which starts as the
wall check's own leave-out list - is not structure, so ETABS carries it as a line load in the
SIDL_C pattern on the frame it stands on. So does a strip of fill: a floor named "Fill SLAB"
narrower than 500 mm is read as a line down its middle. Each is weighed - thickness times height
(or width) times 25 kN/m3, rounded up like a plan line load - and read against the SIDL_C loads
on the frames under it exactly the way a drawn line load is, frame by frame and stretch by
stretch. A wall's height is read off its elevation, place by place: an upstand whose profile
was edited is cut where its height changes, and each length is weighed at its own height (a
slope at its average, a door through it as nothing). A wall loads the story it stands on, not
the one it rises to. ETABS may carry up to
3 kN/m more than the weight and still agree, but only 0.3 kN/m less; both are settings. Two other
ways to set the value, both under Settings, SIDL_C Line: intervals ("2, 6, 10, 14, 18") the weight
is stepped up to - a wall weighing 3 kN/m is checked as 6, one past the last interval as it is -
and taking the load from the element's instance Comments where it states one ("6" or "6 kN/m"),
falling back to the geometry where it does not. No
loading plan is needed, so a SIDL_C load on a frame with nothing standing over it is reported on
every level the check covers. The walls and strips are read with the Revit model, so a change
to the names needs an import. The tab is never up at the same time as Line loads: the two are
different questions about the same frames.

**SIDL_C Area** weighs the light weight concrete fill. A floor named "Light Weight Fill" (a setting)
is not structure and not on a loading plan: ETABS carries it as an area load in SIDL_C on the
slab under it. Its weight is its average depth - its volume over its plan area - times the light
weight density, plus the topping times the difference between the two densities: a ramp from 0 to
500 mm is 0.25 x 10 + 0.1 x 15 = 4 kN/m2 with the defaults (10 and 25 kN/m3, 100 mm of topping),
then rounded up to the next 0.5 kN/m2 (a setting; 0 leaves it unrounded), the way it is typed into ETABS.
ETABS cannot carry a ramp, so the engineer averages it - whole, in halves, in five strips. The ETABS
values are taken exactly as they are, never averaged: each distinct value under the fill is a band,
and each band is checked against what the fill weighs over that band's own footprint, from the
fill's depth there - read off its own top and bottom faces. A 0 to 500 mm ramp in halves is 2.75
over the thin half and 5.25 over the deep; averaged whole it is 4 over all of it; both pass, and
the same halves the wrong way round fail. Shells drawn over each other are added the way the slab
load check adds them. Only the part of the fill standing on ETABS slab is read - unless that is
less than half of it, when the whole fill is read with the bare part as carrying nothing, so a fill
with no slab and no load under it is reported as its load not being in ETABS. ETABS may carry up
to 1 kN/m2 more and 0.5 kN/m2 less. Where the bands are wrong because a good part of the load lies
outside the fill, but the total force on the areas it stands on - all of each, shared between fills
on one panel - agrees within the same tolerance over the fill's area, the load was put on a bigger
or shifted area, and it is put up for review rather than failed. A fill with no SIDL_C under it and
SIDL_C on an area within 2 m of it (a setting) with no fill over it is reported once, as its load
drawn in the wrong place. Any other ETABS area carrying SIDL_C with no fill over any of it is
reported on its own. On the plan the fill and the SIDL_C areas counted towards it are cut along
each other's edges, as the slab load check cuts its plan: each band of the fill in its own verdict,
fill with no SIDL_C under it in the Revit only colour, and SIDL_C outside the fill in the ETABS
only colour. The tab is never up with either slab tab, since all three draw regions of floor.

**Assign load in ETABS** (or SAFE), on the card of a drawn line load or a SIDL_C wall or fill
strip, puts the Revit value on the design model. Only a load paired with ETABS frames can go:
it is assigned to those frames by their unique names, as uniform gravity loads in the pattern the
check reads - the one the frame is already loaded in, or else the first in the model the settings
name. Each frame is written whole from the plan: every drawn line on it goes on at its own value
over its own stretch, never one load averaged over several, and where lines overlap they are
written added together, as the check adds them. A stretch stopping short of the frame's end, or of
the next line, by less than the shortest difference the check reports is run to it. What the frame
carried under the lines in the patterns the check reads is replaced; where no line is drawn, and
in every other pattern, it is left alone. A frame also carrying a line with no value worked out is
not written.
It is written to the model that was imported, while it is still open, and otherwise to one picked
from what is running; a frame whose label has changed since the import stops it. What will be
written is said first and nothing is written until that is agreed to, and a model holding analysis
results is only unlocked once that has been said too. Afterwards the frames are read again and
the tab checked again with what they now carry.

**Forget...**, under Set up, What is checked, asks what to forget on this project: the
acceptances, the elements marked reviewed, the notes, the pairings set by hand, the loading plan
pairings of the levels, the height zones - any of them, or everything. Nothing starts ticked,
each line says how many there are, and Enter cancels. Other projects, the models and the
alignment are not touched.

**Export project...** and **Import project...**, beside it, carry everything decided by hand on
a project in one `.dsproject` file: the alignment (with the SAFE level it pairs), the loading plan pairings,
the pairings set by hand, the reviews, acceptances and notes, and the height zones - to hand to a
colleague, move to another machine or keep with the job. Imported, it goes into whatever project
is open, in place of what that project had, after saying what the file holds; the alignment is
only replaced where the file has one.

## Requirements

| | |
|---|---|
| Revit | 2025 |
| ETABS | 22 |
| SAFE | 22 |
| .NET Framework | 4.7.2 |
| Build | Visual Studio 2022 (or MSBuild 17) |

The Revit and CSI SDK assemblies are not redistributable, so they are referenced from the
local product install rather than checked into this repository. Revit must be installed to
build, and so must one of ETABS or SAFE - either will do, for the reason in
[One API, two programs](#one-api-two-programs).

## Building

```
msbuild DiscrepancyScanner.sln /t:Restore
msbuild DiscrepancyScanner.sln /p:Configuration=Release
```

If your Revit or ETABS install is elsewhere, or you are targeting a different version,
override the lookup on the command line:

```
msbuild DiscrepancyScanner.sln /p:RevitVersion=2024 /p:EtabsVersion=21 /p:SafeVersion=21
```

or point at the directories directly with `/p:RevitApiDir=...`, `/p:EtabsApiDir=...` and
`/p:SafeApiDir=...`. The API assembly itself comes from `/p:CsiApiDir=...`, which defaults to
the ETABS install and falls back to the SAFE one.

## One API, two programs

ETABS and SAFE are read through the same code, because they answer the same interfaces. CSI
ships one assembly, `CSiAPIv1.dll`, with every one of its products, and the ETABS copy and the
SAFE copy are the same API: identical COM interface GUIDs, identical method signatures, and
identical names and values behind every enum the import reads. Only the namespace differs from
the `ETABSv1.dll` this add-in used to reference.

So there is one import engine rather than two, and what tells the programs apart is not the API
but the model. ETABS is a building: many stories, and it says which one every object is on.
SAFE is one floor, drawn about its own origin, with nothing in it that names the floor. That
single difference is the whole of the SAFE support.

Which build of `CSiAPIv1.dll` ships with the add-in does not matter, since the interfaces are
the same, so the build takes whichever product is installed. What the interface comparison
cannot settle on its own is whether a *running* program answers to those interfaces through the
COM registry; `tools/Check-CsiAttach.ps1` attaches to whatever is open and prints the model name
and units, which settles it.

## SAFE models

Pick the program on the import panel, under **Models & alignment**. Importing then asks which
running model to read, listing every ETABS and SAFE instance open on the machine with the model
each one holds, so a machine with two models open reads the one that was meant.

A SAFE model needs one thing an ETABS model does not: the Revit level it stands for. ETABS says
what floor every object is on and the check reads it; SAFE holds one floor and says nothing
about which floor of which building it is. That level is not asked for on its own: the alignment
already pairs a design story with a Revit level to work out its height, and a SAFE model has
exactly one story, so pairing that story with a level in the alignment is the whole of it. One
place to say which floor it is, saved with the alignment, rather than two that could disagree.

The SAFE model is not moved. Its one story stands where its floor is drawn - nought for a model
drawn about its own origin, which is how SAFE starts one, or the real elevation for one drawn at
its level - and the alignment's height puts the Revit level on it, exactly as it does for an
ETABS story. The story is named after the paired level, so everything keyed by story - the level
rail, the loading plan pairing, the story a Revit floor is assigned to - names the same floor in
both models; a pairing made after the SAFE model was read takes that name when it is imported
again. Before it is paired the story is simply called SAFE, so it can be picked in the alignment.

The Revit side then has to be narrowed, and this is the part that is easy to get wrong. Every
Revit element is named after the nearest story, and a SAFE model has exactly one - so nearest
to the only story is *every* story, and without narrowing the whole Revit building would be
named after the one floor SAFE holds and compared against it. So only the elements standing at
the paired level are kept. Standing at, not sitting on: a floor is rarely drawn on its level -
a slab hangs below it, a beam is dropped for an upstand, a column stops short of it - so the
test is how far off the level an element stands, and how far is *How far off the level still
counts as it* under **Settings > Single level models**. What it must not reach is the floor
above or below. The import says how many elements of each kind were left out, because dropping
most of the Revit model is the right thing to do here and an alarming thing to find had happened
without being told.

## Installing

The add-in ships in Slab Reinforcement's installer rather than one of its own. Running
`..\SRV\Installer\rebuild-all.cmd` builds this repository with the rest, obfuscates the DLL
(`DiscrepancyScanner/DiscrepancyScanner.crproj`), and packs it into the one `Install.cmd` that
installs every product. For the current user and with no administrator rights, that installs
it for Revit 2025 and 2026:

```
%APPDATA%\Autodesk\Revit\Addins\<year>\DiscrepancyScanner.addin
%APPDATA%\Autodesk\Revit\Addins\<year>\DiscrepancyScanner\DiscrepancyScanner.dll  (+ EtabsTextModel.dll, CSiAPIv1.dll, ...)
```

The installed manifest is `DiscrepancyScanner/DiscrepancyScanner.addin` with the `<Assembly>`
path pointed at that folder, so the command classes are only ever written down here.
`Uninstall.cmd` takes it out again. Restart Revit; the commands appear under
**Add-Ins > External Tools**.

To install a build by hand instead, copy `DiscrepancyScanner.dll`, `EtabsTextModel.dll` **and
`CSiAPIv1.dll`** from the build output to `C:\Addins\`, and
`DiscrepancyScanner/DiscrepancyScanner.addin` to `%APPDATA%\Autodesk\Revit\Addins\2025\`.

User settings are stored at `%APPDATA%\DiscrepancyScanner\settings.json`.

## The wall check

Walls are checked alongside the beams and the columns: the same **Beams, columns and walls**
mode, the same run, the same plan. Three things are compared — thickness, whether the wall is
where the other model has it, and whether it runs the length the other model gives it — and
each gets a result and a switch of its own. Reinforcement will follow.

ETABS has no wall element. A wall there is an area object whose design orientation is Wall,
which in plan collapses onto a line; the two corners furthest apart are that line, so a panel
stepped at the top, one sloping down to a footing, and one whose corners were drawn in an odd
order all read the same. Its thickness is the thickness of the section property assigned to
it, read once per property however many areas carry it.

On the Revit side a wall is its location line and the layers its type declares. Only the
layers marked as structure are counted, so a wall type carrying its finishes does not read as
thicker than the concrete in it — a 200 wall with 20 of plaster either side would fail against
the ETABS 200 every time. Curtain and stacked walls are left out, and so are partitions unless
*Settings > Wall check* says otherwise: a wall Revit does not call bearing or shear is not
something ETABS was ever going to have.

Which story a panel is on is read from the model rather than worked out from it. A wall is
meshed up its height as well as along its length, and a panel cut half way up a story tops out
exactly between the level it belongs to and the one below — derived from the geometry, which
floor it lands on then comes down to which way a tie falls, and the panel would be checked
against, and drawn on, the wrong one. ETABS assigns every area to a story and says so for all
of them in one call, the way the frames are already read; the geometry is only the fallback,
and the import says how many panels needed it.

The pairing has to cope with the two models splitting a wall run in different places. ETABS
meshes a run into panels wherever it wants a node, and Revit splits a wall wherever the person
drawing it stopped, so one wall on the drawings is commonly several areas in one model and two
walls in the other. A Revit wall may therefore be claimed by as many ETABS panels as stand
along it, each checked against it on its own; a second pass then goes the other way for the
Revit walls no panel picked, so a wall split in two where ETABS has one panel gets a verdict
on both halves. What is left after both passes — a wall with nothing at all on its line — is
what gets reported as unpaired.

What the plan draws is the ETABS model: each checked wall is drawn on its ETABS panel, at the
ETABS thickness, because ETABS is what is being checked against and the panels are what carry
the verdicts. A paired Revit wall is never drawn — only one nothing paired with at all, which
has no panel anywhere to stand for it.

That leaves two things a plan drawn from one model cannot show, and both are said as results on
the panel rather than by drawing the other model over the top of it:

- **Displaced** — the panel stands off the Revit wall's line by more than the location buffer.
  The wall is drawn where ETABS has it, and this is what says Revit has it somewhere else, with
  the distance.
- **Stops short** — ETABS does not run the length of the Revit wall. A warning rather than a
  failure: the two models agree there is a wall here and disagree about how far it runs. The
  result reads "ETABS covers 500 of 6000mm of the Revit wall", and how much shorter it may be
  before it is raised at all is *Settings > Wall check*.

The length is measured as the stretch of the Revit wall covered, not the panels added up, so
panels stacked up its height count once rather than once each, and a gap between two panels is
not counted as covered. Every panel on the wall carries the same length result, because any one
of them is what somebody will click on.

Past a metre of wall with nothing along it — again *Settings > Wall check* — a warning stops
being enough, and the stretch ETABS misses is drawn as the piece of Revit wall it is. Only that
stretch, never the whole wall, so nothing is laid over the panels already on the plan: what is
seen is what ETABS has, and then, in the colour of a wall Revit has on its own, the part it has
not. A wall covered at both ends and open in the middle is drawn as one piece in the middle,
which is where the hole in the design model is. Two gaps are judged together — two of six
hundred leave a wall as badly served as one of twelve hundred — and pieces shorter than the
shortest wall the check bothers with are left alone, being the sliver two walls leave where
they meet rather than a stretch nobody modelled.

Panels meshed up the height of a wall all land in the same place in plan, so they are drawn as
the one wall they stand on. Only the drawing is folded: every panel keeps its own verdict, the
wall takes the colour of the worst of them, its numbers are the worst of them, and it carries
the names of all of them — "ETABS name: 17481, 17482, 17483" with the count beside it. Drawn
one at a time they were indistinguishable from a single wall anyway, with the last one drawn
hiding the colours underneath it and only the first of them selectable, so a failed panel could
sit invisible under a passed one. Panels beside each other along the run are different pieces
of wall in different places and stay drawn as they were modelled.

Two walls are only ever candidates if they run the same way, actually stand alongside each
other rather than merely cross, and are within reach; among those, one inside the location
buffer beats one outside it, and the one running alongside for longest wins. A wall found past
the buffer but inside the search radius is reported as being in the wrong place rather than as
missing, the same way a column is.

The location number is how far the ETABS panel stands off the Revit wall's line, measured at
both of its ends and reported as the worse of the two — which covers a wall moved sideways and
a wall drawn at an angle with one number. A thickness neither model gives up is not a pass and
not a failure: it is reported in amber as a thickness nobody knows, because calling it a match
would be reporting agreement that was never checked.

## PT or RC

Each piece of slab that both models have, the same pieces the thickness is checked on, also
says whether the two agree it is post-tensioned or reinforced. Neither model stores that as
data, so it is read from the names: the ETABS area property and the Revit floor type. "PT"
anywhere in the name means post-tensioned (`PT230`, `MRG_STR_PT SLAB-230`). If there is no
"PT", an "S" means reinforced (`S200`, `MRG_STR_CIP-RC SLAB-200`). Neither match is case
sensitive. Where either name has neither - a drop panel typed `DP7 (400x4000x700)`, say -
that piece is not checked for PT or RC at all.

It is on by default. Switch it off with **Check PT against RC** under *Settings > Slab check*. It
uses the same smallest-patch size as the thickness check, so a sliver where a PT floor runs a
little onto an RC shell is still drawn but is not counted.

## The slab load check

Optional. Tick **Also check the loads on each ETABS shell** on the check panel, then open
**Loading plans...**, which lists every ETABS level and what each one is paired with. For a
level, say two things:

- **Which Revit view** its loading plan is drawn in - found by typing any part of the view
  name, in any order. The plan is read as filled regions, each carrying the area load number
  in a parameter you name under *Settings > Slab loads* - `Loading Type Mark` on the region
  type by default. The number need not be a number: `H1`, `L1` or `3A` work too, as long as
  the schedule's serial column says the same (case and spaces aside, and `07` is `7`). Only
  plan views are listed: a drafting view's coordinates are not the model's, so a plan drawn
  in one would not land on the slab.
- **Which sheet** of the loading schedule workbook those numbers mean something in. The
  workbook carries over from the levels already paired, because a project has one. The sheet
  does not: numbering starts again on every sheet - a tower sheet and a podium sheet both
  start at 1 - so this is asked for outright rather than guessed.

Each level shows where it has got to: ready, not paired, paired with no schedule sheet yet, or
paired with a view that is not in the project any more. Pairings are kept per project and per
level, so a level is paired once and comes back paired; **Unpair this level** forgets one.

Several levels can share one view - a typical floor's loading plan paired with every level built
to it. The view is read once and each level is checked against it on its own, so a finding on
one of them is filed, reviewed and accepted separately from the same drawn line on the next.
**Copy to levels...** gives the selected level's view, workbook and sheets (either or both) to
every level ticked, with *Similar in ETABS* ticking the levels built to the same master story.
**Guess by name...** finds a typical floor's view on its own when the name gives the run of
levels - `L05-L20`, `LEVEL 5 TO 20`, `L5~L20` - and, for a level whose name is on no view, offers
the view of the master story ETABS says it is similar to.

Every paired level is checked in the same run. A level nobody has paired is checked for
thickness and outline exactly as it always was, so pairing six levels out of forty-seven is a
sensible thing to do rather than a half-finished job. Narrowing the check to a single level
checks that one level's loads, if it has a plan. A level that was paired but that the run never
reached - because neither model has a slab on it under that name - is named in the issues at
the end rather than passed over in silence.

Each numbered row of the schedule gives four loads: the TOTAL column is the SDL, and the single
live load is routed by the schedule's own LL / LLR / MECH marking into exactly one of LL, LR or
MEP, with the other two nought. Those are compared against the uniform loads on the ETABS area
- assigned to the shell or to the frames around it, either way - with the load pattern names
mapped to the four under *Settings > Slab loads*.

A shell can carry uniform loads assigned straight to it, a **uniform load set** assigned to it,
or both, and it carries the sum. The OAPI answers for the first and has no word at all for the
second - nothing in `ETABSv1` so much as mentions a load set - so the sets are read out of the
model's own tables instead: `Area Load Assignments - Uniform Load Sets` says which set each
area carries, and `Shell Uniform Load Sets` says what each set is worth per load pattern. The
join is by the area's unique name, and the columns are found by their own field keys rather
than by counting along the row, so a table that gains a column still reads.

The load values come over in whatever units the model is being shown in, so the units are read
off the table beside the column rather than assumed, and the import says which it used. Units
it cannot make out stop the read and are reported: a load set scaled by a thousand is silent,
and worse than one that was never read.

The load verdict is a second result on the same piece of slab, coloured and filtered exactly
like the thickness one: green where it matches, red where it does not, amber where there is
nothing to check it against. Where a loading region edge runs across an ETABS area, the area is
cut along it - an ETABS shell carries one uniform load, so a shell spanning two loading regions
that ask for different loads is wrong by construction.

Loads add, whichever shell is on top. Where shells are stacked - a drop panel on a slab, or a
**null area** (property None) laid over part of a slab to carry the paving, the plant or a truck
route - each piece of plan carries the sum of every shell over it, and the slab is cut wherever
that stack changes. A null area with a load on it is read for the load check only: it has no
thickness and takes no part in the slab check, and one with no slab under it is named in the
issues rather than lost. The raw data shows each null area under the slab load tab, with its
loads as ETABS holds them.

A model file saved before null areas were read has none of them in it - they were dropped on
the way in, before the file was written - so the load check says so when it runs against one,
and the model has to be imported again, or the file saved again, for its load areas to count.
The import lists how many None areas it read, how many it left out and why, and any area
property that is neither a slab, a deck nor None.

## Layout

| Path | What lives there |
|---|---|
| `DiscrepancyScanner/Revit/` | The Revit external commands and model extraction |
| `DiscrepancyScanner/Etabs/` | ETABS API access and model extraction |
| `DiscrepancyScanner/DataContainers/` | The element and geometry types the two models are read into |
| `DiscrepancyScanner/Comparison/` | The check engines — what counts as a discrepancy |
| `DiscrepancyScanner/wpf/` | The results window (MVVM) |
| `DiscrepancyScanner/gui/` | The older WinForms views |
| `DiscrepancyScanner/Export/` | Excel and mark export |
| `DiscrepancyScanner.Tests/` | Unit tests for the geometry the checks decide with |

## Tests

The boolean geometry operations belong to the Revit kernel and only run inside Revit, so the
tests cover the plan-region reasoning the checks do for themselves — whether a leftover piece
of slab is a real opening or the hairline two outlines leave when drawn to the same grid line.

Run them from the Visual Studio test explorer, or with `vstest.console.exe` against
`DiscrepancyScanner.Tests.dll`.
