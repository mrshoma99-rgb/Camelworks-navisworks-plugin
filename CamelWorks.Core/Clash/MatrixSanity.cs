using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Report;

namespace CamelWorks.Core.Clash
{
    /// <summary>One thing wrong with a matrix, found before anybody tries to build it.</summary>
    public sealed class MatrixFault
    {
        internal MatrixFault(string where, string what, bool stops)
        {
            Where = where; What = what; Stops = stops;
        }

        /// <summary>Which row, column or cell.</summary>
        public string Where { get; }

        /// <summary>What is wrong with it.</summary>
        public string What { get; }

        /// <summary>True when no test can be made from it, false when it is only worth knowing.</summary>
        public bool Stops { get; }

        /// <inheritdoc />
        public override string ToString() => Where + " - " + What;
    }

    /// <summary>Checks a matrix against the model before it is turned into tests.</summary>
    public static class MatrixSanity
    {
        /// <summary>
        /// Everything wrong with a matrix, said before anything is built.
        ///
        /// <b>The set that is not there any more.</b> A matrix is drawn once and lived with for
        /// months while the federation moves under it: sets get renamed, models get swapped, a
        /// package gets re-cut. A test built on a set that no longer exists is a test that runs
        /// and finds nothing, which reads exactly like a discipline with no clashes - the single
        /// most dangerous thing a clash matrix can say. So the names are checked against what the
        /// model actually has, before the build, and the check is a list lookup rather than a
        /// walk of anything.
        ///
        /// A capture is checked differently: it cannot go missing, because it is a list of
        /// elements the matrix is carrying itself. What it can be is stale, and that is said
        /// rather than treated as a fault.
        /// </summary>
        /// <param name="board">The grid.</param>
        /// <param name="legend">What each symbol means.</param>
        /// <param name="sets">Every saved set the model has, by name.</param>
        /// <param name="models">Every loaded model file name.</param>
        public static IReadOnlyList<MatrixFault> Check(
            MatrixBoard board, MatrixLegend legend,
            IReadOnlyCollection<string>? sets, IReadOnlyCollection<string>? models)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            var faults = new List<MatrixFault>();

            var known = new HashSet<string>(sets ?? Array.Empty<string>(),
                                            StringComparer.OrdinalIgnoreCase);
            var loaded = new HashSet<string>(models ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);

            foreach (var line in board.Rows.Select(r => (Line: r, Where: "Row " + r.Titled))
                         .Concat(board.Columns.Select(c => (Line: c, Where: "Column " + c.Titled))))
            {
                var source = line.Line.Source;

                if (source.IsEmpty)
                {
                    faults.Add(new MatrixFault(line.Where, "is not pointed at anything yet", true));
                    continue;
                }

                if (source.IsSet && known.Count > 0 && !known.Contains(source.Name))
                {
                    faults.Add(new MatrixFault(line.Where,
                        "points at a set called " + Quoted(source.Name)
                        + ", and this model has no set by that name", true));
                    continue;
                }

                if (source.Kind == MatrixSourceKind.Models && loaded.Count > 0)
                {
                    var missing = source.Scopes.Where(s => !loaded.Contains(s)).ToList();

                    if (missing.Count > 0)
                        faults.Add(new MatrixFault(line.Where,
                            (missing.Count == 1 ? "names a model that is not loaded: "
                                                : "names models that are not loaded: ")
                            + string.Join(", ", missing.Take(3)), true));
                }

                if (source.Capturing)
                    faults.Add(new MatrixFault(line.Where,
                        "holds " + source.Keys.Count.ToString(CultureInfo.InvariantCulture)
                        + " elements captured when it was set up, so anything modelled since is "
                        + "not in it", false));
            }

            if (legend != null)
                foreach (var symbol in legend.Unexplained(board))
                    faults.Add(new MatrixFault("Symbol " + Quoted(symbol),
                        "is in the grid but not in the legend, so its cells make no test", true));

            return faults;
        }

        /// <summary>How many of the faults would stop a test being made.</summary>
        /// <param name="faults">The faults.</param>
        public static int Stopping(IReadOnlyList<MatrixFault>? faults) =>
            faults == null ? 0 : faults.Count(f => f.Stops);

        /// <summary>
        /// The matrix as a sheet, laid out the way a clash matrix is drawn.
        ///
        /// Number and name down the left, number and name across the top, the symbol where they
        /// meet, and the legend underneath - which is what makes it a document somebody can send
        /// to a client rather than a dump of our own table.
        /// </summary>
        /// <param name="board">The grid.</param>
        /// <param name="legend">What each symbol means.</param>
        public static XlsxSheet AsSheet(MatrixBoard board, MatrixLegend? legend)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));

            var sheet = new XlsxSheet("Clash matrix");

            var headings = new List<string?> { "#", "Row" };

            foreach (var column in board.Columns) headings.Add(column.Titled);

            sheet.Row(headings.ToArray());

            foreach (var row in board.Rows)
            {
                var cells = new List<string?> { row.Number, row.Name };

                foreach (var column in board.Columns)
                    cells.Add(board.At(row, column) ?? string.Empty);

                sheet.Row(cells.ToArray());
            }

            if (legend == null || legend.IsEmpty) return sheet;

            sheet.Row();
            sheet.Row("Legend");

            foreach (var symbol in legend.Symbols)
                sheet.Row(symbol.Symbol,
                          MatrixNaming.Worded(symbol.Test)
                          + " at " + (symbol.Tolerance * 1000).ToString("0.###", CultureInfo.InvariantCulture)
                          + " mm" + (symbol.Enabled ? string.Empty : ", off"));

            return sheet;
        }

        private static string Quoted(string said) => "\u201c" + said + "\u201d";
    }
}
