# Export Kit — product audit

The model as a file somebody else opens. Tabs: IFC · GLB · Drawing · Extract · Compile.
Walked as the coordinator on a Thursday afternoon, with Friday's deliverables list on the desk:
a GLB for the client, a DXF for the structural engineer, the weekly properties sheet for the
QS, and the compiled federation for the CDE.

## The app as a whole

**Wrong.** The three format tabs shared a scope picker in name only: each built its own, so a
scope chosen on GLB was re-chosen on Drawing and again on Export Data. For the person doing
three exports of the same selection in a row — the ordinary Friday — that is the same question
three times, and the third answer is the one given wrong. The app also opened cold: no strip
saying whether anything had been exported this week or what is saved to re-run.

**Changed.**
- The scope is now genuinely shared: one static behind all three format tabs, so "Current
  selection" chosen on GLB is already chosen when Drawing opens. Session-lived on purpose —
  what I am exporting this minute is a fact about the session, not the project.
- A status strip (in `Surfaces.Status`, case `exports`): when anything last exported (from the
  activity log, "never" in warning tone), how many extract rules and compilations are saved,
  each chip landing on the tab that owns it.

## IFC

**The job.** The client's information manager wants IFC; the coordinator opens this tab to get
the exporter with the agreed mapping.

**Found.** The tab is deliberately a door to the BIMCamel exporter pane, so the two settings
forms can never disagree, and it says clearly what to do when the plug-in is not installed.
That is the right shape; duplicating the mapping form here would be the mistake.

**Changed.** Nothing.

## GLB

**The job.** Send the client a model they can open on a phone with nothing installed. Weekly,
usually of the current selection or a level.

**Found.** The idle statement already says honestly what a GLB is and is not (no properties —
"if somebody is going to ask for a property, they want IFC"). Silent-geometry and ceiling
counts are surfaced rather than dropped. Colour grouping and path suggestion are derived. The
only friction was the re-chosen scope, fixed above.

**Changed.** Shared scope only.

## Drawing

**The job.** The structural engineer wants the forty selected pipes in AutoCAD, today, with no
recipe set up first.

**Found.** Same honest shape as GLB: R12 ASCII stated, layers derived, name/model carried as
XDATA with the reason more is refused. The doc comment already draws the line against Extract
correctly. Scope re-choice was the pain; fixed above.

**Changed.** Shared scope only.

## Extract

**The job.** "Level 03 mechanical for the fit-out contractor" — a piece chosen by a rule that
re-runs next revision, not a selection made by eye.

**Found.** Already the strongest tab: the wizard order matches the real dependency, every
choosing axis is fed from the model (levels, models, property values with sensible-cardinality
filtering), the folder is remembered per project via `ToolMemory`, the per-model table is
pickable, and the missing-NWD limitation is stated with the honest workaround ("Make it a
set", then Navisworks' own export).

**Changed.** Nothing. Considered and left: a picker of Navisworks selection sets as an axis —
the file itself documents why that runs the wrong way round.

## Compile

**The job.** Friday's federation out of the delivery folder: read the drops, let newest
revision win, say what is going in and what is left out, then take the session over — with the
cost stated and ticked for.

**Found.** The delivery folder and register path were already remembered per project
(`ToolMemory` "publish.compile", and the folder saved on the recipe), which is the hint's ask,
already met. The plan's left-out table is pickable — it is the email to the supplier. The
destructive take-over keeps its two-step.

**Changed.** Nothing.

## Export Data (the "sheet" surface, on Quantify's Data Export tab and reached from here)

**The job.** The QS's weekly sheet: the same five columns, every Friday, one row per element.

**Wrong.**
- The property tab and the column list were typed from memory, spelling and all — the exact
  alt-tab-to-the-host round trip `Pickers` exists to kill.
- Nothing was remembered: Friday's five columns were retyped every Friday.

**Changed.**
- The property tab is a `Pickers.Tab` fed from the model.
- A `Pickers.Property` combo under the column list: a pick from the model's vocabulary joins
  the comma list (deduplicated); the list itself stays a typable box for names the bounded
  scan did not reach.
- The tab and columns are remembered per project (`ToolMemory` "publish.sheet"), written on a
  run that produced a file — half-typed drafts are not worth keeping.

## Judged missing, out of scope

- **Writing part of a document to NWD** — needs an API Navisworks does not offer; the honest
  workaround is already on the Extract surface.
- **Sending the GLB anywhere** (upload, link) — needs a delivery engine that does not exist;
  the file lands in a folder and that is the product's edge.
