# Sets Manager

> The selections everything else works on.

The set is the suite's common currency — [Appearance](Scene-Kit#appearance) colours by it,
[Takeoff](Quantify#takeoff) measures it, [Extract](Export-Kit#extract) cuts it — so this app is
where a coordinator's week starts.

**Tabs:** [Builder](#builder) · [From a Schedule](#from-a-schedule) ·
[Generate Library](#generate-library) · [Organise](#organise)

Everywhere a property tab or name is asked for, the box is a **picker fed by the model**. The exact
spelling is the whole difference between a set and an empty set.

---

## Builder

> Build the selections every other tool works on, out of properties and other sets.

**Main action: Publish as a Navisworks set.**

Rules are AND groups OR'd together. Each group takes:

- **Property conditions** — tab, property, operator, value. The value stays typed: it is often a
  fragment (`contains "duct"`) that no scan would offer.
- **"and in a set…"** — a reference to another set, picked from the document's own sets. So *all
  ductwork above level 3 that isn't in the riser* is one rule, not a manual subtraction.
- **"elements like this one"** — reads what is selected and finds everything that matches it.

**Select what it catches** shows you the answer before you publish anything.

Saved rules live in the **project file**, so whoever opens the job next has them. The saved list
carries verbs — copy, delete — and clicking a row loads it back into the builder.

After publishing, it offers the three tabs whose first question is *which set*: Appearance,
Takeoff, Extract.

---

## From a Schedule

> Turn a spreadsheet of rooms, assets or conditions into that many sets at once.

**Main action: Read the schedule.**

The day the client's set schedule lands. Read it, see what each row would make, rehearse the
counts, publish forty sets in one press.

It handles both layouts a project actually keeps — a row per set, or a sheet of set definitions.
The row and column numbers stay typed, because they are read straight off the sheet preview sitting
beside them.

Afterwards: colour them in Scene Kit, or take quantities off them in Quantify.

---

## Generate Library

> Turn an ordered list of properties into a whole tree of sets at once — discipline, then level,
> then system.

**Main action: Read the model.**

Project setup in one pass instead of an afternoon in the Builder.

Properties are **read out of the model and ticked**, in nesting order, with the count of distinct
values beside each — so you can see that *System* has 30 values before you generate 30 branches.
The name pattern and the floor are templates and numbers.

**Rehearse** before publishing: it says what the tree would be, and catches the case where a rule
matches nothing.

---

## Organise

> Reshape the set library into the folders the properties imply, rather than the order they were
> made in.

**Main action: Read the model.**

The federation arrived in exporter shape; the project is run in zones, packages and systems. This
rebuilds the selection tree in the shape the job is actually coordinated in.

A wizard: the recipe (levels, outermost first), the tree it would make, then publish. On the next
revision it also shows **what moved since the last run**.
