# Issue Manager

> Everything raised on this job, in one register.

The answer to *what is still open on level three, and who owes me an answer* — without a
spreadsheet beside the tool.

**Tabs:** [Issues](#issues) · [Import](#import) · [Export](#export)

## Four ways in, one record

| Raised by | How |
|---|---|
| A person | **New issue**, on whatever is selected in the model |
| Clash results | Right-click in Clash Manager ▸ Manage → *Create an issue from N*, or *Attach N to an issue…* to put them on one that already exists |
| Clearance findings | Right-click in Clash Manager ▸ Clearance Check |
| Somebody else's tool | A BCF file, read in through Import (or through Issue Link ▸ BCF) |

What raised an issue is a **column**, not a filing cabinet — one register holds all four. It is
also the one field nobody can edit afterwards, because it is a fact about the past.

Raising from clash or clearance offers two shapes: **one issue over everything picked** (eleven
clashes are usually one conversation) or **an issue for each**. Either way the issue carries the
elements, the findings and the point in space.

**Attach to an issue** is the other half, and the one that stops a register filling with forty
issues about the same duct run: pick the results, pick the issue, and they join it. An issue can
hold findings from any check — clashes, clearance, whatever moves onto findings next — so one
conversation can cover the whole of a problem rather than one flavour of it.

### The findings on an issue

An issue's detail carries a **Findings** card: what it is about, each one's status, and whether
the latest run still reports it. A finding the newest run no longer mentions is marked *gone*
rather than removed — it is the record of the thing having been fixed. **Take N off** detaches
them from the issue; the findings themselves are untouched.

### Marking the findings when you mark the issue

Closing an issue usually means its findings are dealt with, and *usually* is not *always* — an
issue closed because the RFI was answered says nothing about whether the duct moved. So it is
**offered, not done**: change the status, press Save, and if it implies something you get one line
naming the count — *"This issue is now closed. Mark 11 attached findings as Resolved too?"* — with
a button. Ignore it and nothing happens.

Only the two ends of the run mean anything for the model: **Closed** → Resolved, **Answered** →
Reviewed. Draft, pending and in dispute are about the conversation, not about the building, so
they offer nothing.

---

## Issues

**Main action: New issue.**

The register, with the filters at the top, and a detail panel that opens on the row you pick and
edits in place.

**On the strip:** search, the status ticks, *only mine*, *overdue*, and the open / overdue / closed
figures. Type, assignee and source live behind the ⚙ symbol, with what the bulk verbs write.

**In the detail panel:** title, description, status, type, priority, assignee, due date, root
cause, level, zone — the comment trail underneath, and **Show**, which puts the model where the
issue is.

**Bulk verbs on the picked rows:** show, set status, assign, comment, delete, copy.

Deleting says out loud that closing is usually what somebody means, and asks twice.

### The statuses

**Draft · Open · Answered · Pending · In dispute · Closed** — the six a construction issue actually
moves through.

A status another tool spells its own way (*Resolved*, *Done*) is read as **Closed**. One nobody
here has a word for is **kept exactly as it was written**, rather than forced into a bucket that
would lose what the other party meant.

---

## Import

**Main action: Read.**

**Import is a merge, not a load.** The same BCF file arriving three times over a fortnight updates
what it already brought in and adds only the comments nobody has said yet — it does not raise
everything twice. Matching is on the BCF GUID; duplicate author-and-body comments are skipped.

A BCF file read anywhere in the product — here, or in [Issue Link ▸ BCF](Issue-Link#bcf) — ends up
in this one register.

---

## Export

**Main action: Write.**

Writes one BCF topic per issue, carrying status, type, priority, assignee, due date, description
and every comment. The register's own number travels as a label, so a reply can be matched back to
the issue it answers.

**Excel** writes the register exactly as it is filtered, for the meeting.
