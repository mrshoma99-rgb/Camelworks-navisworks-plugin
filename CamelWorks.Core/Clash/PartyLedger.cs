using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Clash
{
    /// <summary>One party's share of the board.</summary>
    public sealed class PartyLine
    {
        internal PartyLine(string party, int open, int closed, int? previousOpen)
        {
            Party = party;
            Open = open;
            Closed = closed;
            PreviousOpen = previousOpen;
        }

        /// <summary>Who is responsible.</summary>
        public string Party { get; }

        /// <summary>How many findings are still theirs to deal with.</summary>
        public int Open { get; }

        /// <summary>How many are approved or resolved.</summary>
        public int Closed { get; }

        /// <summary>How many were open at the previous snapshot, or null when there was none.</summary>
        public int? PreviousOpen { get; }

        /// <summary>The movement since the previous snapshot, or null when there was none.</summary>
        public int? Delta => PreviousOpen.HasValue ? Open - PreviousOpen.Value : (int?)null;

        /// <summary>
        /// The delta as a report prints it, or empty when there is no prior snapshot.
        ///
        /// Empty, never zero. A party that has never been measured has not "moved by nothing" —
        /// printing a 0 there is a claim about a week that was never observed.
        /// </summary>
        public string DeltaText =>
            !Delta.HasValue ? string.Empty
            : Delta.Value == 0 ? "0"
            : Delta.Value > 0 ? "+" + Delta.Value.ToString("N0", CultureInfo.InvariantCulture)
            : Delta.Value.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Everything on this party's plate, open and closed.</summary>
        public int Total => Open + Closed;

        /// <inheritdoc />
        public override string ToString() =>
            Party + ": " + Open.ToString("N0", CultureInfo.InvariantCulture) + " open"
            + (DeltaText.Length == 0 ? string.Empty : " (" + DeltaText + ")");
    }

    /// <summary>
    /// Who has what, and whether it is going down.
    ///
    /// The single most-asked question of a coordination board, and the one it takes a spreadsheet
    /// to answer today: not how many clashes there are, but how many each party still owes and
    /// whether last week's list got shorter.
    ///
    /// <b>Exactly two snapshots, never a series.</b> A trend line over twelve weeks looks like
    /// management information and is not: groups get re-derived, tests get re-scoped, models get
    /// republished, and a curve drawn through those is a curve through four different questions.
    /// This week against last week, with both timestamps and the scope printed beside it, is a
    /// comparison a coordinator can actually defend.
    ///
    /// A party with no prior measurement gets an empty delta cell rather than a zero, because "did
    /// not move" and "was never measured" are different facts.
    /// </summary>
    public sealed class PartyLedger
    {
        /// <summary>How an unassigned group reads.</summary>
        public const string Nobody = "Unassigned";

        private PartyLedger(IReadOnlyList<PartyLine> lines, DateTimeOffset? now, DateTimeOffset? before,
                            string? scope, bool hasPrevious)
        {
            Lines = lines;
            TakenUtc = now;
            PreviousUtc = before;
            Scope = scope;
            HasPrevious = hasPrevious;
        }

        /// <summary>One per party, most open first.</summary>
        public IReadOnlyList<PartyLine> Lines { get; }

        /// <summary>When this reading was taken.</summary>
        public DateTimeOffset? TakenUtc { get; }

        /// <summary>When the reading it is compared against was taken.</summary>
        public DateTimeOffset? PreviousUtc { get; }

        /// <summary>What the board covered — printed beside the figures, so they can be read.</summary>
        public string? Scope { get; }

        /// <summary>True when there was a previous snapshot to compare against.</summary>
        public bool HasPrevious { get; }

        /// <summary>Everything still outstanding.</summary>
        public int Open => Lines.Sum(l => l.Open);

        /// <summary>Everything approved or resolved.</summary>
        public int Closed => Lines.Sum(l => l.Closed);

        /// <summary>
        /// Take a reading.
        /// </summary>
        /// <param name="groups">The board.</param>
        /// <param name="previous">The previous snapshot, or null when there is none.</param>
        /// <param name="scope">What the board covered.</param>
        /// <param name="takenUtc">When this reading was taken.</param>
        public static PartyLedger Of(IEnumerable<ClashGroup>? groups, ClashSnapshot? previous = null,
                                     string? scope = null, DateTimeOffset? takenUtc = null)
        {
            var open = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var closed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups ?? Array.Empty<ClashGroup>())
            {
                if (group == null) continue;

                var party = string.IsNullOrWhiteSpace(group.AssignedTo) ? Nobody : group.AssignedTo!;

                foreach (var item in group.Items)
                {
                    var into = FindingStatusLattice.IsOpen(item.Status) ? open : closed;
                    into[party] = into.TryGetValue(party, out var seen) ? seen + 1 : 1;
                }
            }

            var was = previous?.OpenByParty;
            var hasPrevious = was != null && was.Count > 0;

            var parties = new HashSet<string>(open.Keys, StringComparer.OrdinalIgnoreCase);
            parties.UnionWith(closed.Keys);

            // A party that has gone to zero this week still gets a line, because "was 40, now 0" is
            // the best number on the report and dropping the row would hide it.
            if (was != null) parties.UnionWith(was.Keys);

            var lines = parties
                .Select(p => new PartyLine(p,
                    open.TryGetValue(p, out var o) ? o : 0,
                    closed.TryGetValue(p, out var c) ? c : 0,
                    was != null && was.TryGetValue(p, out var w) ? w
                        : hasPrevious ? 0 : (int?)null))
                .Where(l => l.Total > 0 || (l.PreviousOpen ?? 0) > 0)
                .OrderByDescending(l => l.Open)
                .ThenByDescending(l => l.Total)
                .ThenBy(l => l.Party, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PartyLedger(lines, takenUtc, previous?.TakenUtc, scope, hasPrevious);
        }

        /// <summary>The table, as a report or a board header shows it.</summary>
        public TableBlock Table()
        {
            var table = HasPrevious
                // "Change" rather than a delta sign: the PDF is written in Helvetica with WinAnsi
                // encoding, which has no Greek, and a column headed "?" is worse than a long word.
                ? new TableBlock("Responsible party", "Open", "Closed", "Was open", "Change")
                : new TableBlock("Responsible party", "Open", "Closed");

            foreach (var line in Lines)
            {
                if (HasPrevious)
                    table.Row(line.Party, Measure.Count(line.Open), Measure.Count(line.Closed),
                              line.PreviousOpen.HasValue ? Measure.Count(line.PreviousOpen.Value) : string.Empty,
                              line.DeltaText);
                else
                    table.Row(line.Party, Measure.Count(line.Open), Measure.Count(line.Closed));
            }

            return table;
        }

        /// <summary>The one line that goes in the board header.</summary>
        public string Explain()
        {
            if (Lines.Count == 0) return "nothing on the board";

            var s = Measure.Count(Open) + " open, " + Measure.Count(Closed) + " closed across "
                  + Measure.Count(Lines.Count) + (Lines.Count == 1 ? " party" : " parties");

            if (HasPrevious)
            {
                var moved = Lines.Where(l => l.Delta.HasValue).Sum(l => l.Delta!.Value);

                s += moved == 0 ? " - no net movement since the last run"
                   : moved < 0 ? " - " + Measure.Count(-moved) + " fewer open than the last run"
                   : " - " + Measure.Count(moved) + " more open than the last run";
            }

            return s;
        }

        /// <summary>
        /// What the two figures are of, printed beside them.
        ///
        /// A per-party count means nothing without the scope and the two dates it spans, and a
        /// report that omits them invites the reading its author least wants.
        /// </summary>
        public string Provenance()
        {
            var s = Scope == null ? "This run" : "This run covers " + Scope;

            if (TakenUtc.HasValue) s += ", taken " + Stamp(TakenUtc.Value);

            s += HasPrevious
                ? PreviousUtc.HasValue
                    ? ", compared against the run of " + Stamp(PreviousUtc.Value) + "."
                    : ", compared against the previous run."
                : ". There is no previous run to compare against, so the change column is not shown.";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static string Stamp(DateTimeOffset when) =>
            when.UtcDateTime.ToString("d MMMM yyyy HH:mm", CultureInfo.InvariantCulture) + "Z";
    }
}
