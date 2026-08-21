# Harvest protocol — moving code into CamelWorks

CamelWorks is **published source**. Two of the owner's private codebases are being harvested into it:
a production Navisworks add-in, and the BIMCamel web product. Both are owned by the project owner and
cleared for use. This document is the gate every harvested file passes through before it lands in a
repository other people can read.

Three rules, from the owner:

1. **The only company identity in the product is BIMCamel / bimcamel.com** — publisher, About box,
   `PackageContents.xml`, support links, file headers and every user-visible string. No other
   company's name, initials or product names appear anywhere in code, UI, data or filenames. That
   includes this document: the predecessor add-in is referred to by what it did, never by whose it
   was.
2. **Do not port the UI one-to-one.** Same capability, new design, new names.
3. **Harvesting from the web product must not expose anything that weakens its security.**

---

## 1. Security clearance — the web product

The web product is a **commercial SaaS**: token auth, third-party sign-in, billing, a bot check, an
admin surface and a server API. Publishing the wrong part of it is not a licensing question, it is a
live-service exposure.

### Scan result (performed before any harvest)

| Check | Result |
|---|---|
| Hardcoded secret values (`key/secret/token/password = "…"`, ≥16 chars) | **none found** |
| `.env` files in git history | one example file and one client-side file |
| Example file values | **placeholders** — every credential is a worded placeholder or empty |
| The client-side file | one variable, the public API base URL, which the bundler bakes into the client anyway |
| Private keys / certs (`*.pem`, `*.key`, `BEGIN … PRIVATE KEY`) | none |

**No live credential is exposed.** The risk is not secrets — it is publishing *logic*.

### The no-harvest zone — never copied into CamelWorks, in whole or in part

Nothing that authenticates, bills, checks entitlement, or administers. Nothing from the edge
configuration. Nothing from the client↔server contract. Nothing from the geometry service.

Publishing entitlement or licence-gating logic tells a reader exactly how to bypass paid gating.
Publishing the server contract exposes endpoint shapes and validation assumptions. Neither is needed
for anything CamelWorks does, so neither is a judgement call — they are simply out of bounds, and
this document deliberately does not name the files so that reading it teaches nobody where to look.

### The cleared zone — safe to harvest

The viewer's appearance-override state and its view-state module.

Verified clean: the override module imports exactly two things — a local types file and a GUID
helper. Zero hits across it for network calls, environment access, auth, credentials, or any API
import. It is **pure client-side state and rendering**, which is why it is the safe piece and also
the piece worth taking.

### Not a viewer — engines only

**CamelWorks is not getting an IFC viewer. Navisworks is the viewer.** Permission to harvest from the
web product is permission to take its *headless engines*, not its rendering.

| Take | Leave |
|---|---|
| the BCF 2.1 + 3.0 reader/writer, viewpoint factory, extensions | anything in the web viewer that draws |
| the conformant IDS 1.0 engine | the WebGL scene, camera rig, picking, tessellation |
| the Findings model and its CSV/JSON serialisers | React components, panels, canvas |
| **from the overrides module: the precedence LOGIC only** — the ordered later-wins-per-property fold and its tests | the painter itself; it paints a scene we do not have |

The Appearance Manager takes the *data model and the fold rule* from the overrides module and paints
through the Navisworks API. Nothing WebGL-shaped crosses the boundary — including, specifically, the
Y-up→Z-up transform, which would silently rotate every exported viewpoint 90° because Navisworks is
already Z-up.

Rule of thumb: **if it renders, it stays on the web.**

### Standing rule

Harvest **by file, never by directory**, and re-run the secret scan on the staged diff before the
commit that publishes it. A file that imports from the contract or from any server module is
disqualified — port the *design*, not the file.

---

## 2. De-branding — the predecessor add-in

### Scope

The predecessor add-in carried its author's employer's name and initials through paths, namespaces,
plug-in and ribbon ids, four package manifests, two installer manifests, the About dialog, an
`%AppData%` folder, and a marker string written into customer model files. None of it survives into
CamelWorks. The release gate in §4 is the check that it did not.

> **Ownership: decided.** The owner has confirmed twice that this code is theirs and that both
> sources may be used as they see fit. That is the answer; it is not reopened here or anywhere else
> in the plan. What remains is mechanical: publish under BIMCamel's identity, and let the gate prove
> nothing else came with it.

### No compatibility burden — the formats get designed properly

CamelWorks has **no users yet**. Nothing harvested has to stay wire-compatible with anything, and
that is worth spending deliberately rather than letting it pass unnoticed.

The predecessor's set tool had two things that would otherwise have been permanent constraints: a
marker string written into the *comment* of saved sets inside the user's NWF or NWD, and a recipe
author name beside it. That marker is a persisted data format living in customer model files, and its
`%AppData%` folder held settings, run logs and a team list. On a product with a userbase, renaming
either means carrying a dual-read path and a first-run migration forever.

**There is no userbase, so there is no dual-read and no migration.** Both are deleted from the plan.

What to do with the freedom, rather than merely enjoying it:

- **Design the recipe format for what CamelWorks needs**, not what fitted inside a set comment. A set
  comment is a single unstructured string; the recipe was squeezed into it because that was the only
  per-set storage Navisworks offers. CamelWorks has a sidecar store — so the recipe belongs there,
  with the *human-readable* formula still written to the set comment for the reason it was there
  originally: anyone opening the model can see what the set means without the tool. Keep that
  property, drop the encoding hack.
- **One namespace for persisted state.** Every harvested tool arrives with its own `%AppData%` folder
  and file layout. They collapse into the single `.camelworks/` sidecar the plan already defines,
  with one schema version across the product rather than one per tool.
- **Version every format from the first commit.** The compatibility burden that does not exist today
  starts existing on the day of first release, and a `schemaVersion` written from day one costs
  nothing now and buys the migration path later.

The same applies to anything else harvested: **change the data structures freely.** The instruction to
redesign the UI (§3) extends to the formats underneath it.

---

## 3. Redesign, don't port

The owner's instruction: same functionality, **not** the same UI and **not** the same names. The
predecessor is 16 WinForms dialogs, one per tool. CamelWorks is a ribbon plus two dock panes with
workspaces and tabs. A one-to-one port would reintroduce exactly the "sixteen unrelated dialogs"
problem the CamelWorks information architecture exists to solve.

**Harvest the engine, rewrite the surface.** The valuable part of every one of these tools is the
logic underneath — the DNF compiler, the IDS facet evaluator, the clearance sweep, the override
resolution order. The forms are the disposable half.

| What it did | Harvest | CamelWorks home | New name |
|---|---|---|---|
| boolean set authoring (node tree, DNF compiler, search emitter, evaluator, formula) | **engine** | Sets & Views workspace, new *Build* tab | **Set Builder** — the algebra. *Set Library* stays the store/reuse tab |
| moving overrides between models | concept only | folds into the new **Appearance Manager** | — |
| IDS parsing and checking | **engine** | Project workspace, *Health* | **IDS Check** — *IDS is a buildingSMART standard, so the name stays; it is not branding* |
| clearance sweeps | **engine** | Coordinate workspace | **Clearance** |
| clash matrix builder and its spreadsheet import | **engine** | existing *Tests* tab | **Clash Tests** (already named) |
| sphere export and overlay for clash points | **engine** | Coordinate | **Clash Markers** |
| clash renaming, search-set making | **engine** | fold into *Rules* / *Set Builder* | — no standalone tool |
| clash XML export/import and its schema | **engine** | *Report* / *BCF* | — |
| property writing, data linking, manual parameter insert | **engine** | Data workspace | **Data Manager** (already named) |
| WBS writing | **engine** | Data/*Takeoff* | reopens the BOQ cut — panel decides |
| batch running, model jobs, run logging, host location | **engine** | Batch workspace | reopens the automation-host cut — panel decides |
| viewpoint creation | **engine** | Sets & Views/*Viewpoints* | **Viewpoints** (already named) |
| all 16 forms | **discard** | — | rebuilt as pane tabs against the shared widget vocabulary |

**Naming rule going forward:** a CamelWorks tool is named for what the user is trying to do, not for
what it is made of, and never after a company. No abbreviations in ribbon labels.

---

## 4. Release gate

Add to §8 Definition of done:

- A case-insensitive grep for any predecessor company name, its initials, or its product names over
  the whole published tree returns **zero** hits, including `PackageContents.xml`, installer
  manifests, resource files, planning documents and the About dialog — and the company identity in
  each of those places is **BIMCamel / bimcamel.com**.
- No harvested file brings a NuGet dependency with it. (The spreadsheet and imaging libraries the
  predecessor used are noncommercial-licensed and would put commercial users in violation of a
  product advertised as free to use; the no-third-party-runtime rule already forbids them.)
- No harvested file renders. Nothing from the web viewer that draws is present.
- The staged diff of every harvested file has passed the secret scan in §1.
- No harvested file imports from the contract, the edge configuration, the server API or any auth or
  billing module.
- Every persisted format carries a `schemaVersion` from its first commit.
- No harvested tool writes outside the single `.camelworks/` sidecar.
