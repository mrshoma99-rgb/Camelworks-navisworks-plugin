# CamelWorks for Autodesk Navisworks

Coordination, data and delivery tools for **Navisworks 2024–2027**, Manage and Simulate.

**Free to use, including commercially. Not free to sell.** Apache 2.0 with the Commons Clause —
source-available, not open source. See [LICENSE](LICENSE), which carries a plain-words summary
alongside the terms.

---

## What it does

| Workspace | |
|---|---|
| **Home** | The weekly cycle as the front door — reconcile, regroup, report — with when each last happened and the way to each |
| **Project** | Health checks · what CamelWorks derived and why · the federation and whether each source has moved on since the file was saved · the IFC exporter |
| **Coordinate** | The clash board · headroom · the engine's tests · the rule editor · reports · BCF in and out |
| **Data** | Every property folded to one row, and a write · levels derived and stamped · takeoff |
| **Sets & Views** | Boolean set builder · the Appearance layers system · bulk viewpoint operations |
| **Batch / Automate** | Saved jobs · a node canvas over the same services the buttons drive |

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
