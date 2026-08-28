# Site Pack — product audit

The model reaching the people building it. Tabs: Setting Out · Dimensions · Weight · Labels ·
Documents. Walked as the coordinator with the site engineer's requests on the desk: points for
the total station, a dimension schedule instead of on-screen measuring, where the hook goes on
Saturday's lift, stickers for the modules, and "where are the data sheets".

## The app as a whole

**Wrong.** The five tabs share one read of the selection — the right economy, since all five
answer questions about the same forty elements and two want the same triangles — but the
shared state was half-visible: a tab opened second said "already read, from the selection at
the time" with no age. A count with no age reads as current, and a selection read before lunch
is not; the person then trusts a Tuesday-morning read on Tuesday afternoon.

**Changed.** The shared read now carries a timestamp. A tab opened onto an existing read says
"214 elements read 5 minutes ago, shared by the five tabs. Read again if the selection has
moved" — the age from `ActivityLog.Ago`, the re-read affordance being the Read runner directly
above the line. The timestamp is cleared with the cache when the session changes.

## Setting Out

**The job.** The engineer walks the floor with a total station; the coordinator produces the
numbered points file in site coordinates.

**Found.** The grid transform states its own consequence ("wrong, and every point is out by
the same amount"); the recipe (offset and angle — the part that is a fact about the project)
saves per project and comes back; the unshifted-coordinates case is warned in the file and on
the glass; the points table is pickable. Sound.

**Changed.** Shared-read age only.

## Dimensions

**The job.** Answer "how long is that run, how far off the slab, are the hangers at centres"
for every element at once, instead of measuring on screen and retyping into an email.

**Found.** The what-to-measure ticks are remembered per project and an all-unticked state is
repaired to a sane default; heights name the level they were taken from because a derived
level is worth less than an authored one; the schedule is pickable — the failed rows are the
ones that go in the email; the written file states its unit. Sound.

**Changed.** Shared-read age only.

## Weight

**The job.** Where the hook goes: the centre of gravity of Saturday's lift, off the mass the
model carries or the volume of the shapes.

**Found.** The mass-property candidates are read off the selection itself (including the
"Unit Wt" case), which is the picker-not-typing rule already honoured; the answer says whether
it is a true centre of gravity; the position can be copied or boxed so it can be seen rather
than read. Sound.

**Changed.** Shared-read age only.

## Labels

**The job.** A sheet of stickers, a code on each, for somebody to put on the work.

**Found.** What the code says is picked from properties that actually behave like marks —
nearly filled in, not constant, short enough to print — and the mark fallback needs no setup.
Loss level is explained in consequence terms ("the more it can lose the bigger the code"). The
printed .csv index is written beside the sheet so the codes can be looked up. Sound.

**Changed.** Shared-read age only.

## Documents

**The job.** The commissioning engineer asks for the O&M paperwork; the model already points
at it; this tab follows the pointers and says what is there, what is missing, and which
folders are there and empty.

**Found.** The naming properties are derived from the selection, the root for relative paths
is a stated option, a found row opens its file, the rules save per project, the empty-folders
list is pickable. Sound.

**Changed.** Shared-read age only.

## Judged missing, out of scope

- **A hand-off from "Nothing is selected" to Sets Manager** (select by rule, come back) —
  considered; the empty state already says what to select in site terms, and a jump that
  abandons the half-configured tab costs more than it saves. Named here in case the ask
  recurs.
- **Weight of an assembly against a rigging plan** (hook positions, sling angles) — a rigging
  engine the product does not have; the copied centre-of-gravity position is the honest edge.
- **Direct total-station formats beyond CSV** (e.g. Trimble JobXML) — a format engine that
  does not exist; the CSV is what the survey office asked for first.
