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

### Sets that have gone quiet

Every sweep also reads what each saved set returns, and compares it with what it returned last
time. This is the sharpest way a federation can stop being fit to coordinate against, and the
easiest to miss.

**Nothing errors when a set goes quiet.** A search set is a saved query, so what it returns moves
whenever the model does — a discipline re-exports, a property is renamed, a parameter mapping
shifts — while the definition still reads exactly as it did the day somebody wrote it. Then:

- a clash test whose side is that set finds **no clashes**;
- a clearance check over it finds **nothing too low**;
- a scoped IDS rule reports **not applicable**.

Every one of those reads as green. A board going green over a building nobody checked is the one
outcome this product exists to prevent, and this is the side door it comes through.

**Two things are faults**, because they disarm rather than merely narrow: a set that **returns
nothing** when it used to return something, and one the **document no longer has**. A set that lost
a quarter of itself is a watch. Growth, and losing a few, are ordinary on a live model and are not
reported — a check that flagged every change would be a check nobody reads, and the two that matter
would hide in it.

**Each finding says what it disarmed.** "The Level 3 Ductwork set returns nothing" is a curiosity;
*"…and clash test MEP vs Structure and rule Ducts carry a system name are pointed at it, and now
cover nothing"* is a morning's work before anybody trusts another green board. Clash tests and
scoped rules are named because CamelWorks can read them; a clearance input or an appearance layer
lives in one tab's memory and is not claimed, because a half-answer beats a wrong one.

**Show me what left** selects the elements that are no longer in the set — the whole diagnosis. A
set that lost its ductwork because somebody deleted the mezzanine is a different morning from one
that lost it because a property was renamed, and only looking at them tells the two apart.

Two things worth knowing:

- **The first sweep has nothing to compare against.** It reads every set, keeps what they hold, and
  says so. Drift is measured from the sweep after it.
- **A drift is said once.** Each sweep becomes the new baseline, so a set that emptied is reported
  on the sweep that finds it and not on every one after. The finding keeps the record.
- A set is addressed **by name**, so a set that was renamed reads the same as one that was deleted.

### The sweep remembers what you decided

Every sweep writes its findings beside the model, so each card also carries:

- **Settle it** — yes, we know, and we have accepted it. The next sweep still finds it and still
  says so, but it says you have accepted it. Press it again to reopen.
- **Raise an issue** — the card becomes a conversation in the [Issue Manager](Issue-Manager), with
  the finding attached and the elements on it.

A finding about *models* rather than geometry gets both too. "This model was moved" has nothing to
select and is exactly the kind of thing somebody accepts once and does not want asked again next
Monday.

The count in a headline is not what identifies a finding — "37 elements sit on top of each other"
and next week "41" are the same finding with the same decision on it. The rule is what identifies,
so a sweep that finds one more never throws away what you said about the rest.

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

### It works on models that never saw IFC

IDS says who a rule is about with an IFC class — and a Navisworks federation is a pile of NWCs from
Revit, Tekla and Civil 3D where most elements have no IFC class at all. On that model an ordinary
IDS matches nothing and reports *"not applicable"* for everything, which is useless and reads as
green.

So **"It applies to"** offers a **set in this model** beside an IFC class. Point a rule at a search
set or a selection set and everything else about IDS works unchanged — the requirements were never
IFC-bound, only the question of who they were about.

- **How many is that?** on the row says what the set holds *right now*. Worth pressing: a set named
  correctly and holding nothing looks exactly like a rule that works, until the day it reports
  nothing.
- **What is selected when it runs** is offered too, and it means what it says — it cannot mean the
  same thing twice. Good for a check you run now; name a set for one the project keeps.
- Rules mixed freely: some by IFC class, some by set, one run, one report.

### Two ways to save, and the choice is yours

IDS's applicability is a fixed list of IFC facets with no room for anything else, so a rule scoped
to one of your sets has nowhere to be written in one. That is a question about **who the file is
for**, so it is asked at the moment of saving rather than decided for you. A line above the buttons
says what each would cost *before* you press either.

| | **Save IDS...** | **Save BIMCamel rules...** |
|---|---|---|
| Extension | `.ids` | `.bcids` |
| Who reads it | Solibri, BIMcollab, anything | CamelWorks |
| Scoped rules | left behind, **each named with its reason** | all carried |
| Validates against the published schema | yes | the IDS inside it does |

A `.bcids` is a **zip** holding `rules.ids` — a real IDS with every portable rule in it — and
`camelworks-rules.json` with the rest. Somebody with none of this can rename it, unzip it, and open
the IDS inside; the degradation path is a real file rather than a shrug. Nothing hides, and nothing
pretends to be a standard it is not.

**Save IDS** refuses outright when *every* rule is scoped — an IDS that opens and checks nothing
reads as a delivered requirement, which is worse than not sending one.

Either kind can be opened again by **Rules from ▸ An IDS file**, told apart by what the bytes are
rather than what the file is called: a rule set renamed on the way through an email is still the
rule set it was.

### Sharing one rule set across several models

This is what the `.bcids` is for. Send it to a colleague, or run it against three models yourself.

The thing to know: **a scoped rule names a set by name.** On another model it applies to whatever
*that* model's set of that name holds — which is usually exactly what you want, and is what lets
one rule set follow a job across its models. If the set is not there, the run **says so** rather
than reporting the rule as matching nothing, because a rule about a missing set otherwise passes as
"not applicable" — the same silence this check exists to break.

Nothing is lost locally either. **Keep the draft here** stores the portable half as IDS — still a
file another tool can read — and the scoped rules beside it, with no rule in both.

The specifications lead the screen. Each is two lists: which elements it is about, and what they
must have. The verdict under the form is live as you type.

- **Property set and property names are model-fed pickers**, still editable — because a requirement
  the federation does not yet meet is a perfectly legitimate thing to write down.
- The draft is kept in the project **as IDS itself**, so what the project holds is already a file
  another tool can read.
- What the document is called — title, version, author, description — is behind the ⚙ symbol.

Saving offers **Check the federation against it**, which lands on Health with the path already
filled in.

### Running them

**Rules from** chooses this project's own rules or an IDS file somebody sent. This project's own is
the one that matters for scoped rules: they can never be in a file, so a check that read files only
could never run the rules you just wrote.

**Check over** narrows the run to a saved set or the selection — a different question from what
each rule is about. A rule set written for the whole job can be run over one level on a Tuesday
without editing a rule.

A rule naming a set this document does not have is **reported**, not left to match nothing: a
misspelled set otherwise passes as "not applicable", which is the same silence this check exists to
break.

### What a check remembers

Each run writes its failures beside the model, and the results grid gains **Raise N as issues** —
one issue per specification, because two requirements failing is two conversations even when the
same wall is in both.

That is what lets "these forty doors are being scheduled separately" be said **once** instead of
every Tuesday. How many fail is this run's answer, not a different requirement; and the file's own
identifier is what identifies a specification, so rewording one does not lose the agreement
anybody reached about it.

A specification that **matched nothing** keeps being reported as its own kind of failure rather
than counted as met. A requirement that quietly stops being checked is worse than one that fails,
and a green tick is exactly where it would go to hide.

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
