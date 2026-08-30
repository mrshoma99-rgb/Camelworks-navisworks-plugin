# Quantify

> Numbers out of the model, the way a bill wants them.

Without asking anybody to map anything first.

**Tabs:** [Takeoff](#takeoff) · [Report Builder](#report-builder) · [Data Export](#data-export)

---

## Takeoff

> Sum the quantities the model carries, grouped how a bill wants them, and say what could not be
> read rather than dropping it.

**Main action: Sum it.**

Pick the **scope**, the **measure** to sum, and one or two **axes** to group by — rows, and
optionally columns. Leave columns empty for a flat list.

- The measure is read off whatever the elements actually carry.
- **What cannot be read as a quantity is counted and named, never dropped.** A total that quietly
  omits four hundred elements is worse than no total.
- Provenance is stated on every run — where each number came from.
- The totals table is pickable, and the sheet it writes is the same table you are looking at.

### If the values need tidying first

Behind the ⚙ symbol: a pattern that takes the number out of a string (`150mm dia` → `150`), and
what to read a unitless value as.

**Nothing here invents a number.** A pattern that matches nothing leaves the value alone, so the
takeoff still counts it as unreadable and names it — turning what it could not clean into a zero
would move the failure out of a line that admits it and into a total that is quietly wrong.

### A takeoff by Level, on a model with no levels

The `(none)` row comes with its count and a one-press jump to
[Data Manager ▸ Levels & Zones](Data-Manager#levels--zones), which derives and stamps them. The
Level axis reads that stamp first, so *quantities per floor* is a two-press workflow rather than a
support question.

The spreadsheet lands **beside the project file** by default — a takeoff the rest of the team
cannot see is one the next person re-runs.

---

## Report Builder

> Build a report of model data on a template it proposes from the model itself.

**Main action: Read the model.**

Four steps in dependency order. The model is read once, and the proposal comes from what it
actually carries rather than from a template somebody guessed.

- The **property catalogue** is a table; pick rows and *Add N as columns*, which says how many.
- The **preview cannot disagree with the file** — it is built from the same table that gets
  written.
- Templates are saved to the project, so next month's report is the same report.
- **Fillable columns** make it a round trip: send the report out, get it back filled in. The write
  hands off to [Data Manager ▸ Excel](Data-Manager#excel), which starts pointed at the file that
  just went out.

---

## Data Export

> The properties you name, as a spreadsheet or a CSV, one row per element.

**Main action: Write it.**

The raw-columns escape hatch, for when the shape somebody wants is *just give me everything*.

The same surface appears in [Export Kit](Export-Kit#data-export) — same engine, same memory, two
doorways, because it is both a data job and a delivery job depending on who is asking.

---

## What this app does not do

**Rates and money.** A bill line is quantity × rate; CamelWorks stops at the quantity. A wrong
currency figure is worse than none.

**Takeoff history.** Each run overwrites the screen. Comparing this week's concrete to last week's
would need takeoffs kept the way baselines are; the activity log keeps the one-line summaries,
which is a record but not a comparison.
