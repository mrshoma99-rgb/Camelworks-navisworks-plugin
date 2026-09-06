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

## Persisted formats are being set, not migrated

The product has not shipped. There are no project files in the field, so nothing
here is a migration and no build older than this one is supported. `IssueRegister`
may become a versioned object, `ProjectStore.SchemaVersion` may be bumped,
`FindingLocation` is simply the shape a location has, and `ManualIssue`,
`IssueOrigin` and `Issue.Clashes` are deleted rather than read once and left on
disk. Each phase below states the format it sets; none states a read path for a
format this plan replaces.

Two rules survive that decision, because neither is about older builds:

- **Identity must be stable between runs of the same build.** A finding id is
  derived from its source, rule, title, elements and discriminator on every load. A
  change that re-derives ids detaches every status, assignee, comment and issue
  attachment in a live project. Section 2.6 states the rules and the tests that
  pin them.
- **A read must not invent a record.** A record the reader cannot rebuild is
  counted unreadable and dropped; it never becomes a finding with an empty source.

---

## 1. The architecture

### 1.1 The shape in one paragraph

A **check** is a descriptor, not a screen. It names itself, lists its **runs**,
reads its runs into **findings**, declares the **columns** its findings fill, and
optionally offers a host echo, host grouping, a runner, a namer, a property reader
and a geometry seam. The Manage tab holds one check at a time and draws its
findings. Everything a check produces is a `Finding`; everything a check knows that
the record does not have a field for rides `Finding.Facts`. Adding a check touches
the board, the register and the existing engines nowhere.

### 1.2 The check descriptor

New namespace `CamelWorks.Core.Checks`.

```csharp
/// One kind of check. Registered once; the Manage tab lists them in a picker.
public interface ICheck
{
    /// Stable key, written to the project file and used as the finding source.
    /// Never renamed once findings exist: see 2.6.
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
    /// measure. Read through the check's own typed accessors, never by parsing a
    /// fact string: see 1.6.
    double? Severity(Finding finding);

    /// What the severity band is called, in this check's own words. The check
    /// owns the wording; the board bands on the number and formats nothing.
    string SeverityBand(double? severity);

    /// Ids this finding may also be recognised under when matching one run
    /// against the previous run. Empty for every check but clash, which offers
    /// the 26 neighbouring position cells. See 1.8.
    IEnumerable<string> Aliases(Finding finding);

    /// Every run this check can offer, without reading any results.
    IReadOnlyList<CheckRun> Runs();

    /// Read results. Null runIds means every run.
    CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress);

    ICheckHostEcho? HostEcho { get; }     // null = the results live nowhere but here
    ICheckGrouping? Grouping { get; }     // null = the host has no groups to nest
    ICheckRunner? Runner { get; }         // null = cannot be re-run from this tab
    ICheckNaming? Naming { get; }         // null = the run names are not ours to change
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

    /// When this run last ran, or 0 for a run saved and never run. The two are
    /// different empty states on the board: see 4.5.
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
    public const string Key = "runs";

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
to know what those words mean. `ClashTestNames` moves into this store as the
project-side name for a run the host will not let us rename, and the `clash`
section stops carrying it.

**The `checks` section holds keyed documents, never a whole-section write.**
`CheckRunStore` owns `checks/runs` and `RunJournal` owns `checks/journals`. Each
reads and writes its own key and leaves the rest of the section alone. A store
that wrote the section wholesale would erase the other on every save, and both
were specified that way in the first draft.

### 1.5 The read, and its scope

```csharp
/// One read of one or more runs. The board's Read().
public sealed class CheckRead
{
    public IReadOnlyList<CheckRun> Runs { get; }

    /// Every result, already a Finding. Parents carry locations; leaves do not.
    public IReadOnlyList<Finding> Findings { get; }

    /// The predicate FindingRegister.Observe is scoped by, so reading three of
    /// twelve runs does not mark the other nine absent. Never null.
    public Func<Finding, bool> Scope { get; }

    /// What the read could not do. Shown on the board, not swallowed: see 4.5.
    public IReadOnlyList<string> Notes { get; }
}
```

`Scope` is not optional and not a convenience. `ClearanceViews` calls `Observe`
with no scope today, which is correct only while there is exactly one unnamed
clearance run; the moment there are named runs it silently marks every other run's
findings absent. Making it a non-nullable member of the read is the guard.

**Run membership is a fact on the finding, not a map built during the read.**
The first draft put `string RunOf(Finding)` on `CheckRead`. That is mechanically
wrong: `FindingRegister.Observe` merges a fresh finding into the **held** one and
the board displays the held object — `ClearanceViews` re-points its rows at
`register.Find(id)` for exactly this reason — so a map keyed on the fresh objects
answers for objects nobody draws. It also has no answer for held findings from
runs this read did not cover, which the rail must still tally.

So the producer writes the run ids onto the finding:

```csharp
public static class CheckFacts
{
    /// Semicolon-separated run ids. A set, because one result can belong to more
    /// than one run: two clash tests reporting one physical conflict, a 4D
    /// sequencing conflict spanning two sequences.
    public const string Runs = "Runs";

    public static IReadOnlyList<string> RunsOf(Finding finding);
    public static void SetRuns(Finding finding, IEnumerable<string> runIds);
}
```

`Scope` and the rail's per-run tallies both derive from this one fact. A finding in
two runs is counted under both and drawn once.

### 1.6 Columns, and why per-check data does not grow the record

The board's row is a `Finding`. There is no `CheckResult` type. `Finding.Facts` is
an `IDictionary<string, string?>` round-tripped verbatim by `FindingJson`, and
every producer already uses it: clash writes `Host id`, `Host test`, `Distance`;
clearance writes `Obstruction`, `Clear height`, `Least allowed`, `Short by`; IDS
writes `Applicable`, `Passed`, `Failed`, `Why`; health writes `Grade`, `Fix`,
`Counted`.

**Facts are for display and export. The board never bands, sorts or filters on a
fact string.** Three reasons, all live today:

- `ClashFindings` writes `Facts["Distance"]` in **model units**; `ClearanceFindings`
  writes metres. `Finding.X/Y/Z` is model units for clash and metres for clearance,
  and the board compensates with `OnFile ? 1.0 : MetresPerUnit` at eight sites. One
  register slice will hold host-read and file-read findings for the same check, and
  a generic reader of a unitless string cannot see which is which. It would band a
  foot-unit model against a millimetre threshold and be silently wrong.
- Facts are written with `"0.###"`, so the string the board would band on is
  already quantised away from the value the check measured.
- Facts merge additively and never expire. A key a producer stops writing keeps
  drawing its last value forever.

Two fixes, both required:

1. **Fix the unit at the producer.** Every length fact and every `Finding.X/Y/Z` is
   **metres at full precision**, for every check, host-read or file-read.
   `Detail` is derived from the number by `Measure` at display time, not stored
   pre-formatted. This deletes the eight `OnFile ? 1.0 : MetresPerUnit` sites
   rather than generalising them.
2. **A typed accessor is the only thing the board reads.** A declared column
   carries functions, not a key to parse.

```csharp
public enum ColumnKind { Text, Number, Element }

/// A column a check declares. Rides the machinery PropColumn already uses.
public sealed class ResultColumn
{
    public ResultColumn(string key, string title, ColumnKind kind);

    /// The Finding.Facts key this column exports under. Display and spreadsheet
    /// only: the board reads Number or Text.
    public string Key { get; }
    public string Title { get; }
    public string? Unit { get; }
    public ColumnKind Kind { get; }

    public double Width { get; set; }
    public bool RightAligned { get; set; }

    /// For Kind == Element: which of Finding.Elements this column draws.
    public int ElementIndex { get; set; }

    /// The value, typed and in metres where it is a length. Sorting, range
    /// filtering and banding all read this. Null means the column is text.
    public Func<Finding, double?>? Number { get; set; }

    /// The value as words, where there is no number. Null means read Number.
    public Func<Finding, string?>? Text { get; set; }

    /// How the number is written for this column, given the project's units.
    public Func<double, string>? Format { get; set; }

    /// Which band a value falls in, for the range filter the board offers on a
    /// numeric column. Null means no ranges, only sort.
    public Func<double, string>? Bucket { get; set; }

    /// Worse than this run's threshold. Null means the column is never toned.
    public Func<Finding, bool>? IsBad { get; set; }
}
```

What each check declares:

| | Clash | Clearance | Fall hazard | Model audit | IDS | Compare | Set drift |
|---|---|---|---|---|---|---|---|
| Runs | host clash tests | saved runs | saved runs | one per rule | one per specification | one per revision pair | one per set |
| Elements | 2 | 2 | 1..n from the owner grid | 1 | 1 per location | 1 per location | 0 |
| Columns | Item 1, Item 2, Worst | Surface, Over, Clear, Short by | Opening area, Widest gap, Edge length | Grade, Fix | Applicable, Passed, Failed, Why | Added, Removed, Moved | In, Out, Drift |
| Severity | `-distance / tolerance` | `shortfall / minimum` | `gap / limit` | grade band | `failed / applicable` | **null** | **null** |
| HostEcho | host clash data | **null** | **null** | **null** | **null** | **null** | **null** |
| Grouping | host clash groups | **null** | **null** | **null** | **null** | **null** | **null** |
| Runner | `IClashSource.Run` | re-run the spec | re-run the spec | re-run the rule | re-run the file | re-run the pair | re-run the set |
| Naming | host + `CheckRunStore` | `CheckRunStore` | `CheckRunStore` | **null** | **null** | `CheckRunStore` | **null** |
| Geometry | `SelectResults` | element locator | element locator | element locator | element locator | element locator | **null** |

The two element columns the board hard-codes today (`Item 1`, `Item 2`, at nine
sites from column identity through search blob to spreadsheet header) become
`ColumnKind.Element` with an `ElementIndex`. `Finding.Elements` is already a list.

**Two checks the seam accommodates awkwardly, stated rather than hidden:**

- **Compare.** A compare run is a **pair** of documents and `CheckRun` has one id.
  The previous revision survives today only as an opaque settings string. The pair
  goes in `CheckRunSpec.Settings` under `base` and `against`, and `Between` is the
  sentence that names both. `Runner` re-runs the pair. This works, but the rail
  cannot offer "compare against a different revision" as a filter; that stays on
  the Compare tab.
- **Set drift.** Its finding carries **zero elements**, so `Geometry` is null,
  `Severity` is null and `ResultCount` counts sets rather than places in the model.
  It joins the seam for the register, the issue attach and the exchange, and it is
  the one check whose rows can never be focused. The board must draw a row with no
  geometry without offering Focus, which is a capability null it already handles.

**Two things the seam does not express, and what happens to them:**

- **A result belonging to more than one run.** A 4D sequencing conflict spans two
  sequences; the same shape exists today as one physical conflict reported by two
  clash tests. `DuplicateCollapse.Across` folds those at a 1 m band before anything
  counts. The seam carries the membership (1.5) but not the reduction: cross-run
  collapse stays inside `ClashCheck`, which runs `DuplicateCollapse.Across` before
  it returns its `CheckRead`. A check that wants it does the same in its own read.
  There is no generic cross-run reducer and this plan does not add one.
- **The suppression funnel.** `ClashRuleSet` produces `Suppressed` results that are
  recorded but never counted. That is a clash concept — a rule set over host clash
  results — and it stays inside `ClashCheck`: suppressed results are observed into
  the register with a fact saying which rule suppressed them, and the board's
  existing "suppressed" filter reads that fact. No other check offers it.

### 1.7 The remaining capabilities

```csharp
/// Echoing a decision into the host document. Only clash has anywhere to put one.
///
/// Every edit is written to the register first and unconditionally. The echo is
/// attempted where a check offers one; a refused echo marks the field pending on
/// the finding and the board says so. See 4.2.
public interface ICheckHostEcho
{
    bool CanEdit { get; }
    string? LastProblem { get; }

    bool SetStatus(IReadOnlyList<Finding> findings, FindingStatus status);
    bool Assign(IReadOnlyList<Finding> findings, string who);
    bool Comment(IReadOnlyList<Finding> findings, string body, string author);
    bool SetGroup(IReadOnlyList<Finding> findings, string? groupName);
    bool SetTitle(IReadOnlyList<Finding> findings, IReadOnlyList<string> titles);

    IReadOnlyList<FindingComment> CommentsOn(Finding finding);
    bool EditComment(Finding finding, long commentId, string body);
    bool DeleteComment(Finding finding, long commentId);
}

/// Nesting results into the host document's own group structure. Clash only:
/// six checks return null, and a capability naming clash types would have kept a
/// clash-shaped branch on the board forever.
public interface ICheckGrouping
{
    ClashNesting Regroup(IReadOnlyList<ClashGroupPath> paths);
    bool CanPin { get; }
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

    /// True when this check offers a naming window of its own. Only the clash
    /// matrix has one.
    bool HasBook { get; }
}

/// Focus, Isolate, viewpoints and snapshots.
public interface ICheckGeometry
{
    /// Select what the producer already holds, returning how many it selected.
    /// Null when it holds nothing and the board must fall back to Locate.
    int? Select(IReadOnlyList<Finding> findings, bool add);

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

Note the shape of `ICheckHostEcho`: it takes `Finding`s, not host ids. The clash
implementation reads its own `Facts["Host id"]` and `Facts["Host test"]`. The host
handle is private to the check, which is what "per-check data rides Facts" means in
practice.

`Rename` and `RenamePicked` are **not** on `ICheckHostEcho`. They are edits to the
record and belong on the register path; see 4.2.

`ICheckNaming.Book` was typed `Action<object>?` in the first draft, which is an
untyped hole with a cast at the far end. It cannot be typed properly either:
`CamelWorks.Core` is `netstandard2.0` with a test asserting it references only the
framework, so a Core interface cannot name a WPF element or `ISeat`. So the seam
carries the fact (`HasBook`) and the board looks the window up on the UI side,
keyed on `ICheck.Key`, in the one place that already knows about windows.

### 1.8 Run comparison

The board's "what changed since last run" line reads `ClashDelta`, keyed on
`ClashKey` — a two-element pair and a position cell. There is no clearance key, no
IDS key, no fall-hazard key, and there should not be one.

The first draft replaced `ClashDelta` with a journal of finding id to status and
computed both halves of the delta from it. That does not work, for two independent
reasons, and the fix is to separate the two jobs.

**Matching is per check, because the finding id already contains the position.**
`ClashFindings` passes the quantised clash cell as the discriminator and
`Finding.Create` hashes it, so a journal keyed on finding id has none of the 3×3×3
neighbour tolerance `ClashCarryOver` provides. A model that moved 40 mm would
report every clash as gone and every clash as new, and the
`MatchedOnNeighbourCell` drift diagnostic — the number that tells a coordinator the
model has shifted — would have no source. So `ICheck.Aliases` (1.2) lets clash
offer its 26 neighbour ids and every other check offer none, and the
neighbour-matched count stays first-class:

```csharp
public sealed class CheckDelta
{
    /// True when there is no journal at all, as against a journal that held
    /// nothing. The board prints "no previous run recorded" for the first and a
    /// count for the second, so a first run does not read as a wholesale change.
    public bool NoPreviousRun { get; }

    public IReadOnlyList<string> New { get; }
    public IReadOnlyList<string> Persisting { get; }
    public IReadOnlyList<string> Regressed { get; }
    public IReadOnlyList<string> Gone { get; }

    /// How many matched only through an alias rather than exactly. A large one
    /// means the model has moved; it is a diagnostic, not a count of results.
    public int MatchedOnAlias { get; }

    public static CheckDelta Between(RunJournal? before, FindingIntake intake,
                                     FindingRegister now, Func<Finding, bool> scope,
                                     Func<Finding, IEnumerable<string>> aliases);
}
```

**Regression is computed at intake, not from the journal.** The status lattice is
promote-only and `FindingRegister.Observe` says so in its own comment: `MergeFrom`
promotes and never regresses, so a re-run cannot undo somebody having approved a
clash. "Was closed in the journal, is open now" is therefore false for every
machine-produced finding forever, and `Regressed` would print a nought on every
check. Regression is visible today only because carry-over demotes on the **item**,
a write the register refuses.

`Observe` already holds the answer: at the moment it merges, it knows it is holding
a finding whose status is Approved or Resolved and that this run reported present.
So `FindingIntake` gains it and `CheckDelta` reads it:

```csharp
public sealed class FindingIntake
{
    public int Added { get; }
    public int Updated { get; }
    public int Absent { get; }

    /// Findings held closed that this run reported present again. The register
    /// does not demote them; this is the report, and the board's Regressed line.
    public IReadOnlyList<string> Reopened { get; }

    public IReadOnlyList<string> Notes { get; }
}
```

The journal then has one job left — a frozen reference for what was there and who
it was with, so a delta does not shift under a colleague saving mid-read:

```csharp
/// What a run's findings stood at when it last ran.
public sealed class RunJournal
{
    public const string Section = "checks";
    public const string Key = "journals";

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
```

**One journal per (check, run), and one of them kept.** `checks/journals` is an
object keyed `"<checkKey>/<runId>"`, each holding `current`. Retention is stated
because a journal is a full id list: twelve runs of eighteen thousand results is
the largest thing in the project file, and an unbounded history would grow it
every run. The `previous` slot an earlier build wrote is read by nothing and is
dropped on the next write; a caller that needs the outgoing journal reads
`Current` before it writes.

A run with no journal is a first run, and `NoPreviousRun` says so. No journal is
seeded from anything: there is no function from a `ClashKey` to a finding id, so
nothing can be reconstructed from a snapshot, and inventing a seed would report a
first run as either wholly new or wholly unchanged. Saying "no previous run
recorded" is the honest answer and it costs one sentence on the board.

---

## 2. Findings and issues

### 2.1 The decision: one finding, many locations

The owner's example is an IDS specification with 1000 findings raised as one issue.
Two readings: 1000 register entries under one issue, or one register entry with
1000 locations.

**One register entry with 1000 locations.** The load-bearing reason is identity:

- **A parent's id ignores its children.** `CreateParent` hashes the rule and the
  discriminator only, so a specification failing 400 elements this week and 401
  next week is the same finding and keeps its sign-off. 1000 register entries
  destroy that, because each one hashes its own element: one element moving,
  splitting or being renamed re-identifies that row and loses whatever was decided
  about it. That is the argument. The triage argument below follows from it but
  does not decide it on its own.
- A finding is a unit of decision. A specification is decided once, not 1000
  times. 1000 register entries would be 1000 statuses no one sets, 1000 rows on the
  board and 1000 BCF topics in every pack.
- Partial progress is recorded by the re-run, not by per-element triage. When the
  specification fails 600 instead of 1000, `ReplaceLocations` records that; when it
  fails none, the finding goes absent.
- Clash stays one finding per result, because a clash **is** the unit of decision.
  The register therefore holds a mixture of leaves and parents, and the board draws
  register entries as rows either way.

**What is given up, stated plainly.** A location can never be individually
dispositioned. There is no per-location status, assignee, due date or sign-off, and
there will not be one: a location carrying triage state is a register entry under
another name and brings the identity problem back. What a location may carry is a
**note** — free text somebody wrote against one place, with an author and a time —
which records "this one is a false positive, the wall is a void former" without
claiming the location was decided.

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

    /// Notes somebody wrote against this one place. Not a decision: see above.
    public IList<FindingComment> Notes { get; }
}
```

`FindingLocation` is the shape a location has. There is no `Children` key and no
read path for one.

On `Finding`, replacing `Children` / `AddChild` / `ReplaceChildren` / `IsParent`:

```csharp
public IReadOnlyList<FindingLocation> Locations { get; }
public bool HasLocations { get; }
public Finding AddLocation(FindingLocation location);
public void ReplaceLocations(IEnumerable<FindingLocation>? locations);
public IReadOnlyList<ElementKey> AllElements();   // unchanged
```

**The cap.** `IdsFindings.DefaultCap` is **500**, not 1,000: a specification
failing 1,000 elements produces 500 locations and a note saying so. The 1,000-
location example in this section is therefore reachable in `Core.Tests`, where the
cap is a parameter, and not through the app. Raise the cap to 1,000 in the same
phase, deliberately, so the number the plan reasons about is the number the
product produces.

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
have, and it loses what it duplicates.

```csharp
public sealed class Issue
{
    public string Id { get; }                       // CW-<n>, unchanged
    public string Title { get; set; }
    public string? Description { get; set; }

    /// On the lattice, like a finding. The six-word free-text list is retired.
    public FindingStatus Status { get; set; }

    /// Where this issue came from: "Hand", "Bcf", "Clash", "Clearance", and so
    /// on. Set once at creation and never derived. IssueBcf.FromTopic creates
    /// issues with no findings attached, so Findings.Count == 0 cannot tell an
    /// imported issue from a hand-typed one, and IssueViews.Where switches on
    /// exactly that distinction.
    public string Provenance { get; set; }

    public string? Type { get; set; }               // BCF round-trip field
    public string? Author { get; set; }             // BCF round-trip field
    public string? RootCause { get; set; }
    public string? AssignedTo { get; set; }
    public string? Priority { get; set; }           // see below
    public int? DueDayNumber { get; set; }
    public long CreatedTicks { get; set; }
    public long ModifiedTicks { get; set; }

    public bool HasPoint { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public IList<ElementKey> Elements { get; }

    /// Derived finding ids. Any source, any mixture.
    public IList<string> Findings { get; }

    /// One comment type. IssueComment is deleted.
    public IList<FindingComment> Comments { get; }

    /// Open bag. Level and Zone move in here; nothing coordination-specific is a
    /// field any more, so a check can carry its own facts onto an issue.
    public IDictionary<string, string?> Facts { get; }

    public IList<string> Labels { get; }
    public string? BcfGuid { get; set; }

    /// Which checks this issue's findings came from, derived from the findings,
    /// falling back to Provenance when none are attached.
    public IReadOnlyList<string> Sources(FindingRegister register);

    public bool IsOpen { get; }                     // reads the lattice
    public bool IsOverdue(int today);
    public FindingComment? LastComment { get; }
    public string Describe();
}
```

**What is deleted, and what happens to each:**

- `IssueOrigin` — deleted. It shadowed `FindingSource`, disagreed with it on two
  members and never gained SetDrift. `Provenance` replaces it as a plain string
  with no parallel enum to fall out of step.
- `IssueComment` — deleted; `FindingComment` is the one comment type.
- `Statuses`, `OpenStatus`, `ClosedStatus` — deleted. `IsOpen` reads
  `FindingStatusLattice` instead of comparing against the literal `"Closed"`.
- `Draft` — dropped as a state. It meant "typed but not raised", which is a screen
  concern, not a claim about the building. An issue exists once it is saved. The
  other five words map: `Open → Active`, `Pending → Active`, `In dispute → Active`,
  `Answered → Reviewed`, `Closed → Resolved`. Pending and In dispute also become
  labels, so the distinction is kept where it belongs — on the conversation.
- `Clashes` — deleted, and `ManualIssue` with it. See below.
- `Types` and `RootCauses` — kept as the vocabulary lists they are.
- `Say` — folded into `Describe`.

**`DueTicks` becomes `DueDayNumber`, which is a unit change, not a rename.** The
value is **whole days since `DateTime.MinValue`, UTC**, matching
`Finding.DueDayNumber`, which is already that. Reading a tick value as a day number
would put every date in year 1; there is nothing old to read, so the only
requirement is that nothing in the port copies a tick value across.
`IsOverdue` takes today's day number rather than now's ticks.

**`Priorities` — three lists, one wins.** There is `Issue.Priorities` (a hard-coded
four), `ProjectProfile.Priorities` (project-configurable, what `Finding.Priority`
draws from) and the proposed `Priorities.All`. **`ProjectProfile.Priorities` wins.**
`Priorities.All` is the four-word default the profile is seeded with, and
`Issue.Priorities` is deleted. An issue and a finding therefore draw from the same
project-configured list, which is the point: an issue over three findings must not
offer a different vocabulary from the findings it covers. A project that has
overridden the list keeps its override; issues raised before the override keep the
word they were given, and a word no longer in the list is drawn as-is and offered
in the dropdown until it is changed.

**`Issue.Clashes` is deleted, and what it did is stated.** It existed as a holding
pen: nothing can turn a `ClashKey` into a finding id on its own, so an issue raised
against a clash before the results had been read held the key until the next read
could swap it for an id. That is gone, and with it the ability to raise an issue
against a clash the register has not seen. **The rule is: an issue may only be
attached to findings that already exist.** The attach and raise verbs are offered
on the board, which is downstream of a read, and are unavailable before one. There
is no deferred-attachment path and no key-shaped placeholder on `Issue`.

`ManualIssue` is deleted outright, along with the `clash/issues` key. An issue
somebody typed by hand is an `Issue` with `Provenance = "Hand"` and no findings.
`ManualIssueList.Open()`, which feeds the board through `CoordinateViews`, reads
`IssueRegister` instead. `Instruction`, `Elements`, the point, `Author` and the
raised time are all fields on `Issue`, so nothing it recorded is lost;
`Instruction` becomes `Facts["instruction"]`, because a typed resolution is a
clash-pipeline concept and not a field every issue needs.

`IssueRegister` becomes a versioned object rather than a bare JSON array, with the
version and the issues as members, so it has the forward-compatibility signal every
other persisted type in Core has.

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
/// Save the findings sidecar and the project file together, in that order.
///
/// There is no cross-file transaction: these are two files and two AtomicFile
/// writes. What is guaranteed is that the project file is never written unless
/// the sidecar write succeeded, so a dangling finding id cannot be created by a
/// half-completed save. The reverse — a finding written with no project record —
/// is recoverable, because the register is self-describing.
public bool SaveWork(out string? problem);
```

The order is fixed and the refusals come first:

1. Refuse before writing anything if `Store.IsReadOnly`, if `!Store.MaySaveSettings`
   or if `Findings.IsMemoryOnly`. Return the reason.
2. Write the findings sidecar. On failure, return `FindingStore.LastSaveProblem`
   and leave the project file untouched.
3. Write the project file.

`SaveWork` lives in **Core**, over `ProjectStore` and `FindingStore` — both Core,
both fakeable through `IFileSystem` — so it is asserted in `Core.Tests`.
`Session.SaveWork` delegates to it and adds nothing. The first draft put it on
`Session`, which is in `CamelWorks.UI`, where no test project can reach it.

`checks/validate-one-save-door.py` lands in the same commit: it fails any call to
`Findings.Save()` or `Store.Save()` outside the two implementations.

The `issues` section stays in the project file. Issue volumes are small, the
write-amplification argument that moved findings out does not reach them, and a
move would buy nothing.

A `TooNew` refusal on `FindingStore` — matching `VersionedDocument`'s — is a
robustness item for later, not a requirement here: there is no newer build to
refuse. `FindingRegister.FromJson`'s comment claiming a newer file "was left alone
rather than guessed at" is false today and is deleted rather than made true.

### 2.6 Source becomes a string, and the identity rules

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
`source.ToString()`, so every existing finding id is unchanged.
`FindingRegister.From(string)`, `Observe(string, ...)`. `ICheck.Key` is the same
string, which is what ties a check to its slice of the register.

**Three identity rules, each pinned by a test.** These are what keep a sign-off
attached across runs of this build, and two of them are one line each in
`FindingJson.Read` that the first draft did not examine.

- **The source gate stays strict.** `Enum.TryParse<FindingSource>(..., false, ...)`
  returns null for a missing or unparseable source today, so the record is dropped
  and counted unreadable. Under `string` that gate disappears unless it is written
  back explicitly: keep `if (string.IsNullOrWhiteSpace(source)) return null;`, and
  read an unknown but non-empty source through so a finding written by a check this
  build does not have survives a round trip. A record with no source must never
  become a finding with an empty source — that finding's id would be derived from
  the empty string and would collide with every other such record.
- **The clash identity branch stays ordinal and case-sensitive.**
  `source == FindingSource.Clash ? ClashFindings.Identity(...) : null` is an enum
  comparison today and becomes a string comparison. Written with
  `OrdinalIgnoreCase`, or with any producer emitting `"clash"` rather than
  `"Clash"`, every clash finding in the project re-identifies at once and every
  status, assignee and issue attachment on them detaches. The comparison is
  `StringComparison.Ordinal` and a test asserts that `"clash"` does **not** take
  the clash branch.
- **A check key is never renamed.** `ICheck.Key` is simultaneously the finding
  source hashed into every id under it, the project-file key for that check's runs
  and the register slice. Registering `"clash"` instead of `"Clash"` is the same
  failure as above, arriving through the catalogue. A test pins the known key set
  against the persisted names, so a rename fails the build rather than the project.

### 2.7 What stays on the clash item spine, and why

`CLASH_PLAN.md` 6.1 says `Finding` is the record and `ClashItem` a derived view.
That remains right, and three things have moved in its favour since: clash finding
identity now survives a test rename where `ClashKey` does not; the clash BCF topic
guid is already stamped onto the finding; and the board already reads triage state
from the finding rather than the item.

Item by item, what happens to the six jobs the item spine still owns:

| Job | Where it ends up |
|---|---|
| Carry-over reference (`ClashSnapshot`) | **Replaced** by `RunJournal` for the delta, and by the register for everything else. `ClashSnapshot` keeps being written until the five tabs that read it move; both exist for one phase and the plan says which read is authoritative. |
| Previous-run reference (`ClashKeys.Previous`) | **Replaced** by the previous `RunJournal`. Same shape, one undo slot. |
| Per-party open counts at run time (`OpenByParty`) | **Moves into `RunJournal`.** It cannot be re-derived from live state, so it is kept, exactly as the snapshot keeps it now. |
| **Simulate-seat hydration** (`CoordinateViews.Hydrate`) | **Moves to the register**, not the journal. See below. |
| Group decisions (`ClashSnapshot.Decisions`), pinning (`ClashKeys.Pinned`) | **Stays on the item spine, clash only**, behind `ICheckGrouping`. A pinned group is a decision about the *host document's* clash groups; no other check has host groups to pin. The board's own banding and chained grouping are generic and work on every check. |
| Per-party assignment log (`ClashKeys.Assignments`) | **Stays**, for the same reason. |

**Hydration is the job the journal cannot do, and it is the one that matters most.**
`CoordinateViews.Build` calls `Hydrate` when `session.Clash.IsAvailable` is false —
a Simulate or Freedom seat, which is most of the seats on most jobs — and
`ClashHydration.From` rebuilds a whole board of `ClashItem`s out of the snapshot's
clash keys, with no clash engine present. A journal of id-to-status rebuilds
nothing. The findings register can, and holds strictly more: elements, point,
status, assignee, comments, group, priority, due date, stage and topic guid.

So **the register becomes the hydration source**, and that is in Phase 8's
done-when rather than left implicit. Two things the snapshot carries that the
register must carry too, or hydration loses them:

- **Suppressed results.** The snapshot records them deliberately — dropping them
  would make every suppressed result reappear as new on the next run. They are
  observed into the register with the suppressing rule as a fact (1.6).
- **`GroupWasPinned` and `GroupName`**, which is what restores `CarriedGroup` on a
  hydrated item. Both become facts on the finding.

**The `ClashItem` projection, costed.** "`CoordinateViews.Build` stops re-reading
the host and projects `ClashItem`s from the register" is the sentence that makes
the item spine derived rather than parallel, and it is not free. An item carries
more than the finding does. What comes from where:

| On `ClashItem` | Source after the projection |
|---|---|
| Key (`ClashKey`) | Rebuilt from the finding's elements and its point. **The point must be in model units**, which is why 1.6 fixes the unit at the producer: metres on the finding, scaled once by `MetresPerUnit` at the projection. |
| Status, assignee, comments, group, priority, stage | Off the finding. |
| Distance, name, host id, host test | Off `Finding.Facts`. |
| `Level` | **Re-derived** by the existing levels pass over the scaled Z. |
| `DisciplineA/B` | **Re-derived** from `ProjectProfile`, as now. |
| `ModelA/B` | Off facts; the display-name lookup stays as it is. |
| Host status and assignee properties | **Unavailable** without the host. On a Manage seat they come from the read; on a hydrated board they are absent and the board already draws that. |
| `CarriedGroup`, `GroupWasPinned` | Off facts (above). |

`DuplicateCollapse`, carry-over and the rules funnel then run over the projected
items unchanged. A test asserts the projection produces the same item set the host
read produces for the same run: build items from a `FakeClashSource` read, observe
them into a register, project them back, and compare key, level, disciplines,
models and coordinates item for item.

So the honest statement is: `Finding` becomes the single spine for **state** and
for **identity**. It does not become the spine for the host document's clash group
structure, and `ClashItem` survives as the derived view the rules pipeline and the
five Coordinate tabs consume.

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
count is surfaced in `CheckRead.Notes`.

`NavTriangles` has zero possible test coverage in this environment, so it is
capped: **at most 50 lines, no logic**. The weld, the scale, the dedupe and the
failure counting all live in Core where they are tested; `NavTriangles` calls the
COM reader and hands the result to Core. One licensed-seat smoke run is required
before it merges, and that is stated in its phase rather than implied.

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
library cannot come with it: about 100 lines of vector and box helpers replace it.

`checks/reserved-type-names.txt` holds 564 names and the obvious ones are all in
it. The helpers are named `Vec3`, `Tri2`, `Span1` and `BoxFit`, and none of
`Point`, `Point3D`, `Vector3D`, `Matrix`, `Rect`, `Size`, `Transform`, `Grid`,
`Label`, `Selection`, `Style`, `Progress`, `Rule`, `Comment`, `Search`,
`Transaction`, `Document`, `Model`, `View`, `Viewpoint` or `Units` is used.

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

/// What detection did and did not manage: elements refused, meshes dropped under
/// the minimum area, patches merged by the stacked dedupe, and the axis chosen.
public sealed class WalkableDiagnostics
{
    public int ElementsRead { get; set; }
    public int ElementsRefused { get; set; }
    public int PatchesUnderMinimumArea { get; set; }
    public int PatchesMerged { get; set; }
    public int UpAxis { get; set; }
    public IList<string> Notes { get; }
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
  exclusion is a visible toggle on the setup screen with the caveat attached, and
  measuring headroom along a pitch line is deferred.
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

### 3.5 What the clearance floor-side screen must say

Automatic floor finding is the first thing the owner asked for, and the honest
statement about it is in 3.3: at 0.05 m² a desk top, a worktop and a duct top all
pass the minimum-area filter.

**Detection refines a source; it does not replace one.** The owner's correction:
the user already says what to measure up from — the current selection, a search
set, a selection set — and they pick the floors there. Detection then finds the
up-facing patches *within that source*. A user who scopes to a floors set has no
desk-top problem, because no desk was ever in scope.

The first implementation got this wrong in two ways, and both are Phase 10's to
correct if they are not corrected sooner: `Detect` passed
`TraversalScope.WholeDocument` regardless of the source, and the detected option
sat in the "Measure up from" list as a peer of the sets, so choosing it discarded
the source rather than refining it. The shape is: source first, then a tick that
finds the walkable surfaces in it.

That makes the caveat proportional rather than constant. Over a named set it is
noise; over everything loaded it is the whole story. So:

- **The caveat is carried when the scope earns it.** Scoped to everything loaded
  or to the current selection, the option's own sentence reads "every up-facing
  surface over 0.05 m², which includes desk tops and duct tops". Scoped to a named
  set, it states what it did — the surfaces found in that set — without the
  warning, because the user chose what went in. Not a tooltip, not a footnote,
  and not a constant.
- **A count and a total area are reported before the run**, from
  `WalkableDiagnostics`: "412 surfaces, 8,940 m², 31 elements refused". A user who
  sees 412 surfaces on a two-storey office knows what has happened.
- **`PreferFloorCategories` is a visible tick with an honest name** — "Prefer
  elements whose category names a floor" — not a hidden default.
- **Patches can be dropped from the detected set** before the run: the list is
  shown, sorted by area, and rows can be excluded. Exclusions are saved on the
  `CheckRunSpec` so a re-run does not undo them.
- **Stairs excluded** is a visible toggle, off by default, with 3.3's reason in its
  sentence.

---

## 4. The screens

### 4.1 The tab set being aimed at

`CamelWorks.UI/Shell/Apps.cs` declares the tabs. The Clash Manager holds six today:
Manage, Clash matrix, Clearance Check, Filter Clashes, Report, Exchange. After the
board move, "Clearance Check" contains only set-up and its results are on Manage,
so the label lies.

The target set:

| Tab | After |
|---|---|
| Manage | Every check's results, under a picker. |
| Clash matrix | Unchanged. |
| **Checks** | One tab, replacing "Clearance Check", holding the set-up for every check that has saved runs: clearance, fall hazard, and whatever follows. Its own picker selects which check's runs are being set up. |
| Filter Clashes | Unchanged, clash only. |
| Report | Unchanged. |
| Exchange | BCF in and out, following the Manage tab's check (4.4). |

**Three set-up tabs would be wrong.** Clearance and fall hazard set-up are the same
shape — a run list, a side picker or two, a handful of numbers — and a tab per check
puts the tab strip back where the board was: one screen per check. One "Checks" tab
with a check picker matches what the Manage tab does with results.

Files the relevant phases edit, named so nobody discovers them late:
`CamelWorks.UI/Shell/Apps.cs` (the tab declarations),
`CamelWorks.UI/Views/Surfaces.cs` (the tab-key-to-view wiring),
`CamelWorks.Studio/Panels.cs` (the render harness's pages), and
`docs/apps/clash.md` (`CONTRIBUTING.md` requires an entry per changed tab).

### 4.2 Rename, renumber, and the host echo

The owner named renaming **and renumbering** results. The first draft put `Rename`
and `RenamePicked` on `ICheckWriteBack`, which its own capability table sets null
for clearance, fall hazard, IDS, model audit and set drift — so renaming would have
been unreachable on five of seven checks, and renumbering was not in the plan at
all.

`ClashManageViews.Renumber` already does it the right way and its comment says why:
it writes `Finding.Title` and treats the host write as secondary, because a
published NWD refuses every write and a board that only wrote the host would show
the new name and record the old one.

**The rule, for every edit on the board — status, assignee, comment, group,
priority, due date, title, number:**

1. The edit is written to the **register** first and unconditionally.
2. The host echo is attempted where the check offers one (`ICheckHostEcho`).
3. A refused echo marks the field pending on the finding (`FindingField.Title` and
   the rest, as now) and the board says the model was not updated.

So `Rename(Finding, string)` and `RenumberPicked(IReadOnlyList<Finding>, string
prefix)` are board verbs over the register, available on every check. `SetTitle` on
`ICheckHostEcho` is the echo, not the edit.

**Numbering restarts per run**, as the host does it. `Renumber` today groups by test
id and restarts the counter per group precisely because the counter running straight
through two tests disagreed with the host. That behaviour is kept and generalised to
`CheckFacts.Runs`.

**What a rename survives:**

| | Re-run | Re-import (BCF) | Run rename |
|---|---|---|---|
| Clash | Yes — the title is not in the id, and `Pending` stops the run rewording it | Yes — the returned pack's title is a proposal, merged not assigned | Yes — the id is keyed on the host test id, not its name |
| Clearance | Yes, provided the geometry did not move: the identity is the run id plus the surface pair | Yes | Yes — the run's stable id is in the identity, its name is not |
| Fall hazard | Same as clearance | Yes | Yes |
| IDS, model audit | Yes — the finding is per specification or rule, not per element | Yes | n/a, the names are not ours |
| Compare, set drift | Yes | Yes | Yes |

The one case where a rename is lost is a clearance or fall-hazard result whose
geometry moved far enough to change the finding's discriminator. That is the same
case where the status is lost, and it is reported by the delta as one gone and one
new rather than passing silently.

**Phase 6's mechanical rule is `Write`, not a find-and-replace.** The first draft
said `_session.Clash.Y(...)` becomes `_check.HostEcho?.Y(...)`. With a null
capability that expression is a no-op returning null, which is the opposite of the
intent: the edit would vanish instead of being recorded. What actually happens is
that the board's existing `Write(...)` helper — which already takes a host action, a
row action and a finding action — becomes the only path: it writes the finding
first, calls `HostEcho` when there is one, and marks the field pending when the echo
refuses. Every `_session.Clash.Y(...)` site moves into a `Write` call.

### 4.3 The check picker

A picker above the rail's search box, on the Manage tab.

- **What it is.** A dropdown button showing the current check's `Name`, opening a
  menu of every check in the catalogue. Not a tab strip and not a combo box: the
  list is short, it needs a disabled state with a reason, and `Parts.Entry.Act` is
  the pattern the board already uses for exactly this.
- **Unavailable checks are listed, greyed, with their reason.** `ICheck.Unavailable`
  exists to be shown. Hiding them makes "where did the clearance results go?" a
  support question on every Simulate seat.
- **Counts.** Each entry shows the check's open finding count from the register —
  available without a read, because the register is loaded.
- **What survives a switch.** Nothing that is check-specific: selection, filter
  text, the grouping chain, the property columns and the search box are all keyed on
  `ICheck.Key` and restored when the user comes back. Switching check does not clear
  them, and coming back to clash finds the board as it was left. The statics that
  hold them become per-check dictionaries and `_current` becomes a
  `(checkKey, runId)` pair.
- **The hand-off from set-up.** Pressing Run on the Checks tab takes the user to the
  Manage tab with that check picked and that run selected.
  `ClashExchangeViews.Preferred` — a static list of test ids the receiving screen
  consumes and clears — is the existing pattern for exactly this, and it generalises
  to `(checkKey, runIds)`.

### 4.4 Exchange

Exchange is not delivered by "one BCF writer". `CamelWorks.UI/Views/ClashExchangeViews.cs`
is 1,897 lines and it is the feature: the pack builder, the trade split, the import
reconciliation and the returned-status merge. Making it per check means:

- **Pack per trade, for a check with no trade sides.** Clash packs split on
  `DisciplineA/B`. Clearance has an obstruction and a surface, not two trades; fall
  hazard has one element set. For those, the split falls back to the **assignee**,
  and where nothing is assigned, to one pack. The tab says which split it used.
- **`Preferred` becomes `(checkKey, runIds)`**, as in 4.3.
- **The Exchange tab follows the Manage tab's check.** It does not gain a second
  picker: two pickers that can disagree is a user exporting the wrong check's
  findings. It shows which check is selected and links back to Manage to change it.
- **No viewpoint parity claim.** `ResultSnapshot` frames a clash the way the native
  clash window does, using the host's own result geometry. It does not exist for any
  other check, so every non-clash pack renders its viewpoints through the slow
  camera route: `AllElements()`, a bounding box, a camera backed off it. That is
  slower and the framing is worse. The tab reports the render time estimate before
  a large pack rather than appearing to hang.
- **Acceptance test for the returned status, after a re-run.** A returned status
  lands on a topic guid. For clash the guid is stamped on the finding and survives.
  For clearance the finding is **re-derived every run**, so if the geometry moved
  between the export and the return, the returned status lands on a finding that no
  longer exists and the trade's answer is silently lost. The test: export a
  clearance pack, move the obstruction, re-run, import the returned pack, and assert
  either that the status lands on the successor finding or that the import reports
  it as unmatched by name. Silently dropping it is the failure.

### 4.5 Empty states, and where notes are shown

There are four, not one, and the board must tell them apart:

| State | What it says |
|---|---|
| The check is unavailable | `ICheck.Unavailable`, verbatim: "there is no clash engine in this Navisworks edition". |
| Available, no runs saved | "No clearance runs yet." with the verb that makes one, linking to the Checks tab. |
| A run saved, never run | "Corridors, 2.1 m — saved, not yet run." with Run. `CheckRun.LastRunTicks == 0`. |
| Ran, found nothing | "Corridors, 2.1 m — no breaches, last run 14 March." A result, not an absence. |

The last two are opposite facts and the first draft collapsed them into one blank
pane. `ClearanceViews` gets this right today with `Bench.Ready(...)`, and the
clearance restructure deletes that code, so the behaviour has to be rebuilt on the
Manage board rather than lost.

**`CheckRead.Notes` is displayed.** The fall-hazard cell cap, the sweep-convergence
report, the refused-element counts from `NavTriangles` and the walkable-surface
diagnostics all write to it, and nothing shows it today. It appears as a line under
the rail, expandable, with a count: "3 notes from this read". A note that is never
shown is the same as a failure that is swallowed.

### 4.6 Clearance set-up

The current set-up is two side pickers and a minimum. That is thinner than a real
job needs: the first federation run with no scope returns every soffit in the
building. Added, each as a `CheckRunSpec` setting:

- **Scope: level and zone.** The run covers one or more levels or zones rather than
  the whole federation. Without it the first run is unusable and the second is a
  guess at a selection set.
- **An obstruction ignore list.** Categories or saved sets excluded from the
  obstruction side. Without it every sprinkler head, light fitting and smoke
  detector is a finding, and the user's first act is to delete 4,000 rows.
- **A tolerance band, distinct from the minimum.** The minimum is what the design
  requires; the tolerance is how far under it is worth reporting. Two numbers,
  because a 2.100 m corridor measured at 2.098 m is not a coordination issue and a
  2.100 m corridor at 1.850 m is.
- **Worst-per-patch or every breach**, as a setting rather than a hard-coded
  reduction. `Headroom`'s worst-per-surface reduction is right for a first pass and
  wrong when a coordinator wants every duct listed.
- **Stairs excluded**, visible and off by default (3.5).

The detected-surfaces option and its honesty requirements are in 3.5.

---

## 5. Phases

Every phase is independently shippable, and no phase's done-when depends on a later
one.

### 5.1 The battery

`CONTRIBUTING.md` gives three commands, and they are the whole local battery:

```bash
dotnet test CamelWorks.Core.Tests -c Release
for f in checks/validate-*.py; do python3 "$f"; done
python3 tools/icons.py --check
```

plus, where BCF or IDS output changes:

```bash
schemas/bcf/validate.sh        # needs libxml2-utils
schemas/ids/validate.sh
```

Nothing else. The first draft listed UiCheck, StudioCheck, NavCheck and InstCheck
as if they were repo commands; they are local scaffolding outside the tree and they
do not appear here.

### 5.2 What cannot be verified in this environment

There is no Navisworks, no licensed seat, no test project over `CamelWorks.UI`
(67,639 lines, zero tests), and WPF compiles only through the reference-assembly
harness. So four kinds of criterion are not checkable here, and each has an honest
substitute rather than a criterion that reads as though it were:

- **"The board renders identically."** Not verifiable: nothing draws the real board
  in this environment. The substitute is a stated scope — *a
  compiler-and-validator-only refactor: no behavioural change is asserted by any
  test, and the criterion is that the phase compiles, the validators pass, and the
  diff contains no change of logic.* Where behaviour must be asserted, it is
  asserted in Core over fakes, below the UI.
- **"On a licensed seat."** Not verifiable. `ClashCheck` and `FileCheck` therefore
  go in **Core**, not UI, so the read, the scope, the host-and-file merge and the
  delta are all asserted in `Core.Tests` over `FakeClashSource`. Everything above
  the seam — the board's use of them — is unproven here and the phase says so.
- **"The harness's own tests."** The harness has no tests. Any assertion attributed
  to it belongs in `Core.Tests`.
- **`NavTriangles`.** Zero possible coverage (3.1): capped at 50 lines, all logic in
  Core, contract pinned with `FakeDocument` including `CanReadTriangles = false` and
  a deliberately unwelded quad, COM failures surfaced in `CheckRead.Notes`, and one
  licensed-seat smoke run required before merge.

**Renders.** `shots/app-clash-manage` is drawn by a hand-written reconstruction in
`CamelWorks.Studio/Panels.cs`, not by `ClashManageViews`, so rendering it proves
nothing about the product's board. `validate-harness.py` only catches captions the
harness invented; it never catches a caption the product gained and the harness did
not. So: **every phase that changes the Manage board updates `Panels.cs` in the same
commit**, and that is a line in each such phase, not a convention.

A new harness page must be added to `Panels.cs` and, to render at all, to both
`Program.cs`'s `Only` list and the `ui-shots` workflow default —
`validate-shots.py` fails if the two disagree. Given the Actions budget, new pages
stay **out** of the workflow default and are named on dispatch.

### 5.3 Gates each phase must account for

- **`validate-doc-coverage.py` (CS1591) and `validate-docs.py` (CS1573)** cover
  Core, Nav and Nav.Clash. About 35 new public Core types are proposed here and
  every member of each needs a summary, every parameter a `<param>`. The
  multi-parameter members that will trip CS1573 if half-documented:
  `FindingLocation` (6), `WalkableSurfaces.Detect` (3), `FloorGaps.Analyze` (8),
  `AnalyzeEdges` (5), `CheckDelta.Between` (5), `ResultColumn` (3).
- **`validate-capability-map.py`.** Every new public Core type is a row in
  `docs/CAPABILITY_MAP.md` in the same commit, or an entry in the script's
  `ALLOWED` set with its reason. The seam interfaces — `ICheck`, `ICheckHostEcho`,
  `ICheckGrouping`, `ICheckRunner`, `ICheckNaming`, `ICheckGeometry`,
  `ICheckProperties`, `ITriangleSource` — go in `ALLOWED` beside `IClashSource`,
  as infrastructure. Everything else is a row: `CheckCatalogue`, `CheckRun`,
  `CheckRunSpec`, `CheckRunStore`, `CheckRead`, `CheckFacts`, `ResultColumn`,
  `ColumnKind`, `RunJournal`, `CheckDelta`, `FindingLocation`, `IssueCascade`,
  `Priorities`, `TriangleMesh`, `WalkableOptions`, `WalkablePatch`,
  `WalkableDiagnostics`, `WalkableSurfaces`, `UpAxis`, `Vec3`, `Tri2`,
  `FloorGapGrid`, `FloorOpening`, `EdgeClass`, `EdgeLabel`, `FloorEdgeResult`,
  `FloorGaps`, `FallHazardFindings` and the PNG encoder.
- **`validate-engines-reached.py`** matches `public static class` with a doc
  summary, and requires a non-test call site. It does not apply to `CheckCatalogue`,
  which is a sealed class — the first draft's stated reason was wrong. It does apply
  to `WalkableSurfaces`, `UpAxis`, `FloorGaps` and `FallHazardFindings`, none of
  which has a call site except the set-up tabs. That is why Phase 3 ships the
  walkable-surface work **with** the screen that uses it, and Phase 11 ships the
  rasteriser with the Fall Hazard tab.
- **`checks/folds.txt` is empty**: zero `Ui.Section` folds are permitted in
  `CamelWorks.UI`. The clearance and fall-hazard set-up run to nine settings each
  and must use `Ui.Configure`, not a fold.
- **`checks/tables-without-picking.txt` is a ratchet at 1.** Every new run list is a
  `Ui.Table` and needs `Ui.Picking(` or `.Menu(`.
- **`validate-verbs-dont-search.py` and `validate-select-then-act-on-selection.py`**
  key on the literals `.Model.Select(`, `.View.Isolate(`, `.View.ZoomTo(`. Routing
  those through `ICheckGeometry` makes both checks pass by finding nothing, which is
  worse than failing. They learn the new seam in the same phase that moves the calls.
- **`checks/lazy-key-resolution.txt` lists `CoordinateViews.cs`.** A line there that
  stops resolving lazily fails the check; the `ClashItem` projection touches that
  file.
- **`validate-board-virtualised.py` is hard-wired to `ClashManageViews.cs`** and
  requires `new BoardList(` to stay in it. The board rows do not move out of that
  file.
- **`checks/reserved-type-names.txt` holds 564 names.** The geometry helper names
  are settled in 3.3.
- **`CONTRIBUTING.md` requires a `docs/apps/` entry per changed tab.** Every phase
  below that changes a tab updates `docs/apps/clash.md` in the same commit.

### Phase 1 — The record layer in Core

No check seam, no board work, no UI. Everything here is `CamelWorks.Core` and
`CamelWorks.Core.Tests`.

Scale, so the phase is not under-estimated: `FindingSource` appears at **135 sites
across 30 files**; the children-to-locations change touches **six producers and
about ten test files**.

- `docs/findings-and-issues.md` rewritten (section 6).
- `FindingLocation`, with its notes list; `Finding.Locations` / `AddLocation` /
  `ReplaceLocations` / `HasLocations`. Six producers updated: clash is unaffected
  (leaves), health, IDS, set drift and compare move to locations.
- `FindingRegister.OwnerOfLocation`, `FindLocation`, `LocationCount`, with a lazy
  index dropped on `Put`, `Remove` and `Clear`.
- `IdsFindings.DefaultCap` raised from 500 to 1,000.
- `FindingSource` becomes a table of constants; `Finding.Source` becomes `string`;
  the three identity rules of 2.6 written and pinned.
- `FindingIntake.Reopened` (1.8).
- `Issue` reshaped per 2.3: lattice status, `Provenance`, `Facts`, `DueDayNumber`,
  `FindingComment`. `IssueComment`, `IssueOrigin`, `Issue.Clashes`, `ManualIssue`
  and `Issue.Priorities` deleted. `IssueRegister` becomes a versioned object.
- `ProjectProfile.Priorities` becomes the one priority vocabulary; `Priorities.All`
  is the default it is seeded with.
- `IssueCascade` replaces `Cascade` and `IssueFindings.StatusFor`. Applied, not
  offered; merge, not assignment.
- Set drift already has raise-an-issue: `ProjectViews.Reached` goes through the
  shared judgement row, which carries it. Attach-to-an-existing-issue is the verb
  it lacks, and it needs an issue picker on that screen, so it moves to Phase 10
  with the other screen work.
- The BCF guid convergence test: `ClashFindings.From` stamps
  `ClashBcf.TopicGuidFor(...)` and `FindingBcf.TopicGuidFor` prefers the stamped
  value. That one-line agreement is undocumented on both sides and is pinned here,
  because Phase 6's file-merge criterion depends on it.

**Done when.** In `Core.Tests`: `FindingJson` round-trips a finding with 1,000
locations and its id is unchanged from the same finding with 400.
`register.Find(locationId)` returns null and `register.FindLocation(locationId)`
returns the location and its owner. A record with no `source` is counted unreadable
and produces no finding; a record with an unknown non-empty source round-trips; a
record with `"clash"` in lower case does **not** take the clash identity branch. A
cascade to Reviewed over a set containing an Approved finding leaves it Approved and
lists it under `Higher`. A cascade to Resolved over 1,000 findings writes 1,000 and
reports 3 missing ids without throwing. `Observe` over a register holding an
Approved finding that the run reports present lists it in `Reopened`. The known
check-key set matches the persisted source names. `ClashFindings` and `FindingBcf`
agree on a clash topic guid. `docs/CAPABILITY_MAP.md` names `FindingLocation`,
`IssueCascade` and `Priorities`.

### Phase 2 — One save door

- `SaveWork(out string?)` in Core over `ProjectStore` and `FindingStore`, with the
  refusals and the order of 2.5. `Session.SaveWork` delegates.
- Every attach, raise and cascade site moved onto it.
- `checks/validate-one-save-door.py`, in the same commit.
- `FindingRegister.FromJson`'s false "left alone rather than guessed at" comment
  deleted.

**Done when.** In `Core.Tests`, over a fake `IFileSystem`: a save with the project
store read-only writes neither file and returns the reason; a save where the
sidecar write throws leaves the project file's bytes unchanged and returns
`FindingStore.LastSaveProblem`; a save with a memory-only register refuses rather
than writing a project file that references findings that are nowhere.
`validate-one-save-door.py` fails a deliberately reintroduced bare `Findings.Save()`
and passes the tree.

### Phase 3 — Triangles at the seam, and walkable surfaces

The first phase that ships a screen, and it ships the owner's first-named request:
automatic floor finding. It has no dependency on the check seam, so it does not wait
for the board work. The set-up screen it lands on is the **existing** Clearance
Check tab; the restructure into runs and results is Phase 10.

- `TriangleMesh`, `ITriangleSource`, the weld, and the seam contract test that pins
  it. `NavTriangles`, capped at 50 lines per 3.1. `FakeDocument`'s mesh answers.
- `Vec3`, `Tri2`, `Span1`, `BoxFit` — the geometry helpers, named to clear
  `reserved-type-names.txt` (3.3).
- `UpAxis`, `WalkableOptions`, `WalkablePatch`, `WalkableDiagnostics`,
  `WalkableSurfaces.Detect`, the stacked-patch dedupe and the patch-triangle
  extrusion.
- `Headroom` keeps its plan grid and its worst-per-surface reduction and gains a
  patch-shaped input: the obstruction search runs over patch prisms rather than
  element boxes. Its 13 existing tests stay green.
- The clearance tab's floor side gains "Detected walkable surfaces" beside the
  seven-word guess, the selection and the saved sets — with the count, the area, the
  visible `PreferFloorCategories` tick, the drop-a-patch list and the stairs toggle
  of 3.5. The guess stays, labelled as now, for seats where the triangle source is
  unavailable. This is also what gives `WalkableSurfaces` and `UpAxis` the non-test
  call site `validate-engines-reached.py` requires.
- The measurement the source omits: actual clear height at the worst point, and
  that point.
- `Panels.cs` gains the clearance floor-side page; it is **not** added to the
  `ui-shots` default and is named on dispatch. `docs/apps/clash.md` updated.

**Done when.** Synthetic-mesh tests in `Core.Tests`, ported from the source
project's own, assert: a slab produces one patch and its underside produces none; a
patch at site coordinates has a footprint at site coordinates, not one reaching the
origin; an L-shaped floor's prism does not cover the missing corner; a structural
slab and its finish 40 mm above it dedupe to one patch; an unwelded quad welds to
four vertices with two shared; a mesh below the minimum area is dropped and counted
in `WalkableDiagnostics`. `CanReadTriangles == false` degrades to the box behaviour
and the diagnostics say which was used. `ArchitectureTests` still passes, which
proves no geometry library came with the port. One licensed-seat smoke run of
`NavTriangles` before merge; that is the only part of this phase not covered by a
test, and it is stated rather than implied.

### Phase 4 — The check seam in Core, with no consumer

`CamelWorks.Core/Checks/`: `ICheck`, `CheckCatalogue`, `CheckRun`, `CheckRunSpec`,
`CheckRunStore`, `CheckRead`, `CheckFacts`, `ResultColumn`, `ColumnKind`,
`ICheckHostEcho`, `ICheckGrouping`, `ICheckRunner`, `ICheckNaming`,
`ICheckGeometry`, `ICheckProperties`, `RunJournal`, `CheckDelta`. Roughly 700 new
lines and no existing line changed.

`CamelWorks.Core/Testing/FakeCheck.cs`, mirroring `FakeClashSource`, and
`CheckSeamContractTests` mirroring `ClashSeamContractTests`, with the same
assertions added to `SelfTest`.

An empty `CheckCatalogue` is registered on the session in this phase. Not for
`validate-engines-reached.py` — that script matches `public static class` and
`CheckCatalogue` is sealed — but because a type with no call site anywhere is a type
nobody has compiled against.

**Done when.** `CheckSeamContractTests` asserts: a check with every capability null
still reads and its findings observe into the register; `CheckRead.Scope` restricted
to one run leaves the other runs' findings present after `Observe`; a finding
carrying two run ids in `CheckFacts.Runs` is in scope for both and drawn once;
`CheckRunStore` round-trips a spec with an unknown settings key verbatim, and
writing `checks/runs` leaves `checks/journals` byte-identical; `CheckDelta.Between`
with no journal reports `NoPreviousRun` and an empty `New`, and with an empty
journal reports every finding New; a check offering aliases matches a finding whose
id changed to a neighbour and counts it in `MatchedOnAlias`; `Regressed` is read
from `FindingIntake.Reopened` and is empty when the intake reports none.
`validate-capability-map.py` passes with the new rows and the `ALLOWED` additions.

### Phase 5 — One status type on the board

`CLASH_PLAN.md` 6.3 says 31 status literals across UI and Nav. The count is **84**,
and all 84 are in `CamelWorks.UI` — none in `CamelWorks.Nav` or
`CamelWorks.Nav.Clash`. This is a phase of its own because it blocks the board move
and because a mechanical change of that size must be separable from a structural one.

- `Row.Status`, `TestEntry`'s five-slot tally, `BoardRow.Rank`,
  `ClearanceViews.Statuses` and the two places clearance re-parses a status out of a
  string all become `FindingStatus`.
- `FindingStatusWords.Of(FindingStatus)` is written — it does not exist yet — and
  becomes the only place a status becomes text in `CamelWorks.UI`.
- `ClashStatusMap.Read` becomes the only reader of a status word from outside, at
  the host and file boundaries. `FindingBcf.Apply`'s bare `TryParse` is replaced by
  it, with the guess tier flagged so a caller that must not guess can refuse.
- `Panels.cs` updated in the same commit where the board's status rendering changes.

**Done when.** Grep for the five status words as string literals in
`CamelWorks.UI` returns zero occurrences outside an allow-list naming two sites:
the doc-comment prose in `Ui.cs` line 606, and `MatrixChange.Added => "New"` in
`MatrixWindow.cs`, which is a change kind and not a finding status. The real target
is 82 of 84. `FindingStatusLattice` and `ClashStatusMap` are in Core and are not in
the greped set. One `Core.Tests` test reads the same pack through `FindingBcf` and
`ClashBcf` and asserts one status, including for a word only the guess tier maps.
This is otherwise a compiler-and-validator-only refactor: no test asserts the board
draws the same thing, because nothing here can.

### Phase 6 — The clash board on the seam: the mechanical pass

`ClashCheck : ICheck` over `IClashSource`, about 400 lines; `FileCheck : ICheck`
over `ClashFile.Read`, about 250, mostly lifted from `ReadFile`. **Both in Core**,
so their reads, scopes, merges and deltas are asserted in `Core.Tests` over
`FakeClashSource` (5.2). `IClashSource` itself does not change.

- `entry.Test.X` becomes `entry.Run.X`; `row.Info.X` becomes reads off the finding
  and its facts.
- Every host edit moves into the board's `Write(...)` helper per 4.2 — the finding
  first, `HostEcho` second, pending on refusal. Not a find-and-replace.
- The rail helper block that composes `Kinded`, `Tolerance`, `Trades` and `Sides` is
  deleted; those are `CheckRun.Kind`, `Threshold`, `Trades` and `Between`.
- `OnFile` branches become capability nulls.
- File-mode boards observe into the register, matching on topic guid, and mark
  nothing absent — a file is a partial view of a run, not a run.
- `validate-verbs-dont-search.py` and `validate-select-then-act-on-selection.py`
  learn `ICheckGeometry` in this commit, since the literals they key on move here.
- `Panels.cs` and `docs/apps/clash.md` updated.

No check picker, no second check, no column change, no delta change. Board behaviour
is unchanged and that is the whole point of doing it alone.

**Done when.** In `Core.Tests`: `ClashCheck` over `FakeClashSource` reads the runs,
scopes them, and observes into the register with the same counts the existing path
produces for the same fixture; a board built from a file writes findings that a
subsequent host read **merges rather than duplicates**, which the Phase 1 guid test
underwrites. `validate-board-virtualised.py`, `validate-table-picking.py`,
`validate-verbs-dont-search.py` and `validate-select-then-act-on-selection.py` pass,
the last two having learned the new seam. Above the seam this is a
compiler-and-validator-only refactor and nothing here proves the board draws the
same thing; that is what the licensed-seat check before release is for.

### Phase 7 — Columns and severity

- `PropColumn` grows into a declared board column with `Kind`, `Number`, `Text`,
  `Format`, `Bucket`, `IsBad`, `RightAligned`, `IsDeclared` and `Unit`. It is
  consumed at about 25 sites between lines 3318 and 4780 of `ClashManageViews.cs`.
  Declared columns take the natural order slots rather than being appended, and
  numeric columns sort numerically and offer ranges rather than 300 distinct strings.
- The two hard-coded element columns (`Item 1`, `Item 2`, nine sites) become
  `ColumnKind.Element` with an `ElementIndex`.
- `_toleranceMm` and the whole `Deep`/`Past`/`Millimetres`/`Bad` block are replaced
  by `ICheck.Severity` and `SeverityBand`. This must land in one change: anything
  still reading `_toleranceMm` will silently tone another check's rows against a
  25 mm default.
- The unit fix of 1.6: every length fact and every `Finding.X/Y/Z` in metres at full
  precision, in every producer, and the eight `OnFile ? 1.0 : MetresPerUnit` sites
  deleted.
- `Panels.cs` and `docs/apps/clash.md` updated.

**Done when.** In `Core.Tests`: a declared numeric column sorts by its `Number`
accessor and not by its formatted string, and a value of 0.0004 m sorts below
0.0006 m where both format as "0 mm"; `ICheck.Severity` returns 1.0 exactly at a
run's threshold for clash and for clearance; a clearance finding and a clash finding
over a foot-unit model both report their length facts in metres; `Bucket` produces
the same band for a value read from the host and the same value read from a file.

### Phase 8 — The journal, the delta and the item projection

- `Standing` moves from `HashSet<ClashKey>` to `CheckDelta`. Note that
  `ClashManageViews.cs` contains **zero** references to `ClashDelta` or
  `ClashSnapshot` — the first draft's premise here was wrong — and that `Standing()`
  reads `CoordinateViews.LastDelta`. So this phase's work is in
  `CoordinateViews.cs`, a second 2,861-line file, not only in the board.
- `RunJournal` written after every read, per (check, run), two kept (1.8).
- `ICheck.Aliases` implemented for clash, returning the 26 neighbour ids;
  `MatchedOnAlias` shown where `MatchedOnNeighbourCell` is shown now.
- `Regressed` read from `FindingIntake.Reopened`.
- `CoordinateViews.Build` stops re-reading the host and projects `ClashItem`s from
  the register, per 2.7's field table. `ClashSnapshot` is still written; the journal
  and the register are authoritative.
- **`CoordinateViews.Hydrate` reads the register**, not the snapshot: the Simulate
  seat rebuilds its board from findings, including suppressed results and the
  carried group.
- `checks/lazy-key-resolution.txt` covers `CoordinateViews.cs`; the projection must
  not resolve keys eagerly.
- `Panels.cs` and `docs/apps/clash.md` updated.

**Done when.** In `Core.Tests`: `CheckDelta` over two journals reproduces the same
New / Persisting / Gone counts the `ClashDelta` path produces for the same two
fixture runs, and the same neighbour-matched count; a first run reports
`NoPreviousRun` and the board's sentence is "no previous run recorded"; the
`ClashItem` projection produces the same item set the host read produces — key,
level, disciplines, models and coordinates, item for item; a register hydrated with
no clash source rebuilds a board carrying suppressed results and `CarriedGroup`.

### Phase 9 — One BCF writer, so exchange is per check

Before the clearance restructure, not after. Phase 10 deletes `ClearanceViews`' own
BCF export, and the board's export takes `ClashItem`s and derives the topic guid
from a `ClashKey`, which a clearance finding does not have. Done in the other order,
clearance loses BCF for a release and gains nothing.

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
  colouring, section box, the render callback. It generalises to "the finding's
  `AllElements()` and its point", with per-check defaults, and 4.4's note about
  `ResultSnapshot` is written on the tab rather than implied.
- `IssueBcf` folds in, now that the issue status is on the lattice.
- `ClashExchangeViews`: the trade split falls back to assignee for a check with no
  trade sides, `Preferred` becomes `(checkKey, runIds)`, and the tab follows the
  Manage tab's check (4.4).
- `docs/apps/clash.md` updated for the Exchange tab.

**Done when.** One writer. In `Core.Tests`: a pack written from clearance findings,
read back and re-exported carries the same labels it left with and no more; the same
returned file yields one status through every path; a returned status for a
clearance finding whose geometry moved between export and return is either matched
to the successor or reported unmatched, never silently dropped.
`schemas/bcf/validate.sh` passes on an archive carrying a clearance finding, its
issue, the relation both ways and a viewpoint.

### Phase 10 — The check picker, and clearance as tests and results

- The check picker of 4.3: the control, the greyed unavailable entries with reasons,
  the counts, the per-check statics, `_current` as a `(checkKey, runId)` pair, and
  the set-up-to-results hand-off.
- Attach-to-an-existing-issue on the set drift screen, deferred from Phase 1
  because it needs an issue picker rather than a mechanical edit.
- The tab set of 4.1: "Clearance Check" becomes "Checks" in `Apps.cs`, with its own
  check picker over the set-up screens; `Surfaces.cs` rewired.
- `ClearanceCheck : ICheck` with `HostEcho` null, `Grouping` null, `Naming` through
  `CheckRunStore`, a runner, geometry through the element locator, and four declared
  columns.
- Clearance runs become `CheckRunSpec`s, with the settings of 4.6: name, folder,
  minimum, tolerance, surfaces, targets, level and zone scope, obstruction ignore
  list, worst-per-patch, stairs, and the detected-surface exclusions from Phase 3.
  Re-running is pressing Run on a saved run.
- `ClearanceFindings` gains the run's stable id as `identity` and the run's name as
  `Rule`. Without it, two clearance runs over different corridors at the same place
  collide on identity. The minimum stays out of identity, as now. `CheckFacts.Runs`
  is written on every finding.
- `Keep` passes `CheckRead.Scope`.
- `ClearanceViews` reduces to the set-up: the side pickers, the numbers, the
  traversal, the run list, and the floor-side screen Phase 3 built. Its
  `PropColumn`, `Bucket`, `Row`, banding, filtering, columns, marker export, BCF
  export and issue attach are deleted — about 1,200 lines — because they are the
  Manage board. `Bench.Ready`'s four empty states move onto the board per 4.5,
  rather than being deleted with the rest.
- The four empty states and the `CheckRead.Notes` line of 4.5.
- **`ClearanceViews.Check(Session)` becomes `Check(ISeat)`, and that is an `ISeat`
  widening this plan has to pay for.** `ClearanceViews` uses `_session.View`,
  `.Model`, `.Findings`, `.Profile` and `.MetresPerUnit`, none of which are on
  `ISeat`. `Findings`, `Profile` and `MetresPerUnit` go onto `ISeat` as plain
  members. `View` goes on as `IViewSession`, which is already an abstraction.
  **`Model` does not**: `Session.Model` is a `NavDocument`, a Navisworks type, and
  putting it on `ISeat` drags Navisworks into the render harness, which is the whole
  reason `ISeat` exists. What replaces it: the two things the set-up screen asks the
  document for are the model list and a traversal, and `ISeat.Models()` and
  `ISeat.Visible()` already answer both. `MatrixWindow.Surface(ISeat)` is the
  template that already works.
- `Panels.cs` gains the Checks tab page and the Manage board's picker; neither goes
  in the `ui-shots` default. `docs/apps/clash.md` updated for both tabs.

**Done when.** In `Core.Tests`: two named clearance runs over overlapping geometry
produce disjoint findings, and running one leaves the other's findings present;
`CheckFacts.RunsOf` returns both runs for a finding reported by both; a run saved and
never run is distinguishable from a run that found nothing. In the harness:
`validate-shots.py` and `validate-reachable-screens.py` pass with the new pages, and
the Checks tab draws over `ISeat` with no Navisworks assembly loaded, which is what
`Check(ISeat)` buys. `ClearanceViews.cs` is under 750 lines. Whether the Manage tab
draws clearance results correctly is not verifiable here and is a licensed-seat check
before release.

### Phase 11 — Fall hazard

- The rasteriser port into `CamelWorks.Core/Data/`, with the owner grid, the
  surviving per-opening and per-run records, metres, path-measured runs and the
  convergence report.
- A PNG encoder in Core beside the existing reader, or the raster returned as
  width, height and RGBA for the report writers to encode.
- `FallHazardFindings`: one finding per flagged opening and one per dangerous edge
  run, elements from the owner grid, facts for opening area, widest gap and edge
  length, point at the centroid, all in metres.
- `FallCheck : ICheck` with `HostEcho` and `Grouping` null, a runner, geometry, and
  three declared columns.
- A Fall Hazard set-up screen on the Checks tab: floors, handrails, obstructions,
  level, band, cell size, limit, handrail tolerance, minimum passage — nine
  settings, so `Ui.Configure` and not a fold — saved as a `CheckRunSpec`. Results
  are on the Manage tab, like every other check. This is also the non-test call site
  `FloorGaps` and `FallHazardFindings` need.
- `Panels.cs` gains the Fall Hazard set-up page, off the `ui-shots` default.
  `docs/apps/clash.md` updated.

**Done when.** The source project's own test file, ported into `Core.Tests`, is
green: cell-size independent clearance, slot width against slot length, corner cells
of a dangerous opening, and the short break under minimum passage. An opening's
finding names the slabs around it from the owner grid. A 45-degree dangerous run
reports a length within 2% of its true length. The sweep cap, when hit, appears in
`CheckRead.Notes`. `validate-shots.py` and `validate-reachable-screens.py` pass.
Adding the check touched no engine and no board code, which is the claim the whole
plan rests on and the only phase that can test it.

---

## 6. Corrections to the existing documents

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
  which stops being true the moment there is more than one clearance run. Phase 10
  changes the code; the row changes with it.
- The whole document must be rewritten around the decision in section 2: a finding
  is the unit of decision, a location is where it is and carries a note but never a
  decision, an issue is a finding-set somebody raised, and the cascade is a lattice
  merge applied rather than offered.

### `docs/CLASH_PLAN.md`

- **6.3 says "31 status literals across UI and Nav". The count is 84, all of them
  in `CamelWorks.UI`.** Correct the number and the distribution in 6.3 and in the
  Not-yet-done entry.
- **6.2 says the two BCF writers use "different topic GUIDs for the same clash".**
  They agree today: `ClashFindings.From` stamps `ClashBcf.TopicGuidFor(...)` onto the
  finding and `FindingBcf.TopicGuidFor` prefers the stamped value. The convergence is
  one line deep and undocumented on both sides. The sentence should say so, and the
  test that pins it is in Phase 1.
- **6.1's Not-yet-done entry** should name what stays on the item spine — host clash
  group structure, pinning and the assignment log — rather than implying the whole
  item spine goes.

### `docs/CAPABILITY_MAP.md`

Each phase adds its rows in the same commit, or `validate-capability-map.py` fails.
The full list, and which seam interfaces go in the script's `ALLOWED` set instead,
is in 5.3.

### `docs/apps/clash.md`

`CONTRIBUTING.md` requires an entry per changed tab. Phases 3, 5, 6, 7, 8, 9, 10 and
11 all change a tab in the Clash Manager and each updates this file in its own
commit.

---

## Not yet done

Deliberately out of scope, with the reason.

- **Per-location triage.** A location carries a note and never a status, assignee or
  sign-off. Giving it one makes it a register entry under another name and brings
  back the identity problem 2.1 exists to avoid.
- **Host clash groups for other checks.** Pinning, `GroupDecision` and the chained
  regroup that writes groups into the model stay clash-only behind `ICheckGrouping`.
  No other check has host groups to write. The board's own banding and chained
  grouping are generic and work on every check from Phase 6.
- **Deleting `ClashSnapshot`.** It stops being authoritative in Phase 8 and keeps
  being written until the five Coordinate tabs that read it — Filter Clashes,
  Report, BCF, Merge, Penetrations — and the review window are re-pointed. Deleting
  it is a phase of its own and nothing here depends on it.
- **A `TooNew` refusal on `FindingStore`.** `VersionedDocument` has one and the
  sidecar does not. There is no newer build to refuse yet, so it is a robustness
  item for the first release that has a predecessor, not a requirement here.
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
- **A generic cross-run reducer.** `DuplicateCollapse.Across` folds one physical
  conflict reported by two clash tests. That stays inside `ClashCheck`; the seam
  carries run membership but not the reduction, and a check that wants one does it in
  its own read. A generic reducer would need a generic notion of "the same result",
  which is exactly what the per-check identity avoids.
- **Cross-check runs on one rail.** The picker holds one check at a time. A flat list
  of every run of every check would need a union of columns, most of them blank on
  most rows, and banding and filtering are scoped to one run.
- **Deferred issue attachment.** `Issue.Clashes` is deleted and nothing replaces it:
  an issue may only be attached to findings that already exist, so the attach and
  raise verbs are unavailable before a read. A holding pen keyed on a clash key would
  reintroduce the one type that has to be swapped for a finding id later.
- **Moving the `issues` section out of the project file.** The write-amplification
  argument that moved findings to a sidecar does not reach issue volumes, and the
  move would buy nothing. The dangling-id bug it is confused with is fixed by the one
  save door in Phase 2.
- **Column sorting in `Ui.Table` for the set-up tabs.** The Manage board sorts; the
  Checks tab is a list of runs, and sorting a list of six runs is not worth a change
  to the shared table.
- **`CLASH_PLAN.md` 6.7 and 6.9.** Splitting the two large files and having the
  harness draw the real views. Phase 6 removes the reason `ClashManageViews` reads
  host clash types, which is the precondition for both, but neither is attempted
  here. Until 6.9 lands, the Manage board's rendered page is the harness's own table
  in `Panels.cs` and does not prove a change to the product's board.
