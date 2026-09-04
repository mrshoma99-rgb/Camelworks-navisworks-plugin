using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// Where the matrix sits inside a worksheet.
    ///
    /// Every number is 1-based, as Excel shows them, because the person filling this in is reading
    /// the row and column headings off their own screen. Converting in their head to make a tool
    /// happy is exactly the tax this is meant to remove.
    ///
    /// Nobody's matrix is laid out like anybody else's. They arrive with a title block, a merged
    /// heading row, a spacer column between the labels and the grid, and a key in the corner. The
    /// answer is not a template they have to reformat their sheet into; it is telling the tool
    /// where things already are.
    /// </summary>
    public sealed class SheetMapping
    {
        /// <summary>The sheet's name. Empty means the first one.</summary>
        public string SheetName { get; set; } = string.Empty;

        /// <summary>Column holding each category's number, or 0 when the sheet has none.</summary>
        public int NumberColumn { get; set; } = 2;

        /// <summary>Column holding each category's discipline code, or 0 for none.</summary>
        public int CodeColumn { get; set; } = 3;

        /// <summary>Column holding each category's name.</summary>
        public int NameColumn { get; set; } = 5;

        /// <summary>Column holding the saved set that backs each category.</summary>
        public int SetColumn { get; set; } = 6;

        /// <summary>First row of the category list.</summary>
        public int FirstRow { get; set; } = 7;

        /// <summary>Row repeating the numbers across the top, or 0 when there is none.</summary>
        public int HeaderNumberRow { get; set; } = 2;

        /// <summary>Row repeating the set names across the top, or 0 when there is none.</summary>
        public int HeaderSetRow { get; set; } = 6;

        /// <summary>First column of the grid body.</summary>
        public int FirstBodyColumn { get; set; } = 7;

        /// <summary>How many empty rows in a row end the category list.</summary>
        public int BlankRowStop { get; set; } = 3;
    }

    /// <summary>What came out of a sheet, and everything that did not quite fit.</summary>
    public sealed class SheetMatrixResult
    {
        internal SheetMatrixResult(MatrixBoard board, IReadOnlyList<string> warnings, int cells)
        {
            Grid = board;
            Warnings = warnings;
            Cells = cells;
        }

        /// <summary>The matrix.</summary>
        public MatrixBoard Grid { get; }

        /// <summary>
        /// Everything the read could not do cleanly, in the order it hit them.
        ///
        /// Never an exception and never silence. A matrix that read nine of eleven columns and said
        /// so is useful; one that threw is not, and one that quietly dropped two is worse than
        /// either, because the tests it then builds look complete.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>How many cells carried a symbol.</summary>
        public int Cells { get; }

        /// <summary>True when at least one pair came out.</summary>
        public bool IsUsable => Grid.Rows.Count > 0 && Cells > 0;

        /// <summary>One line saying what happened.</summary>
        public string Explain()
        {
            if (Grid.Rows.Count == 0) return "No categories were found where the mapping says they are.";

            var line = Grid.Rows.Count.ToString(CultureInfo.InvariantCulture) + " categories and "
                       + Cells.ToString(CultureInfo.InvariantCulture) + " filled cells";

            if (Warnings.Count > 0)
                line += ", with " + Warnings.Count.ToString(CultureInfo.InvariantCulture)
                        + (Warnings.Count == 1 ? " thing" : " things") + " worth a look";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Reads a matrix out of a spreadsheet somebody already has.
    ///
    /// The categories are read DOWN the row headers, and then the body columns are matched BACK to
    /// them across the top — by number first, and by set name where there are no numbers. That is
    /// the part that matters: matching by position assumes the first body column is the first
    /// category, which is wrong the moment there is a spacer column, a repeated heading or a merged
    /// cell. Matching by content skips whatever is in between without being told it is there.
    /// </summary>
    public static class SheetMatrix
    {
        /// <summary>The top-left of a sheet as plain text, so the mapping can be filled in by looking.</summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="rows">How many rows to show.</param>
        /// <param name="columns">How many columns to show.</param>
        public static IReadOnlyList<IReadOnlyList<string>> Preview(SheetTable? sheet, int rows = 14, int columns = 14)
        {
            var grid = new List<IReadOnlyList<string>>();
            if (sheet == null) return grid;

            for (var r = 0; r < rows; r++)
            {
                var line = new List<string>();

                for (var c = 0; c < columns; c++)
                    line.Add(Text(sheet, r + 1, c + 1) ?? string.Empty);

                grid.Add(line);
            }

            return grid;
        }

        /// <summary>Read a matrix.</summary>
        /// <param name="sheet">The worksheet.</param>
        /// <param name="mapping">Where things are in it.</param>
        public static SheetMatrixResult Read(SheetTable? sheet, SheetMapping? mapping)
        {
            var map = mapping ?? new SheetMapping();
            var warnings = new List<string>();

            if (sheet == null)
            {
                warnings.Add("There is no sheet by that name in the workbook.");
                return new SheetMatrixResult(new MatrixBoard(), warnings, 0);
            }

            var categories = ReadCategories(sheet, map, warnings);

            // A sheet is the square case: the same categories down the side and across the top,
            // so the board is filled from one list and squared off. Rows and columns can diverge
            // afterwards - that is what the editor is for - but nothing in a spreadsheet says
            // they should start apart.
            var grid = new MatrixBoard();

            foreach (var category in categories) grid.AddRow(category);

            grid.SquareOffRows();

            if (categories.Count == 0)
            {
                warnings.Add("No category names were found from row "
                             + map.FirstRow.ToString(CultureInfo.InvariantCulture) + " down column "
                             + map.NameColumn.ToString(CultureInfo.InvariantCulture) + ".");
                return new SheetMatrixResult(grid, warnings, 0);
            }

            var columns = MatchColumns(sheet, map, categories, warnings);
            var cells = 0;

            for (var row = 0; row < categories.Count; row++)
            {
                var sheetRow = map.FirstRow + row;

                foreach (var column in columns)
                {
                    var symbol = (Text(sheet, sheetRow, column.Key) ?? string.Empty).Trim();
                    if (symbol.Length == 0) continue;

                    // <b>What a paper matrix has always used for "not required".</b> A client's
                    // workbook says it with a dash of whichever kind their font gave them, or
                    // with the letters, and every one of them means the same thing: this pair
                    // was ruled out, not overlooked. Read as anything else it becomes a symbol
                    // with no meaning, and the reader is asked to give a tolerance to a
                    // decision not to test.
                    if (Ruled(symbol)) symbol = MatrixBoard.NotRequired;

                    // The mirrored cell may already hold something. A sheet filled in on both sides
                    // of the diagonal usually agrees with itself; when it does not, the reader is
                    // told rather than one of the two silently winning.
                    var already = grid.At(grid.Rows[row], grid.Columns[column.Value]);

                    if (already != null && !string.Equals(already, symbol, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add(categories[row].Name + " against " + categories[column.Value].Name
                                     + " says \"" + already + "\" one way and \"" + symbol
                                     + "\" the other. The first one read was kept.");
                        continue;
                    }

                    if (already == null) cells++;

                    // Both halves, because the board no longer mirrors for us: a sheet says one
                    // thing about a pair, and both cells that name that pair have to show it.
                    grid.Set(grid.Rows[row], grid.Columns[column.Value], symbol);
                    grid.Set(grid.Rows[column.Value], grid.Columns[row], symbol);
                }
            }

            return new SheetMatrixResult(grid, warnings, cells);
        }

        private static List<MatrixLine> ReadCategories(SheetTable sheet, SheetMapping map,
                                                           List<string> warnings)
        {
            var categories = new List<MatrixLine>();
            var blanks = 0;
            var row = map.FirstRow;

            while (blanks < Math.Max(1, map.BlankRowStop) && row <= sheet.Rows.Count + map.BlankRowStop)
            {
                var name = (Text(sheet, row, map.NameColumn) ?? string.Empty).Trim();

                if (name.Length == 0) { blanks++; row++; continue; }

                blanks = 0;

                var set = (Text(sheet, row, map.SetColumn) ?? string.Empty).Trim();

                if (set.Length == 0)
                    warnings.Add("\"" + name + "\" names no saved set, so no test on it can run.");

                categories.Add(new MatrixLine(
                    name,
                    Text(sheet, row, map.NumberColumn),
                    set.Length == 0 ? MatrixSource.Nothing : MatrixSource.SelectionSet(set),
                    Text(sheet, row, map.CodeColumn)));
                row++;
            }

            return categories;
        }

        /// <summary>Body column number to the category index it belongs to.</summary>
        private static Dictionary<int, int> MatchColumns(SheetTable sheet, SheetMapping map,
                                                         List<MatrixLine> categories,
                                                         List<string> warnings)
        {
            var found = new Dictionary<int, int>();
            var claimed = new HashSet<int>();

            var last = Math.Max(sheet.Columns, map.FirstBodyColumn + categories.Count * 2);

            for (var column = map.FirstBodyColumn; column <= last; column++)
            {
                var number = map.HeaderNumberRow > 0
                    ? (Text(sheet, map.HeaderNumberRow, column) ?? string.Empty).Trim()
                    : string.Empty;

                var set = map.HeaderSetRow > 0
                    ? (Text(sheet, map.HeaderSetRow, column) ?? string.Empty).Trim()
                    : string.Empty;

                if (number.Length == 0 && set.Length == 0) continue;

                var index = Index(categories, number, set);

                if (index < 0)
                {
                    warnings.Add("Column " + column.ToString(CultureInfo.InvariantCulture) + " is headed \""
                                 + (number.Length > 0 ? number : set)
                                 + "\", which matches no category down the side. It was skipped.");
                    continue;
                }

                if (!claimed.Add(index))
                {
                    warnings.Add("Column " + column.ToString(CultureInfo.InvariantCulture) + " repeats "
                                 + categories[index].Name + ", which an earlier column already claimed.");
                    continue;
                }

                found[column] = index;
            }

            var missing = categories.Count - claimed.Count;

            if (missing > 0)
                warnings.Add(missing.ToString(CultureInfo.InvariantCulture)
                             + (missing == 1 ? " category has" : " categories have")
                             + " no column across the top, so nothing was read for "
                             + (missing == 1 ? "it" : "them") + ".");

            return found;
        }

        /// <summary>By number first, then by set name. Never by position.</summary>
        private static int Index(List<MatrixLine> categories, string number, string set)
        {
            if (number.Length > 0)
            {
                var byNumber = categories.FindIndex(
                    c => c.Number.Length > 0 && string.Equals(c.Number, number, StringComparison.OrdinalIgnoreCase));

                if (byNumber >= 0) return byNumber;
            }

            if (set.Length > 0)
            {
                var bySet = categories.FindIndex(
                    c => c.Source.Name.Length > 0
                         && string.Equals(c.Source.Name, set, StringComparison.OrdinalIgnoreCase));

                if (bySet >= 0) return bySet;
            }

            return -1;
        }

        /// <summary>One cell by 1-based row and column, or null past the edge.</summary>
        /// <summary>Whether a cell of a workbook says "we agreed not to check this pair".</summary>
        /// <param name="said">What the cell held, trimmed.</param>
        private static bool Ruled(string said) =>
            said == "-" || said == "\u2013" || said == "\u2014"
            || string.Equals(said, "n/a", StringComparison.OrdinalIgnoreCase)
            || string.Equals(said, "na", StringComparison.OrdinalIgnoreCase);

        private static string? Text(SheetTable sheet, int row, int column)
        {
            if (row <= 0 || column <= 0 || row > sheet.Rows.Count) return null;

            var cells = sheet.Rows[row - 1];
            return column > cells.Count ? null : cells[column - 1];
        }
    }
}
