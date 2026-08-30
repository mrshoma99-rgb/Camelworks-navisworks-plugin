# Compare Track

> What changed, against what, since when.

Time. Fix a point, measure against it, walk what moved, put a name to it, and answer *who did
what*.

**Tabs:** [Compare](#compare) · [Side by Side](#side-by-side) · [Changes](#changes) ·
[Freeze](#freeze) · [Change Notice](#change-notice) · [History](#history)

Comparison is not only against your own past. Three of the comparisons a coordinator actually makes
are not that — the supplier returns the model you sent them, a revision arrives as its own file
beside the old one, two sets that should hold the same thing do not. Those are
[Side by Side](#side-by-side).

---

## Compare

> Say what changed between the model in front of you and a baseline — moved, resized, added,
> removed, retyped.

**Main action: Run the comparison.**

The baselines you have kept are the landing data: **pick a row and that is what the run compares
against**, with a line under the table saying so. Recording a new one is the one thing on the rail.

The picked rows can be copied or deleted — deleting names what it is about to destroy and asks
twice, because a baseline is a week of history nothing else in the product can rebuild.

### What counts as a change

Behind the ⚙ symbol:

| | |
|---|---|
| **Move over** | Below this, an element has not moved. This is what stops a re-export's floating-point noise reading as a moved federation. |
| **Properties** | The standard list, every property, or none |
| **Match on** | GUID, tree path or shape — how an element is recognised as the same element |
| **Grouping nodes** | Whether levels, categories and families count |
| **Read the actual triangles** | The only way to see an element remodelled *inside* its own bounding box, at the cost of a geometry pass per element |

Triangles are off by default and the screen tells you whether reading them is worth it against the
baseline you picked: if the baseline was taken with bounding boxes only, reading triangles now has
nothing to compare against, and would report "nothing was remodelled" — the most expensive way this
feature could lie.

### One trap, said before the press

If you have appended the old revision to see what was removed, a fresh comparison sees both
revisions at once, pairs the baseline model to itself, and reports that nothing changed. The tab
says so on arrival — take it back out in **Changes** first.

---

## Side by Side

> Compare two things that are both loaded now.

**Main action: Compare.**

- **Two models** — pick either loaded model on each side. They pair as counterparts even when the
  file names differ, which is exactly the supplier case: you sent them a file, they sent back a
  differently-named one.
- **Two sets** — any search set or selection set on each side.

Both sides become snapshots taken here and now, and the ordinary comparison runs between them, so
the matching, the move tolerance and the property diff behave exactly as they do against a
baseline.

The verdict is said in the words of what was compared — **only in A**, **only in B**, **moved**,
**changed**, **the same**. *Added* and *removed* are the baseline comparison's words and would be
wrong here.

The differences grid focuses, isolates, copies, writes to Excel, and right-clicks into the
[Issue Manager](Issue-Manager).

**Stated limit:** bounding boxes and properties, not triangles. An element remodelled inside its
own box reads as the same. The baseline comparison on the Compare tab is the one that reads
triangles.

---

## Changes

> The change list, filtered and painted onto the model, so a revision can be walked rather than
> read.

**Main action: Paint it onto the model.**

This is how a finding reaches the 3D view:

| Bucket | Colour |
|---|---|
| Added | green |
| Removed | red |
| Moved | amber |
| Changed | blue |
| Unchanged | faded |

One press paints it; the symbols beside the button take the colours off and show everything again.

- **Click a row** to select and frame that element.
- **Pick rows** and Focus or Isolate them.
- The strip at the top filters by bucket and searches the list.

**Removed elements are the honest exception.** They are not in this document, so nothing can frame
them until the baseline's own file is appended — which the tab offers, and takes back out again
afterwards. Until then the red layer has nothing to colour, and the tab says so rather than
pretending.

Findings can be raised as real issues into the [Issue Manager](Issue-Manager), each carrying the
elements it is about. They are kept beside the model first and attached to the issue, so the issue
points at a record that survives rather than one the button invented and dropped — which means the
next comparison knows what you decided about the last. The changes can also be written onto the
elements as properties.

---

## Freeze

> Record that a scope was agreed and what it was at the moment of agreeing.

**Main action: Freeze.**

A freeze is a named baseline somebody put their name to: each element's box, the signatory, the
release reference, and a hash that stops an edited record passing as genuine.

The scope is **anything the model can name** — the current selection, a search or selection set,
everything loaded, or the clash board when one is loaded. Sign-off does not depend on a clash run
having happened.

It offers **Write the change notice** afterwards.

---

## Change Notice

> What has changed since a scope was frozen, written the way a variation has to be written.

**Main action: Write the change notice.**

Reads the freeze from the tab before it. With nothing frozen, it offers **Freeze something**. Its
grid focuses and copies the picked rows.

---

## History

> What has happened on this project and who did it, newest first.

**Main action: Copy.**

The activity log as a register you can pick rows out of — the handover question, answered with a
record rather than somebody's memory.
