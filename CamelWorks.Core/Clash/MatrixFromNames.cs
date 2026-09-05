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
        /// Whether a test that no pattern matches is read from its saved sets instead.
        ///
        /// Default true, so unmatched tests are not silently dropped. Set false to read only what
        /// the patterns cover, which is useful when checking a convention.
        /// </summary>
        public bool TakeUnmatched { get; set; } = true;

        /// <summary>
        /// Whether parsed names take precedence over set names.
        ///
        /// Default true. When false, the parse still supplies code, folder and symbol, but the
        /// row keeps the set's name.
        /// </summary>
        public bool NamesWin { get; set; } = true;

        /// <summary>
        /// Which pattern a test is to be read with, by host test id, overriding the automatic
        /// match. The value is a one-based index into the pattern list; 0 means the test is read
        /// from its saved sets whatever the patterns would have matched.
        ///
        /// The automatic match is a proposal. It is what the window shows before anybody touches
        /// it, and it is what an unassigned test still gets, but an entry here is the user's
        /// decision and wins. Without this, pattern order silently decided which of two patterns
        /// claimed a test, which is not something the person reading the board can see or change.
        ///
        /// An id with no matching test is ignored rather than reported: a stored assignment
        /// outliving the test it named is ordinary once a document has been edited.
        /// </summary>
        public IDictionary<string, int> Assigned { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Builds a matrix from clash tests, using name patterns to determine row and column
    /// identity rather than the saved sets alone.
    ///
    /// <see cref="MatrixFromModel"/> keys lines on the set each side points at, so rows are named
    /// after those sets. This keys lines on the name parsed out of the test, and takes the code,
    /// number, folder and symbol from the same parse. The saved set is still attached to the line,
    /// so building tests from the result works unchanged.
    /// </summary>
    public static class MatrixFromNames
    {
        /// <summary>Reads the given tests into a matrix.</summary>
        /// <param name="tests">The tests to read.</param>
        /// <param name="patterns">Name patterns, matched in order; first match wins.</param>
        /// <param name="options">Handling of unmatched tests and name precedence.</param>
        public static MatrixReading Read(IReadOnlyList<ClashTestRead>? tests,
                                         IReadOnlyList<MatrixNamePattern>? patterns,
                                         MatrixReadOptions? options = null)
        {
            var how = options ?? new MatrixReadOptions();
            var usable = (patterns ?? Array.Empty<MatrixNamePattern>())
                .Where(one => one != null && one.IsUsable).ToList();

            // With no usable patterns this is the set-based read, unchanged.
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
                var match = Chosen(usable, how, test.Test.Id, name, out var byHand, out var refused);

                if (refused != null) warnings.Add(refused);

                if (match == null)
                {
                    // A test the user put on no pattern is a decision, not a gap, so it is read
                    // from its sets without being counted against the patterns.
                    if (!byHand) unmatched++;

                    if (!how.TakeUnmatched && !byHand) continue;
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

                // Both halves, as the matrix is mirrored.
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

        /// <summary>
        /// The parse to read a test with: the user's choice where they made one, the automatic
        /// match otherwise.
        ///
        /// An assignment names a pattern by its position in the list, which is what the window
        /// shows beside each test. Zero means the user put the test on no pattern, so it is read
        /// from its saved sets and is not counted as unmatched — that is an answer rather than a
        /// gap, and reporting it as one would send somebody looking for a pattern to write.
        ///
        /// A pattern that cannot parse the name it was assigned to is reported against that test
        /// rather than silently falling through to another pattern. Falling through would undo
        /// the assignment, which is the behaviour this exists to remove.
        /// </summary>
        /// <param name="usable">The patterns, in the order the window lists them.</param>
        /// <param name="how">The options carrying the assignments.</param>
        /// <param name="id">The host's id for the test.</param>
        /// <param name="name">The test's name.</param>
        /// <param name="byHand">True when the user assigned this test.</param>
        /// <param name="refused">What to warn about, or null.</param>
        private static MatrixNameMatch? Chosen(IReadOnlyList<MatrixNamePattern> usable,
                                               MatrixReadOptions how, string? id, string name,
                                               out bool byHand, out string? refused)
        {
            byHand = false;
            refused = null;

            if (!string.IsNullOrEmpty(id) && how.Assigned.TryGetValue(id!, out var wanted))
            {
                byHand = true;

                if (wanted <= 0 || wanted > usable.Count) return null;

                var said = usable[wanted - 1].Match(name);

                if (said == null)
                    refused = Quoted(name) + " is assigned to pattern "
                              + wanted.ToString(CultureInfo.InvariantCulture)
                              + ", which its name does not fit, so it was read from what it is "
                              + "between instead.";

                return said;
            }

            return usable.Select(one => one.Match(name)).FirstOrDefault(one => one != null);
        }

        /// <summary>The column corresponding to one side of a pair.</summary>
        private static MatrixLine? Matching(IReadOnlyList<MatrixLine> columns,
                                            MatrixLine one, MatrixLine other) =>
            columns.FirstOrDefault(c => string.Equals(c.Name, other.Name, StringComparison.Ordinal));

        /// <summary>
        /// One side of a test as a matrix line, keyed on the parsed name rather than the set, so
        /// several tests naming the same discipline produce one line.
        ///
        /// The first set encountered is attached to the line. A second, different set under the
        /// same name is reported as a warning rather than dropped silently.
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

        /// <summary>The first of two tokens with a non-empty value.</summary>
        private static string Pick(MatrixNameMatch match, string first, string second)
        {
            var one = match.Value(first);

            return one.Length > 0 ? one : match.Value(second);
        }

        /// <summary>
        /// The symbol for a test's cell.
        ///
        /// Uses the symbol parsed from the name when the pattern captures one, so the legend
        /// matches the project's own marks. Falls back to lettering distinct kind/tolerance pairs
        /// A, B, C in order of appearance. The kind and tolerance always come from the test
        /// itself, not from the name.
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
