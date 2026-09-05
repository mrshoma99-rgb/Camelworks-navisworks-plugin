using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>How a read off the document's test names is done.</summary>
    public sealed class MatrixReadOptions
    {
        /// <summary>
        /// Whether a test no pattern fits is read from what it is between instead.
        ///
        /// On by default, because a matrix that silently dropped the forty tests set up before
        /// the naming convention settled is a matrix somebody signs off believing it covers the
        /// job. Off, the read is exactly what the patterns say and nothing else - which is what
        /// somebody checking a convention wants.
        /// </summary>
        public bool TakeUnmatched { get; set; } = true;

        /// <summary>
        /// Whether the patterns' own words win over the sets a test points at.
        ///
        /// On: a row is called what the name calls it. Off: the name is only used for the code,
        /// the folder and the symbol, and the row keeps the set's name.
        /// </summary>
        public bool NamesWin { get; set; } = true;
    }

    /// <summary>
    /// A matrix read out of the document's tests, using the naming the job already uses.
    ///
    /// <b>The names were the one thing the read threw away.</b> Every test carries what it is
    /// between, so a matrix could always be reconstructed from the saved sets - and the grid that
    /// came back was named after whatever those sets happened to be called, which on a real job
    /// is "ARC_GENERAL_ELEMENTS_R21_ISSUED" and not what anybody puts in front of a client. Every
    /// other thing the job knows - the discipline codes, the numbers, the packages, which kind of
    /// test each one is - was sitting in the test's own name, in an order that job agreed on
    /// months ago, and none of it was read.
    ///
    /// So the person reading says how their tests are named, in the same tokens the matrix uses
    /// to write names, and this reads them back out. What comes back is their matrix, in their
    /// words, pointing at the sets the tests actually use.
    /// </summary>
    public static class MatrixFromNames
    {
        /// <summary>
        /// Read the chosen tests as a matrix, using the patterns in the order they are given.
        /// </summary>
        /// <param name="tests">The tests to read. Everything not in this list is ignored.</param>
        /// <param name="patterns">The naming conventions, in order. The first that fits wins.</param>
        /// <param name="options">What to do with what does not fit, and who names a row.</param>
        public static MatrixReading Read(IReadOnlyList<ClashTestRead>? tests,
                                         IReadOnlyList<MatrixNamePattern>? patterns,
                                         MatrixReadOptions? options = null)
        {
            var how = options ?? new MatrixReadOptions();
            var usable = (patterns ?? Array.Empty<MatrixNamePattern>())
                .Where(one => one != null && one.IsUsable).ToList();

            // Nothing to read names with is the old read, exactly: what a test is between, and
            // nothing else. Which is still the right answer on a job whose tests were named by
            // six people over two years.
            if (usable.Count == 0)
                return MatrixFromModel.Read(tests ?? Array.Empty<ClashTestRead>());

            var board = new MatrixBoard();
            var warnings = new List<string>();

            var lines = new Dictionary<string, MatrixLine>(StringComparer.OrdinalIgnoreCase);
            var symbols = new Dictionary<string, MatrixSymbol>(StringComparer.Ordinal);
            var pairs = new List<(MatrixLine A, MatrixLine B, string Symbol)>();

            var read = 0;
            var unmatched = 0;

            foreach (var test in tests ?? Array.Empty<ClashTestRead>())
            {
                if (test?.Shape == null || !test.Shape.KnowsA || !test.Shape.KnowsB)
                {
                    warnings.Add(Quoted(test?.Test.Name ?? "A test")
                                 + " does not say what it is between, so it is not on the matrix.");
                    continue;
                }

                var name = test.Test.Name;
                var match = usable.Select(one => one.Match(name)).FirstOrDefault(one => one != null);

                if (match == null)
                {
                    unmatched++;

                    if (!how.TakeUnmatched) continue;
                }

                var a = Line(lines, warnings, match, side: true, how,
                             test.Shape.SetA, test.Shape.ScopesA, name);

                var b = Line(lines, warnings, match, side: false, how,
                             test.Shape.SetB, test.Shape.ScopesB, name);

                if (a == null || b == null) continue;

                var symbol = Symbol(symbols, match, test.Shape);

                pairs.Add((a, b, symbol.Symbol));
                read++;
            }

            foreach (var line in lines.Values) board.AddRow(line);

            board.SquareOffRows();

            foreach (var pair in pairs)
            {
                board.Set(board.Rows.FirstOrDefault(r => ReferenceEquals(r, pair.A)),
                          Matching(board.Columns, pair.A, pair.B), pair.Symbol);

                // Both halves, the way every clash matrix on paper is drawn.
                board.Set(board.Rows.FirstOrDefault(r => ReferenceEquals(r, pair.B)),
                          Matching(board.Columns, pair.B, pair.A), pair.Symbol);
            }

            if (unmatched > 0)
                warnings.Insert(0, unmatched.ToString(CultureInfo.InvariantCulture)
                    + (unmatched == 1 ? " test's name fits" : " tests' names fit")
                    + " none of the patterns, so "
                    + (how.TakeUnmatched
                        ? (unmatched == 1 ? "it was" : "they were") + " read from what "
                          + (unmatched == 1 ? "it is" : "they are") + " between instead."
                        : (unmatched == 1 ? "it is" : "they are") + " not on the matrix."));

            return new MatrixReading(board,
                new MatrixLegend(symbols.Values.OrderBy(s => s.Symbol, StringComparer.Ordinal)),
                warnings, read);
        }

        /// <summary>The column standing for one of a pair's two sides.</summary>
        private static MatrixLine? Matching(IReadOnlyList<MatrixLine> columns,
                                            MatrixLine one, MatrixLine other) =>
            columns.FirstOrDefault(c => string.Equals(c.Name, other.Name, StringComparison.Ordinal));

        /// <summary>
        /// One side of a test as a line of the matrix, named the way the job names it.
        ///
        /// <b>Keyed on the name, not on the set.</b> That is the whole point of reading names: a
        /// discipline that five tests all call "Steel" is one row, whichever five sets those
        /// tests happen to point at. The first set seen becomes what the row points at, and a
        /// second that disagrees is said out loud rather than silently dropped - because two
        /// different sets under one name is either a re-cut set or a naming mistake, and both are
        /// things the person reading needs to know about.
        /// </summary>
        private static MatrixLine? Line(Dictionary<string, MatrixLine> lines,
                                        List<string> warnings, MatrixNameMatch? match, bool side,
                                        MatrixReadOptions how, string? set,
                                        IReadOnlyList<string> scopes, string test)
        {
            var source = !string.IsNullOrWhiteSpace(set)
                ? MatrixSource.SelectionSet(set!.Trim())
                : scopes != null && scopes.Count > 0
                    ? MatrixSource.Models(scopes)
                    : MatrixSource.Nothing;

            if (source.IsEmpty) return null;

            var said = match == null || !how.NamesWin
                ? string.Empty
                : Pick(match, side ? "row" : "column", side ? "rowTitle" : "columnTitle");

            var name = said.Length > 0 ? said : source.Describe();
            var key = name;

            if (lines.TryGetValue(key, out var found))
            {
                if (!string.Equals(found.Source.Fingerprint(), source.Fingerprint(),
                                   StringComparison.Ordinal)
                    && !warnings.Any(w => w.Contains(Quoted(name))))
                    warnings.Add(Quoted(name) + " is used for more than one saved set - "
                                 + Quoted(found.Source.Describe()) + " and "
                                 + Quoted(source.Describe())
                                 + ". The matrix keeps the first, so check the row points where "
                                 + "you mean.");

                return found;
            }

            var line = new MatrixLine(name,
                                      match?.Value(side ? "rowNumber" : "columnNumber"),
                                      source,
                                      match?.Value(side ? "rowCode" : "columnCode"))
            {
                Folder = match?.Value(side ? "rowFolder" : "columnFolder") ?? string.Empty,
            };

            lines[key] = line;

            return line;
        }

        /// <summary>The first of two tokens that caught anything.</summary>
        private static string Pick(MatrixNameMatch match, string first, string second)
        {
            var one = match.Value(first);

            return one.Length > 0 ? one : match.Value(second);
        }

        /// <summary>
        /// The symbol a test's cell carries.
        ///
        /// <b>The name's own mark when it has one.</b> A job whose tests are called "… (B)" has
        /// already written its matrix's legend into every test name; reading the kind and the
        /// tolerance off the host and lettering them A, B, C in the order they turn up produces a
        /// legend that is right and letters that are not the job's - so the sheet comes back
        /// looking like somebody else's matrix.
        ///
        /// What the mark means still comes from the host, because a tolerance in a name is a
        /// number somebody typed and a tolerance on a test is what will actually run.
        /// </summary>
        private static MatrixSymbol Symbol(Dictionary<string, MatrixSymbol> symbols,
                                           MatrixNameMatch? match, ClashTestSides shape)
        {
            var kind = MatrixDiff.Kinded(shape.Kind);
            var tolerance = shape.ToleranceMetres;
            var mark = (match?.Value("symbol") ?? string.Empty).Trim();

            var key = mark.Length > 0
                ? "m:" + mark
                : (int)kind + ":" + tolerance.ToString("0.######", CultureInfo.InvariantCulture);

            if (symbols.TryGetValue(key, out var found)) return found;

            var letter = mark.Length > 0
                ? mark
                : symbols.Count < 26
                    ? ((char)('A' + symbols.Count)).ToString()
                    : "S" + (symbols.Count + 1).ToString(CultureInfo.InvariantCulture);

            var symbol = new MatrixSymbol(letter, kind, tolerance);

            symbols[key] = symbol;

            return symbol;
        }

        private static string Quoted(string said) => "“" + said + "”";
    }
}
