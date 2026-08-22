using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.OpenBim
{
    /// <summary>One issue as it goes out on a worksheet.</summary>
    public sealed class IssueLine
    {
        /// <summary>Create a line.</summary>
        /// <param name="group">The board group it is, which is its identity across runs.</param>
        public IssueLine(string group)
        {
            Group = string.IsNullOrWhiteSpace(group)
                ? throw new ArgumentException("a line needs its group", nameof(group))
                : group.Trim();

            IssueId = IssueSheet.IdOf(Group);
        }

        /// <summary>The short opaque identity the sheet is keyed on.</summary>
        public string IssueId { get; }

        /// <summary>The board group it is.</summary>
        public string Group { get; }

        /// <summary>Who it is with.</summary>
        public string? Party { get; set; }

        /// <summary>Which level it is on.</summary>
        public string? Level { get; set; }

        /// <summary>Where it is, as the report writes coordinates.</summary>
        public string? Location { get; set; }

        /// <summary>What it is, in one line.</summary>
        public string? Description { get; set; }

        /// <summary>What to do about it, when somebody has said.</summary>
        public string? Instruction { get; set; }

        /// <summary>Where the board has it.</summary>
        public FindingStatus Status { get; set; } = FindingStatus.New;

        /// <summary>When it is wanted by, when the project says.</summary>
        public string? Due { get; set; }

        /// <inheritdoc />
        public override string ToString() => IssueId + " " + Group;
    }

    /// <summary>One returned row, and what it would do to the group it came from.</summary>
    public sealed class IssueResponse
    {
        internal IssueResponse(int row, string issueId, string group, FindingStatus current,
                               FindingStatus? incoming, bool isReopen, string? unknownStatus,
                               string? note)
        {
            Row = row;
            IssueId = issueId;
            Group = group;
            Current = current;
            Incoming = incoming;
            IsReopen = isReopen;
            UnknownStatus = unknownStatus;
            Note = note;

            // The same rule the BCF leg uses, from the same function. Two copies of it would
            // eventually disagree about somebody's approval.
            Merged = BcfResponse.Merge(current, incoming, isReopen);
        }

        /// <summary>Which row of the sheet it came from, numbered as Excel numbers them.</summary>
        public int Row { get; }

        /// <summary>The identity the row carried.</summary>
        public string IssueId { get; }

        /// <summary>The board group it belongs to.</summary>
        public string Group { get; }

        /// <summary>Where the board has it now.</summary>
        public FindingStatus Current { get; }

        /// <summary>What the sheet says, or null when the word was not one CamelWorks knows.</summary>
        public FindingStatus? Incoming { get; }

        /// <summary>Where it would end up.</summary>
        public FindingStatus Merged { get; }

        /// <summary>True when the sheet says somebody deliberately re-opened this.</summary>
        public bool IsReopen { get; }

        /// <summary>The status word, when it was not one CamelWorks recognises.</summary>
        public string? UnknownStatus { get; }

        /// <summary>What they wrote back, when they wrote anything.</summary>
        public string? Note { get; }

        /// <summary>True when applying this would move the group.</summary>
        public bool Changes => Merged != Current;

        /// <summary>True when the sheet asked for a demotion the merge would not take.</summary>
        public bool Refused => Incoming != null && !IsReopen && Incoming.Value < Current;

        /// <summary>How the row reads on the preview.</summary>
        public string Explain()
        {
            if (UnknownStatus != null)
                return Group + ": \"" + UnknownStatus + "\" is not a status CamelWorks knows, so "
                     + "nothing changes. The note still comes across.";

            if (Refused)
                return Group + ": the sheet says " + FindingStatusLattice.ToName(Incoming!.Value)
                     + " but the board has moved on to " + FindingStatusLattice.ToName(Current)
                     + " since it went out, so it stays where it is.";

            if (IsReopen && Changes)
                return Group + ": somebody re-opened it, so it goes back to "
                     + FindingStatusLattice.ToName(Merged) + ".";

            if (!Changes) return Group + ": no change.";

            return Group + ": " + FindingStatusLattice.ToName(Current) + " becomes "
                 + FindingStatusLattice.ToName(Merged) + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>What a returned worksheet would do, before any of it is done.</summary>
    public sealed class IssueSheetReport
    {
        internal IssueSheetReport(IReadOnlyList<IssueResponse> matched, IReadOnlyList<SheetRow> unmatched,
                                  IReadOnlyList<SheetRow> duplicates, int blank, string? problem)
        {
            Matched = matched;
            Unmatched = unmatched;
            Duplicates = duplicates;
            Blank = blank;
            Problem = problem;
        }

        /// <summary>Rows whose identity this project sent out.</summary>
        public IReadOnlyList<IssueResponse> Matched { get; }

        /// <summary>Rows carrying an identity this project never sent.</summary>
        public IReadOnlyList<SheetRow> Unmatched { get; }

        /// <summary>Rows repeating an identity an earlier row already claimed.</summary>
        public IReadOnlyList<SheetRow> Duplicates { get; }

        /// <summary>How many rows carried a response but no identity.</summary>
        public int Blank { get; }

        /// <summary>Why nothing could be read, or null.</summary>
        public string? Problem { get; }

        /// <summary>How many would move.</summary>
        public int Changes => Matched.Count(m => m.Changes);

        /// <summary>How many asked for a demotion the merge would not take.</summary>
        public int Refused => Matched.Count(m => m.Refused);

        /// <summary>How many carried a status word nobody knows.</summary>
        public int Unknown => Matched.Count(m => m.UnknownStatus != null);

        /// <summary>How many were deliberately re-opened.</summary>
        public int Reopened => Matched.Count(m => m.IsReopen && m.Changes);

        /// <summary>True when there is something to apply.</summary>
        public bool IsUsable => Problem == null && Matched.Count > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (Problem != null) return Problem;

            var s = Measure.Count(Changes) + " of " + Measure.Count(Matched.Count)
                  + " returned rows would change the board";

            if (Reopened > 0) s += ", " + Measure.Count(Reopened) + " re-opened";
            if (Refused > 0) s += ", " + Measure.Count(Refused) + " refused as stale";
            if (Unknown > 0) s += ", " + Measure.Count(Unknown) + " with a status nobody knows";

            if (Unmatched.Count > 0)
                s += ", " + Measure.Count(Unmatched.Count) + " naming an issue this project never sent";

            if (Duplicates.Count > 0)
                s += ", " + Measure.Count(Duplicates.Count) + " repeating an earlier row";

            if (Blank > 0) s += ", " + Measure.Count(Blank) + " with no issue id";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The worksheet leg of a coordination conversation.
    ///
    /// BCF assumes the far end has a tool that reads BCF. Most of the far ends on a real project
    /// have Excel, and a coordinator who sends a .bcfzip to a fabricator gets a phone call. So the
    /// same issues go out as a worksheet with two empty columns, and come back through the same
    /// merge policy as a returned BCF file — including the rule that a stale "Open" does not undo
    /// an approval and an explicit re-open is honoured even downwards.
    ///
    /// <b>Keyed on a short opaque id, not on the group name.</b> The name is what a person edits:
    /// they fix a typo, add a level prefix, translate it. The id is six characters derived from the
    /// name once, printed in a column headed "do not edit", and never written back.
    /// </summary>
    public static class IssueSheet
    {
        /// <summary>The identity column, headed for what it is so nobody tidies it away.</summary>
        public const string IdHeading = "Issue id (do not edit)";

        /// <summary>Where the far end writes what they did.</summary>
        public const string ResponseHeading = "Response";

        /// <summary>Where the far end writes where it now stands.</summary>
        public const string StatusHeading = "Response status";

        /// <summary>How many characters an issue id is.</summary>
        public const int IdWidth = 6;

        /// <summary>The status words a returned sheet is expected to use, for a note on the sheet.</summary>
        public static IReadOnlyList<string> Vocabulary { get; } =
            new[] { "Open", "In progress", "Reviewed", "Approved", "Closed", "Reopened" };

        /// <summary>
        /// The short opaque identity for a group.
        ///
        /// Derived rather than allocated, so the same group produces the same id on every machine
        /// and every re-run without anything having to be stored. Six hex characters is a one in
        /// sixteen million collision on a board of a few thousand — and a collision costs one
        /// unmatched row that is reported by name, not a wrong write.
        /// </summary>
        /// <param name="group">The board group's name.</param>
        public static string IdOf(string? group) =>
            "CW-" + Hash.Of(IdWidth, group).ToUpperInvariant();

        /// <summary>Build the sheet that goes out.</summary>
        /// <param name="party">Who it is for, which names the tab.</param>
        /// <param name="lines">One per open issue.</param>
        public static XlsxSheet Write(string? party, IEnumerable<IssueLine>? lines)
        {
            var tab = XlsxSheet.Sanitise(party);

            var sheet = new XlsxSheet(tab.Length == 0 ? "Issues" : tab);

            sheet.Row(IdHeading, "Issue", "Level", "Where", "What is wrong", "What to do",
                      "Status", "Due", ResponseHeading, StatusHeading);

            foreach (var line in lines ?? Array.Empty<IssueLine>())
            {
                if (line == null) continue;

                sheet.Row(line.IssueId,
                          line.Group,
                          line.Level,
                          line.Location,
                          line.Description,
                          line.Instruction,
                          FindingStatusLattice.ToName(line.Status),
                          line.Due,
                          null,
                          null);
            }

            return sheet;
        }

        /// <summary>
        /// Work out what a returned sheet would do, without doing any of it.
        /// </summary>
        /// <param name="sheet">The worksheet that came back.</param>
        /// <param name="sent">Issue id to the board group it was exported from.</param>
        /// <param name="current">Board group to the status it holds now.</param>
        public static IssueSheetReport Read(SheetTable? sheet,
                                            IReadOnlyDictionary<string, string>? sent,
                                            IReadOnlyDictionary<string, FindingStatus>? current)
        {
            // Rebuilt case-insensitively whatever the caller handed over: an id that has been
            // through Excel, an email and a paste into a new sheet comes back in whatever case the
            // last person's autocorrect felt like.
            var known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in sent ?? new Dictionary<string, string>(StringComparer.Ordinal))
                known[entry.Key] = entry.Value;

            var board = current ?? new Dictionary<string, FindingStatus>(StringComparer.Ordinal);

            var read = SheetImport.Of(sheet, known.Keys.ToList(), IdHeading,
                StringComparer.OrdinalIgnoreCase,
                "Send the worksheet this board produced, with that column left as it was.");

            if (read.Problem != null)
                return new IssueSheetReport(Array.Empty<IssueResponse>(), read.Unmatched,
                                            read.Duplicates, read.Blank, read.Problem);

            var statusColumn = read.ColumnOf(StatusHeading);
            var responseColumn = read.ColumnOf(ResponseHeading);

            if (statusColumn < 0 && responseColumn < 0)
                return new IssueSheetReport(Array.Empty<IssueResponse>(), read.Unmatched,
                    read.Duplicates, read.Blank,
                    "that sheet has neither a \"" + ResponseHeading + "\" nor a \"" + StatusHeading
                    + "\" column, so there is nothing on it to bring back.");

            var matched = new List<IssueResponse>();

            foreach (var row in read.Matched)
            {
                if (!known.TryGetValue(row.Key, out var group)) continue;

                var word = statusColumn < 0 ? null : row.Cell(statusColumn);
                var note = responseColumn < 0 ? null : row.Cell(responseColumn);

                matched.Add(new IssueResponse(
                    row.Number,
                    row.Key,
                    group,
                    board.TryGetValue(group, out var held) ? held : FindingStatus.Active,
                    BcfResponse.StatusOf(word),
                    BcfResponse.IsReopen(word),
                    string.IsNullOrWhiteSpace(word) || BcfResponse.StatusOf(word) != null
                        ? null
                        : word!.Trim(),
                    string.IsNullOrWhiteSpace(note) ? null : note!.Trim()));
            }

            return new IssueSheetReport(matched, read.Unmatched, read.Duplicates, read.Blank, null);
        }

        /// <summary>How a sheet's coordinates read, so the outbound and inbound legs agree.</summary>
        /// <param name="x">Easting, in metres.</param>
        /// <param name="y">Northing, in metres.</param>
        /// <param name="z">Elevation, in metres.</param>
        /// <param name="units">How this project writes a length.</param>
        public static string Where(double x, double y, double z, Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            return measure.Bare(x) + ", " + measure.Bare(y) + ", " + measure.Bare(z)
                 + " " + measure.LengthUnit;
        }

        /// <summary>The note that goes on the sheet, so the far end knows what to write.</summary>
        public static string Instructions() =>
            "Write what you did in \"" + ResponseHeading + "\" and where it now stands in \""
            + StatusHeading + "\", using one of: " + string.Join(", ", Vocabulary)
            + ". Leave the rest alone - the \"" + IdHeading
            + "\" column is how each row is matched back to the model, and a row without it cannot "
            + "be brought back. Adding rows is fine; they will be reported rather than applied.";
    }
}
