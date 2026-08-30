# Data Manager

> What the elements carry, and the pen to fix it.

**Tabs:** [Properties](#properties) · [Write](#write) · [Excel](#excel) ·
[External Data](#external-data) · [Levels & Zones](#levels--zones) · [Breakdown](#breakdown)

## Two things that hold across the app

**Every property name is a picker fed by the model.** A property name typed from memory is how a
write silently changes nothing and a check reports "nothing carries it" all morning. Every such box
is a combo offering what the model actually holds — still editable, because a name the scan missed
is still valid.

**Nothing is written until you have seen the diff.** Every write shows what it would do, element by
element, and the commit button names the consequence.

---

## Properties

> Browse every property the elements carry, and write the ones that are missing.

**Main action: Read properties.**

Every property folded to one table: name, value, and how many elements are blank.

The result line **names the scope it read from** — *read from the current selection* — and if you
change the scope box afterwards, it says out loud that the table and the writers still hold the
last read. That mattered: a coordinator who read a selection, widened the box and pressed Write
used to believe they had written onto everything.

*Correct a value* is a find-and-replace over what was read.

---

## Write

> Put one property onto every element in scope — a value you type, a formula, a code table or a
> spreadsheet.

**Main action: Read what is there now.**

Four sources, all on the screen at once rather than hidden behind a picker: **a value**, **a
formula**, **a code table**, **a spreadsheet**. All four write into CamelWorks' own property tab,
and none of them writes until a diff has been shown.

One overwrite policy governs all four — what happens when a value is already there is answered
once, not four times.

Columns can be renamed and removed. Any edit to the recipe drops the preview, so you can never
commit a diff that describes a different write.

---

## Excel

> Take the properties out to a workbook, let somebody who does not have Navisworks edit them, and
> read the edits back in.

**Main action: Write a sheet.**

Two legs of one trip, days apart — and the tab remembers the trip. On Thursday it says *the last
sheet went out 2 days ago to …, and nothing has come back in since*, and the read-back box starts
pointed at the file that actually went out.

- An **identity column** ties a row back to its element.
- Unmatched, duplicate and blank rows are **reported, not dropped**.
- A returned column lands on the model's own spelling of the property, not the sheet author's.
- The diff comes before any write.

---

## External Data

> Join a register, a schedule or an asset list onto the model by a key column, and write it back
> either way.

**Main action: Read the file.**

A wizard: read the file, state the key, map the columns, commit.

- Sheet headings come from the file; the property matched against comes from the elements read.
- Each mapped column's tab and property are pickers — on a twenty-column register that is forty
  names you do not type.
- **Orphan rows** — file rows that matched nothing — are shown as a table, not swallowed.
- The link can be **saved and re-run** on next month's drop.

---

## Levels & Zones

> Work out what level and what zone every element is on, from whatever the model already carries.

**Main action: Derive levels.**

Three sources — the coordination grid, the models' own level property, and the geometry — and it
keeps the most trustworthy answer that **actually separates the model**. A grid that does not
separate anything is reported rather than used.

Stamps **Level** and **Grid** onto the elements, and can write the breakdown at the same time.

Afterwards it hands off to the two tools that read what it just stamped:

- **Build a set per level** → [Sets Manager ▸ Generate Library](Sets-Manager#generate-library)
- **Take off quantities by level** → [Quantify ▸ Takeoff](Quantify#takeoff)

---

## Breakdown

> Write the selection tree's own shape onto the elements as properties.

**Main action: Show me what it would write.**

Writes `WBS1…WBS6` plus a joined key, so a schedule can group by the breakdown the model already
implies.

The recipe — prefix, depth, separator — is genuine free choice; none of it is derivable from the
model. The two-press commit shows the branch table with element counts first.
