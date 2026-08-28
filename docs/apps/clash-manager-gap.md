# Clash Manager — gap analysis against the clash managers people actually use

The benchmark is the native Clash Detective plus the in-house and commercial clash managers
coordinators run beside it. Measured against them, the app as shipped had a board that derives
issues but no screen that *manages results* — and the board build froze on a real project
(160 tests, 18,844 results). Both are fixed by this pass.

## Why "Load the board" froze

Three separate costs, all paid on the UI thread:

1. **A full-federation element index, built unpumped.** The board wanted each result's element
   category, and got it by resolving the result's element key back through the document index —
   whose first use walks the entire federation with no progress pumping. The absurdity: the
   clash adapter *was holding the element* when it read the result, and threw everything but the
   key away. **Fix:** the adapter now carries the two elements' names and categories on the
   result record itself; the index is never touched.
2. **A whole-document elevation traversal per build** to derive level bands. **Fix:** the level
   set is derived once and cached against the federation's shape; a rebuild reuses it.
3. **Pumping once per test, not per result.** A test with 4,000 results blocked the window for
   its whole read. **Fix:** the read pumps per result.

## Capability gaps, and where each lands

| The benchmark managers have | We had | Now |
|---|---|---|
| Tests list with live per-status counts (total/new/active/reviewed/approved/resolved) | A bare test list with a raw total | **Manage tab**: counts per test, recomputed as statuses change |
| View results per test, with status, assignee, comment | Results flattened into derived issues only | **Manage tab**: the results grid |
| Edit status (5 states), bulk, with write-back into the model | No write-back at all | Seam + adapter write-back (copy test → mutate → replace), bulk verbs |
| Comment results (authored, appended) | — | Comment verb, written as native comments with author |
| Assign results to people | Group-level assign in the derived board only | Assign verb, written to the native assignee field |
| Group results by hand | Rule-derived groups only | Group-into verb, projected as native result groups |
| Focus camera on a clash point; select; isolate; show all | Select+zoom on derived groups only | Focus / Select / Isolate / Show all verbs per picked results |
| Temp sphere markers at clash points | Marker engine existed but only on the report tab | Markers verb on the picked results, colour by status |
| Filter/sort tests and results (status, assignee, text) | — | Filter row over both grids |
| Bulk rename/delete/move tests; sequential result renaming | Create and run only | Test verbs on the Manage tab |
| Run all / run selected from the manager | Run from the Tests tab only | Run verbs beside the tests grid |
| Export XML with full control over which tests/results (status, assignee) | All-or-nothing export | Export leg reads the manager's picked tests + status filter |
| Load from the model or from an exported file | Model only (file went to a separate reader) | Both are the manager: a source switch on the Manage tab opens either, with the same grids, filters and verbs |
| The offline review round: baseline out, reviewers mark up the file on seats with no clash engine, markups sync back | Nothing | The whole round (see below) — the file leg is the Manage tab's other source, and it needs no clash engine at all |
| Matrix by hand or from a spreadsheet | Already there (Matrix tab reads the client's sheet and builds the tests) | Unchanged |
| Per-result saved views and rendered snapshots | — | Focus on one result applies the engine's own composed view; Snapshot writes the host's rendered image (scene plus highlight) per picked result to `clash-snapshots` beside the project |
| Sort any grid | — | Click a column header, on every table in the product; click again to turn it around |
| Worst-distance column | Native shows it | Shown in honest units (mm/m), sortable |

## Beyond what any of the benchmarks have

- **Property columns and a chained grouping over them.** Add a column that reads any element
  property off the clashing elements themselves — picked from the model's own tabs and
  properties, from the first item, the second, or both — then chain the grouping: Level, then
  System, then anything a column carries, in any order, reordered live. The chain renders a
  group-summary table (counts per band, bulk mark/assign/comment on picked bands) that narrows
  the results grid, and both the columns and the chain go out in the spreadsheet. Values are
  read once, in a pumped pass, and survive re-reads.

- **Cross-test duplicate finder.** The same pipe through the same wall reported by two tests is
  one conflict; no native window can see that. One press finds them (the board's own collapse
  engine, run over the manager's rows) and, if asked, marks each duplicate Approved with a
  comment naming the result that stays open — in the model or in a markup file alike.
- **Full comment trails**, read in one pass and travelling in the baseline, so the file a
  reviewer opens carries the whole conversation, not the last word of it.
- **The grid to a spreadsheet**, exactly as filtered — the "just send me the list" answer
  without a report template.
- **A full-fidelity baseline from the manager itself** — statuses, assignments, comment
  trails, groups and distances — from the picked tests or everything, one dialog.

## What deliberately stays ours

The derived board (dedupe across tests → carry-over → suppress/flag → group → assign) is the
part none of the benchmarks have, and reports/BCF/freeze all read it. It keeps its own tab. The
Manage tab is where a coordinator works the engine's results; the Board is where the noise has
already been decided away. The two agree because the manager's status edits write into the
model, and the board reads the model.

## The review round — the workflow the whole thing serves

1. **Manage seat** — Manage tab, "Send the picked tests out as a file..." writes the baseline
   XML: every result with its stable id, status, assignee, comments and grouping.
2. **Every reviewer, on Simulate** — the same Manage tab, source switched to "A file": the same
   grids, counts, filters and verbs, working entirely on the file's own data. No clash engine
   needed, because none is used. "Save the markup" writes `markups\<reviewer>.xml` beside the
   baseline — never the baseline itself, never anyone else's file.
3. **Manage seat again** — "Bring the markups into the model" reads every markup beside the
   baseline, oldest save first (where two reviewers disagree, the later save wins), and writes
   the statuses, comments, assignments and regroupings into the model's own clash data. The
   adapter refuses to write the same author saying the same thing twice, so a sync re-run never
   doubles a conversation.

The file format is the Navisworks-shaped exchange XML every other tool already opens; the
markup fields (assignee, authored comment trail) ride as attributes and elements a foreign
reader shrugs past, and each result's id is derived from its clash key, so the same clash keeps
the same id across exports.

## Engine honesty

- Writing into the *model* requires the clash module (Manage edition) and a writable document;
  on Simulate the model source is off and the tab says why — but the file source is fully
  writable there, because the review round is the Simulate seat's whole job.
- New/Active/Resolved are the engine's own verdicts and are recomputed by the next run;
  Reviewed/Approved are human judgement and survive. Said on the tab, not discovered.
- A baseline can be written two ways: from the board (assignments travel) or, at full
  fidelity, straight from the Manage tab - statuses, assignments, whole comment trails,
  groups and distances.