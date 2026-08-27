# Model Audit — product audit

**The app's job.** The manager's question before anybody coordinates: is this federation fit to
work on? A fresh drop of models lands Monday morning; Health sweeps it, Models registers which
revisions arrived, Broken Links repairs what the federation can no longer find, IDS holds the
job to the client's (and its own) written requirements, and Project keeps what CamelWorks
worked out. The persona is the BIM manager between the drop and the first clash run.

**The seam this pass closed:** Health and Models both *talk about* missing sources — a fault in
the sweep, a red row in the register — and the status strip counts them, but neither surface
pointed at Broken Links, the one tab that can actually bring the found file in. Both do now.

## Health

- *The job:* one button, one verdict, worst first, on the whole drop.
- *Was wrong:* the "N models has no file on disk" fault carried an inline folder search that
  only *reports* where files went (deliberately — it does not rewrite the federation), and then
  stopped. The fix lives one tab over. The finding now hands off to Broken Links, which can
  append the found file, right under the search.
- *Was wrong:* the code-check property was a bare text box — the classic type-a-property-name-
  from-memory input, and the reason the check reports "nothing carries it" all morning. It is
  now the model-fed property picker, still editable, still accepting "Category / Name".
- *Was wrong (IDS group):* "IFC class from" was also bare text, even though the run's own
  caveat lists candidate properties to put in it. It is now the property picker, so the box
  offers what the caveat names.
- *Was wrong (IDS group):* a check that failed dead-ended at the workbook. The suite has a pen
  — Data Manager's Write tab — and the results now offer the jump, but only when at least one
  failure is a property failure: an entity, classification or aggregation failure is the
  exporter's to fix, and the hand-off says honestly that Write fixes the federation here while
  the authoring model still needs the same fix (which is what the workbook is for).
- *Held:* one traversal for all checks, the verdict line, findings with their fix and their
  show-me selection, the put-it-back placement restore, the manifest-since-last-sweep register,
  ToolMemory on the IDS path.

## IDS

- *The job:* write the job's own requirements as the open format, in a sitting; save it, keep
  the draft in the project as IDS itself.
- *Was wrong:* the Property rows asked for a property set and a property name as bare text —
  exactly the names the model already carries on the compliant elements, and a typo here fails
  a model that actually complies. Both boxes are now model-fed pickers (set names as tabs,
  property names), still editable because a requirement the federation does not yet meet is a
  legitimate thing to write down.
- *Was wrong:* saving the .ids ended the workflow, when the file's whole point is being run.
  The save now offers "Check the federation against it", which lands on Health with its IDS box
  waiting for the path just written.
- *Held:* the live verdict under the form, lossy-import visibility, the draft-as-IDS store, the
  writer's refusal reasons surfacing before the save dialog.

## Models

- *The job:* the register — which revision of what is loaded, and what moved since the last
  manifest. The Monday answer to "which files is this board against?".
- *Was wrong:* the tab described the federation while staying silent about sources the
  federation cannot find — the register of a model nobody is actually building against. The
  landing now states the count of missing sources (the same cheap File.Exists read the strip
  makes) with the jump to Broken Links.
- *Held:* the one-pass element counts, manifest-vs-manifest register, pickable rows for the
  inbox, the recorded-against-this-one contract.

## Broken Links

- *The job:* find where the missing source files went, bring the right one in, and say plainly
  what the Navisworks API will not let a plug-in finish (no repoint, no single-model remove).
- *Was wrong:* the "Look in" folder started empty, though the likeliest answer is derivable —
  a republished file usually lands beside the healthy models. The box is now prefilled from the
  first model whose source exists, stated as a prefill, still the user's to change.
- *Was wrong:* after a successful append the tab stopped one step short of the question that
  sent the user here — is the federation fit now? "Check the federation" now jumps to Health.
- *Held:* the append-alongside honesty, the unknown-is-not-missing share handling, the
  everything-reads-what-is-loaded warning.

## Project

- *The job:* the derived register with an override on every line, plus parties, code tables and
  the roster — all skippable by design.
- Nothing changed this pass. Its inputs are the project's own (names, tables pasted from a
  client spreadsheet), not names the model knows better, and each save already states which
  screens read it.

## Out of scope, named

- Refreshing a stale model (source newer than the save) from inside the app — Navisworks
  exposes no per-model refresh to plug-ins; the sweep's wording stays the honest limit.
- A scheduled Monday sweep belongs to Automate's Jobs, not to a checkbox here.

**Checks:** UiCheck build clean; all thirteen validators pass.
