# Automate — product audit

## The redesign pass

The saved jobs were rows with a single Delete beneath them that acted on whatever had last been
clicked. They carry verbs now — copy, and delete everything picked, still naming what it is about
to destroy. On the canvas, saving, loading, adding a node and emptying the canvas are symbols.


Doing all of it again next Friday, in one press. Tabs: Jobs · Canvas. Walked as the person who
does the same five exports every week: Monday morning, open the federation, run "Weekly
coordination", check nothing stopped.

## Jobs

**The job.** Monday morning. Pick the saved job, press Run, read what happened — and first,
answer "did this already run today, and when did it last work?"

**Wrong.**
- The jobs table said what each job is (name, size, steps) but not when it last ran — yet the
  activity log two inches below holds exactly that. The person joined the table and the
  history by eye, which is the Friday question this tab exists to answer.
- The empty state told the user to go to the canvas in prose, with no way there — a dead end
  where the suite's own hand-off pattern applies.
- The app opened with no strip: nothing said "3 jobs saved, last run yesterday" before a tab
  was read.

**Changed.**
- A **Last ran** column on the jobs table, joined from the activity log by the run record's
  own summary ("ran \"name\"…"), newest entry wins, "never" where nothing has. A cheap read —
  the log is already in memory, capped at 300.
- The empty state carries an **Open the canvas** button (`Shell.AppWindow.Show("automate",
  "graph")`) beside the sentence that used to only describe it.
- A status strip (in `Surfaces.Status`, case `automate`): jobs saved (warning "none" lands on
  the canvas; a count lands on Jobs) and when a job last ran, from the activity log.

**Found sound.** Make-a-job-from-what-is-open remains the right answer to the blank-canvas
problem; delete keeps its two-press confirm; the run history is pickable because the rows
worth sending are the ones that say why something stopped; the "nothing is scheduled or
unattended" sentence keeps the product honest about what a Navisworks plug-in can be.

## Canvas

**The job.** Build or adjust the sequence when the list of steps is not enough; save it into
the project file so Monday's press exists.

**Wrong.** Loading a job meant typing its name into the *Name* box, spelt exactly as saved —
a name the project file already knows, demanded from memory, with the miss handled by listing
the correct spellings for the user to retype. The textbook hostile input.

**Changed.** A **Saved on this project** group with a picker of the saved jobs' names and the
Load button beside it. Saving a new job adds it to the picker and selects it, so save-then-
reload works within one sitting. The Name box stays what it was: what the job will be saved
as. No behaviour lost — load still guards against a job deleted since the list was read.

**Found sound.** The seeded three-node graph teaches the tool faster than help text; ports are
typed and refuse mismatched wires with a sentence; an input clicked on its own unwires, and
the hint bar narrates the wiring gesture; the run log and `session.Record` match the Jobs tab,
so canvas runs appear in the same history (and now the same Last ran column).

## Judged missing, out of scope

- **Scheduled / unattended runs** — needs a host process Navisworks does not provide; the tab
  already says so on the glass, which is the honest treatment.
- **Per-job run history** (click a job, see only its runs) — the joined Last ran column covers
  the Monday question; filtering the history table per job would want a small query engine on
  the log and was not worth the surface this week.
- **Node settings with pickers on the canvas** (e.g. a property name inside a `filter.where`
  node) — node settings render generically from `NodeCatalogue`; feeding them from `Pickers`
  means teaching the catalogue which settings are property-shaped, an engine change.
