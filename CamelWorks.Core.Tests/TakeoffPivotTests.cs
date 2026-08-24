using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class TakeoffPivotTests
    {
        private static PivotLine Line(string row, string column, string? value, string id = "e") =>
            new PivotLine(id, row, column, value);

        [Fact]
        public void Every_cell_is_added_up_on_its_own()
        {
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "2 m3"),
                Line("Walls", "Level 1", "3 m3"),
                Line("Walls", "Level 2", "4 m3"),
                Line("Slabs", "Level 1", "10 m3"),
            });

            Assert.Equal(5, table.Cell("Walls", "Level 1")!.Total!.Value.Value, 6);
            Assert.Equal(4, table.Cell("Walls", "Level 2")!.Total!.Value.Value, 6);
            Assert.Equal(10, table.Cell("Slabs", "Level 1")!.Total!.Value.Value, 6);
            Assert.Null(table.Cell("Slabs", "Level 2"));
        }

        [Fact]
        public void Rows_and_columns_come_back_biggest_first()
        {
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "1"),
                Line("Walls", "Level 1", "1"),
                Line("Walls", "Level 2", "1"),
                Line("Slabs", "Level 2", "1"),
            });

            Assert.Equal(new[] { "Walls", "Slabs" }, table.Rows);
            Assert.Equal(new[] { "Level 1", "Level 2" }, table.Columns);
        }

        [Fact]
        public void Totals_add_up_across_and_down()
        {
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "2 m3"),
                Line("Walls", "Level 2", "3 m3"),
                Line("Slabs", "Level 1", "10 m3"),
            });

            Assert.Equal(5, table.RowTotal("Walls")!.Total!.Value.Value, 6);
            Assert.Equal(12, table.ColumnTotal("Level 1")!.Total!.Value.Value, 6);
            Assert.Equal(15, table.Grand.Total!.Value.Value, 6);
            Assert.Equal(3, table.Grand.Count);
        }

        [Fact]
        public void A_trimmed_table_still_adds_up_to_the_truth()
        {
            // Row and column totals come from every line, not from the visible cells. Otherwise the
            // totals are a different number from the one the model contains and nobody can tell.
            var lines = Enumerable.Range(0, 10)
                .Select(n => Line("Row " + n, "Level 1", "1 m3"))
                .ToList();

            var table = TakeoffPivot.Of(lines, rowCap: 3);

            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(7, table.DroppedRows);
            Assert.Equal(10, table.Grand.Count);
            Assert.Equal(10, table.ColumnTotal("Level 1")!.Total!.Value.Value, 6);
            Assert.Contains("beyond the cap, still counted in the totals", table.Explain());
        }

        [Fact]
        public void An_element_with_nothing_on_an_axis_gets_a_named_row()
        {
            // A row headed by an empty string looks like a rendering fault. One headed "(none)" is
            // a fact about the model, and usually the most interesting row on the table.
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "1 m3"),
                new PivotLine("e", null, "  ", "2 m3"),
            });

            Assert.Contains(PivotLine.None, table.Rows);
            Assert.Contains(PivotLine.None, table.Columns);
            Assert.Equal(2, table.Cell(PivotLine.None, PivotLine.None)!.Total!.Value.Value, 6);
        }

        [Fact]
        public void A_cell_of_mixed_units_refuses_to_total()
        {
            // Nothing about being in a grid makes a wrong number more acceptable.
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "2 m3"),
                Line("Walls", "Level 1", "3 m2"),
            });

            var cell = table.Cell("Walls", "Level 1")!;

            Assert.True(cell.MixedKinds);
            Assert.Null(cell.Total);
            Assert.False(cell.IsComplete);
        }

        [Fact]
        public void An_unreadable_value_is_counted_and_named_rather_than_dropped()
        {
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "2 m3"),
                Line("Walls", "Level 1", "about three"),
            });

            var cell = table.Cell("Walls", "Level 1")!;

            Assert.Equal(1, cell.Unreadable);
            Assert.Equal(2, cell.Count);
            Assert.Contains("about three", cell.UnreadableExamples);
            Assert.Equal(2, cell.Total!.Value.Value, 6);
        }

        [Fact]
        public void A_blank_value_is_not_an_unreadable_one()
        {
            // Plenty of elements simply do not carry the property being measured, and calling that
            // unreadable would bury the values that really are malformed.
            var table = TakeoffPivot.Of(new[]
            {
                Line("Walls", "Level 1", "2 m3"),
                Line("Walls", "Level 1", null),
                Line("Walls", "Level 1", "   "),
            });

            var cell = table.Cell("Walls", "Level 1")!;

            Assert.Equal(0, cell.Unreadable);
            Assert.Equal(3, cell.Count);
            Assert.True(cell.IsComplete);
        }

        [Fact]
        public void Names_that_run_together_do_not_share_a_cell()
        {
            // The cell key is length-prefixed, so ("AB", "C") is not ("A", "BC").
            var table = TakeoffPivot.Of(new[]
            {
                Line("AB", "C", "1 m3"),
                Line("A", "BC", "5 m3"),
            });

            Assert.Equal(1, table.Cell("AB", "C")!.Total!.Value.Value, 6);
            Assert.Equal(5, table.Cell("A", "BC")!.Total!.Value.Value, 6);
        }

        [Fact]
        public void Nothing_in_scope_is_a_readable_answer_rather_than_an_error()
        {
            var table = TakeoffPivot.Of(new PivotLine[0]);

            Assert.True(table.IsEmpty);
            Assert.Equal(0, table.Grand.Count);
            Assert.Contains("nothing in scope", table.Explain());
        }
    }
}
