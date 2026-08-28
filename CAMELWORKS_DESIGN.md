# The CamelWorks design — a suite of apps

The fourth front end, and the document that governs it. The first design was one pane with seven
workspaces and eighteen tabs; the second, thirty-seven buttons opening forty-two windows; the
third, five desk windows with tool rails. Each was rejected. What survives every time is the
design system — `Skin.cs`, `SkinSheet.xaml`, the `Ui.cs` vocabulary — and the engines.

The client named the model to follow: **the DiRoots plug-in collections** (DiRootsOne, ProSheets,
SheetLink). Their philosophy, distilled into law here:

1. **A flat suite of named apps.** No categories, no desks, no rails. One ribbon row, one button
   per app; each app is a self-contained product with a name and one job, and all of them share
   identical chrome so switching between tools feels natural rather than disjointed.
2. **Grid-first.** An app opens on its data, not on a form: the things it operates on — clashes,
   properties, viewpoints, models, sheets of quantities — as a big filterable table with a search
   box, live counts, and rows you tick. The data is the landing page.
3. **Modes are flat tabs across the top.** Never collapsible, never nested, always visible.
4. **The primary action sits bottom-right**, in a footer bar every app shares, with progress
   beside it. The classic dialog contract: read the data, set the options, press the big button.
5. **Options are a quiet right-hand panel.** Compact, always visible, never folded.

## 1. The suite: eleven apps, and the hub

| App | The job | Its tabs |
|---|---|---|
| **CamelWorks** (hub) | The week, the search, and the way to every app | one surface |
| **Clash Manager** | Find what fights and decide it | Tests · Matrix · Clearance · Rules · Board · Freeze |
| **Issue Link** | Issues out to the world and answers back | Report · BCF · Clash XML · Merge · Penetrations |
| **Model Audit** | Is this federation fit to coordinate against | Health · IDS · Models · Broken Links · Project |
| **Compare Track** | What changed, against what, since when | Compare · Changes · Change Notice · History |
| **Data Manager** | What the elements carry, and the pen to fix it | Properties · Write · Excel · External Data · Levels & Zones · Breakdown |
| **Quantify** | Numbers out of the model | Takeoff · Report Builder · Data Export |
| **Sets Manager** | The selections everything else works on | Builder · From a Schedule · Generate Library · Organise |
| **Scene Kit** | What is visible and what colour it is | Appearance · Viewpoints |
| **Export Kit** | The model as a file somebody else opens | IFC · GLB · Drawing · Extract · Compile |
| **Site Pack** | The model reaching the people building it | Setting Out · Dimensions · Weight · Labels · Documents |
| **Automate** | Doing it again next Friday | Jobs · Canvas |

Rules of the suite:

- **An app is one window**, singleton, remembering its last tab. There are no dock panes: every
  app opens, does its job, and closes, and the 3D view stays the host's.
- **Tools keep their identity** in `Shell/Tools.cs`; `Shell/Apps.cs` says which app and tab each
  one is. A check holds every tool to exactly one app.
- **Review** stays the one floating palette (launched from the Board tab and found by search),
  because its whole point is to not cover the model. **Section Box** stays an instant ribbon act.
  **Help** stays its own window, because it must work when nothing else does.
- The **hub** is the one app that is not a tool: the week's three steps with their state, the
  Find-a-Tool search (symptom phrasing and all), and the suite directory.

## 2. The ribbon: one flat row

One panel, twelve app buttons at one size — the suite as DiRootsOne draws it — with Section Box
and Help stacked at the end. Fourteen commands. Every button opens the app it names; nothing on
the ribbon opens a menu, a pane, or somebody else's window.

## 3. The app anatomy

Every tab of every app renders to one shape (`Views/Bench.cs`):

```
┌───────────────────────────────────────────┬─────────────┐
│  Tests · Clearance · Rules · Board        │   (tabs)    │
├───────────────────────────────────────────┼─────────────┤
│  THE DATA                                 │  OPTIONS    │
│  the grid/tree of what this tab operates  │  compact,   │
│  on: search, counts, tickable rows, and   │  grouped,   │
│  everything a run produces                │  all shown  │
├───────────────────────────────────────────┴─────────────┤
│  progress …                     [ quiet ]  [ PRIMARY ]  │
└─────────────────────────────────────────────────────────┘
```

1. **The data fills the window** and lands first: current state, live counts, the table. Before
   the first run it states, computed from the live model, what the primary will do — one line
   over the empty grid, never a welcome paragraph.
2. **Options on the right**, fixed width, grouped under small-caps labels — for a tool that is
   configured and then run. A bench somebody works in for an afternoon uses the second anatomy
   below instead; the two are not a matter of taste, they are a matter of what the tab is for.
3. **The footer is the contract.** Progress and its Stop on the left, the one primary action on
   the right, quiet secondaries beside it. Same place in every app, every tab.
4. **Destructive writes keep the two-step**: preview as a diff in the data area, then a commit
   button that names its consequence.
5. **Prose above a control is deleted on sight** — the one law every design has kept.
6. No `Ui.Section`, `Ui.Card`, `Ui.Marked`, `Ui.Numbered` anywhere. `checks/folds.txt` stays
   empty and ratcheted.

## 3b. The second anatomy: a bench

A tab whose job is *worked* rather than *run* — the clash results, the clearance findings, the
issue register — renders to `Bench.Split` and `Bench.Pane` instead:

```
┌───────────────────────────────────────────────────────────┐
│  🔍 search · what it covers · the ticks touched constantly │
├──────────────────────┬────────────────────────────────────┤
│  THE MASTER          │  THE DETAIL                        │
│  tests, sets, files  │  what the picked master rows hold  │
│                      │                                    │
├──────────────────────┼────────────────────────────────────┤
│ ▶ ✎ # ⇢ ⇕ 🗑 ⧉        │ ⊙ 👁 ✓ 👤 💬 ⬚ ● 📷 ⊕ ⧉            │
├──────────────────────┴────────────────────────────────────┤
│  [ PRIMARY ]   ⚙ filters  ⚙ columns  ⚙ the round  …       │
└───────────────────────────────────────────────────────────┘
```

1. **The tables get the window.** Two related tables sit side by side with a draggable split, not
   stacked with an options column beside them.
2. **What is touched every few minutes is on the surface**: one search over both tables, what it
   covers, and the status ticks. Nothing else.
3. **Verbs are symbols under the table they act on** — greyed until something is picked, the
   caption and the count in the tooltip — and the same verbs are on right-click. `Ui.Picking`
   does both for every table in the product; a verb with no shape keeps its words rather than
   being given one that has to be learned.
4. **Everything decided once for the job lives in a configuration window** (`Ui.Configure`),
   opened from the symbol on the action row that names it. Edits apply live; closing redraws.
5. **The action row reads left to right**: the primary press, then the windows. Not right-aligned,
   because it carries more than one thing.

The law behind both anatomies is the same one: *the work gets the window, and setup does not
compete with it.* Rule 2 of §3 stands for tools that are configured and run; a bench earns the
split the moment its options outgrow the eye.

## 3a. An app is a product, not a binder

The anatomy says how a tab looks; `CAMELWORKS_APPS.md` says what an app owes its user — the
checklist every tab is audited against (the job, the missing next step, hostile inputs, useless
data, invisible dependencies) and the two mechanisms that answer it: the **status strip** every
app draws under its tabs, and **hand-offs** that carry context between tabs and between apps.
An app without its audit in `docs/apps/` has not been through the pass.

## 4. What the design system keeps

Everything in `Skin.cs` and `SkinSheet.xaml`, and the `Ui.cs` primitives. The app chrome — title
strip, tab strip, footer — is built from them in `Shell/AppWindow.cs`, and the per-tab shape in
`Views/Bench.cs`. One accent per app (the family hues), carried as a 2 px underline and the tab
strip's active mark, so two open apps can be told apart across a room.

## 5. The test

Point at anything and answer in one word: **app** (a ribbon button), **mode** (a top tab),
**data** (the grid), **option** (the right panel), or **the action** (bottom-right). If it takes
two words, it is wrong. If it folds, it is deleted.
