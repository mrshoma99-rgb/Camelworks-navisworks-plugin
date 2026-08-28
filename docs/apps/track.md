# Compare Track — product audit

**The app's job.** Time. Fix a point (baseline), measure against it (compare), walk what moved
(changes), put a number and a name to it (change notice), and answer "who did what" (history).
The coordinator opens it when a revision lands and when somebody says "that moved after we
agreed it".

**No status strip.** It went from every app: a band of figures repeating what the surface below
already said.

**Comparison is not only against your own past.** The app used to mean one thing by "compare" —
this model against a snapshot of itself. Three of the comparisons a coordinator actually makes
are not that: the supplier returns the model you sent them, a revision arrives as its own file
appended beside the old one, and two sets that are supposed to hold the same thing do not. All
three are the same question — what is in one and not the other, and what is in both but
different — and they are now the **Side by Side** tab.

## Compare — the measuring tab

- *Was wrong:* the tab opened on empty space with the baselines register buried at the bottom —
  so the first question, "what can I even compare against?", was a scroll. The register of kept
  baselines is now the landing data, with taking a new one beside it and the comparison's
  answer underneath once there is one.
- *Held:* the triangles/bbox trade-off stays a stated choice on both the take and the run; the
  holding-both-revisions trap is still said before the button, not discovered after it.

## Side by Side — two things that are both here now (new)

- **Two models**: pick either loaded model on each side. They pair as counterparts even when
  their file names differ (the engine's own only-candidate rung), which is exactly the supplier
  case — you sent them a file, they sent back a differently-named one.
- **Two sets**: any search set or selection set on each side, read out of this document.
- Both sides become snapshots taken here and now, and the ordinary comparison runs between them,
  so the matching rungs, the move tolerance, the moved-not-replaced pairing and the property
  diff behave exactly as they do against a baseline.
- The verdict is said in the words of what was compared: *only in A*, *only in B*, *moved*,
  *changed*, *the same* — "added" and "removed" are the baseline comparison's words and would be
  wrong here.
- The differences grid focuses, isolates, copies, writes to Excel, and right-clicks into the
  Issue Manager like every other results grid in the product.
- *Stated limit:* bounding boxes and properties, not triangles. An element remodelled inside its
  own box reads as the same; the baseline comparison on the Compare tab is the one that reads
  triangles.

## Freeze — sign-off, moved here from Clash Manager (new tab)

- A freeze is a named baseline somebody put their name to: each element's box, the signatory,
  the release reference, and a hash that stops an edited record passing as genuine. It sat in
  Clash Manager only because its element list was harvested from the clash board.
- **The scope is now anything the model can name** — the current selection, a search or selection
  set, everything loaded, or the board when one is loaded — so sign-off does not depend on a
  clash run having happened.
- It sits beside the Change Notice, which is the half that earns it, and each points at the
  other: the freeze offers *Write the change notice*, and the notice with nothing frozen offers
  *Freeze something*.
- The Change Notice's own grid gained a picking bar (focus the picked rows, copy them) rather
  than one-row-at-a-time clicking.

## Changes — the walking tab

- **How a finding reaches the 3D view**, which is the whole point of the tab: paint the four
  buckets onto the model (added green, removed red, moved amber, changed blue, unchanged faded)
  with one press and one press back; click a row to select and frame that element; pick rows and
  **Focus** or **Isolate** them; **Show everything** puts the view back.
- **Removed elements are the honest exception** — they are not in this document, so nothing can
  frame them until the baseline's own file is appended, which the tab offers and takes back out
  again. Until then the red layer has nothing to colour, and the tab says so rather than
  pretending.
- *Was wrong:* "Raise them on the board" listed findings, claimed they had been raised, and
  pointed at a tab that no longer exists — nothing was ever written anywhere. It now raises real
  issues into the **Issue Manager**, each carrying the elements it is about.

## Change Notice

- Reads the freeze written on the tab before it, and each points at the other. Its grid focuses
  and copies the picked rows rather than clicking one at a time.

## History

- The activity log as a pickable register, newest first, for the handover question. Unchanged.
