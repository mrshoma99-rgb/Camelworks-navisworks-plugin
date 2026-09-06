# Clash Manager — product audit

**The app's job.** The clash week, end to end: say what gets tested, run it, refine the noise
away, decide what is left, and record what was agreed. A coordinator opens it Monday after the
new models land and lives in it until the meeting.

**No status strip.** Every app used to carry a band of figures across the top of every tab. It
cost a row of screen on every surface to say things the surface below it already said, and the
one figure that was not a repeat — what the board held — belonged to a tab that no longer
exists. It is gone from every app.

## Manage — the finder and the board

The tab a coordinator lives in, measured against the clash managers they actually use — the full
gap analysis is in [clash-manager-gap.md](clash-manager-gap.md). It has been rebuilt three times:
for what it could do, then for how it read, and now for what it is actually used to do.

**What the third pass was for.** The second pass put the tests and the results side by side and
gave each table its own row of verbs. It was right about the split and wrong about everything
that hung off it. A coordinator's week is two activities — *find the test*, then *review, edit and
group the clashes in it* — and the tab spent its width on neither: seventeen always-visible verb
symbols sat over the two tables, greyed rather than hidden, which somebody's own words called a
wall of buttons that read as pressed rather than off; and the two facts that change every minute,
a result's status and who it is with, were behind a window opened by one of those symbols.

- *Changed:* a **test finder** on the left and **one board** on the right.
  - The finder is one row per test, read **down** rather than across: the name, one line of hint,
    and the four figures as a band. A tone bar down the left edge is red while anything is new,
    amber while anything is open, and green when the test is finished — and the accent when it is
    the test on the board. Search, sort and a filter panel sit at the top of the list they act on;
    a filter comes off in the panel that set it, and the funnel turns accent while one is in
    force. The chips row that used to say so was a row of the list it was describing.
  - The board carries the picked test's results as **two kinds of row at the same level**: a
    **clash group**, which is a real object in the test, and a **loose result**, which is not in
    one. Navisworks' own **Item 1 / Item 2** pair keeps two columns rather than being merged into
    one sentence, and a group shows em-dashes there because it aggregates many pairs and has none
    of its own.
  - **Status and assignee are edited where they are read** — an inline chip on the row opens a
    small list. The window they used to live behind is gone.
  - **Row verbs are hidden, not greyed**, and appear on hover or when the row is picked. Three on
    the surface — focus, isolate, comment — and the rest behind one symbol.
  - The **status strip** is five segments carrying the counts, and it is the only way the board is
    filtered by status. There are no status tickboxes anywhere any more.
  - A **selection bar** exists only while something is ticked. Tick two or more loose results and
    its Group button turns primary and says how many it will group.
  - **Arranging the view** by assignee, status or how bad buckets the rows into bands and
    **touches no clash group**. The bands are deliberately shaped nothing like a group row — no
    tick, no chevron, no tone bar — and the footnote under the table says so in as many words,
    because somebody who believes arranging regrouped their board hands a report to the wrong
    party.
  - Loose results are gathered under one always-visible band rather than behind a tab: grouping
    them is the job, so the rows it is done to have to be on screen without being found first.

**What the setup windows became.** There were six sections behind six symbols. Four are gone
because the board does what they set up where it happens: the filter page is the status strip and
the chips; "what the status, assign and group verbs write" is the value picked on the chip itself;
and the grouping chain became the sort menu's band chain, on the rows, saying that nothing moved —
and able to say the word that makes it move, once the arrangement is the one wanted.
Two are left, and the board's own header names each: **Settings** and **Columns**. Settings was
"Source and tolerance" and was neither: it held where the results come from, how far the camera
stands back, how big a marker is and what colour it is drawn, under a title naming one of them.
The review round moved into the footer's *Export* menu, with the rest of the ways work
leaves the product.

**Deviations from the handover, and why.** The design asks the finder to filter by *Level*.
Nothing on a clash test carries one, and a level guessed out of a test's name is a filter that
hides tests for a reason it cannot state — so the chips are **Folder**, which is the grouping the
host does carry and where coordinators put the level. The design's footer also carries a revision
line ("Rev C issued to Structures"); no Navisworks document holds an issued revision, and a
number somebody reads out in a meeting is the wrong thing to invent, so the footer carries four
derived facts instead. And the design's footer shows both a greyed *Sync into the document* and a
primary *Save the changes*: here every edit is already written the moment the chip is picked, so
what is left to save is what the host refused — the primary **is** the sync and counts what is
waiting for it, and the menu carries the refusal's reason only when there is one.

**What the pictures changed.** The tab was rendered in `CamelWorks.Studio` before it was called
done, and four things only a picture could say came back from it.

- **The table fits the board rather than scrolling sideways.** The design fixes nine columns at
  862 px, which is wider than the board is at the window's own default size — so the first render
  came back scrolled off its status and assignee columns, the two things this redesign exists to
  put on the row. The name and both item columns flex now, and the sideways scroll is what it was
  meant to be: the last resort when the board is genuinely too narrow.
- **Every text style in the skin sets `TextWrapping="Wrap"`,** so `TextTrimming` never engaged and
  cells wrapped to two lines instead of ellipsising. Every trimmable cell now says `NoWrap` on the
  instance, and carries its full text as a tooltip.
- **The chips share the status strip's row.** They had a row of their own, which cost the board a
  row of results to say what it was filtered to — and what it is filtered to is one short phrase.
  On the rail the chips went entirely: the funnel carries the state, the panel carries the way to
  take it off, and the footer line already there says how many tests are hidden.
- **One primary.** `Ui.Runner` defaults to the primary weight, so "Read the results" and "Save the
  changes" were two blue buttons in one footer, which is the same as none. Reading is the way back
  to the board, not the thing the screen is for once the board is on it.

Two smaller ones: the view menu wore the round-trip glyph, which is the one BCF and the review
round use, and beside a search box it read as a sync button — it is the layers glyph now; and the
row-verb column was a symbol short, because four 26 px buttons do not fit the design's 92 px.

**A second pass over the same pictures** took more out than it put in.

- **The line beside the test's name went.** It said what the test was between, what it ran at and
  when — none of which changes while the board is worked, so the eye read it once and then had to
  read past it. It is the title's tooltip now, and the space carries *Showing 18 of 168*, which is
  the one number on that line that moves.
- **The chips row went from the board too,** on the same argument as the rail's: the lit status
  segment, the search box and the layers menu each unset what they set, and the count beside the
  title says how much is hidden.
- **The footnote went.** It was the right argument in the wrong place — a paragraph of
  documentation under a table somebody reads four hundred rows of. What it was defending is in the
  wiki and in the shape of the screen: bands look nothing like group rows, and the grouping verbs
  say what they do.
- **The view menu moved up** beside the other symbols that open something, rather than sitting
  next to the search box where it read as a button that acted on the board.
- **The grouping verb is one verb, chosen by what is ticked** — Group, Remove from the group,
  Dissolve the group, Merge the groups — instead of a Group button that is wrong four times out of
  five and says so only after it is pressed. It no longer counts the rows either: the bar it sits
  on already says how many are picked.
- **One action list.** The row menu and the selection bar were written separately and immediately
  disagreed; they are built from one method now, so picking three clashes offers what picking one
  of them offers.
- **Columns sort and resize**, and **double-click renames** a test, a group or a clash.
- **"Send the board" became "Export."** It named the metaphor rather than the act.

**A discipline is read, never guessed.** The rail's discipline chips come from the test's two
sides: the saved selection set behind each side, or the model's name when the side is whole models
rather than a set. There was a fallback that split the test's *name* on " v " when neither was
readable — which is the same fault as inferring a level, and it is gone. A test whose sides are an
ad-hoc selection has no discipline to filter on and says so.

What the bench does has not shrunk: read from the model or from a file; run, rename, renumber,
move, compact, delete and copy tests; focus, isolate, mark, assign, comment, group, ungroup,
rename, sphere, snapshot, raise an issue and copy results; property columns; the duplicate
finder, the spreadsheet, the baseline and the whole review round.

**A status is a status, not a word.** A result's status was a string from the host read to the
board, and the five words were written out at eighty-odd places across this tab and Clearance.
Two of those places ranked an unrecognised word differently — one dropped it from the test's tally
altogether, the other sorted it last — so a result could be counted one way and ordered another,
and nothing in the build could see the disagreement. The board now holds `FindingStatus`
throughout: the tally, the status strip's filter, the group row's worst-of-its-members, the chip,
the marker colour and the row's tone bar. A word is read from outside once, through
`ClashStatusMap.Read`, at the host and file boundaries, and a status becomes text once, through
`FindingStatusWords.Of`. Nothing on screen changed: the status column still sorts by the word, so
its order is alphabetical as before rather than the lattice's.

**The board reads a check, not the clash engine.** The tab used to name the host's clash seam at
every turn: forty-nine calls to `IClashSource`, a rail row built from `ClashTestInfo` and
`ClashTestSides`, a board row built from `ClashResultInfo`, and fifty-two branches on whether the
results came from the model or from a file. It now holds one `ICheck` — `ClashCheck` over the
document, `FileCheck` over an opened baseline or markup — and draws whatever that returns.

- A rail row is a `CheckRun`: the check words the kind, what the run is between, its threshold and
  its trades, so the board composes none of them. The board reads the threshold's *number* from the
  check when it tones a row and never from the words it prints.
- A board row is a `Finding` and what the check wrote onto it. There is no separate result type.
- **The file leg is not a branch; it is a check with capabilities missing.** A file has nothing to
  echo a decision into, no host groups to nest in, nothing to re-run, no run of ours to rename and
  no elements in this document to read properties off, so it answers null to five of the six.
  Running, renaming by double-click and the property columns appear where the check offers them.
- **An edit is not lost because there is no echo.** Every edit on the board goes through one door:
  the register takes it first and unconditionally, the check's host echo is attempted where the
  check offers one, and a refused echo marks the field pending and the sync offers it again. That
  is what a published NWD has always done here; it is now what a file board does too, so a markup
  is recorded in the project as well as written back into the file.
- **A file board's results now reach the register.** They are matched to the document's own
  findings on the topic guid — the one id a file and a model agree on for one clash — so a seat
  that marked up the baseline and later opens the model finds one clash with one history, not two.

Nothing on screen changed: the rail, the board, the verbs, the counts and the wording are what
they were. Nothing in this repository can prove that, which is why the check itself is asserted in
`Core.Tests` against a fake clash seat and the board is checked by the compiler and the validators.

**Corrections to the above, found in review.**

- **A renumber's numbers can disagree with the board.** The register takes the names counted down
  the board; the host verb takes a stem and numbers the results in the document's own order, one
  call per test. The two agree while the board is in the order the document reports and disagree
  when it is not — sorted by depth or by assignee, or filtered. The register is the record, so the
  board and every later read of it are consistent and the document is the copy that is out of step
  until the names are pushed to it. This was recorded as harmless when the check went in; it is
  not, and `A_renumber_reaches_the_host_as_one_write_per_test` now pins it. Making the two agree
  costs a host call per result, which is what the numbering call exists to avoid.
- **Every edit on a file board reaches the register.** It did not. A row is a view of a finding,
  and the way back from a row to that finding was an index keyed on the host's test and result
  ids. A file row carries the file's test name, so on a file board the lookup found nothing and
  every status, assignee, group, comment and rename was applied to the screen and recorded
  nowhere, while the toast reported the count. The row's own finding is the handle now, and the
  index is the fallback.
- **One clash marked up twice is one decision.** A topic guid is derived from the element pair and
  the cell they meet in, not from the run, so two clash tests reporting one physical conflict
  produce two findings with one topic guid between them. Both the fold and the adopt claim a
  matched finding once and order their candidates, so one approval is one approved row and which
  finding wins does not depend on how the register enumerated. `checks/validate-claim-once.py`
  holds the line for the next match written on a key like this one.
- **An issue keeps its clash when the document takes a file's finding over.** Adopting removes the
  file-born finding, and an issue names its findings by id. The ids that move are returned and the
  attachments are re-pointed through the same door that writes issues and findings together.
- **A file board draws what the register holds.** The read re-pointed each row at the held finding
  and then drew the file's own status and trail over it, so a clash held as Approved in a file
  that still says New drew as New — and the first edit wrote the register from that stale row.
- **A comment edit is recorded before it is echoed.** Amending or deleting a remark wrote the
  document first and the register second, and did nothing at all on a check with no host echo. It
  now goes the way every other edit goes: the register first, the echo after, a refusal marked
  pending. The host's remark count and last remark are facts on the finding, restated by the same
  edit, so a deleted remark stops being counted and stops being exported.
- **A finding a file raised is closed by an adopt or by a person, and by nothing else.** Its run is
  the file's own test name, which is no host test id, so no host read speaks for it and none marks
  it absent. That is deliberate: a seat cannot close a clash it was sent on the strength of a
  document that does not run that test.
- **A BCF topic carries what the check measured and not the seam's own handles.** `Host id`,
  `Host test`, `Host comments`, `Host last comment` and `Runs` are left out of the topic
  description. They mean nothing in a viewer, and the two comment facts would put a stale copy of
  a remark beside the remarks the topic already carries.

**The columns are declared by the check, and lengths are metres.** The board used to hold Item 1,
Item 2 and Worst itself, at about twenty-five sites, and tone Worst against a tolerance field it
kept for whichever run was open. Those three are now `ResultColumn`s the check declares, and the
board's one column type covers them and the element-property columns somebody adds:

- **A column carries accessors, not the name of a fact to read.** Sorting, banding, range
  filtering and toning all go through the check's own `Number`, `Text`, `Bucket` and `IsBad`. A
  fact string is written to be read — it is rounded for display and says nothing about its unit —
  so nothing decides anything by parsing one.
- **Declared columns take the natural order slots.** They sit where the three they replace sat,
  between the name and the level, rather than being appended past Status.
- **A numeric column sorts as a number and offers ranges.** Two clashes 0.2 mm and 0.4 mm deep
  both print "0 mm" and used to sort in whichever order they arrived in. The filter menu offers
  six depth ranges rather than the several hundred distinct strings a real test holds. The Status
  column is unchanged and still sorts alphabetically by its word.
- **How bad a row is is the check's own measure.** `_toleranceMm` and the four helpers around it
  are gone. The band the view menu offers is `ICheck.SeverityBand` — "within tolerance", "past
  tolerance", "far past tolerance", "no measure" — instead of "Worse than 25 mm" and "Within
  25 mm". A run whose host records no tolerance reports no measure, and its figures still tone
  against the 25 mm a clash test is usually run at, which is now clash's own default rather than a
  number the board held for every check.
- **Every length is metres, fixed at the producer.** `ClashFindings` converts the document's units
  once, on the way in, so a finding's point, its depth fact and its identity cell mean the same
  thing whether it was read from the model or from a file. Eight `OnFile ? 1.0 : MetresPerUnit`
  sites on the board are gone rather than generalised, and with them two latent faults on a
  non-metric model: a baseline whose BCF cameras were scaled twice, and a distance written into a
  baseline in feet that the reader took for metres.
- **`Facts["Distance"]` is metres to the micrometre, under the same key.** The fact key is not
  renamed and never will be: facts merge additively into a held finding and none is ever removed,
  so a producer that stopped writing one key and started writing another would leave the old key
  drawing its last value for the life of the project. The new number goes under the old name, and
  the first read after this change overwrites it. The clearance producer's length facts changed
  the same way, at the same resolution.

## Tests — removed

The tab listed the engine's tests and carried a matrix builder. The Matrix tab has been the place
to build a matrix since it was rebuilt, and the list of tests is the left half of Manage — so the
tab was two things somewhere else, and it is gone. `ClashMatrix`, the first matrix engine behind
it, went with it: `MatrixGrid` and `MatrixPlanner` superseded it, with a legend, three ways in
and an Excel round trip.

## The Board — removed

The board tab is gone. It was the app's oldest screen and the one that aged worst: a derived
list of groups that could be filtered and assigned but not exported, whose real output — the
issues somebody chases — had nowhere to live except inside it. What a coordinator works is now
two surfaces that each own their job: **Manage**, the results the engine holds, and the
**Issue Manager**, the register of everything raised on the job. The board itself survives as
the derived thing underneath the report, BCF, the clash file and the merge — each of those tabs
builds it where it stands (**Load the board**, in place), so nobody navigates to fetch a
prerequisite any more.

Two things that lived on it moved rather than died: **Raise an issue** is now the Issue
Manager's own **New issue**, working on any elements rather than only beside a clash; and
**Review** — the one-finding-at-a-time palette — launches from the Manage tab's rail.

## Filter Clashes / Matrix

- **Filter Clashes** (called "Rules" before, which said nothing about what it does) is the
  reduction between the engine and the issue list, in the order it runs: **suppress and flag**
  every raw result against a predicate, **group** what survives (empty means the built-in
  model-pair → level → proximity stack), then **assign** first-match-wins. Its one action is
  *Try these on the board* — it builds the board with the unsaved edits and shows the four
  figures the rules exist to move — and nothing is written until Save. It acts on clash results,
  never on issues: a rule that quietly closed something a person wrote a sentence about would be
  the worst behaviour in the product.
- **The matrix has three ways in**: by hand (categories picked from the model's own search
  sets, selection sets or whole models — never typed), read back out of the model's existing
  tests (each test's sides become categories, each distinct kind-and-tolerance becomes a
  lettered legend symbol), or the client's spreadsheet (mapping dialog, preview, and a sanity
  check that every category names a set this model actually has). One grid and one legend
  either way — symbol, test type, tolerance, on/off — a name pattern and a folder for the
  tests it creates, a per-kind report after creating, jumps to run or manage them, and the
  whole matrix exported back out as an Excel file (grid on one sheet, legend on the next).
  The matrix survives tab switches, and the legend's test type now actually reaches the host —
  it was silently Hard for every cell before.

## Clearance Check — the test no clash engine runs

The tab was called "Clearance", asked "what counts as a floor", offered *Floors and slabs* as an
answer, and printed a table nobody could do anything with.

- *Was wrong:* neither input could name a set. A coordinator who has spent the week building
  search sets was offered a category guess, the current selection, or everything — so the check
  could not be pointed at the corridors, and there was no way to say what "floors and slabs" even
  meant. **Both inputs now list every search set and selection set the document holds**, plus the
  current selection and everything loaded. The guess survives as one option, renamed *Walking
  surfaces (a guess)* and spelled out on screen: it takes anything whose category or type contains
  floor, slab, stair, landing, ramp, platform or deck. The second input's *Everything except the
  surfaces* now means exactly that — the elements the first input did not take — instead of a
  second silent category guess.
- *Was wrong:* the findings were a dead table. Clicking a row selected it and that was the whole
  vocabulary. **The findings now get the Manage tab's bench**: click-to-sort on every column, a
  contains-filter over any column or one picked column, status ticks and only-with-comments,
  an ordered grouping chain (status, assignee, group, either element, either category, how short
  it is, commented or not, and any property column) with a summary band table that narrows the
  grid, element-property columns read off the surface, off what is over it, or both, and bulk
  Focus / Isolate / Mark / Assign / Comment / Group / Sphere / Copy on the picked rows.
- *Held honestly:* the judgement lives on the rows, not in the model's clash data — no clash
  engine found these, so there is nothing there to write into. It survives a re-run instead:
  status, assignment, comments and groups carry over onto the same surface-and-obstruction pair,
  so re-checking after a model update does not reopen everything somebody closed.
- *Was wrong:* the floor side was still a question the model should answer. Naming a set works,
  and it is a set somebody has to build first. **The floor side now offers detected walkable
  surfaces**, found in the elements' triangles: every up-facing surface within 10 degrees of
  horizontal, banded by elevation, clustered by shared vertex and larger than 0.05 m². The volume
  swept over a surface is that surface's own outline extruded, not its bounding rectangle, so an
  L-shaped plate does not sweep its missing corner and a ring corridor does not sweep its
  courtyard. Every breach carries the clear height and the point it was measured at.
- *Held honestly:* detection cannot tell a floor from a desk top. The only discriminators are
  which way a face points and how much of it there is, so a worktop, a window sill, a plant skid
  and the top of a wide duct all pass. So the option's own sentence says so — *Detected walkable
  surfaces — every up-facing surface over 0.05 m², which includes desk tops and duct tops* — the
  window states the count, the total area and how many elements refused before the check is run,
  the surfaces are listed largest first and any of them can be dropped, and *Prefer elements whose
  category names a floor* is a visible tick that is off by default. *Leave stairs out* is a second
  tick, also off: detection makes one surface per tread and then reads the flight above as low
  headroom over the treads below it.
- *Held honestly:* a seat whose primitive reader refuses triangles falls back to element bounding
  box tops, which is the coarser answer the check gave before, and the window says that is what
  happened. The named-set and guess options are unchanged for those seats.
- **Out of the tab:** the grid to a spreadsheet exactly as filtered, and every shown finding as a
  clash file — which the Manage tab's "A file" source opens, so a clearance list runs the same
  offline review round as a clash export.
- *Changed:* the row's status is `FindingStatus`, the same as Manage's. The tick-box filter, the
  "Mark as" choice, the group tally's five columns and the marker colour all key off it, and the
  three places that re-parsed a status out of the row's own text are gone. Nothing on screen
  changed.

## Freeze — moved out

Freeze has left this app for **Compare Track**, beside the Change Notice that reads it. It was
never clash-shaped: it records each element's box under somebody's name and hashes the lot, and
the only reason it lived here was that its element list used to be harvested from the board. Its
scope is now anything the model can name — the current selection, a search or selection set,
everything loaded, or the board when one happens to be loaded — so a level can be signed off in
a week when nobody has run a clash test.

## Out of scope, named

- Auto-loading the board on tab-open for small result sets was considered and declined: the
  "nothing runs when a screen opens" law protects a half-million-element federation from a
  misclick, and the self-pressing hand-off from Tests covers the case where the user just asked.
