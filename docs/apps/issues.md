# Issue Manager — product audit

**The app's job.** Every issue on the job, in one register, whatever raised it. A coordinator
asks "what is still open on level three, and who owes me an answer" — and until now the honest
answer was a spreadsheet beside the tool, because issues could only be raised beside a clash,
only while the board was loaded, and only about clashes.

**Four ways in, one record.** Written by hand (from whatever is selected in the host), made
from picked clash results, made from picked clearance findings, or read out of somebody's BCF
file. What raised it is a column, never a filing cabinet — and it is the one field nobody can
edit afterwards, because it is a fact about the past.

**The shape** is the issue list every common-data environment already taught people: a filter
rail (status ticks, overdue, mine, assignee, type, source, search), a sortable register, and a
detail panel that opens on the row you picked and edits in place — title, description, status,
type, priority, assignee, due date, root cause, level, zone — with the comment trail under it
and **Show** putting the model where the issue is.

- **Bulk verbs** on picked rows: show, set status, assign, comment, delete (behind a second
  press), copy. Deleting says out loud that closing is what somebody usually means.
- **Right-click in the Clash Manager's results grid or the Clearance Check's**: *Create an issue
  from N* (one issue over everything picked — eleven clashes are usually one conversation) or
  *An issue for each of N*. The issue carries the elements, the findings and the point.
- **Attach to an issue** puts picked results onto one that already exists — the verb that stops a
  register filling with forty issues about the same duct run. An issue holds findings of any
  source, so one conversation can cover a clash, a clearance breach and a health failure together.
- **The findings card** on an issue's detail says what it is about, each one's status, and whether
  the latest run still reports it; *Take N off* detaches without touching the findings themselves.
- **The cascade is offered, never applied.** Saving a status change that implies something — closed
  means resolved, answered means reviewed — puts up one line naming the count. Ignore it and
  nothing happens. Draft, pending and in dispute imply nothing: they are about the conversation.
- **Import** is a merge, not a load: the same BCF file arriving three times over a fortnight
  updates what it already brought in and adds only the comments nobody said yet. The Issue
  Link BCF tab's read now lands in the register too — a BCF file read anywhere in the product
  ends up in one place.
- **Export** writes one topic per issue with status, type, priority, assignee, due date,
  description and every comment; the register's own number travels as a label so a reply can be
  matched to the issue it answers.
- **Excel** writes the register exactly as it is filtered, for the meeting.
- **Statuses** are the six a construction issue actually moves through — Draft, Open, Answered,
  Pending, In dispute, Closed. A status another tool spells its own way ("Resolved", "Done") is
  read as Closed; one nobody here has a word for is kept as it was written rather than forced
  into a bucket.

---

# Issue Link — product audit

**The app's job.** The outbound half of the coordination week. The board gets decided in Clash
Manager; Issue Link is where it leaves the building — the report for the meeting, the BCF or
the clash file for whoever answers in a tool, the worksheet for whoever answers in Excel — and where
the answers come back and land on the board. The persona is the coordinator on Thursday
(publish), and the same coordinator the following Monday (read the replies).

**The one fact every tab assumes** is a board, and the board is no longer a tab anywhere. Each
surface here opens by stating where it stands — loaded and how many issues, or not loaded — and
"not loaded" now carries **Load the board** *on this tab*, which builds it in place. Nobody
navigates to another app to fetch a prerequisite.

## Report (and Penetrations, which is the same screen locked to one template)

- *The job:* Thursday afternoon. One file per party, revision-lettered, superseding last week's.
  Penetrations is the structural engineer's pre-pour list, reached without answering "which of
  three reports?".
- *Was wrong:* the tab said nothing about the board until the press — a coordinator could set
  up audience, format and markers against a board that was not the one they triaged (Write
  silently rebuilds from the saved rules). Now the landing data leads with the board state and
  the warning that writing without loading reports the rebuilt board, not the triaged one.
- *Was wrong:* after "Wrote revision C", the screen stopped. The real Thursday continues — the
  same board goes out as BCF to whoever answers in a tool. A successful write now
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

## Clash file

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
