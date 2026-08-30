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

### Drift to absorb while we are here

The clash section of the project file has become a junk drawer of thirteen keys: `snapshot`,
`rules`, `pinned`, `issues`, `issued`, `freeze`, `decisions`, `bcfTopics`, `bcfGroups`,
`assignments`, `sheetIssues`, and the two added recently (`heldEdits`, `asides`). Eleven of them
are written by `CoordinateViews` — the old Board, which was retired in favour of the Issue Manager.
There is a `clash/issues` key *and* a separate `issues/register`: two issue stores.

Any migration should establish which of those are live and delete the rest, rather than carrying
them forward.

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

The clash row deserves its reasoning written down, because it contradicts the existing
`ClashKey`. `ClashKey` includes a quantised cell — the clash *point* — so a conflict that shifts a
few millimetres between runs keys differently. That is right for matching one run against another
where position is evidence, and wrong for a finding's identity, where it would resurrect a
conflict somebody already closed. The BCF topic id already made this choice (it derives from
`PairId`, which excludes the cell) and the two should agree. **A clash finding's identity is the
element pair and the test, and not where the clash is.**

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
  finding rather than being mashed into a comment. `ClashAsides` already does exactly this for
  clashes and is the shape to generalise.

## Open questions

1. **Scale.** A unified store across every check on a large federation is real weight in JSON.
   Parent/child absorbs most of it — one IDS rule is one parent and N children, not N findings —
   and `ClashSnapshot` is precedent for tens of thousands of rows. It wants measuring before it is
   assumed. If it does not hold, the fallback is one file per source under `.camelworks/` rather
   than one section in the project file.
2. **Do clash findings replace `ClashSnapshot`, or sit beside it?** Replacing is the point of the
   exercise. Sitting beside it means two stores that can disagree, which is the failure this is
   meant to end. The migration should read the existing snapshot once and write findings from it.
3. **`HeldEdits` and `ClashAsides` are clash-shaped versions of things every source will want.**
   Held edits are "what the host refused"; asides are "what the host has no field for". Both belong
   on `Finding` once findings are the record. They should be folded in, not left as clash-only
   registers.
4. **Does a parent finding project to the host at all?** A health rule failing 400 elements has no
   clash result to write to. Probably the projection is per-source and some sources project
   nothing, which is fine — but it should be stated per source rather than discovered.

## Order of work

Each step ships working and verifiable on its own.

1. **The store.** A findings section in the project file; `Finding` gains JSON; a register with
   merge-by-id. No producer changes yet. Testable end to end without a host.
2. **Clash onto findings.** The producer with the most at stake and an existing store to migrate
   from, so the safety net is real. Fold in `HeldEdits` and `ClashAsides`. Delete the dead keys
   from the clash section.
3. **Issues attach findings.** `Issue.Clashes` becomes finding ids; the attach and raise verbs
   land on the clash results grid; cascade with the prompt.
4. **Clearance, IDS, health, set drift, compare.** One per step. Each is a producer emitting
   findings and a window gaining the two verbs.
5. **BCF for both.** Findings and issues out and back, with the relation between them.

Step 1 is the only one that must come first. After that the order is negotiable.
