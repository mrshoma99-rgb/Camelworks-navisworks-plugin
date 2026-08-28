# CamelWorks for Autodesk Navisworks

Coordination, data and delivery tools for **Navisworks 2024–2027**, Manage and Simulate.

**Free to use, including commercially. Not free to sell.** Apache 2.0 with the Commons Clause —
source-available, not open source. See [LICENSE](LICENSE), which carries a plain-words summary
alongside the terms.

---

## What it does

| On the ribbon | |
|---|---|
| **Project** | Health — one sweep for placement, geometry, arrivals and codes, ranked into one verdict · Setup, which says what was derived and why · the project profile · fix broken links |
| **Coordinate** | The clash results workbench · the issue register, however an issue was raised · review mode · the engine's tests and a matrix that builds them · the clearance check · the rule editor · reports · BCF in and out · clash XML |
| **Compare** | What changed between two revisions — added, removed, moved, changed — painted onto the model, stamped into the properties, or sent out as a report |
| **Data** | Every property folded to one row and one write pipeline · the Excel round-trip · joining a register onto the model · levels and zones · takeoff · a report on the model's own data · IDS |
| **Sets & Views** | The boolean set builder · a spreadsheet of conditions turned into that many sets · the Appearance layers system · bulk viewpoint operations · restructuring the set library |
| **Deliver & Automate** | Extract a piece of the federation · compile models into a deliverable · export IFC · the five field tools — centre of gravity, dimensions, setting out, documents and photos, site labels · saved jobs and the node graph |

Every one of these is a tool with a name, a sentence saying what it is for, and **one main action**.
A ribbon button opens its own tool, in its own window; the six that are read continuously — the
board, the change list, data, sets, appearance and the graph — open in the dock pane instead. There
is no tab strip and no workspace switcher.

Three of those are worth singling out.

**The funnel.** Every result the clash engine produced is accounted for — suppressed, collapsed,
grouped, assigned. A board that quietly shows a smaller number than the engine found is worse than
no board.

**Headroom.** A duct 1.9 m over a corridor floor clashes with nothing; there is air between them, so
no clash test at any tolerance will ever report it. One row per surface, positioned at the overlap
rather than at the middle of a forty-metre duct.

**Appearance.** A stack of named layers that can answer *why is this hidden* — the question
Navisworks loses the instant you hide something, and the one that stops a federation being handed
over. Overrides CamelWorks did not author are reported and never cleared.

## Nothing needs configuring first

Every screen derives what it needs from whatever is open — including an empty document — and says
what it derived. Setup exists to correct a guess, never to be filled in before you start. There is
no state in which a feature asks to be configured before it will run.

## Install

Download `CamelWorksSetup.exe` from the [latest release](../../releases/latest) and run it. It
installs per user into `%APPDATA%\Autodesk\ApplicationPlugins`, needs no admin rights, and registers
an Apps & Features entry. Start Navisworks and the **CamelWorks** tab appears.

For a manual install, see [dist/README.md](dist/README.md).

## How it is built

```
CamelWorks.Core        netstandard2.0, no Autodesk reference — every rule, tested on Linux
CamelWorks.Nav         net48, the only thing that talks to Navisworks
CamelWorks.Nav.Clash   net48, separate because Clash Detective is Manage-only
CamelWorks.UI          net48, the ribbon, the panes and the screens
```

The seam is the point. Because `CamelWorks.Core` has no Autodesk dependency, the clash pipeline, the
set compiler, the appearance fold, the graph runner, BCF, the report writers and the headroom check
are all proved by ~640 unit tests on a Linux CI job, with no Navisworks anywhere near them.

`CamelWorks.Nav.Clash` is a separate assembly because `Autodesk.Navisworks.Clash.dll` ships with
Manage and not with Simulate. The UI loads it by name when a clash screen opens, so a Simulate user
gets a sentence about their edition rather than a `FileNotFoundException` naming a DLL they have
never heard of.

## What CI checks on every push

| | |
|---|---|
| `CamelWorks.Core` | Compiler and unit tests |
| `.Nav`, `.Nav.Clash`, `.UI` | Compiler, once per Navisworks year |
| BCF output | `xmllint` against the published buildingSMART XSDs |
| PDF output | `qpdf --check` over real writer output |
| Ribbon | The markup, the `[Command]` attributes and the catalogue must agree |
| Bundle | The manifest, the release workflow and its validation gate must agree |
| Icons | Every command has both sizes on disk |
| XML docs | Every `<param>` and `<paramref>` names a parameter that exists |

Every Navisworks API member used was read out of the reference assemblies for all four years before
being written, rather than recalled.

**The honest limit: none of it has been run inside Navisworks from the build environment.** Compiling
against four years verifies the API surface and says nothing about behaviour.
[CAMELWORKS_BUILD_STATE.md](CAMELWORKS_BUILD_STATE.md) is the state of the code against the spec.

## Documents

| | |
|---|---|
| [CAMELWORKS_IMPLEMENTATION.md](CAMELWORKS_IMPLEMENTATION.md) | The spec, and the single source of truth |
| [CAMELWORKS_BUILD_STATE.md](CAMELWORKS_BUILD_STATE.md) | What is built against it, and what is not |
| [CAMELWORKS_PLAN.md](CAMELWORKS_PLAN.md) | The research the product was scoped from |
| [CAMELWORKS_SCOPE_DECISION.md](CAMELWORKS_SCOPE_DECISION.md) | The record of what was added, declined and cut |
| [HARVEST_PROTOCOL.md](HARVEST_PROTOCOL.md) | The gate for code brought in from other repositories |

## Related

The **BIMCamel IFC exporter** is a separate plug-in in its own repository, installed separately, and
appears on its own ribbon tab. More at [bimcamel.com](https://bimcamel.com).
