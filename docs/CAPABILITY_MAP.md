# Capability map — every engine, and the surface that reaches it

Written because a redesign is where features die quietly. The tool catalogue in
`CamelWorks.UI/Shell/Tools.cs` lists 42 tools, but it is **not** the inventory: several
shipped capabilities were sub-panels inside the old tabs and never had a tool entry at
all. This file is built from the engines instead — all 248 public documented types in
`CamelWorks.Core` — so a capability cannot go missing without leaving a hole here.

Rule: **every engine has a named surface.** If a row has no home, that is a bug in the
design, not a decision.

---

## Gaps this audit found, and where each one now lives

These sixteen had no home in the first cut of the redesign. Two were caught by the user
(1 and 2); the rest came out of the engine sweep.

| # | Engine | Was | Now |
|---|--------|-----|-----|
| 1 | `Clash/SheetMatrix` + `SheetMapping` + `MatrixGrid` + `MatrixSymbol` | **Never dropped — the design record was wrong, not the code.** `MatrixWindow` is and always was entirely spreadsheet-driven: step 1 is the file picker, the preview and the column mapping, and there is no hand-filled grid anywhere in it. A design prototype invented one and omitted the real path, which is how this came to be reported as missing. | **Unchanged.** Clash Matrix keeps its three steps — the sheet, the matrix, the tests. Nothing to build; the entry stays here so the next person who reads a prototype and doubts it can check. |
| 2 | `Data/Breakdown` (WBS) | **Dropped, and never in `Tools.cs`.** Buried as `BreakdownStamp` inside the old Data tab. | **Work Breakdown** — its own window, Data panel. Reads the selection tree's shape, writes `WBS1…WBS6` + the joined key. |
| 3 | `Clash/Freeze` + `ChangeNotice` | Dropped. Records that a scope was agreed, and what has changed since. | **Freeze** (act) and **Change notice** (window), Revisions panel. |
| 4 | `Clash/Penetrations` + `PenetrationSchedule` | Dropped. Services through structure, as a schedule for the structural engineer. | **Penetrations** — window, Issues panel. |
| 5 | `Project/ModelManifest` + `ModelIntake` + `TestRecord` | Dropped. What is loaded, at which revision, and restoring placement. | **Models** — window, Project panel. |
| 6 | `Appearance/AppearanceTransfer` | Dropped. A colour scheme lifted off one model onto another. | Appearance panel toolbar: "Copy appearance from another model…". |
| 7 | `Report/DxfWriter` | **Half-dropped, and the first read of this row was wrong.** The engine was never callerless: Extract has written DXF all along. What was missing is a DXF you can ask for without a recipe — the Export window only had GLB and CSV/XLSX, so somebody who had selected forty pipes and wanted them in AutoCAD had no way there. | **Export Drawing** — its own window and its own Deliver button, scoped to the selection or everything loaded. R12 ASCII, one layer per discipline, model or category. Extract keeps its own recipe-driven DXF. |
| 8 | `Project/ActivityLog` | Dropped. What has happened on this project, newest first. | **History** — window, Project panel. |
| 9 | `Clash/ClashMarkers` + `MarkerMesh` | Dropped. Clash points as geometry you can actually see. | Clash Board toolbar toggle: "Show the clash points". |
| 10 | `Compare/CompareStamp` | Dropped. Writes what changed onto the elements as properties. | Compare Revisions post-action: "Write the changes onto the elements". |
| 11 | `Compare/CompareClusters` | Dropped. Groups changes that sit near each other. | Changes panel — "Group by: Cluster (nearby changes)". |
| 12 | `Sets/Similarity` | Dropped. Finds elements like the one selected. | Set Builder: "+ Elements like this one". |
| 13 | `Sets/SetTree` + `SetFamily` | Dropped — and the only two engines in the whole map with *no caller anywhere in the UI*: tests, and nothing in front of them. | **Generate a Set Library** — steps, under the Set Builder split. Built rather than moved. |
| 14 | `Sets/SheetSets` + `SetSheetMapping` | Half-dropped. Distinct from `SetSchedule`: reads set definitions out of the spreadsheet a project already keeps. | Folded into **Sets from a spreadsheet**, whose mapping step handles both layouts. |
| 15 | `Store/ProjectLease` + `SectionDiff` | Dropped — and it is not a tool, it is a state nobody drew. The only engines left in the map whose only callers were their own tests. | Built. Every window and both panes carry a blue strip when someone else holds the project, with "Take it over" on it; a refused save names the sections they changed. Claimed on the way to a write, not on the way to a screen. `StorageSubstrate` stays infrastructure: on a synced folder the lease is refused and saving carries on. |
| 16 | `Data/ValueCleaner` | Dropped. Makes what a property says readable as a quantity. | Takeoff tuning band ("Tidy values before summing"), and a Write Properties → Find & replace preset. |

---

## The full map

Infrastructure rows (`Hash`, `JsonReader`, `IFileSystem`, `AtomicFile`, `VersionedDocument`,
`Measure`, `IfcGuid`, the `I*` seams) have no surface by design — they are how other
surfaces work, not things a coordinator opens.

### Project
| Engine | Surface |
|---|---|
| `ModelHealth`, `HealthFinding`, `HealthReport`, `FederationReview`, `ReviewFinding` | Health Check |
| `HealthFindings` | **Health Check** — every sweep writes its findings beside the model, so a card gains **Settle it** and **Raise an issue** rather than only "show me them". One parent per rule carrying the decision, one child per element carrying the location; identity is the rule id, so a headline that reads "37 elements" and next week "41" is still the same finding with the same decision on it. |
| `ProjectProfile`, `ProfileSetting`, `Disciplines`, `ProjectStore` | Project Settings — *This project* |
| `CodeTable`, `CodeTables`, `TeamRoster`, `AssignmentBook` | Project Settings — *Team & outputs* |
| `ModelManifest`, `ModelIntake`, `TestRecord` | **Models** (new) |
| `ActivityLog`, `Activity` | **History** (new) |
| `ProjectLease`, `StorageSubstrate`, `LeaseResult` | The read-only strip on every panel (new) |
| `SampleProject`, `SelfTest` | Help |
| `ToolMemory` | Invisible — every tool remembers its last inputs |
| — (host source paths) | Fix Broken Links |

### Clash
| Engine | Surface |
|---|---|
| `ClashPipeline`, `ClashPipelineOptions`, `ClashFunnel`, `ClashPredicates`, `GroupingRules`, `AssignRule`, `ClashRuleSet`, `FilterSpec`, `GroupingSpec`, `PredicateSpec`, `SpecInput` | Clash Rules |
| `ClashPipelineResult` — *results no grouping rule could place* | Clash Board — a standing **Ungrouped (N)** row at the foot of the group list. Never hidden: a result nothing grouped is the one most likely to be somebody's problem. |
| `ClashItem`, `ClashSnapshot`, `ClashSnapshotJson`, `GroupDecision`, `DuplicateCollapse` | Clash Board |
| `ClashProjection` | **Clash Manager ▸ Board** — a held finding as the item the rules run over. The board reads the check once, the register takes the findings, and the items are projected back out of it, so a status, an assignee or a hand-made group survives to the next run. It is also how a Simulate seat rebuilds a board with no clash engine present: the register carries the elements, the exact point, the comments and the group, where the snapshot it replaced carried a status and a quantised cell. |
| `ClashTestSides` | Clash Matrix — "The model's tests" mode reads each test's sides back into categories |
| `ClashResultInfo` | Clash Results (Manage tab) — every row of the results grid, and the Board's read behind it |
| `Issue`, `IssueRegister` | **Issue Manager ▸ Issues** — the register and its detail panel; raised by hand, from clash results, from clearance findings, or read out of BCF. A finding-set somebody raised: the status is the finding lattice, the comment is `FindingComment`, and the priority comes from the project's own list. |
| `FindingBcf`, `FindingBcfOptions`, `FindingBcfDocument`, `FindingPackRead`, `FindingRecord`, `IssueImportResult` | **Clash Manager ▸ Exchange**, **Clash Manager ▸ Manage ▸ Save baseline / Save markup**, **Clearance Check ▸ Write as a clash file**, **Issue Manager ▸ Export / Import** — the one BCF writer, for every check. A finding goes out as a topic with a viewpoint framing its elements and its point; the issues that carry findings go in the same file, related both ways, so a pack is the whole story rather than eleven clashes arriving with no geometry. What BCF has no field for — the finding's identity, its point, where the writing document had each element, and every fact the check measured — rides a sidecar inside the same archive, so every check round-trips its own numbers. The ids and the routes are read from the open document on every path that has one, and a markup saved from a file board sends back the ones the file it was reading arrived with. On the way back a topic answering a finding this project sent out goes onto that finding; everything else is a stranger and becomes an issue, because a finding is only ever something a check produced. |
| `FindingLabels` | **Every tab that exports BCF** — the one label vocabulary and the one test for whether a label is ours. A prefixed label is taken off on the way back in; a bare one is the reviewer's own tag and is kept. Without it a pack grew the reviewer's tag list by the writer's own labels on every round trip. |
| `IssueFindings` | **Issue Manager ▸ Issues** — the findings card on an issue's detail, and "Take {n} off". Reached from the other side by **Clash Manager ▸ Manage ▸ Attach to an issue**. An issue may only be attached to findings that already exist. |
| `IssueCascade` | **Issue Manager ▸ Issues ▸ Save** — marking an issue merges its status into the findings attached to it and says what that did. A merge rather than an assignment, so it can never demote a finding somebody has already approved. |
| `IssueItems` | **Clash Manager ▸ Board** — issues somebody raised by hand ride the board beside the engine's results, so a door swinging into a corridor groups, assigns, reports and exports like a clash does. |
| `ClashMarkers`, `MarkerMesh` | **Clash Manager ▸ Manage ▸ Export the markers as .obj** — over the picked results, or over every test on the board from the test menu. A group per test, an object per clash named what the engine named it, a status colour each, and an .mtl beside it. For the person who has neither the plug-in nor Navisworks. Also on **Report ▸ Markers**, over the whole board. |
| `SceneMarker`, `MarkerOverlay` | **Clash Manager ▸ Manage ▸ Focus / Show the markers**, and **Clearance ▸ Sphere** — status-coloured balls drawn straight into the view by the `CamelWorks.Markers` render plug-in. Temporary graphics: up on the next frame, down on "Clear the markers", never added to the document. |
| `IClashSource.CommentsOn`, `DeleteComment`, `EditComment` | **Clash Manager ▸ Manage ▸ Comments** — one clash's conversation, read as well as written. The trail was write-only: the board showed a count and the newest remark and the only verb added another. Editing is a rebuild of the whole trail, because every field of a host comment is read-only, so a changed remark takes today's date and the window says so. |
| `ClashTestNames` | **Clash Manager ▸ Manage ▸ rename / move into a folder** — what this project calls a clash test when the document will not be told. A published NWD holds its clash data read-only, so both verbs simply refused; the name and the folder are kept here instead and laid over the host's on every read, and dropped the moment the document accepts them. |
| `ElementLocator` | **Clash Manager ▸ Manage ▸ Save as a clash file / Focus and Isolate from a file** — where the writing seat had each element, carried in the file so the reading seat can ask for it instead of searching. A key is a hash, and the only way back from one is to hash the whole federation until something matches; the seat that wrote the file never needed that, because the clash engine handed it both elements. |
| `ClashGroupPath` | **Clash Manager ▸ Board ▸ Sort ▸ Make these the test's clash groups** — the trail of band names down to one bucket and the results in it, handed to the seam as segments rather than a joined name so the seam can decide the shape: groups inside groups where the document holds them, one group named for the whole trail where it does not. |
| `ClashTrail` | **Clash Manager ▸ Board** — the one spelling of a trail of clash-group names, so the reader that walks a nested tree and the writer that builds one agree on what the board should show. A middle dot rather than a slash, because a group's name goes into the document and both slashes are a path separator to somebody. |
| `TeamRoster` | **The team** — everybody on the job, opened from any assign list and from Project ▸ Who is on the job, and the one place a name, role, company or team is known. Kept beside the person as well as in the project, so the same people are there on the next model and whoever opens the job gets them with it. |
| `MatrixBoard` | **Clash matrix** — the grid itself: rows down one side as side A, columns across the top as side B, and a symbol where they meet. Two lists rather than one square one, so the matrix everybody draws is the case where they hold the same things and a matrix of packages against disciplines is possible without inventing categories. |
| `MatrixSource` | **Clash matrix ▸ a row or column's header** — where its elements come from: a saved selection or search set, whole models, or a captured list from the selection, the tree or what is visible. The first two re-resolve every run; the last three are a list as it was, and the header says which. |
| `MatrixNaming` | **Clash matrix ▸ Naming** — how every test the grid asks for gets its name, as a pattern with the pieces a cell actually knows. Every job names its tests differently, and the naming is what somebody sorts and filters a rail of a hundred and sixty by. |
| `MatrixDiff` | **Clash matrix ▸ See what would change, and Build** — what has to happen for the document to match the matrix. A test is matched on what it is between rather than on its name, so a rename is not a new test and one edited cell does not delete a fortnight of statuses and comments. |
| `MatrixFromModel`, `MatrixReading` | **Clash matrix ▸ Start from ▸ The model's tests** — the same read with no pattern written: each test becomes a cell keyed on the saved sets its two sides point at, so a federation already full of tests draws as a matrix without anybody typing a convention first. `MatrixReading` is what a read hands back — the board, the legend it inferred from the kinds and tolerances in use, what it could not do cleanly, and which pattern read each test, so the project can record the last of those instead of asking again. |
| `MatrixNamePattern`, `MatrixFromNames`, `MatrixReadOptions` | **Clash matrix ▸ Start from ▸ The model's tests** — a table of the document's tests with a search and ticks, and beside it the job's own naming conventions written in the same tokens the matrix writes names with, read backwards. The read used to take every test in the document and name the rows after whatever the saved sets are called; now the person says which tests and what their names mean, and the table shows live which pattern claimed each one. Several patterns, tried in order, because a job set up over two years has more than one convention. A row is keyed on its name, so one discipline named by five tests is one row; it still points at the set the tests use, and a name covering two different sets is said out loud. |
| `MatrixSheets`, `MatrixInk` | **Clash matrix ▸ Export ▸ Spreadsheet** — a tab per matrix somebody has filled in (the symbols, the priorities, the counts) and a legend under them, each drawn the way the screen draws it: the folder bands merged and in their colours, the headings turned, the row headers washed with their folder, the spine down the diagonal, the mirrored half quiet. `MatrixInk` is where the ranks and the heat scale are defined, so the sheet a meeting marks up in red is a picture of the screen it was shown. |
| `MatrixTestsXml`, `MatrixTestsXmlDocument` | **Clash matrix ▸ Export ▸ Tests for Navisworks** — the board as an XML the Clash Detective imports, so a matrix argued over on a laptop becomes two hundred tests on the seat that will run them, without that person building them by hand from a picture. Tests and no results: a name, two sides, a kind and a tolerance per pair. A side is named by its saved set; a side that is whole models or a captured selection cannot be addressed in this format, so it is left out and named in the warnings rather than written as a test that would import empty. |
| `MatrixRule` | **Clash matrix ▸ Arrange ▸ Fill by rule** — a standing sentence about two folders ("all services against all architecture, B") that goes on being true as the matrix grows: a discipline added next week arrives already checked the way its folder is checked. An empty side means any folder; the last rule that covers a pair wins, so exceptions go below the general rule. A hand that writes over a rule-filled cell owns it from then on, and a rule that is edited or dropped takes back only its own cells. Cells a rule wrote carry a corner mark and name the rule on their tip. |
| `MatrixField` | **Clash matrix ▸ Arrange ▸ Fields** — one field a line carries, and how it is filled: by hand, counted down the axis from a start and a step, or the same value on every line. The four the board has always had — name, number, code, folder — are fields like any other and gain a title, a fill rule and a series; they cannot be deleted. A custom field becomes a naming token, so `{rowPhase}` reaches it when a test name is written and when one is read back. |
| `MatrixScheme` | **Clash matrix ▸ Arrange ▸ Fields** — the field list, shared by both axes, and the pass that writes every filled field across the board. Numbering forty rows is one edit rather than forty, and moving a line renumbers what follows it. A line can hold any one field by hand, which takes it out of that series without leaving a gap in the numbering and marks it on the header as custom. |
| `NamingBook` | **Clash Manager ▸ the test menu ▸ Naming** — the window that holds the fields, the patterns and which pattern reads which test, reached from the rail's filter panel and from the test menu; also written from **Clash matrix ▸ Build** and **▸ Read from the model**, which bind the tests they name without asking. — the project's naming: the field scheme, the patterns test names are written in, and which pattern reads each test. It lives on the project rather than on a matrix because the Clash Manager needs it with no matrix open. A test is bound to one pattern and stays bound, so pattern order never decides anything; a bound test whose name stopped fitting reads as nothing rather than falling through. `Written` changes one token in a name and leaves every separator the pattern carries exactly as it was. |
| `NamingWrite`, `NamingStep`, `NamingRefusal` | **Clash Manager ▸ the test menu ▸ Renumber the tests** — what writing one field across a run of tests would do, worked out before anything is written. Every line of it is a rename of a host test, so the plan is on screen first: what each name is, what it would become, and which tests are refused because they are on no pattern or their pattern carries no such field. A refused test still takes its place in the count, so refusing the third of ten does not shift the seven under it. |
| `MatrixTally`, `MatrixCounts` | **Clash matrix ▸ Results** — how many clashes are still open on each pair, counted off the document's tests and drawn in the same grid that says which disciplines meet. Open means active, new or unruled; approved, reviewed and resolved are decisions somebody made. A pair tested and clean reads nought; a pair no test covers is left blank, because those are different answers and drawing them alike reassures a room about a discipline nobody checked. |
| `MatrixSkip` | **Clash matrix ▸ the count under the grid** — the cell that could not become a test, and why: a symbol the legend does not explain, a row pointed at nothing, or a pair some other cell already tests. |
| `Responsible` | **Clash Manager ▸ Board ▸ Assign** — who a clash is given to: one person, or a whole role, company or team. Held as a string because the host's assignee field is one, with the kind in front of the name and a bare name still meaning a person, so every assignment made before this existed reads back unchanged. |
| `UserChoices` | **Clash Manager ▸ Settings** — the handful of choices a person should not have to make twice: how far back the camera sits, how big a marker is, whether focusing drops one, whether clicking a row focuses it. Kept per user in `%AppData%/CamelWorks/choices.json`, never in the project, because they are about a pair of eyes and not about a model. |
| `ClashFindings` | **Clash Manager ▸ Manage** — every read of the results also writes them into the findings register, so a decision survives the session, a published NWD, and the next run renumbering every result. Scoped to the tests that were read. |
| `ClashLegacy` | **Clash Manager ▸ Manage** — the two side registers the board kept before findings (refused edits, reviewer fields), read back onto the findings they belong to on every read. Idempotent, so the old keys stay on disk and an older build still finds them. |
| `FindingLocation` | **The findings store** — where a finding is, when one rule failed many elements. Carries a note somebody wrote against one place and never a decision: the finding is the unit of decision, so a location has no status, assignee or sign-off and cannot be re-identified by its element moving. |
| `Priorities` | **Project ▸ Settings ▸ Priorities** — the four words a project's priority list is seeded with. `ProjectProfile.Priorities` is the one vocabulary; a finding and an issue both draw from it. |
| `Finding`, `FindingField`, `FindingSource`, `FindingComment`, `FindingJson`, `FindingRegister`, `FindingStore` | **The findings store** — the one record every check emits, kept in `.camelworks/<model>.findings.json` beside the project file. Clash writes to it; the other checks move onto it one at a time. See [docs/findings-and-issues.md](findings-and-issues.md). |
| `WorkSave` | **Every screen that raises, attaches or cascades** — the one door for work that spans the findings sidecar and the project file. The sidecar is written first and the project file only if that succeeded, so a crash between them leaves a finding nothing points at rather than an issue naming a finding that is nowhere. |
| `Resolution`, `FindingStatus`, `FindingStatusLattice` | Clash Board detail + Review. The status lattice is what stops a re-run demoting a decision somebody made. |
| `MatrixOptions`, `MatrixPlanner`, `MatrixCategory`, `MatrixGrid`, `MatrixSymbol`, `SheetMatrix`, `SheetMapping` | Clash Matrix — by hand, from the model's tests, or from the client's spreadsheet |
| `MatrixLegend` | **Clash Matrix ▸ the palette** — every symbol the grid uses and what each one means: the kind of test, the tolerance, whether it builds anything and what colour its cells carry. Edited in the options panel, where renaming one takes its cells with it. |
| `MatrixStyle` | **Clash Matrix ▸ Look** — how the matrix is drawn: which triangle is worked in, whether the dead half echoes, what the diagonal and the ruled-out pairs are coloured, and which of a line's fields show beside its name. Saved with the matrix rather than with the seat. |
| `Headroom`, `HeadroomElement`, `HeadroomSpan`, `HeadroomResult` | Clearance Check — the engine behind the findings grid. Takes either named elements or detected surfaces; over a detected surface the volume swept is the patch's own outline extruded, and every breach carries the clear height and the point it was measured at. |
| `TriangleMesh` | **Clearance Check ▸ Detected walkable surfaces** — the triangles a check reads, in metres, welded. Welding is the seam's contract and not an optimisation: the host emits a vertex per triangle corner, and surface detection clusters by shared vertex index. |
| `WalkableSurfaces`, `WalkableOptions`, `WalkablePatch`, `WalkableDiagnostics` | **Clearance Check ▸ What to measure ▸ Detected walkable surfaces** — the surfaces people stand on, found in the triangles instead of named by hand. The set-up window states the count, the total area and the elements refused before a check is run, and any surface can be dropped from the list. |
| `UpAxis` | **Clearance Check ▸ Detected walkable surfaces** — which world axis is up, taken from where the horizontal area is. A federation mixes Z-up and Y-up sources, and a check that assumes one reports nothing at all on the other. |
| `Vec3`, `Tri2`, `Span1`, `BoxFit` | **Clearance Check** — the geometry the checks are written in: a point, a triangle in plan, a range along an axis, and a box being built. `BoxFit` has no empty constructor on purpose, so no box can be grown from the world origin and reach back to it on a site-coordinate model. |
| `ClearanceFindings` | **Clearance Check** — every run writes its breaches into the findings register beside the model, so a decision survives closing Navisworks rather than being carried in memory until the session ends. The minimum somebody asked for is a fact about the run, not part of what a finding is, so raising it does not lose last week's judgement. |
| `FloorGaps`, `FloorGapGrid`, `FloorOpening`, `EdgeClass`, `EdgeRun`, `FloorEdgeResult` | **Checks ▸ Fall hazard**, with its results on **Clash Manager ▸ Manage** — the plan of one level rasterised from triangles: the openings enclosed by floor, how wide each one is, and which floor edges face a void wide enough to fall through with no handrail along them. Every cell records which element covered it, so an opening names the slabs it is cut in and a dangerous run names the floor it runs along. What the algorithm cannot see — a column read as floor, an opening open at a slab edge, anything thinner than a cell — is written down on `FloorGaps` itself. |
| `FallHazardFindings` | **Checks ▸ Fall hazard** — one finding per flagged opening and one per dangerous edge run, written into the findings register beside the model. The run is in the identity and the limit it asked for is a fact, so re-running at a stricter limit does not lose last week's judgement. |
| `FallPlan`, `FallRaster` | **Clash Manager ▸ Manage ▸ the picture on a fall-hazard finding** — the plan drawn as pixels: the voids coloured by how far each point is from anything solid, and the edges coloured by whether they need a handrail. The one check in the product that can produce a picture on a seat with no renderer. |
| `Freeze`, `ChangeNotice` | **Compare Track ▸ Freeze** and **▸ Change Notice** — sign-off scoped by set, selection, everything or the board |
| `Penetrations`, `PenetrationSchedule` | **Penetrations** (new) |
| `PartyLedger` | Clash Report — per-party split |
| `ClashAside` | **Clash Manager ▸ Manage** — the clash properties Navisworks has no field for (priority, due date, stage, a retitle, the reviewer's tags), as one markup said them. Laid onto the finding, shown as the **Reviewer set** column with overdue rows toned, searchable, and read back off the finding onto the topic on the next export. |
| `IfcIdentity`, `IfcGuid` | **Clash Manager ▸ Exchange / baseline** and **Clearance Check ▸ out** — reads each element's IFC GlobalId on the way out so the receiving tool can select the two clashing elements; the export says how many carry none. |
| `ClashBcf`, `ClashFile` | **Clash Manager ▸ Exchange**, **Clash Manager ▸ Manage ▸ A file** — clash's view of a BCF pack: the topic guid a clash key derives, the neighbouring cells a clash that moved may have gone out under, and a returned pack read back as clash results for the board. It writes nothing — `FindingBcf` writes every check's pack, this one included. BCF is the only clash file the product reads or writes. |
| `ClashPackTest`, `ClashPackResult`, `ClashPackRead` | **Clash Manager ▸ Exchange** — a pack, read: its tests, its results, and the topics no clash answers to, which go to the **Issue Manager**. What the model has no field for (priority, due date, stage, a retitle, the reviewer's tags) becomes a comment on the clash. |
| `ClashPick`, `ClashClause`, `ClashFacet`, `ClashFacets`, `ExportProfile`, `ExportRound` | **Clash Manager ▸ Exchange ▸ Send out** — which clashes go in a pack and in what order, by any parameter a clash carries: status, assignee, folder, storey, elevation, model, or a property the models themselves carry. Saved as an export profile, and split so one press writes a pack per recipient. |
| `ClashExchange`, `ExchangePack`, `ExchangeRow`, `ExchangeReading`, `ExchangeApplied`, `ClashStatusMap`, `ClashStatusWord`, `ClashIssueLog`, `ClashIssued`, `BcfView` | **Clash Manager ▸ Exchange ▸ Take back** — several parties' returned packs read together and applied once. Where two parties answered the same clash differently the row is marked and applied to nothing until one side is chosen; every party's comments come back, not only the winning side's. |

### Checks
The seam every check sits on. One check at a time on the Manage tab, drawing whatever the
check returns; the checks themselves move onto it one at a time, so these rows name the
surface each type will be reached through rather than a screen that exists today.

| Engine | Surface |
|---|---|
| `ClashCheck` | **Clash Manager ▸ Manage** — clash detection as a check: the host's own tests read through the clash seam, with the host echo, grouping, runner, naming, geometry and properties a licensed seat offers. The host handle stays private to it; a finding carries its host test and result id as facts. |
| `FileCheck` | **Clash Manager ▸ Manage** — clash results read out of a file rather than a document: a baseline somebody exported, or a markup sent back. The same check with five capabilities null, which is how a seat with no clash engine still takes part in a review. |
| `CheckCatalogue` | **Manage ▸ the check picker** — every check the session can offer, registered once when the document opens. Adding a check is one class and one `Add` call: no enum member, no switch, no board edit. An empty catalogue is the ordinary state of a seat with nothing available, and the picker says so. |
| `CheckRun` | **Manage ▸ the run rail** — one line per test, saved run, specification or rule: what it is called, what it covers, its threshold, its trades and how many results it holds. The check writes the wording, because only the check knows what its own settings mean. |
| `CheckRunSpec`, `CheckRunStore` | **Manage ▸ the runs this project owns** — the runs this product created, saved in the project file rather than the model, so a run survives a published file that will not hold one. The settings are an open bag of strings, so a new check adds a run type without a schema change. Shares the `checks` section with `RunJournal`, each writing only its own key. |
| `CheckRead`, `CheckFacts` | **Manage ▸ Read the results** — one read of one or more runs: the runs it covered, the findings it produced and the scope those findings are observed under. Run membership is a fact on the finding, so reading three of twelve runs never marks the other nine absent and a result reported by two runs is counted under both and drawn once. |
| `ResultColumn`, `ColumnKind` | **Manage ▸ the result columns** — the columns a check declares beyond the standing ones, carrying typed accessors rather than the name of a fact to parse. Sorting, range filtering and banding read the number; the fact key is where the value exports. |
| `ClearanceCheck` | **Checks ▸ Clearance**, with its results on **Clash Manager ▸ Manage** — clearance as a check: the project's own saved runs, a runner that measures the clear height over the surfaces a run names, geometry through the element locator, and element properties off the surface and the obstruction. Three capabilities are null, because no clash engine found these results: there is nowhere in the document to echo a decision to, no host groups and no naming window. |
| `DocumentProperties` | **Clash Manager ▸ Manage ▸ the columns somebody adds** — element properties read off the document, for a check whose findings name ordinary elements. Clash reads its own through the clash engine, which holds both elements of every result; everything else resolves the finding's element keys here, which is why the fill runs as a job with progress rather than per cell. |
| `ClearanceSettings` | **Checks ▸ Clearance ▸ What to measure**, **▸ Scope and reductions** and **▸ Detected walkable surfaces** — one run's settings, typed, over the string bag the run record holds: the minimum, the tolerance band, the two sides, the level and zone scope, the obstruction ignore list, worst-per-surface, stairs, and the detected surfaces somebody dropped. |
| `ClearanceColumns` | **Clash Manager ▸ Manage** — the four columns a clearance breach fills: the surface, what is over it, the clear height that was there and how far short it fell. Declared beside the clash columns so the board draws, sorts, bands and exports either without knowing which check produced the row. |
| `FallCheck` | **Checks ▸ Fall hazard**, with its results on **Clash Manager ▸ Manage** — fall hazard as a check: the project's own saved runs, a runner that rasterises the plan again, geometry through the element locator, and a snapshot drawn from the run's own plan. The host echo and the grouping are null, because no clash engine found these results. |
| `FallSettings` | **Checks ▸ Fall hazard ▸ What to read** and **▸ How to judge it** — one run's nine settings, typed, over the string bag the run record holds: the floor, handrail and obstruction sides, the level and band the plan is cut at, the cell size, the gap that needs a handrail, the handrail reach and the smallest break worth reporting. |
| `FallColumns` | **Clash Manager ▸ Manage** — the three columns a fall hazard fills: the opening's area, the widest gap and the length of edge with no handrail. An opening fills two of them and a dangerous run the other two, and a blank is the honest answer for the rest. |
| `ClashColumns` | **Clash Manager ▸ Manage** — the three columns a clash board draws: the two participants and how deep the clash is. Declared once for the document leg and the file leg together, so one register slice read from both cannot hold two definitions of the same column. The depth is metres, toned against the run's own tolerance and banded into ranges for the filter menu. |
| `RunJournal` | **Manage ▸ what changed since the last run** — what a run's findings stood at when it last ran, and who the open ones were with, kept so a comparison does not shift under a colleague saving mid-read. Two per run, because a journal is a full id list and an unbounded history would be the largest thing in the project file. |
| `CheckDelta` | **Manage ▸ the changed-since line** — new, still there, gone, and reported again after being closed. Matching is per check through the ids a finding may also be recognised under, so a model that moved 40 mm reports drift rather than a wholesale change; regression is read from the intake, because the status lattice promotes only. A run with no journal reads as a first run and says so. |

### Issues out
| Engine | Surface |
|---|---|
| `ReportDocument` + blocks, `PdfReportWriter`, `XlsxWriter`, `HtmlReportWriter`, `KeyPlan`, `Deliverable` | Clash Report |
| `PngWriter` | **Clash Manager ▸ Manage ▸ a fall-hazard snapshot** — an RGBA raster as PNG bytes, beside `PngImage` which reads one. That snapshot is its only caller today: nothing routes a fall-hazard plan into the report or the pack writer. It returns PNG because every consumer of a picture in this product already takes PNG, so a raster handed out raw would make each of them grow an encoder. |
| `BcfWriter`, `BcfReader`, `BcfViewpoint`, `BcfCamera`, `BcfComponent`, `BcfColouring`, `BcfComment`, `BcfProject`, `BcfGuid`, `BcfFraming`, `ViewFraming` | **Clash Manager ▸ Exchange** and **Issue Manager ▸ Import / Export** |
| `BcfResponse`, `BcfResponseReport`, `IssueSheet` | **Clash Manager ▸ Exchange ▸ Take back** — where a returned status leaves a clash: an explicit re-open is honoured downwards, everything else is promote-only so a stale answer cannot undo a decision made since the pack went out. |

### Revisions
| Engine | Surface |
|---|---|
| `SnapshotBuilder`, `ModelSnapshot`, `SnapshotStore`, `ElementFingerprint` | Take a Baseline |
| `SnapshotBuilder` split per model or per set | **Compare Track ▸ Side by Side** — two loaded models, or two sets, compared against each other |
| `ModelCompare`, `ModelPair`, `ModelPairing`, `CompareSettings`, `ComparePolicy`, `CompareResult`, `ChangeKind`, `ChangeBucket` | Compare Revisions |
| `CompareLayers`, `ComparePalette`, `CompareView` | Compare — "Paint the changes" |
| `CompareStamp` | Compare — "Write the changes onto the elements" (new) |
| `CompareClusters` | Changes — "Group by: Cluster" (new) |
| `CompareIssues` | **Compare Track ▸ Changes** — "Raise them as issues" keeps the findings beside the model first and attaches them, so an issue points at a record that survives rather than one the button invented and dropped. |
| `CompareReport` | Compare — "Report" |
| `BaselineModel`, `BaselineModels` | Compare — loading the baseline so removals can be seen |

### Data
| Engine | Surface |
|---|---|
| `PropertyDiff`, `WriteDiff`, `PropertyChange` | Data Manager → Write Properties (the preview) |
| `Formula` | Write Properties → Formula; Data Manager calculated column |
| `FindReplace`, `ReplacementCount` | Write Properties → Find & replace |
| `ValueCleaner` | Takeoff tuning; Find & replace preset (new) |
| `Breakdown`, `BreakdownRecipe`, `BreakdownEntry` | **Work Breakdown** (new) |
| `DataLink`, `DataLinkRecipe`, `LinkRow`, `LinkPlan` | External Data |
| `SheetImport`, `XlsxReader`, `WorkbookTables` | Excel Round-trip — reading back |
| `DataReport`, `ReportTemplate`, `ReportTable`, `ReportRow`, `ReportColumn`, `ReportElement` | Report Builder |
| `LevelSet` | Levels & Zones |
| `LevelPlan`, `LevelStorey` | **Clash Manager ▸ Settings ▸ Levels** — the storeys the project says the building has, typed once as a name and a floor height and kept in the project file. `LevelSet` can read levels off a grid, off a Level property, or infer them from where things sit; none of those is what the client calls the second floor, and a federation routinely has no grid and spells its storeys three ways. A storey runs from its own floor up to the next one's, so only a floor height is asked for: a range per storey would let a gap open that clashes fall into and disappear from every band. The board bands by the storey a clash's own point falls on — the clash point, not either element, so a riser through six storeys is filed on the one the conflict is on. |
| `Takeoff`, `TakeoffResult`, `TakeoffPivot`, `TakeoffPivotTable` | Takeoff |
| `IdsChecker`, `IdsReader`, `IdsWriter`, `IdsDocument`, `IdsSpecification`, `IdsFacet`, `IdsEntityFacet`, `IdsValue`, `IdsElement`, `IdsReport`, `IdsProjection`, `IdsSheet` | IDS |
| `IdsFile`, `IdsPackResult`, `IdsJson` | **Model Audit ▸ IDS** — two ways to save, chosen at the moment of saving because it is a question about who the file is for: **Save IDS** writes the open standard everyone reads and names the rules it left behind, **Save BIMCamel rules** writes a `.bcids` carrying all of them. The `.bcids` is a zip holding a real `rules.ids` plus the rest, so somebody with none of this can rename, unzip and open the portable half. One reader takes either, told apart by the bytes rather than the file name. |
| `IdsScope`, `IdsScopeFacet`, `IdsScopeSpecification`, `IdsPortable`, `IdsStayedBehind` | **Model Audit ▸ IDS** — a rule can say who it is about in the model's own terms (a saved set, the current selection, a property) instead of by IFC class, which is what makes an IDS check a Revit or Civil 3D federation at all. The published schema's applicability is a closed sequence with no extension point, so a scoped rule stays here and **Save as .ids** names each one it could not carry and why. |
| `IdsFindings` | **Model Audit ▸ IDS** — every check writes its failures beside the model and the grid gains **Raise {n} as issues**, so "these forty doors are being scheduled separately" can be said once instead of every Tuesday. One finding per specification, identified by the specification's own identifier so rewording it does not lose the agreement; a specification that matched nothing is reported as its own kind of failure rather than buried in a green tick. |

### Sets & Views
| Engine | Surface |
|---|---|
| `SetExpression`, `SetCondition`, `SetOperator`, `DnfCompiler`, `SearchPlan`, `SearchClause`, `SetExpressionJson` | Set Builder |
| `Similarity` | Set Builder — "+ Elements like this one" (new) |
| `SetDrift`, `SetReading`, `SetDriftResult`, `SetDriftKind`, `SetBaselineStore` | **Model Audit ▸ Health** — every federation sweep reads what each saved set returns and says which have stopped returning what they did. The two that disarm rather than narrow — a set that emptied, and one the document no longer has — are faults; ordinary movement on a live model is not, because a check that reports every change is a check nobody reads. What each set held is kept beside the project, like the findings. |
| `SetDriftFindings`, `SetUse` | **Model Audit ▸ Health** — drift as findings under its own source, each naming the clash tests and scoped rules that were pointed at the set. That is the point of the check: a set going quiet is a curiosity until somebody knows what it disarmed. |
| `SetTree`, `SetTreeOptions`, `SetTreePlan`, `SetFamily` | **Generate a set library** (new) |
| `SetSchedule`, `SetRow`, `SheetSets`, `SetSheetMapping`, `SetSheetResult` | Sets from a spreadsheet |
| `Restructure`, `RestructureRecipe`, `RestructureChange`, `RestructureSnapshot`, `LevelTally` | Organise Sets |
| `AppearanceStack`, `AppearanceLayer`, `AppearancePlan`, `AppearancePlanner`, `AppearanceFold`, `AppearanceProperty`, `ILayerResolver`, `LayerReport`, `LayerStackJson` | Appearance |
| `ForeignAppearance`, `ForeignAudit`, `ForeignGroup` | Appearance — the amber "not ours" strip |
| `AppearanceTransfer`, `AppearanceSnapshot`, `TransferOptions`, `TransferMatch` | Appearance — "Copy appearance from another model…" (new) |
| `ViewpointPlanner`, `PlannedView` | Viewpoints |

### Deliver & Site
| Engine | Surface |
|---|---|
| `GltfWriter` | Export GLB |
| `XlsxWriter`, `DataReport` | Export Data (CSV / XLSX) |
| `DxfWriter` | Export Drawing (DXF), and Extract's drawing leg |
| `Extract`, `ExtractShare` | Extract |
| `Compilation`, `CompileJob`, `SourceFile` | Compile |
| `Dimensions`, `DimensionRow`, `Separation`, `SpacingRun`, `FieldElement` | Dimensions |
| `SettingOut`, `SetOutResult` | Setting Out |
| `MassProperties` | Centre of Gravity |
| `LabelSheet`, `LabelRun`, `QrCode` | Site Labels |
| `Attachments`, `AttachmentReport` | Documents & Photos |

### Automate
| Engine | Surface |
|---|---|
| `Graph`, `GraphRunner`, `GraphStep`, `NodeCatalogue`, `IGraphHost` | Graph Editor |
| (job list over the same services) | Batch |

---

## How to keep this true

`checks/validate-capability-map.py` fails the build when a public documented type in
`CamelWorks.Core` has no row here. Adding an engine means adding its surface in the same
commit — which is the only way a redesign stops losing things.
