namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// What the project file's clash section holds, by name.
    ///
    /// Every one of these was a string literal at its use site, which meant a key written in one
    /// screen and read in another agreed only as long as both were spelt the same way. Two of them
    /// already have their names on the type that owns the value — <see cref="ClashIssueLog.Key"/>
    /// and the levels — and those stay where they are; this is for the rest, which belong to no
    /// type in particular.
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

        /// <summary>Issues somebody raised by hand, which ride on the board beside the results.</summary>
        public const string Issues = "issues";

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
    }
}
