# Contributing

CamelWorks is under development and contributions are welcome — code, but especially the two
things this project cannot get for itself.

## The most useful thing you can do

**Run it inside a real Navisworks, on a real federation, and say what happened.**

Every host adapter and every screen compiles against Navisworks 2024, 2025, 2026 and 2027, and the
engines behind them are covered by unit tests on a machine with no Navisworks on it at all. What
none of that proves is behaviour: no screen in this product has been driven against real geometry
from the build environment. That is the honest limit, it is written down in
[CAMELWORKS_BUILD_STATE.md](CAMELWORKS_BUILD_STATE.md), and the only way it closes is somebody with
a licence and a model.

So a bug report from a live federation is worth more here than a patch.

### A good report

[Open an issue](../../issues) with:

- **Navisworks year and edition** — 2024–2027, Manage or Simulate.
- **Which app and tab**, and what you pressed.
- **What you expected, and what happened.** A screenshot of the screen saying the wrong thing is
  usually enough.
- **The size of the model**, roughly — a lot of what breaks only breaks at half a million elements.
- Anything from **Help ▸ diagnostics**, if the window still opens.

If Navisworks crashed, say what you were doing at the moment it went. A crash in a plug-in is
nearly always the host being touched from the wrong place at the wrong time, and *what you clicked
while it was thinking* is the detail that finds it.

## The second most useful thing

**Tell us where the words are wrong.** Every screen in this product is supposed to say what it did,
what it derived and why — in words a coordinator would use rather than a programmer's. A screen
that reads as nonsense on a real project is a bug, and it is reported the same way.

The same goes for the [wiki](../../wiki): if a page describes something that does not match what you
see, that is worth an issue.

## Code

Read [CAMELWORKS_DESIGN.md](CAMELWORKS_DESIGN.md) first — it is the law the shell implements, and a
pull request that ignores it will be asked to change. The short version:

- **Two anatomies.** A tool that is configured and run is `Bench.Surface`; a tab somebody works in
  for an afternoon is `Bench.Split` + `Bench.Pane`. §3b lists the nine faults every tab is held to.
- **No property name is ever a bare text box.** Use the model-fed pickers.
- **Every table gets `Ui.Picking`** — verbs as symbols under the rows they act on, and the same
  verbs on right-click.
- **Options set once for a job go in `Ui.Configure`**, behind a symbol on the action row, not
  laid out beside the work.
- **Destructive writes are two-step**: a diff, then a commit that names its consequence.
- **Never pump the message loop inside a host enumeration.** This is not style — it is what a
  reproducible Navisworks crash turned out to be.

### Before you open a pull request

```bash
dotnet test CamelWorks.Core.Tests -c Release
for f in checks/validate-*.py; do python3 "$f"; done
python3 tools/icons.py --check
```

Some of the checks are **ratchets** — `checks/tables-without-picking.txt` records how many tables
have no picking bar, and the number may fall but never rise.

CI compiles `.Nav`, `.Nav.Clash` and `.UI` once per Navisworks year. Please keep pushes tight: this
project runs on a small Actions budget, so a validated push beats three speculative ones.

`docs/apps/` carries a product audit per app — the job it does, what was wrong, what changed, and
what was judged out of scope. If you change a tab, that is where the reasoning goes.

## What the licence means for a contribution

CamelWorks is **Apache 2.0 with the Commons Clause**: free to use, including commercially, and not
free to sell. It is source-available, not open source. Contributing means your contribution goes out
under those same terms. [LICENSE](LICENSE) carries a plain-words summary alongside the text.

## What is deliberately not here

Before proposing a feature, check [What it cannot do](../../wiki/What-it-cannot-do) on the wiki and
the "out of scope, named" sections in `docs/apps/`. Several obvious-looking gaps — a scheduler, a
context-menu entry, re-pointing a moved model — are limits of the Navisworks API rather than things
nobody got round to, and each one is written down with the reason.

If you think one of those is wrong, that is a good issue to open. They were decided from reading the
reference assemblies, and reading can be wrong.
