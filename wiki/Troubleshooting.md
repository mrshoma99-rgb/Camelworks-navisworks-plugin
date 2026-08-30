# Troubleshooting

## The CamelWorks tab does not appear

Almost always the "downloaded from the internet" mark on the files, after a manual copy. Navisworks
reports `PLUGIN_LOAD_02` or `0x80131515`.

```powershell
Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\CamelWorks.bundle" -Recurse -File | Unblock-File
```

Then restart Navisworks. The installer does this for you; a hand-copy does not.

If that is not it, check the bundle has a folder for **the year you are running**. A DLL built for
another year loads without error and shows no ribbon tab — see
[What it cannot do](What-it-cannot-do#editions-and-versions).

## A clash tab says the engine is not available

You are on **Simulate**. Clash Detective ships with Manage. Everything else works; for the clash
workflow see [the offline review round](Common-workflows#the-offline-review-round).

## A search or a rule matches nothing

The property name is spelled differently from what the model carries. Every property box in
CamelWorks is a **picker fed by the model** — open it rather than typing, and pick the name the
model actually has. A name in the form `Category / Name` targets one tab; a bare name matches
whichever tab carries it.

Use [Data Manager ▸ Properties](Data-Manager#properties) to see exactly what the elements carry.

## A write says it wrote, and nothing changed

Check the scope. The result line names what it read from — *read from the current selection*. If
you changed the scope box after reading, the table and the writers still hold the **last read**, and
the screen says so. Read again to move them.

## The comparison says nothing changed, and things clearly changed

You probably have the old revision appended, to see what was removed. The comparison then pairs the
baseline model against itself. Take it back out in [Changes](Compare-Track#changes) and run it
again — the [Compare tab](Compare-Track#compare) warns about this on arrival.

The other cause is the **move tolerance**: below it, an element has not moved. It is behind the ⚙
symbol.

## An element was remodelled and the comparison did not notice

Bounding boxes and properties do not see a change *inside* the same box. Turn on **Read the actual
triangles** — but only against a baseline that was itself taken with them, which the screen tells
you. [Side by Side](Compare-Track#side-by-side) never reads triangles.

## Something is hidden or coloured and I do not know why

[Scene Kit ▸ Appearance](Scene-Kit#appearance) → **Scan the model**. It finds every appearance state
CamelWorks did not author and lets you select it, adopt it into the stack, or put it back.

## A run seems to hang

Every long run reports progress and has a **Stop** button, which takes effect at the end of the
current batch. If a screen refuses to start with *something else is still running*, that is
deliberate: two walks of the same document at once is what the host does not survive.

Reading a very large baseline reports as it goes and names the file it is reading before it starts.

## A model shows geometry from months ago

Its source file has moved and Navisworks is showing its own cached copy, with no warning anywhere.
[Model Audit ▸ Broken Links](Model-Audit#broken-links) finds where it went. Until the stale model is
out of the federation, every report in CamelWorks includes it.

## Somebody else has the project

You get a strip across the top saying so, with **Take it over** on it. A refused save names the
sections they changed. On a synced folder (OneDrive, Dropbox) the lock cannot be trusted, so saving
carries on regardless.

## The project file could not be read

[Model Audit ▸ Project](Model-Audit#project) reports it, at the top of the tab — including when a
backup was used instead. Nothing that was saved before the last write is lost.

## Nothing here matches

**Help** on the ribbon carries the guide, the stated limits, the shortcuts, a sample project and the
diagnostics. Failing that, open an issue on the repository with what you did, what you expected and
what you got — and say which Navisworks year and edition you are on.
