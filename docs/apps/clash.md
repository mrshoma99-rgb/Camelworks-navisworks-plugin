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
- The matrix reads the client's own spreadsheet; the quick grid builder stays on Tests, where
  the Monday user is.
- Clearance is the test no clash engine runs; it lives beside the tests because it is one.

## Freeze — the record tab

- *Was wrong:* a freeze recorded the agreement and stopped. The entire reason a freeze exists —
  the later argument — lives in Compare Track's Change Notice, and nothing said so. The freeze
  result now carries **When it changes, write the change notice here**, jumping there.

## Out of scope, named

- Auto-loading the board on tab-open for small result sets was considered and declined: the
  "nothing runs when a screen opens" law protects a half-million-element federation from a
  misclick, and the self-pressing hand-off from Tests covers the case where the user just asked.
