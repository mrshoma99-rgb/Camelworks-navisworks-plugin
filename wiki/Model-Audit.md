# Model Audit

> Whether this federation is fit to coordinate against.

The manager's question before anybody coordinates. A fresh drop of models lands on Monday morning;
this is the app that says whether the week can start.

**Tabs:** [Health](#health) · [IDS](#ids) · [Models](#models) · [Broken Links](#broken-links) ·
[Project](#project)

---

## Health

> Say whether this federation is fit to coordinate against, before anybody spends a week
> coordinating against it.

**Main action: Check this federation.**

One button, one verdict, worst first. Placement, geometry, what has arrived and what it is coded
as — all in **one traversal**, because each pass over a real federation is minutes and there is no
reason to ask four times.

Each finding carries **its own fix** and a *show me* that selects the elements it is about:

- Elements away from the origin → put the placement back.
- A model whose source file is missing → hands off to [Broken Links](#broken-links), which is the
  tab that can actually bring the found file in.
- Nothing carries the code → the property picker is fed by the model, so you pick a name that
  exists instead of typing one from memory.

Which property carries the code lives behind the ⚙ symbol on the action row.

### The client's rules

The rail carries a second run with a second file: **an IDS** the client sent. It is checked against
the federation and the answer lands on the same stage.

IDS speaks IFC and a federation does not, so every guess the bridge makes is **reported on screen**
rather than folded silently into the result. Where a failure is a *property* failure, the result
offers the jump to [Data Manager ▸ Write](Data-Manager#write) — while saying honestly that this
fixes the federation here, and the authoring model still needs the same fix.

---

## IDS

> Write down what every model must carry, as the open standard for saying so.

**Main action: Save as an .ids file.**

The specifications lead the screen. Each is two lists: which elements it is about, and what they
must have. The verdict under the form is live as you type.

- **Property set and property names are model-fed pickers**, still editable — because a requirement
  the federation does not yet meet is a perfectly legitimate thing to write down.
- The draft is kept in the project **as IDS itself**, so what the project holds is already a file
  another tool can read.
- What the document is called — title, version, author, description — is behind the ⚙ symbol.

Saving offers **Check the federation against it**, which lands on Health with the path already
filled in.

---

## Models

> Which revision of what is loaded, and what moved since the last manifest.

**Main action: Take a manifest.**

The Monday answer to *which files is this board against?*

- The federation is **on the tab when you arrive** — every loaded model, with its source marked
  found or missing. Missing ones hand off to Broken Links.
- Element counts come from **one pass over the geometry**, not from the model headers: a model
  appended twice reports its own count twice, and this register's whole job is to be the thing you
  can trust when the geometry disagrees with the file name.
- Taking a manifest records what is loaded now and reports **what changed since the last one** —
  republished, added, or no longer loaded.

If nothing has been republished since the last manifest, it says so: a clash run against exactly
these files produces exactly last week's board, which is worth knowing before the meeting rather
than during it.

---

## Broken Links

> Find the source files a loaded model can no longer see, and point it at where they went.

**Main action: Look for them.**

A model whose source has moved still displays — Navisworks shows its own cached copy, with no
warning anywhere. Everybody coordinates against it for a fortnight and then discovers the geometry
is from November.

The missing models are a table. *Look in* is prefilled with the folder the healthy models live in,
which is where a republished file usually landed. The search fills the same table in place, and the
picked rows can be **brought into the federation** or **copied out** as columns for an email.

### What Navisworks will not let a plug-in do

There is no API to re-point an existing model at a new path, and none to remove a single model. The
document exposes append, merge and open — nothing else.

So bringing the right file in **adds it alongside the stale one**. Remove the stale one in the
Selection Tree and save the federation, and the link is properly fixed. Until you do, everything in
CamelWorks reads what is loaded — including the stale copy. The tab says all of this rather than
pretending to a repoint it cannot make.

Afterwards it offers **Check the federation**, which is the question that sent you here.

---

## Project

> Everything CamelWorks worked out about this job on its own, where each answer came from, and an
> override on every line.

**Main action: Save overrides.**

**Nothing here is required.** Every row is something CamelWorks derived from the models, with its
reasoning in the tooltip and a box to overrule it. *Reset all* puts every row back to what was
derived — a register you can only override is one people are afraid to touch.

There is a **search** over the register, because it runs to thirty-odd rows and you are usually
after one of them by name.

Behind the ⚙ symbols at the foot: **who a clash can be given to** (parties and priorities),
**code tables**, and **the roster**. These are the project's own inputs — names pasted from a
client spreadsheet — rather than anything the model knows better.

At the bottom of the stage: what CamelWorks has written into the model, and how to take it back
out. The project file's own path is on the same screen, along with any problem reading it.
