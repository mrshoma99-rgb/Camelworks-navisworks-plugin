# Checks — implementation plan

The Clash Manager runs one kind of check. Three more are wanted — clearance, fall hazard,
and whatever comes after — and each of them needs the results board the clash tab
already has: grouping, sorting, filtering, assignment, comments, issues, markers,
spreadsheets and BCF in and out.

That board is 11,148 lines. `ClearanceViews.cs` is the second copy of about two
thirds of it, at 1,906 lines, and its own doc comment says so. IDS, model audit and
fall hazard would be copies three, four and five.

This plan makes the Manage tab the one viewer for every check, makes each check a
descriptor rather than a screen, and settles what a finding is and what an issue is
so the record layer carries checks that do not exist yet.

Five audits sit behind it: the findings and issues record layer, what on the Manage
board is generic, the clearance check as it exists, the BIMCamel walkable-surface
and clearance engine, and the Dyncamelo fall-hazard nodes. Their conclusions are
used here rather than restated.

---

## 1. The architecture

### 1.1 The shape in one paragraph

A **check** is a descriptor, not a screen. It names itself, lists its **runs**,
reads its runs into **findings**, declares the **columns** its findings fill, and
optionally offers a host write-back, a runner, a namer, a property reader and a
geometry seam. The Manage tab holds one check at a time and draws its findings.
Everything a check produces is a `Finding`; everything a check knows that the
record does not have a field for rides `Finding.Facts`. Nothing about adding a
check touches the board, the register, the JSON version or any existing engine.

### 1.2 The check descriptor

New namespace `CamelWorks.Core.Checks`.

```csharp
/// One kind of check. Registered once; the Manage tab lists them in a picker.
public interface ICheck
{
    /// Stable key, written to the project file and used as the finding source.
    string Key { get; }

    /// What the picker shows. "Clash detection", "Clearance", "Fall hazard".
    string Name { get; }

    /// What one run is called: "test", "run", "specification", "rule".
    string RunWord { get; }

    /// What one result is called: "clash", "breach", "opening", "failure".
    string ResultWord { get; }

    bool IsAvailable { get; }

    /// Why not, in one sentence, for the empty state. Null when available.
    string? Unavailable { get; }

    /// The per-result columns this check declares, beyond the standing ones.
    IReadOnlyList<ResultColumn> Columns { get; }

    /// How bad, normalised: 1.0 is exactly at the run's threshold, null is no
    /// measure. The board bands and tones on this and never on millimetres.
    double? Severity(Finding finding);

    /// What the severity band is called on this check. "Worse than 25 mm".
    string SeverityBand(double? severity);

    /// Every run this check can offer, without reading any results.
    IReadOnlyList<CheckRun> Runs();

    /// Read results. Null runIds means every run.
    CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress);

    ICheckWriteBack? WriteBack { get; }   // null = the results live nowhere but here
    ICheckRunner? Runner { get; }         // null = cannot be re-run from this tab
    ICheckNaming? Naming { get; }         // null = the names are not ours to change
    ICheckProperties? Properties { get; } // null = no element-property columns
    ICheckGeometry? Geometry { get; }     // null = cannot point at anything
}
```

A null capability is the honest answer for four of the five checks, not a degraded
mode. The board already lives with all five absences on its file leg — 52 branches
on `OnFile` — and roughly 40 of them collapse into capability nulls.

### 1.3 The registry

```csharp
/// Every check the session can offer. Built once per session.
public sealed class CheckCatalogue
{
    public void Add(ICheck check);
    public ICheck? Find(string? key);
    public IReadOnlyList<ICheck> All { get; }
    public IReadOnlyList<ICheck> Available { get; }
}
```

Adding a check is one `Add` call at session build and one class implementing
`ICheck`. No enum member, no switch, no board edit, no JSON version bump.

### 1.4 Runs and the run record

```csharp
/// What the rail draws one line for. Replaces ClashTestInfo + ClashTestSides.
public sealed class CheckRun
{
    public string Id { get; }
    public string Name { get; }
    public string? Folder { get; }

    /// "Hard clash", "Clearance", "IDS specification" — the rail's hint word.
    public string Kind { get; }

    /// "Mechanical v Structure", "Corridors, 2.1 m", "IDS-2024-03.ids".
    /// The check writes this sentence; the board never composes it.
    public string Between { get; }

    /// The threshold, worded: "25 mm", "2.1 m", "grade C". Null when none.
    public string? Threshold { get; }

    /// Discipline chips on the rail. Empty is legitimate.
    public IReadOnlyList<string> Trades { get; }

    public int ResultCount { get; }
    public long LastRunTicks { get; }

    /// True when this product created the run, so it may be renamed and re-run.
    /// False for a host clash test read back, an IDS specification, an audit rule.
    public bool IsOurs { get; }
}
```

Runs this product owns are saved in the project file, not the model:

```csharp
/// A saved, named, re-runnable check run. The settings are an open bag, so a new
/// check adds a run type without a schema change.
public sealed class CheckRunSpec
{
    public CheckRunSpec(string checkKey, string id, string name);

    public string CheckKey { get; }
    public string Id { get; }
    public string Name { get; set; }
    public string? Folder { get; set; }
    public IDictionary<string, string?> Settings { get; }
    public long LastRunTicks { get; set; }

    public JsonValue ToJson();
    public static CheckRunSpec? FromJson(JsonValue? json, IList<string>? notes = null);
}

/// The saved runs of every check, in the project file under one section.
public sealed class CheckRunStore
{
    public const string Section = "checks";

    public IReadOnlyList<CheckRunSpec> For(string checkKey);
    public CheckRunSpec? Find(string checkKey, string id);
    public void Put(CheckRunSpec spec);
    public bool Remove(string checkKey, string id);

    public static CheckRunStore Read(ProjectStore store, IList<string>? notes = null);
    public void Write(ProjectStore store);
}
```

`Settings` is a string bag for the same reason `Finding.Facts` is: the clearance
run stores `minimum`, `surfaces`, `targets`, `slope`, `minArea`; the fall-hazard
run stores `level`, `band`, `cell`, `minGap`, `handrails`; neither needs the store
to know what those words mean. `ClashTestNames` generalises into this store as the
project-side name for a run the host will not let us rename.

### 1.5 The read, and its scope

```csharp
/// One read of one or more runs. The board's Read().
public sealed class CheckRead
{
    public IReadOnlyList<CheckRun> Runs { get; }

    /// Every result, already a Finding. Parents carry locations; leaves do not.
    public IReadOnlyList<Finding> Findings { get; }

    /// Which run a finding came from. The rail's tallies are built from this.
    public string RunOf(Finding finding);

    /// The predicate FindingRegister.Observe is scoped by, so reading three of
    /// twelve runs does not mark the other nine absent. Never null.
    public Func<Finding, bool> Scope { get; }

    public IReadOnlyList<string> Notes { get; }
}
```

`Scope` is not optional and not a convenience. `ClearanceViews` calls `Observe`
with no scope today, which is correct only while there is exactly one unnamed
clearance run; the moment there are named runs it silently marks every other run's
findings absent. Making it a non-nullable member of the read is the guard.

### 1.6 Columns, and why per-check data does not grow the record

The board's row is a `Finding`. There is no `CheckResult` type. A declared column
names a `Finding.Facts` key and the board reads it:

```csharp
public enum ColumnKind { Text, Number, Element }

/// A column a check declares. Rides the machinery PropColumn already uses.
public sealed class ResultColumn
{
    public ResultColumn(string key, string title, ColumnKind kind);

    /// The Finding.Facts key this column draws, or the element index for Element.
    public string Key { get; }
    public string Title { get; }
    public string? Unit { get; }
    public ColumnKind Kind { get; }

    public double Width { get; set; }
    public bool RightAligned { get; set; }

    /// For Kind == Element: which of Finding.Elements this column draws.
    public int ElementIndex { get; set; }

    /// Numeric sort and range filtering. Null means sort as text.
    public Func<Finding, double?>? Number { get; set; }

    /// Worse than this run's threshold. Null means the column is never toned.
    public Func<Finding, bool>? IsBad { get; set; }
}
```

`Finding.Facts` is an `IDictionary<string, string?>` round-tripped verbatim by
`FindingJson`, and every producer already uses it: clash writes `Host id`,
`Host test`, `Distance`; clearance writes `Obstruction`, `Clear height`,
`Least allowed`, `Short by`; IDS writes `Applicable`, `Passed`, `Failed`, `Why`;
health writes `Grade`, `Fix`, `Counted`. A new check writes its own keys and
declares columns over them. Nothing is added to `Finding`.

What each check declares:

| | Clash | Clearance | Fall hazard | Model audit | IDS |
|---|---|---|---|---|---|
| Runs | host clash tests | saved runs | saved runs | one per rule | one per specification |
| Elements | 2 | 2 | 1..n from the owner grid | 1 | 1 per location |
| Columns | Item 1, Item 2, Worst | Surface, Over, Clear, Short by | Opening area, Widest gap, Edge length | Grade, Fix | Applicable, Passed, Failed, Why |
| Severity | `-distance / tolerance` | `shortfall / minimum` | `gap / limit` | grade band | `failed / applicable` |
| WriteBack | host clash data | **null** | **null** | **null** | **null** |
| Runner | `IClashSource.Run` | re-run the spec | re-run the spec | re-run the rule | re-run the file |
| Naming | host + `CheckRunStore` | `CheckRunStore` | `CheckRunStore` | **null** | **null** |
| Geometry | `SelectResults` | element locator | element locator | element locator | element locator |

The two element columns the board hard-codes today (`Item 1`, `Item 2`, at nine
sites from column identity through search blob to spreadsheet header) become
`ColumnKind.Element` with an `ElementIndex`. `Finding.Elements` is already a list.

### 1.7 The remaining capabilities

```csharp
/// Writing a decision back into the model. Only clash has anywhere to put one.
public interface ICheckWriteBack
{
    bool CanEdit { get; }
    string? LastProblem { get; }

    bool SetStatus(IReadOnlyList<Finding> findings, FindingStatus status);
    bool Assign(IReadOnlyList<Finding> findings, string who);
    bool Comment(IReadOnlyList<Finding> findings, string body, string author);
    bool Regroup(IReadOnlyList<Finding> findings, string? groupName);
    ClashNesting Regroup(IReadOnlyList<ClashGroupPath> paths);
    bool Rename(Finding finding, string name);
    bool RenamePicked(IReadOnlyList<Finding> findings, string prefix);

    IReadOnlyList<FindingComment> CommentsOn(Finding finding);
    bool EditComment(Finding finding, long commentId, string body);
    bool DeleteComment(Finding finding, long commentId);
}

/// Running a run again from this tab.
public interface ICheckRunner
{
    bool CanRun { get; }
    string? CannotRun { get; }
    bool Run(string runId, Func<int, bool>? onProgress);
    bool Delete(IReadOnlyList<string> runIds);
    bool Compact(IReadOnlyList<string> runIds);
}

/// Renaming and refiling runs.
public interface ICheckNaming
{
    bool Rename(string runId, string name);
    bool Refile(string runId, string? folder);

    /// The naming window, or null. Only the clash matrix has one.
    Action<object>? Book { get; }
}

/// Focus, Isolate, viewpoints and snapshots.
public interface ICheckGeometry
{
    /// Select what the producer already holds. -1 when it holds nothing and the
    /// board must fall back to Locate.
    int Select(IReadOnlyList<Finding> findings, bool add);

    IReadOnlyList<ElementLocator> Locate(IReadOnlyList<Finding> findings);
    bool ApplyView(Finding finding);
    byte[]? Snapshot(Finding finding, int width, int height);
}

/// Element-property columns the user adds.
public interface ICheckProperties
{
    IReadOnlyList<PropertyValue> ValuesFor(Finding finding, int elementIndex,
                                           string category, string name);
}
```

Note the shape of `ICheckWriteBack`: it takes `Finding`s, not host ids. The clash
implementation reads its own `Facts["Host id"]` and `Facts["Host test"]`. The host
handle is private to the check, which is what "per-check data rides Facts" means in
practice.

### 1.8 Run comparison

The board's "what changed since last run" line reads `ClashDelta`, which is keyed
on `ClashKey` — a two-element pair and a position cell. There is no clearance key,
no IDS key, no fall-hazard key, and there should not be one.

Generalise the question instead. `FindingRegister.Observe` already returns
`FindingIntake` with Added, Updated and Absent, and the board already prints it.
Regression needs a frozen reference, which the register cannot be:

```csharp
/// What a run's findings stood at when it last ran. A frozen reference, written
/// after every read, so a delta does not shift under a colleague saving mid-read.
public sealed class RunJournal
{
    public const string Section = "checks";
    public const string Key = "journal";

    public string CheckKey { get; }
    public string RunId { get; }
    public long WhenTicks { get; }

    /// Finding id to the status it held at that moment.
    public IReadOnlyDictionary<string, FindingStatus> Statuses { get; }

    /// Open findings per assignee at that moment, kept rather than re-derived,
    /// because a past run's parties cannot be reconstructed from live state.
    public IReadOnlyDictionary<string, int> OpenByParty { get; }

    public JsonValue ToJson();
    public static RunJournal? FromJson(JsonValue? json, IList<string>? notes = null);
}

public sealed class CheckDelta
{
    public IReadOnlyList<string> New { get; }
    public IReadOnlyList<string> Persisting { get; }
    public IReadOnlyList<string> Regressed { get; }
    public IReadOnlyList<string> Gone { get; }

    public static CheckDelta Between(RunJournal? before, FindingRegister now,
                                     Func<Finding, bool> scope);
}
```

`Regressed` means "was Approved or Resolved in the journal and is open now". For a
check whose status only ever comes from a person it is always empty, and the
sentence omits the word rather than printing a nought.

---

## 2. Findings and issues

### 2.1 The decision: one finding, many locations

The owner's example is an IDS specification with 1000 findings raised as one issue.
Two readings: 1000 register entries under one issue, or one register entry with
1000 locations.

**One register entry with 1000 locations.** Reasons:

- A finding is a unit of decision. A specification is decided once, not 1000
  times. 1000 register entries would be 1000 statuses no one sets, 1000 rows on the
  board and 1000 BCF topics in every pack.
- The record already works this way and the reasoning is already written down:
  `AddChild` refuses a child with triage state of its own, refuses nesting beyond
  one level, and a parent's id ignores its children so 400 becomes 401 next week
  without losing the sign-off.
- Partial progress is recorded by the re-run, not by per-element triage. When the
  specification fails 600 instead of 1000, `ReplaceLocations` records that; when it
  fails none, the finding goes absent. That is honest and it needs no new state.
- Clash stays one finding per result, because a clash **is** the unit of decision.
  The register therefore holds a mixture of leaves and parents, and the board draws
  register entries as rows either way.

**So the vocabulary changes to match.** "Child finding" is retired. The type is
`FindingLocation`, and it is a distinct type rather than a `Finding` with its state
suppressed, so "never separately triaged" is structural instead of a runtime
refusal in `AddChild` and a field-clearing pass in `FindingJson`:

```csharp
/// One place a finding is. Carries no decision: the finding it belongs to holds
/// the decision, and this is where the coordinator is sent to look.
public sealed class FindingLocation
{
    public FindingLocation(string id, IReadOnlyList<ElementKey> elements,
                           string? detail = null,
                           double? x = null, double? y = null, double? z = null);

    public string Id { get; }
    public IReadOnlyList<ElementKey> Elements { get; }
    public string? Detail { get; }
    public double? X { get; }
    public double? Y { get; }
    public double? Z { get; }
    public IDictionary<string, string?> Facts { get; }
}
```

On `Finding`, replacing `Children` / `AddChild` / `ReplaceChildren` / `IsParent`:

```csharp
public IReadOnlyList<FindingLocation> Locations { get; }
public bool HasLocations { get; }
public Finding AddLocation(FindingLocation location);
public void ReplaceLocations(IEnumerable<FindingLocation>? locations);
public IReadOnlyList<ElementKey> AllElements();   // unchanged
```

### 2.2 The addressability gap, answered

The audit is right that `FindingRegister` indexes only top-level findings, so
`register.Find(childId)` always returns null. Under this decision that is not a
promotion problem — a location is not a register entry and must never be one — but
a lookup is still needed for BCF viewpoints and for the detail panel's location
list. So:

```csharp
/// Find the location with this id, and the finding that owns it. The index is
/// built lazily and dropped on Put, Remove and Clear.
public Finding? OwnerOfLocation(string? locationId);
public FindingLocation? FindLocation(string? locationId);
public int LocationCount { get; }
```

Locations are **not** counted in `Count` or `Open`, are **not** attachable to an
issue, and are **not** written as their own BCF topics. `LocationCount` exists so a
screen can say "37 rules, 1,204 locations" instead of implying 37 problems.

### 2.3 The issue

An issue is a finding-set somebody raised. It gains nothing a finding does not
have, and it loses the four things it duplicates.

```csharp
public sealed class Issue
{
    public string Id { get; }                       // CW-<n>, unchanged
    public string Title { get; set; }

    /// On the lattice, like a finding. The six-word free-text list is retired.
    public FindingStatus Status { get; set; }

    public string? AssignedTo { get; set; }
    public string? Priority { get; set; }           // Priorities.All, shared with Finding
    public int? DueDayNumber { get; set; }

    /// Derived finding ids. Any source, any mixture.
    public IList<string> Findings { get; }

    /// One comment type. IssueComment is deleted.
    public IList<FindingComment> Comments { get; }

    /// Open bag. Level and Zone move in here; nothing coordination-specific is a
    /// field any more, so a check can carry its own facts onto an issue.
    public IDictionary<string, string?> Facts { get; }

    public IList<string> Labels { get; }
    public string? BcfGuid { get; set; }

    /// Which checks this issue's findings came from, derived, not stored.
    /// IssueOrigin is deleted: it shadowed FindingSource, disagreed with it on two
    /// members and never gained SetDrift.
    public IReadOnlyList<string> Sources(FindingRegister register);

    /// True when nobody attached a finding: an issue somebody wrote by hand.
    public bool IsManual => Findings.Count == 0;
}
```

`ManualIssue` collapses into this. It is a board item with a `ClashKey`, neither a
`Finding` nor an `Issue`; under the stated model it is an issue with no findings,
and the `clash/issues` key is read once, migrated into `IssueRegister`, and left on
disk so an older build still finds it.

Status migration, applied once on read and recorded in the notes:
`Draft → New`, `Open → Active`, `Pending → Active`, `In dispute → Active`,
`Answered → Reviewed`, `Closed → Resolved`. Pending and In dispute also become
labels, so the distinction is kept where it belongs — on the conversation, not on
a claim about the building.

`IssueRegister` gains a version field. It is a bare JSON array today with no
forward-compatibility signal, which every other persisted type in Core has.

### 2.4 The cascade, precisely

```csharp
/// What marking an issue would do to the findings attached to it, worked out
/// before anything is written.
public sealed class IssueCascade
{
    public static IssueCascade Plan(Issue issue, FindingStatus to,
                                    FindingRegister register);

    /// Findings the merge moves up.
    public IReadOnlyList<Finding> Promoted { get; }

    /// Findings already at or above the target. Not written.
    public IReadOnlyList<Finding> Unchanged { get; }

    /// Findings above the target. Reported, never demoted.
    public IReadOnlyList<Finding> Higher { get; }

    /// Attached ids the register no longer holds. Reported by id.
    public IReadOnlyList<string> Missing { get; }

    public int Apply();
    public string Describe();
}
```

The rules:

1. **The issue's own status is a direct edit.** A person deciding about an issue
   may demote it; the screen confirms when they do. `IsDemotion` decides whether to
   confirm.
2. **Every attached finding takes `FindingStatusLattice.Merge(f.Status, to)`.**
   Never a direct assignment. This is the fix for the live bug where marking an
   issue Answered pulled an already Approved finding back to Reviewed:
   `Cascade.Apply` assigns, and `Proposed` filters only findings already *at* the
   target rather than findings above it.
3. **Every status cascades, not two of six.** With the issue on the lattice there
   is no mapping table left to be incomplete.
4. **Locations follow their finding** and are never written.
5. **It is applied, not offered.** The offer exists because the mapping was partial
   and could demote; both reasons are gone. The plan is shown first — "1,000
   findings move to Resolved, 12 are already Resolved, 3 are no longer in the
   register" — and Apply is one write with one activity entry.
6. **One finding on two issues takes the higher of the two cascades.** The lattice
   does that without a rule.
7. **The reverse cascade is offered, not applied.** When every attached finding is
   Approved, Resolved or absent, the issue shows as ready to close. Closing is an
   act between parties and a machine should not do it.

### 2.5 One save door

`session.Findings.Save()` and `session.Store.Save()` are separate calls with
separate failure paths, and several sites make one without the other — an attach
that saves the project file but not the sidecar leaves a dangling finding id.

```csharp
/// Save the findings sidecar and the project file together. Returns false with
/// the reason when either refuses; neither is left written without the other.
public bool SaveWork(out string? problem);
```

The `issues` section stays in the project file. Issue volumes are small, the
write-amplification argument that moved findings out does not reach them, and a
migration would buy nothing.

### 2.6 Source becomes a string

`FindingSource` is an enum, hashed into identity **by name** and written to JSON by
name. Adding a check therefore means editing an enum in Core — small, but it is the
edit the owner asked to be rid of.

Retire the enum and keep the name as a table of constants, so every call site reads
the same and only signatures change:

```csharp
/// Which check produced a finding. A string, so the list of checks is open.
/// The values are the old enum member names, so no finding is re-identified.
public static class FindingSource
{
    public const string Clash = "Clash";
    public const string Manual = "Manual";
    public const string Health = "Health";
    public const string Headroom = "Headroom";
    public const string Ids = "Ids";
    public const string SetDrift = "SetDrift";
    public const string Compare = "Compare";
    public const string FallHazard = "FallHazard";

    /// The ones this build knows. An unknown key is drawn by its key, not refused.
    public static IReadOnlyList<string> Known { get; }
}
```

`Finding.Source` becomes `string`; `Finding.Create` already hashes
`source.ToString()`, so every existing finding id is unchanged and
`FindingJson.Version` does not move. `FindingRegister.From(string)`,
`Observe(string, ...)`. `ICheck.Key` is the same string, which is what ties a check
to its slice of the register.

### 2.7 What stays on the clash item spine, and why

`CLASH_PLAN.md` 6.1 says `Finding` is the record and `ClashItem` a derived view.
That remains right, and three things have moved in its favour since: clash finding
identity now survives a test rename where `ClashKey` does not; the clash BCF topic
guid is already stamped onto the finding; and the board already reads triage state
from the finding rather than the item.

Item by item, what happens to the five jobs the item spine still owns:

| Job | Where it ends up |
|---|---|
| Carry-over reference (`ClashSnapshot`) | **Replaced** by `RunJournal`, which is per check and keyed on finding id. `ClashSnapshot` keeps being written until the five tabs that read it move; both exist for one phase and the plan says which read is authoritative. |
| Previous-run reference (`ClashKeys.Previous`) | **Replaced** by the previous `RunJournal`. Same shape, one undo slot. |
| Per-party open counts at run time (`OpenByParty`) | **Moves into `RunJournal`.** It cannot be re-derived from live state, so it is kept, exactly as the snapshot keeps it now. |
| Group decisions (`ClashSnapshot.Decisions`), pinning (`ClashKeys.Pinned`) | **Stays on the item spine, clash only.** A pinned group is a decision about the *host document's* clash groups; no other check has host groups to pin. The board's own banding and chained grouping are generic and work on every check today. |
| Per-party assignment log (`ClashKeys.Assignments`) | **Stays**, for the same reason. |

So the honest statement is: `Finding` becomes the single spine for **state** —
status, assignee, group name, comments, priority, due date, stage, topic guid — and
for **identity**. It does not become the spine for the host document's clash group
structure, and `ClashItem` survives as the derived view the rules pipeline and the
five Coordinate tabs consume. `CoordinateViews.Build` stops re-reading the host and
projects `ClashItem`s from the register instead; that is what makes the item spine
derived rather than parallel.

---

## 3. The two ports

### 3.1 The geometry seam

Neither port can be written against `IModelItem`. Its whole geometric vocabulary is
one world-space axis-aligned box per element, and both algorithms need triangles.
Triangles exist in the product already, in `CamelWorks.Nav/NavGeometry.cs`, on the
wrong side of the seam: it is in the Nav assembly, returns a `GltfMesh` (an export
type), and is constructed from a concrete `NavDocument`.

New in `CamelWorks.Core/Abstractions`:

```csharp
/// World-space triangles in metres. Positions are 3 doubles per vertex, indices
/// 3 per triangle. Neutral: not the report writer's mesh type.
public sealed class TriangleMesh
{
    public TriangleMesh(IReadOnlyList<double> positions, IReadOnlyList<int> indices);

    public IReadOnlyList<double> Positions { get; }
    public IReadOnlyList<int> Indices { get; }
    public int Vertices { get; }
    public int Triangles { get; }
    public BoundingBox Bounds { get; }

    /// Merge vertices whose positions are within tolerance metres, rewriting the
    /// indices. Mandatory before any connectivity work; see 3.2.
    public TriangleMesh Weld(double toleranceMetres = 0.0001);
}

/// Reading triangles for an element. Availability is declared, like IClashSource:
/// the COM primitive reader can refuse a single element and a whole seat.
public interface ITriangleSource
{
    bool CanReadTriangles { get; }
    string? Unavailable { get; }

    /// One element. False when the host refused it; the caller degrades to Bounds.
    bool TryRead(ElementKey key, out TriangleMesh? mesh);

    /// A scope, streamed, so a federation sweep reports progress and cancels.
    IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> Read(TraversalScope scope,
                                                            Func<int, bool>? onProgress);
}
```

Nav side: `NavTriangles : ITriangleSource`, a reshape of `NavGeometry.Read` with a
key-based overload, returning `TriangleMesh` rather than `GltfMesh`. It keeps
`nwEVertexProperty.eNONE` and the existing metre scaling, and it welds before
returning. Per-element COM failures are swallowed and counted, not thrown, and the
count is reported.

Fake side: `FakeDocument` implements `ITriangleSource`, with

```csharp
public void SetMesh(ElementKey key, IReadOnlyList<double> positions,
                    IReadOnlyList<int> indices);
public void SetSlab(ElementKey key, double x, double y, double z,
                    double width, double depth, double thickness);
public void SetBox(ElementKey key, BoundingBox box);
public bool CanReadTriangles { get; set; }     // settable, so the degrade path is tested
```

That is the whole answer to "testable on Linux". Every walkable-surface and
fall-hazard assertion below runs against synthetic meshes with no host, which is
how the source projects' own tests are written and why they port directly.

### 3.2 The weld is mandatory

`GenerateSimplePrimitives` emits vertices per triangle. The existing `TriangleSink`
appends a new position for every vertex of every triangle with no welding. The
walkable-surface flood fill clusters by **shared vertex index**. Fed an unwelded
mesh, no two triangles ever share an index, the fill produces one cluster per
triangle, every cluster falls under the minimum patch area, and the check reports
zero walkable surfaces on a model full of floors.

So the weld is part of the seam contract, not an optimisation: `NavTriangles`
welds, `FakeDocument` can be asked for unwelded input, and a seam contract test
asserts that a two-triangle quad supplied unwelded comes back with four vertices
and both triangles sharing two of them.

### 3.3 The walkable-surface detector

Ported from the BIMCamel clearance engine. `CamelWorks.Core` is `netstandard2.0`
with a test asserting it references only the framework, so the source's geometry
library cannot come with it: about 100 lines of `Vec3` and box helpers replace it.

To Core, in `CamelWorks.Core/Data/`:

```csharp
public sealed class WalkableOptions
{
    public double ClearHeightMetres { get; set; } = 2.100;
    public double MaxSlopeDegrees { get; set; } = 10.0;
    public double MinPatchAreaSquareMetres { get; set; } = 0.05;
    public double ElevationBucketMetres { get; set; } = 0.050;

    /// Bias detection towards elements whose category or type names a floor.
    /// Off by default; see 3.5 for why it is offered rather than relied on.
    public bool PreferFloorCategories { get; set; }
}

public sealed class WalkablePatch
{
    public string Id { get; }                    // derived, stable across runs
    public ElementKey Source { get; }
    public string? Level { get; }
    public BoundingBox Footprint { get; }
    public double TopMetres { get; }
    public double AreaSquareMetres { get; }
    public TriangleMesh Mesh { get; }            // the patch's own triangles
}

public static class WalkableSurfaces
{
    public static IReadOnlyList<WalkablePatch> Detect(
        IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> meshes,
        WalkableOptions options,
        WalkableDiagnostics? diagnostics = null);
}

/// Which world axis is up, inferred from where the horizontal area is. Federations
/// mix Z-up and Y-up sources, so this pays for itself beyond clearance.
public static class UpAxis
{
    public static int Detect(IEnumerable<TriangleMesh> meshes, double maxSlopeDegrees,
                             out double[] areasByAxis);
}
```

Taken: the up-axis heuristic, as a standalone Core helper rather than buried in the
detector; the signed up-facing normal filter, which is what stops a slab underside
becoming a patch; the elevation bucket plus shared-vertex flood fill as the
patch-forming primitive; the stacked-patch dedupe, which removes the structural
slab plus finish pair for about 50 lines; the derived patch id with a content
discriminator on collision.

Fixed in the port, not carried:

- **The two origin-growth bugs are not carried.** The source builds two of its boxes
  with a default-constructed box grown by `Contain`, which starts at the world
  origin, so on any site-coordinate model the box reaches back to the origin and the
  containment fallback reports elements nowhere near the patch. Every box here is
  built from explicit min and max seeded from the first vertex. A Core test asserts
  a patch at (400000, 300000, 12) has a footprint at (400000, 300000, 12).
- **The clearance volume is extruded from the patch triangles, not its bounding
  rectangle.** The source's prism covers an L-shaped floor's missing corner, a
  stair void and the courtyard inside a ring corridor, and every column and railing
  inside that rectangle becomes a violation. The patch already carries its own mesh;
  the builder just ignored it.
- **The obstruction search reuses `Headroom`'s uniform plan grid** rather than the
  source's nested loop over volumes by targets. That loop is 10^8 to 10^9 box tests
  on a Navisworks federation. The triangle separating-axis test is taken as-is; the
  loop around it is replaced.
- **The double triangle pass goes.** `UpAxis.Detect` returns its areas, so the
  triangles are walked once.
- **The result carries a measurement.** The source reports "this duct is somewhere
  in the 2.1 m band above this patch" with no depth, no clear height and no point,
  which cannot be sorted or prioritised. The port reports the actual clear height at
  the worst point and that point's coordinates, because the board bands and tones on
  severity and there is otherwise nothing to band on.

Recorded and deferred, with the reason:

- **Hard elevation bin edges on the triangle centroid.** A flat floor whose
  centroids straddle a boundary splits into two patches; the dedupe pass does not
  fix it because the halves do not overlap. Wanted: seed-and-grow banding within
  plus or minus a bucket. Deferred because it changes patch identity and should not
  ride the same commit as the port.
- **Clustering per mesh.** Two adjacent slabs never merge into one patch. Correct
  enough for headroom, wrong for a walkable-area report; deferred until there is one.
- **Stairs.** The source produces one patch per tread and then reports the flight
  above as an encroachment on the treads below. Shipping that as a stair headroom
  check would be wrong, so stairs are **excluded from detection by default**, the
  exclusion is stated on the setup screen, and measuring headroom along a pitch line
  is deferred.
- **Gratings.** A perforated plate becomes many sub-threshold clusters and vanishes.
  Recorded; no fix attempted.
- **Minimum patch area is not a furniture discriminator.** At 0.05 m² a desk top, a
  worktop and a wide duct top all pass, whatever the source's own comment claims.
  `PreferFloorCategories` is offered as a bias because Navisworks does carry the
  category, and it is off by default so the geometric answer stays the default.

### 3.4 The fall-hazard rasteriser

Ported from the Dyncamelo fall-hazard nodes. The source's pure half is already
`netstandard2.0` with no host reference and states so itself, so it moves
essentially unchanged into `CamelWorks.Core/Data/`:

`Tri2`, `FloorGapGrid` (the source's result grid), `FloorOpening`, `EdgeClass`,
`EdgeLabel`, `FloorEdgeResult`, and `Analyze`, `AnalyzeEdges`, `Rasterize`,
`PointInTriangle`, `EnclosedOpens`, `ChamferClearance`, `ChamferFromSeeds`,
`ChamferThroughMask`, `LabelOpenings`, and the colour ramp and glyph helpers.

```csharp
public static class FloorGaps
{
    public static FloorGapGrid Analyze(double minX, double minY, double maxX, double maxY,
                                       double cellMetres,
                                       IReadOnlyList<Tri2> floor,
                                       IReadOnlyList<Tri2> plugs,
                                       IReadOnlyList<int> owners,
                                       double minGapMetres);

    public static FloorEdgeResult AnalyzeEdges(FloorGapGrid grid,
                                               IReadOnlyList<Tri2> handrails,
                                               double limitMetres,
                                               double toleranceMetres,
                                               double minPassageMetres = 0.0);
}
```

Its existing test file is the behavioural contract and ports as-is: cell-size
independent clearance, slot width against slot length, the corner cells of a
dangerous opening, and the short break under `minPassage`. Every one pins a real
bug that was fixed, and none needs a host.

Two things the port must add, because without them there are no findings:

- **`Rasterize` takes a per-triangle owner index and fills a parallel `int[]
  owner` grid.** The source loses element identity the moment triangles arrive as
  an undifferentiated list, so a finding could carry a coordinate and nothing else —
  no Focus, no Isolate, no issue attachment, no BCF viewpoint on an element. The
  owner grid is what names the slabs around an opening and the rails along an edge.
- **The per-opening and per-run records survive.** The source computes an opening's
  area, cell count, cell-snapped box and flag, and a dangerous run's cells,
  centroid, faced gap and length, and then discards all but three aggregate numbers.
  A dangerous run is the natural per-edge finding.

Also fixed now:

- **Metres throughout.** The source works in document units and prints the same
  opening's gap two different ways in two outputs. Core takes metres and `Measure`
  is the only unit authority, as everywhere else in this product.
- **Diagonal runs are measured as paths.** Edge length is a cell count times cell
  size today, so a 45-degree run under-reports by about √2 and a two-cell-thick
  stretch double-counts. `DangerousLength` is one of three numbers this check
  reports and `minPassage` is judged on the same quantity.
- **The 16-sweep cap reports.** `ChamferThroughMask` stops at 16 sweeps and leaves
  stale distances that read as safe, silently. It now iterates to convergence and,
  if it hits a raised cap, says so in the read's notes.
- **The colour ramp's documentation is made to match the code.** The source's
  comments describe ramping the gap width; the code ramps the clearance. The code is
  right; the words are rewritten.

Recorded and deferred:

- **The plan silhouette is the projection of every triangle of every spanning
  element.** A column, a riser or a stair whose box merely reaches within the band
  projects its full height as solid floor. This is the largest fidelity problem and
  it was a deliberate trade to stop equipment cap faces being read as open floor.
  Deferred: it needs a real Z band on triangles with an explicit cap-face rule, and
  that is a change to the algorithm rather than a port of it.
- **Bounding-box spanning selects elements far too coarsely.** One grouped "Floors"
  item spanning a building passes the band test and drags every floor onto one plan.
- **Cell-centre point sampling** loses anything thinner than a cell: a slab edge, a
  rail, a thin plate. At the default cell size that is a coin flip under 250 mm.
- **Openings are only found when fully enclosed**, by a four-connected fill from the
  grid border. A stair void at a slab edge, a mezzanine perimeter and a
  double-height overlook are classed as exterior and never analysed, and one
  rasterisation gap in the perimeter deletes an interior void from the results. The
  edge check partly covers this, which is why fall hazard ships both passes and the
  screen says which one found what.
- **Chamfer anisotropy** and the two hand-tuned constants it needs. An exact
  distance transform would remove both; the constants are pinned by tests and left.
- **Handrails are neither height- nor band-filtered**, and matching is proximity
  only: a rail 300 mm across the void protects the edge as well as one on it.
- **The cell cap** at six million cells, which at a 100 mm cell is a 245 m square.

---

## 4. Phases

Every phase is independently shippable. The local battery for all of them:

```bash
dotnet test CamelWorks.Core.Tests -c Release
for f in checks/validate-*.py; do python3 "$f"; done
python3 tools/icons.py --check
```

plus UiCheck, StudioCheck, NavCheck and InstCheck, and a render where UI changed.

One honesty note on renders: the Manage board in `shots/app-clash-manage` is drawn
by the Studio harness's own table, not by `ClashManageViews`. Until 6.9 lands, a
render does not prove a change to the product's board. Where a phase says "render",
it means the pages that are drawn by the real views; where the board itself changed,
the proof is the harness drawing the real view or a check on a licensed seat, and
the phase says which.

### Phase 1 — Findings and issues: one record layer

No check seam, no board work. Ships on its own.

- `docs/findings-and-issues.md` rewritten (section 5).
- `FindingLocation`; `Finding.Locations` / `AddLocation` / `ReplaceLocations` /
  `HasLocations`. Six adapters updated: clash is unaffected (leaves), health, IDS,
  set drift and compare move from children to locations.
- `FindingRegister.OwnerOfLocation`, `FindLocation`, `LocationCount`, with a lazy
  index dropped on `Put`, `Remove` and `Clear`.
- `FindingSource` becomes a table of constants; `Finding.Source` becomes `string`.
- `Issue.Status` on the lattice, with the six-word migration and the two labels.
  `IssueComment` and `IssueOrigin` deleted. `Issue.Facts` added; `Level` and `Zone`
  move into it. `IssueRegister` gains a version field.
- `IssueCascade` replaces `Cascade` and `IssueFindings.StatusFor`. Applied, not
  offered; merge, not assignment.
- `ManualIssue` migrated into `IssueRegister` on read; the old key left on disk.
- `SaveWork(out string?)`, and every attach, raise and cascade site moved onto it.
- Set drift gains raise-an-issue and attach-to-an-issue, which it has neither of.
- `Priorities.All` shared by `Finding` and `Issue`.

**Done when.** `FindingJson` round-trips a finding with 1,000 locations and the id
is unchanged from the same finding with 400. `register.Find(locationId)` returns
null and `register.FindLocation(locationId)` returns the location and its owner.
A cascade to Reviewed over a set containing an Approved finding leaves it Approved
and lists it under `Higher`. A cascade to Resolved over 1,000 findings writes 1,000
and reports 3 missing ids without throwing. Every one of the six word migrations
maps, and a re-read of a migrated project is a no-op. An attach that fails the
sidecar write leaves the project file unwritten and returns the reason. The
capability map names `FindingLocation`, `IssueCascade` and `Priorities`.

### Phase 2 — The check seam in Core, with no consumer

`CamelWorks.Core/Checks/`: `ICheck`, `CheckCatalogue`, `CheckRun`, `CheckRunSpec`,
`CheckRunStore`, `CheckRead`, `ResultColumn`, `ColumnKind`, `ICheckWriteBack`,
`ICheckRunner`, `ICheckNaming`, `ICheckGeometry`, `ICheckProperties`, `RunJournal`,
`CheckDelta`. Roughly 700 new lines and no existing line changed.

`CamelWorks.Core/Testing/FakeCheck.cs`, mirroring `FakeClashSource`, and
`CheckSeamContractTests` mirroring `ClashSeamContractTests`, with the same
assertions added to `SelfTest`.

**Done when.** `CheckSeamContractTests` asserts: a check with every capability null
still reads and its findings observe into the register; `CheckRead.Scope` restricted
to one run leaves the other runs' findings present after `Observe`; `CheckRunStore`
round-trips a spec with an unknown settings key verbatim; `CheckDelta.Between` a
journal and a register reports New, Persisting, Gone, and Regressed only for
findings the journal held closed. `validate-capability-map.py` and
`validate-engines-reached.py` pass, which for this phase means the map names the
new types and the catalogue has a call site — so the phase includes registering an
empty catalogue on the session.

### Phase 3 — One status type on the board

`CLASH_PLAN.md` 6.3 says 31 status literals across UI and Nav. The count is **84**.
This is a phase of its own because it blocks the board move and because a mechanical
change of that size must be separable from a structural one.

- `Row.Status`, `TestEntry`'s five-slot tally, `BoardRow.Rank`,
  `ClearanceViews.Statuses` and the two places clearance re-parses a status out of a
  string all become `FindingStatus`.
- `FindingStatusWords.Of(FindingStatus)` is the only place a status becomes text.
- `ClashStatusMap.Read` becomes the only reader of a status word from outside, at
  the host and file boundaries. `FindingBcf.Apply`'s bare `TryParse` is replaced by
  it, with the guess tier flagged so a caller that must not guess can refuse.

**Done when.** Grep for the five status words as string literals across
`CamelWorks.UI`, `CamelWorks.Nav` and `CamelWorks.Nav.Clash` returns only
`FindingStatusWords` and `ClashStatusMap`. One test reads the same pack through
`FindingBcf` and `ClashBcf` and asserts one status, including for a word only the
guess tier maps. The board renders identically.

### Phase 4 — The clash board on the seam, with no second check

The 2,800-line mechanical pass. `ClashCheck : ICheck` over `IClashSource`, about
400 lines; `FileCheck : ICheck` over `ClashFile.Read`, about 250, mostly lifted from
`ReadFile`. `IClashSource` itself does not change.

- `entry.Test.X` becomes `entry.Run.X`; `row.Info.X` becomes reads off the finding
  and its facts; `_session.Clash.Y(...)` becomes `_check.WriteBack?.Y(...)`.
- The rail helper block that composes `Kinded`, `Tolerance`, `Trades` and `Sides` is
  deleted; those are `CheckRun.Kind`, `Threshold`, `Trades` and `Between`.
- `_toleranceMm` and the whole `Deep`/`Past`/`Millimetres`/`Bad` block are replaced
  by `ICheck.Severity` and `SeverityBand`. This must land in one change: anything
  still reading `_toleranceMm` will silently tone another check's rows against a
  25 mm default.
- `PropColumn` grows into a board column with `Kind`, `Number`, `IsBad`,
  `RightAligned`, `IsDeclared` and `Unit`; declared columns take the natural order
  slots rather than being appended, and numeric columns sort numerically and offer
  ranges rather than 300 distinct strings.
- `Standing` moves from `HashSet<ClashKey>` to `CheckDelta` over `RunJournal`.
- `OnFile` branches become capability nulls.
- `CoordinateViews.Build` stops re-reading the host and projects `ClashItem`s from
  the register. `ClashSnapshot` is still written; the journal is authoritative.
- File-mode boards observe into the register, matching on topic guid, and mark
  nothing absent — a file is a partial view of a run, not a run.

No check picker. No second check. The board must behave identically afterwards, and
that is the whole point of doing it alone.

**Done when.** The clash board reads, filters, groups, sorts, assigns, comments,
raises, exports and syncs exactly as before on a licensed seat, and the harness's
own tests over `FakeCheck` and `FakeClashSource` assert the same reads. A test
asserts a board built from a file writes findings that a subsequent host read
merges rather than duplicates. `CheckDelta` over two journals reproduces the same
New / Persisting / Regressed counts the `ClashDelta` path produced for the same two
runs. `validate-board-virtualised.py`, `validate-table-picking.py` and
`validate-select-then-act-on-selection.py` pass unchanged.

### Phase 5 — The check picker, and clearance as tests and results

- A check picker above the rail search box; the statics become per-check keyed on
  `ICheck.Key`; `_current` becomes a `(checkKey, runId)` pair. Not one flat list of
  every run of every check: banding, filtering and the verbs bar are scoped to one
  run and the columns differ per check.
- `ClearanceCheck : ICheck` with `WriteBack`, `Naming` through `CheckRunStore`, a
  runner, geometry through the element locator, and four declared columns.
- Clearance runs become `CheckRunSpec`s: name, folder, minimum, surfaces, targets.
  Re-running is pressing Run on a saved run.
- `ClearanceFindings` gains the run's stable id as `identity` and the run's name as
  `Rule`. Without it, two clearance runs over different corridors at the same place
  collide on identity. The minimum stays out of identity, as now.
- `Keep` passes `CheckRead.Scope`.
- `ClearanceViews` reduces from 1,906 lines to the setup: the two side pickers, the
  minimum, the traversal, and the run list. Its `PropColumn`, `Bucket`, `Row`,
  banding, filtering, columns, marker export, BCF export and issue attach are
  deleted — about 1,200 lines — because they are the Manage board.
- `ClearanceViews.Check(Session)` becomes `Check(ISeat)` so the tab is drawable in
  the harness, which it has never been.

**Done when.** Two named clearance runs over overlapping geometry produce disjoint
findings and running one leaves the other's findings present. A clearance run's
results appear on the Manage tab under the picker with grouping, sorting, filtering,
assignment, comments, issues, spreadsheet and BCF, and the clash board is unchanged.
The harness draws the clearance setup page and `validate-shots.py` and
`validate-reachable-screens.py` pass. `ClearanceViews.cs` is under 750 lines.

### Phase 6 — One BCF writer, so exchange is per check

`CLASH_PLAN.md` 6.2. Exchange in and out is one of the four controls the owner asked
for on every check, and it cannot be delivered while there are three writers over
one format with two label namespaces and two status readers.

- `FindingBcf` is the base: it already has the guid-stability argument, the lattice
  merge, the `Ours` label discipline and `Together`, which writes findings and
  issues related both ways.
- One label vocabulary with one `Ours` predicate. The clash six move into it or into
  `Finding.Facts` with a declared fact-to-label mapping. Today a file written by
  `ClashBcf` and read by `FindingBcf` takes `Test:` and `Level:` as the reviewer's
  own tags, appends them to the finding's labels permanently and re-exports them
  next cycle.
- One generic sidecar: a `Finding.Facts` dump keyed by topic guid, so every check
  round-trips its own numbers. Today only `ClashBcf` writes a sidecar and only it
  reads one.
- `ClashBcfOptions`' viewpoint machinery survives intact — camera direction, element
  colouring, section box, the render callback. It is the bulk of `ClashBcf` and it
  generalises to "the finding's `AllElements()` and its point", with per-check
  defaults.
- `IssueBcf` folds in, now that the issue status is on the lattice.
- Before any of it: a test pinning the one-line convergence that already makes
  `ClashFindings` and `FindingBcf` agree on a clash topic guid. It is undocumented on
  both sides and the first edit to either would break it silently.

**Done when.** One writer. A pack written from clearance findings, read back, and
re-exported carries the same labels it left with and no more. The same returned file
yields one status through every path. The BCF schema validation in the workflow
passes on an archive carrying a clearance finding, its issue, the relation both ways
and a viewpoint.

### Phase 7 — Triangles at the seam, and walkable surfaces

- `TriangleMesh`, `ITriangleSource`, `NavTriangles`, and `FakeDocument`'s mesh
  answers. The weld, and the seam contract test that pins it.
- `Vec3` and the box helpers, about 100 lines.
- `UpAxis`, `WalkableOptions`, `WalkablePatch`, `WalkableSurfaces.Detect`, the
  stacked-patch dedupe, and the patch-triangle extrusion.
- `Headroom` keeps its plan grid and its worst-per-surface reduction and gains a
  patch-shaped input: the obstruction search runs over patch prisms rather than
  element boxes. Its 13 existing tests stay green.
- The clearance run's floor side gains "Detected walkable surfaces" beside the
  seven-word guess, the selection and the saved sets. The guess stays, labelled as
  now, for seats where the triangle source is unavailable.
- The measurement the source omits: actual clear height at the worst point, and that
  point, so the board can sort and tone.

**Done when.** Synthetic-mesh tests, ported from the source project's own, assert:
a slab produces one patch and its underside produces none; a patch at site
coordinates has a footprint at site coordinates, not one reaching the origin; an
L-shaped floor's prism does not cover the missing corner; a structural slab and its
finish 40 mm above it dedupe to one patch; an unwelded quad welds; a mesh below the
minimum area is dropped. `CanReadTriangles == false` degrades to the box behaviour
and the screen says which was used. `ArchitectureTests` still passes, which proves
no geometry library came with the port.

### Phase 8 — Fall hazard

- The rasteriser port into `CamelWorks.Core/Data/`, with the owner grid, the
  surviving per-opening and per-run records, metres, path-measured runs and the
  convergence report.
- A PNG encoder in Core beside the existing reader, or the raster returned as
  width, height and RGBA for the report writers to encode.
- `FallHazardFindings`: one finding per flagged opening and one per dangerous edge
  run, elements from the owner grid, facts for opening area, widest gap and edge
  length, point at the centroid.
- `FallCheck : ICheck` with `WriteBack` null, a runner, geometry, and three declared
  columns.
- A Fall Hazard setup tab in the Clash Manager: floors, handrails, obstructions,
  level, band, cell size, limit, handrail tolerance, minimum passage — saved as a
  `CheckRunSpec`. Results are on the Manage tab, like every other check.

**Done when.** The source project's own test file, ported, is green: cell-size
independent clearance, slot width against slot length, corner cells of a dangerous
opening, and the short break under minimum passage. An opening's finding names the
slabs around it and Focus selects them. A 45-degree dangerous run reports a length
within 2% of its true length. The harness draws the setup page. The Manage tab shows
fall-hazard findings with the same controls as clash and clearance, and adding the
check touched no engine and no board code.

---

## 5. Corrections to the existing documents

### `docs/findings-and-issues.md` — rewrite, do not patch

It is stale in ways that mislead, and it is the document somebody reads first.

- **Line 3 calls it a proposal**: "Status: proposal. No code has been written to
  this." Six producers emit findings, the register is persisted, BCF goes both ways
  and set drift is built. It is a description, not a proposal.
- **The producer table at lines 38-46 is wrong on five of its six rows.** It says
  only Compare uses `Finding`, that clearance, IDS, health and compare do not
  persist, and that nothing but clash is attachable to an issue. All six machine
  sources emit `Finding`, all persist through `FindingStore`, and attachment has been
  1:N over finding ids for some time.
- **"It is used by exactly one of the seven producers and is never persisted
  anywhere"** is false in both halves.
- **Line 329, "Step 2 is next"**, is wrong: steps 2, 3, 5, 6 and 7 are done.
- **Two items are numbered 6** (lines 287 and 327).
- **The identity table's `Headroom` row** says "none; the pair is unique per check",
  which stops being true the moment there is more than one clearance run. Phase 5
  changes the code; the row changes with it.
- The whole document must be rewritten around the decision in section 2: a finding
  is the unit of decision, a location is where it is, an issue is a finding-set
  somebody raised, and the cascade is a lattice merge applied rather than offered.

### `docs/CLASH_PLAN.md`

- **6.3 says "31 status literals across UI and Nav". The count is 84.** Correct the
  number in 6.3 and in the Not-yet-done entry.
- **6.2 says the two BCF writers use "different topic GUIDs for the same clash".**
  They agree today: `ClashFindings.From` stamps `ClashBcf.TopicGuidFor(...)` onto the
  finding and `FindingBcf.TopicGuidFor` prefers the stamped value. The convergence is
  one line deep and undocumented on both sides. The sentence should say so and the
  test that pins it is Phase 6's first item.
- **6.1's Not-yet-done entry** should name what stays on the item spine — host clash
  group structure, pinning and the assignment log — rather than implying the whole
  item spine goes.

### `docs/CAPABILITY_MAP.md`

Each phase adds its rows in the same commit, or `validate-capability-map.py` fails:
`FindingLocation`, `IssueCascade`, `Priorities` (Phase 1); the whole `Checks`
namespace and `RunJournal` (Phase 2); `CheckRunStore` surfaces (Phase 5);
`TriangleMesh`, `UpAxis`, `WalkableSurfaces` (Phase 7); `FloorGaps` and
`FallHazardFindings` (Phase 8).

---

## Not yet done

Deliberately out of scope, with the reason.

- **Host clash groups for other checks.** Pinning, `GroupDecision` and the
  chained regroup that writes groups into the model stay clash-only. No other check
  has host groups to write. The board's own banding and chained grouping are generic
  and work on every check from Phase 4.
- **Deleting `ClashSnapshot`.** It stops being authoritative in Phase 4 and keeps
  being written until the five Coordinate tabs that read it — Filter Clashes,
  Report, BCF, Merge, Penetrations — and the review window are re-pointed. Deleting
  it is a phase of its own and nothing here depends on it.
- **A stair headroom check.** Stairs are excluded from walkable-surface detection by
  default and the screen says so. Measuring headroom along a pitch line, with nosing
  and flight merging, is a different algorithm and shipping the tread-by-tread
  behaviour under the name would be wrong.
- **The fall-hazard plan silhouette.** The projection of every triangle of every
  spanning element is the source's largest fidelity problem, and fixing it needs a
  Z band on triangles with an explicit cap-face rule. Recorded in 3.4 with the rest
  of the deferred weaknesses; the port ships the behaviour the source's tests pin.
- **Openings that touch a slab edge.** Only fully enclosed voids are found. The edge
  pass covers part of the gap and the screen says which pass found what. An exact
  fix needs the perimeter treated as a boundary condition rather than as exterior.
- **An exact distance transform.** The chamfer's anisotropy is why the edge classifier
  carries two hand-tuned constants that shift with cell size. Both are pinned by
  tests. Replacing them is a correctness improvement with no user-visible feature.
- **Cross-check runs on one rail.** The picker holds one check at a time. A flat list
  of every run of every check would need a union of columns, most of them blank on
  most rows, and banding and filtering are scoped to one run.
- **Moving the `issues` section out of the project file.** The write-amplification
  argument that moved findings to a sidecar does not reach issue volumes, and the
  migration would buy nothing. The dangling-id bug it is confused with is fixed by
  the one save door in Phase 1.
- **Column sorting in `Ui.Table` for the setup tabs.** The Manage board sorts; the
  clearance and fall-hazard setup tabs are lists of runs, and sorting a list of six
  runs is not worth a change to the shared table.
- **6.7 and 6.9.** Splitting the two large files and having the harness draw the real
  views. Phase 4 removes the reason `ClashManageViews` reads host clash types, which
  is the precondition for both, but neither is attempted here. Until 6.9 lands, the
  Manage board's rendered page is the harness's own table and does not prove a change
  to the product's board.
