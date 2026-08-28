# Sets Manager — product pass

The client's checklist, walked tab by tab as the person who actually opens this app: the
coordinator who builds the selections every other tool consumes. The set is the suite's common
currency — Appearance colours by it, Takeoff measures it, Extract cuts it — so every dead end
here is a dead end in four other apps.

## Builder (`sets`)

**The job.** Monday, before anything else runs: the coordinator has been asked for "all ductwork
above level 3 that isn't in the riser" and needs it as a named set the rest of the week can
point at. They build the rule, check what it catches, and publish it into the selection tree.

**What was wrong.**

- The condition rows asked for the property tab and the property name as bare text, typed from
  memory. The exact spelling is the whole difference between a set and an empty set, and the
  model already knows every name it carries.
- The tool's stated job is "properties and other sets", but the screen had no way to add a
  set-reference condition — only loading a saved rule that already had one could produce it.
  Worse, a loaded set-reference row *rendered* as a property row showing its defaults
  ("Element" / "Category"), so a rule that meant "in Ductwork" read as something else entirely.
- Publishing ended on a count. The set is the input to half the suite, and nothing said what
  the week does with it next or offered the way there.

**What changed.**

- Tab and property are now model-fed pickers (`Pickers.Tab` / `Pickers.Property`), editable as
  before. The value stays typed: it is often a fragment (`Contains "duct"`) no scan would offer.
- Each AND group has "+ and in a set...", which adds a row drawn as what it is — "in the set"
  plus a `Pickers.Set` combo over the document's sets. A reference row whose set is not picked
  yet is skipped, the same way a half-typed numeric row always was. Loaded set references now
  render as set references.
- After a successful publish, the screen states the set is in the selection tree and offers the
  three tabs whose first question is "which set": Scene Kit's Appearance, Quantify's Takeoff,
  Export Kit's Extract — each a jump via `Shell.AppWindow.Show`.

## From a Schedule (`schedule`)

**The job.** The day the client's set schedule (or its next revision) lands as a spreadsheet:
read it, see what each row would make, rehearse the counts, publish forty sets at once.

**What was wrong.**

- The Columns layout's "Property category" was typed text — and it is the single field that
  decides whether all forty sets match anything, because the category is spelled the host's
  way, not the schedule's.
- The wizard ended on "Published 40 sets" with no onward path, on the day the whole point is to
  go and use them.

**What changed.**

- "Property category" is now `Pickers.Tab`.
- After a publish that made anything, two hand-offs: colour them in Scene Kit, take quantities
  off them in Quantify.

The row/column numbers stay typed by design: they are read straight off the sheet preview
sitting beside them, which no picker improves on.

## Generate Library (`setlib`)

**The job.** Project setup, or the week a new discipline lands: turn "discipline, then level,
then system" into a whole tree of sets in one pass instead of an afternoon in the Builder.

**What was found.** The feared "ordered property list as typed text" is not here — properties
are read out of the model and *ticked*, in nesting order, with distinct-value counts beside
them. That is already the right input. The name pattern and floor are templates and numbers,
correctly typed. What was missing was the same dead end as everywhere else: a published library
and no onward path.

**What changed.** After a publish that made anything: jumps to Scene Kit Appearance and
Quantify Takeoff — by-level colouring and per-system takeoff being the two jobs people generate
a library for.

**Out of scope, named.** The generator reads properties by name only and builds every rule
against the fixed category "Element" (`_category`). A property that lives under a different tab
generates rules that match nothing — the Rehearse step does catch this (that is what it is
for), but catching it earlier would need the read to keep `(category, name)` pairs through
`SetFamily`/`SetTree`, an engine-shape change, not a view fix.

## Organise (`restructure`)

**The job.** The federation arrived in exporter shape; the project is run in zones, packages
and systems. Rebuild the selection tree in the shape the job is actually coordinated in, and on
the next revision, see what moved.

**What was wrong.** Each level's "Property tab" and "Property" were typed text — a level typed
as "Levels" where the federation says "Level" is a tree with one branch called "(no value)",
found out only after the read.

**What changed.** Both fields are now `Pickers.Tab` / `Pickers.Property`. Everything else on
this tab — the starter recipe, the read/tree/publish order, the rehearse, the moved-since-last
delta — already answered the checklist and was left alone.

## The app's status strip

Sets Manager had no `Status` provider, so nothing said where the app's own workflow stood. It
now shows two figures (cheap reads only): rules saved in the project file (jumps to Builder;
warns when zero, because a project with no saved rules rebuilds them by hand next revision) and
sets in the Navisworks document (jumps to Organise) — the two different facts a coordinator
actually tracks, since the first travels with the `.cw` file and the second is what every other
tool's "which set" question is answered from.
