# CamelWorks — handover

Written 2026-08-26, against `main` at `ad67b21`. For whoever picks this up next.

This is not a status report. It is the set of things that would otherwise cost you a day to
discover, plus an honest account of what is not done and where the previous work went wrong.

---

## 1. Read this first: you cannot see the UI, and that is the central problem

CamelWorks is a Navisworks dock-pane plug-in. The build machine has no Navisworks and no Windows,
so **nothing in `CamelWorks.UI` has ever been run by the people who wrote it.** CI proves it
compiles against four Navisworks reference-assembly sets. It proves nothing about how any of it
looks or behaves.

There are two partial ways to see something, and both are weaker than they appear:

**The `ui-shots` branch.** An orphan branch, force-pushed by `.github/workflows/ui-shots.yml`,
holding a PNG of every harness sheet in light and dark. Fetch it:

```
git fetch origin ui-shots && git checkout ui-shots   # shots/*.png, plus shots/_log.txt
```

Run it on demand from the Actions tab (`workflow_dispatch`), or it fires on pushes to `main` and
`claude/**` that touch `CamelWorks.UI/**` or `CamelWorks.Studio/**`.

**`CamelWorks.Studio`**, a WPF harness that renders panels with no Autodesk assembly present:

```
dotnet run --project CamelWorks.Studio                       # look at them
dotnet run --project CamelWorks.Studio -- --shoot out\shots  # write PNGs
```

### Why both are weaker than they look

1. **The harness is a reconstruction, not the product.** `CamelWorks.Studio/Sheets.cs` hand-builds
   its own version of each panel. `checks/validate-harness.py` holds its *button captions* to the
   real ones — nothing else. Layout, figures, ordering and structure can drift freely. As of
   `ad67b21` the rendered clash board still shows the old four-figure row (`new / came back / gone
   / unassigned`) even though the shipping code was changed that day to show a different row and a
   reconciling sentence. **The pictures can be stale and still pass every check.**

2. **24 of the 51 tools have no sheet at all.** Measured, not estimated:

   > Health Check, History, Fix Broken Links, Clash Board, Clearance, Clash Report, BCF, Merge
   > Markups, Penetrations, Changes, Excel Round-trip, Levels and Zones, Work Breakdown, Generate a
   > Set Library, Organise Sets, Section Box, Extract, Dimensions, Setting Out, Documents and
   > Photos, Batch, Export GLB, Export Data, Export Drawing

   Everything built on 2026-08-26 is in that list.

3. **The shell itself does not render.** `shell-*.png` fails every run — `ShellView` reaches
   `Host.Current`, which needs `Autodesk.Navisworks.Api`. See `shots/_log.txt`. So the chrome
   around every panel — rail, header, purpose bar, navigation — has never been seen either.

**If you do one thing before writing code, do this one:** make the harness render the real screens
rather than reconstructions, or at minimum add sheets for the 24 missing tools. Until then every
UI claim in this repository, including the ones in commit messages, is unverified.

---

## 2. Environment

| Fact | Consequence |
|---|---|
| No .NET SDK on the dev box; `builds.dotnet.microsoft.com` is blocked by network policy | **CI is the only compiler.** You cannot build or run tests locally. |
| `.github/workflows/build.yml` has `concurrency: cancel-in-progress` on the ref | Every push kills the run behind it. **Batch work, push once, wait.** |
| `cancel_workflow_run` / `rerun_workflow_run` return 403 for this integration | Only a real push creates a run. Never push an empty commit to kick CI. |
| `ui-shots.yml` is a Windows runner (billed 2x) | It is `workflow_dispatch` + path-filtered on purpose. Do not make it run on every push. |
| The user has said they are short on Actions minutes | Batch aggressively. One validated push beats three speculative ones. |

`checks/` holds ~31 Python scripts that run without a compiler and catch a real slice of what the
compiler would. **Run all of them before every push.** They are the only feedback loop shorter
than a CI cycle:

```
for f in checks/validate-*.py; do python3 "$f" || echo "FAILED $f"; done
python3 tools/icons.py --check
```

Three of them exist because something got through: `validate-engines-reached.py` (an engine whose
only callers are its own tests — a doc comment mentioning the class defeated the first version, so
it strips comments now), `validate-braceless-bodies.py` (a declaration as the body of a braceless
`if` — CS1023, cost a full CI cycle), and `validate-shadowed-locals.py` (CS0136, caught a real one
the same day). They will not catch everything the compiler does. Nothing does, here.

---

## 3. Shape of the code

```
CamelWorks.Core         netstandard2.0   135 files, ~46k lines   no Autodesk reference at all
CamelWorks.Core.Tests   net8.0            95 files, ~27k lines   1,975 tests, run on Linux in CI
CamelWorks.Nav          net48             11 files               the Navisworks adapter
CamelWorks.Nav.Clash    net48              1 file                clash engine, loaded reflectively
CamelWorks.UI           net48             45 files, ~31k lines   dock pane, ribbon, every screen
CamelWorks.Studio       net48              5 files               the render harness (see §1)
```

**The seam is the point.** `CamelWorks.Core` holds every engine and knows nothing about
Navisworks, which is why 1,975 tests can run on a Linux box. `CamelWorks.Nav` implements the
interfaces in `CamelWorks.Core/Abstractions/` against the real API. Keep engines in Core; keep
host contact in Nav. `checks/validate-seam.py` enforces that every interface implementer is
complete.

`CamelWorks.Nav.Clash` is separate and loaded by reflection because
`Autodesk.Navisworks.Clash.dll` ships with Manage and not with Simulate — a hard reference would
make the plug-in fail to load on a Simulate seat.

`CamelWorks.Nav.csproj` defines `NAV$(NavisworksYear)`. That is the project's **only** `#if`, and
it should stay that way. The clash-plane API differs by year (2024 uses `Interop.LcOaClipPlaneSet`
methods; 2025/2026/2027 use `Api.ClipPlaneSet` properties) and `NavViewSession.cs` handles it by
never naming the type — only member style sits behind the `#if`. Note 2025 groups with 2027, not
with 2024, which is the opposite of the natural guess.

**UI conventions.** `CamelWorks.UI/Views/Ui.cs` is a widget vocabulary — every screen is built
from it, and `checks/validate-ui-calls.py` holds every call site to a declared signature.
`CamelWorks.UI/Shell/Tools.cs` is the tool registry (51 tools); `Shell/CommandCatalogue.cs` maps
ribbon commands to tools (39 commands); `CamelWorks.xaml` is **hand-authored AdWindows ribbon
markup that nothing compiles** — `checks/validate-ribbon.py` is the only thing standing between a
typo there and a broken tab.

`TreatWarningsAsErrors` is on, `Nullable` is enabled, C# 10, and no `record`/`init`.

---

## 4. Standing constraints from the user

Preserve these. They are not negotiable and several were repeated more than once.

- **No mention of KVI or Kanadevia anywhere**, in code or UI. The only company named is
  **bimcamel.com**. `checks/validate-branding.py` enforces it. There is a `docs/HARVEST_PROTOCOL.md`
  about harvesting from the bimcamel repo without leaking anything, including security issues.
- **Do not mirror another product's UI or reuse its tool names.**
- **Zero setup.** Verbatim: *"i don't want it that a person needs to spend 5 hours settings up
  templates and settings before even beginning to use the plug in, every feature should be useable
  even without a template always usable."* Every screen must work on a raw model with nothing
  configured.
- **No feature scoping under cover of a redesign.** Verbatim: *"i do not want to lose any features,
  this is a UX/UI redesign not a feature scoping."*
- **No phases.** Ship a complete product.
- Licence is Apache-2.0 + Commons Clause — free to use, not to resell.
- There are no users yet, so the shape is still open.

---

## 5. What is actually built

`docs/CAPABILITY_MAP.md` is the inventory: **214 public documented Core engines, each mapped to a
named surface.** `checks/validate-capability-map.py` fails the build if an engine has no row. It
was written after a redesign silently dropped 16 features, and it exists because `Tools.cs` is a
list of *tools*, not of *capabilities* — several shipped features were sub-panels with no tool
entry, so a redesign that faithfully rebuilt every tool still lost them. **Start there, not here.**

Other docs, in rough order of usefulness: `docs/CAMELWORKS_PANEL_GRAMMAR.md` (the layout rules),
`docs/CAMELWORKS_BUILD_STATE.md`, `docs/CAMELWORKS_IMPLEMENTATION.md`, `docs/CAMELWORKS_PLAN.md`.
Treat the last three as historical — they have drifted.

### Landed 2026-08-26 (all CI-green, none seen by a human)

| Commit | What |
|---|---|
| `c97a171` | Export split into three windows: Export GLB, Export Data, Export Drawing |
| `1cf9360` | Project lease surfaced — the strip on every window and both panes, `Save` refuses while another holds it, refusal names the changed sections |
| `0f220da` | `SetFamily` wired into Set Library; `validate-engines-reached.py` |
| `172a420`, `3fa91a6`, `d68ef71`, `4ae1f21` | Table picking bars: 45 bare tables → 10 |
| `e098328` | Build fix + `validate-braceless-bodies.py` |
| `e54b8a5` | Write-preview invalidation (blocker 5) |
| `bb9da45` | Board figures reconcile; `ClashDelta.OnBoard` (blocker 2) |
| `56fdc19` | Board filter state + count staleness (blockers 4, 6) |
| `3756320`, `0a3a49c` | Issues hold clashes (blocker 1) |
| `76a3792` | Responsive panes (blockers 3, 7) |

---

## 6. What is NOT done

### 6.1 The redesign the user asked for

This is the biggest gap and the reason this handover exists. The user asked, verbatim:

> *"show me detailed groups of the ribbons, with their correct icons, what each button opens, i
> want to see the dialouges, the panels, the wizards myself before coding"*

That was partly answered once with an HTML prototype, and then **not maintained**. What followed
was a large amount of internal work — correctness, plumbing, checks, feature-gap closure — none of
which the user could see. Two specific failures:

- **The design spec and both critiques were lost in a container reset and never regenerated.** The
  critique loop that produced the seven blockers was never closed on paper; the blockers were
  eventually answered by the user directly, from a summary rather than from the documents.
- **The harness was never extended to the new windows** (§1), so the redesign was built and
  shipped without a single screen of it being looked at.

If you do nothing else: **close the loop between what is written and what can be seen.**

### 6.2 Seven design blockers — answered and implemented, unverified

The user's answers, and where the work landed. All of it is reasoned and tested, none of it has
met a real model:

1. **What makes an issue** — manual only; clashes can be attached to one. `ManualIssue.Attached`,
   `ManualIssueList.Attach/Detach/Holding`, one clash to at most one issue.
2. **Carry-over arithmetic** — the four figures counted three different populations, three of them
   pre-suppression. `ClashDelta.OnBoard` restricts by identity; the row is one population now.
3. **/ 7. Pane sizing** — WPF is already DPI-independent; the pane was never handled. Table height
   binds to the ancestor `ScrollViewer`; column widths became proportions with a floor. **29 of 53
   tables declared more fixed width than the pane's own 470 minimum.**
4. **Live counts** — kept, and they now say when the board has been edited since they were counted
   (`CoordinateViews.BoardEdits`).
5. **Write-preview invalidation** — was real: `Apply` read the value box live while the button's
   count came from the preview, so editing the value after previewing wrote something never shown,
   with no undo. Any input edit now drops the preview.
6. **Board filter** — the figures *are* the filter; one state, stated once, with a way out.

### 6.3 Known-open, smaller

- **Ten tables deliberately have no picking bar** (`checks/tables-without-picking.txt`): four in
  `CoordinateViews`, two in `SetsViews`, one each in `BatchViews`, `CompareViews`,
  `ReportBuilderWindow`, `SiteWindow`. Each drives something on pick — a detail pane, a load, a
  reveal, a delete — so `Ui.Picking` would turn them multi-select and change what a click means on
  the board. **This is a decision for the user, not a number to grind down.**
- **`checks/folds.txt` sits at 33.** Only two files genuinely broke the one-fold-per-surface law
  and both were fixed; the rest of the count is separate screens sharing a file. The ratchet is a
  proxy and it over-reports — do not chase it blindly.
- **v0.3.0 shipped with a dock-pane crash** that closed six tools. It is fixed in `main` but
  **`main` has not been released.** Cutting that release is probably the highest-value single act
  available.

---

## 7. Where the previous work went wrong

Worth knowing so you do not repeat it.

- **Verification theatre.** A great deal of effort went into checks, ratchets and commit prose.
  Those are real and they caught real bugs. But they measure the code against itself. None of them
  can tell you whether a screen is usable, and the volume of green ticks made it easy to keep
  going without noticing that the user had not seen anything.
- **A truncated `grep` produced a false claim.** `DxfWriter` was reported as having no caller; it
  had one in `ExtractWindow` all along, hidden because the test file's matches filled a `head -20`.
  The claim reached a commit message before it was corrected. **Check the tail of your greps.**
- **An edit script asserted its way to a stop before writing the file**, so a serialiser shipped
  with a write side and no read side. A test caught it, but a sibling test had been passing
  vacuously for the same reason. **Re-read the file after scripted edits; do not trust the exit
  code.**
- **Scope drift.** The user asked for a UX/UI redesign. Much of what was delivered was
  correctness work that they had not asked for and could not see. Some of it was genuinely
  valuable — the write-preview bug would have destroyed data — but it was not the assignment.

---

## 8. If you are starting now

1. **Read `docs/CAPABILITY_MAP.md`.** It is the only trustworthy inventory.
2. **Fetch the `ui-shots` branch and look at what exists.** Then read §1 again so you know how much
   of what you are looking at is real.
3. **Ask the user what the redesign is meant to look like before building more of it.** They have
   asked twice to see things before coding and have twice not been shown. Do not repeat that.
4. **Fix the harness before trusting any UI change**, or accept explicitly that you are shipping
   blind and say so every time.
5. Run the full `checks/` sweep before every push, batch your pushes, and wait for CI.

The engines are in good shape and heavily tested. The plumbing is sound. What is missing is any
evidence that the thing is nice to use — and that was the job.
