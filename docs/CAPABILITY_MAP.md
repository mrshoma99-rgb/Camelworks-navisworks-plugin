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
| 1 | `Clash/SheetMatrix` + `SheetMapping` + `MatrixGrid` + `MatrixSymbol` | **Dropped.** The redesign's Matrix wizard only hand-filled a grid. The shipped tool's *primary* path reads the client's own matrix spreadsheet, at whatever row/column their layout puts things. | **Clash Matrix step 1 becomes a fork:** "Read a matrix I already have" (xlsx, with the mapping step) or "Pick sides and fill it in here". The spreadsheet path leads, because that is what arrives by email. |
| 2 | `Data/Breakdown` (WBS) | **Dropped, and never in `Tools.cs`.** Buried as `BreakdownStamp` inside the old Data tab. | **Work Breakdown** — its own window, Data panel. Reads the selection tree's shape, writes `WBS1…WBS6` + the joined key. |
| 3 | `Clash/Freeze` + `ChangeNotice` | Dropped. Records that a scope was agreed, and what has changed since. | **Freeze** (act) and **Change notice** (window), Revisions panel. |
| 4 | `Clash/Penetrations` + `PenetrationSchedule` | Dropped. Services through structure, as a schedule for the structural engineer. | **Penetrations** — window, Issues panel. |
| 5 | `Project/ModelManifest` + `ModelIntake` + `TestRecord` | Dropped. What is loaded, at which revision, and restoring placement. | **Models** — window, Project panel. |
| 6 | `Appearance/AppearanceTransfer` | Dropped. A colour scheme lifted off one model onto another. | Appearance panel toolbar: "Copy appearance from another model…". |
| 7 | `Report/DxfWriter` | Dropped. The Export split only had GLB/CSV/XLSX. | Export split gains **Drawing (DXF)**. |
| 8 | `Project/ActivityLog` | Dropped. What has happened on this project, newest first. | **History** — window, Project panel. |
| 9 | `Clash/ClashMarkers` + `MarkerMesh` | Dropped. Clash points as geometry you can actually see. | Clash Board toolbar toggle: "Show the clash points". |
| 10 | `Compare/CompareStamp` | Dropped. Writes what changed onto the elements as properties. | Compare Revisions post-action: "Write the changes onto the elements". |
| 11 | `Compare/CompareClusters` | Dropped. Groups changes that sit near each other. | Changes panel — "Group by: Cluster (nearby changes)". |
| 12 | `Sets/Similarity` | Dropped. Finds elements like the one selected. | Set Builder: "+ Elements like this one". |
| 13 | `Sets/SetTree` + `SetFamily` | Dropped. A whole set library generated from an ordered property list. | **Generate a set library** — steps, under the Set Builder split. |
| 14 | `Sets/SheetSets` + `SetSheetMapping` | Half-dropped. Distinct from `SetSchedule`: reads set definitions out of the spreadsheet a project already keeps. | Folded into **Sets from a spreadsheet**, whose mapping step handles both layouts. |
| 15 | `Store/ProjectLease` + `StorageSubstrate` | Dropped — and it is not a tool, it is a state nobody drew. | Every panel: a blue strip when someone else holds the project — "Ahmed has this project open. You can look, not change. Take it over". |
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
| `ManualIssue`, `ManualIssueList` | Clash Board — "Raise an issue" |
| `ClashMarkers`, `MarkerMesh` | Clash Board — "Show the clash points" (new) |
| `Resolution`, `FindingStatus`, `FindingStatusLattice`, `Finding` | Clash Board detail + Review |
| `ClashMatrix`, `MatrixSide`, `MatrixOptions`, `MatrixPlan`, `MatrixPlanner`, `MatrixCategory`, `MatrixGrid`, `MatrixSymbol`, `SheetMatrix`, `SheetMapping` | Clash Matrix (both forks) |
| `Headroom` | Clearance |
| `Freeze`, `ChangeNotice` | **Freeze** + **Change notice** (new) |
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
| `GltfWriter` | Export → Geometry (GLB) |
| `XlsxWriter`, `DataReport` | Export → Properties (CSV / XLSX) |
| `DxfWriter` | Export → **Drawing (DXF)** (new) |
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
