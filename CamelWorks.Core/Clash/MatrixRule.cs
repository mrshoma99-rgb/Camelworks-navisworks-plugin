using System;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// One standing instruction about a whole block of the matrix: this folder against that folder
    /// is checked like this.
    ///
    /// Real matrices are filled by rule, and this one could previously only be filled by hand. "All
    /// services against all architecture, clearance at twenty-five millimetres" is one sentence from
    /// a kick-off and it governs sixty cells. Block fill and folders wrote those sixty once; a
    /// discipline added the following week is not covered, the new row is blank, nothing says it
    /// should not be, and the matrix stops meaning what was agreed.
    ///
    /// A rule is that sentence, kept. It is re-applied whenever the board changes shape, so a
    /// discipline added later arrives checked the way its folder is checked, and a rule that is
    /// changed or deleted takes its cells with it rather than leaving them as decisions nobody made.
    ///
    /// An empty side means any. "All services against everything" is as common as a folder-to-folder
    /// pair, and writing it as one rule per folder is the same maintenance problem one layer up.
    /// </summary>
    public sealed class MatrixRule
    {
        /// <summary>Write one.</summary>
        /// <param name="a">One folder, or empty for any.</param>
        /// <param name="b">The other folder, or empty for any.</param>
        /// <param name="symbol">What goes in the cells it covers.</param>
        public MatrixRule(string? a, string? b, string? symbol)
        {
            A = (a ?? string.Empty).Trim();
            B = (b ?? string.Empty).Trim();
            Symbol = (symbol ?? string.Empty).Trim();
        }

        /// <summary>One folder, or empty for any.</summary>
        public string A { get; }

        /// <summary>The other folder, or empty for any.</summary>
        public string B { get; }

        /// <summary>What it writes: a legend symbol, or the reserved not-required mark.</summary>
        public string Symbol { get; }

        /// <summary>Whether it says anything at all.</summary>
        public bool IsEmpty => Symbol.Length == 0;

        /// <summary>
        /// Whether this rule is about a pair of lines in these two folders.
        ///
        /// Order-independent, because a matrix is mirrored: a rule about services against
        /// architecture is the same rule read the other way, and asking somebody to write it
        /// twice is asking them to keep two things in step for ever.
        /// </summary>
        /// <param name="one">The row's folder.</param>
        /// <param name="other">The column's folder.</param>
        public bool Covers(string? one, string? other) =>
            (Matches(A, one) && Matches(B, other)) || (Matches(A, other) && Matches(B, one));

        private static bool Matches(string side, string? folder) =>
            side.Length == 0 || string.Equals(side, (folder ?? string.Empty).Trim(),
                                              StringComparison.OrdinalIgnoreCase);

        /// <summary>What it says, for a menu or a list.</summary>
        public string Worded() =>
            Named(A) + " against " + Named(B) + " → " + (Symbol.Length == 0 ? "nothing" : Symbol);

        private static string Named(string side) => side.Length == 0 ? "Anything" : side;

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object().Set("a", A).Set("b", B).Set("symbol", Symbol);

        /// <summary>Read one back, or null when there is nothing usable in it.</summary>
        /// <param name="json">The stored rule.</param>
        public static MatrixRule? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var rule = new MatrixRule(json["a"].AsString(), json["b"].AsString(),
                                      json["symbol"].AsString());

            return rule.IsEmpty ? null : rule;
        }

        /// <inheritdoc />
        public override string ToString() => Worded();
    }
}
