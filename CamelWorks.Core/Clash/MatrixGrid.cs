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
    /// One row and column of the matrix: a discipline, package or system, backed by a set that
    /// already exists in the model.
    ///
    /// Rows and columns are the same list. A matrix where they could differ would be a table of
    /// pairs, and nobody draws one of those — the thing people bring to a coordination kick-off is
    /// a square with the same headings down the side and across the top.
    /// </summary>
    public sealed class MatrixCategory
    {
        /// <summary>Create a category.</summary>
        /// <param name="name">How it reads — "Foundations", "Ductwork".</param>
        /// <param name="setName">The saved set backing it. Empty when nothing backs it yet.</param>
        /// <param name="number">Its number in the sheet, used in test names. Optional.</param>
        /// <param name="code">A discipline code such as "ST". Optional.</param>
        /// <param name="scopes">Whole-model scopes backing it instead of a set, when it is models.</param>
        public MatrixCategory(string name, string? setName = null, string? number = null, string? code = null,
                              IReadOnlyList<string>? scopes = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name.Trim();
            SetName = (setName ?? string.Empty).Trim();
            Number = (number ?? string.Empty).Trim();
            Code = (code ?? string.Empty).Trim();
            Scopes = scopes ?? Array.Empty<string>();
        }

        /// <summary>Whole-model scopes backing it, when it is models rather than a set.</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>How it reads.</summary>
        public string Name { get; }

        /// <summary>The saved set backing it, or empty.</summary>
        public string SetName { get; }

        /// <summary>Its number in the sheet, or empty.</summary>
        public string Number { get; }

        /// <summary>Its discipline code, or empty.</summary>
        public string Code { get; }

        /// <summary>True when nothing backs it - no set and no models - so no test on it could run.</summary>
        public bool IsUnbacked => SetName.Length == 0 && Scopes.Count == 0;

        /// <inheritdoc />
        public override string ToString() =>
            Number.Length == 0 ? Name : Number + " " + Name;
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
        /// <param name="grid">The grid whose symbols must all be covered.</param>
        public MatrixLegend Covering(MatrixGrid grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            var filled = new List<MatrixSymbol>(_byText.Values);

            foreach (var symbol in grid.SymbolsUsed)
                if (Meaning(symbol) == null) filled.Add(new MatrixSymbol(symbol));

            return new MatrixLegend(filled);
        }

        /// <summary>Symbols the grid uses that this legend does not explain.</summary>
        /// <param name="grid">The grid to check against.</param>
        public IReadOnlyList<string> Unexplained(MatrixGrid grid)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            return grid.SymbolsUsed.Where(s => Meaning(s) == null).ToList();
        }
    }

    /// <summary>
    /// The matrix: one ordered list of categories, and a symbol in some of the cells.
    ///
    /// Cell (i,j) and cell (j,i) are the same test, so a symbol is stored once under a normalised
    /// pair and reading either way round returns it. Half the grid on screen is a mirror of the
    /// other half; storing both halves is how they come to disagree.
    ///
    /// The diagonal is a category against itself, which is a real test — the one that finds the
    /// duct running through its own hanger — and is kept rather than skipped.
    /// </summary>
    public sealed class MatrixGrid
    {
        private readonly List<MatrixCategory> _categories;
        private readonly Dictionary<string, string> _cells = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Create a grid.</summary>
        /// <param name="categories">The rows, which are also the columns.</param>
        public MatrixGrid(IEnumerable<MatrixCategory>? categories = null)
        {
            _categories = (categories ?? Enumerable.Empty<MatrixCategory>()).Where(c => c != null).ToList();
        }

        /// <summary>The rows, which are also the columns.</summary>
        public IReadOnlyList<MatrixCategory> Categories => _categories;

        /// <summary>How many cells carry a symbol.</summary>
        public int Filled => _cells.Count;

        /// <summary>True when there is nothing to build from.</summary>
        public bool IsEmpty => _categories.Count == 0 || _cells.Count == 0;

        /// <summary>Every distinct symbol in the grid, ordered.</summary>
        public IReadOnlyList<string> SymbolsUsed =>
            _cells.Values.Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Add a category and return its index.</summary>
        /// <param name="category">The category.</param>
        public int Add(MatrixCategory category)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));

            _categories.Add(category);
            return _categories.Count - 1;
        }

        /// <summary>Put a symbol in a cell, or clear it with null.</summary>
        /// <param name="a">One category's index.</param>
        /// <param name="b">The other's.</param>
        /// <param name="symbol">The symbol, or null to clear.</param>
        public void Set(int a, int b, string? symbol)
        {
            var key = Key(a, b);
            if (key == null) return;

            // Narrowed into a local rather than relying on IsNullOrWhiteSpace: netstandard2.0's
            // reference assembly carries no [NotNullWhen] on it, so the compiler cannot see that
            // the else branch has a string.
            var text = symbol == null ? string.Empty : symbol.Trim();

            if (text.Length == 0) _cells.Remove(key);
            else _cells[key] = text;
        }

        /// <summary>What is in a cell, either way round, or null.</summary>
        /// <param name="a">One category's index.</param>
        /// <param name="b">The other's.</param>
        public string? At(int a, int b)
        {
            var key = Key(a, b);
            return key != null && _cells.TryGetValue(key, out var symbol) ? symbol : null;
        }

        /// <summary>Every filled cell once, in grid order, never twice for the mirrored pair.</summary>
        public IEnumerable<(int A, int B, string Symbol)> Pairs()
        {
            for (var a = 0; a < _categories.Count; a++)
            for (var b = a; b < _categories.Count; b++)
            {
                var symbol = At(a, b);
                if (symbol != null) yield return (a, b, symbol);
            }
        }

        /// <summary>
        /// Drop a category and renumber the cells around it.
        ///
        /// Every pair above the removed index shifts down by one, and every pair touching it goes.
        /// Getting this wrong leaves symbols attached to the wrong categories, which is worse than
        /// losing them: the grid still reads as a grid and now says something nobody meant.
        /// </summary>
        /// <param name="index">The category to drop.</param>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _categories.Count) return;

            var moved = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var cell in _cells)
            {
                var parts = cell.Key.Split(':');
                var a = int.Parse(parts[0], CultureInfo.InvariantCulture);
                var b = int.Parse(parts[1], CultureInfo.InvariantCulture);

                if (a == index || b == index) continue;

                if (a > index) a--;
                if (b > index) b--;

                moved[a.ToString(CultureInfo.InvariantCulture) + ":"
                      + b.ToString(CultureInfo.InvariantCulture)] = cell.Value;
            }

            _categories.RemoveAt(index);

            _cells.Clear();
            foreach (var cell in moved) _cells[cell.Key] = cell.Value;
        }

        private string? Key(int a, int b)
        {
            if (a < 0 || b < 0 || a >= _categories.Count || b >= _categories.Count) return null;

            var low = Math.Min(a, b);
            var high = Math.Max(a, b);

            return low.ToString(CultureInfo.InvariantCulture) + ":"
                   + high.ToString(CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override string ToString() =>
            _categories.Count.ToString(CultureInfo.InvariantCulture) + " categories, "
            + _cells.Count.ToString(CultureInfo.InvariantCulture) + " filled";
    }
}
