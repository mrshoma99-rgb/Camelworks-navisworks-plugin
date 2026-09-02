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
        public MatrixSymbol(string symbol, MatrixTest test = MatrixTest.Hard,
                            double tolerance = MatrixOptions.DefaultTolerance, bool enabled = true)
        {
            Symbol = (symbol ?? string.Empty).Trim();
            Test = test;
            Tolerance = tolerance < 0 ? 0 : tolerance;
            Enabled = enabled;
        }

        /// <summary>The cell's text.</summary>
        public string Symbol { get; }

        /// <summary>Which test to make.</summary>
        public MatrixTest Test { get; }

        /// <summary>Its tolerance, in metres.</summary>
        public double Tolerance { get; }

        /// <summary>False to read the symbol but make no test for it.</summary>
        public bool Enabled { get; }

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

        /// <summary>Create a legend.</summary>
        /// <param name="symbols">The entries. Later entries win on a repeated symbol.</param>
        public MatrixLegend(IEnumerable<MatrixSymbol>? symbols = null)
        {
            _byText = new Dictionary<string, MatrixSymbol>(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbols ?? Enumerable.Empty<MatrixSymbol>())
                if (symbol != null && symbol.Symbol.Length > 0) _byText[symbol.Symbol] = symbol;
        }

        /// <summary>The entries, ordered by symbol.</summary>
        public IReadOnlyList<MatrixSymbol> Symbols =>
            _byText.Values.OrderBy(s => s.Symbol, StringComparer.OrdinalIgnoreCase).ToList();

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
                if (Meaning(symbol) == null) filled.Add(new MatrixSymbol(symbol));

            return new MatrixLegend(filled);
        }

        /// <summary>Symbols the grid uses that this legend does not explain.</summary>
        /// <param name="board">The grid to check against.</param>
        public IReadOnlyList<string> Unexplained(MatrixBoard board)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            return board.SymbolsUsed.Where(s => Meaning(s) == null).ToList();
        }
    }
}
