# Clash Manager — product audit

**The app's job.** The clash week, end to end: say what gets tested, run it, refine the noise
away, decide what is left, and record what was agreed. A coordinator opens it Monday after the
new models land and lives in it until the meeting.

**No status strip.** Every app used to carry a band of figures across the top of every tab. It
cost a row of screen on every surface to say things the surface below it already said, and the
one figure that was not a repeat — what the board held — belonged to a tab that no longer
exists. It is gone from every app.

## Tests — "six models landed, run everything again"

- *Was wrong:* the run dead-ended on a note telling the user to go and find the results
  themselves — the product's most important hand-off, written as homework.
- *Changed:* a finished run offers **Open results**, and the Manage tab re-reads on arrival, so
  what it shows is the run that just finished.

## Manage — the results workbench (new)

The tab a coordinator coming from any other clash manager expects, measured against the ones
they actually use — the full gap analysis is in
[clash-manager-gap.md](clash-manager-gap.md). Tests on top with live per-status counts
(total, new, active, reviewed, approved, resolved), the picked tests' results under them, and
verbs on the picked rows:

- **Read** pulls every test and result out of the document — pumped per result, after the
  freeze the old board build hit at 18,844 results (fixed at the adapter: element names and
  categories now travel on the result instead of being re-resolved through a full federation
  index).
- **Focus / Isolate / Sphere** — camera and selection to the picked clashes, everything else
  hidden, or a status-coloured marker ball dropped at each clash point as ordinary geometry.
- **Mark / Assign / Comment / Group** — written back into the model's own clash data through
  the seam, so the native clash window, the exports and every report see the same thing.
- **Run / Rename / Move / Compact / Delete** on the picked tests, with delete behind a second
  press; Compact is the host's own drop-Resolved-from-storage, the only per-result delete any
  clash tool honestly has. Picked results rename too (prefix1, prefix2, ...), and every column
  keeps a manual drag - the auto-fit steps aside the moment a header gripper moves.
- Filters: status ticks, only-with-comments, test-name search, hide-empty-tests, and a
  contains-filter over any column - or one picked column, the property columns included -
  matching as you type. Every column of every grid sorts on a header click.
- **Send the picked tests out as a file...** jumps to Issue Link's Clash XML leg with those
  tests already ticked.
- **Property columns + chained grouping**: any element property as a results column (model-fed
  pickers, first/second/both items), and an ordered grouping chain over Test, Status, Assigned,
  host group and any property column — a group-summary band table with counts and bulk verbs
  that narrows the grid, all of it travelling into the spreadsheet.
- **Beyond the engine**: a cross-test duplicate finder (the same conflict reported by two
  tests, marked Approved with the keeper named), the grid written to a spreadsheet exactly as
  filtered, a full-fidelity baseline saved from the manager itself, the engine's own composed
  view and rendered snapshot per result, a sortable worst-distance column, and click-to-sort
  on every column of every table in the product.
- **The review round**, end to end: the tab has a second source — "A file" — where the same
  verbs mark up an exported baseline with no clash engine needed at all (the Simulate seat's
  whole job), "Save the markup" writes `markups\<reviewer>.xml` beside it, and on a Manage
  seat "Bring the markups into the model" reads everyone's markups back and writes their
  statuses, comments, assignments and groups into the model's own clash data. Re-running a
  sync never doubles a comment: the adapter skips an author repeating themselves word for word.

## The Board — removed

The board tab is gone. It was the app's oldest screen and the one that aged worst: a derived
list of groups that could be filtered and assigned but not exported, whose real output — the
issues somebody chases — had nowhere to live except inside it. What a coordinator works is now
two surfaces that each own their job: **Manage**, the results the engine holds, and the
**Issue Manager**, the register of everything raised on the job. The board itself survives as
the derived thing underneath the report, BCF, Clash XML and the merge — each of those tabs
builds it where it stands (**Load the board**, in place), so nobody navigates to fetch a
prerequisite any more.

Two things that lived on it moved rather than died: **Raise an issue** is now the Issue
Manager's own **New issue**, working on any elements rather than only beside a clash; and
**Review** — the one-finding-at-a-time palette — launches from the Manage tab's rail.

## Rules / Matrix

- Rules previews against the live board and its "try" lands where the board shows it.
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
  clash XML file — which the Manage tab's "A file" source opens, so a clearance list runs the same
  offline review round as a clash export.

## Freeze — the record tab

- *Was wrong:* a freeze recorded the agreement and stopped. The entire reason a freeze exists —
  the later argument — lives in Compare Track's Change Notice, and nothing said so. The freeze
  result now carries **When it changes, write the change notice here**, jumping there.

## Out of scope, named

- Auto-loading the board on tab-open for small result sets was considered and declined: the
  "nothing runs when a screen opens" law protects a half-million-element federation from a
  misclick, and the self-pressing hand-off from Tests covers the case where the user just asked.
