# Data Manager — product audit

The app: what the elements carry, and the pen to fix it. Six tabs: Properties, Write, Excel,
External Data, Levels & Zones, Breakdown. Walked as the people who actually open it: the BIM
coordinator who just got a spreadsheet back from the QS, the information manager filling a
naming-standard property across six models, and the coordinator who has to answer "which level
is this on" by Friday.

## Properties

**The job.** Mid-week, an element or a selection is behaving oddly in a filter or a schedule,
and the coordinator wants to see what it actually carries — every property, its values, and
its blanks — and then send the list of what is missing to whoever owns the model.

**What was wrong.**
- The read-result line said "40 elements, 200 properties" without saying *which* 40 — and the
  scope box could be flipped after the read, leaving the table and the writers below silently
  describing a different scope than the box claimed. The writers act on the keys that were
  read, so a coordinator who read a selection, widened the box and pressed Write believed they
  wrote "everything" onto forty elements.
- The find-and-replace ("Correct a value") asked for the property as bare text — the one field
  on the tab where a misspelled name silently corrects nothing.

**What changed.**
- The result line now names the scope it read from ("read from the current selection"), and
  flipping the scope box after a read posts a note saying the table and writers still hold the
  last read, and to read again to move them. Same machinery on the Write tab.
- The Correct-a-value property field is a `Pickers.Property` combo, fed from the model. A bare
  name still matches whichever tab carries it; the hint says so.
- (Existing hand-offs to Report Builder, External Data, Extract and Site Pack kept as-is.)

## Write

**The job.** The information manager on a naming-standard errand: put one property — typed,
computed, coded or from a sheet — onto everything in scope, without trampling a colleague's
Tuesday answers.

**What was wrong.**
- The target property name ("Name") and the code table's source property ("Read from") were
  bare text boxes. The property being written half-exists on the model already, and the code
  table's source is by definition a property the model carries — both were typed from memory.
- Same silent scope drift as Properties: the scope picker could disagree with what the writers
  held, with nothing saying so.

**What changed.**
- The writer's Name field and the code table's Read-from field are `Pickers.Property` combos
  (still editable — a name the bounded scan missed is still valid). Both keep the
  stale-preview behaviour: any edit, picked or typed, drops the diff.
- The rename/remove column field is a picker too — the column exists on the model or there is
  nothing to rename. The *new* name stays a box, deliberately.
- Read-result line states the scope; flipping the scope after a read says the writers did not
  move. The preview/commit two-step, single overwrite policy, and journal notes all survive
  untouched.

## Excel

**The job.** Tuesday: send every property out as a workbook for the QS. Thursday (often a
different person): read the edited workbook back. Two legs of one trip, days apart.

**What was wrong.**
- The tab had no memory. On Thursday nothing said which file went out on Tuesday, whether
  anything had come back since, or where to point the "Read back" box — the answer lived in
  someone's sent-items folder.
- The "Write it as" name for a returned column was a bare text box.

**What changed.**
- The tab now reads the activity log and states the trip: "The last sheet went out 2 days ago
  to C:\...\project properties.xlsx, and nothing has come back in since" (or when the edits
  came back, if they did). The out-leg's path is offered as the read-back path when the box is
  empty — the return leg starts pointed at the file that actually went out.
- "Write it as" is a `Pickers.Property` combo, so a returned column lands on the model's own
  spelling of the property, not the sheet author's.
- The identity-column contract, the unmatched/duplicate/blank row reporting, and the diff
  before any write are unchanged.

## External Data

**The job.** Somebody else's file — an asset register keyed on tag, a procurement extract
keyed on order number — joined to the model on a stated key, mapped column by column, written
either way, and saved to run again on next month's drop.

**What was wrong.**
- Each mapped column's "tab" and "property" — where existing values are read from and what the
  column lands on — were bare text boxes, per column. On a twenty-column register that is
  forty names typed from memory, and a mistyped tab silently reads nothing.
- (The key column itself was already fine: sheet headings come from the file, and the
  matched-against property is a picker fed from the elements that were read.)

**What changed.**
- The per-column tab and property fields are `Pickers.Tab` and `Pickers.Property` combos, fed
  from the model. The auto-propose flow, saved links, verdict, orphan-row table and both
  commits are untouched.

## Levels & Zones

**The job.** "Which level is this on?" — derive levels from the grid, the models' own
property, or the geometry; stamp Level and Grid onto every element; then actually *use* them.

**What was wrong.**
- The tab derived and stamped and then stopped. The two tools that read the stamped Level by
  name — one set per level (Sets Manager ▸ Generate Library) and quantities by level
  (Quantify ▸ Takeoff, whose Level axis prefers the CamelWorks tab) — live in other apps, and
  nothing here pointed at either. The coordinator asked "how much ductwork per floor" was left
  to know the suite's map by heart.

**What changed.**
- A hand-off block after the stamps: "Build a set per level..." opens Sets Manager's Generate
  Library, "Take off quantities by level..." opens Quantify's Takeoff, with one line saying
  both read the property that was just stamped. Honest buttons — they jump, they do not
  pretend to carry a mapping that the target derives itself.
- The three-source derivation, source picker, non-separating-grid warning, and all three
  stamps are unchanged.

## Breakdown

**The job.** Write the selection tree's shape onto the elements so schedules and spreadsheets
can group by the breakdown the model already implies.

**What was found.** Sound as-is: the recipe fields are genuinely free choices (prefix, depth,
separator — none derivable from the model), the two-press commit shows the branch table with
element counts first, and the same writer is shared with the Levels & Zones stage so the two
cannot drift. No changes.

## App status strip

Added a `Status("data")` provider (cheap reads only — activity log and project store):
- **last write** — when the pen last touched the model, amber "never" when it hasn't; opens
  the Write tab.
- **last sheet out** — the Excel round-trip's out leg, with the file path as the tooltip;
  opens the Excel tab.
- **saved links** — how many External Data joins are saved to re-run; shown only when there
  are any; opens External Data.

## Judged missing, out of scope

- **Read-back recorded with its path.** The Excel return leg records what it wrote but not
  which file it read, so "edits came back" cannot name the file. Wants a detail argument on
  that one `session.Record` call plus matching in the status line — small, but it touches the
  recorded activity shape other screens read, so left alone this pass.
- **A blanks-first view on Properties.** The table shows "N blank" per property, but the
  persona's real deliverable is "these eleven properties are missing on half the MEP model" —
  a sort/filter by blank count would need `Ui.Table` sorting, which is shared chrome.
- **Zone (volume) stamping.** The tab is named Levels & Zones and derives levels well, but a
  spatial zone (from named boxes or a zones model) has no engine — `LevelSet` is elevation
  bands only. Would need a zone-geometry engine in Core.
