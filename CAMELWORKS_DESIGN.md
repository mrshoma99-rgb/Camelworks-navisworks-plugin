# The CamelWorks design — desks and the surface anatomy

This is the third design of the CamelWorks front end, and the document that governs it. The first
design was one dock pane holding seven workspaces and eighteen tabs; the second was a ribbon of
thirty-seven buttons opening forty-two separate windows. Both were rejected whole. What survives
from them is the design system — the palette in `Skin.cs`, the control styles in `SkinSheet.xaml`,
and the widget vocabulary in `Ui.cs` — and nothing else.

The client's words, kept because every rule below answers one of them:

> *"you don't know what is part of a tool and what is its own tool"*
> *"enough with these collapsible tabs"*
> *"three different collapsible tabs inside a dialogue window!!!"*

---

## 1. The shape: five desks, one home, one pane

A coordinator does not have forty-two jobs. They have five, and each one is a **desk** — a window
you open to do that job, holding every tool that belongs to it, visible at once, none of them
folded away.

| Desk | The job | Its tools, by stage |
|---|---|---|
| **Federation** | Bring the models in and trust them | ARRIVE: Models · Fix Broken Links — TRUST: Health Check · IDS — TRACK: Baseline · Compare · Changes · Change Notice · History — PROJECT: Project Settings |
| **Coordination** | Find what fights, decide it, send it | FIND: Clash Tests · Clash Matrix · Clearance — REFINE: Clash Rules — DECIDE: Clash Board · Review — SEND: Clash Report · BCF · Clash XML · Penetrations — RETURN: Merge Markups — RECORD: Freeze |
| **Data** | What the elements carry, and fixing it | READ: Data Manager — WRITE: Write Properties · Excel Round-trip · External Data · Levels and Zones · Work Breakdown — OUT: Takeoff · Report Builder · Export Data |
| **Scenes** | What you select and what you see | SELECT: Set Builder · Sets from a Schedule · Generate a Set Library · Organise Sets — SHOW: Appearance · Viewpoints · Section Box |
| **Deliver** | The model leaving the building | BUILD: Compile · Extract — FILES: Export IFC · Export GLB · Export Drawing — SITE: Setting Out · Dimensions · Centre of Gravity · Site Labels · Documents and Photos — AGAIN: Batch · Graph Editor |

Rules of the desk:

1. **A desk is one window.** Opening the desk again raises the one that is open. Every tool of the
   desk is named on a permanent rail down its left edge, grouped under its stage in small caps.
   The rail does not collapse, does not scroll tools out of a fold, and never hides a tool behind
   another. One tool is on the bench at a time; the rail says which, and one click changes it.
2. **The rail is a register, not a menu.** Each row carries the tool's name and, where the tool
   has state worth a glance, a chip: the board's open count, when tests last ran, whether a
   baseline exists. A desk tells you where the job stands before you choose a tool.
3. **Tools keep their identity.** The tool registry (`Shell/Tools.cs`) — id, title, purpose, one
   main action — survives from the second design because it is an inventory, not a layout. What
   changed is where a tool appears, never whether it exists.
4. **Beside-the-model tools live in the pane, always.** Clash Board, Changes, Data Manager, Set
   Builder and Appearance do their work over an hour with the 3D view alongside, so they open in
   the dock pane, whether summoned from the ribbon or from their desk's rail. Their rail rows say
   so. The Graph Editor keeps its own wide pane.
5. **Home is the dock pane at rest.** The week's three steps with their state, the desks with a
   line each, the tool search, and the recent activity. Home is a place to leave from, never a
   place a tool lives.

There are exactly two windows that are not desks: **Review**, a floating palette over the 3D view,
because its whole point is to not cover the model; and **Help**, because it must work when the rest
of the product will not.

## 2. The ribbon: six panels, twenty-two buttons

One button per desk, plus the daily tools as direct entries that land *inside* their desk with the
tool already on the bench. Down from thirty-seven buttons to twenty-two, with nothing lost: every
tool is one desk-click away, and the daily ten are still one ribbon-click away.

| Panel | Large | Standard |
|---|---|---|
| CamelWorks | Home | Help |
| Federation | Federation | Health Check · Compare · Baseline |
| Coordination | Coordination · Clash Board | Clash Tests · Review · Clash Report |
| Data | Data | Data Manager · Takeoff |
| Scenes | Scenes | Set Builder · Appearance · Section Box |
| Deliver | Deliver | Export IFC · Compile · Batch |

Right-click → Add to Quick Access Toolbar remains the personalisation story on every button.

## 3. The surface anatomy

Every tool surface — on a desk bench, in the pane, in Review's palette — is built to one anatomy.
It answers *"what is part of a tool and what is its own tool"* structurally: a tool is a rail row,
an option is a field in the setup column, a result is the stage. Nothing else exists.

```
┌──────────────┬───────────────────────────────────┐
│  SETUP       │  STAGE                            │
│  the inputs, │  the answer. Results, previews,   │
│  every one   │  tables, verdicts. The biggest    │
│  visible     │  and most decorated thing on the  │
│  ────────    │  surface is always here.          │
│  [ Action ]  │                                   │
└──────────────┴───────────────────────────────────┘
```

1. **Setup is a column, on the left, and everything in it is visible.** Scope first, then the
   options, as plain labelled fields under small-caps group labels. **No folds. Zero.** An option
   that seems to deserve hiding is either a default that should state itself, or a setting that
   belongs to Project Settings, or clutter to delete. `Ui.Section` does not appear on any surface.
2. **The one action sits at the foot of the setup column, filled, full width.** Same place on
   every tool, so the hand knows where to go before the eye does. Secondary acts — export the
   other format, copy, dry-run — are quiet buttons on the stage, next to the result they act on.
3. **The stage is the answer and it dominates.** Before the first run it states, in one sentence,
   what pressing the action will do and to what — computed from the live model, never boilerplate.
   After a run it is the result: verdict line first, figures, then the table. Progress and Stop
   appear on the stage, where the result will land.
4. **Destructive writes keep the two-step.** Preview as a diff on the stage, then a commit button
   that names its consequence (`Ui.Commit`). The one thing Ctrl+Z cannot undo is the one thing
   that never happens on a single press.
5. **A wizard is a bench with a spine.** Steps are numbered down the left where the setup column
   would be, all of them visible with their state; the current step's fields sit under its number;
   the stage previews what the finished run will produce. Next is the column's foot action.
   Wizards exist only where step N genuinely cannot be answered before step N−1.
6. **Narrow is a mode, not an accident.** In the pane (~470 px) the setup column folds under a
   single fixed header row — scope summary + the action — and the stage takes the rest. The same
   anatomy, stacked; never a different screen.
7. **Prose above a control is still deleted on sight** — the one rule of the old grammar the
   client never complained about. Meaning lives in labels, field hints under ten words, or a line
   printed after the run.

## 4. What the design system keeps

`Skin.cs` (palette, tones, families), `SkinSheet.xaml` (control styles), and the `Ui.cs`
primitives: text ranks, fields, buttons, pills, figures, tables with picking, the diff/commit
pair, the runner's job plumbing, empty states. The desk chrome and the surface anatomy are built
*from* these parts in `Views/Bench.cs`. `Ui.Card`, `Ui.Marked`, `Ui.Numbered` and `Ui.Section` are
no longer part of any tool surface's vocabulary.

## 5. The test

Point at anything and answer in one word: **tool** (a rail row), **option** (a field in setup),
**action** (the foot of the column), or **result** (on the stage). If it takes two words, it is
wrong. If it is folded, it is deleted.
