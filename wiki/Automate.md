# Automate

> Doing all of it again next Friday, in one press.

**Tabs:** [Jobs](#jobs) · [Graphs](#graphs)

**One honest sentence up front:** a job runs on the model that is open now, using whatever is
selected now. **Nothing here is scheduled or unattended** — Navisworks has to be running, and so do
you. A plug-in cannot be a build server, and the tab says so rather than implying otherwise.

---

## Jobs

> Run a saved sequence of steps over the document that is open, in one press.

**Main action: Run the selected job.**

The saved jobs as a table: name, size, what it does, and **when it last ran** — joined from the
activity log, so the Friday question *did this already run, and when did it last work?* is answered
in one place instead of by eye across two.

Verbs on the picked rows: **copy**, **delete** (which names what it is about to destroy and asks
twice).

Underneath, the run history — pickable, because the rows worth sending to somebody are the ones
that say why something stopped.

**Make a job from what is open** is the answer to the blank-canvas problem: the four things you
already do by hand every Monday — append these models, reload them, run the tests, write the
report — turned into a job in one press.

---

## Graphs

> Run a Dyncamelo graph against the document that is open, without opening the editor.

**Main action: Run the graph.**

This tab used to be a node canvas of its own — a smaller, weaker one than
[Dyncamelo](https://github.com/mrshoma99-rgb/Dyncamelo) already is, maintained twice and teaching
two vocabularies for one idea. Dyncamelo ships in the same install now, so the editing belongs
there and this tab does the half an editor is a bad place for: **pick a graph off the shelf and
run it.**

- **The shelf** is a folder of `.dyc` files, remembered with the project so whoever opens the job
  next has the same one. It is behind the folder symbol on the action row.
- **The run** works on whatever is open and whatever is selected now, in this Navisworks, through
  Dyncamelo's own engine and its own node registry — the same two the editor runs on, so a graph
  cannot behave differently depending on which window started it.
- **Every node reports**: what ran, what warned, what failed and what it said. The rows can be
  copied.
- **The pencil** opens the real editor, and a run with errors offers the same jump.

Runs are recorded in the project's activity log the way every other write is, so they show up in
[Automate ▸ Jobs](#jobs)'s history and its *Last ran* column.

If Dyncamelo is not there, the tab says which of the two things is wrong — not installed at all,
or installed without a build for this Navisworks year — because those have different fixes.
