# Scene Kit — product pass

The client's checklist, walked tab by tab as the manager preparing the Tuesday walkthrough:
what does the model look like, why, and where are the viewpoints and images the meeting runs
from.

## Appearance (`appearance`)

**The job.** Monday afternoon: make the federation *legible* for tomorrow — services by system,
structure greyed back, the finished zones hidden — and be able to say, when someone asks why
half of level 4 is invisible, exactly which layer decided that and take it back off.

**What was wrong.**

- The "Covers" input for a new layer offered the current selection, everything, and the
  *project library's* saved rules — but not the document's own Navisworks sets, which is where
  a coordinator's sets mostly live (including every set this suite's other tabs publish). The
  pain path was: publish a set in the Builder, come here, find it not offered, select by hand.
- On landing with no layers yet, the screen said "No layers yet — add one" and nothing else.
  But the first question on landing is not "what do I add", it is "what am I looking at" — a
  federation is nearly always already coloured or part-hidden by somebody, and the foreign-state
  scan (this tab's best answer to that question, over the whole federation) sat below as just
  another group with nothing pointing at it.

**What changed.**

- "Covers" now also lists the document's selection and search sets ("Navisworks set: ..."),
  read the same way `Pickers.Set` reads them, deduplicated against the project library and
  wrapped in try/catch so a host that cannot list its sets degrades to the old choice. A host
  set becomes a *rule* target (`SetExpression.InSet`), so the layer follows the set: republish
  the set with more elements and the layer covers them too.
- The empty-stack landing text now states the situation honestly — what you see is the host's
  state, not CamelWorks' — and points at "Scan the model" as the way to find out exactly what
  that state is and who put it there. (The scan itself, with its select / adopt / put-back
  verbs, already did the job; it was a discoverability problem, not a capability one.)

Deliberately not changed: the colour input stays `#rrggbb` text rather than growing a picker
dialog — it is one field, the swatch in the stack shows the result immediately, and the
anatomy has no room for a popup colour wheel. Judged tolerable; named here in case the client
disagrees.

## Viewpoints (`viewpoints`)

**The job.** Tuesday morning: forty views for the walkthrough — one per issue on the board,
named and numbered so the list reads as an agenda, filed into a folder, rendered to PNGs for
the pack that goes out before the meeting.

**What was wrong.**

- Render-to-images opened with a blank "Write into" box. The fallback (a folder in Documents)
  only revealed itself after the run — a decision the tool was taking silently that it could
  state up front. Export Kit already solved this with a suggested path beside the document.
- The walkthrough generator ("A viewpoint per issue") needs the board from Clash Manager, and
  when there was none it said "Load the board in Triage first" — an instruction to go and find
  another app, with no way there.

**What changed.**

- The render folder is pre-filled with a path beside the saved document (`<model folder>/<project>
  viewpoints`), falling back to Documents — the `ExportWindow.Suggested` pattern, reimplemented
  locally as `SuggestedRenderFolder` since that member is private to Export Kit and this brief
  does not touch it. The blank-box fallback behaviour is kept, so clearing the field still works.
- The missing-board note now carries a button that opens Clash Manager on the Board tab.

Everything else on this tab already answered the checklist — the tick-driven verbs, the
generate-per-element two-step, the delete that refuses to fall back to "all of them", the
redlines warning on copy-overrides — and was left alone.

## The app's status strip

Scene Kit had no `Status` provider. It now shows (cheap reads only — the store and the host's
viewpoint register, never a traversal): the saved layer count (jumps to Appearance; when zero
the tooltip says the honest thing — whatever the model looks like, CamelWorks did not decide
it, and the scan says who did) and the document's viewpoint count (jumps to Viewpoints). These
are the two numbers the Tuesday-walkthrough manager checks before opening either tab.

**Out of scope, named.** A "the model may be showing foreign overrides" figure on the strip
would be the ideal landing answer, but knowing it requires reading every element's appearance —
a full traversal, exactly what the strip's contract forbids. It stays a one-press scan on the
Appearance tab.
