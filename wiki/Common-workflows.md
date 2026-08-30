# Common workflows

Five things a coordinator actually does, with the presses in order.

---

## Monday: is this drop fit to work on?

1. **[Model Audit ▸ Health](Model-Audit#health)** → *Check this federation*. One verdict, worst
   first.
2. Any finding about a **missing source** hands off to
   **[Broken Links](Model-Audit#broken-links)** → *Look for them* → pick the rows → *Bring in*.
   Then remove the stale copy in the Selection Tree and save.
3. **[Models](Model-Audit#models)** → *Take a manifest*. Now *which revisions is this board
   against?* has an answer that is a record, not a memory.

If the manifest says nothing has been republished, a clash run produces exactly last week's board.
Worth knowing before the meeting rather than during it.

---

## The clash week

1. **[Clash Manager ▸ Matrix](Clash-Manager#matrix)** — build the test set from a grid, or read
   the client's spreadsheet. Give the tests a name pattern and a folder.
2. **[Manage](Clash-Manager#manage)** → pick the tests → **run** → *Read the results*.
3. **[Filter Clashes](Clash-Manager#filter-clashes)** — suppress the noise, group what is one
   issue, assign the rest. *Try these on the board* first; save when the four figures look right.
4. Back on **Manage**, work the results: focus, isolate, status, assign, comment, group.
   Right-click → **Create an issue from N** for anything that needs chasing.
5. **[Clearance Check](Clash-Manager#clearance-check)** — the defects no clash test can find.
   Point it at the corridors by naming the corridor set.
6. **[Issue Link ▸ Report](Issue-Link#report)** → *Write the report*, one file per party.
   It then offers BCF or Clash XML for whoever answers in a tool.

---

## The offline review round

For a reviewer who has no Navisworks, or a coordinator on **Simulate**, where the clash engine is
not available.

1. On a machine with Manage: **Clash Manager ▸ Manage** → the ⚙ **The review round** → export the
   results as a file.
2. The reviewer opens that file in **Manage** on their machine (source: *A file*), marks up,
   comments, and saves.
3. Back on the original machine, the same ⚙ page **syncs the markups back** onto the model's own
   clash data.

The [Clearance Check](Clash-Manager#clearance-check) writes the same file format, so a clearance
list runs the identical round trip.

---

## A revision lands

1. Before it lands: **[Compare Track ▸ Compare](Compare-Track#compare)** → *Take a baseline*.
2. When the new models are in: pick that baseline in the table → *Run the comparison*.
3. **[Changes](Compare-Track#changes)** → *Paint it onto the model*. Green added, red removed,
   amber moved, blue changed, faded unchanged.
4. Walk it: click a row to frame that element; pick rows and Focus or Isolate.
5. Anything that needs chasing → raise it into the [Issue Manager](Issue-Manager).

**To see what was removed**, the tab appends the baseline's own file — removed elements are not in
this document, so nothing can frame them otherwise. It takes it back out again afterwards, and you
must do that before comparing again.

If the model you are comparing is not your own past — a supplier's return, two sets that should
match — use **[Side by Side](Compare-Track#side-by-side)** instead.

---

## Somebody says "that moved after we agreed it"

1. At the time of agreeing: **[Compare Track ▸ Freeze](Compare-Track#freeze)** — name the scope,
   the signatory and the release reference.
2. When the argument starts: **[Change Notice](Compare-Track#change-notice)** →
   *Write the change notice*. It reads the freeze and writes what changed the way a variation has
   to be written.

The freeze is hashed, so an edited record cannot pass as genuine.

---

## The QS wants numbers per floor

1. **[Data Manager ▸ Levels & Zones](Data-Manager#levels--zones)** → *Derive levels* → stamp them.
2. It offers **Take off quantities by level** →
   **[Quantify ▸ Takeoff](Quantify#takeoff)**, whose Level axis reads the stamp first.
3. Set the measure and the axes → *Sum it*. Unreadable values are counted and named, never dropped.
4. The sheet lands beside the project file, where the rest of the team can see it.

---

## A property is missing across six models

1. **[Data Manager ▸ Properties](Data-Manager#properties)** → *Read properties* over the scope, and
   see what is blank.
2. **[Write](Data-Manager#write)** → choose the source — a value, a formula, a code table or a
   spreadsheet → *Read what is there now* → check the diff → commit.
3. If somebody outside Navisworks has to supply the values, use
   **[Excel](Data-Manager#excel)** instead: write the sheet, send it, read it back. The tab
   remembers which file went out.

---

## Handover Friday

- **[Scene Kit ▸ Appearance](Scene-Kit#appearance)** → *Scan the model*, adopt or clear whatever
  somebody else left coloured or hidden. A federation nobody can explain is a federation nobody
  accepts.
- **[Export Kit ▸ Compile](Export-Kit#compile)** → the delivery folder into one federation, newest
  revision of each, with a log.
- **[Export Kit ▸ GLB](Export-Kit#glb)** for the client who wants it on a phone;
  **[IFC](Export-Kit#ifc)** for the one who wants properties.
- **[Automate ▸ Jobs](Automate#jobs)** → *Make a job from what is open*, so next Friday is one
  press.
