namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// What the project file's clash section holds, by name.
    ///
    /// Each of these was a string literal at its use site, which meant a key written in one screen
    /// and read in another agreed only as long as both were spelt the same way. Keys whose value
    /// already has a name on the type that owns it — <see cref="ClashIssueLog.Key"/> and the
    /// levels — stay where they are; this is for the ones that belong to no type in particular.
    /// </summary>
    public static class ClashKeys
    {
        /// <summary>The board as the last run left it.</summary>
        public const string Snapshot = "snapshot";

        /// <summary>The board before that, which is what Undo puts back.</summary>
        public const string Previous = "previous";

        /// <summary>Groups a person made by hand, which the rules must never re-derive.</summary>
        public const string Pinned = "pinned";

        /// <summary>What a person decided about a group: the party, the priority, the instruction.</summary>
        public const string Decisions = "decisions";

        /// <summary>The suppress, flag, group and assign rules.</summary>
        public const string Rules = "rules";

        /// <summary>Who is on which group.</summary>
        public const string Assignments = "assignments";

        /// <summary>The last count of what came back, per matrix pair.</summary>
        public const string MatrixCounts = "matrixCounts";

        /// <summary>The count that one replaced, which the grid's deltas are read against.</summary>
        public const string MatrixCountsBefore = "matrixCountsBefore";

        /// <summary>The report writer's revision log, by audience.</summary>
        public const string Issued = "issued";

        /// <summary>The topic GUIDs a written BCF used, so a returned one lands back.</summary>
        public const string BcfTopics = "bcfTopics";

        /// <summary>Which group each written topic came from.</summary>
        public const string BcfGroups = "bcfGroups";

        /// <summary>The frozen board a comparison is read against.</summary>
        public const string Freeze = "freeze";

        /// <summary>The matrix as it was last edited.</summary>
        public const string Matrix = "clashmatrix";

        /// <summary>
        /// Keys nothing writes any more, kept so a project file that still holds them can be
        /// migrated and cleared. Named here rather than spelt out at the one site that removes
        /// them, so a rename cannot leave a stale value behind unnoticed.
        /// </summary>
        public static class Retired
        {
            /// <summary>The snapshot's per-party counts, before they moved into the snapshot.</summary>
            public const string Parties = "parties";

            /// <summary>When the snapshot was taken, before it moved into the snapshot.</summary>
            public const string Taken = "taken";

            /// <summary><see cref="Parties"/>, for the board Undo puts back.</summary>
            public const string PreviousParties = "previousParties";

            /// <summary><see cref="Taken"/>, for the board Undo puts back.</summary>
            public const string PreviousTaken = "previousTaken";
        }
    }
}
