# CamelWorks — what is actually built

`CAMELWORKS_IMPLEMENTATION.md` is the spec. This file is the state: what exists, how each piece is
verified, and what is not wired yet. It is deliberately separate, because a spec that quietly edits
itself to match the code stops being able to tell you the code is wrong.

Every claim below is checked by CI on every push. Where something is unverifiable in CI, it says so.

---

## How each layer is verified

| Layer | Verified by | Where |
|---|---|---|
| `CamelWorks.Core` | Compiler + unit tests, on Linux | `core` job |
| BCF output | `xmllint` against the published buildingSMART XSDs | `core` job |
| PDF output | `qpdf --check` over real writer output | `core` job |
| Ribbon consistency | A script comparing the markup, the attributes and the catalogue | `core` job |
| Bundle consistency | A script comparing the manifest, the release workflow and its gate | `core` job |
| Icons | Every command has both sizes on disk | `core` job |
| `CamelWorks.Nav`, `.Nav.Clash`, `.UI` | Compiler, once per Navisworks year | 4 × Windows jobs |
| Anything visual | **Not verified.** No Navisworks in CI | — |

The last row is the honest limit. Every host adapter and every screen compiles against all four
supported releases; none of it has been run inside Navisworks from this environment.

---

## Core services — built and tested

**Identity.** `ElementKey` on three rungs (instance GUID, tree path, geometry signature) and
`ClashKey` whose occurrence discriminator is a quantised position rather than an ordinal — an
ordinal renumbers the moment one of three penetrations is fixed, which is the run that matters.
Boundary drift is handled at match time by a neighbour-cell lookup, not at key time.

**Store.** Atomic writes with a sibling temp file, per-writer append-only journals with a two-class
fold, an advisory expiring lease, a sync-root probe, and a versioned document that preserves keys it
does not understand.

**Project.** A profile derived from whatever is open — including an empty document — with an
override per line, an activity log, saved clash rules as data rather than closures, and a project
file that opens successfully in every failure mode: missing is a first run, damaged falls back to
its backup, unwritable degrades to memory, and a file from a newer build opens read-only.

**Findings.** Five statuses forming a promote-only lattice for machine merges, with the asymmetry
argued in place: losing a "Resolved" costs a wasted minute, hiding an unresolved conflict behind a
green row ends up in the building.

**Clash.** The rule pipeline (suppress and flag → group → assign), grouping rules including a
stable proximity bucket, carry-over with two-pass matching, the New/Persisting/Resolved/Regressed
delta, and cross-test duplicate collapse on a proximity band. The funnel reports every number that
removed a row from the board.

**Headroom.** Clear height between a walkable surface and whatever is above it — the check a clash
test cannot do, because a duct 1.9 m over a corridor clashes with nothing. One row per surface, the
position reported at the overlap, and a spatial grid whose oversized bucket every query searches. It
is run from Coordinate ▸ Tests, beside the clash tests, because it is one.

**Sets.** Boolean set algebra compiled to disjunctive normal form — the only shape the host can run
— with negation compiled to a subtraction rather than a negated operator, because those are not the
same set over a model.

**Appearance.** A layer stack with per-property precedence, an explanation of which layer decided
what and which were overruled, and a planner that emits the smallest set of host writes. Overrides
CamelWorks did not author are reported and never cleared.

**Automation.** A node graph with topological ordering, three-colour cycle detection and evaluation
against a host seam — so the ordering, the type checking and what happens to a half-wired graph are
all provable without Navisworks. Every node in the catalogue wraps a service the ribbon also drives.

**openBIM.** BCF 2.1 and 3.0 read and written from one neutral model; IDS read and checked, with a
specification that matches nothing reported as matching nothing rather than passing.

**Data.** Level inference from geometry when the model has none, unit-aware quantity parsing,
takeoff that reports what it could not read, and six model-health checks with scale-free thresholds.

**Report.** A block model with HTML, XLSX and PDF writers. The PDF writer is hand-rolled, including
PNG embedding with alpha split into a soft mask.

---

## Host adapters — built, compile-verified on four years

`CamelWorks.Nav` implements the seam: document, items, selection, view session, viewpoint store, a
compiled-set search runner and a batched property-write transaction over the COM bridge.
`CamelWorks.Nav.Clash` is a **separate assembly** because `Autodesk.Navisworks.Clash.dll` ships with
Manage and not with Simulate, and the UI loads it by name so a Simulate user never faults on it.

Every API member used was checked against the reference assemblies for all four years before being
written, with a metadata reader rather than by recall. That found the clipping API is JSON in and
JSON out, that saved viewpoints have no rename, that `Document.CurrentViewpoint` is not a
`SavedViewpoint`, that `SetUserDefined` is on the COM GUI property node, that `SearchCondition`
takes its comparison before its value and has no `ByPropertyDisplayName`, and that 2027 does not
carry the clash `Tests` property the other three do.

---

## UI — the desks

**Fifty-one tools, five desks, twenty-two ribbon buttons.** The third design, recorded in
`CAMELWORKS_DESIGN.md`. A coordinator has five jobs, and each is a **desk** — one window with a
permanent rail naming every tool of that job, grouped under its stage in small caps, nothing
folded and nothing tabbed. One tool is on the bench at a time; the rail says which. The
beside-the-model tools — the board, changes, the data grid, the set builder, appearance — open in
the dock pane from wherever they are summoned, and the pane at rest is **Home**: the week's three
steps with their state, the five desks, the tool search, and recent activity. Review keeps a
floating palette over the 3D view; Help keeps a window that works when nothing else does.

Every tool still carries three things in one registry (`Shell/Tools.cs`): its name, a sentence
saying what it is trying to do, and **one main action**. `Shell/Desks.cs` says which desk and
stage each tool sits on; `Views/Surfaces.cs` is the one dispatch a desk benches from; and a
ribbon check holds all of it consistent — every tool on exactly one desk, every benched tool with
a surface, no two buttons opening the same thing.

**Every surface is one anatomy** (`Views/Bench.cs`): a setup column on the left holding every
input, all of them visible; the tool's one action at the column's foot, in the same place on
every tool; and the stage on the right, where the answer lands and dominates. At pane width the
same anatomy stacks. Wizards are the same shape with a spine: every step listed down the left
with its state, the current step's surface on the stage, Next at the spine's foot (`Ui.Steps`).
There are **zero collapsible sections in the product** — `checks/folds.txt` is empty and
ratcheted, and `Ui.Card`/`Ui.Marked`/`Ui.Numbered` appear on no tool surface.

The ribbon is six panels: Home and Help, then one panel per desk — the desk's button plus the
daily tools as direct entries that land inside the desk with the tool already on the bench.
Find a Tool (`Shell/Finder.cs`) knows all fifty-one tools by name, synonyms and symptom phrasing.

**Icons** are Google Material Symbols (Apache-2.0, attributed in `NOTICE`), fetched and rasterised by
`tools/icons.py` and committed. The tool is in the repository rather than the PNGs alone, so the set
is reproducible and each button's symbol is on the record. There is one set, drawn for a light
ribbon; a theme-aware second set is a design job.

---

## What the second pass added

The first pass built the structure: every service, every screen, every button. The second closed the
gap between a screen existing and a screen doing the job somebody opens it for.

| Added | Why it was missing mattered |
|---|---|
| BCF cameras, components and snapshots | A topic with no viewpoint is a sentence about a problem somewhere in a federation |
| Duplicate collapse in the funnel | Two tests reporting one pipe through one wall is one problem assigned twice and closed once |
| Triage filters, delta column, regression banner | The week's first question was not answerable without scrolling |
| Board editing: statuses, hand grouping, undo | Carry-over exists to preserve human judgements and there was nowhere to make one |
| Issues no clash produced | Most of what goes wrong in a federation is not an intersection |
| The BCF reply leg | An issue that goes out and never comes back is a spreadsheet with extra steps |
| IDS against the federation | Employers' requirements arrive as IDS files and nothing could read one against a model |
| The clash matrix builder | Thirty-six Add Test clicks is why a matrix stops covering the federation it was drawn for |
| The board on Simulate | Most seats on most jobs have no Clash Detective, and none of the work built on the board is clash detection |
| Appearance foreign state | The layer list said what CamelWorks intended and nothing about what anybody else had done |
| The write preview, and formulas | Writing into a model is the one destructive thing here, and Ctrl+Z does not reach it |
| Set families | One set per level, per discipline, per system: an hour of clicking on every job |
| The takeoff pivot and its export | One axis says how much, two say where, and where is what a programme is built from |
| Reports per party, filtered, with pictures | The single whole-board report is the one nobody sends |
| A viewpoint per issue | The walkthrough a coordination meeting is run from, and never has time to make |
| Six coordination nodes on the canvas | The automation half could not do the half of the job the product is named for |

Two guards were added at the same time, both because CI caught something the local checks could not:
a list of 471 WPF and Navisworks type names that a Core public type may not shadow, and a check that
the only company named anywhere in the tree is BIMCamel.

---

## Known gaps

- **Nothing has been run inside Navisworks.** Compilation on four years is real verification of the
  API surface and no verification at all of behaviour. This is the one that matters.
- **One icon set, for a light ribbon.** On a dark Navisworks theme the glyphs are darker than ideal.
- **IDS output is not written**, only read — deliberately, since authoring tools produce IDS files
  and nothing in this product needs to.
- **The IDS schema is not validated in CI.** The proxy in the build environment blocks w3.org, which
  the IDS schema imports. BCF and PDF, which this product *writes*, are both checked; IDS is only
  read, so validating our own reading of somebody else's file would prove nothing anyway.
- **Nothing runs unattended.** A job runs when you run it, in the Navisworks you have open. There is
  no scheduler and no headless mode, and Help says so.
- **There is no CamelWorks entry on the selection tree's right-click menu, and there cannot be.**
  The Navisworks .NET API has no context-menu extension point: the plug-in kinds are AddIn,
  ClashResultAction, CommandHandler, Custom, DockPane, EventWatcher, FileProtocol,
  HomeScreenExtension, Input, Render and Tool, and none of them attaches to a context menu. This was
  checked against the 2027 reference assembly rather than assumed. The nearest thing the API does
  offer is `ClashResultActionPlugin`, which adds an entry to the right-click menu on a clash result —
  but it is Manage-only and would have to live in a module the bundle declares, which would make a
  Simulate seat load an assembly that is not there. The Simulate story is worth more than the menu
  entry, so the menu entry is the thing that goes.
- **No multi-file batch.** A graph runs over the document you have open, not over a folder of NWFs.
  Opening documents is a host capability with real failure modes and no way to verify it from here;
  the scope decision left it reopened and it stays reopened.
