# Quantify — product audit

**The app's job.** Numbers out of the model the way a bill wants them, without asking anybody
to map anything first. The QS asks "how much ductwork per floor"; the planner asks "what does
level 3 weigh in concrete"; the client asks for a door schedule. Takeoff answers the quantity
questions, Report Builder answers the schedule questions, and Data Export is the raw-columns
escape hatch when the shape they want is "just give me everything".

**The status strip**: when quantities were last summed (warns "never" — a project that has
never been measured is usually one that should have been), how many report shapes are saved
with the project, and when a report last went out.

## Takeoff

- *Was already right, and stays:* one axis or two, measure read from whatever the elements
  carry, unreadable values counted and named rather than dropped, provenance stated on every
  run, the cleaner that refuses to invent numbers, pickable totals, the same table written to
  the sheet as shown on screen.
- *Was wrong:* a takeoff split by Level with half the model in "(none)" was a dead end — the
  row said the model has no levels and left the person to know, somehow, that Levels & Zones
  stamps them. The "(none)" line now comes with the count and a one-press jump to
  Levels & Zones, on whichever axis Level sits.
- *Was wrong:* the spreadsheet's default path was the operator's own Documents folder — a
  takeoff the rest of the team cannot see is a takeoff the next person re-runs. It now lands
  beside the project file first, Documents only when there is no project file.

## Report Builder

- *Was already right, and stays:* four steps in dependency order, the model read once, the
  proposal from what the model actually carries, the preview that cannot disagree with the
  file, templates saved to the project, the fillable-columns round trip.
- *Was wrong:* the round trip's return leg was prose — "bring it back through Data ▸ Bring a
  sheet back in" — naming a tab in another app and leaving the person to find it. The write
  tail now carries the jump to Data Manager's Excel tab, which also prefills the read-back
  path with the out-leg's file (see `docs/apps/data.md`).

## Data Export

- Shared surface with Export Kit's Export Data (deliberately: same engine, same memory, two
  doorways). The product pass on it — model-fed tab picker, property picker feeding the column
  list, per-project memory — is recorded in `docs/apps/exports.md`.

## Hand-offs in

- Data Manager ▸ Levels & Zones offers "Take off quantities by level..." the moment the stamp
  lands, and the takeoff's Level axis reads that stamp first — the pair of jumps makes
  "quantities per floor on a model with no levels" a two-press workflow instead of a support
  question.
- Sets Manager's Builder and From-a-Schedule offer the takeoff after publishing a set, since a
  set is most often built exactly to be measured.

## Named as missing, out of scope for this pass

- **Rates and money.** A bill line is quantity × rate; CamelWorks stops at the quantity. A
  rates table (per project, per measure, per group) would need its own store section and its
  own audit trail, and a wrong currency figure is worse than none.
- **Takeoff history.** Each run overwrites the screen; comparing this week's concrete to last
  week's needs the takeoff kept the way baselines are. The activity log keeps the one-line
  summaries, which is a record but not a comparison.
