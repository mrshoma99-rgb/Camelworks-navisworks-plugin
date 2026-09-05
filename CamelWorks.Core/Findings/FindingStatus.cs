using System;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The five statuses, and the only five. They are exactly the host's own clash statuses, so
    /// that CamelWorks status and native status are one field rather than two that drift.
    ///
    /// The ordering is a lattice, and the numeric values are the rung: higher is further through
    /// the process. That ordering is load-bearing — see <see cref="FindingStatusLattice.Merge"/>.
    /// </summary>
    public enum FindingStatus
    {
        /// <summary>Seen for the first time in this run.</summary>
        New = 0,

        /// <summary>Still outstanding on a subsequent run.</summary>
        Active = 1,

        /// <summary>Somebody has looked at it.</summary>
        Reviewed = 2,

        /// <summary>Accepted as not requiring a change.</summary>
        Approved = 3,

        /// <summary>Fixed.</summary>
        Resolved = 4,
    }

    /// <summary>
    /// How two concurrent status edits reconcile.
    ///
    /// Promote-only, and only for merges. When two people edited the same finding while offline —
    /// one on the board, one returning a BCF file — the merge takes the higher status. The reasoning
    /// is asymmetric: overwriting a "Resolved" with a stale "Active" costs a second look at
    /// something already fixed, while overwriting an "Active" with a stale "Resolved" hides an
    /// unresolved conflict behind a green row. Only one of those two errors reaches the building.
    ///
    /// This is not a rule about what a user may do. A coordinator deliberately re-opening an
    /// Approved finding is a direct write rather than a merge and is always allowed, which is what
    /// the board's Regressed state shows. Promote-only governs machine reconciliation of two edits
    /// neither author knew about.
    /// </summary>
    public static class FindingStatusLattice
    {
        /// <summary>
        /// Reconcile two concurrent edits. Never loses progress; see the type remarks for why the
        /// asymmetry is deliberate.
        /// </summary>
        public static FindingStatus Merge(FindingStatus a, FindingStatus b) => a > b ? a : b;

        /// <summary>
        /// True when a finding is still somebody's to deal with.
        ///
        /// Approved and Resolved are both closed: one was fixed and the other was accepted as not
        /// needing fixing, and from a party's outstanding list they are the same thing. Everything
        /// below them is open, including Reviewed — somebody looking at a problem is not the same
        /// as the problem going away, and a board that counted it as closed would show a week's
        /// work disappearing the moment it was read.
        /// </summary>
        /// <param name="status">The status.</param>
        public static bool IsOpen(FindingStatus status) => status < FindingStatus.Approved;

        /// <summary>True when a finding is off somebody's list. The complement of <see cref="IsOpen"/>.</summary>
        /// <param name="status">The status.</param>
        public static bool IsClosed(FindingStatus status) => !IsOpen(status);

        /// <summary>
        /// True when moving from <paramref name="from"/> to <paramref name="to"/> is a demotion —
        /// which a merge refuses and a direct edit permits. Callers use this to decide whether an
        /// incoming change needs to be surfaced as a conflict rather than applied.
        /// </summary>
        public static bool IsDemotion(FindingStatus from, FindingStatus to) => to < from;

        /// <summary>
        /// True when the status means "somebody has dealt with this" — the two the host does NOT
        /// recompute on a re-run, and therefore the only two safe to read back from it as truth.
        /// New, Active and Resolved are recomputed by the clash engine every run, so treating them
        /// as authoritative would silently overwrite a human decision with a machine's guess.
        /// </summary>
        public static bool IsHumanJudgement(FindingStatus status) =>
            status == FindingStatus.Reviewed || status == FindingStatus.Approved;

        /// <summary>Parse a status name, case-insensitively. Returns false rather than throwing.</summary>
        public static bool TryParse(string? text, out FindingStatus status)
        {
            status = FindingStatus.New;
            if (string.IsNullOrWhiteSpace(text)) return false;

            switch (text!.Trim().ToUpperInvariant())
            {
                case "NEW": status = FindingStatus.New; return true;
                case "ACTIVE": status = FindingStatus.Active; return true;
                case "REVIEWED": status = FindingStatus.Reviewed; return true;
                case "APPROVED": status = FindingStatus.Approved; return true;
                case "RESOLVED": status = FindingStatus.Resolved; return true;
                default: return false;
            }
        }

        /// <summary>The canonical name, as written to files and shown on the board.</summary>
        public static string ToName(FindingStatus status) => status switch
        {
            FindingStatus.New => "New",
            FindingStatus.Active => "Active",
            FindingStatus.Reviewed => "Reviewed",
            FindingStatus.Approved => "Approved",
            FindingStatus.Resolved => "Resolved",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }
}
