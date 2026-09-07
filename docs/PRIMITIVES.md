# Repeated components, and the primitives that replace them

Written because the same controls keep being reported broken one screen at a time. A source
picker with no search was fixed on the fall-hazard panel, then on the clearance panel, then
written a third way on the new-clash form — three implementations of one control, each with its
own bugs to find.

This is the audit: what is built more than once, where, and the one primitive each becomes.

The rule for the list below: a shape counts as repeated when two or more screens build it from
scratch. A shape built once and called from many places is already a primitive and is not
listed — `Ui.Table` (65 callers), `Ui.Picking` (63), `Bench.Pane`, `Parts.Ask`, `Parts.Sure`,
`FindingStatusWords`.

## What is repeated

### 1. The source picker

A control that says what a run reads from — a saved set, a whole model, the selection — and
lets it be changed.

| Where | How it was built |
| --- | --- |
| `ClearanceViews` | zone, measure-from, measure-to |
| `FallHazardViews` | floor, handrails, obstructions |
| `ClashTestViews` | the two sides of a clash test |
| `Pickers.Set` | an editable combo, filled lazily — used by none of the above |

Four shapes for one question. **Becomes `Parts.Source`.**

`Parts.Source` exists and has two call sites: `ClearanceViews` and `FallHazardViews`, each
wrapping it in a local helper. `ClashTestViews` and `Pickers.Set` still build their own, so this
item is half done and the list above still describes the product.

### 2. A list with ticks and a box that narrows it

Choose several things out of a list too long to scroll.

| Where | What is picked |
| --- | --- |
| `ClashTestViews` | the sets or models on a side |
| `MatrixReadWindow` | which tests to read |
| `CheckCreateViews` | which tests to act on |
| `ClashManageViews` | facet values |
| `ClashNaming` | which tests a pattern reads |

Each filters with its own `IndexOf(query, OrdinalIgnoreCase) >= 0` loop and its own empty state.
**Becomes `Parts.Ticked`.**

`Parts.Ticked` exists and has one call site, `ClashTestViews`. The other four still build their
own, so this item is barely started.

### 3. What values a field can hold

Statuses, parties, priorities, the groups and labels the results actually carry, the tests on
the seat.

| Where | For |
| --- | --- |
| `CoordinateViews.Offered` | the Filter tab's conditions and actions |
| `ClashManageViews` | the filter, sort and band menus |
| `ClearanceViews`, `FallHazardViews` | levels and categories |
| `SearchEditor` | whatever its caller supplies |

The same vocabularies derived four ways, so a status added in one place appears in three of the
four. **Becomes one vocabulary in Core, read by all of them.**

### 4. Ask the document, or answer nothing

```csharp
try { return supply(); } catch (Exception) { return Array.Empty<T>(); }
```

Three copies (`ClearanceViews.Named`, `FallHazardViews.Named`, `ClashTestViews.Named`), each
with the same comment explaining why. **Becomes `Seat.Offer`.**

### 5. Distinct, non-empty, sorted

`Where(non-empty).Distinct(OrdinalIgnoreCase).OrderBy(OrdinalIgnoreCase)` — 32 sites across 15
files. **Becomes `Ui.Listed`.**

### 6. The clash folders this document has

Derived from `check.Runs()` in `CheckCreateViews` and again in `ClashTestViews`, both written on
the same day. **Becomes `Seat.Folders`.**

### 7. A pane whose rows scroll and whose verbs do not

`ClashExchangeViews` documents the shape — verbs docked bottom, table as the filling child —
and `CheckCreateViews` re-derived it wrongly, putting the table inside a second scroller.
**Becomes `Bench.Listing`, and `Bench.Listed` for a pane a screen redraws in place.**

### 8. Searching results

`SearchEditor` writes a `ResultQuery` over `ResultFields`. The Manage tab's filter, sort and band
menus ask the same questions of the same results through their own facet machinery.
**Candidate for unification**, the largest item here and deliberately last: the Manage tab's
menus are multi-select over facets, which the search grammar does not yet express.

## Order of work

1. ~~`Seated.Offer`, `Ui.Listed`, `Seated.Folders`~~ — done.
2. `Parts.Ticked` — **written, not adopted.** One call site of the five screens listed above.
3. `Parts.Source` — **written, partly adopted.** Two call sites of the four shapes listed above.
4. ~~`ResultVocabulary` in Core~~ — done. The three a seat alone can name (the checks on this
   edition, the tests in the document, the models loaded) stay with the seat.
5. ~~`Bench.Listing` / `Bench.Listed`~~ — done, adopted in the Create tab and Exchange's four
   panes.
6. The Manage tab's facets against the search grammar. Still to do, and still the largest: the
   menus are multi-select over facets, which `ResultQuery` does not yet express, so it is a Core
   change before it is a UI one.

Each step replaces the hand-built copies rather than sitting beside them. A primitive that is
added and not adopted is a fifth shape, not a standard.
