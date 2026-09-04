using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Reading a coordination matrix out of the spreadsheet it already lives in.
    ///
    /// The fixture is deliberately awkward in the ways real ones are: the labels are not in column
    /// A, there is a gap between the label block and the grid, there is a spacer column inside the
    /// grid, and the numbers and the set names are on different header rows. A reader that only
    /// works on a tidy sheet is a reader nobody can use, because nobody's sheet is tidy.
    /// </summary>
    public class SheetMatrixTests
    {
        private static SheetTable Sheet(params string?[][] rows)
        {
            var sheet = new XlsxSheet("Matrix");
            foreach (var row in rows) sheet.Row(row);

            return XlsxReader.Read(XlsxWriter.Write(new[] { sheet })).First!;
        }

        //                       1     2       3      4     5              6           7           8    9           10
        private static SheetTable Typical() => Sheet(
            new string?[] { "Coordination matrix" },
            new string?[] { "",   "",     "",    "",   "",            "",         "01",       "",  "02",       "03" },
            new string?[] { "" },
            new string?[] { "" },
            new string?[] { "" },
            new string?[] { "",   "",     "",    "",   "",            "",         "ST_Found", "",  "ST_Frame", "ME_Duct" },
            new string?[] { "",   "01",   "ST",  "",   "Foundations", "ST_Found", "A",        "",  "B",        "" },
            new string?[] { "",   "02",   "ST",  "",   "Frame",       "ST_Frame", "",         "",  "A",        "C" },
            new string?[] { "",   "03",   "ME",  "",   "Ductwork",    "ME_Duct",  "",         "",  "",         "A" });

        [Fact]
        public void A_dash_in_a_workbook_means_the_pair_was_ruled_out()
        {
            // A client's workbook says "not required" with whichever dash their font gave them,
            // or with the letters. Read as anything else it becomes a symbol with no meaning,
            // and the reader is asked to give a tolerance to a decision not to test.
            foreach (var said in new[] { "-", "\u2013", "\u2014", "N/A", "na" })
            {
                var read = SheetMatrix.Read(Sheet(
                    new string?[] { "Coordination matrix" },
                    new string?[] { "", "", "", "", "", "", "01", "", "02", "03" },
                    new string?[] { "" },
                    new string?[] { "" },
                    new string?[] { "" },
                    new string?[] { "", "", "", "", "", "", "ST_Found", "", "ST_Frame", "ME_Duct" },
                    new string?[] { "", "01", "ST", "", "Foundations", "ST_Found", "A", "", said, "" },
                    new string?[] { "", "02", "ST", "", "Frame", "ST_Frame", said, "", "A", "C" },
                    new string?[] { "", "03", "ME", "", "Ductwork", "ME_Duct", "", "", "", "A" }),
                    Mapping());

                Assert.Equal(MatrixBoard.NotRequired,
                             read.Grid.At(read.Grid.Rows[0], read.Grid.Columns[1]));

                Assert.Empty(new MatrixLegend(new[] { new MatrixSymbol("A") })
                                 .Unexplained(read.Grid)
                                 .Where(MatrixBoard.IsNotRequired));
            }
        }

        private static SheetMapping Mapping() => new SheetMapping
        {
            NumberColumn = 2, CodeColumn = 3, NameColumn = 5, SetColumn = 6, FirstRow = 7,
            HeaderNumberRow = 2, HeaderSetRow = 6, FirstBodyColumn = 7, BlankRowStop = 3,
        };

        [Fact]
        public void The_categories_come_off_the_side_of_the_sheet()
        {
            var read = SheetMatrix.Read(Typical(), Mapping());

            Assert.Equal(3, read.Grid.Rows.Count);
            Assert.Equal(new[] { "Foundations", "Frame", "Ductwork" },
                         read.Grid.Rows.Select(c => c.Name));
            Assert.Equal("ST_Found", read.Grid.Rows[0].Source.Name);
            Assert.Equal("ST", read.Grid.Rows[0].Code);
        }

        [Fact]
        public void A_spacer_column_inside_the_grid_is_skipped()
        {
            // Column 8 is blank in both header rows. Matching by position would read every symbol
            // one category to the left of where it belongs; matching by content steps over it.
            var read = SheetMatrix.Read(Typical(), Mapping());

            Assert.Equal("B", At(read.Grid, 0, 1));   // Foundations vs Frame
            Assert.Equal("C", At(read.Grid, 1, 2));   // Frame vs Ductwork
            Assert.Null(At(read.Grid, 0, 2));         // Foundations vs Ductwork: nothing was written
        }

        [Fact]
        public void The_diagonal_is_a_real_test_and_is_kept()
        {
            var read = SheetMatrix.Read(Typical(), Mapping());

            Assert.Equal("A", At(read.Grid, 0, 0));
            Assert.Equal("A", At(read.Grid, 1, 1));
            Assert.Equal("A", At(read.Grid, 2, 2));
        }

        [Fact]
        public void A_cell_reads_the_same_either_way_round()
        {
            var read = SheetMatrix.Read(Typical(), Mapping());

            // Both halves carry it now: the board no longer mirrors on read, so a sheet that
            // says one thing about a pair writes it into both cells that name that pair.
            Assert.Equal(At(read.Grid, 0, 1), At(read.Grid, 1, 0));
            Assert.Equal(5, MatrixPlanner.Plan(read.Grid,
                new MatrixLegend().Covering(read.Grid)).Tests.Count);
        }

        [Fact]
        public void Every_pair_is_offered_once_not_twice()
        {
            // A sheet fills both halves of the grid, the way a clash matrix is drawn. The board
            // holds both and offers each pair once, so nothing is skipped and nothing is doubled:
            // three diagonals and two off-diagonal pairs.
            var read = SheetMatrix.Read(Typical(), Mapping());

            var plan = MatrixPlanner.Plan(read.Grid, new MatrixLegend().Covering(read.Grid));

            Assert.Equal(5, plan.Tests.Count);
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void Columns_out_of_order_still_find_their_category()
        {
            // The grid's columns run 03, 02, 01 while the side still runs 01, 02, 03.
            var sheet = Sheet(
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "03", "02", "01" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "ME_Duct", "ST_Frame", "ST_Found" },
                new string?[] { "", "01", "ST", "", "Foundations", "ST_Found", "", "B", "" },
                new string?[] { "", "02", "ST", "", "Frame", "ST_Frame", "C", "", "" },
                new string?[] { "", "03", "ME", "", "Ductwork", "ME_Duct", "", "", "" });

            var read = SheetMatrix.Read(sheet, Mapping());

            Assert.Equal("B", At(read.Grid, 0, 1));
            Assert.Equal("C", At(read.Grid, 1, 2));
        }

        [Fact]
        public void A_category_backed_by_no_set_is_reported_rather_than_dropped()
        {
            var sheet = Sheet(
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "01", "02" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "ST_Found", "" },
                new string?[] { "", "01", "ST", "", "Foundations", "ST_Found", "", "A" },
                new string?[] { "", "02", "ST", "", "Frame", "", "A", "" });

            var read = SheetMatrix.Read(sheet, Mapping());

            Assert.Equal(2, read.Grid.Rows.Count);
            Assert.True(read.Grid.Rows[1].IsUnbacked);
            Assert.Contains(read.Warnings, w => w.Contains("Frame") && w.Contains("no saved set"));
        }

        [Fact]
        public void A_heading_matching_no_category_is_reported_and_skipped()
        {
            var sheet = Sheet(
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "01", "99" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "ST_Found", "XX_Nothing" },
                new string?[] { "", "01", "ST", "", "Foundations", "ST_Found", "A", "Z" });

            var read = SheetMatrix.Read(sheet, Mapping());

            Assert.Equal("A", At(read.Grid, 0, 0));
            Assert.Contains(read.Warnings, w => w.Contains("matches no category"));
            Assert.DoesNotContain("Z", read.Grid.SymbolsUsed);
        }

        [Fact]
        public void Rows_below_the_matrix_are_not_read_as_categories()
        {
            var sheet = Sheet(
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "01" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "", "", "", "", "", "", "ST_Found" },
                new string?[] { "", "01", "ST", "", "Foundations", "ST_Found", "A" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "", "", "", "", "Notes: agreed 12 March" });

            var read = SheetMatrix.Read(sheet, Mapping());

            Assert.Single(read.Grid.Rows);
            Assert.Equal("Foundations", read.Grid.Rows[0].Name);
        }

        [Fact]
        public void No_sheet_is_a_warning_not_an_exception()
        {
            var read = SheetMatrix.Read(null, Mapping());

            Assert.False(read.IsUsable);
            Assert.NotEmpty(read.Warnings);
        }

        [Fact]
        public void The_preview_shows_the_corner_as_text()
        {
            var preview = SheetMatrix.Preview(Typical(), 3, 8);

            Assert.Equal(3, preview.Count);
            Assert.Equal(8, preview[0].Count);
            Assert.Equal("Coordination matrix", preview[0][0]);
            Assert.Equal("01", preview[1][6]);
        }
    
        /// <summary>One cell of a board by index, the way the old square grid was addressed.</summary>
        private static string? At(MatrixBoard board, int row, int column) =>
            row < 0 || row >= board.Rows.Count || column < 0 || column >= board.Columns.Count
                ? null
                : board.At(board.Rows[row], board.Columns[column]);
}
}
