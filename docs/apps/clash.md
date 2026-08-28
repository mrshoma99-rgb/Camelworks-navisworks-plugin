# Clash Manager — product audit

**The app's job.** The clash week, end to end: say what gets tested, run it, refine the noise
away, decide what is left, and record what was agreed. A coordinator opens it Monday after the
new models land and lives in it until the meeting.

**The status strip** (on every tab): how many tests and results, when anything last ran, what
the board holds and its delta — each figure jumping to the tab that owns it. This is the app's
own answer to "where does my week stand", visible before any tab is read.

## Tests — "six models landed, run everything again"

- *Was wrong:* the run dead-ended on a note telling the user to go and load the board
  themselves — the product's most important hand-off, written as homework.
- *Changed:* a finished run offers **Open the board with these results** as its primary; the
  Board tab presses its own load once, on arrival, because the user asked for that board the
  moment they pressed Run (`LoadOnArrival`).

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

## Board — the decide tab

- *Was wrong, badly:* the loaded board **did not survive leaving the tab**. A glance at Rules
  cost the whole triage view and a full reload. The board now hydrates from the last load's own
  object on every return — no host read, no rules re-run.
- *Was wrong:* Review — the one-finding-at-a-time palette — lost its ribbon button when the
  suite went flat and became reachable only through search. It now launches from the board
  (**Walk them in Review**), beside the undo.
- *Held:* assignment was already roster-fed (a picker over the project's parties), statuses are
  the five native values, and hand-grouping, manual issues, freeze-aware banners and undo all
  survive.

## Rules / Matrix / Clearance

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
- Clearance is the test no clash engine runs; it lives beside the tests because it is one.

## Freeze — the record tab

- *Was wrong:* a freeze recorded the agreement and stopped. The entire reason a freeze exists —
  the later argument — lives in Compare Track's Change Notice, and nothing said so. The freeze
  result now carries **When it changes, write the change notice here**, jumping there.

## Out of scope, named

- Auto-loading the board on tab-open for small result sets was considered and declined: the
  "nothing runs when a screen opens" law protects a half-million-element federation from a
  misclick, and the self-pressing hand-off from Tests covers the case where the user just asked.
