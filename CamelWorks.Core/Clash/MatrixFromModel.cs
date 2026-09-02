using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>A matrix read back out of the document, with anything that did not fit.</summary>
    public sealed class MatrixReading
    {
        internal MatrixReading(MatrixBoard board, MatrixLegend legend,
                               IReadOnlyList<string> warnings, int tests)
        {
            Board = board; Legend = legend; Warnings = warnings; Tests = tests;
        }

        /// <summary>The grid.</summary>
        public MatrixBoard Board { get; }

        /// <summary>The legend, built from the kinds and tolerances the tests actually use.</summary>
        public MatrixLegend Legend { get; }

        /// <summary>Everything the read could not do cleanly.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>How many tests became cells.</summary>
        public int Tests { get; }

        /// <summary>True when at least one test came out.</summary>
        public bool IsUsable => Tests > 0;

        /// <summary>One line saying what happened.</summary>
        public string Explain() =>
            Tests == 0
                ? "No test in this document says what it is between, so there is no matrix to read."
                : Tests.ToString(CultureInfo.InvariantCulture)
                  + (Tests == 1 ? " test read into " : " tests read into ")
                  + Board.Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows and "
                  + Board.Columns.Count.ToString(CultureInfo.InvariantCulture) + " columns"
                  + (Warnings.Count == 0 ? "." : ", with "
                     + Warnings.Count.ToString(CultureInfo.InvariantCulture)
                     + (Warnings.Count == 1 ? " thing" : " things") + " worth a look.");
    }

    /// <summary>Builds a matrix out of the tests a document already holds.</summary>
    public static class MatrixFromModel
    {
        /// <summary>
        /// Read the document's tests as a matrix.
        ///
        /// <b>Every test carries what it is between</b> - a saved set on each side, or a list of
        /// models - so the matrix is already in the document and nobody has to redraw it. Each
        /// distinct source becomes a row and a column, the pair becomes a cell, and the legend is
        /// built from what the tests themselves say: one entry per distinct kind and tolerance, so
        /// a job running hard at 10 mm and clearance at 50 mm comes back with two symbols rather
        /// than a legend somebody has to reconstruct.
        ///
        /// <b>Square, because a read has nothing to say about which axis is which.</b> Both axes
        /// hold every source found. Splitting them afterwards is the editor's job.
        /// </summary>
        /// <param name="tests">The document's tests, with what each is between.</param>
        public static MatrixReading Read(IReadOnlyList<ClashTestRead> tests)
        {
            var board = new MatrixBoard();
            var warnings = new List<string>();

            var found = tests ?? Array.Empty<ClashTestRead>();

            // One line per distinct source, in the order the tests first name them, so a matrix
            // read out of a job that was set up in a sensible order comes back in that order.
            var lines = new Dictionary<string, MatrixLine>(StringComparer.Ordinal);
            var symbols = new Dictionary<string, MatrixSymbol>(StringComparer.Ordinal);
            var pairs = new List<(MatrixLine A, MatrixLine B, string Symbol)>();

            var read = 0;

            foreach (var test in found)
            {
                if (test.Shape == null || !test.Shape.KnowsA || !test.Shape.KnowsB)
                {
                    warnings.Add(Quoted(test.Test.Name)
                        + " does not say what it is between, so it is not on the matrix.");
                    continue;
                }

                var a = Line(lines, test.Shape.SetA, test.Shape.ScopesA);
                var b = Line(lines, test.Shape.SetB, test.Shape.ScopesB);

                if (a == null || b == null) continue;

                var symbol = Symbol(symbols, MatrixDiff.Kinded(test.Shape.Kind),
                                    test.Shape.ToleranceMetres);

                pairs.Add((a, b, symbol.Symbol));
                read++;
            }

            foreach (var line in lines.Values) board.AddRow(line);

            board.SquareOffRows();

            foreach (var pair in pairs)
            {
                var row = board.Rows.FirstOrDefault(r => ReferenceEquals(r, pair.A));
                var column = Matching(board.Columns, pair.B);

                board.Set(row, column, pair.Symbol);

                // The mirror too, so a square matrix read out of the model looks like the square
                // matrix somebody drew - both halves filled, the way every clash matrix is.
                board.Set(board.Rows.FirstOrDefault(r => ReferenceEquals(r, pair.B)),
                          Matching(board.Columns, pair.A), pair.Symbol);
            }

            return new MatrixReading(board,
                new MatrixLegend(symbols.Values.OrderBy(s => s.Symbol, StringComparer.Ordinal)),
                warnings, read);
        }

        /// <summary>The column standing for the same thing as a row.</summary>
        private static MatrixLine? Matching(IReadOnlyList<MatrixLine> columns, MatrixLine row) =>
            columns.FirstOrDefault(c => string.Equals(c.Name, row.Name, StringComparison.Ordinal));

        private static MatrixLine? Line(Dictionary<string, MatrixLine> lines,
                                        string? set, IReadOnlyList<string> scopes)
        {
            var source = !string.IsNullOrWhiteSpace(set)
                ? MatrixSource.SelectionSet(set!.Trim())
                : scopes != null && scopes.Count > 0
                    ? MatrixSource.Models(scopes)
                    : MatrixSource.Nothing;

            if (source.IsEmpty) return null;

            var key = source.Fingerprint();

            if (lines.TryGetValue(key, out var found)) return found;

            var line = new MatrixLine(source.Describe(), null, source);

            lines[key] = line;
            return line;
        }

        /// <summary>
        /// A symbol for one kind and tolerance, made the first time that pair is seen.
        ///
        /// Lettered A, B, C in the order they turn up, because that is what a clash matrix looks
        /// like and what the key in the corner of one says. The legend that comes back is
        /// therefore the job's own settings, already written down.
        /// </summary>
        private static MatrixSymbol Symbol(Dictionary<string, MatrixSymbol> symbols,
                                           MatrixTest test, double tolerance)
        {
            var key = (int)test + ":" + tolerance.ToString("0.######", CultureInfo.InvariantCulture);

            if (symbols.TryGetValue(key, out var found)) return found;

            var letter = symbols.Count < 26
                ? ((char)('A' + symbols.Count)).ToString()
                : "S" + (symbols.Count + 1).ToString(CultureInfo.InvariantCulture);

            var symbol = new MatrixSymbol(letter, test, tolerance);

            symbols[key] = symbol;
            return symbol;
        }

        private static string Quoted(string said) => "\u201c" + said + "\u201d";
    }
}
