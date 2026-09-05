using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class MatrixFieldTests
    {
        private static MatrixBoard Board(params string[] names)
        {
            var board = new MatrixBoard();

            foreach (var name in names)
                board.AddRow(new MatrixLine(name, source: MatrixSource.SelectionSet(name)));

            board.SquareOffRows();

            return board;
        }

        [Fact]
        public void A_number_series_counts_down_the_axis()
        {
            var board = Board("Steel", "Ducts", "Pipes");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "10";
            number.Step = 10;

            board.ApplyScheme();

            Assert.Equal(new[] { "10", "20", "30" }, board.Rows.Select(r => r.Number));
        }

        [Fact]
        public void Changing_where_it_starts_is_one_edit()
        {
            var board = Board("Steel", "Ducts", "Pipes");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "10";
            number.Step = 10;
            board.ApplyScheme();

            number.Start = "100";
            board.ApplyScheme();

            Assert.Equal(new[] { "100", "110", "120" }, board.Rows.Select(r => r.Number));
        }

        [Fact]
        public void A_number_series_pads_when_it_is_asked_to()
        {
            var board = Board("Steel", "Ducts");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";
            number.Pad = 2;

            board.ApplyScheme();

            Assert.Equal(new[] { "01", "02" }, board.Rows.Select(r => r.Number));
        }

        [Fact]
        public void A_letter_series_runs_past_Z()
        {
            var field = new MatrixField("mark")
            {
                Fill = MatrixFill.Series, Steps = MatrixSteps.Letters, Start = "Y",
            };

            Assert.Equal("Y", field.At(0));
            Assert.Equal("Z", field.At(1));
            Assert.Equal("AA", field.At(2));
            Assert.Equal("AB", field.At(3));
        }

        [Fact]
        public void A_letter_series_takes_a_step()
        {
            var field = new MatrixField("mark")
            {
                Fill = MatrixFill.Series, Steps = MatrixSteps.Letters, Start = "A", Step = 2,
            };

            Assert.Equal(new[] { "A", "C", "E" }, new[] { field.At(0), field.At(1), field.At(2) });
        }

        [Fact]
        public void A_series_can_restart_in_each_folder()
        {
            var board = Board("Walls", "Doors", "Steel", "Frames");

            board.Rows[0].Folder = "ARC";
            board.Rows[1].Folder = "ARC";
            board.Rows[2].Folder = "STR";
            board.Rows[3].Folder = "STR";

            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";
            number.Pad = 2;
            number.PerFolder = true;

            board.Scheme.Apply(board.Rows);

            Assert.Equal(new[] { "01", "02", "01", "02" }, board.Rows.Select(r => r.Number));
        }

        [Fact]
        public void The_same_value_can_go_on_every_line()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase");

            var phase = board.Scheme.Field("phase")!;

            phase.Fill = MatrixFill.Same;
            phase.Same = "P2";

            board.ApplyScheme();

            Assert.All(board.Rows, r => Assert.Equal("P2", r.Value("phase")));
        }

        [Fact]
        public void A_pinned_field_keeps_what_it_holds_and_is_marked_custom()
        {
            var board = Board("Steel", "Ducts", "Pipes");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";

            board.Scheme.Apply(board.Rows);
            board.Rows[1].Pin(MatrixField.NumberKey, "99");
            board.Scheme.Apply(board.Rows);

            Assert.Equal("99", board.Rows[1].Number);
            Assert.True(board.Rows[1].IsCustom);
            Assert.False(board.Rows[0].IsCustom);
        }

        [Fact]
        public void A_pinned_line_does_not_take_a_place_in_the_series()
        {
            var board = Board("Steel", "Ducts", "Pipes");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";

            board.Rows[1].Pin(MatrixField.NumberKey, "99");
            board.Scheme.Apply(board.Rows);

            // The numbering runs 1, 2 over the lines still in it rather than leaving a gap at 2.
            Assert.Equal(new[] { "1", "99", "2" }, board.Rows.Select(r => r.Number));
        }

        [Fact]
        public void Giving_a_field_back_puts_the_line_in_the_series_again()
        {
            var board = Board("Steel", "Ducts");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";

            board.Rows[0].Pin(MatrixField.NumberKey, "99");
            board.Rows[0].Unpin(MatrixField.NumberKey);
            board.Scheme.Apply(board.Rows);

            Assert.Equal("1", board.Rows[0].Number);
            Assert.False(board.Rows[0].IsCustom);
        }

        [Fact]
        public void Pinning_one_field_leaves_the_others_in_the_scheme()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase");

            var number = board.Scheme.Field(MatrixField.NumberKey)!;
            var phase = board.Scheme.Field("phase")!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";
            phase.Fill = MatrixFill.Same;
            phase.Same = "P2";

            board.Rows[0].Pin(MatrixField.NumberKey, "99");
            board.Scheme.Apply(board.Rows);

            Assert.Equal("99", board.Rows[0].Number);
            Assert.Equal("P2", board.Rows[0].Value("phase"));
        }

        [Fact]
        public void A_custom_field_becomes_a_token_a_pattern_can_carry()
        {
            var board = Board("Steel");

            board.Scheme.Add("phase", "Phase");

            Assert.Equal("{rowPhase}", board.Scheme.Field("phase")!.RowToken);
            Assert.True(MatrixNamePattern.Known("rowPhase", board.Scheme));
            Assert.False(MatrixNamePattern.Known("rowPhase"));
        }

        [Fact]
        public void A_custom_token_fills_from_the_line()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase");
            board.Write(board.Rows[0], "phase", "P2");
            board.Write(board.Rows[1], "phase", "P3");

            var said = MatrixNaming.Fill("{rowPhase}-{row} vs {columnPhase}-{column}",
                                         board.Rows[0], board.Columns[1], null, 1, board.Scheme);

            Assert.Equal("P2-Steel vs P3-Ducts", said);
        }

        [Fact]
        public void A_field_written_on_a_row_lands_on_its_column_too()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase");

            var twin = board.Write(board.Rows[0], "phase", "P2");

            Assert.NotNull(twin);
            Assert.Equal("P2", board.Columns[0].Value("phase"));
        }

        [Fact]
        public void A_twin_that_pinned_the_field_keeps_what_it_holds()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase");
            board.Columns[0].Pin("phase", "P9");

            board.Write(board.Rows[0], "phase", "P2");

            Assert.Equal("P9", board.Columns[0].Value("phase"));
        }

        [Fact]
        public void A_pattern_can_read_a_custom_token_back_off_a_name()
        {
            var board = Board("Steel");

            board.Scheme.Add("phase", "Phase");

            var pattern = MatrixNamePattern.Parse("{rowPhase}-{row} vs {columnPhase}-{column}",
                                                  board.Scheme);

            Assert.True(pattern.IsUsable);

            var match = pattern.Match("P2-Steel vs P3-Ducts");

            Assert.NotNull(match);
            Assert.Equal("P2", match!.Value("rowPhase"));
            Assert.Equal("Ducts", match.Value("column"));
        }

        [Fact]
        public void A_field_cannot_be_added_twice_under_one_key()
        {
            var scheme = new MatrixScheme();

            var first = scheme.Add("phase", "Phase");
            var again = scheme.Add("phase", "Something else");

            Assert.Same(first, again);
            Assert.Equal(5, scheme.Fields.Count);
        }

        [Fact]
        public void The_four_built_in_fields_cannot_be_removed()
        {
            var scheme = new MatrixScheme();

            foreach (var key in MatrixField.BuiltIn)
                Assert.False(scheme.Remove(key));

            Assert.Equal(4, scheme.Fields.Count);
        }

        [Fact]
        public void Removing_a_field_takes_its_values_off_the_lines()
        {
            var board = Board("Steel");

            board.Scheme.Add("phase", "Phase");
            board.Rows[0].Pin("phase", "P2");

            Assert.True(board.Scheme.Remove("phase", board.Rows.Concat(board.Columns)));

            Assert.Equal(string.Empty, board.Rows[0].Value("phase"));
            Assert.False(board.Rows[0].IsCustom);
        }

        [Fact]
        public void A_key_is_reduced_to_what_a_token_can_carry()
        {
            Assert.Equal("subphase", new MatrixField("sub phase").Key);
            Assert.Equal("phase2", new MatrixField("2 phase 2").Key);
        }

        [Fact]
        public void The_scheme_survives_a_round_trip()
        {
            var board = Board("Steel", "Ducts");

            board.Scheme.Add("phase", "Phase code");

            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "10";
            number.Step = 10;
            number.Pad = 3;
            number.PerFolder = true;

            board.Rows[0].Pin("phase", "P2");
            board.ApplyScheme();

            var read = MatrixBoard.FromJson(JsonReader.Parse(board.ToJson().ToJson(indented: false)));

            Assert.NotNull(read);

            var back = read!.Scheme.Field(MatrixField.NumberKey)!;

            Assert.Equal(MatrixFill.Series, back.Fill);
            Assert.Equal("10", back.Start);
            Assert.Equal(10, back.Step);
            Assert.Equal(3, back.Pad);
            Assert.True(back.PerFolder);
            Assert.Equal("Phase code", read.Scheme.Field("phase")!.Title);
            Assert.Equal("P2", read.Rows[0].Value("phase"));
            Assert.True(read.Rows[0].IsCustom);
        }

        [Fact]
        public void A_board_using_none_of_this_writes_no_fields_at_all()
        {
            var board = Board("Steel");

            Assert.DoesNotContain("\"fields\"", board.ToJson().ToJson(indented: false));
        }

        [Fact]
        public void A_file_missing_a_built_in_field_still_opens()
        {
            var scheme = MatrixScheme.FromJson(
                JsonValue.Array(new[] { new MatrixField("phase").ToJson() }));

            foreach (var key in MatrixField.BuiltIn) Assert.True(scheme.Has(key));

            Assert.True(scheme.Has("phase"));
        }

        [Fact]
        public void The_rows_and_the_columns_each_count_from_their_own_start()
        {
            var board = Board("Steel", "Ducts");
            var number = board.Scheme.Field(MatrixField.NumberKey)!;

            number.Fill = MatrixFill.Series;
            number.Start = "1";

            board.ApplyScheme();

            Assert.Equal(new[] { "1", "2" }, board.Rows.Select(r => r.Number));
            Assert.Equal(new[] { "1", "2" }, board.Columns.Select(c => c.Number));
        }
    }
}
