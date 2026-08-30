# What it cannot do

Every one of these is a limit of the Navisworks API or a deliberate scope decision, not an
oversight. Each is said on the screen where it matters, too — this page is the list in one place.

## Things the Navisworks API does not offer

**Re-point a model at a moved file.** The document exposes append, merge and open. Nothing replaces
one model's source in place, and nothing removes a single model. So
[Broken Links](Model-Audit#broken-links) brings the right file in *alongside* the stale one, and
tells you to remove the stale one in the Selection Tree and save.

**Refresh a stale model from inside the app.** There is no per-model refresh for plug-ins. Health
reports that a source is newer than the save; reloading is yours to do.

**Write part of a document to NWD.** No API for it. [Extract](Export-Kit#extract) offers the honest
workaround: make it a set, then use Navisworks' own export.

**A CamelWorks entry on the selection tree's right-click menu.** The .NET API has no context-menu
extension point — checked against the 2027 reference assembly, not assumed. The one thing it does
offer attaches to a clash result and is Manage-only, which would make a Simulate seat load an
assembly that is not there. The Simulate story is worth more than the menu entry.

## Editions and versions

**The clash engine is Manage-only.** `Autodesk.Navisworks.Clash.dll` ships with Manage and not with
Simulate. On Simulate the clash screens say so in a sentence, and the
[offline review round](Common-workflows#the-offline-review-round) is the way a Simulate user still
works a board.

**One DLL per Navisworks year.** A plug-in must be compiled against the API of the release it runs
in. A single DLL built against one year loads in the others but its **ribbon tab silently fails to
appear**, because Navisworks reflects over attribute types that do not resolve across a major API
version. The bundle ships a folder per year for exactly this reason.

## Deliberate scope

**Nothing runs unattended.** A job runs when you run it, in the Navisworks you have open. There is
no scheduler and no headless mode.

**No multi-file batch.** A graph runs over the document you have open, not over a folder of NWFs.

**No rates and no money.** [Quantify](Quantify) stops at the quantity. A bill line is quantity ×
rate, and a wrong currency figure is worse than none.

**No takeoff history.** Each run overwrites the screen. The activity log keeps the one-line
summaries, which is a record but not a comparison.

**No zone geometry.** [Levels & Zones](Data-Manager#levels--zones) derives elevation bands well; a
spatial zone from named boxes or a zones model has no engine behind it.

**No rigging plan.** [Weight](Site-Pack#weight) gives you the centre of gravity. Hook positions and
sling angles need an engine the product does not have.

**No email.** The report writer produces one file per party; the roster holds their addresses; the
sending is yours. A mail engine is not in the product.

**IDS is read, not written by the checker.** [The IDS tab](Model-Audit#ids) authors and saves `.ids`
files, and Health runs a file against the federation — but nothing generates an IDS from the model.

## Two honest notes about the geometry comparison

**Side by Side reads bounding boxes and properties, not triangles.** An element remodelled inside
its own box reads as the same. The [baseline comparison](Compare-Track#compare) is the one that can
read triangles, and only if the baseline was taken with them.

**A baseline taken with boxes only cannot answer a triangle question later.** Reading triangles
against it costs the minutes and reports nothing, which would read as *nothing was remodelled*. The
screen says so before you press.

## The limit that matters most

**None of this has been run inside Navisworks from the build environment.** Compiling against four
years verifies the API surface and says nothing about behaviour. The unit tests prove the engines —
the clash pipeline, the set compiler, the appearance fold, BCF, the report writers, the clearance
check — on a machine with no Navisworks on it at all. What happens in the host is what your
testing, and your bug reports, are for.
