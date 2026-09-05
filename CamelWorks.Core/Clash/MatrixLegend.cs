using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Clash
{
    /// <summary>How a pair of categories should be tested against each other.</summary>
    public enum MatrixTest
    {
        /// <summary>Solids actually overlapping. The ordinary test.</summary>
        Hard,

        /// <summary>Hard, but on the triangles rather than the solids. Slower, catches more.</summary>
        HardConservative,

        /// <summary>Not touching, but closer than they are allowed to be.</summary>
        Clearance,

        /// <summary>The same thing modelled twice.</summary>
        Duplicate,
    }

    /// <summary>
    /// What one symbol in a cell means: which test to make, and how tight.
    ///
    /// The symbols are whatever the sheet already uses. A matrix that arrives from a client says
    /// "A" and "B" and has a key in the corner explaining them; asking somebody to retype that as
    /// our vocabulary before the tool will read their sheet is asking them to do the tool's job.
    /// </summary>
    public sealed class MatrixSymbol
    {
        /// <summary>Create a legend entry.</summary>
        /// <param name="symbol">The cell's text, as the sheet has it.</param>
        /// <param name="test">Which test to make.</param>
        /// <param name="tolerance">Its tolerance, in metres.</param>
        /// <param name="enabled">False to read the symbol but make no test for it.</param>
        /// <param name="colour">
        /// What its cells are washed with, as <c>#RRGGBB</c>, or empty to let the drawing choose.
        /// </param>
        public MatrixSymbol(string symbol, MatrixTest test = MatrixTest.Hard,
                            double tolerance = MatrixOptions.DefaultTolerance, bool enabled = true,
                            string? colour = null)
        {
            Symbol = (symbol ?? string.Empty).Trim();
            Test = test;
            Tolerance = tolerance < 0 ? 0 : tolerance;
            Enabled = enabled;
            Colour = Hexed(colour);
        }

        /// <summary>The cell's text.</summary>
        public string Symbol { get; }

        /// <summary>Which test to make.</summary>
        public MatrixTest Test { get; }

        /// <summary>Its tolerance, in metres.</summary>
        public double Tolerance { get; }

        /// <summary>False to read the symbol but make no test for it.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// What its cells are washed with, as <c>#RRGGBB</c>, or empty for the drawing's choice.
        ///
        /// Held here rather than in the screen that draws it, because a matrix's colours belong to
        /// the matrix: a practice that has always drawn clearances green expects them green on the
        /// next seat and in the copy they send out. Empty is not a missing value; it means "whatever
        /// the tool would pick", which is what every matrix starts as.
        /// </summary>
        public string Colour { get; }

        /// <summary>
        /// A colour that is safe to hand to a drawing, or empty.
        ///
        /// Nothing here parses it into a colour - Core has no idea what one is - but a string
        /// that is not six or three hex digits behind a hash is not one either, and letting it
        /// through means every screen that reads this has to guard against the same thing.
        /// </summary>
        /// <param name="said">What was given.</param>
        private static string Hexed(string? said)
        {
            var text = (said ?? string.Empty).Trim();

            if (text.Length != 4 && text.Length != 7) return string.Empty;
            if (text[0] != '#') return string.Empty;

            for (var at = 1; at < text.Length; at++)
            {
                var one = text[at];

                var hex = (one >= '0' && one <= '9') || (one >= 'a' && one <= 'f')
                          || (one >= 'A' && one <= 'F');

                if (!hex) return string.Empty;
            }

            return text.ToUpperInvariant();
        }

        /// <inheritdoc />
        public override string ToString() =>
            Symbol + " = " + Test + " at "
            + Tolerance.ToString("0.###", CultureInfo.InvariantCulture) + "m"
            + (Enabled ? string.Empty : " (off)");
    }

    /// <summary>Every symbol the matrix uses, and what each one means.</summary>
    public sealed class MatrixLegend
    {
        private readonly Dictionary<string, MatrixSymbol> _byText;
        private readonly List<MatrixSymbol> _inOrder;

        /// <summary>Create a legend.</summary>
        /// <param name="symbols">The entries. Later entries win on a repeated symbol.</param>
        public MatrixLegend(IEnumerable<MatrixSymbol>? symbols = null)
        {
            _byText = new Dictionary<string, MatrixSymbol>(StringComparer.OrdinalIgnoreCase);
            _inOrder = new List<MatrixSymbol>();

            foreach (var symbol in symbols ?? Enumerable.Empty<MatrixSymbol>())
            {
                if (symbol == null || symbol.Symbol.Length == 0) continue;

                // A repeated symbol replaces the one before it where it already sat, rather than
                // being appended: an edit is a new legend built out of the old one, and an edited
                // symbol that jumped to the end would renumber every key under it.
                var at = _inOrder.FindIndex(
                    one => string.Equals(one.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));

                if (at >= 0) _inOrder[at] = symbol;
                else _inOrder.Add(symbol);

                _byText[symbol.Symbol] = symbol;
            }
        }

        /// <summary>
        /// The entries, in the order they were added.
        ///
        /// Not alphabetical. This order is the palette's order, and the palette's order is the number
        /// key that puts each symbol in a cell, so sorting by text meant that adding a symbol which
        /// sorted early renumbered every key under it. A user who has learned that 2 is the
        /// fifty-millimetre clearance loses that across every cell typed afterwards, with nothing on
        /// screen to say so. In insertion order a new symbol lands at the end and the learned keys
        /// stay correct.
        /// </summary>
        public IReadOnlyList<MatrixSymbol> Symbols => _inOrder.ToList();

        /// <summary>True when nothing has been defined.</summary>
        public bool IsEmpty => _byText.Count == 0;

        /// <summary>What a symbol means, or null when the legend does not say.</summary>
        /// <param name="symbol">The cell's text.</param>
        public MatrixSymbol? Meaning(string? symbol) =>
            symbol != null && _byText.TryGetValue(symbol.Trim(), out var found) ? found : null;

        /// <summary>
        /// A legend covering every symbol a grid uses, keeping what is already defined.
        ///
        /// So a sheet can be read before anybody has said what its symbols mean: the unknown ones
        /// arrive as ordinary hard clashes at the default tolerance, visibly, rather than the read
        /// failing or the cells being dropped. What the reader then does is correct the two that
        /// are not hard clashes, which is a smaller job than describing all of them.
        /// </summary>
        /// <param name="board">The grid whose symbols must all be covered.</param>
        public MatrixLegend Covering(MatrixBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            var filled = new List<MatrixSymbol>(_byText.Values);

            foreach (var symbol in board.SymbolsUsed)
                if (!MatrixBoard.IsNotRequired(symbol) && Meaning(symbol) == null)
                    filled.Add(new MatrixSymbol(symbol));

            return new MatrixLegend(filled);
        }

        /// <summary>Symbols the grid uses that this legend does not explain.</summary>
        /// <param name="board">The grid to check against.</param>
        public IReadOnlyList<string> Unexplained(MatrixBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            // The reserved mark is never unexplained: it is the one thing in the grid whose
            // meaning is fixed, and offering to give it a tolerance is offering to turn every
            // pair somebody ruled out into a test.
            return board.SymbolsUsed
                .Where(s => !MatrixBoard.IsNotRequired(s) && Meaning(s) == null)
                .ToList();
        }
    }
}
