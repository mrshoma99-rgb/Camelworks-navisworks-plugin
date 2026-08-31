# Clash Manager

> Find what fights, and decide it.

The clash week, end to end: say what gets tested, run it, filter the noise away, decide what is
left, and record what was agreed.

**Tabs:** [Manage](#manage) · [Matrix](#matrix) · [Clearance Check](#clearance-check) ·
[Filter Clashes](#filter-clashes) · [Report](#report) · [BCF](#bcf) ·
[Clash file](#clash-file) · [Merge](#merge)

The last four are the ways a board leaves this window and comes back. They were an app of their
own, called Issue Link, which meant saving your work started with leaving the screen you were
working on.

Clash Detective ships with Navisworks **Manage** and not with Simulate. On Simulate these tabs open
and say so; see [the offline review round](Common-workflows#the-offline-review-round) for how a
Simulate user still works a board.

---

## Manage

The bench you live in. **Tests on the left, the results of the picked tests on the right**, with a
draggable split between them.

**Main action: Read the results.** Nothing runs when the tab opens — on a half-million-element
federation that would be a misclick you cannot take back.

### The strip across the top

- **One search over both tables.** It matches a test's name, a result's number, an element name, a
  status, an assignee — everything on either row. *Both / Tests / Results* says what it is
  covering.
- **The status ticks**, beside it, because that is the filter you change every few minutes.

### Working the tests

Each test's row carries a band reading **new · active · closed** rather than five columns of
numbers, which leaves the width for the name.

Verbs under the tests table: **run**, **rename**, **renumber**, **move** into a folder,
**compact**, **delete**, **copy**.

### Working the results

Verbs under the results table: **focus** the camera, **isolate**, set **status**, **assign**,
**comment**, **group**, **rename**, drop a **marker sphere**, take a **snapshot**,
**raise an issue**, **copy**.

Turning a row back into elements needs the federation indexed, and on a model whose elements carry
no GUIDs building that index is a walk of every geometry leaf in it. **Read the results** does it
there, inside its own progress bar, so the verbs afterwards are instant. If a press ever needs one
that has not been built — a board carried over, or a read you stopped — it gets its own small
window with a progress bar and a **Stop** rather than a frozen Navisworks.

All of them work on however many rows are picked, and all of them are on right-click too.

### Behind the ⚙ symbols

| Symbol | What it holds |
|---|---|
| Source | Read from the model, or from an exported file |
| Filters | Everything the search bar does not |
| Columns and grouping | Property columns off either element, and a chained grouping order |
| Status and assignment | What the status, assign and group verbs write |
| In the model | Marker spheres, the camera and isolation |
| The review round | The offline round trip — export as BCF, mark up, sync back |
| Walk the board | The review window, one clash at a time |

### Save the changes, and sync them

Two buttons sit beside **Read the results**, and they do different jobs.

- **Save the changes** writes what is on the board to a file. From the model that is a *baseline*;
  from a file it is your *markup*, saved into `markups\` beside the file you opened. Either way it
  carries statuses, assignments, whole comment trails and groups.
- **Sync into the document** writes the changes into the model's own clash results, where the
  native clash window and every export can see them.

**Sync is greyed out when this document cannot take it**, and hovering it says which of the four
reasons applies: you are marking up a file rather than the model; this Navisworks edition has no
clash engine; the document's results will not take an edit; or — the usual one — **it is a
published NWD**.

### Working on a published NWD

A published NWD is a snapshot: its clash results are read-only and nothing can be saved back into
it. The manager does not throw your work away when that happens.

Every status, assignment, comment and regroup the document refuses is **held**: it shows on the
board the way you left it, it survives pressing *Read the results* again, and it survives closing
Navisworks, because it lives in the findings kept beside the document. A banner says how many are
waiting, with **What is waiting** to list them and **Forget them** to stop offering them.

What is held is remembered **field by field**, not as one lump. If the document takes the status
and refuses the comment, only the comment is still waiting — so a sync never writes the status
twice, and never sends a comment the document already has.

Pressing *Read the results* on a published NWD does not undo any of it. The document keeps
reporting the status it had before you touched it, and a read is not allowed to overwrite a field
that is waiting. Everything else the read is still free to correct, so a test renamed or a clash
that moved still updates.

There are two ways out:

- **Save the changes** puts them in the clash file, which is what carries them into the NWF.
- Open the **NWF** the models were federated in and press **Sync into the document**. Everything
  held goes in at once, and the count on the button tells you how much is waiting.

### The clash file

Clash files are **BCF** (`.bcfzip`) — the format Solibri, BIMcollab, Revizto, BIM Track and every
other coordination tool reads and writes. A file you export here opens in the architect's own tool,
they answer in it, and their file comes back and lands on the elements it left from.

BCF has no field for a good deal of what a clash is — the tolerance its test ran at, the distance
between the two elements, the level and grid reference, the keys that put an edit back on the right
element. Those travel in a CamelWorks file **inside the same archive**, which other tools ignore by
design, so nothing the old clash XML carried is lost.

Baselines and markups written before this change are Navisworks clash XML. **They still open** —
the reader recognises either by what is in the file rather than by its name.

#### What the receiving tool can do with it

BCF names elements by their **IFC GlobalId** and by nothing else, so the export reads each
element's GlobalId out of the model on the way out. With it, the architect's tool highlights the
duct and the beam; without it they get the camera and an empty selection. The export says which
you got — elements from IFC sources carry a GlobalId, and elements from Revit, DWG and native
formats generally do not.

#### The round trip, and what holds it together

Send a clash file out, let the architect answer it in Solibri or Revizto, read it back with **Sync
markups** — the edits land on the right results in your model.

That works because of the **topic id**. Every clash is written as a BCF topic whose id is derived
from the two clashing elements, so the same clash is the same topic in every export. The receiving
side does not read that id out of the returned file and trust it — it *recomputes* it from the
model it already has, and matches. The topic id is the one field BCF will not let a tool change:
it names the topic folder and BCF's own merge model is built on it.

So a file can come home with our sidecar gone, the element keys stripped and the viewpoints
re-authored — everything another tool rewrites — and it still lands. The id is derived from the
element pair and not from the clash point, so a conflict that shifts a few millimetres between runs
is still the same topic.

#### What comes back that a clash cannot hold

Two ticks under **Bring it back**, both on by default, because silently dropping what somebody
wrote is the failure the round trip exists to prevent.

- **Raise topics I cannot place as issues.** A reviewer with your file open raises what they find,
  and what they find is not always a clash you sent them — a missing handrail, a door that opens
  the wrong way. Those topics answer to no clash in your model, and they now go to the
  [Issue Manager](Issue-Manager) instead of being counted and discarded. Importing is a merge, so
  the same reviewer sending the same file three times updates their issues rather than tripling
  them.
- **Note what this model has no field for, on the clash.** This one only controls the *comment*.
  Whether it is ticked or not, what the reviewer set is **kept** — see below.

Topics still not placed are counted and named: either raised in the reviewer's own tool with the
tick off, or resolved and gone from a newer run.

#### Priority, due dates and tags

BCF carries a priority, a due date, a stage, a title and any tags a reviewer likes. A Navisworks
clash result carries a status, an assignee, a group and comments, and that is all.

The host not having a column is not a reason the board cannot show one. Everything a reviewer sets
is kept on the finding beside the model and shown on the results grid as a **Reviewer set** column — "High ·
due 15 Apr" — with **overdue rows toned red**, and the search box matches it like any other column.
The column only appears when somebody has actually set something, so it costs no width on an
ordinary board.

It is kept **whether or not** the comment tick is on. That tick decides only whether the same thing
is *also* written as a comment on the clash, which is about making it visible in the native clash
window and to the next tool down the line — not about storing it.

And it goes back out. The next export writes the priority and the due date onto the topic again, so
a reviewer who set one in March still sees it in April's file. Without that, resending the file
would quietly reset what they set — worse than never having carried it, because they saw it
accepted.

#### Where all this lives

Beside the model, in `.camelworks\<model>.cwproj` — the project file. It holds the issue register,
the clash edits a published NWD refused, the bench you set up, and every tool's remembered
settings. It is a plain JSON file, so it diffs, and it travels with the job rather than with the
machine. A document that has never been saved keeps the same thing under your user profile until
it has a home.

**Draw a picture of each clash** (under ⚙ *The review round*, and on the Clash file tab) writes a
thumbnail into each topic. Every coordination tool shows its issue list as pictures, so a file
without them arrives as rows of grey boxes. It is off by default because each one is drawn
separately and on a large board that is the slow part of the export.

### What it will not do

It will never show a smaller number than the engine found without saying so. Suppressed, collapsed
and grouped results are all still counted, and can be shown.

---

## Matrix

> Build a whole test set at once from a grid of one discipline against another, instead of naming
> forty tests by hand.

**Main action: Read the matrix.**

Three ways in, all ending at the same grid and the same legend:

1. **By hand** — categories are picked from the model's own search sets, selection sets or whole
   models. Never typed, because a typed category that matches nothing is a test that finds nothing.
2. **Out of the model's existing tests** — each test's two sides become categories, and each
   distinct kind-and-tolerance becomes a lettered legend symbol. Useful for seeing what last
   month's coordinator actually set up.
3. **The client's spreadsheet** — with a mapping step, a preview, and a check that every category
   names a set this model really has.

The legend is per-symbol: **test type** (hard, clearance, …) and **tolerance**, on or off. Give the
tests a name pattern and a folder, and it reports what it created per kind, with jumps to run them
or open them in Manage.

The whole matrix exports back out as an Excel file — the grid on one sheet, the legend on the next.
It survives switching tabs.

---

## Clearance Check

> Find where the clear height under the services is not there — which no clash test can find,
> because nothing is touching.

**Main action: Check.**

A duct 1.9 m over a corridor floor clashes with nothing: there is air between them. No clash test
at any tolerance will ever report it, and it is still a defect.

### Pointing it

Both inputs — the surfaces, and what is over them — list **every search set and selection set the
document holds**, plus the current selection and everything loaded. So you can check the corridors
by naming the corridors.

- **Walking surfaces (a guess)** is one of the options, not the only one: it takes anything whose
  category or type contains *floor, slab, stair, landing, ramp, platform* or *deck*, and the screen
  says so.
- **Everything except the surfaces** means exactly that — the elements the first input did not
  take.

### The findings

The findings get the same bench as the clash results: sorting, filtering, status ticks, chained
grouping, property columns read off the surface or off what is over it, and bulk
Focus / Isolate / Mark / Assign / Comment / Group / Sphere / Copy.

One row per surface, positioned at the overlap rather than at the middle of a forty-metre duct.

**The judgement survives a re-run — and closing Navisworks.** No clash engine found these, so there
is nothing in the model's clash data to write into. Status, assignment, comments and groups live
beside the model instead, on the same surface-and-obstruction pair and the same place. Re-checking
after a model update does not reopen everything somebody closed, and neither does going home.

**Raising the minimum does not lose what you decided.** Re-running at 2.4 m after running at 2.1 m
finds the same low soffit, and it is the same finding: the number it fell short of is a fact about
the run, shown as one, not part of what the finding *is*.

Picked rows can also be **attached to an issue that already exists**, so a corridor that is too low
for its whole length joins the conversation already about it rather than starting a fifteenth.

### Out of the tab

- The grid to a spreadsheet, exactly as filtered.
- Every shown finding as a **clash file** (BCF) — which the Manage tab's *A file* source opens, so a
  clearance list runs the same offline review round as a clash export.
- Right-click → **raise an issue**, into the [Issue Manager](Issue-Manager).

---

## Filter Clashes

> Suppress what does not matter, group what is one issue, and assign what is left.

**Main action: Try these on the board.**

The reduction between the engine and the issue list, laid out in the order it runs:

1. **Suppress and flag** — every raw result tested against a predicate.
2. **Group** what survives. Left empty, the built-in stack applies: model pair → level →
   proximity.
3. **Assign**, first match wins.

*Try these on the board* builds the board with your unsaved edits and shows the four figures the
rules exist to move. **Nothing is written until you Save.**

These act on **clash results, never on issues**. A rule that quietly closed something a person had
written a sentence about would be the worst behaviour in the product.

---

### The board these four work on

Report, BCF, Clash file and Merge each work on a board. Each one opens by saying where it stands — loaded, and how many
issues, or not loaded — and *not loaded* carries **Load the board** right there, which builds it on
the tab you are on. You never navigate away to fetch a prerequisite.

Writing without loading reports the **rebuilt** board (from the saved rules), not the one you
triaged. The screen says so before the press, not after.

---

## Report

> Get the board out as a document somebody who does not have Navisworks can read.

**Main action: Write the report.**

Thursday afternoon: one file per party, revision-lettered, superseding last week's.

- The audience comes from the parties in your project profile, with a **revision ledger per party**
  — so *revision C to Structures* is a fact the tool holds, not something you remember.
- The funnel is always reported over the **whole** board, whatever the report is filtered to.
- A warning before you commit to pictures, which are what makes a report take minutes.

Behind the ⚙ symbols: where the file goes and what it is called, markers in the model, and sending
it onward as a file somebody works in.

When it has written, it offers the two jumps that actually follow — the same board as **BCF** or as
**Clash file** — and says where a reply will land when it returns.


---

## BCF

> Exchange issues with every other tool that speaks BCF, in 2.1 or 3.0.

**Main action: Send the board.**

The round trip. Send one topic per issue; a fortnight later read the answered file back against the
board and apply what survives the merge.

- **De-duplicated by GUID** — the same topic coming back twice is one topic.
- **Promote-only status merge**: an incoming status can move an issue forward, never quietly
  reopen something that was closed.
- **Nothing is applied until you press the button.** The reply table shows what would change, and
  its rows can be picked and copied for the email.
- A read here also lands in the [Issue Manager](Issue-Manager)'s register.

Behind the ⚙ symbol: how the file is written. On the stage: **Read the answers back**.


---

## Clash file

> Read a board out of the format other coordination tools speak, or write one back into it.

**Main action: Show me what would go in.**

A rehearse-then-commit two-step against a saved per-test profile, with a **last-sent date per
test**, so you can see which tests have gone out and when. Profiles live in the project file and
can be written to disk.

It reconciles renamed and missing tests on the way back in rather than dropping them.

The write points at the read leg, and at BCF's read — which is where a reply actually lands on the
board.


---

## Merge

> Fold several reviewers' responses back into one board, showing where two of them disagree.

**Main action: Bring them together.**

Six packs go out on Monday and six come back on Thursday. Merge folds them onto one board.

- The **party comes from the filename**, so you do not retype six names.
- First-sight baseline reconstruction, for the reviewer who edited a file you never sent.
- **Conflicts are shown, not resolved.** Where two reviewers disagree you get both, rather than
  whichever file was read last.

Without a board loaded, the merge still reads — but it ends at a message rather than an apply
button, and the tab says so on arrival.
