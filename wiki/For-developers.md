# For developers

## The shape of it

```
CamelWorks.Core        netstandard2.0, no Autodesk reference — every rule, tested on Linux
CamelWorks.Nav         net48, the only thing that talks to Navisworks
CamelWorks.Nav.Clash   net48, separate because Clash Detective is Manage-only
CamelWorks.UI          net48, the ribbon, the windows and the screens
CamelWorks.Studio      the screenshot harness — draws every surface with fake data
```

**The seam is the point.** Because `CamelWorks.Core` has no Autodesk dependency, the clash pipeline,
the set compiler, the appearance fold, the graph runner, BCF, the report writers and the clearance
check are all proved by unit tests on a Linux CI job, with no Navisworks anywhere near them.

`CamelWorks.Nav.Clash` is a separate assembly because `Autodesk.Navisworks.Clash.dll` ships with
Manage and not Simulate. The UI loads it **by name** when a clash screen opens, so a Simulate user
gets a sentence about their edition rather than a `FileNotFoundException` naming a DLL they have
never heard of.

## Building

The Navisworks API is restored from NuGet, so **no Navisworks install is needed to build**.

```bash
dotnet build CamelWorks.UI/CamelWorks.UI.csproj             -c Release -p:NavisworksYear=2025
dotnet build CamelWorks.Nav.Clash/CamelWorks.Nav.Clash.csproj -c Release -p:NavisworksYear=2025
dotnet test  CamelWorks.Core.Tests -c Release
```

Years: 2024 (API v21), 2025 (v22), 2026 (v23), 2027 (v24). `dist/README.md` has the manual bundle
layout — which file goes in which year folder.

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

The repo also carries ~28 validators under `checks/`, runnable without a build:

```bash
for f in checks/validate-*.py; do python3 "$f"; done
python3 tools/icons.py --check
```

Some are **ratchets** — `checks/tables-without-picking.txt` records how many tables have no picking
bar per file, and the numbers may fall but never rise. It is currently at zero.

## The design law

`CAMELWORKS_DESIGN.md` is the law the shell implements, and `CAMELWORKS_APPS.md` is the checklist
every tab is audited against. Two anatomies:

- **A surface** — `Bench.Surface(setup, foot, stage)` — for a tool that is configured and run.
- **A bench** — `Bench.Split` + `Bench.Pane` — for a tab somebody works in for an afternoon.

Section §3b of the design record carries the nine faults every tab was held to in the pass that
produced the current UI: no unactionable tables, no dead ends, no rail bloat, search at the top of
long lists, verbs on the rows, symbols not sentences, bulk and export everywhere, no two tabs doing
one job, and a hand-off after every successful run.

Per-app audits — the job story, what was wrong, what changed, and what was judged out of scope —
are in `docs/apps/`. `docs/CAPABILITY_MAP.md` maps every engine in Core to the surface that reaches
it, so a capability cannot go missing without leaving a hole.

## Conventions worth knowing before you write UI

- **No property name is ever a bare text box.** Use `Pickers.Property` / `Pickers.Tab` /
  `Pickers.Set`, fed from the model, still editable.
- **Every table gets `Ui.Picking`**, which draws the verbs as symbols, keeps the tally, and hangs
  the same verbs on right-click.
- **Set-once options go in `Ui.Configure`**, behind a symbol on the action row.
- **Never pump the message loop inside a host enumeration.** `NavDocument` counts open walks and
  the progress pump refuses while one is open — this is what the compare crash turned out to be.
- **Destructive writes are two-step**: a diff, then a commit button that names the consequence.

## The honest limit

None of it has been run inside Navisworks from the build environment. Compiling against four years
verifies the API surface and says nothing about behaviour. `CAMELWORKS_BUILD_STATE.md` is the state
of the code against the spec, including everything known to be unverified.

## Contributing

The most useful contribution is not a patch. Every host adapter compiles against four Navisworks
years and the engines are unit-tested, but **no screen has been driven against real geometry from
the build environment** — so a bug report from a live federation is worth more here than code.

[CONTRIBUTING.md](https://github.com/mrshoma99-rgb/Camelworks-navisworks-plugin/blob/main/CONTRIBUTING.md)
says what makes a good report, and what to run before opening a pull request.

## Licence

Apache 2.0 with the **Commons Clause**: free to use, including commercially; not free to sell.
Source-available, not open source. `LICENSE` carries a plain-words summary alongside the terms.
