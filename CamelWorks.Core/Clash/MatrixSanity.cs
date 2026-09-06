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
        /// Everything wrong with a matrix, reported before anything is built.
        ///
        /// The main case is a set that no longer exists. A matrix is drawn once and used for months
        /// while the federation moves under it: sets get renamed, models get swapped, a package gets
        /// re-cut. A test built on a set that no longer exists runs and finds nothing, which reads
        /// as a discipline with no clashes. So the names are checked against what the model has,
        /// before the build, as a list lookup rather than a walk.
        ///
        /// A capture is checked differently: it cannot go missing, because it is a list of elements
        /// the matrix carries itself. It can be stale, and that is reported rather than treated as a
        /// fault.
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

                if (source.IsSet && known.Count > 0)
                {
                    var trail = source.Folder.Length == 0
                        ? source.Name
                        : source.Folder + "/" + source.Name;

                    if (!known.Contains(trail))
                    {
                        // Distinguish moved from missing. A set dragged into a package folder
                        // still exists and still has its name, but no longer answers to where this
                        // row was pointed. Reporting "there is no set called MEP" while a set called
                        // MEP is on screen is not credible; reporting where it went is one press to
                        // fix.
                        faults.Add(new MatrixFault(line.Where,
                            known.Contains(source.Name)
                                ? "points at " + Quoted(trail) + ", and the set called "
                                  + Quoted(source.Name) + " is somewhere else in this model now. "
                                  + "Point it at the set again."
                                : "points at a set called " + Quoted(source.Name)
                                  + ", and this model has no set by that name",
                            true));

                        continue;
                    }
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

            // Say what each row is, next to its name. Two rows called "Ductwork" on two different
            // sets exported identically, so a sheet issued to a client did not say what any of it
            // was checking — which is the question asked about a matrix weeks later, once the sets
            // have been re-cut.
            var headings = new List<string?> { "#", "Row", "Points at" };

            foreach (var column in board.Columns) headings.Add(column.Titled);

            sheet.Row(headings.ToArray());

            foreach (var row in board.Rows)
            {
                var cells = new List<string?>
                {
                    row.Number,
                    row.Name,
                    row.Source.IsEmpty ? "nothing" : row.Source.Kinded() + ": " + row.Source.Describe(),
                };

                foreach (var column in board.Columns)
                    cells.Add(board.At(row, column) ?? string.Empty);

                sheet.Row(cells.ToArray());
            }

            var ruled = false;
            var open = false;
            var unexplained = new List<string>();

            foreach (var row in board.Rows)
            foreach (var column in board.Columns)
            {
                var held = board.At(row, column);

                if (held == null)
                {
                    // A symbol on the diagonal is a test somebody asked for; an empty diagonal
                    // cell is not a gap in the coordination.
                    if (board.IsKeptHalf(row, column)
                        && (!board.IsSelfPair(row, column) || board.At(row, column) != null))
                        open = true;
                    continue;
                }

                if (MatrixBoard.IsNotRequired(held)) { ruled = true; continue; }

                if (legend?.Meaning(held) == null
                    && !unexplained.Contains(held, StringComparer.OrdinalIgnoreCase))
                    unexplained.Add(held);
            }

            sheet.Row();
            sheet.Row("Legend");

            // The reserved mark, said in words, because a sheet issued to a client is where the
            // difference between "agreed not required" and "nobody got to it" matters most and
            // where nobody can hover a cell to find out.
            if (ruled)
                sheet.Row(MatrixBoard.NotRequired, "Not required, agreed - no test is made");

            // Spell the blank out as well as the dash. The dash exists because blank meant two
            // opposite things, and a sheet that explains the dash while leaving the blank
            // unexplained restores the ambiguity.
            if (open)
                sheet.Row("(blank)", "Undecided - no test is made");

            foreach (var symbol in legend?.Symbols ?? Array.Empty<MatrixSymbol>())
                sheet.Row(symbol.Symbol,
                          MatrixNaming.Worded(symbol.Test)
                          + " at " + (symbol.Tolerance * 1000).ToString("0.###", CultureInfo.InvariantCulture)
                          + " mm" + (symbol.Enabled ? string.Empty : ", off - no test is made"));

            // A symbol the legend has nothing to say about is flagged on the screen and by the
            // sanity check, and used to leave the sheet as a bare letter with no key line.
            foreach (var symbol in unexplained)
                sheet.Row(symbol, "Not in the legend - no test is made for it");

            sheet.Row();
            sheet.Row("The matrix is mirrored: a pair reads the same either way round, and is "
                      + "tested once.");

            return sheet;
        }

        private static string Quoted(string said) => "\u201c" + said + "\u201d";
    }
}
