# Automate

> Doing all of it again next Friday, in one press.

**Tabs:** [Jobs](#jobs) · [Canvas](#canvas)

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

## Canvas

> Build that sequence as a graph, when a list of steps is not enough.

**Main action: Run the graph.**

Nodes wired together. A new canvas is **seeded with three wired nodes**, which teaches the tool
faster than any help text: everything → filter → colour.

- **Ports are typed** and refuse a mismatched wire with a sentence saying why.
- Clicking an input on its own unwires it; the hint bar narrates the gesture.
- **Saved on this project** is a picker of the jobs the project holds, with Load beside it — you
  never retype a name the project file already knows. Saving adds the new job to the picker.
- The **Name** box is what the job will be saved as.

Symbols on the rail: save the job, load the picked one, add a node, empty the canvas.

Canvas runs are recorded the same way as Jobs runs, so they appear in the same history and the same
*Last ran* column.
