# Scene Kit

> What is visible, and what colour it is.

**Tabs:** [Appearance](#appearance) · [Viewpoints](#viewpoints)

---

## Appearance

> Control what is visible and what colour it is, as layers that stack and can be taken back off.

**Main action: Apply the stack.**

Navisworks loses the answer to *why is this hidden* the instant you hide something. Appearance is a
stack of **named layers** instead: each says what it covers, what it does, and can be switched off.
That is what makes a federation handed over rather than abandoned.

### A layer

- **Covers** — the current selection, everything, one of the project's saved rules, or one of the
  **document's own Navisworks sets**. A host set becomes a rule, so the layer follows the set:
  republish the set with more elements and the layer covers them too.
- **Does** — colour (`#rrggbb`, with the swatch showing the result), transparency, hide, show.

The stack applies top-down and comes back off in one press.

### What somebody else did

A federation is nearly always already coloured or part-hidden by someone. **Scan the model** finds
every appearance state no layer accounts for, and reports each one **where it was applied** rather
than on every element that ended up with it:

| Where | State | Elements | |
|---|---|---|---|
| ARCH / Level 03 | hidden | 412 | hidden there, so everything under it is |
| MEP / Level 03 / Ductwork | `#c81e1e` | 180 | everything under there is |
| 1,204 places | `#3cb44b`, 40% transparent | 1,204 | |

That matters because Navisworks applies an override to a node in the project tree and every element
under it takes it. One act on a storey is four hundred coloured elements; a row each would be four
hundred rows and, if you adopted them, four hundred layers.

- **Select** them — see what they are.
- **Make them a layer** — adopt the state into the stack, as **one** layer naming the place. The
  model looks exactly as it did; what changed is that the stack now says *why*.
- **Put them back** — clear the overrides and un-hide.

All three work on however many rows you pick. **Overrides CamelWorks did not author are never
cleared without being asked.**

Rows are grouped by state and ordered **deepest place first**, which is also the order they go into
the stack: a colour on `MEP / Level 03 / Ductwork` has to decide its elements before one on
`MEP / Level 03` gets to, or the more specific layer covers elements and decides nothing about any
of them.

**Where** is read for a hiding and worked out for a colour, and the screen says which. The host
records hiding on the item it was applied to and lets descendants inherit it, so the level is a
fact. Colour and transparency it stores on the geometry, so an override put on a storey is pushed
down to every element under it and the storey keeps no note of it — the place shown is then the
highest node whose every element is in that one state, which is what a storey-level override looks
like afterwards.

A model where thousands of elements each carry their own colour is a painted model, not a
coordination problem — past a hundred states those are counted but not listed, and the screen says
so.

---

## Viewpoints

> Rename, renumber, re-folder and render viewpoints in bulk, instead of one at a time down the
> tree.

**Main action: Save this view.**

The document's viewpoints as a table. Tick the ones to act on — with nothing ticked the batches act
on all of them, **except deleting, which never does**.

Behind the ⚙ symbols on the action row, each opening on its own page:

| Symbol | Batch |
|---|---|
| ✎ | **Rename them** — a pattern with `{n}`, `{name}`, `{folder}`, and a starting number |
| 📁 | **File them into a folder** |
| 📷 | **Render them to images** — pre-filled with a folder beside the document |
| 🎨 | **Copy overrides onto the rest** — with a warning where redlines are involved |
| ⊕ | **A viewpoint per issue** — one per group on the clash board, named as an agenda |
| 🗑 | **Delete them** — only what is ticked, never all |

Each page reports what it did where you pressed the button.

**Make one for each element** is on the stage: name pattern, numbering, where to look from, how
much room to leave, an optional section cut, and a folder. It shows the names it would create
before making anything.

This is the tab that turns *forty minutes of framing and typing* into one press — which is why
reviews are usually one person driving and everybody else watching.
