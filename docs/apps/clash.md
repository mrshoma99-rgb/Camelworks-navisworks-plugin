# Clash Manager — product audit

**The app's job.** The clash week, end to end: say what gets tested, run it, refine the noise
away, decide what is left, and record what was agreed. A coordinator opens it Monday after the
new models land and lives in it until the meeting.

**No status strip.** Every app used to carry a band of figures across the top of every tab. It
cost a row of screen on every surface to say things the surface below it already said, and the
one figure that was not a repeat — what the board held — belonged to a tab that no longer
exists. It is gone from every app.

## Manage — the bench, redrawn

The tab a coordinator lives in, measured against the clash managers they actually use — the full
gap analysis is in [clash-manager-gap.md](clash-manager-gap.md). It was rebuilt twice: first for
what it could do, then for how it read.

- *Was wrong:* one table stacked on another, twelve one-word buttons under each, and a column
  down the right holding every option of every verb — the two tables the tab exists for had half
  the window between them, and nothing on the options column was findable.
- *Changed, to a prototype the user approved before a line was written:*
  **tests on the left, their results on the right**, a draggable split between them
  (`Bench.Split` / `Bench.Pane`, the product's second anatomy);
  **one search at the top over both tables** — it matched only a test's name before, which is why
  typing a result number or an element name found nothing and the box read as broken — with
  *Both / Tests / Results* saying what it covers;
  **the status ticks beside it**, because that is the one filter somebody changes every few
  minutes; **the verbs as symbols under the table they act on**, greyed until something is picked
  and tooltipped, plus the same verbs on **right-click**; and
  **every other option in a configuration window** (`Ui.Configure`) opened from the symbol that
  names it: Source, Filters, Columns and grouping, Status and assignment, In the model, The
  review round, Beyond the engine.
- The tests table's five status columns became one band — *new · active · closed* — which says
  the same thing at a glance and leaves the width for the name, which is the long part.

What the bench does has not shrunk: read from the model or from a file; run, rename, renumber,
move, compact, delete and copy tests; focus, isolate, mark, assign, comment, group, rename,
sphere, snapshot, raise an issue and copy results; property columns and chained grouping; the
duplicate finder, the spreadsheet, the baseline and the whole review round.

## Tests — removed

The tab listed the engine's tests and carried a matrix builder. The Matrix tab has been the place
to build a matrix since it was rebuilt, and the list of tests is the left half of Manage — so the
tab was two things somewhere else, and it is gone. `ClashMatrix`, the first matrix engine behind
it, went with it: `MatrixGrid` and `MatrixPlanner` superseded it, with a legend, three ways in
and an Excel round trip.

## The Board — removed

The board tab is gone. It was the app's oldest screen and the one that aged worst: a derived
list of groups that could be filtered and assigned but not exported, whose real output — the
issues somebody chases — had nowhere to live except inside it. What a coordinator works is now
two surfaces that each own their job: **Manage**, the results the engine holds, and the
**Issue Manager**, the register of everything raised on the job. The board itself survives as
the derived thing underneath the report, BCF, the clash file and the merge — each of those tabs
builds it where it stands (**Load the board**, in place), so nobody navigates to fetch a
prerequisite any more.

Two things that lived on it moved rather than died: **Raise an issue** is now the Issue
Manager's own **New issue**, working on any elements rather than only beside a clash; and
**Review** — the one-finding-at-a-time palette — launches from the Manage tab's rail.

## Filter Clashes / Matrix

- **Filter Clashes** (called "Rules" before, which said nothing about what it does) is the
  reduction between the engine and the issue list, in the order it runs: **suppress and flag**
  every raw result against a predicate, **group** what survives (empty means the built-in
  model-pair → level → proximity stack), then **assign** first-match-wins. Its one action is
  *Try these on the board* — it builds the board with the unsaved edits and shows the four
  figures the rules exist to move — and nothing is written until Save. It acts on clash results,
  never on issues: a rule that quietly closed something a person wrote a sentence about would be
  the worst behaviour in the product.
- **The matrix has three ways in**: by hand (categories picked from the model's own search
  sets, selection sets or whole models — never typed), read back out of the model's existing
  tests (each test's sides become categories, each distinct kind-and-tolerance becomes a
  lettered legend symbol), or the client's spreadsheet (mapping dialog, preview, and a sanity
  check that every category names a set this model actually has). One grid and one legend
  either way — symbol, test type, tolerance, on/off — a name pattern and a folder for the
  tests it creates, a per-kind report after creating, jumps to run or manage them, and the
  whole matrix exported back out as an Excel file (grid on one sheet, legend on the next).
  The matrix survives tab switches, and the legend's test type now actually reaches the host —
  it was silently Hard for every cell before.

## Clearance Check — the test no clash engine runs

The tab was called "Clearance", asked "what counts as a floor", offered *Floors and slabs* as an
answer, and printed a table nobody could do anything with.

- *Was wrong:* neither input could name a set. A coordinator who has spent the week building
  search sets was offered a category guess, the current selection, or everything — so the check
  could not be pointed at the corridors, and there was no way to say what "floors and slabs" even
  meant. **Both inputs now list every search set and selection set the document holds**, plus the
  current selection and everything loaded. The guess survives as one option, renamed *Walking
  surfaces (a guess)* and spelled out on screen: it takes anything whose category or type contains
  floor, slab, stair, landing, ramp, platform or deck. The second input's *Everything except the
  surfaces* now means exactly that — the elements the first input did not take — instead of a
  second silent category guess.
- *Was wrong:* the findings were a dead table. Clicking a row selected it and that was the whole
  vocabulary. **The findings now get the Manage tab's bench**: click-to-sort on every column, a
  contains-filter over any column or one picked column, status ticks and only-with-comments,
  an ordered grouping chain (status, assignee, group, either element, either category, how short
  it is, commented or not, and any property column) with a summary band table that narrows the
  grid, element-property columns read off the surface, off what is over it, or both, and bulk
  Focus / Isolate / Mark / Assign / Comment / Group / Sphere / Copy on the picked rows.
- *Held honestly:* the judgement lives on the rows, not in the model's clash data — no clash
  engine found these, so there is nothing there to write into. It survives a re-run instead:
  status, assignment, comments and groups carry over onto the same surface-and-obstruction pair,
  so re-checking after a model update does not reopen everything somebody closed.
- **Out of the tab:** the grid to a spreadsheet exactly as filtered, and every shown finding as a
  clash file — which the Manage tab's "A file" source opens, so a clearance list runs the same
  offline review round as a clash export.

## Freeze — moved out

Freeze has left this app for **Compare Track**, beside the Change Notice that reads it. It was
never clash-shaped: it records each element's box under somebody's name and hashes the lot, and
the only reason it lived here was that its element list used to be harvested from the board. Its
scope is now anything the model can name — the current selection, a search or selection set,
everything loaded, or the board when one happens to be loaded — so a level can be signed off in
a week when nobody has run a clash test.

## Out of scope, named

- Auto-loading the board on tab-open for small result sets was considered and declined: the
  "nothing runs when a screen opens" law protects a half-million-element federation from a
  misclick, and the self-pressing hand-off from Tests covers the case where the user just asked.
