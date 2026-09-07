# Clash Manager — the coordinator's workflow, and what to change

`docs/CLASH_MANAGER_REVIEW.md` graded the design system: tokens, spacing, contrast, whether a
dropdown reads as a dropdown. This document asks a different question — whether a coordinator
working this tool every day is helped or obstructed — and plans the work that follows.

Every claim below was checked in source. Where a claim from the earlier review turned out to be
softer than stated, that is said here rather than carried forward.

## What the audit does not cover

The earlier review covered the tabs. It did not cover, and no render exists of:

- **Five dialogs**: "How a test is named", "How rules are run", "Keyboard shortcuts", "Markers in
  the model", "What goes in each document".
- **Every menu in its open state**: the board's sort, column and filter menus, the row's
  right-click menu, the Assign dropdown, the check picker's popup, and the matrix's seven toolbar
  menus.
- **Every picker in its open state**: `Parts.Source`, `Parts.Ticked`.
- **Every prompt**: `Parts.Ask`, `Parts.Sure`, the comment editor, the issue raiser, the rename.

A coordinator's day runs through those constantly. Item 4 below closes the gap, and it is placed
early because everything after it is easier to judge once the menus can be seen.

---

## 1. Setting a status is the most frequent action and it is hidden

**What is wrong.** The status cell is inert text. Its own tooltip reads: *"New. Set it from the
row's menu, or pick rows and set it on the bar."* The assignee cell beside it — a far less frequent
edit — is a one-click `Parts.Presser`. The two most-used columns have their affordances inverted,
and the only thing that says so is a tooltip.

**The fix.** A second renderer beside the inert one:

- `FindingStatusWords.Chip(status, why)` stays exactly as it is, for Exchange's two tables and the
  counter strip, where a status is a fact and not an edit.
- `FindingStatusWords.Picker(status, onSet)` is new: the same geometry with the chevron the Assign
  control carries, opening the five statuses.

Manage's rows use the picker. Nothing else does.

**Blast radius.** One cell on the board's rows. Exchange, the counter strip and the naming screens
are untouched because they keep calling `Chip`.

**How this could make things worse.**

- *One shape, two meanings.* A chip that is pressable here and inert there is the exact fault this
  work has been removing. The chevron is what separates them, and it has to be present on one and
  absent on the other without exception.
- *The Status column grows.* A chevron adds roughly 14px to a chip with `MinWidth 76` in a 104px
  column. The board already fought its way out of a horizontal scrollbar twice; this could put it
  back.
- *Bulk ambiguity.* With twenty rows ticked, pressing one row's chip could plausibly mean one row
  or twenty. Either reading loses somebody's work.
- *Click collision.* The chip sits inside a row that already responds to click (select) and
  double-click (open). A press that falls through sets a status and opens the detail.

**The guard.**

- The rule is written into `FindingStatusWords`: the picker always carries the chevron, the chip
  never does, and the file says why.
- Bulk ambiguity is settled by decision, not by inference: **the chip sets its own row only**, and
  its tooltip says so. The verb bar remains the way to set many.
- The click handler marks the event handled, as the group-name cell already does.
- The width is settled by a render of `app-clash-manage` and `app-clash-manage-wide` before and
  after, read for a horizontal scrollbar — the same gate that caught it the last two times.

---

## 2. There is no keyboard triage

**What is wrong.** The board has no key handling at all — no `KeyDown`, no `InputBindings`. Working
300 results means 300 rounds of mouse, right-click, menu, click. Navisworks' own clash window does
better, and this is the single largest daily cost in the tool.

**The fix.** Key handling on the results table, active only when the table itself has keyboard
focus:

| Key | What it does |
| --- | --- |
| `↑` `↓` | Move the current row |
| `Space` | Tick or untick the current row |
| `1`–`5` | Set the current row's status, in lattice order |
| `F` | Focus the model on the current row |
| `Esc` | Clear the selection |

A "Keyboard shortcuts" sheet, built from the same table, reachable from the board's tool row. The
matrix already has one; this reuses the shape.

**Blast radius.** The Manage tab's results table. Nothing else gains key handling.

**How this could make things worse — and this one is genuinely risky.**

- **A status change is not fully held.** I checked: `Mark` → `Write` updates the in-memory register
  *and echoes to the host where the check has a `HostEcho`*; only the file write waits for "Save
  changes". So a stray `3` is a real edit to the Navisworks document, not a pending one. This is
  the reason to be careful rather than a reason not to do it.
- *Keys stolen from the search box.* Typing `3` into "the board" search must type a 3. This is the
  classic way a feature like this ships broken.
- *Meaning drift.* If `FindingStatusLattice` ever reorders, `1`–`5` silently mean different things
  and the shortcuts sheet lies.
- *Virtualisation.* Holding `↓` through 96,000 rows must not realise all of them; the repo has
  `validate-board-virtualised.py` because that has hurt before.

**The guard.**

- Handled on the ListView's `PreviewKeyDown`, never the window, and returning immediately when
  `Keyboard.FocusedElement` is a `TextBox` or an editable `ComboBox`. That single rule covers the
  search box, the inline editors and any IME.
- **No destructive verb gets a bare key.** No delete, no remove, no run. The five status keys and
  Focus are all recoverable by setting the status back; nothing else is bound.
- The key map is derived from `FindingStatusLattice.All`, and the shortcuts sheet is generated from
  the same list, so the two cannot disagree. A new check, `validate-shortcuts.py`, asserts every
  status has exactly one key and no key is bound twice — that is testable without WPF, which
  matters because nothing else here is.
- Movement uses the ListView's own selection, which respects virtualisation; the existing check
  keeps that honest.
- A harness page draws the shortcuts sheet, so it is reviewed rather than assumed.

---

## 3. The filter panel covers the list it filters

**What is wrong.** The rail's filter panel is a card drawn over the test rows. You cannot see what
your filter does while you set it. It gained a close button this session; the overlay itself is
still the fault.

**The fix.** The panel docks above the list instead of over it: the list shortens and stays visible,
and the panel scrolls internally past a capped height.

**Blast radius.** The Manage tab's rail.

**How this could make things worse.**

- *The list jumps.* Content shifting under the cursor as the panel opens is worse than the overlay
  for a user mid-scroll.
- *Less list.* On a project with forty tests, giving 200px to chips leaves very little.

**The guard.** The list's scroll position is preserved across the open and close. The panel's height
is capped and scrolls inside itself, so the list never drops below a usable minimum. Rendered open
and closed, both read.

---

## 4. The harness cannot draw menus, dialogs or pickers

**What is wrong.** See the top of this document. It is why the earlier review's "every renderable
surface" was true and misleading.

**The fix.** The pattern already exists and is proven: `CheckPicker.Entries` was split out from
`CheckPicker.Draw` precisely so the harness could draw the list without a popup. Apply it to the
rest — the board's menus, the matrix's toolbar menus, the two pickers — and add harness pages for
the five `Ui.Configure` dialogs, which are already plain panels behind a window.

**Blast radius.** Additive. No product behaviour changes; some builders are split in two so the
content can be built without its container.

**How this could make things worse.**

- *A split that drifts.* If `Draw` and `Entries` can be edited independently, the harness draws
  something the product does not.
- *Harness-only API.* Methods that exist solely to be photographed rot, and `validate-reachable-screens.py`
  will flag them if they are ever orphaned.

**The guard.** `Draw` must call `Entries` — never build its own list — so the two cannot diverge;
that is how `CheckPicker` is already written. `validate-reachable-screens.py` covers the orphan
case.

---

## 5. Roughly eight unlabelled icon controls on the Manage tab

**What is wrong.** Four top-right of the results pane, two on the rail, two on the rail's foot, plus
three more on the Levels editor. The review's own rule — "two symbols beside a primary say nothing
about what they open" — was applied to Exchange and Report and not here.

**The fix.** Not labels on the strip, which the row has no width for. Each cluster becomes a
`Parts.MoreMenu` with one glyph and labelled items inside, which is what the sort and filter
controls on the same row already do.

**Blast radius.** The Manage tool row and the Levels verb bar.

**How this could make things worse.**

- *A press becomes two.* Burying a one-click view toggle in a menu costs a daily user a click every
  time.
- *The row gets wider, not narrower*, if done carelessly.

**The guard.** Only clusters go into menus; a control used constantly keeps its place on the strip
and gains a label if it fits. The judgement of which is which is made from the renders, not from
the code. Rendered before and after.

---

## 6. "Filter" is the name of a tab that deletes from the document

**What is wrong.** The rules on that tab assign, set status to Approved, group, take off the board,
and **delete from the Navisworks document**. That is an automation engine with write access,
named after a read-only operation.

**This one is yours to decide, and I am not doing it unilaterally.** The tab was deliberately
renamed *from* "Rules" *to* "Filter" in earlier work. Reversing that reverses your decision, and I
have no evidence the new name is worse for you — only that it does not describe what the tab does.

**Two options.**

1. **Rename it.** "Rules" or "Automation". The route id stays `rules`, so no cross-tab link breaks;
   only the label changes.
2. **Keep the name and make the write visible.** The delete action gets the danger treatment and a
   confirmation, and the tab's opening line states plainly that rules write to the document.

Option 2 is available immediately and does not need your decision. Option 1 does.

**How either could make things worse.** A rename costs muscle memory and touches the wiki, which I
am not to update unless asked. Making the delete louder risks making a legitimate action feel
unsafe.

**The guard.** `validate-capability-map.py` and `validate-reachable-screens.py` cover the wiring
either way. The route id does not move.

---

## 7. Three grouping concepts, one screen

**What is wrong.** Folders (the host's), groups (the host's clash groups) and bands (ours, for
display) all group results, and only one of them says which it is.

**The fix.** Naming only. The Settings section headed "Bands" becomes "Board grouping", which is
what its own subtitle already says it is.

**Blast radius.** One rail row and one heading.

**How this could make things worse.** Very little — but a longer label in a 190px rail could wrap.

**The guard.** Rendered.

---

## 8. Renumber is three levels deep and writes to the host at once

**What is wrong.** Settings → Test naming → Renumber writes to the Navisworks document immediately,
while everything else in Settings holds until "Save the settings". A destructive host write behind
two navigations, under a button that promises otherwise.

**The fix.** Not to buffer the write — buffering host writes to make a promise true is how data goes
missing. Move the tool to where actions live: the Create tab's test list, as a bulk verb over the
selection, with the `Ui.Plan` preview it already has.

**Blast radius.** One tool moves between tabs. The naming settings stay in Settings; only the act of
renumbering moves.

**How this could make things worse.**

- *Muscle memory and the wiki*, which I am not to touch unless asked.
- *A bulk verb over a selection changes what gets renumbered* — currently it is all tests, and a
  selection-scoped version is a different tool.

**The guard.** The scope stays "the tests the preview lists", not "the selection", so the behaviour
is unchanged and only its position moves. The `Ui.Plan` preview already shows every affected row
before the press, and that render exists.

---

## What this plan deliberately does not do

**Unify the four save contracts.** Create saves as you type, Filter and Manage and Settings each
hold until a press. I called this an inconsistency, and it is — but a triage session you can abandon
and a settings edit you must confirm are genuinely different transactions, and forcing one shape on
both would make one of them worse. The right fix was to make each contract legible, which the
footer's save line now does. Left alone.

**Remove the "Read the results" gate.** Reading a large federation is expensive and a tab that did
it on open would hang. Left alone.

**Unify the run verbs.** I said four verbs for one idea. Checking them: "Build the tests" builds
tests rather than running them, and "Run all rules" runs rules. They are correct. What is actually
wrong is the unlabelled play glyph on the rail's foot, which is item 5. My original claim was
overstated.

---

## Order

1. The status picker (1) — most frequent action, smallest change.
2. The harness gap (4) — everything after is easier to judge once menus can be seen.
3. Keyboard triage (2) — largest daily win, largest risk, wants the guards above in place first.
4. The filter panel (3).
5. The unlabelled clusters (5).
6. Grouping names (7), renumber's home (8), and whichever option you pick for the Filter tab (6).

Each step is verified the same way: the full local battery, then a render of the surfaces it
touches, read before the next step starts.
