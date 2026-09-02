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

The bench you live in. A **test finder** on the left and **one board** on the right, with a
draggable split between them.

**Main action: Read the results.** Nothing runs when the tab opens — on a half-million-element
federation that would be a misclick you cannot take back.

### Finding the test

One row per test, read down the rail rather than across a table: the name, one line of hint, and
four figures — **new, active, reviewed, resolved** — as a band on the right. The bar down the left
edge of a row is **red** while anything on that test is new, **amber** while anything is open, and
**green** when the test is finished. The test on the board has a blue bar and a tinted row instead,
so "where am I" reads before "how is it".

The hint line carries the test's tolerance, and the time it ran **only when that differs from when
the rest of them ran** — there is no point repeating "run Tue 14:20" down eighteen rows. The whole
sentence is on the row's tooltip.

At the top of the rail: a **search** over names and disciplines, a **sort** (most new, most open,
name, last run) and a **filter panel** behind the funnel — discipline chips, folder chips, *only
tests with something open*, and *Clear all*.

**A filter comes off where it went on**, in that panel. There is no row of chips above the list:
the rail is a list of tests and a row given to saying what it is filtered to is a row of tests you
cannot see. Instead the **funnel turns blue** while anything is narrowing the list and says what,
in full, on hover — and the line at the foot of the rail reads *Showing 3 of 6 tests* rather than
its usual count.

> The design this was built from asks for a **Level** filter. Nothing on a clash test carries a
> level, and one guessed out of the test's name would hide tests for a reason it could not state,
> so the chips are the clash **folder** — which is where a level goes when a coordinator files
> their tests.

At the foot of the rail: **run all the tests again**, **rename this test**, **compact**, and a
menu holding renumber, move into a folder, copy the list, find duplicates across tests, and
delete.

### What it costs

**The board holds one screenful of rows at a time.** A test with thousands of results draws the
rows you can see and builds the rest as you scroll to them, so searching, ticking, sorting and
dragging a column edge cost the same on a board of fifty thousand results as on one of fifty.
Typing in the search box waits for you to stop before it filters.

**Reading is one pass.** *Read the results* walks the clash tree once for every test, its results
and what each test is between. Nothing that only an export wants is read there: the comment trail
and the IFC GlobalIds are fetched when a baseline is written, each with its own progress line and
a **Stop**.

**Nothing here searches the model.** Every verb on a board read from the model — focus, isolate,
status, assign, comment, group, the property columns, the export's IFC ids — goes to the clash
engine, which is holding both elements of every result. Only a board read from somebody else's
**file** has element keys and nothing behind them, and only there does reading pay to index the
federation.

**Property columns belong to the board, not to a test.** A column you add lands on every test's
results, and *Read the values* fills it across all of them. The names offered in the picker are
what the clashing elements of every test actually carry — not a sample of the model, which can
miss a property only the clashing pipes have.

**An edit refreshes what it touched.** A run, a renumber or a compact reads back the tests it
named; a rename changes a word in place. Only deleting or moving tests — which changes the rail
itself — reads the document again, and it shows a progress line while it does.

### Working the board

The board is the picked test's results, as **two kinds of row at the same level**.

- A **clash group** is a real object in the test. It is saved with the test, it carries its own
  status and comments, and it goes out with every report and BCF file. It has a chevron that opens
  it, a group icon, a count, and the worst overlap among its members. Its Item 1 and Item 2 columns
  show em-dashes, because a group aggregates many different pairs and has none of its own.
- A **loose result** is a single clash in no group. Same row, with the real Item 1 and Item 2 that
  Navisworks reports, its own overlap, and no count.

Loose results sit under one **Not grouped** band, always visible — never behind a tab, because
grouping them is the job.

**Status and assignee are edited on the row.** Click the chip and pick. There is no window.

**Double-click to rename** — a test in the rail or in the title, a clash group on its row. A single
clash renames too, though the host has no rename for one result: it takes a prefix and numbers what
is picked from it, so renaming one makes `Corridor1`. The message says so.

**The columns sort and resize.** Press a header to sort by it, press again to turn it round, press a
third time for the order the test itself holds. Drag a header's right edge to resize. The rail's
four figures sort it the same way — press *New*, *Active*, *Rev.* or *Res.*, or *Test* for name
order; the sort menu holds the same six and says which is on.

**Click a row to pick it.** **Ctrl-click** adds one row or takes it back out; **Shift-click** picks
the whole run between the last row you pressed and this one, and Ctrl with Shift adds that run to
what is already picked. The run is what is on the screen in the order it is on the screen, so a run
across an opened clash group takes the group and the members it is showing, and one over a closed
group takes the group alone. Drag rows onto a clash group to move them into it. **Row verbs appear when you hover a row or tick it**, and are invisible otherwise: focus, isolate,
comment, and a menu with snapshot, marker sphere, clear the markers, raise as an issue, attach to an
existing issue, copy, and — depending on which kind of row it is — rename or ungroup the clash
group, or group it with what is picked and move it into an existing one.

**Marker spheres are drawn over the model, not added to it.** A marker is a status-coloured ball
hanging at the point where the two elements met, so a clash between a duct and a beam stops looking
like a duct and a beam. It appears on the next frame, it costs nothing, and it is gone the moment
you press *Clear the markers* or close Navisworks — nothing joins the selection tree and there is no
file to unload afterwards. **Focus drops one too**, which you can turn off, and their size is set,
on the *Markers and the view* page under the ball symbol in the header.

**Export the markers as .obj** is the other half — the same balls as a file, for somebody who has
neither this plug-in nor Navisworks. It is under the results, over whatever you have picked, and on
the **test menu**, over every test on the board. What arrives at the other end is a **group per
test** with an **object per clash inside it**, each named what the engine named it — *Clash117* —
and coloured by its status. So the receiving tool's selection tree reads the way this board does:
collapse to a line per test, expand to the clashes, click one and know which one it is. It opens in
Revit, Rhino, Blender, SketchUp, a free viewer or a phone.

> Send the **.mtl** file saved beside the .obj, or the status colours are lost. Coordinates are
> written in metres, which is what every OBJ reader assumes.

The same export over the *whole* board, rather than a selection, is the *Markers* section behind the
ball symbol on [Report](#report).

### Filtering the board

The **status strip** above the table is five segments carrying the counts — *all, new, active,
reviewed, resolved*. Clicking one filters the board to it; clicking it again clears it. That is
the only status filter; there are no tickboxes.

Beside it, a **search**. Two symbols in the header above hold the rest, and **each turns blue when
it is doing something** — the sort (bars, long to short) and the filter (a funnel), the same pair
and the same symbols the test rail uses. Sorting holds the column order and the banding; filtering
holds grouped-or-loose, whose it is, and what the property columns say. The filter's menu is where:

| | |
|---|---|
| **Show** | Both, grouped only, or loose only |
| **Assigned to** | Anyone, or one party |
| **Arrange view by** | Nothing, assignee, status, or how bad |

**Arranging the view changes nothing in the model.** It draws bands across the table and
re-orders what is on screen. No clash group is created, dissolved or touched by it — which is why
a band looks nothing like a group row: no tick, no chevron, no colour bar. *How bad* splits on
**this test's own tolerance**, not a fixed number, because a 50 mm drainage test and a 10 mm
ceiling test do not share a definition of bad.

**A filter comes off where it went on**, exactly as on the rail: the lit status segment clears by
pressing it again, the search box clears by emptying it, and the layers menu sets *Both*, *Anyone*
and *Nothing* — with **Show everything again** at the bottom of it when anything is in force. There
is no chips row: what the board is showing is written beside the test's name as *Showing 18 of 168*,
and the layers symbol says the rest on hover.

### Grouping, on the selection bar

Tick anything and the **selection bar** appears at the foot of the board. It carries one grouping
verb, and it is the one those rows actually have:

| What is ticked | What the bar offers |
|---|---|
| Two or more loose results | **Group** — names it and makes it |
| Anything already in a group | **Remove from the group** |
| One whole clash group | **Dissolve the group** |
| Two or more whole clash groups | **Merge the groups** |

A group made this way is **made in the test**, not on this screen. It is saved with the test and
goes out with every export.

The bar also sets status and assignee over everything ticked, raises issues, and holds the **same
verbs a single row holds** — focus, isolate, show everything again, comment, snapshot, marker
sphere, raise as an issue, attach to an existing issue, copy as columns, move into an existing
group and suppress. One row or forty, the list is the same one.

### Which columns the board shows

Nine, and they are the clash: the tick, the group or result name, **Item 1**, **Item 2**, how many
clashes, the **worst** overlap, **status**, **assigned**, and the row's verbs. Navisworks reports a
clash as a pair of colliding items and this keeps them as two columns rather than merging them
into one sentence.

The **columns** symbol in the board's header adds the column the host does not have: a **property
read off the clashing elements themselves** — a system name, a workset, a fire rating — which
lands between *Worst* and *Status*.

### Behind the board's header symbols

| Symbol | What it does |
|---|---|
| Run | Run this test again |
| Walk | The review window, one clash at a time |
| Columns | Property columns off either element |
| Layers | How the board is shown — grouped or loose, whose it is, how it is arranged |
| Source | Read from the model, or from an exported file; tolerance and markers |

The footer's **Export** menu holds the ways work leaves: save as a clash file, write a report,
the board to a spreadsheet, set up the review round, and raise the board as issues.

### Save the changes, and sync them

**Every edit on this board is already written.** Picking a status or an assignee on a row asks the
document there and then — there is no draft to commit. What is left to save is what the document
**refused**, which is what the footer's one blue button is for.

- **Comments read as well as write.** The speech-bubble on a single clash opens its conversation:
every remark with who wrote it and when, a box to add another, and — on remarks that came from the
document — a pencil and a bin.

> **Editing replaces.** Every field of a Navisworks comment is read-only and the only write the host
> offers is the whole trail at once, so changing a remark means putting a new one in its place. It
> keeps its position in the conversation and its author, and takes **today's date**. There is no way
> round that; the button says so rather than pretending otherwise.

A remark added here that the document would not take is shown in the conversation too — it is the
same remark, waiting for a seat that can write it. Picking several clashes and commenting still
says the one thing on all of them, because a set of clashes has no single trail.

**Renumbering writes the clash's own name.** A Navisworks clash result carries a `DisplayName`, and
"Clash1", "Clash2" is Navisworks' own convention for filling it — there is no separate number field.
Renumbering sets that name to *prefix*1, *prefix*2 and so on, in the order the test holds its
results, so the new names show in the native Clash Detective grid and travel in the clash XML. It is
not an identity: re-running a test recreates its results and renames them, which is why every
decision here is keyed to the clash point and the two elements rather than to what the clash is
called.

**Every change on this board is kept, whatever the document will take.** A status, an assignment, a
comment, a group, a clash's name, a test's name and the folder it is filed in all go into the
project first — that is what *Save the changes* writes — and are synced into the document as well
when the model is an NWF on a Manage seat. On a published NWD the document takes none of them and
nothing on the board says so per edit: the strip at the top of the window says it once, and the
changes wait for a seat that can write.

Three verbs are the exception, and they say so when they refuse: **running** a test is the engine's
work, and **deleting** or **compacting** one changes the document itself. None of those is a
decision the project could hold and hand over later.

**Renaming a test, and moving it into a folder, are kept even on a published NWD.** Both are writes
into the host's clash data, which a published NWD holds read-only — so both used to refuse outright.
They now fall back to the project, exactly as a status or a comment does, and the board shows the
name you chose. A seat that can write puts them into the document instead.

**Save the changes** writes every held decision into the model's own clash results, where the
  native clash window and every export can see them. When something is waiting, the button counts
  it: *Save 14 changes into the document*.
- **Export ▸ Save as a clash file** writes what is on the board to a file instead. From
  the model that is a *baseline*; from a file it is your *markup*, saved into `markups\` beside the
  file you opened. Either way it carries statuses, assignments, whole comment trails and groups.

**The blue button greys out when this document cannot take a write**, and hovering it says which
of the four reasons applies: you are marking up a file rather than the model; this Navisworks
edition has no clash engine; the document's results will not take an edit; or — the usual one —
**it is a published NWD**. The same reason appears in the *Export* menu, where somebody goes
looking for a way out.

### Working on a published NWD

A published NWD is a snapshot: its clash results are read-only and nothing can be saved back into
it. The manager does not throw your work away when that happens.

Every status, assignment, comment and regroup the document refuses is **held**: it shows on the
board the way you left it, it survives pressing *Read the results* again, and it survives closing
Navisworks, because it lives in the findings kept beside the document. The footer says how many are
waiting, in red with a dot; pressing it lists them.

What is held is remembered **field by field**, not as one lump. If the document takes the status
and refuses the comment, only the comment is still waiting — so a sync never writes the status
twice, and never sends a comment the document already has.

Pressing *Read the results* on a published NWD does not undo any of it. The document keeps
reporting the status it had before you touched it, and a read is not allowed to overwrite a field
that is waiting. Everything else the read is still free to correct, so a test renamed or a clash
that moved still updates.

There are two ways out:

- **Export ▸ Save as a clash file** puts them in the clash file, which is what carries them into
  the NWF.
- Open the **NWF** the models were federated in and press **Save the changes**. Everything held
  goes in at once, and the count on the button tells you how much is waiting.

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

Behind the ⚙ symbols: where the file goes and what it is called, the clash points as an **OBJ mesh**
somebody else can open, and sending it onward as a file somebody works in. That mesh is a file, not
the on-screen ball the board's row verbs drop — use it when the markers have to survive without
CamelWorks installed.

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
