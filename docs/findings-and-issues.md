# Findings and issues

The record layer every check in CamelWorks writes to. This describes what is built, not what is
proposed.

## What it is for

Every tool in CamelWorks that checks the model produces the same shape of thing: a problem, at a
place, that somebody has to decide about. Clash results. Clearance breaches. IDS failures. Model
health faults. Set drift. Differences between two revisions.

Each of those was heading for its own record, its own status field, its own board columns and its
own export path — six near-identical subsystems and six places for the same bug. They converge on
one record, `Finding`, kept in one file, exported by one BCF writer, and grouped by one thing
people actually assign and close, `Issue`.

## The three types

### `Finding` — the unit of decision

What a check produced. Identified by what makes it that finding rather than by an allocated id:
`Finding.Create` hashes the source, the rule (or the identity string that stands in for it), the
discriminator and the elements, sorted, so two producers listing a pair the other way round get
the same id and the same problem in next week's run keeps last week's sign-off.

Findings are kept in `.camelworks/<model>.findings.json` beside the project file, not inside it.
The project file is rewritten whenever any tool remembers a setting, and an eighteen-thousand-clash
job is about 3.6 MB of findings; remembering a textbox should not rewrite that.

### `FindingLocation` — where it is

One place a finding is: an id, its elements, an optional point, its own facts, and notes somebody
wrote against that one place.

A location carries **no decision**. There is no per-location status, assignee, due date or sign-off,
and there will not be one. A location carrying triage state is a register entry under another name,
and its id would then have to be derived from its own element — so one element moving, splitting or
being renamed would re-identify it and lose whatever had been decided about it. That is the
identity problem this shape exists to avoid.

What it may carry is a **note**: free text with an author and a time, which records "this one is a
false positive, the wall is a void former" without claiming the location was decided.

The load-bearing consequence: **a finding's id ignores its locations.** A specification failing 400
elements this week and 1,000 next week is the same finding, with the same status, assignee and
comment trail. `ReplaceLocations` records this run's places over last run's, because keeping the
old ones would leave "show me all of them" pointing at a set that has moved on.

Locations are not counted in `FindingRegister.Count` or `Open`, are not attachable to an issue, and
are not written as their own BCF topics. `FindingRegister.Find(locationId)` returns null;
`FindLocation` and `OwnerOfLocation` answer instead, off a lazy index dropped whenever the register
changes. `LocationCount` exists so a screen can say "37 rules, 1,204 locations" rather than implying
37 problems are 1,204.

### `Issue` — a finding-set somebody raised

What somebody decided to do about one or several findings. It can exist with no finding at all
("no access to this valve"), which is why it is not collapsed into `Finding`: collapsing them would
mean either inventing machine findings for things no machine found, or giving every one of eighteen
thousand clashes a conversation nobody will have.

It gains nothing a finding does not have:

- **Status** is `FindingStatus`, the same five-rung lattice.
- **Comments** are `FindingComment`, the same type.
- **Priority** comes from `ProjectProfile.Priorities`, the same project-configured list a finding
  draws from, seeded from `Priorities.All`. An issue over three findings must not offer a different
  vocabulary from the findings it covers.
- **`DueDayNumber`** is whole days since `DateTime.MinValue`, the same unit as
  `Finding.DueDayNumber`.
- **`Facts`** is the open bag. Level, zone and the file a topic arrived in live there; nothing
  coordination-specific is a field, so a check can carry its own facts onto an issue.

`Provenance` is where the issue came from — `"Hand"`, `"Bcf"`, or a check's key — set at creation
and never derived. An imported topic arrives with no findings attached, so the number of attached
findings cannot tell an imported issue from a hand-typed one, and the list screens filter on exactly
that distinction. `Sources(register)` derives which checks the attached findings came from, falling
back to `Provenance` when none are attached.

**An issue may only be attached to findings that already exist.** The attach and raise verbs sit on
a results board, which is downstream of a read. There is no deferred-attachment path and no
key-shaped placeholder: a holding pen keyed on anything other than a finding id has to be swapped
for one later, and that swap is what the deleted clash-key list was.

## Identity per source

A finding survives a re-run only if its derived id is stable across runs. Get it wrong and every
check duplicates its findings each time it runs, and every status, assignee, comment and issue
attachment detaches at once.

| Source | Identity string | `elements` | `discriminator` |
|---|---|---|---|
| `Clash` | the host's own id for the test, not its name | the two clashing elements | the quantised clash cell |
| `Headroom` | the check's name | floor + obstruction | the quantised position cell |
| `Ids` | the specification's own identifier | none — the locations carry the elements | the specification |
| `Health` | the rule's stable id | none | the rule's id |
| `Compare` | `"Model compare"` | none | model + kind of change |
| `SetDrift` | `"Set drift"` | none | the set's name |

Two rows deserve their reasoning written down.

**Clash identity is the whole key, including where the clash is.** An earlier design used the
element pair alone so that a conflict shifting a few millimetres would not resurrect as new. That
was a defect: a duct crossing one beam twice is two results and would have been one BCF topic — two
`markup.bcf` entries at one path, a malformed archive whose second topic most readers silently
drop. Tolerance belongs at match time instead, where the sync looks through the twenty-six
neighbouring cells for the few topics that matched nothing exactly. The general rule: **identity is
exact and matching is tolerant.**

The rule the identity is hashed from is the host's test **id**, not the test name, because renaming
a clash test is a first-class feature of this product. The name stays on `Finding.Rule`, where it
is read.

**The `Headroom` discriminator is the position cell, and that is not enough for more than one
clearance run.** Two runs at different minimums over the same soffit produce the same id today,
which is deliberate — re-running at 2.4 m after 2.1 m finds the same low soffit and calling it new
would throw a decision away on a settings change. It stops being right the moment clearance has
named, saved runs, and the row changes with the code that gives it them.

## Source is a string

`FindingSource` is a table of constants, not an enum: `Clash`, `Manual`, `Health`, `Headroom`,
`Ids`, `SetDrift`, `Compare`, `FallHazard`. The values are the names the enum it replaced was
written and hashed by, so no finding was re-identified by the change.

Three rules keep a sign-off attached, each pinned by a test in `Core.Tests`:

- **The source gate is strict.** A record with a missing or blank source is counted unreadable and
  dropped. It must never become a finding with an empty source: that finding's id would be derived
  from the empty string and would collide with every other record that lost its source.
- **An unknown but non-empty source reads through.** A finding written by a check this build does
  not have survives a round trip and is drawn by its own key rather than refused.
- **The clash identity branch in `FindingJson.Read` is ordinal and case-sensitive.** Matched
  loosely, or with a producer emitting `"clash"` rather than `"Clash"`, every clash finding in the
  project takes a different branch, re-identifies at once, and detaches everything recorded against
  it.

A check's key is simultaneously the finding source hashed into every id under it and the register
slice for that check, so it is never renamed once findings exist.

## The producers

All six machine sources emit `Finding`, all persist through `FindingStore`, and any of them can be
attached to an issue.

| Producer | Persisted | Shape | Attachable to an issue |
|---|---|---|---|
| Clash | yes | one finding per result — a clash *is* the unit of decision | yes |
| Clearance (`Headroom`) | yes | one finding per breach | yes |
| IDS | yes | one finding per specification, a location per failing element | yes |
| Health | yes | one finding per rule, a location per element it names | yes |
| Compare | yes | one finding per model and kind of change, a location per element | yes |
| Set drift | yes | one finding per drifted set, a location per departed element | yes |

`FindingSource.Manual` has no producer. Anything a person raises by hand is an `Issue` with
`Provenance = "Hand"`; findings are only ever produced by a check.

The caps are on the locations, not the findings: `IdsFindings.DefaultCap` is 1,000, and health and
set drift cap at 500. A rule that fails ninety thousand elements is one decision, and the finding
still records how many there were.

## The cascade

`IssueCascade.Plan(issue, to, register)` works out what marking an issue does to the findings
attached to it, and `Apply` writes it.

1. **The issue's own status is a direct edit.** A person deciding about an issue may demote it;
   `FindingStatusLattice.IsDemotion` is what a screen confirms on.
2. **Every attached finding takes `FindingStatusLattice.Merge(f.Status, to)`**, never a direct
   assignment. Assignment is what pulled an already-Approved finding back to Reviewed when the issue
   over it was answered.
3. **Every status cascades.** With the issue on the lattice there is no mapping table left to be
   incomplete.
4. **Locations follow their finding** and are never written.
5. **It is applied, not offered.** The offer existed because the mapping was partial and could
   demote; both reasons are gone. The plan is shown — how many move, how many are already at or
   above the target, how many attached ids the register no longer holds — and `Apply` is one write
   with one activity entry.
6. **One finding on two issues takes the higher of the two cascades.** The lattice does that
   without a rule.
7. **The reverse cascade is offered, not applied.** When every attached finding is Approved,
   Resolved or absent, the issue reads as ready to close. Closing is an act between parties.

## What the model gets told

**The database is authoritative; the Navisworks write is a projection of it.** One direction,
always.

Where the host has a field — a clash result's status, assignee, group and comments — the projection
writes it, so the native clash window and every other tool agree with us. Where the host has no
field, or refuses the write because the document is a published NWD, the projection does less and
the database is unaffected. `Finding.Pending` names which fields are waiting, as a set rather than
a flag, so a sync offers exactly those and never writes the same comment twice.

Reading back from the model is not the reverse of that. It is a **new observation**, merged by
derived id: `FindingRegister.Observe` promotes status through the lattice and skips any field that
is waiting, because a published NWD keeps reporting the value it had before anybody touched it and
a read allowed to win would revert the afternoon's work on every press of "Read the results".

A closed finding that a run reports present again is listed in `FindingIntake.Reopened`. The
register does not demote it — the lattice promotes only — so that list is the only place the fact
exists, and it is what the board's Regressed line reads.

## A finding the latest run no longer reports

Kept and marked absent, not deleted. It is the record of the thing having been fixed, an issue may
point at it, and a check re-run over a narrower scope must not silently destroy history. The board
hides them by default and can show them.

## Import and export

Findings and issues go out as BCF and come back as BCF, through the reader and writer that already
exist.

- A finding exports as a topic; an issue with attached findings exports as a topic per finding plus
  the issue's own, related by BCF's `RelatedTopic` both ways round, so a reader who opened the clash
  first can reach the conversation and not only the other way about.
- The return leg matches on the topic guid, which is a function of the finding's identity and is the
  only thing that survives somebody else's tool. The guid is stamped onto the finding when it goes
  out and kept, because a file that went out under a name the register does not know cannot come
  home.
- **The two clash writers agree on that guid.** `ClashFindings.From` stamps
  `ClashBcf.TopicGuidFor(key)` onto the finding and `FindingBcf.TopicGuidFor` prefers the stamped
  value over one derived from the id. The agreement is one line on each side and a test pins it;
  dropping either half gives one clash two guids and a returning file matches neither.
- A topic nothing here answers to stays a stranger and becomes an issue. Inventing a machine finding
  for a thing no machine found would put a row on a board no re-run can confirm or clear.
- The status comes home through the lattice, so a file that sat in an inbox for a fortnight cannot
  undo a decision taken since it left.

Verified against the vendored `markup21`, `markup30`, `visinfo21` and `visinfo30` schemas with
`xmllint`, on a real archive carrying an issue, its finding, the relation both ways and a viewpoint.

## Not done, and why

- **Per-location triage.** A location carries a note and never a status, assignee or sign-off.
  Giving it one makes it a register entry under another name and brings back the identity problem
  this shape exists to avoid.
- **Deferred issue attachment.** Nothing replaces the deleted clash-key list. The attach and raise
  verbs are unavailable before a read, which is where they are offered anyway.
- **Moving the `issues` section out of the project file.** The write-amplification argument that
  moved findings to a sidecar does not reach issue volumes.
- **A `TooNew` refusal on `FindingStore`.** `VersionedDocument` has one and the sidecar does not.
  There is no newer build to refuse yet, so it is a robustness item for the first release that has
  a predecessor.
- **`ClashSnapshot`.** It still holds carry-over, the previous-run comparison and the party history,
  and is still written. Those jobs move to the run journal and the register one at a time; deleting
  it is a phase of its own.
