# Issue Link — product audit

**The app's job.** The outbound half of the coordination week. The board gets decided in Clash
Manager; Issue Link is where it leaves the building — the report for the meeting, the BCF or
Clash XML for whoever answers in a tool, the worksheet for whoever answers in Excel — and where
the answers come back and land on the board. The persona is the coordinator on Thursday
(publish), and the same coordinator the following Monday (read the replies).

**The one fact every tab assumes** is a board loaded in another app. The status strip said so;
the surfaces themselves mostly did not — each one discovered it at the press, in its own words,
with no way over. That was the biggest single finding of this pass: every tab now opens by
stating where the board stands (loaded and how many issues, or not loaded), and "not loaded"
carries a button that opens Clash Manager's Board tab with load-on-arrival set, the same
hand-off the Tests tab already used.

## Report (and Penetrations, which is the same screen locked to one template)

- *The job:* Thursday afternoon. One file per party, revision-lettered, superseding last week's.
  Penetrations is the structural engineer's pre-pour list, reached without answering "which of
  three reports?".
- *Was wrong:* the tab said nothing about the board until the press — a coordinator could set
  up audience, format and markers against a board that was not the one they triaged (Write
  silently rebuilds from the saved rules). Now the landing data leads with the board state and
  the warning that writing without loading reports the rebuilt board, not the triaged one.
- *Was wrong:* after "Wrote revision C", the screen stopped. The real Thursday continues — the
  same board goes out as BCF or Clash XML to whoever answers in a tool. A successful write now
  offers both jumps and says where the reply will land when it returns. Suppressed for the
  penetration schedule: nobody answers a pour list through a BCF tool, and a button pretending
  otherwise would be noise.
- *Was wrong:* the marker builder's "board not built" message named the fix ("open Clash
  Triage") without doing it. It is now the same one-press load-on-arrival hand-off.
- *Held:* audience from the profile's parties, per-party revision ledgers, the funnel always of
  the whole board, the picture-cost warning, ToolMemory on every option. No input here asks for
  a name the model knows better.

## BCF

- *The job:* the round trip. Send one topic per issue; a fortnight later, read the answered file
  against the board and apply what survives the promote-only merge.
- *Was wrong:* board state invisible until the press (worse on the spreadsheet leg, which
  refuses outright without a board). The landing line now states it, and both refusals
  ("nothing to send", "nothing to reconcile against") became the load-the-board hand-off
  instead of an instruction to go and find another window.
- *Was wrong:* a successful send dead-ended. It now says out loud what the file is for — "when
  it comes back answered, put it in Read the answers back" — and offers the report jump for the
  half of the audience that reads PDF. The worksheet leg got the same read-back pointer.
- *Held:* the two-path in/out boxes, GUID de-duplication, the promote-only status lattice, the
  nothing-applied-until-the-button contract, the reply table's copy-for-the-email picking.

## Clash XML

- *The job:* the baseline file a reviewer opens in their own tool, against a saved per-test
  profile with a last-sent date per test.
- *Was wrong:* the always-visible "what is actually going" line told a boardless user to "open
  Clash Triage and run it" as prose; the rehearse refusal did the same. Both are now the
  one-press load hand-off.
- *Was wrong:* two dead ends. Writing the file said nothing about the return leg sitting one
  segment-switch away; reading a return ended on "taking it on is a decision the board makes"
  with no way to the board. The write now points at the read leg (and at BCF's read, which is
  where a reply actually lands on the board); the read now opens the Clash Board.
- *Held:* profiles in the project file and on disk, the rename/missing test reconciliation, the
  rehearse-then-commit two-step, last-sent stamps.

## Merge

- *The job:* six packs go out Monday, six come back Thursday; fold them onto one board and show
  the disagreements instead of letting the last file win.
- *Was wrong:* the merge reads fine without a board but its answers then land nowhere, and the
  tab only said so after the work. The landing line now states the board and says plainly that
  without it the merge ends at a message rather than an apply button; the post-merge "load the
  board and read again" note became the one-press hand-off.
- *Held:* party from the filename, first-sight baseline reconstruction, conflicts shown not
  resolved, the shared Reply/apply path with BCF.

## Out of scope, named

- Emailing the per-party files to the roster's addresses (the roster already holds them) would
  need a mail engine the product does not have; the record of "last exchange" in the strip is
  the honest substitute.
- A "re-send exactly what went out last time" one-press would need the export options recorded
  per exchange, not just the activity line — a small store change, but an engine-side one.

**Checks:** UiCheck build clean; all thirteen validators pass.
