# Export Kit

> The model as a file somebody else opens.

**Tabs:** [IFC](#ifc) · [GLB](#glb) · [Drawing](#drawing) · [Extract](#extract) ·
[Compile](#compile)

Every tab reads the same **scope** — the current selection, a set, a level, or everything loaded —
chosen once for the app rather than re-chosen on each tab.

---

## IFC

> Write the model out as IFC, with the mapping and the classification it should carry.

**Main action: Open the IFC exporter.**

This tab is deliberately a **door** to the BIMCamel IFC exporter, which is a separate plug-in with
its own ribbon tab, installed separately. Duplicating its mapping form here would guarantee the two
disagree one day.

If the exporter is not installed, the tab says so and where to get it.

---

## GLB

> The geometry as one file a browser opens, on a phone or a tablet, with nothing installed and no
> licence at the other end.

**Main action: Write the GLB.**

The weekly file for the client. **No properties travel** — if somebody is going to ask for a
property, they want IFC, and the tab says so before you send the wrong thing.

- One colour per discipline, so services read against structure.
- Elements with no geometry, and anything over the ceiling, are **counted and reported**, not
  silently dropped.
- The path is suggested beside the document.

---

## Drawing

> The geometry as a DXF every CAD package opens, one layer per discipline.

**Main action: Write the drawing.**

For when the structural engineer wants the forty selected pipes in AutoCAD today, with no recipe
set up first.

R12 ASCII, stated plainly. Layers derived from discipline or category. The element's name and model
travel as XDATA; the tab says why more is refused.

For a piece chosen by a **rule** rather than by eye, use [Extract](#extract) instead.

---

## Extract

> Cut a piece out of the federation — a level, a zone, a room, a view — as a set, a drawing or a
> model somebody else can open.

**Main action: Read it.**

*Level 03 mechanical for the fit-out contractor* — chosen by a rule that re-runs next revision, not
a selection made by eye.

A wizard in dependency order, with **every choosing axis fed from the model** — the levels it
found, the models loaded, the property values it holds (filtered to the ones with a sensible number
of distinct values). The per-model table is pickable, and the output folder is remembered per
project.

**It cannot write part of a document to NWD** — Navisworks offers plug-ins no API for it. The
honest workaround is on the tab: **Make it a set**, then use Navisworks' own export.

---

## Compile

> Append a list of models into one deliverable, newest revision of each, with a log of what went
> in.

**Main action: Compile.**

Friday's federation out of the delivery folder. It reads the drops, lets the **newest revision
win**, and shows two tables: what is going in, and what is being left out. The left-out table is
pickable — it is the email to the supplier.

Taking the session over is destructive, so it keeps its two-step and states the cost first.

---

## Data Export

The same surface as [Quantify ▸ Data Export](Quantify#data-export) — the properties you name, as a
spreadsheet or CSV, one row per element. Two doorways to one engine, because it is both a data job
and a delivery job depending on who is asking.

The property tab and the column list are model-fed, and both are **remembered per project**, so
Friday's five columns are not retyped every Friday.
