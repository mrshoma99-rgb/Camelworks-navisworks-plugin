# Clash Manager — design review and plan

Every renderable Clash Manager surface was drawn and read: 31 pages across the
seven tabs, the matrix editor in eight states, the naming window in three, the
exchange round in three, and all six Settings sections.

The document is in four parts.

1. **The design system** — what is wrong in the token and style layer, measured.
2. **Surface by surface** — each of the 31 pages: what works, what is wrong,
   and what to do, with each item marked `[DS]` for a design-system change or
   `[SCREEN]` for one surface.
3. **Faults of fact** — defects found that are not judgment calls.
4. **The plan** — the order of work.

Throughout, `[DS]` marks a change made once in the token or primitive layer that
changes many screens; `[SCREEN]` marks work on one surface. The standing
instruction on this work is that a shared pattern is fixed in the design system,
so that split is the operative part of the document.

---

# Part 1 — The design system

## 1.1 The diagnosis, in three numbers

- `Field` is `#FFFFFF` and `Card` is `#FFFFFF`. **1.00:1.** The palette's own
  comment says the input should be the brightest surface on the panel, "which is
  what makes it read as *type here*". In the light theme the values do not
  implement the rule the comment describes.
- `FieldBd` and `BdStrong` are the same colour in **both** themes.
- `Card2`, the secondary button fill, against `Card` is **1.02:1**.

So the sheet has no colour with which to separate an editable surface from a
display surface from a pressable one, and falls back to separating them by
radius and height — which differ by one to three pixels:

| Control | Radius | Border | Fill | Height | Font |
| --- | --- | --- | --- | --- | --- |
| `TextBox` | 8 | 1px `FieldBd` | `Field` #FFFFFF | 31 | 13 |
| `ComboBox` | 8 | 1px `FieldBd` | `Field` #FFFFFF | 31 | 12 |
| `Button` | 8 | 1px `FieldBd` | `Card2` #FBFCFD | ~29 | 12 |
| `Parts.Source` | 4 | 1px `FieldBd` | `Card` #FFFFFF | 28 | 13 |
| `OptionCard` | 9 | 1px `FieldBd` | transparent | ~33 | 12 |

Five control classes, one appearance. A dropdown is distinguished from a text
box by an 8×4px hairline chevron with no well and no gutter; a read-only value
is either a bare label or the same bordered box, depending on the screen.

## 1.2 What the sheet is missing

**Focus.** Two of thirteen templated controls draw a focus state, and the two
use different geometry: `TextBox` swaps its border brush at 1.6px, `RowBtn` adds
a 2px border over a 0px one and shifts its content. `ComboBox`, `Button`,
`PrimaryBtn`, `QuietBtn`, `LinkBtn`, `DangerBtn`, `Segment`, `CheckBox`,
`Switch`, `OptionCard`, `RailBtn` and `TabBtn` have none. Every clickable
`Border` in the product — `Parts.Source`, `Parts.Presser`, `ClashNaming.Chip`,
`StatusChip`, the matrix cards — is unreachable by keyboard entirely.

**Elevation.** Five hand-written `DropShadowEffect`s in three specs. None
carries a colour, so all five are black in the dark theme. Two omit `Direction`,
so the popup shadow and the floating-panel shadow fall in different directions.

**Scales.** Ten distinct corner radii, none named. 678 `new Thickness(` literals
in 191 distinct shapes. Control heights of 17, 19, 20, 24, 26, 28, 29, 31, 33 in
use. `Ui.Gap` — the one spacing constant — has zero external consumers.

**Type.** The 18px step is dead, so the effective ramp is 16 / 13 / 12 / 11:
four steps inside five points, three of them one point apart. `Caps` and `Hint`
are the same size and colour and differ only in weight. `Size.Small` governs both
the secondary label style and the caption of fifteen control templates, so label
size cannot be changed without resizing every button in the product. 64 call
sites set `FontWeight` inline because there is no strong-body style.

**Disabled.** Four mechanisms: explicit token swap, `Opacity 0.5`, `0.55`, and
`0.75`. A disabled `PrimaryBtn` drops to a transparent outline, so on a blocked
screen the primary becomes the quietest control on it — and there is then no
filled control anywhere.

**Field vocabulary.** `BadgeReq`, `BadgeOpt` and `BadgeAuto` are defined in the
sheet and have **zero call sites**. The word "optional" appears once in the whole
Views folder; nothing is ever marked required.

**Dead weight.** Seven of 29 named styles have no callers: `Title`, the three
badges, `IconBtn`, `OptionCard`, `TabBtn`. Five palette tokens are unused.
About 370 lines of `Ui`/`Parts` primitives have no callers.

## 1.3 Two accents, and the warm one means "wrong"

The active tab and the rule under the title bar are `Fam.Coordinate` `#C2410C`,
the app-family hue. Everything else — primary, focus, selection wash, links,
active counter — is `Accent` `#0070C0`. Nothing else inside the working area is
orange except warning triangles and dropped-row stripes. The app's most
persistent signal, "you are here", is drawn in the colour it otherwise reserves
for something being wrong.

Against the BIMCamel web system the plug-in also diverges on the brand itself:
web brand blue is `#2563eb` against the plug-in's `#0070C0` (a contrast-neutral
swap: 5.15:1 against 5.17:1 on white); the web's hover *darkens* to `#1d4ed8`
while the plug-in's *lightens* to `#0D83DA`, so a hovered control here is less
legible than at rest; and the web's primary is a 135° blue-to-indigo gradient
with a brand glow, against a horizontal blue-to-blue wash here.

## 1.4 Bypass rate — better than feared, and concentrated

31 of 63 view files have zero inline styling and will be reached by a sheet-level
change alone. Of 815 inline operations, 34% are inside `Ui.cs`, `Parts.cs` and
`Bench.cs` — the design system bypassing itself, centrally fixable — and 40% are
in the five matrix files, a bespoke drawing surface. The remaining 26% across 46
screens is almost entirely margins, i.e. missing spacing tokens rather than
colour bypass. Only six screens ever assign a brush directly.

Two prior claims do not survive checking: `docs/PRIMITIVES.md` records
`Parts.Source` as replacing four hand-built pickers (it has two call sites) and
`Parts.Ticked` as replacing five (it has one). That consolidation is about 40%
done and the document says otherwise.

---

# Part 2 — Surface by surface

## Create and the checks

### app-clash-create — the Create tab, a clearance test selected

**Works.** The master/detail split with a drag grip. The folder as a band row
with its count, not a repeated column cell. A per-check glyph in the Kind column
so a mixed list is scannable. "Never run" rather than an empty cell. The
footer's anatomy: reason left, primary hard right, holding position blocked or
live. The Run caption tracking its own scope.

**Defects.**
1. Two of the three pickers are not controls. "Search for walkable surfaces in"
   and "Search against" are `Parts.Source` — a `Border` with a mouse handler: no
   tab stop, no keyboard operation, no focus. Directly below sits a real
   `ComboBox` that is 31px to their 28, radius 8 to their 4. The two fields that
   define what the test measures cannot be set from the keyboard.
2. The disabled state has three renderings on one panel and states its cause
   nowhere near the controls — the reason is in the footer, 380px below.
3. The section heading is smaller than the labels it governs: `Caps` 11px bold
   over `Lbl` 12px, both `Muted`.
4. Three ragged right edges, 9px apart — too small to read as intent, too large
   to read as a grid.
5. Nothing marks required, optional or automatic. Name and both sources are
   required and say nothing; Levels is optional and is the only field that says
   so.
6. The save contract is invisible: every settled edit writes straight to the
   project file, with no Save, no dirty mark and no sentence saying so.
7. The primary is duplicated and outranked — Run is both the first of five
   unlabelled glyphs in the picking bar and the footer primary, and the glyph
   that deletes tests is 130px from the glyph that runs them.

**Prescription.** `[DS]` separate `Field` from `Card` and `FieldBd` from
`BdStrong`; `[DS]` rebuild `Parts.Source` on a focusable control with the
`Choice` geometry; `[DS]` one disabled recipe; `[DS]` required/optional/automatic
marks on `Ui.Field`; `[DS]` a save-contract line in the pane anatomy; `[DS]`
radius, spacing and control-height scales; `[SCREEN]` one field width so every
row ends on the same edge; `[SCREEN]` name the test in the settings pane;
`[SCREEN]` remove Run from the picking bar and word the destructive verb.

### app-clash-create-fall — the Create tab, a fall-hazard test selected

**Works.** Field order is argued and correct — the cut plane first, because the
floor side is filtered by the band. Conditional notices only when the value is
actually wrong. Cut elevation renders empty rather than "0", because zero is a
real elevation. Reference matter behind a link rather than standing under the
fields.

**Defects.** The list is not top-aligned — at 873px tall there is 127px of white
between the pane header and the column headings, and the table floats down as the
window grows. Three `Parts.Source` pickers, as above. Four right edges rather
than three. Two numeric boxes of identical appearance carry incompatible
quantities: an absolute datum in project coordinates and a ± half-width. Five
lines of 11px grey prose over four lines of control. The unit is glued into six
labels. An orphan half-row. A settable value is hidden behind a link worded as
documentation. Picking a storey fills the elevation box and the box does not say
the value was derived.

**Prescription.** `[SCREEN]` top-align the list; `[DS]` a unit affix on the field
rather than in the label; `[DS]` an automatic-value mark with its source named;
`[SCREEN]` reunify widths; `[SCREEN]` split the link so the setting behind it is
on the panel.

### app-clash-create-empty — the Create tab with no tests

**Works.** The empty state defines what a test is in the product's own terms and
carries the verb that fixes it. The right pane is one line rather than a second
card repeating the offer. The footer states "There is no test to run."

**Defects.** The two panes align differently — the left card is centred, the
right line is top-left. The screen's one action is not the primary anywhere:
"New test" appears twice at secondary weight while the only filled button is the
greyed "Run all". The card's `Card2` fill against a white pane is 1.01:1, so a
hairline is the whole container. A live search box and "From the matrix" sit over
a list with zero rows. The card teaches what a test is but not that there are
three kinds, so the first press is always a surprise.

**Prescription.** `[DS]` let an empty state's action render as the primary;
`[DS]` give the empty card a real surface; `[SCREEN]` draw the three kinds as
option cards in the empty pane rather than behind a menu; `[SCREEN]` align both
panes.

### app-clash-new — the new-clash-test form

**Works.** Both sides are ticked lists with a find box, not combos over hundreds
of set names. The sides come first, because they are what the test is and the one
thing that cannot be changed later. Kind carries a one-line explanation that
changes with the pick, and Tolerance disappears for Duplicate. Nothing is written
until the explicit press — the only deferred save in the set, and correct.

**Defects.** The form is fillable while it cannot possibly submit: with no
document open the two side cards are disabled but Name, Folder, Kind and
Tolerance stay live. The layout uses a third of the width, and the only
full-width element on the form is the warning. Both sides open on the segment
that cannot be used, printing the same dead sentence twice. "Clear" is offered
with nothing to clear. Validation is post-hoc — two errors arrive in a log panel
after the press. The hint column's width is tied to the numeric box it explains,
so a 190px box gets a three-word-per-line paragraph.

**Prescription.** `[SCREEN]` gate the whole form on the block and raise the
banner to the blocked-primary treatment; `[SCREEN]` lay the two sides side by
side; `[SCREEN]` open on a segment that works and disable the one that cannot;
`[DS]` hide `Clear` when nothing is picked; `[DS]` detach the hint width from the
control width; `[DS]` required marks and validation on blur.

### app-clash-picker — the check picker

**Works.** Unavailable checks are listed with their reason rather than omitted —
a seat that cannot offer a check must be able to say so, or its results read as
having vanished. Every line carries its check's symbol, barred ones included.

**Defects.** Every entry is barred and the control does not say so. The first
entry contradicts itself: "Clash detection — 119 open clashes" over "This
Navisworks edition has no clash engine". Barred is signalled by 45% opacity
alone, which puts the reason text at about 3.0:1. "Already showing" is rendered
in the same slot as a refusal reason, and nothing marks the current item as
selected.

**Prescription.** `[SCREEN]` state it once when every line is barred; `[SCREEN]`
suppress or qualify the count on a barred entry; `[DS]` a barred rendering that
does not rely on opacity; `[DS]` a selected treatment for the current entry.

### app-clash-clearance-surfaces — what detection found, and what to drop

**Works.** The page states its own failure mode before anything is read: "which
is a floor, and is also a desk top, a worktop, a window sill and the top of a
wide duct: read the list and drop what is not a floor." The result line gives
count, area and refusals together, and the refusal is explained rather than left
as a number. Largest-first ordering. Drops are written into the run's settings so
the same desk tops are not dropped twice.

**Defects.** A five-item dropdown stretched to 1148px to hold "0.05 m²" — the
value at x=28 and its chevron at x=1148. "In the check" is a blank-or-"dropped"
column, so four of six rows answer nothing. Six rows under a line that says 412
surfaces, with a footer that agrees with neither. The Level column takes 850px to
hold "Level 03" five times while the columns that would identify a row are pinned
at 90px. One verb bar with two control shapes.

**Prescription.** `[DS]` cap `Ui.Field`'s control width and require an explicit
opt-in to stretch — this one change fixes every over-wide control in the product;
`[SCREEN]` make "In the check" answer on every row; `[SCREEN]` add the
reconciling count line unconditionally; `[SCREEN]` add an identifying column;
`[DS]` make a verb bar homogeneous.

## Manage, Filter and Report

### app-clash-manage — the results board

**Works.** The two-pane split, each pane with its own head/list/foot so scrolling
one does not move the other. The 3px status bar down the left edge of each row —
the one piece of pre-attentive encoding in the product, and it must survive. The
status counter strip doubling as the status filter, with counts that reconcile.
Row press and row tick deliberately separate, so reading four clashes in sequence
does not stage four rows for action. Footer facts derived, never typed.

**Defects.**
1. The Status chip and the Assigned chip are the same object to the eye and only
   one is pressable. Both are `Border`, radius 7, 11px semibold, 8px apart in
   adjacent columns; the assignee opens a picker, the status does nothing and its
   own tooltip says to use the row menu.
2. Two columns are empty on all 16 rows and a third on 14 — 243px, 30% of the
   board, while Name is squeezed to 157px. The empty cell is also rendered two
   ways: a clash row draws nothing, a group row draws an em-dash.
3. The Overlap column shows bare integers with no unit, and 5 + 3 + 8 = 16, the
   result count — so it may be printing member counts under a distance heading.
   To be verified against a real read before anything else here is touched.
4. Two unlabelled play buttons, 740px apart, meaning different things: "run all
   tests" and "run this test". The most expensive and least reversible act in the
   product, and the wider-scoped one is the quieter.
5. The board toolbar is four undifferentiated 15px glyphs; three open
   configuration and one executes a job.
6. `Save changes` is always enabled and rewrites its caption underneath the user.
   The board's own code says every edit has already been written. The loudest
   control on the screen has the least to do, most of the time.
7. Row verbs are hover-only and there are four of them, so a resting board tells
   the user nothing about focus, isolate or comment — and in a docked pane with a
   pen there is no hover at all.
8. Status is encoded three ways on one screen: counter figures, rail integers,
   row pills.
9. The rail's folder rows put a folder membership count in the "Res" (Resolved)
   column — a number in a labelled column that is not that column's quantity.
10. Density mismatch: the rail carries eight fields in 370px at 52px rows; the
    board carries three populated fields in 810px at 49px rows.

**Prescription.** `[DS]` separate the field surface, and give menus a chevron
well so a state token and a menu trigger are different shapes; `[DS]` a focus
token on all thirteen templates; `[DS]` require a label on any icon-only control
that executes a job; `[DS]` one save contract with a dirty indicator; `[SCREEN]`
suppress empty element columns and use one empty-cell glyph; `[SCREEN]` verify
and fix Overlap; `[SCREEN]` label both run controls and separate the executing
verb from the configuring glyphs; `[SCREEN]` persistent low-contrast row verbs;
`[SCREEN]` collapse three status encodings to two; `[SCREEN]` band ungrouped
tests in the rail as the board does.

### app-clash-finder — the board with the rail's filter panel open

**Works.** The panel is drawn as an overlay rather than inside the head, so the
two panes' column headings do not desynchronise. Chips are values that exist in
the read. The closed funnel tints accent when anything is narrowing and states
the full filter in words.

**Defects.** The panel is narrower than the rows it covers, so three orphaned 3px
red status bars are visible beside a white card with no shadow and no scrim — it
reads as a rendering fault. It hides four rows and the column headings while the
footer still says "6 tests". Opening it grows the surface by 266px, which in a
docked pane clips the footer instead. It has no title, no close and no connection
to the funnel that opened it. A scope checkbox is stranded under the "Naming"
heading. A navigation link sits inside a transient overlay.

**Prescription.** `[DS]` one overlay pattern — full width of its container, real
shadow, a connector to its opener, and never resizing its host; `[SCREEN]` a head
row with a close; `[SCREEN]` move the scope checkbox under its own heading;
`[SCREEN]` state how many rows are hidden behind the panel.

### app-clash-rules — the Filter tab with no rules

**Works.** The scope line is a real derived statement, including what is *not* in
scope. The blocked state is honest and specific: rules can be written and saved
with no model open; only review and run need one — and the screen correctly stays
editable. The empty card states the consequence of emptiness, not just the fact.

**Defects.** The right pane is 588×380px and contains twelve words — the screen's
emptiest half is its larger half. There is no filled control anywhere: the
primary is disabled and the disabled `PrimaryBtn` falls back to an outline, so
three buttons of ascending size and identical white fill sit in the footer and
nothing says which is the action. The blocked reason is bound to the primary but
reads as though it governs the enabled `Save rules` beside it. A screen with
nothing in it opens with 68 words, including instructions for dragging rows in a
list that does not exist. Type hierarchy is inverted: the heaviest text is a
caveat.

**Prescription.** `[DS]` a disabled primary must stay visually primary; `[DS]`
raise the secondary button fill off the card; `[DS]` split the footer's reason
from its persistence line; `[SCREEN]` put the rule anatomy in the empty right
pane with the real call to action in it; `[SCREEN]` cut the intro to two lines.

### app-clash-rules-set — the Filter tab with seven rules

**Works.** The rule row is well built: number, name, and a derived subtitle
saying both halves of what the rule does, with "(off)" agreeing with the unticked
box. The open rule is marked three ways at once. Edited in place with no Apply.
`Delete rule` is the only red control on the screen.

**Defects.** The row tick means "enabled" and looks like "selected" — seven
filled accent boxes are the strongest colour on the screen. A play glyph sits
64px from a bin glyph on every row, and running a rule with a Remove action
deletes results from the document irreversibly. The condition builder has four
visual weights for one concept, and two delete affordances in two positions 8px
apart in the vertical. "Add condition" is a button and "Add bracket" is a link
for the same kind of act. The explanatory sentence is stranded closer to the
group it does not describe. `Assign to` is written as `options[0]` on draw, so a
new Assign rule silently holds "Unassigned" — and in this render the panel shows
"Unassigned" for a rule the list describes as assigning to Mechanical.

**Prescription.** `[DS]` two distinct forms for inclusion and enablement;
`[DS]` a destructive-adjacency rule, with per-row delete in the overflow menu;
`[DS]` required marks and a real difference between a text box and a combo;
`[SCREEN]` unify the condition builder to one container weight; `[SCREEN]` stop
writing `options[0]` on draw, seed a sentinel and block the run with a reason;
`[SCREEN]` per-row dirty dots and a counted Save caption.

### app-clash-report — the Report tab

**Works.** Options that do not apply are removed rather than disabled. The cost
of the expensive option is stated in advance and in the user's terms. Segmented
control at the right cardinality, with its initial state settled before handlers
are wired. The file-name field ships a working token template and lists its
tokens beneath it.

**Defects.** The left 70% of the screen is empty: the code that would fill it
returns immediately when there is no board, so the blocked state loses the entire
purpose of the pane. Half the settings persist and half do not — Document and
Recipient, the two controls that most change the output, reset on every reopen
while the four checkboxes below them remember. Four fields at four widths in a
330px column, because `Ui.Field` declines to set alignment and the width argument
is a minimum nobody honours. Two hints are orphaned from the fields they describe.
The folder field is an empty white box with no placeholder, joined to a Browse
button with no visual join. A sphere glyph in the footer renders as a "no entry"
sign 30px from a disabled primary. The footer uses a different anatomy from every
other tab.

**Prescription.** `[DS]` one footer anatomy for every screen; `[DS]` make
`Ui.Field`'s width authoritative with an explicit fill/fixed choice; `[DS]`
placeholders through the template's existing `Tag`; `[DS]` the automatic-save
mark; `[SCREEN]` render the document's shape from settings alone so the eleven
controls give feedback with no model open; `[SCREEN]` persist Document and
Recipient; `[SCREEN]` bind the two orphaned hints to their fields; `[SCREEN]`
replace the sphere glyph with a labelled button; `[SCREEN]` join folder and
Browse into one control.

## The matrix

### app-clash-matrix — the start screen, four ways in

**Works.** The four routes are the right four, each states its own precondition
rather than deferring to an error after the click, and the lead paragraph defines
what the matrix is in two sentences. Reading measure is right at 560px.

**Defects.** No main action: four identical 12px accent text links, and the real
default for a new matrix — "Add the first discipline" — is last and least
emphasised. The blocked route is drawn as an available one. Nine of eleven
toolbar controls are inapplicable and seven look pressable. `Build the tests` is
present, ghosted and unexplained before there is anything to build. Cards are
separated from the page by a hairline at 1.07:1, and the gap between them is
smaller than their internal leading, so four choices read as one block of text.

**Prescription.** `[SCREEN]` one primary, cards ordered by expected use;
`[SCREEN]` draw the blocked route blocked; `[DS]` an elevation step so a card is
a container; `[DS]` a spacing scale with the inter-card gap larger than the
padding; `[DS]` disable toolbar actions that have no subject.

### app-clash-matrix-drawn — the matrix drawn, showing symbols

**Works.** Frozen headers on both axes with folder bands carried on both and
matched tints. The diagonal drawn as a spine with the mirror half echoed at 90%
opacity. Every cell carries a letter as well as a wash, so the grid survives
greyscale and colour-blindness. Unnamed rows carry an inline warning on the line
itself. The counts strip and panel are docked, so dismissing a notice does not
move cells under the pointer.

**Defects.**
1. Three warning strips stack at the top and take 60px of grid. They are ordered
   by nothing: a defect the user caused sits above a permanent property of the
   Navisworks edition, and only the second is dismissible.
2. **The whole grid is drawn in the error colour** — 162 of 190 live cells are
   `RedBg` with `RedTx` letters, because no symbol is in the legend. A
   newly-drawn matrix, the normal state after any of the four start routes,
   renders as a wall of red, so the signal reserved for "this builds nothing"
   signals nothing.
3. No key for the symbols view, while results and priority both have one. The
   right-hand panel is headed "No symbols yet" *above a list of four symbols*.
4. Counts in four formats inside one panel, and the number 162 appears twice
   meaning "pairs decided" in one place and "cells skipped" in the other.
5. Three coordinate systems on one grid — column letters, the Number field, the
   gutter index — plus a fourth in the tooltip. The Number sequence is
   non-monotonic on both axes, so two of the four cannot be used to navigate.
6. A ~53,000px² dead quadrant at the grid's origin, which is where the eye lands.
7. The terminal action is the eleventh item in an eleven-item toolbar, and in its
   blocked state it is the quietest control on the surface.

**Prescription.** `[SCREEN]` collapse the alert band to one strip with a count
and a disclosure, actionable first; `[DS]` give alerts a real container;
`[SCREEN]` stop drawing an undecided cell in the error colour; `[SCREEN]` add a
symbols key and fix the panel's false empty-state wording; `[DS]` one count
format per panel; `[SCREEN]` pick one visible coordinate; `[SCREEN]` fill the
origin quadrant; `[SCREEN]` move the primary to a command bar.

### app-clash-matrix-scrolled — the same, scrolled

**Works.** Both header axes stay frozen and the folder bands scroll with their
columns. Dropping a field column recovers grid width and reveals another column.

**Defects.** No scroll-position cue: a column is clipped mid-glyph at the pane
edge with no gradient and no "3 more" marker, so it reads as a rendering fault.
The visible numbering has a hole — two adjacent integer columns diverge silently
from row 10 down. Column headers are rotated 90° and 130px tall while row names
are horizontal, so the two axes of one matrix are read in two orientations, and
rotated labels elide *vertically*. Gutter headings are truncated to "Fol…", "#",
"Nu…". The folder band tint and the selection tint are the same blue family.

**Prescription.** `[SCREEN]` a seam and an edge fade at the frozen boundary, plus
a "columns 1–16 of 20" line; `[SCREEN]` one visible coordinate; `[SCREEN]` rotate
only when the label exceeds the column width; `[DS]` a minimum header width tied
to the label with a tooltip carrying the full word; `[DS]` separate the grouping
colour family from the selection family.

### app-clash-matrix-line — a row picked

**Works.** Editing in place rather than in a modal, so the effect of a rename is
watchable. "Checked against" groups the row's pairs by symbol with member names.
The undecided group is a distinct card with an empty swatch, so the gap is as
visible as the decisions.

**Defects.** Opening the panel makes the page 1907px tall and pushes the grid out
of view — selecting a row to edit it costs you sight of the grid you selected it
from. Counts in five formats inside one panel, with a leading integer meaning
"number field" 40px from a badge meaning "count". The badge numeral is a count
here and a keyboard shortcut in the priority legend: same shape, same size,
opposite meaning. Seven field cards consume 520px to hold seven short strings,
each a white box inside a white box. Seven identical unlabelled overflow glyphs.
Five unlabelled row actions, two red-tinted, distinguishable only by hue.

**Prescription.** `[SCREEN]` scroll the panel independently at the grid's height;
`[DS]` one count format, and a kbd token distinct from a count badge; `[SCREEN]`
collapse the field cards to a label/value list; `[SCREEN]` label the row actions;
`[DS]` a shared selected-row treatment for grid and panel.

### app-clash-matrix-results — showing results, a heat scale

**Works.** The ramp is normalised to the board's worst pair, so boards of
different scale produce the same picture of where to go first. Zero-open and
untested are drawn differently and the legend names both. Cell figures carry a
movement delta in the same text block.

**Defects.** A third unrelated colour scale for the same grid: red means "symbol
not in the legend", "worst pair on the board" and "high priority" across the
three views; green means "nothing open" here and "low priority" there. The legend
encodes one variable three ways, with the zero chip off the ramp it belongs to.
The grid shifts 27px sideways between views, so switching moves every cell under
the pointer. Figures are centred, so a column of counts cannot be scanned, and
the panel writes a thousands separator the cells do not. Ink flips mid-ramp at a
luminance threshold, so a column alternates dark and light at a boundary the
reader cannot predict. Four different denominators on one screen. The two warning
strips are the symbols view's, displayed above a grid showing results.

**Prescription.** `[DS]` one matrix colour system — one categorical set, one
sequential ramp, no hue reused for opposite meanings; `[SCREEN]` put zero at the
ramp's foot and hatch the untested case; `[DS]` fix the grid's left edge across
views; `[DS]` right-aligned tabular figures with one separator rule; `[DS]` a
designed two-step ramp rather than a luminance flip; `[SCREEN]` suppress alerts
that do not apply to the current view.

### app-clash-matrix-priority — showing priority

**Works.** Three ranks in the conventional three colours, shared with the
workbook export. Every rank carries a letter as well as a colour. The legend
states what clearing a priority does. A checked-but-unranked pair is drawn as the
faintest state rather than as an error.

**Defects.** Four stacked bars before the grid, the fourth being a mode hint
drawn with a warning triangle. **The mode chooser wears the severity glyph** —
the same triangle as the three alerts directly beneath it. Two of the three rank
colours sit within 0.08 of the ink-flip threshold, so a small colour change
silently re-inks a whole rank. The grid reads as mostly empty because 47 low-rank
pairs are drawn in the echo half. The panel's figure list is stale and reports a
figure that differs from the other three views with nothing explaining it. The
legend's numerals are shortcut keys drawn as count badges.

**Prescription.** `[DS]` reserve the warning glyph for severity; `[DS]` re-derive
the three rank colours against a fixed ink; `[SCREEN]` collapse and rank the
alert band; `[SCREEN]` draw ranks in the echo half at reduced saturation;
`[SCREEN]` make the figure list view-specific.

### app-clash-matrix-fields — the fields editor

**Works.** The five columns are the right five and the scheme is legible as a
table rather than as seven dialogues. The token is shown literally. The detail
panel edits the selected row in place below it.

**Defects.** A shipped grammatical fault: "2 of thems keep a value typed by
hand". Three columns are truncated to uselessness — the Writes column is 65px
and every value is elided to four characters — while 56% of the window is empty.
A multi-select table with no visible selection control. Five unrelated control
types on one 14px action line. The detail card cross-references a setting three
levels deep in another menu with no link. The primary is "Done", drawn as a
secondary, alone in 660px of space.

**Prescription.** `[SCREEN]` fix the string; `[DS]` guard the pluraliser against
pronouns; `[SCREEN]` widen the table to the window with explicit column widths;
`[DS]` content-driven minimum column widths with tooltips; `[SCREEN]` a checkbox
column and a grouped action row; `[DS]` a footer ground so the closing button
anchors the page.

### app-clash-matrix-read — reading the document's tests back

**Works.** The only surface in the matrix set with a correctly weighted terminal
action. The pattern editor shows a live parse of the first matching test and a
live count, so a pattern is judged by its result rather than by reading regex.
Every row shows both what a test is called and what it is between. The
consequence is stated at the point of commitment.

**Defects.** The list's bulk control is a truncated ghost button in a tinted box,
above the table, while three text links that also act on the list sit beside it.
The "Read by" column is nine "Pattern 1" and twelve em-dashes, and the dash is
both the failure state and the click target for fixing it. Selection checkboxes
and setting checkboxes are the same control 30px apart. The two footer settings
contradict each other for any test that has both, with no precedence stated. The
secondary panel is the only elevated object while the primary content is
unbordered rows.

**Prescription.** `[SCREEN]` one control row above the table; `[SCREEN]` draw the
unmatched state as a labelled state with a button affordance; `[DS]` separate
selection checkboxes from setting checkboxes by placement; `[SCREEN]` state the
precedence; `[DS]` one elevation rule — primary content gets the container.

## Exchange and naming

### app-clash-exchange — the export side

**Works.** The two-pane split matches the data model: a clash belongs to a test,
and the two are filtered by different things. Both pane headers carry a live
count, and the right one names the output's unit — packs, not rows. The primary
is unambiguous and correctly placed, with Preview beside it running the same job
without files. Set-once configuration is pushed hard left, away from the two run
actions. The filter bar states its predicate in words and counts the remainder.

**Defects.** The Kind value and its link render as one run-together string —
`ClashChange on Manage` — because a 170px cap never wraps and the value has no
right margin. Kind is the only read-only value on the strip and is not marked as
one, so users click it and the recovery is a link off the tab. Status is coloured
pills on Manage and plain 12px text here. Three zero-count verb buttons on one
screen. A numeric column header is left-aligned over right-aligned values, 43px
apart. Three combos at three widths, none derived from content. "Saved setup" is
350px and 60px away from "Save as a profile…", which writes into it.

**Prescription.** `[SCREEN]` give the value a margin, or make it a read-only
field with the change affordance inside its bounds; `[DS]` a read-only field
variant; `[DS]` one status renderer used by every table; `[DS]` a zero selection
renders the verb without a count and disabled; `[DS]` pass the numeric flag into
the header template; `[DS]` a control-width scale; `[SCREEN]` reunite the profile
read and write sides.

### app-clash-exchange-import — import with no packs chosen

**Works.** The empty state is the right shape: what is missing, what happens
next, and the button that resolves it. The blocked primary states its reason, and
the reason is bound to the button's own tooltip so the two cannot drift.

**Defects.** The screen is 78% empty, and the state where the app has nothing to
show is the one that wastes the most space. The footer is drawn at full weight
for a screen with no content, and the disabled primary — a bordered ghost 136px
wide — is more prominent than the enabled action inside the card. The page header
and tab row are absent from the render; that is the harness drawing the panel
alone, not the product (fault 12).

**Prescription.** `[DS]` one empty-state block, vertically centred, with one
primary; `[DS]` when the only footer action is disabled and the screen is empty,
suppress the footer and promote the empty state's action; `[DS]` a disabled
primary that never out-weighs an available action.

### app-clash-exchange-import-read — returned packs read and merged

**Works.** The pane split matches the two questions: provenance left, consequence
right. "On the board" beside "Returned" is the correct framing for a merge.
Nothing is applied by reading — the right table is a plan and Apply is the only
write. The status mapping is spelled out in words before commit, which matters
because BCF does not fix its status list.

**Defects.** Five horizontal strips in the bottom 125px, four of them full width,
with no rule or ground change separating them — the commit action is buried in a
band of near-identical grey rows. The one row that requires a human decision is
signalled by a 3px orange edge 350px from the word that explains it. "Apply" is a
Yes/No text column rather than a tick, so the per-row decision the screen exists
to make is the only per-row state that cannot be toggled on the row. Two links
sit inside a bar of buttons, both dead until a row is selected and neither saying
so. Four zero-count buttons. The left pane is 520px for two rows.

**Prescription.** `[SCREEN]` collapse the four bottom bars into two; `[DS]` a
row-level warn state must be legible where the reason is written, not only at the
row's leading edge; `[DS]` a boolean column rendered as a real checkbox in
`Ui.Table`; `[DS]` one grammar per verb bar; `[DS]` content-derived pane widths.

### app-clash-naming-tests — the per-test audit

**Works.** A table, not cards, for 21 rows of three facts. The middle column
shows the pattern's actual output for that name in the project's own words.
Unread rows state it in the same column, so a failure is scannable. The footer
names the consequence, not the count. The pane order is the order the work
happens in.

**Defects.** The pattern chip is the primary control on every row and gives no
sign it opens a menu — a `Border` with a hand cursor, no keyboard path, no focus.
The same chip shape carries three meanings across two panes. Column headings
scroll away. No row separation at 26px pitch across a 542px horizontal traverse.
The name column truncates on 14 of 21 rows, and two pairs of rows become
identical after truncation — the pane's whole purpose is checking that each test
is on the right pattern, and it cannot be done by reading. The window is 700px
inside 1180. Nothing indicates the pane saves automatically.

**Prescription.** `[DS]` chips that open menus become buttons with focus and a
chevron; `[DS]` rebuild the hand-rolled row lists on `Ui.Table`, which already
has sticky headings, rules, hover and trimming; `[SCREEN]` widen the column and
truncate from the middle; `[DS]` an automatic-save indicator.

### app-clash-naming-patterns — patterns and token palettes

**Works.** Each pattern card states its own yield against this job's names.
The token palette separates the project's own fields from the built-ins, and the
project's lead. The palette appears under one card only. Tokens insert at the
caret. The pattern is written on focus-out, not per keystroke.

**Defects.** Nothing explains why one card has the palette and the other does
not. "Up", "Down" and "Remove" are three identical accent links, and Remove
unbinds every test on the pattern with no confirmation. Two of the six reorder
controls are dead but drawn live. The pattern box does not distinguish tokens
from literals — the structure the whole feature depends on is one undifferentiated
run. The chips do not name the tokens they insert, so the two representations of
one vocabulary never appear together. The row/column pairs are split across the
wrap boundary.

**Prescription.** `[SCREEN]` mark the live card or give every card a palette;
`[DS]` route destructive actions through the danger weight and confirm anything
that invalidates other objects' references; `[DS]` disable reorder controls at
list boundaries; `[SCREEN]` syntax-highlight the pattern box — the highest-value
change on the pane; `[SCREEN]` put the token on the chip and pair the palette.

### app-clash-naming-write — the renumber preview

This is the best confirmation pattern in the product and it is to be generalised,
so what makes it work is stated precisely:

- **Every affected row is drawn, not only the changing ones**, and the header
  counts account for all of them — 21 tests, 9 renamed, 3 already right, 9 the
  naming cannot write, and 9+3+9=21. A preview that lists only changes leaves the
  reader unable to tell "not listed" from "not considered".
- **Before and after on one line**, one column pair, separated by an arrow.
- **Four outcomes distinguished in words, not colour alone**, with the reason a
  row is skipped written in the row.
- **Skipped rows are muted, not red** — a preview of 40 skipped tests must not
  read as 40 errors.
- **The primary carries the exact scope of the write** — "Rename 9 tests" — and
  becomes "Nothing to rename", disabled, when nothing would change.
- **Cause and effect are 20px apart**: every control change re-runs the plan and
  redraws the preview in place.
- **A 200-row cap with an explicit remainder line** rather than silent truncation.

**Defects.** "Already right" and "cannot write" are rendered identically, so the
rows indicating a fixable configuration problem do not stand out from the rows
that are simply correct. Nine rows say the write is impossible and none offers a
fix. Four control heights in one card, and the field chooser — the most
consequential control in it — is the smallest and least button-like. Two
placeholders contradict their own defaults. The primary is not in a footer: on a
998px page it sits below 21 rows of preview, left-aligned, and moves as the
preview grows. The outcome arrives as a modal, interrupting the pattern's best
property — that the plan is the receipt.

**Prescription.** `[DS]` generalise this as `Ui.Plan(...)` with the seven
properties above, and apply it to Apply-to-the-board, to the export, and to any
bulk status change; `[SCREEN]` distinguish the three outcomes visually; `[SCREEN]`
make each refusal reason link to its remedy; `[DS]` move the primary into the
persistent footer; `[SCREEN]` replace the modal with an in-place result.

## Settings

### app-clash-settings-units — units and distances

**Works.** The three settings that belong together, with the pane redrawing so
"Round inches to" exists only on an imperial job. `Rowed()` caps the field row at
440px and left-aligns it — the one place in the tab that does this, and it is
right.

**Defects.** **The distance labels hard-code "(m)" while the unit dropdown 45px
above can be set to feet and inches.** A coordinator who reads the label as feet
and types 16 sets a 16-metre grouping distance and every band on the board
collapses. Both numeric boxes are unvalidated free text written on every
keystroke — "5m" or "abc" is accepted and saved. Two boxes of the same kind
differ by 17% in width. The unit dropdown is 200px here and 140px on Levels. The
pane is a 270px column in a 972px pane: 7.9% ink coverage.

**Prescription.** `[SCREEN]` derive the unit in the label from the setting, as
the Levels editor already does; `[DS]` a numeric field variant with
parse-on-commit, an inline error and a unit suffix slot; `[DS]` a control-width
scale; `[SCREEN]` put the two distances in a group with a worked example in the
empty 700px.

### app-clash-settings (Levels) — the project's storeys

**Works.** The "Covers (m)" column, derived rather than left for the reader to
subtract. Top-down order matching a section drawing. Column headers carrying
their own unit. An empty state that carries the verb that ends it. Two routes to
a level list, with the derived one additive rather than destructive.

**Defects.** "Heights in" is a throwaway view toggle drawn in the exact slot the
Units pane uses for a saved setting, and Save does not persist it. It offers only
metres and millimetres, so an imperial project cannot read a storey height at
all. The action bar renders its verbs as three unlabelled glyphs, one of which is
a sliders icon meaning "set floor height". "All" and "None" are the only blue
text in a bar of grey icons, so the eye lands on the two least consequential
controls while the destructive one is a grey glyph. The table is indented 10px
from everything above it. Two competing primaries at the foot, the expensive one
— a whole-federation walk — drawn identically to the button that adds an empty
row. **The derive routine reports a write that has not happened**: its save
callback always returns true, so "5 levels added" is printed while nothing has
reached the project file, and its failure branch is unreachable.

**Prescription.** `[DS]` label the picking bar's verbs; `[DS]` one control idiom
per bar; `[DS]` editable table cells so a two-column table does not need a dialog
per edit; `[SCREEN]` make the save callback tell the truth; `[SCREEN]` mark the
view toggle as a view control and add feet, or read the unit from Units;
`[SCREEN]` describe the expensive action and demote "Add a level".

### app-clash-settings-disciplines — which discipline each model belongs to

**Works.** The empty state names the cause and the remedy in the user's terms.
The card is capped so the second sentence wraps rather than running the pane. The
populated design is sound: one chooser beside the verb rather than a dropdown per
row.

**Defects.** The empty state hides a control that has nothing to do with being
empty — declaring the project's own discipline words needs no loaded model, and a
coordinator setting up before the federation is appended cannot do the one useful
thing here. The pane has zero interactive controls other than the info glyph. The
card is centred while every other pane's content starts at the left edge, so
switching sections moves the eye 253px right and back. The card fill against the
pane is 1.06:1.

**Prescription.** `[DS]` empty states keep the pane's left edge and carry the
actions still available; `[DS]` separate a blocked state from an empty-result
state; `[SCREEN]` draw the own-words editor in the empty state.

### app-clash-settings-team — the roster

**Works.** One row per person with headings once at the top, every value editable
where it stands. Star-share columns so a narrow pane squeezes rather than drops.
Consistent 35px row pitch. Bulk removal by tick with the count in the caption.
Editable combos over the values the roster already uses, so the project's own
vocabulary becomes the suggestion list. The only pane on the tab that uses the
width it is given.

**Defects.** **57 interactive controls in a 394px pane, all the same 31px white
box.**
1. **The inactive member is drawn identically to the five active ones** — no
   dimming, no badge, no toggle. That person cannot be assigned a clash, and the
   screen that owns the roster neither shows it nor lets it be changed. The
   product already dims inactive members elsewhere.
2. **Renaming an inactive member silently reactivates them** — the rename path
   copies Role, Team, Folder and Note but not the active flag, and the roster's
   add-on-collision sets it true. Data corruption from a text edit.
3. Four of six data columns clip on every row, inside editable controls, so there
   is no ellipsis and no tooltip — the value is simply cut. Only Name fits.
4. The width shares spend the most on the least-used column: the always-empty
   BCF folder gets 191px, Email gets 154.
5. An unset dropdown cannot be told from a broken one: six empty combos with no
   placeholder, while the text boxes beside them do have placeholders — so the
   pane teaches that an empty box with no grey text means something failed, then
   shows six.
6. The disabled destructive verb is at about 1.1:1 — it reads as grey noise, not
   a button. "Add member" is at 1.05:1 and sits below the verb that shrinks the
   roster.
7. Edits commit on focus-out, so typing a value and switching sections discards
   it, because the pane's content is replaced and the box never blurs.
8. Two removal idioms side by side: a row bin with no confirmation, a bar verb
   that asks first.

**Prescription.** `[DS]` a real difference between a text box, an editable combo
and a read-only value; `[DS]` a placeholder on every empty combo and a distinct
unavailable state; `[DS]` a legible disabled treatment; `[DS]` commit-on-change,
or a flush when the host pane is replaced; `[DS]` one removal idiom; `[SCREEN]`
draw the active flag and let it be set; `[SCREEN]` carry the flag through the
rename and stop the reactivation; `[SCREEN]` rebalance the column shares.

### app-clash-settings-naming — test naming, filed under "Create"

**Works.** One row per test rather than a card. The extracted fields shown in the
project's own words. Explicit assignment with proposals offered separately. The
footer states the consequence. The four-pane switcher ordered as the work
happens, and the one control on the tab with a genuine selected state.

**Defects.** The pane is a 700px column centred in a 972px pane, so switching
sections shifts the whole pane 143px sideways, and 31% of the width is structural
nothing while the two data columns inside it clip. 12 of 21 test names truncate,
and two pairs of rows become identical — the pane's purpose cannot be served by
reading it. The "reads" column truncates the evidence that would separate them.
The unassigned chip is white with the field border, so the three rows that need
action are the three drawn as inert. Three navigation levels stack in 90px with
nothing connecting the rail word to the heading to the switcher. Nested
scrollers.

**Prescription.** `[DS]` a max-width column is left-aligned to the pane's content
edge, not centred; `[DS]` chips that open menus are buttons, and an unset chip is
visually distinct from a text box; `[DS]` one scroller per pane; `[SCREEN]` give
the name column the width and truncate from the middle; `[SCREEN]` move the
pane's actions into the tab foot and mark the renumber pane as writing to the
document immediately.

### app-clash-settings-board — the Manage section, headed "Bands"

**Works.** The subtitle draws the right line: a band changes how results are
presented, not what they are. The no-bands sentence states the real default with
the project's own grouping distance interpolated. The band stack is written back
key-by-key, so saving here cannot destroy a Filter-tab rule.

**Defects.** **The "Band by" dropdown reads as the current setting and is
actually an unsubmitted composer.** It shows "Model pair"; the sentence above
says the board currently divides by model pair. The two agree, so the pane states
twice that banding is set to model pair. It is not — the stack is empty. A
coordinator reads it, presses Save, and has changed nothing. The dropdown is
954px wide for an enumerated choice. The empty state is a bare muted sentence
where two other sections in the same tab use a bordered card. The rail says
"Manage" and the heading says "Bands".

**Prescription.** `[DS]` a composer is visually separated from the state it adds
to; `[DS]` cap dropdown width by content class; `[DS]` one empty-state component;
`[SCREEN]` draw the default stack as greyed non-removable rows so the empty state
shows the actual behaviour in the shape the configured state uses; `[SCREEN]`
name the section for its subject in both the rail and the heading.

### The Settings tab as a whole

**The EVERYWHERE / PER TAB division is the wrong axis and its two groups are
named in two vocabularies.** Four rows answer "what is this about" and two answer
"which screen does this change", so the reader must know which scheme a row is
using before they can scan it. Two of six rail labels do not match their headings,
and neither heading contains the rail word.

**Test naming is filed under Create and is not a Create setting.** It is read by
the board's banding, filtering and sorting, by the renumber and by the exports —
the section's own help says so. It is filed where tests are made rather than
where naming is used, which is the reasoning the tab's own design rejects for the
other five sections.

**Ink coverage** of the content pane: Levels 6.2%, Units 7.9%, Test naming 12.8%,
Bands 14.2%, Disciplines 15.6%, Team 43.0% — and two of those figures are mostly
one card and one over-wide dropdown. Four of six panes put every control in the
left 270px of a 972px pane.

**The single "Save the settings" primary is not honest.** Five sections hold
their edits and write on that press. But the Levels editor reports writes that
have not happened; its unit toggle is not persisted at all; the Bands composer's
displayed value is never written; the renumber pane writes to the Navisworks
document immediately; and Team edits commit on focus-out, so switching sections
mid-edit discards the box being typed in. One promise, six sections, five write
behaviours.

**The rail is weakly drawn**: a selected row at 1.08:1 with no accent bar and no
weight change, 100px below a tab strip that marks its selection with a 2px orange
underline. Two selection idioms, two hues, one screen. No per-section counts and
no per-section unsaved marker.

---

# Part 3 — Faults of fact

These are not judgment calls. Each was verified in source.

1. **Renaming an inactive team member reactivates them.** `TeamWindow`'s rename
   path copies Role, Team, Folder and Note onto the new record but not
   `IsActive`, and `TeamRoster.Add` sets `IsActive = true` when the id collides.
   An off-the-job person returns to every assignment list because somebody fixed
   a typo. Silent data corruption from a text edit.
2. **The Report tab loses two of its nine settings.** `format`, `changedOnly`,
   `keyPlan`, `perParty`, `pictures`, `folder` and `name` are written to tool
   memory; `_mode` (Document) and `_audience` (Recipient) never are. The two
   controls at the top of the panel, which most change what comes out, reset on
   every reopen while the four checkboxes below them remember.
3. **The Levels editor reports writes that have not happened.** Its save callback
   is `() => { Touched(); return true; }`, so "5 levels added, from 41,000
   elements", "renamed" and "height set" are printed whether or not anything
   reached the project file, and the failure branch is unreachable.
4. **"2 of thems".** `Ui.Count(pinned, "of them")` pluralises the pronoun. It is
   the last line of the fields editor.
5. **The units labels hard-code metres.** "Grouping distance (m)" and
   "Same-conflict distance (m)" are literal strings while the unit dropdown 45px
   above supports feet and inches. Typing 16 while reading the label as feet sets
   a 16-metre grouping distance.
6. **The priority view's own icon is the severity glyph** — the same warning
   triangle the alert strips use, on the control naming the current view.
7. **A new Assign rule silently holds "Unassigned".** The picker writes
   `options[0]` on draw. In the render the panel shows "Unassigned" for a rule
   the list describes as assigning to Mechanical, so one of the two is wrong.
8. **The Bands composer is presented as the current setting.** The pane states
   twice that banding is by model pair while the band stack is empty.
9. **The Manage board's Overlap column** shows bare integers with no unit, and
   they sum to the result count — so it may be printing member counts under a
   distance heading. To be verified against a licensed read before it is changed.

Three defects in the review instrument itself:

10. **The harness rebuilds the walkable-surfaces panel by hand** and omits a
    field the real screen has, so the field most likely to be misread has never
    appeared in a render.
11. **The new-clash page renders the form's body without its footer**, so
    `Make the test` — the press the whole form exists for — has never been in a
    render.
12. **Two of the three Exchange pages are drawn without the deck**, so the title
    bar and the tab row are missing from them while every other tab page has
    both. The absence was read as a product defect on the import empty state.

---

# Part 4 — The plan

Ordered by screens fixed per unit of work. Everything in phases 1 to 4 is design
system.

## Phase 1 — Make the control classes different objects

The whole of the "everything looks the same" complaint is here.

1. **Separate the field surface from the page surface.** `Field` moves off
   `#FFFFFF` in the light column; `FieldBd` separates from `BdStrong`. Reaches
   329 field call sites plus every directly built box.
2. **Give the dropdown a chevron well** — a reserved right gutter with its own
   edge, so a long value truncates before it reaches the arrow and the disabled
   state takes the chevron with it.
3. **Give the secondary button its own fill** off `Card2`, and move buttons to a
   larger radius than fields, so an action and an input are different shapes.
4. **Give a read-only value no box**, and reserve the bordered-but-inert
   rectangle for one meaning: a field you cannot type in now.
5. **Add a tonal weight** between primary and secondary.
6. **A disabled primary stays visually primary.** Today it drops to an outline,
   so a blocked screen has no filled control at all.

## Phase 2 — The four missing token families

7. **One focus ring**, in every control template. Eleven of thirteen have none.
8. **Elevation tokens** with a theme-aware colour and one direction, replacing
   five hand-written shadows and the black-in-dark-theme defect at all five.
9. **A radius scale**, collapsing ten ad-hoc values.
10. **A spacing and control-height scale.** This cannot retro-fix 678 literals,
    but it stops the count growing and makes the 191 shapes auditable.

## Phase 3 — Repair the type ramp

11. Make the dead 18px step a real screen-title style, distinct from a card title.
12. Split the label size from the control-caption size, which are one token today.
13. Add a strong body style, removing 64 inline weight assignments.
14. Merge the two 11px styles that differ only in weight.

## Phase 4 — The vocabulary the product is missing

15. **Required, optional and automatic marks on fields.** The styles exist and
    are dead.
16. **A save-state line in the footer anatomy**, filled by every screen.
17. **Form-completeness in the blocking-reason contract**, so a form states what
    is missing before the press rather than after it.
18. **One state banner** — blocked, licence-limited, error, stale — replacing the
    five renderings of "no model is open" and giving the missing states somewhere
    to live.
19. **One status token**, drawn identically on the board, the counters and both
    Exchange tables.
20. **A severity token**, carried out of the matrix onto the board and the export.
21. **One chip doctrine**: static metadata, status and count, interactive toggle,
    severity — four named roles on one geometry, replacing five hand-built shapes
    at three radii and three heights.
22. **`Ui.Plan`**, generalised from the renumber preview, with its seven
    properties, for every bulk write in the product.
23. **A determinate progress contract** and an outcome that carries a route to
    where the result landed.

## Phase 5 — Structure the page

24. One page header; one command bar; one list footer and one selection noun;
    one empty-state card; one search field; a pane content-column token.

**Done.** One selection noun — every tally that counts selected rows says
*selected*, and `Ui.Worded` drops the count entirely when nothing is selected.
One empty-state card — `Ui.Empty` is centred in its pane and its way out is the
primary. One search field — a `Find` style with the magnifier inside the box,
adopted at all eleven search call sites. One footer anatomy — Report was the
last tab building its own; it now uses `Ui.Foot` like the rest. A control-width
scale — `Ui.Wide`'s six content classes replace thirteen ad-hoc values across
208 controls.

**Not done.** One command bar: the tab strip, the matrix toolbar and the board's
rail tools are still three grammars.

## Phase 6 — Reconcile with the web product

25. Move the accent to the BIMCamel brand blue, reverse the hover direction,
    split the accent wash from the info tint, and make the primary gradient the
    brand's 135° blue-to-indigo.

**Done**, all four. The app-family hue on the tab strip is unchanged: 1.3 states
the argument against it but this phase does not prescribe a change.

## Phase 7 — Clear the dead system, and the faults of fact

26. Delete or wire up the seven dead styles, the five dead palette tokens and the
    ~370 lines of primitive with no callers.
27. Fix every item in Part 3, and correct `docs/PRIMITIVES.md`.
28. Repair the two harness fidelity defects so the renders cannot drift from the
    product again.

**Done.** `Title` is wired up as the screen-title style, which is what phase 3
asked for; `IconBtn`, `OptionCard` and `TabBtn` are deleted, as are `Veil`,
`ConsoleBg` and `ConsoleTx`. The three badges and `AccentHover` were given
callers earlier in this work. `docs/PRIMITIVES.md` records the real call counts.
Faults 1 to 8 and 10 to 12 are fixed.

**Not done.** The primitives with no callers: most candidates are extension
methods, and a text search cannot tell an uncalled one from one called on its
receiver. Fault 9, the Overlap column, is held for a licensed read.

## Then, screen by screen

The `[SCREEN]` items in Part 2, in tab order. Several dissolve once phases 1 to 5
are in place; the ones that will not are Manage's empty element columns and
hidden row verbs, Report's inverted layout, the matrix's toolbar and its three
colour scales, Team's column shares and active flag, and the Settings rail's two
naming schemes.
