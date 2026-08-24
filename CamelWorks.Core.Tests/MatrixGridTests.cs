using System.Linq;
using CamelWorks.Core.Clash;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>The grid itself: symmetry, the diagonal, and what removing a category does.</summary>
    public class MatrixGridTests
    {
        private static MatrixGrid Three()
        {
            var grid = new MatrixGrid(new[]
            {
                new MatrixCategory("Foundations", "ST_Found", "01", "ST"),
                new MatrixCategory("Frame", "ST_Frame", "02", "ST"),
                new MatrixCategory("Ductwork", "ME_Duct", "03", "ME"),
            });

            grid.Set(0, 1, "A");
            grid.Set(1, 2, "B");
            grid.Set(2, 2, "C");

            return grid;
        }

        [Fact]
        public void A_cell_written_one_way_reads_the_other()
        {
            var grid = Three();

            Assert.Equal("A", grid.At(1, 0));
            Assert.Equal("B", grid.At(2, 1));
        }

        [Fact]
        public void Writing_the_mirror_overwrites_rather_than_adding_a_second_cell()
        {
            var grid = Three();
            grid.Set(1, 0, "Z");

            Assert.Equal("Z", grid.At(0, 1));
            Assert.Equal(3, grid.Filled);
        }

        [Fact]
        public void Clearing_a_cell_empties_it_both_ways()
        {
            var grid = Three();
            grid.Set(1, 0, null);

            Assert.Null(grid.At(0, 1));
            Assert.Null(grid.At(1, 0));
            Assert.Equal(2, grid.Filled);
        }

        [Fact]
        public void Removing_a_category_renumbers_the_pairs_above_it()
        {
            var grid = Three();

            // Drop Frame, the middle one. Foundations-Frame and Frame-Ductwork both touch it and go;
            // Ductwork-Ductwork survives and must follow Ductwork down from index 2 to index 1.
            grid.RemoveAt(1);

            Assert.Equal(new[] { "Foundations", "Ductwork" }, grid.Categories.Select(c => c.Name));
            Assert.Equal(1, grid.Filled);
            Assert.Equal("C", grid.At(1, 1));
        }

        [Fact]
        public void A_cell_outside_the_grid_is_ignored_rather_than_thrown()
        {
            var grid = Three();

            grid.Set(0, 9, "X");
            grid.Set(-1, 0, "X");

            Assert.Equal(3, grid.Filled);
            Assert.Null(grid.At(0, 9));
        }

        [Fact]
        public void The_legend_fills_in_symbols_nobody_has_explained()
        {
            var grid = Three();

            var legend = new MatrixLegend(new[]
            {
                new MatrixSymbol("A", MatrixTest.Clearance, 0.05),
            });

            Assert.Equal(new[] { "B", "C" }, legend.Unexplained(grid));

            var covering = legend.Covering(grid);

            // The one that was described keeps what it was given; the two that were not arrive as
            // ordinary hard clashes rather than stopping the read.
            Assert.Empty(covering.Unexplained(grid));
            Assert.Equal(MatrixTest.Clearance, covering.Meaning("A")!.Test);
            Assert.Equal(0.05, covering.Meaning("A")!.Tolerance);
            Assert.Equal(MatrixTest.Hard, covering.Meaning("B")!.Test);
        }

        [Fact]
        public void A_symbol_is_matched_however_it_was_typed()
        {
            var legend = new MatrixLegend(new[] { new MatrixSymbol("a", MatrixTest.Duplicate) });

            Assert.Equal(MatrixTest.Duplicate, legend.Meaning("A")!.Test);
            Assert.Equal(MatrixTest.Duplicate, legend.Meaning(" a ")!.Test);
            Assert.Null(legend.Meaning("b"));
        }

        [Fact]
        public void An_empty_grid_offers_no_pairs()
        {
            var grid = new MatrixGrid();

            Assert.True(grid.IsEmpty);
            Assert.Empty(grid.Pairs());
        }
    }
}
