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
| `ClashItem`, `ClashSnapshot`, `ClashDelta`, `ClashCarryOver`, `ClashHydration`, `GroupDecision`, `DuplicateCollapse` | Clash Board |
| `ClashTestSides` | Clash Matrix — "The model's tests" mode reads each test's sides back into categories |
| `ClashResultInfo` | Clash Results (Manage tab) — every row of the results grid, and the Board's read behind it |
| `Issue`, `IssueRegister`, `IssueComment` | **Issue Manager ▸ Issues** — the register and its detail panel; raised by hand, from clash results, from clearance findings, or read out of BCF |
| `IssueBcf`, `IssueImportResult` | **Issue Manager ▸ Import / Export** — BCF in as a merge, BCF out as one topic per issue |
| `ManualIssue`, `ManualIssueList` | Board pipeline — issues raised in earlier versions still ride the board into reports and BCF |
| `ClashMarkers`, `MarkerMesh` | Clash Board — "Show the clash points" (new) |
| `Resolution`, `FindingStatus`, `FindingStatusLattice`, `Finding` | Clash Board detail + Review |
| `ClashMatrix`, `MatrixSide`, `MatrixOptions`, `MatrixPlan`, `MatrixPlanner`, `MatrixCategory`, `MatrixGrid`, `MatrixSymbol`, `SheetMatrix`, `SheetMapping` | Clash Matrix (both forks) |
| `Headroom`, `HeadroomElement`, `HeadroomSpan`, `HeadroomResult` | Clearance Check — the engine behind the findings grid |
| `Freeze`, `ChangeNotice` | **Compare Track ▸ Freeze** and **▸ Change Notice** — sign-off scoped by set, selection, everything or the board |
| `Penetrations`, `PenetrationSchedule` | **Penetrations** (new) |
| `PartyLedger` | Clash Report — per-party split |
| `ClashXml`, `ClashXmlTest`, `ExportProfile` | Clash XML |

### Issues out
| Engine | Surface |
|---|---|
| `ReportDocument` + blocks, `PdfReportWriter`, `XlsxWriter`, `HtmlReportWriter`, `KeyPlan`, `Deliverable` | Clash Report |
| `BcfWriter`, `BcfReader`, `BcfViewpoint`, `BcfCamera`, `BcfComponent`, `BcfColouring`, `BcfComment`, `BcfProject`, `BcfGuid`, `BcfFraming`, `ViewFraming` | BCF — send / receive |
| `BcfMerge`, `MergeConflict`, `BcfResponse`, `BcfResponseReport`, `IssueSheet` | Merge Markups |

### Revisions
| Engine | Surface |
|---|---|
| `SnapshotBuilder`, `ModelSnapshot`, `SnapshotStore`, `ElementFingerprint` | Take a Baseline |
| `SnapshotBuilder` split per model or per set | **Compare Track ▸ Side by Side** — two loaded models, or two sets, compared against each other |
| `ModelCompare`, `ModelPair`, `ModelPairing`, `CompareSettings`, `ComparePolicy`, `CompareResult`, `ChangeKind`, `ChangeBucket` | Compare Revisions |
| `CompareLayers`, `ComparePalette`, `CompareView` | Compare — "Paint the changes" |
| `CompareStamp` | Compare — "Write the changes onto the elements" (new) |
| `CompareClusters` | Changes — "Group by: Cluster" (new) |
| `CompareIssues` | Changes — "Raise on the board" |
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
| `Takeoff`, `TakeoffResult`, `TakeoffPivot`, `TakeoffPivotTable` | Takeoff |
| `IdsChecker`, `IdsReader`, `IdsWriter`, `IdsDocument`, `IdsSpecification`, `IdsFacet`, `IdsEntityFacet`, `IdsValue`, `IdsElement`, `IdsReport`, `IdsProjection`, `IdsSheet` | IDS |

### Sets & Views
| Engine | Surface |
|---|---|
| `SetExpression`, `SetCondition`, `SetOperator`, `DnfCompiler`, `SearchPlan`, `SearchClause`, `SetExpressionJson` | Set Builder |
| `Similarity` | Set Builder — "+ Elements like this one" (new) |
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
