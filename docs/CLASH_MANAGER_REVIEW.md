# Clash Manager — design review

All 28 renderable Clash Manager surfaces were drawn and read: the six tabs
(Create, Manage, Filter, Exchange, Report, Clash matrix), the Settings sections,
the matrix editor in six states, the naming window, the exchange import round,
and the empty and blocked states of each.

This document is the finding list. It is split into what belongs in the design
system — one change, every screen — and what is a single screen's problem. The
instruction on this work is that fixes go into the design system wherever a
pattern is shared, so that split is the operative part.

## The one-sentence diagnosis

Every interactive surface in the product is the same object: a 31px near-white
rounded rectangle with a 1px `#CDD3DA` border and an 8px radius. A text box, a
dropdown, a secondary button and a disabled field differ by a 1.01:1 step in
fill, one point of font size, and the presence or absence of a 7px chevron.
Blandness, weak hierarchy and "I cannot tell what is clickable" all follow from
that single missing distinction.

Measured from the skin sheet:

| Control | Radius | Border | Fill | Height | Font |
| --- | --- | --- | --- | --- | --- |
| `TextBox` | 8 | 1px `FieldBd #CDD3DA` | `Field #FFFFFF` | 31 | 13 |
| `ComboBox` | 8 | 1px `FieldBd #CDD3DA` | `Field #FFFFFF` | 31 | 12 |
| `Button` (secondary) | 8 | 1px `FieldBd #CDD3DA` | `Card2 #FBFCFD` | ~27 | 12 |

Three classes of control, one appearance.

## A. Where the plug-in stands against the BIMCamel web system

The web product has a token file (`theme.ts`), a primitive library and a written
structural spec. The plug-in has a palette table and a skin sheet. They disagree
on the brand itself and on nearly every scale below it.

| Role | Web | Plug-in | Consequence |
| --- | --- | --- | --- |
| Brand blue | `#2563eb` | `#0070C0` | Two different brands in one product family. Contrast is unchanged by the swap: 5.15:1 against 5.17:1 on white. |
| Accent hover | `#1d4ed8`, darker (6.7:1) | `#0D83DA`, lighter (3.98:1) | The plug-in's hover moves the wrong way: a hovered link is less legible than at rest. |
| Primary CTA | 135° gradient `#2563eb → #4f46e5`, with a `0 4px 14px rgba(37,99,235,.35)` glow | `BrandBar`, a horizontal `#0070C0 → #0D83DA` wash | The plug-in's primary is a flat blue box; the web's separates from every outlined control by material, not only by fill. |
| Accent wash | `#eff6ff` | `#E6F0FA`, the same value as the info tint | "Armed" and "for information" are the same colour. |
| Chrome tier | ink `#0b1220` | none | No counterpart for floating chrome over the model canvas. |
| Severity | five rungs, `soft`/`softFg`/`solid` | `Tone.Good/Warn/Bad/Plain` | The same clash severity cannot be drawn the same way in the two products. |
| Radius | a scale: 6 / 8 / 12 / 14 / 18, with `inner = outer − gap` for nesting | eight values in use, no tokens | Nesting is eyeballed. |
| Spacing | 4 · 8 · 12 · 16 · 20 · 24 · 32 · 40 · 48 · 64 | one constant, `Ui.Gap = 8` | Every other margin is a literal at the call site. |
| Control height | xs 24 · sm 32 · md 40 · lg 48 | `MinHeight 31` and a dozen literals | No density axis; nothing can be compact or comfortable on request. |
| Type ramp | 11 · 12 · 13 · 14 · 16 · 18 · 20 · 24 · 28 · 32 | 11 · 12 · 13 · 16 · 18 | A window title and a card title are the same size. |
| Focus | one global rule, `2px rgba(37,99,235,.55)` at 2px offset | `TextBox` and `RowBtn` only | Eleven control styles have no visible keyboard focus at all. |
| Disabled | two explicit-value tokens, never opacity | four mechanisms: explicit, 0.5, 0.55, 0.75 | The same state fades differently in the two themes. |
| Elevation | seven shadow tokens | two inline shadow effects | Popovers, cards and modals sit at the same depth as the page. |
| Placeholder | pinned `#64748b` at opacity 1 | foreground at opacity 0.55 | Composites differently on every surface it is drawn over. |

### How the web keeps three controls apart

The web separates control classes on four axes at once, and no two classes agree
on more than two of them:

- **Text input** — radius 8, 1px `#e2e8f0`, white, no shadow, text caret.
- **Select** — radius 8 and the same border, plus a reserved 32px right gutter
  and a 12px chevron drawn into it. The gutter is the point: a long value
  truncates before it reaches the arrow. When disabled, the chevron takes the
  disabled ink too, so the control reads as unavailable rather than broken.
- **Filterable picker** — deliberately not the field box at all: radius 12, a
  pill, visibly a different species from both of the above.
- **Read-only value** — no box. Plain ink at label weight, or a tinted pill.
  A boxed but inert control means one specific thing: a field you cannot type in
  *now*.
- **Button** — radius 12 rather than 8, a gradient or a tint or an outline,
  a shadow on the primary, minimum height 40.

The plug-in has one of these five.

## B. Visual and affordance findings

Ordered by damage. `[DS]` is a design-system fix; `[SCREEN]` is one surface.

1. **`[DS]` A text box and a dropdown are the same rectangle.** Seen on
   `settings-units` (Lengths in / Grouping distance, 45px apart, identical),
   `new` (Name / Folder, adjacent on one row), `report` (Document / Folder /
   File name, three identical boxes stacked), `rules-set` (Rule name / Do this /
   Assign to), `matrix-line` (seven identical boxes, some holding values from a
   fixed vocabulary and some free text, with no way to predict which).
2. **`[DS]` Disabled reads as a third kind of control rather than the same
   control switched off.** `create` draws three grades on one form: live text
   boxes in black, blocked pickers in grey with grey chevrons, and a live
   dropdown with a dark chevron. Nothing says which grey means "unavailable" and
   which means "computed".
3. **`[DS]` Peer actions render at four affordance grades in one 40px strip.**
   `exchange`: "Include 0 tests" is a bordered button, "Exclude 0 tests" is
   plain grey text that looks disabled and is not, "All"/"None" are links.
   `exchange-import-read` does the same to a symmetrical pair — Accept is a
   button, Reject is a label.
4. **`[DS]` The primary action is applied by habit, not by rule.** It is correct
   on Manage, Exchange, Settings and the naming window. It is absent on
   `rules-set`, where "Run all rules" — which rewrites every clash result in the
   project — is drawn at the same weight as "Save rules"; and on the matrix
   toolbar, where "Build the tests" is the same white pill as "Undo".
5. **`[DS]` Two competing accents, and neither is the brand.** The active tab is
   marked with an orange underline. Orange is otherwise the warning colour:
   warning triangles on `matrix-drawn` and `new`, dropped-row stripes on
   `clearance-surfaces`. The app's most persistent signal — you are here — is
   drawn in the colour it reserves for something being wrong.
6. **`[DS]` Chips carry five meanings with one geometry.** On `manage` alone,
   the Status column is a read-only pill and the Assigned column is a dropdown
   pill of the same size and radius; on `finder` the discipline pills are
   toggles whose off state is identical to the Assigned dropdown; on
   `naming-tests` the Pattern pill is a button that opens a picker; on `create`
   the level chips are removable tokens.
7. **`[DS]` Pressable cards look inert.** `matrix-line`'s Source card says
   "Press to change it." in prose. The five cards below it are also pressable
   and say nothing. The matrix start screen delegates its affordance to a text
   link inside each card.
8. **`[DS]` No height, radius, gutter or width scale.** Six input heights across
   the set (28, 30, 30, 32, 34, 36). Four radii (4, 6, 8, 14). Label-to-control
   gaps of 6, 10 and 14px. The Manage board draws 49px rows in the right pane
   against 32px rows in the left pane of the same screen.
9. **`[DS]` The type ramp is two sizes and two weights.** On `create` the
   section headings "What is checked" and "How much room" are ~11px — smaller
   than the values they govern. A heading drawn smaller than its content cannot
   organise it.
10. **`[DS]` Icons are unlabelled and inconsistent.** `manage` puts four bare
    16px glyphs in a row where a funnel and a slider both plausibly mean filter.
    `naming-write` has the only grey-filled dropdown in the product, which reads
    as a chip.
11. **`[DS]` The pane content column is neither aligned nor centred.**
    `settings-naming` starts its content at x≈350 inside a pane that starts at
    x≈190 and ends 160px short of the right edge.
12. **`[SCREEN]` `exchange`: the Kind value and its link collide** — the screen
    renders "ClashChange on Manage" with no gap.
13. **`[SCREEN]` Layout budget is inverted on `report` and `create-fall`.** The
    form is in the narrow rail; the empty state gets the wide pane. On `report`
    roughly 70% of the tab is empty grey. On `settings-units` it is 78%.
14. **`[SCREEN]` `clearance-surfaces`** gives the Level column 850px to hold
    "Level 03" and crams Area, Top and In the check into the remaining 290.
15. **`[SCREEN]` The matrix's three views use three unrelated colour scales.**
    `matrix-drawn` fills a normal cell with red letters on pink, so a healthy
    fully-decided matrix reads as an alarm; `matrix-results` is a blue-to-red
    heatmap; `matrix-priority` is categorical. Red means three different things,
    and `matrix-drawn` is the only one of the three with no legend.
16. **`[SCREEN]` `rules-set`: "Delete rule" is tinted red and "Run this rule" is
    quiet** — destructive outranks constructive in the same panel.

## C. What is already right and must survive

This section is not padding. These are the parts a redesign is most likely to
flatten by accident.

1. **The status colour language on Manage.** New, Active, Reviewed, Approved and
   Resolved hold stable hues across the board and the filter panel, and the row
   stripe repeats the pill's hue so status can be scanned down the gutter
   without reading. It is the one working semantic palette in the product.
2. **Blocked actions state their reason.** Every disabled primary in the set
   carries a specific, model-aware sentence naming the missing precondition:
   "No model is open, so there is nothing to measure the height over." The check
   picker puts the reason on each unavailable option. This is better than the
   category does it, and a standardised footer is exactly what would lose it.
3. **The matrix axis banding.** Rotated discipline labels in a tinted gutter,
   matched to tinted group headers above the columns. A correct, low-ink answer
   to a hard two-axis grouping problem.
4. **Legends sit with their data.** The results view names both ends of its
   scale and gives the "no test covers it" case its own swatch.
5. **The rename preview.** "What it would write", with unchanged rows saying why
   they are unchanged and only the changing values in accent. The best use of
   colour in the product.
6. **Restraint.** No ornament, no faux depth, tabular figures aligned. The app is
   bland partly because it is disciplined. The fix is to spend the saved
   complexity on affordance and hierarchy, not to add decoration.
## D. Does the user know what to do?

### D1. First run

A coordinator opening the app on a federated model with no project sees the
Create tab: two buttons, a search box, an empty-state card defining what a test
is, and a blocked `Run all` with its reason. That is a better first screen than
most plug-ins ship, and it is the whole of the orientation.

Nothing on any tab says what the product is for, what a coordination round is,
or in what order the tabs are meant to be used. The paragraph that does say it —
"The weekly cycle is the front door: bring in the new models, run the rules over
what that broke, send out the result" — lives in `HelpWindow` and is reachable
only from the CamelWorks hub. Create and Manage have no ⓘ at all, while Filter,
Report and Settings each have one.

**Tab order does not match task order.** The strip reads Create · Manage ·
Filter · Exchange · Report · Clash matrix · Settings. The actual first-run
sequence is Settings (declare storeys, or clearance and fall tests have no
storey list) → Clash matrix (draw the round) → Create (run) → Filter → Manage →
Exchange or Report. Two of the first three steps are the last two tabs.

Nothing states that a project database is created, where it lives, or that
decisions are kept beside the model. The one screen that says so is Manage,
which a first-run user has not reached.

### D2. The main action

| Tab | The action | Where it is |
| --- | --- | --- |
| Create | Run the test | Footer, bottom right |
| Manage | Read the results, then decide them | Footer |
| Filter | Run all rules | Footer, bottom right |
| Exchange | Write the packs / Apply to the board | Footer, bottom right |
| Report | Write the report | Footer, bottom right |
| Clash matrix | Build the tests | **Toolbar, top right, among ten peers** |

Six tabs put the primary in the footer beside its blocking reason. The matrix —
the surface with the most block conditions — puts it in a toolbar and hides the
reason in a tooltip. That tooltip is a good three-sentence diagnosis that no
screenshot can show and most users will never trigger.

Two further problems: on Create, with nothing selected the primary runs
everything, so on a project with eighty tests the safest-looking state is the
most expensive press. On Manage, the row verbs a coordinator uses constantly —
Focus, Isolate, Comment — appear only on hover and only as unlabelled glyphs, so
a board at rest shows no affordance saying a row is a place in the model.

### D3. Setting up a test

- **Clearance** — six presses to something runnable, edits written through as
  they are made. The panel never explains what a Navisworks saved set is, or
  that the source lists come from the document rather than the project.
- **Fall hazard** — one press to create, then nine fields, three of which are
  concepts the product invents and does not define on the panel: band above and
  below, grid resolution, and handrail coverage distance.
- **Clash** — nine to twelve interactions through a form: name, folder, two
  sides each with a mode and a ticked list, kind, tolerance, then make it, then
  run it.

Two notices give different answers to one question. The clearance panel says
storeys are worked out in Settings; the fall-hazard panel names a different
window. The Settings tab has the button.

### D4. Required and optional

**There is no required-field mark anywhere in the product**, and the word
"Optional" appears exactly once in the whole Views folder. Three different
things happen when a needed input is empty:

- **A footer reason** — the primary is dead and says why. Correct, and used on
  Create, Report and Exchange import.
- **An inline note under a group** — "This test does not say where to cut."
  Acceptable, used on fall hazard.
- **A post-press error log** — the new-clash form is always live; press it empty
  and two errors arrive in sequence, in a log panel away from the fields they
  are about. Worst of the three, and it is the form with the most inputs.

The skin sheet already declares `BadgeReq`, `BadgeOpt` and `BadgeAuto` styles.
They have zero call sites. The vocabulary was designed and never wired up.

### D5. Automatic and manual — the save contract

This is the weakest area in the product and it is a design-system problem.

| Surface | Contract | Stated on screen |
| --- | --- | --- |
| Create, clearance and fall settings | Automatic, every settled keystroke | **No** |
| Create, new clash test | Manual, `Make the test` | By the button |
| Manage | Row edits written as made, plus a `Save changes` primary | **No** |
| Filter | Manual, nothing written until `Save rules` | After the first edit |
| Settings | Manual, with "Not saved yet" beside the primary | Yes |
| Clash matrix | Automatic on a timer, plus a manual `Save` | Yes — "Saving as you go" |

A coordinator can edit a clearance height on Create (already saved, no sign of
it), a rule on Filter (not saved until pressed) and a status on Manage (written,
but a blue `Save changes` implies otherwise) inside two minutes. Three tabs,
three truths, and the tab that saves silently is the one that says nothing.

`Save changes` on Manage is worse than silent: it is always enabled by design,
and rewrites its own caption only when the Navisworks sync has work. Resting, it
means either "everything is already written" or "the document will not take it".
A permanently lit primary is not a save indicator.

Other automatic behaviour that is never announced: test auto-naming, grouping by
the project's grouping distance, banding by level.

### D6. Feedback

The runner is real — the button disables, a Stop appears, a status line counts
work. It is **indeterminate everywhere**, so "how long will this take" is
unanswerable on the one operation where it is always asked, a clash run over a
federation.

Only Create answers "where did the result go" with a press (`View in Manage`).
Filter names the destination in prose with no link. The matrix, after building
tests, reports the count and strands the user with no route to Create.

### D7. Dead ends

1. A matrix full of symbols with an empty legend: `Build the tests` is greyed
   and its only explanation is a tooltip, on a strip the user can dismiss.
2. "Lines pointing at nothing: 4" is a fact with no control attached.
3. The check picker with every entry barred, and no line saying what to do.
4. The new-clash form with no document: both sides are empty lists with no way
   forward.
5. Report with no model: fifteen live controls above a dead primary, so five
   minutes can be spent configuring a report that cannot be written.
6. Terms defined behind a toggle or on another sub-tab: grouping distance,
   same-conflict distance, band above and below, grid resolution, pattern names.
7. Zero-count buttons: `Include 0 tests`, `Accept 0 answers`, `Take out 0
   packs`, `Include these 0`. Live-looking, unable to act, silent about why.
### C7. The three Settings sections the first pass missed

Team, Disciplines and Manage had no harness page, so the first review covered
three of six sections. Drawn now, the Team section is the most extreme instance
of every finding in section B.

**Settings ▸ Team.**

1. **Forty-two controls in a six-by-seven grid.** Name, Email and BCF folder are
   text boxes; Role, Team and Company are dropdowns. They interleave, and
   nothing but a chevron separates them. This is finding B1 at its worst.
2. **Six of the seven columns clip their content** at 1150px: "Project archi",
   "Setting-out e", "Vos Architect", "Meridian ME", "d.whitfield@northga",
   "s.haddad@meridian". The one thing a roster is for — reading who is on the
   job and how to reach them — is the thing this table cannot do.
3. **The Team column is an empty dropdown on every row**, with no value and no
   placeholder, so an unset field and a broken one look the same.
4. **The roster knows who has left the job and the table does not show it.**
   `TeamMember` carries an active flag; the inactive member is drawn exactly
   like the other five.
5. **Six identical Browse buttons** for a field most projects never set carry
   more visual weight than the six people's names.
6. **A roster is read far more often than it is edited, and there is no read
   state at all.** Every value is a live control.
7. "Remove from the roster" is another zero-count verb.

**Settings ▸ Manage.** The nav entry says "Manage" and the section heading says
"Bands" — the only place in the product where a section does not agree with its
own nav. Its whole content is three lines and one dropdown in a 1180×412 pane,
and the sentence saying there are no bands sits above the control that would add
one rather than in the empty state.

**Settings ▸ Disciplines.** On a project with no models the section is one empty
state, correctly written. It is centred in the pane while the heading above it is
left-aligned, so the two do not share an axis — finding B11 again.

## E. Do the seven tabs read as one application?

No. Counted across the set:

- **Four page-header idioms.** Title + subtitle + ⓘ (Settings); a bolded
  sentence acting as a header (Filter); a toolbar and no title (Matrix,
  Exchange); no header at all (Create, Manage, Report). Report is the clearest
  symptom: it opens with no title and an orphan grey sentence — "Covers the
  clash board: clash results, and issues raised by hand." — which is plainly the
  subtitle of a header that does not exist.
- **Five ways to say how many rows are picked**, using five nouns for one idea:
  *picked*, *rows*, *selected*, *included*, *in*. Exchange manages two of them in
  one pane — the panel header says "6 of 6 tests included" and its footer says
  "6 rows · Include 0 tests".
- **Five renderings of "no model is open"**: an empty-state card (Report), a
  footer sentence (Create), a yellow strip (new clash form), a body paragraph
  (Filter), a red strip stacked at the top (Matrix).
- **Four panel-title idioms** and **three footer depths** — Exchange import
  stacks four horizontal bars at the foot of the screen.
- **Status in three renderings**: coloured pills on the Manage rows, coloured
  numerals in the Manage header, plain text on Exchange, and on Exchange import
  the disputed row is marked only by an orange left edge — colour is dropped
  exactly where a trade is deciding whether to accept an answer.
- **Five run verbs**, two of them unlabelled glyphs. Manage has two unlabelled
  play buttons on one screen that do different things.
- **Four search boxes**: two verbs (Search, Find), icon or no icon, label above
  or placeholder inside.
- **The tab named Filter does not filter.** It runs rules that mutate the board.
  The two surfaces that do filter are on other tabs and are called something
  else.

### What the category expects and does not find here

1. **No run summary.** After a run, the whole answer is one footer sentence: "6
   tests, 96 results read. 0 new, 96 already known." No duration, no per-test
   result, no failures, no regressed count.
2. **No progress during a run.** Nothing in 28 renders shows a run in flight,
   and every runner in the product is indeterminate.
3. **Severity never reaches the board.** Priority is ranked per matrix cell with
   a good legend, and appears nowhere on Manage, in the export table, or in the
   report options. A trade receiving a pack of 96 clashes cannot tell which
   matter.
4. **No route from a result to the model on a board at rest.** Focus and Isolate
   are well built and hover-only. The "First item" and "Second item" columns are
   empty on every row, and Overlap is "—" on sixteen of eighteen. A row reading
   "Clash9 · — · Approved · Mechanical" carries nothing a coordinator can act on.
   This is the largest single category gap.
5. **No batch actions where the checkboxes are.** Manage has a select-all
   checkbox and sixteen row checkboxes and no verb bar bound to them. Create has
   exactly that verb strip, for a list of two rows.
6. **Grouping is shown, not explained.** Two groups and an "In no group" band,
   with nothing saying what made them and no route to the rule that did.
7. **The issue lifecycle stops at status.** Assigned is a dropdown of
   disciplines, not people, though the project holds a team roster. No due date,
   no visible comment thread, no history.
8. **No overview.** The app opens on Create. The closest thing to a dashboard is
   two levels inside the matrix tab.

### What this product does better than the category

Stated plainly, because these are the parts most at risk in a redesign.

1. **The matrix is a coordination artefact that builds tests.** The
   discipline-by-discipline grid that elsewhere lives as an Excel appendix to the
   BEP, transcribed by hand into Clash Detective, is here the thing that
   generates the test set — and it reads back the other way, from the document's
   existing tests or the client's own workbook.
2. **Coverage as a first-class number.** "162 of 190 pairs decided, 28 left to
   decide", with a press that goes to the first undecided pair. No tool in the
   category can say what has *not* been tested.
3. **Untested and zero are told apart.** "Nothing open — tested, and everything
   on it is decided" against "No test covers it — a hole in the coordination".
   Every competitor collapses these into an empty cell, and that collapse is how
   coordination holes ship.
4. **Triage before the run.** Ranking pairs H/M/L before there are 18,000
   results is the right order of operations, and nothing in the category
   supports it.
5. **A fall-hazard check**, with the geometry fallback stated honestly: "31
   elements would not give triangles; their bounding-box tops were used
   instead."
6. **Test naming as a project system**, with a rename preview that names the
   reason per test — including the tests the naming cannot write.
7. **A BCF merge shown before it is applied**, party by party, with the status
   mapping spelled out in words. The tools that dominate this market merge
   silently and report afterwards.
8. **Blocked states that name the reason per capability**, rather than greying a
   menu item and saying nothing.

## F. States the product has never drawn

None of these appear in any of the 28 renders.

| State | What is needed |
| --- | --- |
| Running | Per-test progress in the rail, elapsed time, a cancel, and the footer summary replaced by a live counter. The matrix coverage meter is already the right component. |
| Run finished | Tests run and their durations, results new / carried / regressed / gone, tests that failed and why, each with a route. |
| Error | No failure state exists anywhere — not a failed run, an unreadable pack, a failed report write, or a lost host connection. |
| Partial results | A run that completed three of six tests, or a test that hit a result cap, has no rendering. |
| Stale results | A test whose source model changed since it last ran is not marked. This is the state coordinators are burned by most often. |
| Licence-limited | Stated on the check picker and buried among three warnings on the matrix. Manage, Exchange and Report say nothing. |
| Unsaved changes | No dirty indicator on Manage or Settings; no discard; no warning on leaving a tab. |
| Large data | Every matrix render is 20×20 and the board shows 16 rows. There is no truncation, paging or "showing first N" state, and no rendering of a 40-discipline grid where the row labels no longer fit. |

## G. The state of the design system itself

Measured, not estimated.

**Dead weight — 7 of 29 named styles have zero call sites:** `Title`,
`BadgeReq`, `BadgeOpt`, `BadgeAuto`, `IconBtn`, `OptionCard`, `TabBtn`. Five
palette tokens are also unused: `Veil`, `ConsoleBg`, `ConsoleTx`, `AccentHover`
and, all but once, `OkBd`. Roughly 370 lines of `Ui`/`Parts` primitives have no
callers, including a complete disclosure control that `SectionStyle` exists to
serve.

**The root cause of the flatness is two pairs of identical tokens.**

- `Field` is `#FFFFFF` and `Card` is `#FFFFFF` — **1.00:1** in the light theme.
  The palette's own comment says "In light the input is the brightest surface on
  the panel, which is what makes it read as *type here*". The values do not
  implement the rule the comment describes. In dark they do.
- `FieldBd` and `BdStrong` are the same colour in **both** themes.
- `Card2` (the secondary button fill) against `Card` is **1.02:1**.

So the sheet has no colour with which to separate an editable surface from a
display surface, and falls back to separating controls by radius and height —
which differ by one to three pixels.

**Focus:** two of thirteen templated controls draw a focus state, and the two
use different geometry. Everything else falls back to WPF's dotted default.

**Elevation:** five hand-written drop shadows in three different specs, none
carrying a colour, so every one of them is black in the dark theme; two omit
`Direction`, so the popup shadow and the floating-panel shadow fall in different
directions.

**Scales:** ten distinct corner radii, none named. 678 `new Thickness(` literals
in 191 distinct shapes. `Ui.Gap` — the one spacing constant — has zero external
consumers.

**Type:** the 18px step is dead, so the effective ramp is 16 / 13 / 12 / 11 —
four steps inside five points, three of them one point apart. `Caps` and `Hint`
are the same size and colour and differ only in weight. `Size.Small` governs
both the secondary label style and the caption of fifteen control templates, so
label size cannot be changed without resizing every button in the product. 64
call sites set `FontWeight` inline because there is no strong-body style.

**Bypass rate — better than expected, and concentrated.** 31 of 63 view files
have zero inline styling and will be reached by a sheet-level change alone. Of
the 815 inline operations, 34% are inside `Ui.cs`, `Parts.cs` and `Bench.cs` —
the design system bypassing itself, which is centrally fixable — and 40% are in
the five matrix files, which are a bespoke drawing surface. The remaining 26%
across 46 screens is almost entirely margins, i.e. missing spacing tokens rather
than colour bypass. Only six screens ever assign a brush directly.

**Two prior claims do not survive checking.** `docs/PRIMITIVES.md` records
`Parts.Source` as replacing four hand-built pickers; it has two call sites.
It records `Parts.Ticked` as replacing five; it has one. That consolidation is
about 40% done, and the document says otherwise.

**One defect found while measuring.** `MatrixLook`'s ten wash hexes are meant to
be the first eight of the palette's `Sym.A`–`Sym.H`, so that choosing a symbol
colour deliberately gives back the one already in use. Positions 2 to 6 are in a
different order, so that holds for three of eight. They are also light-only, so
the picker's swatches stay light in the dark theme while the cells they set do
not.
## H. The work, in order

Everything in this section is a change to the design system unless it is marked
otherwise. The ordering is by screens fixed per unit of work: each item says
what it reaches without any screen being edited.

### H1. Make the three control classes different objects

The whole of the "everything looks the same" complaint is here.

1. **Separate the field surface from the page surface.** `Field` moves off
   `#FFFFFF` in the light column, and `FieldBd` separates from `BdStrong`.
   Reaches 329 field call sites (`Ui.Text`, `Ui.Settled`, `Ui.Choice`) plus
   every directly built box.
2. **Give the dropdown a chevron well.** A reserved right gutter with its own
   edge, not a hairline glyph floating over the value, so a long value truncates
   before it reaches the arrow, and the disabled state takes the chevron with it.
3. **Give the secondary button its own fill**, off `Card2`, and move buttons to
   a larger radius than fields so an action and an input are different shapes.
4. **Give a read-only value no box at all** — ink at value weight on the page
   ground — and reserve the bordered-but-inert rectangle for one meaning: a
   field you cannot type in now.
5. **Add a tonal weight** between primary and secondary, so a screen with two
   affirmative actions does not have to draw them both as outlines.

### H2. Add the four missing token families

6. **One focus ring**, applied in every control template. Eleven of thirteen
   have none today.
7. **Elevation tokens** — resting card, raised popover, modal — with a
   theme-aware colour and one direction. Replaces five hand-written shadows and
   fixes the black-shadow-in-dark-theme defect at all five.
8. **A radius scale.** Three named steps, collapsing ten ad-hoc values.
9. **A spacing scale.** Four steps, exposed to C# and to the sheet. This cannot
   retro-fix 678 literals, but it stops the count growing and makes the 191
   distinct shapes auditable.

### H3. Repair the type ramp

10. Make the dead 18px step a real screen-title style, distinct from a card
    title.
11. Split the label size from the control-caption size, which are one token
    today, so labels can be changed without resizing every button.
12. Add a strong body style, removing 64 inline weight assignments.
13. Merge the two 11px styles that differ only in weight.

### H4. Give the product the vocabulary it is missing

14. **Required, optional and automatic marks on fields.** The styles exist and
    are dead; wire the vocabulary into `Ui.Field` so a form can say which of its
    inputs must be filled. Today the word "optional" appears once in the whole
    Views folder and nothing is ever marked required.
15. **A save-state line in the footer anatomy**, filled by every screen:
    "Saving as you go" / "Not saved yet" / "Saved". Five surfaces have four save
    contracts and only two of them say so.
16. **Form-completeness in the blocking-reason contract.** The footer already
    binds a reason to a disabled primary for seat-level blocks; extend it to
    missing inputs, so the new-clash form states "Both sides need something on
    them" before the press rather than after it.
17. **One state banner** — blocked, licence-limited, error, stale — one icon and
    one colour each, always directly under the page header. Replaces the five
    renderings of "no model is open" and gives the missing states somewhere to
    be built.
18. **A determinate progress contract**, so a run over a federation can say how
    much is left, and an outcome that carries a route to where the result landed.
    Only Create has that route today.
19. **One status token**, drawn identically on the Manage rows, the Manage
    counters, the Exchange export table and both import columns.
20. **A severity token**, carried out of the matrix onto the board, the export
    table and the report.
21. **One chip doctrine.** Five hand-built chip shapes at three radii and three
    heights become four named roles on one geometry: static metadata, status and
    count, interactive toggle, severity.

### H5. Structure the page

22. **One page header** — title, one-line subtitle, optional metrics, optional
    ⓘ. Settings is already the reference; six tabs do something else.
23. **One command bar.** One footer per screen: the blocking reason, then
    secondaries, then exactly one primary at the right. Fixes the grey primaries,
    the three peers on Filter, the primary in the matrix toolbar, the bottom-left
    primary on the naming window and the four stacked footers on Exchange import.
24. **One list footer and one selection noun.** Keep *selected*; retire *picked*,
    *included* and *in*. Count verbs disable at zero and say "tick rows first".
25. **One empty-state card**, centred, with its action inside it, and never used
    for a blocked state.
26. **One search field.**
27. **A pane content-column token**, so a pane's content is either aligned to
    the pane or centred in it, and not neither.

### H6. Reconcile with the web product

28. Move the accent to the BIMCamel brand blue, reverse the hover direction so
    hovering darkens rather than lightens, split the accent wash from the info
    tint, and make the primary gradient the brand's 135° blue-to-indigo rather
    than a horizontal blue-to-blue wash.

### H7. Clear the dead system

29. Delete or wire up the seven dead styles, the five dead palette tokens and
    the ~370 lines of primitive with no callers.
30. Finish the consolidation `docs/PRIMITIVES.md` claims is complete, and
    correct that document.
31. Point the matrix's wash picker at the palette's symbol ramp, fixing both the
    ordering mismatch and the light-only swatches.

### Screen-level work, after the system lands

Several of these dissolve once H1–H5 are in place; these are the ones that will
not.

- **Manage** — fill the empty "First item" and "Second item" columns; put a
  visible route to the model on every row rather than on hover; bind a batch
  verb bar to the checkboxes that already exist; say what made a group; make
  Assigned a person from the roster rather than a discipline.
- **Report** — the form is a 340px rail beside 810px of empty pane. Invert it.
- **Filter** — rename the tab; it collides with two other things called filter.
- **Create** — with nothing selected, the primary runs everything; make the
  default explicit.
- **Matrix** — move the primary to a command bar, collapse the three stacked
  warning strips into one, give the toolbar one button shape, and give
  "162 cells skipped" a route to the skipped cells.
- **Matrix start** — four equal cards with no primary; rank them.
- **Exchange** — the Kind value and its link collide; the panel header and the
  footer state the same count twice.
- **Clearance surfaces** — 850px for a Level column holding "Level 03".
- **Matrix drawn** — red as the fill for a normal cell, and the only one of the
  three matrix views with no legend.
