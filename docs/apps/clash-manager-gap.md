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
- A baseline written from the board carries assignments; carrying each result's full native
  comment trail into the baseline is the named next step (the sync direction already handles
  comments completely).
