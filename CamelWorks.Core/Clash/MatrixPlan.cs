using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>One test the board asks for.</summary>
    public sealed class MatrixTestPlan
    {
        internal MatrixTestPlan(MatrixLine row, MatrixLine column, string name, MatrixSymbol symbol)
        {
            Row = row; Column = column; Name = name; Symbol = symbol;
        }

        /// <summary>The row, which is side A.</summary>
        public MatrixLine Row { get; }

        /// <summary>The column, which is side B.</summary>
        public MatrixLine Column { get; }

        /// <summary>What the test is called.</summary>
        public string Name { get; }

        /// <summary>What the cell's symbol asked for.</summary>
        public MatrixSymbol Symbol { get; }

        /// <summary>
        /// What makes this the same test as one already in the document, whatever it is called.
        ///
        /// The two sources and the kind of test, with the sources in a settled order so a cell
        /// and its mirror answer alike. Deliberately not the name: a rename is not a new test,
        /// and a diff that thought it was would delete and remake every test on the job the
        /// first time somebody changed the naming pattern.
        /// </summary>
        public string Pairing() => Sides() + "\u001f" + (int)Symbol.Test;

        /// <summary>
        /// What this test is between, and nothing else.
        ///
        /// The kind is not part of what a test is between. A pair tested hard and the same pair
        /// tested as a clearance are the same two things being checked, so a legend changed from one
        /// to the other has to find the existing test and alter it. Folded into the fingerprint, as
        /// it was, the old test matched nothing and stayed in the document untouched with weeks of
        /// statuses on it while a second test was made beside it.
        ///
        /// The pairing above still carries the kind, because two cells asking for a hard test and a
        /// clearance between the same two sets are two tests that were intended.
        /// </summary>
        public string Sides() => Both(Row.Source.Matched(), Column.Source.Matched());

        /// <summary>
        /// What this test is between and how, identified exactly rather than as the document would
        /// report it.
        ///
        /// Two different questions. Matching a plan against a test the document holds has to happen
        /// on what the document can report, which for a set is its name rather than its folder.
        /// Telling two cells of the same matrix apart has to happen on everything known about them,
        /// or two rows pointed at two different sets that share a name become one row and the second
        /// one's tests are dropped as duplicates.
        /// </summary>
        public string Exact() =>
            Both(Row.Source.Fingerprint(), Column.Source.Fingerprint())
            + "\u001f" + (int)Symbol.Test;

        /// <summary>Two sides in a fixed order, so a pair reads the same from either end.</summary>
        /// <param name="one">One side.</param>
        /// <param name="two">The other.</param>
        private static string Both(string one, string two) =>
            string.CompareOrdinal(one, two) <= 0
                ? one + "\u001f" + two
                : two + "\u001f" + one;

        /// <summary>What to hand the host.</summary>
        /// <param name="folder">Which clash folder to put it in, or null for the root.</param>
        public ClashTestSpec Spec(string? folder = null) =>
            new ClashTestSpec(Name, Row.Source.AsSide(), Column.Source.AsSide(),
                              Symbol.Tolerance, folder, Symbol.Test);

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>Why one cell could not become a test.</summary>
    public sealed class MatrixSkip
    {
        internal MatrixSkip(string cell, string why) { Cell = cell; Why = why; }

        /// <summary>Which cell, as a person reads it.</summary>
        public string Cell { get; }

        /// <summary>What is wrong with it.</summary>
        public string Why { get; }

        /// <inheritdoc />
        public override string ToString() => Cell + " - " + Why;
    }

    /// <summary>Every test a board asks for, and every cell that could not ask for one.</summary>
    public sealed class MatrixPlan
    {
        internal MatrixPlan(IReadOnlyList<MatrixTestPlan> tests, IReadOnlyList<MatrixSkip> skipped)
        {
            Tests = tests; Skipped = skipped;
        }

        /// <summary>The tests, in reading order.</summary>
        public IReadOnlyList<MatrixTestPlan> Tests { get; }

        /// <summary>The cells that could not become one, and why.</summary>
        public IReadOnlyList<MatrixSkip> Skipped { get; }

        /// <summary>What this reads as under the grid.</summary>
        public string Explain() =>
            Tests.Count.ToString(CultureInfo.InvariantCulture)
            + (Tests.Count == 1 ? " test" : " tests")
            + (Skipped.Count == 0
                ? string.Empty
                : ", " + Skipped.Count.ToString(CultureInfo.InvariantCulture) + " cell"
                  + (Skipped.Count == 1 ? string.Empty : "s") + " skipped");
    }

    /// <summary>Turns a board and its legend into the tests they ask for.</summary>
    public static class MatrixPlanner
    {
        /// <summary>
        /// Plan every test the board asks for.
        ///
        /// Two cells can name one test. Where the rows and the columns hold the same items — the
        /// square matrix — the cell at (Ducts, Steel) and the cell at (Steel, Ducts) name one test,
        /// and making both would give the job two identical tests with two names and two sets of
        /// results to review. The first one reached wins and the second is skipped with a note,
        /// rather than silently dropped.
        ///
        /// A cell against itself is a real test — it finds the duct through its own hanger — so it
        /// is planned like any other.
        /// </summary>
        /// <param name="board">The grid.</param>
        /// <param name="legend">What each symbol means.</param>
        /// <param name="pattern">The naming pattern, or null for the default.</param>
        public static MatrixPlan Plan(MatrixBoard board, MatrixLegend legend, string? pattern = null)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (legend == null) throw new ArgumentNullException(nameof(legend));

            var tests = new List<MatrixTestPlan>();
            var skipped = new List<MatrixSkip>();

            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paired = new HashSet<string>(StringComparer.Ordinal);

            foreach (var cell in board.Cells())
            {
                var where = cell.Row.Titled + " x " + cell.Column.Titled;

                // A pair ruled out on purpose is not a test and is not a fault either. It is the
                // one mark in the grid that means "no test here", said deliberately, so it
                // passes through without a word.
                if (MatrixBoard.IsNotRequired(cell.Symbol)) continue;

                var meaning = legend.Meaning(cell.Symbol);

                if (meaning == null)
                {
                    skipped.Add(new MatrixSkip(where,
                        "the legend does not say what " + Quoted(cell.Symbol) + " means"));
                    continue;
                }

                if (!meaning.Enabled)
                {
                    skipped.Add(new MatrixSkip(where,
                        Quoted(meaning.Symbol) + " is in the legend but turned off"));
                    continue;
                }

                if (cell.Row.IsUnbacked || cell.Column.IsUnbacked)
                {
                    skipped.Add(new MatrixSkip(where,
                        (cell.Row.IsUnbacked ? cell.Row.Titled : cell.Column.Titled)
                        + " is not pointed at anything yet"));
                    continue;
                }

                var plan = new MatrixTestPlan(cell.Row, cell.Column, string.Empty, meaning);

                if (!paired.Add(plan.Exact()))
                {
                    skipped.Add(new MatrixSkip(where,
                        "the same pair is already tested by another cell"));
                    continue;
                }

                var named = MatrixNaming.Distinct(
                    MatrixNaming.Fill(pattern, cell.Row, cell.Column, meaning, tests.Count + 1),
                    taken);

                tests.Add(new MatrixTestPlan(cell.Row, cell.Column, named, meaning));
            }

            return new MatrixPlan(tests, skipped);
        }

        private static string Quoted(string said) => "\u201c" + said + "\u201d";
    }
}
