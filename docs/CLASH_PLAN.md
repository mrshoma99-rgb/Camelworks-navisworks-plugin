# Clash Manager — implementation plan

Five reviews were run against the Clash Manager in September: a UI critique and a
UX critique of sixteen rendered surfaces, a BIM coordinator's practitioner
verdict on the same images, a performance review of the code, and an
architecture review of the code. Together they raised about 135 findings.

This plan says what is being done about each of them, in the order the work
happens. Every item names where it lands and what proves it done.

Two things are out of scope by decision:

- **A level axis on the clash matrix.** Rejected. The matrix stays discipline by
  discipline. Levels remain a banding and filtering facet.
- **Nothing else.** Every other finding is either scheduled below, already
  merged, or listed in the appendix as judged wrong with the evidence.

---

## Status

Phases 1 to 4 are implemented, with Phase 5 and Phase 6 partly done. What is left
is listed under **Not yet done** at the foot of this file, with the reason in
each case. Every phase was reviewed adversarially after it landed and the
findings fixed; those fixes are in the commits alongside the work.

---

## Phase 0 — merged

| What | Where |
|---|---|
| Renaming a clash test no longer detaches its findings | `Finding.Create`, `ClashFindings`, `FindingJson` |
| The matrix refuses to build nothing, and says which symbols are the reason | `MatrixWindow` |
| Renumbering refuses tests that have already been issued | `NamingWrite`, `ClashNaming` |
| Export and Import are one two-table bench with one switch | `ClashExchangeViews` |
| Links, picking bars and filter chips sit on one baseline | `SkinSheet.xaml`, `Ui.Picking` |

---

## Phase 1 — correctness and data loss

Findings that lose a coordinator's work. These go first whatever else is
waiting.

### 1.1 The exchange apply writes to a static field

`ClashExchange.Apply` sets `Status`, `AssignedTo` and appends comments on
`ClashItem`s that live in `CoordinateViews.LastBoard`, a process static. The
screen then says "Save on the Manage tab", but the Manage tab operates on
`Finding`s indexed by host id — it has never seen those items. `ClashRecord`
carries no assignee and no comments, so even the snapshot cannot hold them.

Six packs read, disputes resolved by hand, "14 moved, 9 assigned, 31 comments
written" reported — and the assignees and comments are gone when the tab is
left.

- Route `Apply` through `FindingRegister`, using `Finding.MergeFrom` and
  `FindingComment.Repeats`, which already do this job correctly.
- `ExchangeApplied` reports what the register took, not what was set on a DTO.
- **Done when** a test reads two packs, applies them, reloads the register from
  disk and finds the statuses, assignees and comments.

### 1.2 The snapshot serializer is untested UI code

`ClashSnapshot` and `ClashRecord` have no `ToJson`/`FromJson`, unlike every
other persisted Core type. They are written and read by inline `JsonValue`
construction in `CoordinateViews.Snapshot` and `CoordinateViews.Save`, plus
`Shift`/`Swap` for the one-step undo. Nothing tests the format. This is the file
that decides whether last month's approvals survive.

- Move to `Clash/ClashSnapshotJson.cs` beside `FindingJson`, with a version
  field.
- **Done when** a round-trip test covers every field and the UI holds no JSON
  construction for it.

### 1.3 Three BCF status vocabularies that disagree

- `BcfResponse.StatusOf` — explicit list, unknown word returns null and is
  reported by name. Its own doc says it exists "in one place" for this reason.
- `ClashBcf.Status` — private, substring matching, different answers: `open`
  becomes New rather than Active, and an unknown word becomes Active rather
  than being reported.
- `FindingStatusLattice.TryParse` — exact names only.

The same `.bcfzip` read through two paths yields two statuses.

- `ClashBcf.Status` calls `BcfResponse.StatusOf`, keeping its substring pass as
  a documented second tier.
- **Done when** one test reads a pack through both paths and asserts one answer,
  including for "Awaiting client".

### 1.4 The status mapping is invisible

A trade returns forty topics marked Rejected and nothing on screen says where
they land.

- The Import half shows the mapping it will apply, and lists the words it could
  not map with their counts, before Apply.
- **Done when** the render shows a pack of unmapped statuses named rather than
  counted.

---

## Phase 2 — performance

Ranked by wall-clock on a real job: 18,000 results, 780 matrix pairs, 40
disciplines, 400 tests, 500k model leaves.

### 2.1 `BcfReader.Visualisation` scans the whole zip per viewpoint

Two `FirstOrDefault` passes over `zip.Entries` for each viewpoint. A pack of
18,000 clashes holds about 54,000 entries, so one import is up to 2×10⁹ string
comparisons — minutes.

- Build one `Dictionary<string, ZipArchiveEntry>` on `OrdinalIgnoreCase` in
  `Read` and pass it down; a second keyed on folder for the suffix fallback.

### 2.2 Element keys cost three SHA256 objects each

`Hash.Of` does `SHA256.Create()` per call. `ElementKey.Make` calls it twice, one
of them for `UnscopedValue`, which its own doc says is diagnostic only.
`NavKeys.Of` adds `ScopeOf(model)` per item, which reads a host property and
hashes the path again. `NavDocument.Index` keys every geometry leaf: 1.5M hash
objects on a 500k-leaf federation, before any clash work.

- Memoize the model scope per `Model`.
- Compute `UnscopedValue` lazily.
- Hold a `[ThreadStatic]` hasher and hash UTF-8 bytes without the intermediate
  string.

### 2.3 Every realised board row runs two full filter passes

`Finish` calls `Verbs(row)` per realised row; `Verbs` calls `Reaching(row)`;
with a multi-row selection live that calls `Picked()`, which runs `GroupRows`
and `LooseRows` over the whole test rather than reading `Shape(entry)`.
Scrolling a screenful over a 5,000-result test with 40 rows ticked is on the
order of 450,000 filter evaluations. This is the scroll stutter that survived
the virtualiser.

- Compute `Picked()` once per redraw under the `Shape` cache signature.

### 2.4 The rest of the performance list

| Finding | Fix |
|---|---|
| `NavSearch` keys the same items three or four times | Key once into a dictionary and reuse |
| Status/assign/comment writes walk the whole test tree | Use the existing `ById` cache |
| Bulk test operations are O(tests²) with a tree walk each | One id→test dictionary per method |
| `SelectResults` dedups with a linear `Contains` on a host collection | `HashSet<ModelItem>`, one `CopyFrom` |
| `MatrixSource.Fingerprint` recomputed per cell comparison | Memoize; the type is immutable |
| `MatrixTally.Worst` is a LINQ max per cell | Cache on `MatrixCounts` |
| `Hidden`/`Folded` are O(lines) per cell | Precompute arrays per redraw |
| `ClashExchange.Read` matches topics linearly | One `HashSet` of file guids per pack |
| `ExchangeRow.IsDisputed`/`Settled` recomputed six times per row | Cache, invalidated by `Add` and `Chosen` |
| The exchange desk re-flattens the board several times per redraw | One `Results()` per redraw; reuse the `ExportSelection` |
| `ExportProfile.Rounds` trims every group per recipient | Bucket in one pass |
| `Ui.Table`'s tone bar is O(n²) per scroll batch | Walk realised containers; skip `Plain` |
| `Order()` rebuilt about fifteen times per row | Compute per redraw; pass a column index map |
| `TestEntry.Of` called eight times per rail row | Five-slot count array, invalidated by the status setter |
| Issue register re-parsed and re-scanned after every edit | Keep it on the session; update incrementally |
| `Write` rebuilds the finding index per row | Batch the `Observe`, index once |
| `Hydrate` re-resolves model and category per side | Two dictionaries local to the loop |
| `XlsxWriter.Reaches` scans merges and styles per row | Precompute a per-row maximum column |
| `ClashPipeline` flags with a linear `Contains` | Parallel `HashSet` |
| `ByTreePath` re-walks from the root per locator | Memoize path prefixes across the loop |
| String keys where the value type would do | Key on `ClashKey`/`ElementKey` directly |
| `NavDocument.Sets()` walks the tree per call | Cache by document generation |
| `Ui.Table` sort parses every cell twice | Project sort keys once |

`MatrixWindow`'s per-cell `ContextMenu` construction is suspected, not
confirmed. Measure before changing it.

**Read and left alone.** Three items on the list above were traced and judged
not worth the change they would take:

- `NavDocument.Sets()`. The document generation bumps when a model is appended,
  removed or refreshed and never when somebody makes a selection set, so a cache
  keyed on it would answer with the list from before the set they just made. The
  host offers no cheaper signal.
- `MatrixWindow.Hidden`/`Folded`. Both return before the scan unless the line's
  folder is actually shut, so the quadratic only bites while folders are
  collapsed, and the cache it would take has no reliable invalidation.
- The issue register re-read. The four call sites are edit actions, not
  per-row draws.

`ElementKey.UnscopedValue` stays eager. Making it lazy means the struct
retaining its parts array, which pins a host tree path for each of 500,000
leaves — a memory cost in place of a hash that is now cheap.

**Do not touch:** `BoardList`, `Shape`, `Row.Blob`, the 160 ms search debounce,
`Resize()`, `RailChanged`/`RepaintRail`, `NavClashSource.Described` and `Focus`,
the `_byId` generation cache, `ClashPick.Take`'s facet hoisting,
`CheckDelta.Between`'s two-pass claim (the carry-over match this replaced),
`MatrixCounts`, the `Ruler` and measurement caches,
`Job`'s pump throttle, `BcfWriter`'s compression choice. The performance review
names each of these as already correct.

---

## Phase 3 — the screens tell the truth

Four reviewers concluded that features were missing which are in fact built. The
engines are right; the screens do not say what they do, and the harness fixtures
are too thin to show it. This phase is the cheapest large win in the plan.

### 3.1 Say that the rules grouped the board

`ClashPipeline` groups by level, grid, zone, model pair, discipline, test,
system, property, same item and proximity, and the board already reports
"Nothing is configured, so the built-in rules ran". The screen's loudest
grouping message is "143 loose results — tick two or more and press Group",
which reads as though hand-grouping is the only mechanism.

- The band says which rules ran and how many groups they made, with a route to
  the rule editor.
- The loose band says the rules left these behind, not that grouping has not
  happened.

### 3.2 Say that statuses carried over

The carry-over runs on every board build and `delta` is already reported as a
note among others. (Since Phase 8 the carry-over is `CheckDelta` over
`RunJournal`; `ClashCarryOver` is deleted.)

- The carry-over result is a first-class line on the board head: how many
  carried, how many are new, how many regressed, how many matched only on a
  neighbouring cell.

### 3.3 Make the harness fixtures real

Three reviewers inferred absent features from thin fixture data: four filter
chips, disciplines in the assignee column, no folders, no previous run. The
renders are the review instrument and they misrepresent the product.

- The clash fixture gains a roster with people, folders, two runs so a delta
  exists, results at every status, and property columns.
- **Done when** a render of the Manage tab shows assignment to a person, a
  populated filter panel and a carry-over line.

### 3.4 Other invisible capability

| Built | Screen change |
|---|---|
| `TeamRoster` — people, roles, companies, teams | Assignment surfaces the roster, not a discipline string |
| `ClashBcf.Viewpoint` writes cameras and snapshots | The export says a viewpoint is written per topic, and offers the controls in 5.3 |
| Chained grouping and multi-select filters | The filter panel shows every facet, not the four the fixture holds |
| `MatrixDiff`, `Freeze`, week folders | A previous-run column and per-cell delta on the matrix results view |

---

## Phase 4 — UI craft

The UI critique's list, grouped by where the fix lands. Shared primitives first,
because most of the report is one defect seen in several places.

### 4.1 Shared

- Field cards reserve the `Remove` slot so the arrow column holds one position.
- Token chips get a column sized to the longest token instead of truncating.
- Windows drawn into a 580–670px column inside 1180 get a title bar and either
  centre or use the width.
- One selection accent. Group rows lose the second border colour.
- Dialog dismissal is a button, not a text link, and sits away from a
  destructive primary.
- Red is reserved for errors. Skipped rows in a preview are muted, not red.

### 4.2 Manage

- The results pane fills the window; the footer keeps its buttons inside it.
- Three stacked footers collapse to one command bar.
- `Clashes` leaves the clash-row level; its width goes to `Item 1`, `Item 2`
  and `System Name`.
- Count columns get a key, full names and headers over their own numbers.
- The filter panel overlays rather than pushing the table down and
  desynchronising the two pane headers.
- One clear-filters control, in the filter panel.
- The NOT GROUPED band is one line plus a count.
- "14 waiting" names what is queued and what the play button starts.

### 4.3 Matrix

- The results view fills one triangle, or both on one scale.
- The warning triangle is reserved for actual defects — not stamped on every row
  label and column header.
- The legend shows a ramp with min and max, not a sampled value as a swatch.
- Blank and "no test covers it" are told apart, and uncovered is the loud one.
- The second toolbar row splits into static counts, real buttons and icon
  buttons.
- The details panel's palette and progress move to a fixed footer so the
  row-specific content starts at the top.
- Row actions are labelled, with the destructive pair separated.
- The discipline band is pinned so a partially visible group still reads, and the
  scrollbar attaches to the grid.
- One coordinate system: the display index, the Number field and the column
  letters must agree between views.
- The start screen has one primary path; the dead card offers a next step.

### 4.4 Exchange

- The `In` column is a checkbox, as the Manage board is.
- The two icon buttons beside the primary are labelled or moved.
- Every active filter is a removable chip, with the excluded count.
- Apply is disabled until a pack is read.
- Empty-state cards are centred in their panes.

---

## Phase 5 — capability that is genuinely missing

### 5.1 A per-result issue history, and a delta filter

`LastExportedTicks` is per test. A weekly issue is a delta: new, reopened, and
do not touch what is in progress.

- Stamp each result when a pack carrying it is written.
- Facets: not previously sent, changed since last sent, sent on or before a date.
- **Done when** a profile can express "everything new or reopened since the last
  pack to this party".

### 5.2 Party override on import

The party is taken from the file name. What arrives is
`RE_ L3 clashes - responses FINAL v2 (3).bcfzip`, and one from the sprinkler
subcontractor called `Mechanical.bcfzip`.

- The Packs table's Party column is editable, and the rows redraw.

### 5.3 BCF viewpoint controls on export

Viewpoints and snapshots are written; nothing controls them.

- Camera direction, section box, whether the two clashing elements are
  highlighted, and image size.

### 5.4 Conflicts that are not two-sided

Three parties disagreeing is normal, and the resolution is usually a note and an
action rather than a winner.

- More than two answers on a row, a note on the resolution, and the losing
  answers kept and readable.

### 5.5 Self-clash

`IsSelfPair` is skipped everywhere, so duct-versus-duct inside one discipline
cannot be expressed. It is a test that runs every week.

- The diagonal takes a symbol, with the pair count and the planner following.

### 5.6 Element identity on the board

Triage is done from a picture and a grid reference.

- A level column, a grid reference, resizable and addable columns, and a preview
  of the two elements.

### 5.7 Priority at the right grain

Priority is per matrix cell and hand-set: 780 pairs to rank by hand, and within
one pair a duct through a beam web and the same duct clipping a secondary are
not the same.

- Priority moves to the clash or the group and is derivable from penetration
  depth, category and level.

### 5.8 Run state

- Per-test last-run time and duration, which tests are stale because their source
  models changed, and a queue.

### 5.9 Smaller

- Levels: unit control, ordering and duplicate validation, undo on Remove, and a
  line saying what a level list is for.
- Tests on no naming pattern get a bucket on the Manage board, not a footnote in
  a modal.
- A level token in the naming patterns — every real test name leads with one.
- "Digits" reads as "Least digits", which is what the field does.
- Geometry belonging to no matrix row is reported.
- Read from the model says it replaces the matrix on screen.

---

## Phase 6 — structure

The architecture review's verdict was that the core is deliberate, not a pile —
but that two complete spines describe the same fact, and the newer one has not
finished displacing the older one. Almost every structural finding follows from
that.

### 6.1 One spine

`ClashItem`/`ClashSnapshot` and `Finding`/`FindingRegister` are two independent
read → state → persist paths over the same host read, with no conversion between
them. Status, assignee, group, comments and priority each have two homes that
can disagree, and only one of them has merge semantics.

- `Finding` is the record. `ClashItem` becomes a derived view for the rules.
- `CoordinateViews.Build` projects from the register instead of re-reading the
  host.
- `ClashSnapshot` persistence goes once carry-over runs on finding identity.

This is a quarter of work, not a week. Phases 1 to 5 do not depend on it, and
1.1 is the first payment against it.

### 6.2 One BCF writer

`ClashBcf` and `FindingBcf` are two round trips over one format with label
namespaces neither recognises, so a file written by one and read by the other
takes the writer's labels for the reviewer's tags.

They do agree on the topic GUID for a clash, and the agreement is one line deep
on each side: `ClashFindings.From` stamps `ClashBcf.TopicGuidFor(key)` onto the
finding, and `FindingBcf.TopicGuidFor` prefers a stamped value over one derived
from the finding id. It was undocumented on both sides; it is now stated in both
and pinned by a test in `Core.Tests`.

- One writer over `Finding`, clash facts in `Finding.Facts`, one label
  namespace.

### 6.3 One status type on the main screen

The grid uses status strings — 31 literals across UI and Nav — so it cannot use
the lattice it was built with.

- `Row.Status` becomes `FindingStatus`; the host boundary does the one
  conversion.

### 6.4 One grouping vocabulary

Three grouping implementations and four facet vocabularies. `ClashFacets` is the
right shape and is visible only to the export screen.

- The Manage grid's banding becomes an `IGroupingRule` stack over the same
  items; `Bucket`'s bespoke cases become new factories.

### 6.5 One clash parameter descriptor

Adding a parameter is six edits, none of them a declared list.

- One `ClashParameter` table drives the facet, the predicate factory, the spec
  kind and the BCF sidecar round trip. Extend `ClashFacets`; do not build a
  rival.

### 6.6 A testable clash seam

`IClashSource` has no fake. Everything downstream of the clash read is verified
only by hand on a licensed seat.

- `Testing/FakeClashSource.cs` and `ClashSeamContractTests`, mirroring the
  existing `FakeDocument`/`SeamContractTests` pattern, with the same assertions
  added to `SelfTest`.

### 6.7 The two large files

`ClashManageViews.cs` is 10,821 lines and `MatrixWindow.cs` is 10,756. Both are
accumulated rather than structural. The read/keep/index/sync cluster is not UI
at all and moves to Core with 6.1; `MatrixEditSession` (board, legend, undo,
dirty state) moves to Core with tests, leaving the window to draw.

### 6.8 Smaller

- An export writer seam: five writers with three signatures called from sixteen
  places.
- One comment type and one repeat test; there are three of each.
- One `EditHistory<T>` in Core; there are two undo mechanisms in the WPF layer.
- Named keys for the clash section of the project file; fifteen are string
  literals at their use sites.
- A clash application service, so "the board" stops being a process static five
  screens read.
- `IClashSource` moves out of `IViewpointStore.cs`.
- `MatrixTest` becomes a table-driven value rather than a closed enum crossing
  the host boundary.
- `MarkerOverlay` gets a document scope.
- `ArchitectureTests` moves out of `HashTests.cs`.

### 6.9 The harness stops being a second codebase

`Studio/Panels.cs` reconstructs each screen by hand, and its own rule says a
disagreement means the reconstruction is wrong. That doubles the cost of every
UI change and of splitting the two large files.

- Once the screens take an injected session (6.7), Studio draws the real views
  against `FakeDocument`, `FakeClashSource` and `InMemoryFileSystem`, and the
  reconstruction is deleted.

---

## Appendix — findings judged wrong

Recorded so they are not raised again. Each was reported from screenshots.

| Finding | Evidence against |
|---|---|
| "Nothing handles clash identity across re-runs" | A carry-over runs on every board build: two-pass claim with neighbour tolerance, keyed on `ClashKey` then, since Phase 8, on the finding id with the neighbouring cells as aliases. The architecture review calls it the hardest logic in the product and correct. |
| "Grouping is manual, one tick at a time" | `ClashPipeline` + `GroupingRules`: level, grid, zone, model pair, discipline, test, system, property, same item, proximity. The board reports the funnel. |
| "Assigned to is a discipline, not a person" | `TeamRoster`/`TeamMember` hold people, roles, companies and teams. The fixture uses disciplines. |
| "BCF export has no viewpoints" | `ClashBcf.Viewpoint` writes a camera per topic and snapshots are counted. The controls are missing; the viewpoints are not. |
| "The filter panel has four chips" | Chained grouping and multi-select facets exist. The fixture holds four values. |
| "No trend, anywhere" | `MatrixDiff` and `Freeze` exist and runs are kept as week folders. Nothing compares them on screen — that part is real, and is 3.4. |
| "The number field is inconsistent with its own preview" | Pad is a minimum width, so 200 stays 200. The label is wrong, not the behaviour; see 5.9. |

The pattern is that a thin harness fixture reads as a missing feature. Phase 3.3
addresses the instrument itself.

---

## Not yet done

Everything above that has not been implemented, with why.

### Phase 4

- **4.3, the matrix.** The results view filling one triangle, the toolbar row
  splitting, the details panel's fixed footer, labelled row actions, the pinned
  discipline band, one coordinate system, and the start screen's single primary
  path. The legend ramp, the reserved warning triangle and the blank-versus-
  uncovered distinction are done. The rest needs the matrix rendered and read
  screen by screen, which is a pass of its own.
- **4.2, the footers.** Three stacked footers collapsing to one command bar, and
  the count columns' key and full names.

### Phase 5

- **5.6, element identity on the board.** A grid reference, and a preview of the
  two elements. The Level column is done; the preview needs a host render per
  row, which is a seam that does not exist yet, and a grid reference needs the
  project's grid, which nothing reads.
- **5.7, priority at the right grain.** Moving priority from the matrix cell to
  the clash or the group, and deriving it from penetration depth, category and
  level. It changes where priority is stored, so it belongs with 6.1.
- **5.8, run state.** Per-test last-run time and duration, which tests are stale
  because their source models changed, and a queue.
- **5.9, one item.** Reporting geometry that belongs to no matrix row. It needs
  an element count per source, which the matrix screen cannot ask for: rows
  point at saved sets and nothing resolves a set to a count. Reported at the
  model level instead, it would call a discipline uncovered because its row
  reaches it through a set. The rest of 5.9 is done: the levels unit control,
  ordering, duplicate validation, undo on Remove and a line saying what the
  list is for; the bucket for tests on no naming pattern; the `{level}` token;
  "Least digits"; and "Read the tests" saying it replaces the matrix on screen.

### Phase 6

- **6.1, one spine.** `Finding` becoming the record and `ClashItem` a derived
  view. The plan says this is a quarter of work rather than a week, and nothing
  in phases 1 to 5 depends on it; 1.1 is the first payment against it.
- **6.2, one BCF writer.** `ClashBcf` and `FindingBcf` are still two round trips
  over one format.
- **6.3, one status type.** 31 status literals across UI and Nav.
- **6.4, one grouping vocabulary**, and **6.5, one clash parameter descriptor**.
- **6.7, the two large files**, and **6.9, the harness drawing the real views**.
  Both wait on 6.1.

`FakeClashSource` and `ClashSeamContractTests` (6.6) are done, as are the named
project-file keys and the two file splits from 6.8. The fake now refuses what
the host refuses, and `SelfTest` runs the same assertions on a support machine.

Note on the rendered pages: the Manage board in `shots/app-clash-manage` is
drawn by the Studio harness's own table, not by `ClashManageViews`. Changes to
the product's board — the Level column and the group name taking the two item
columns among them — do not appear in it. That is 6.9.
