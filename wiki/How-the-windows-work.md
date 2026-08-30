# How the windows work

Every CamelWorks screen is built the same way on purpose. Learn one and you can drive the rest.

## The ribbon

One tab, one flat row of twelve apps. A button opens that app's window. There is no workspace
switcher and no dock pane to arrange — an app window is a window, and you can leave several open.

Beside the apps sit the three things that are not apps: **Review**, **Section Box** and **Help**.

## An app window

```
┌──────────────────────────────────────────────────────────────┐
│  Clash Manage r                                              │
│  Manage │ Matrix │ Clearance Check │ Filter Clashes          │  ← the tabs
├────────────────────────────────────────────┬─────────────────┤
│                                            │                 │
│  THE STAGE — what you came to look at      │  THE SETUP      │
│  and work on                               │  what this run  │
│                                            │  needs          │
│                                            │                 │
├────────────────────────────────────────────┴─────────────────┤
│  [ THE ACTION ]   ⚙ ⚙ ⚙                                      │  ← the action row
└──────────────────────────────────────────────────────────────┘
```

- **The tabs** are the app's modes. They keep their state while the window is open, so going to
  another tab and back does not throw away what you read.
- **The stage** is the answer — a table, a verdict, a list of findings.
- **The setup column** carries only what changes from run to run.
- **The action row** carries the one main action, and beside it the symbols that open the
  configuration window.

Progress and a **Stop** button appear on the action row while something is running. Stop takes
effect at the end of the current batch, so nothing is left half-written.

## The other shape: a bench

A tab you *work in* for an afternoon — the clash results, the clearance findings, the issue
register — uses a second layout instead:

```
┌──────────────────────────────────────────────────────────────┐
│  🔍 search over both tables · what it covers · status ticks  │
├───────────────────────┬──────────────────────────────────────┤
│  Tests, sets, files   │  What the picked rows hold           │
│                       │                                      │
├───────────────────────┼──────────────────────────────────────┤
│  ▶ ✎ # ⇢ ⇕ 🗑 ⧉        │  ⊙ 👁 ✓ 👤 💬 ⬚ ● 📷 ⊕ ⧉             │  ← verbs
├───────────────────────┴──────────────────────────────────────┤
│  [ PRIMARY ]   ⚙ filters  ⚙ columns  ⚙ the round  …          │
└──────────────────────────────────────────────────────────────┘
```

Two tables side by side with a draggable split, one search across both at the top, and the verbs
under the table they act on.

## Tables

Every table in CamelWorks behaves the same way.

- **Sort** by clicking a column header; **resize** by dragging the divider.
- **Pick** rows the usual way — click, ctrl-click, shift-click; **All** and **None** are links
  under the table.
- The tally under a table reads **"3 of 218 rows picked"**, so you always know what a verb is
  about to act on.
- **The verbs sit under the rows they act on**, as symbols. Hover for the caption and the live
  count — *Assign 40…*, not *Assign…*. A verb with no natural shape keeps its words.
- **Right-click a row** and the same verbs appear as a menu. Right-clicking a row that is not
  picked picks it first.
- Verbs are greyed until something is picked.

Anything on a table can be copied out — **Copy** puts the picked rows on the clipboard as columns,
ready to paste into a mail or a spreadsheet.

## The configuration window

Anything decided once for a job — how a report is written, what counts as a change, which property
carries the code — is not laid out beside the work. It is behind a **⚙ symbol on the action row**
that names it.

The window it opens has its own list of sections down the left. Edits apply live; closing it
redraws what is behind it. It opens on the section whose symbol you pressed.

## Two-step writes

Anything that writes into the model or a file shows you what it would do first: a diff, element by
element, and a commit button that names the consequence — *Write 412 properties*. Nothing is
written on the strength of a preview you have not seen.

Deleting names what it is about to destroy, and asks twice.

## Hand-offs

When a screen finishes something whose natural next step is somewhere else, it says so and takes
you there — a health finding that needs Broken Links, a set of clash results worth raising as
issues, a level stamp whose point is quantities per floor. You are never told a tab exists and left
to find it.

## Nothing needs configuring first

Every screen derives what it needs from whatever is open, including an empty document, and says
what it derived and where it got it. **Model Audit ▸ Project** is where those derived answers live,
one override per line. It exists to correct a guess, never to be filled in before you start.
