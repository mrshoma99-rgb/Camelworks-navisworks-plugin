# Findings and issues

> **Status: proposal.** No code has been written to this. It exists to be corrected before any is.

## What this is for

Every tool in CamelWorks that checks the model produces the same shape of thing: a problem, at a
place, that somebody has to decide about. Clash results. Clearance breaches. IDS failures. Model
health faults. Differences between two revisions.

Today each of those is its own subsystem, with its own record, its own status, its own storage —
or no storage at all. This proposes one record for all of them, one place to keep it, and one way
to group them into the things people actually assign and close.

## Where we actually are

The interesting part: **most of this was already designed and then not wired up.**

`CamelWorks.Core/Findings/Finding.cs` is the record. It has:

- `FindingSource` — `Clash`, `Manual`, `Health`, `Headroom`, `Ids`, `SetDrift`, `Compare`. All
  seven producers, already named.
- Status, assignee, due date, priority.
- **Parent and child.** One rule failing 400 elements is one decision and 400 locations. A parent
  carries the triage; children carry the places and are never separately triaged. `AddChild`
  actively refuses a child that has been given a status of its own.
- **Derived identity.** `Finding.Create` hashes source + rule + discriminator + the elements,
  *sorted*, so two producers listing a pair the other way round get the same id. A parent's id
  ignores its children, so a rule that fails 401 elements next week instead of 400 is still the
  same finding and keeps its sign-off.
- `MergeFrom`, with `FindingStatusLattice` — status promotes and never regresses on a merge;
  assignee, due date and priority are latest-wins; `IsDemotion` exists so a caller can surface a
  regression as a conflict rather than apply it silently.

It has tests. It is used by exactly one of the seven producers — `CompareIssues` — and it is
**never persisted anywhere.**

| Producer | Persists across sessions | Uses `Finding` | Attachable to an issue |
|---|---|---|---|
| Clash | yes, via its own `ClashSnapshot` | no | yes, clash-only |
| Clearance (`Headroom`) | no — a static that dies with the session | no | no |
| IDS (`IdsReport`) | no | no | no |
| Health (`FederationReview`) | no | no | no |
| Compare | no | **yes** | no |
| Manual | — | no | it *is* the issue today |

`Issue.Clashes` is `IList<ClashKey>`. An issue can hold clashes and nothing else.

### The clash section is not a junk drawer — a correction

An earlier draft of this document said the clash section held thirteen keys, eleven of them written
by "the Board, which was retired", and proposed deleting them. **That was wrong, and it is recorded
here rather than quietly fixed, because a decision was taken on it.**

Two mistakes. `CoordinateViews` is not the retired surface: it backs five tabs that are live in the
app registry today — **Filter Clashes, Report, BCF, Merge and Penetrations** — plus the review
window. And the keys are written through a local variable (`section.Set("snapshot", …)`), so a
search for `ClashSection).Set(` found almost none of the writers and made live keys look
write-only.

The real picture. Every one of these is written and read by live code:

| Key | What it holds |
|---|---|
| `snapshot` | last run's results — what carry-over compares the next run against |
| `previous`, `previousParties`, `previousTaken` | the run before that, for the change columns |
| `parties`, `taken` | per-party open counts at the moment of the run, kept rather than re-derived |
| `pinned` | groups made by hand, which must never be re-derived |
| `decisions`, `issued`, `bcfGroups`, `sheetIssues` | grouping decisions, what has been issued, BCF and sheet state |
| `rules`, `issues`, `freeze` | the rule set, hand-raised issues, the freeze record |

**Nothing here is dead. No key is being deleted.** The lesson for the migration is the larger one:
`ClashSnapshot` is not merely "where clash results are kept" — it is the input to carry-over, to
the previous-run comparison and to the party history. Replacing it means findings taking on all
four jobs, not one.

## The model

### Finding

The result of any check. Owned by the check that produced it, identified by what makes it that
finding rather than by an allocated id.

Findings live in the project file beside the model, so opening the same model next week shows last
week's findings with their statuses intact.

**Identity per source.** This is the load-bearing decision of the whole design: a finding survives
a re-run only if its derived id is stable across runs. Get it wrong and every check duplicates its
findings each time it runs.

| Source | `rule` | `elements` | `discriminator` |
|---|---|---|---|
| Clash | test name | the two clashing elements | *nothing* — see below |
| Headroom | the check's name | floor + obstruction | none; the pair is unique per check |
| Ids | specification + facet | the failing element | the requirement that failed |
| Health | the rule's name | the element, or none for a model-wide fault | none |
| Compare | as `CompareIssues` already does | the changed element | the baseline it was compared against |
| SetDrift | the set's name | none (a parent) | none |

The clash row deserves its reasoning written down, because an earlier draft of this document got
it wrong and the mistake is instructive.

That draft argued for the element pair alone, excluding `ClashKey`'s quantised cell, so that a
conflict shifting a few millimetres between runs would not resurrect as new. The BCF topic id was
built that way — and it was a defect. A duct that crosses one beam twice is two Navisworks results
and would have been **one** topic: two `markup.bcf` entries at one path, a malformed archive whose
second topic most readers silently drop. Coarsely decomposed models, where a whole system is one
item, make that ordinary rather than exotic.

**Uniqueness wins. A clash finding's identity is the whole key, including where the clash is.**
Tolerance for a conflict that moved belongs at match time instead: the sync looks through the
twenty-six neighbouring cells for a topic that matched nothing exactly, and only for the few that
did not. That index is twenty-seven times the size and is never built on an ordinary sync.

The general rule this yields: **identity is exact and matching is tolerant.** Where a source has
the same ambiguity — two headroom breaches under one slab, one element failing one IDS rule at two
places — it resolves the same way.

### Issue

A thing somebody is asked to do. Raised by hand, or from one or more findings.

- An issue attaches **any number of findings, of any source**. "This riser is a mess" can hold
  eleven clashes, two clearance breaches and an IDS failure.
- A finding can be attached to more than one issue. That is real — the same clash can be evidence
  in two conversations — and it is why cascade is a decision rather than a rule.
- Issues also live in the project file. They already do.

### What the model gets told

**The database is authoritative; the Navisworks write is a projection of it.** One direction,
always.

Where the host has a field — a clash result's status, assignee, group and comments — the projection
writes it, so the native clash window and every other tool agree with us. Where the host has no
field, or refuses the write because the document is a published NWD, the projection simply does
less, and the database is unaffected.

Reading back from the model is not the reverse of that. It is a **new observation**, merged by
derived id through `MergeFrom`: a re-run of the clash test produces findings, they merge with what
is stored, statuses promote, and nothing anybody decided is lost. That is what keeps the database
from disagreeing with the model without making the model the owner of decisions it cannot hold.

### A finding that is gone from the latest run

Kept, and marked as absent from the latest run — not deleted.

It is the record of the thing having been fixed, an issue may point at it, and a check that is
re-run with a narrower scope should not silently destroy history. A board that hides them by
default and can show them is the right surface.

## Behaviours

### Attaching

Every findings window — clash results, clearance, IDS, health, compare — carries the same two
verbs on its selection: **attach to an issue** and **raise a new issue from these**. Both act on
however many rows are picked, both are on the right-click menu, like every other verb in the
product.

### Cascade

**Changing an issue's status asks, at that moment, whether to carry it to the attached findings.**

Not an automatic invariant. A finding can sit on two issues, and closing one of them must not
quietly close a finding the other still needs open. Asking makes the ambiguity visible at the one
moment somebody can resolve it, and the activity log records what was chosen rather than what was
inferred.

The prompt names the number and the direction: *"Also mark the 6 attached findings Resolved?"* —
and where the projection can reach the host, the findings' own write follows.

### Import and export

Both findings and issues go out as BCF and come back as BCF, through the reader and writer that
already exist.

- A finding exports as a topic; an issue with attached findings exports as a topic per finding
  plus the issue's own, related by BCF's `RelatedTopic`.
- On import, a topic that matches a known finding by topic id merges into it. A topic that matches
  nothing becomes an issue, which is what already happens today.
- What BCF carries that the host cannot hold — priority, due date, stage, tags — is stored on the
  finding rather than being mashed into a comment. `ClashAside` is the shape this generalises from
  — now a message read out of one markup and laid onto a finding, rather than a register of its own.

## Decisions taken

- **Clash findings replace `ClashSnapshot`**, migrating existing data on first open: read the
  snapshot once, write findings from it, stop writing the snapshot. One store, nothing to disagree.
- **`HeldEdits` and `ClashAsides` fold onto `Finding`.** ✅ **Done.** "What the host refused" and
  "what the host has no field for" are things every source will want — a clearance finding on a
  published NWD has the same problem — not clash quirks. Both registers are gone. The refusal is
  now `Finding.Pending`, a set of field names rather than a flag, so a sync offers exactly the
  fields that are waiting and never writes the same comment twice; the reviewer's fields are
  ordinary `Finding` properties. What was in the old keys is read back by `ClashLegacy` on every
  read, which is idempotent, so nothing is thrown away and the old keys stay on disk.
- **Anything a person raises by hand is an issue, not a finding.** Findings are only ever produced
  by a check. `FindingSource.Manual` stays unused. An issue raised by hand can still attach
  machine findings, so "no access to this valve" can carry the clashes that explain it.
- **Findings live in their own file**, `.camelworks/<model>.findings.json`, beside the project
  file. Not inside it: the project file is rewritten every time any tool remembers a setting, and
  an eighteen-thousand-clash job is about 3.6 MB of findings. Remembering a textbox should not
  rewrite that.
- **Status cascade asks at the moment of the edit**, naming the count, rather than being an
  automatic invariant. A finding can sit on two issues.
- ~~**The eleven Board-era keys in the clash section are deleted** during the migration.~~
  **Withdrawn.** The analysis behind this was wrong — see the correction above. Every key is live.
  Nothing is deleted.

## Open questions

1. **Scale.** Measured: about 209 bytes a finding, so 18,000 clashes is roughly 3.6 MB and
   100,000 is 20 MB. Fine for a file of its own; the reason it is not in the project file is write
   amplification rather than size. If a job ever outgrows one file, the fallback is one file per
   source.
2. **Does a parent finding project to the host at all?** A health rule failing 400 elements has no
   clash result to write to. Probably the projection is per-source and some sources project
   nothing, which is fine — but it should be stated per source rather than discovered.

## Order of work

Each step ships working and verifiable on its own.

1. ~~**The store.**~~ **Done.** `Finding` gained JSON and the fields the fold-in needs; a register
   that reconciles a run against what is held; a `.findings.json` file beside the project file. No
   producer writes to it yet.
2. ~~**Clash emits findings, alongside the snapshot.**~~ **Done.** Every clash run writes findings,
   and the Manage board reads its triage state from them: `Restored` carries only the custom column
   values now, and everything a coordinator decided is laid over the row from the finding after the
   read has been taken into the register. `HeldEdits` and `ClashAsides` folded in. The snapshot
   keeps doing its four jobs untouched, so nothing that works today broke.

   One rule fell out of the fold and is worth stating: **a run may not overwrite a field that is
   waiting.** A published NWD keeps reporting the status it had before anybody touched it, so a
   read that was allowed to win would revert the afternoon's work on every press of "Read the
   results" — the exact failure the hold exists to prevent. `MergeFrom` and `Observe` skip a field
   in `Pending`; everything else the run is still free to correct.
3. ~~**Issues attach findings.**~~ **Done.** `Issue.Findings` holds finding ids of any source — a
   clash, a clearance breach and a health failure can sit on one issue. `Issue.Clashes` is kept and
   still read, because a clash key names two elements and a place but not the test that found them,
   so nothing can turn one into a finding id on its own: the swap happens on the next read of the
   results, where both are in hand at once, and is idempotent so a key this run did not report
   waits for a wider one rather than being dropped.

   Raising an issue from the grid attaches findings; **Attach to an issue** puts picked results
   onto one that already exists, which is what stops a coordinator ending up with forty issues
   about one duct run. The issue's detail carries a findings card with **Take {n} off**, and a
   status change that implies something — closed means resolved, answered means reviewed — *offers*
   the cascade with its count. The middle states (draft, pending, in dispute) map to nothing: they
   are about the conversation, and mapping them would invent a claim about the building out of a
   note about a meeting.

   One finding may sit on two issues; one clash may not sit on two issues. The asymmetry is
   deliberate — the same duct through the same beam can be both a coordination issue and an RFI,
   but two issues each claiming one *conflict* is two people each believing the other is dealing
   with it.
4. **The snapshot's other jobs move across, one at a time** — carry-over, then the previous-run
   comparison, then the party history. Only when the last one has moved does the snapshot stop
   being written. Splitting this out is the correction: replacing it in one step was proposed on
   the belief that it held results and nothing else.
5. ~~**Clearance, IDS, health, set drift, compare.**~~ **Done, except set drift.** One per step, as
   planned:

   - **Clearance** kept its judgement on the row and carried it between runs *in memory*, so
     closing Navisworks reopened everything anybody had closed. Identity is the floor, what is over
     it, and where, quantised into a two-metre cell — wider than a clash's, because a clash is a
     point where two solids meet and a low soffit is an area somebody walks under. The minimum is
     deliberately not part of it: re-running at 2.4 m after 2.1 m finds the same low soffit, and
     calling it new would throw away a decision on the strength of a settings change.
   - **Health** had no persistence at all — it said what it found and forgot it. It is the first
     producer to use the parent and child `Finding` was shaped for: one parent per rule carrying
     the decision, one child per element carrying the location. Identity had to move off the prose
     first, because review headlines read "37 elements sit on top of each other" with the count in
     the words; `ReviewFinding` gained a stable rule id, set at every call site.
   - **IDS** could select failures and write a workbook and remember nothing. One finding per
     specification, identified by the file's own identifier so rewording it does not lose an
     agreement. A specification that applied to nothing stays reported as its own kind of failure:
     a requirement that quietly stops being checked is worse than one that fails, and a green tick
     is where it would go to hide.
   - **Compare** already produced findings through `CompareIssues` and never kept them, so an issue
     pointed at a record the button invented and dropped. Now they are observed into the register
     first and attached.

   `IssueOrigin` gained `Compare`, `Health` and `Ids`. It is written and read by name, so appending
   leaves every issue anybody has already raised reading exactly as it did.

6. **Set drift.** ~~One per step.~~ **Nothing to move.** `FindingSource.SetDrift` is a reserved slot
   and nothing else: there is no drift engine, no result type and no surface anywhere in the
   product. A set that stopped returning what it used to is a real check worth writing, but writing
   it is building a feature, not moving one onto findings, so it is not part of this work. The
   enum member stays where it is — numbered last but one, so the slot costs nothing.
6. **BCF for both.** Findings and issues out and back, with the relation between them.

Step 2 is next. Steps 4 and 5 are independent of each other.
