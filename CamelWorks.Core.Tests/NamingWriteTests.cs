using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class NamingWriteTests
    {
        private const string Standard =
            "{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column}";

        private static NamingBook Booked(params string[] patterns)
        {
            var book = new NamingBook();

            book.Patterned(patterns);

            return book;
        }

        private static MatrixField Series(string start, int step, int pad = 0) =>
            new MatrixField("value", "Value")
            {
                Fill = MatrixFill.Series,
                Start = start,
                Step = step,
                Pad = pad,
            };

        [Fact]
        public void A_counted_write_numbers_the_tests_in_the_order_it_was_given()
        {
            var book = Booked(Standard);

            var tests = new List<(string, string)>
            {
                ("t1", "ST-01 Steel vs ME-04 Ducts"),
                ("t2", "ST-02 Steel vs ME-05 Pipes"),
                ("t3", "ST-03 Steel vs ME-06 Trays"),
            };

            foreach (var one in tests) book.Bind(one.Item1, 1);

            var plan = NamingWrite.Plan(book, tests, "rowNumber", Series("10", 10, 2));

            Assert.Equal(new[]
            {
                "ST-10 Steel vs ME-04 Ducts",
                "ST-20 Steel vs ME-05 Pipes",
                "ST-30 Steel vs ME-06 Trays",
            }, plan.Select(one => one.Now));
        }

        [Fact]
        public void Everything_the_pattern_did_not_name_survives_the_write()
        {
            var book = Booked("{rowCode}-{rowNumber} · {row} | {columnCode} · {column}");

            book.Bind("t1", 1);

            var plan = NamingWrite.Plan(book, new[] { ("t1", "ST-01 · Steel | ME · Ducts") },
                                        "rowNumber", Series("7", 1));

            Assert.Equal("ST-7 · Steel | ME · Ducts", plan[0].Now);
        }

        [Fact]
        public void A_test_on_no_pattern_is_refused_and_named()
        {
            var book = Booked(Standard);

            book.Bind("t1", 1);

            var plan = NamingWrite.Plan(book, new[]
            {
                ("t1", "ST-01 Steel vs ME-04 Ducts"),
                ("t2", "Coordination check 3"),
            }, "rowNumber", Series("10", 10, 2));

            Assert.Equal(NamingRefusal.None, plan[0].Why);
            Assert.Equal(NamingRefusal.Unbound, plan[1].Why);
            Assert.Null(plan[1].Now);
            Assert.Equal("No naming pattern", NamingWrite.Said(plan[1].Why));
        }

        [Fact]
        public void A_refused_test_still_takes_its_place_in_the_count()
        {
            var book = Booked(Standard);

            book.Bind("t1", 1);
            book.Bind("t3", 1);

            var plan = NamingWrite.Plan(book, new[]
            {
                ("t1", "ST-01 Steel vs ME-04 Ducts"),
                ("t2", "Coordination check 3"),
                ("t3", "ST-03 Steel vs ME-06 Trays"),
            }, "rowNumber", Series("10", 10, 2));

            // The third test gets 30, not 20: the one in the middle held its place, so taking a
            // test out of the run does not shift everything under it.
            Assert.Equal("ST-10 Steel vs ME-04 Ducts", plan[0].Now);
            Assert.Equal("ST-30 Steel vs ME-06 Trays", plan[2].Now);
        }

        [Fact]
        public void A_pattern_without_that_field_is_refused_rather_than_guessed_at()
        {
            var book = Booked("{row} | {column}");

            book.Bind("t1", 1);

            var plan = NamingWrite.Plan(book, new[] { ("t1", "Steel | Ducts") },
                                        "rowNumber", Series("10", 10));

            Assert.Equal(NamingRefusal.NoToken, plan[0].Why);
            Assert.Null(plan[0].Now);
        }

        [Fact]
        public void A_bound_test_whose_name_stopped_fitting_is_refused_as_unread()
        {
            var book = Booked(Standard);

            book.Bind("t1", 1);

            var plan = NamingWrite.Plan(book, new[] { ("t1", "Something else entirely") },
                                        "rowNumber", Series("10", 10));

            Assert.Equal(NamingRefusal.Unread, plan[0].Why);
        }

        [Fact]
        public void Same_on_all_writes_one_value_everywhere()
        {
            var book = Booked("{rowCode} {row} vs {columnCode} {column}");

            book.Bind("t1", 1);
            book.Bind("t2", 1);

            var how = new MatrixField("value", "Value")
            {
                Fill = MatrixFill.Same,
                Same = "XX",
            };

            var plan = NamingWrite.Plan(book, new[]
            {
                ("t1", "ST Steel vs ME Ducts"),
                ("t2", "AR Walls vs ME Pipes"),
            }, "rowCode", how);

            Assert.Equal(new[] { "XX Steel vs ME Ducts", "XX Walls vs ME Pipes" },
                         plan.Select(one => one.Now));
        }

        [Fact]
        public void A_write_that_changes_nothing_is_planned_but_not_counted_as_a_change()
        {
            var book = Booked(Standard);

            book.Bind("t1", 1);

            var plan = NamingWrite.Plan(book, new[] { ("t1", "ST-10 Steel vs ME-04 Ducts") },
                                        "rowNumber", Series("10", 10, 2));

            Assert.Equal(NamingRefusal.None, plan[0].Why);
            Assert.False(plan[0].Changes);
            Assert.Empty(NamingWrite.Changing(plan));
        }

        [Fact]
        public void Letters_count_in_the_spreadsheets_own_alphabet()
        {
            var book = Booked("{rowCode} {row} vs {columnCode} {column}");

            var tests = Enumerable.Range(1, 3)
                .Select(one => ("t" + one, "ST Steel vs ME Ducts " + one))
                .ToList();

            foreach (var one in tests) book.Bind(one.Item1, 1);

            var how = new MatrixField("value", "Value")
            {
                Fill = MatrixFill.Series,
                Steps = MatrixSteps.Letters,
                Start = "A",
                Step = 1,
            };

            var plan = NamingWrite.Plan(book, tests, "rowCode", how);

            Assert.Equal(new[] { "A", "B", "C" }, plan.Select(one => one.Value));
        }

        [Fact]
        public void A_field_of_the_row_counts_once_per_row_not_once_per_test()
        {
            var book = Booked(Standard);

            var tests = new List<(string, string)>
            {
                ("t1", "ST-01 Steel vs ME-04 Ducts"),
                ("t2", "ST-01 Steel vs ME-05 Pipes"),
                ("t3", "AR-02 Walls vs ME-04 Ducts"),
            };

            foreach (var one in tests) book.Bind(one.Item1, 1);

            var plan = NamingWrite.Plan(book, tests, "rowNumber", Series("10", 10, 2),
                                        perSide: true);

            // Steel is one discipline, so it reads 10 in both tests it appears in. Counting down
            // the list would have made it 10 in one and 20 in the other. Every discipline is
            // placed once and written on whichever side it appears on, so Ducts is 20 as a
            // column here and would be 20 as a row anywhere else.
            Assert.Equal("ST-10 Steel vs ME-20 Ducts", plan[0].Now);
            Assert.Equal("ST-10 Steel vs ME-30 Pipes", plan[1].Now);
            Assert.Equal("AR-40 Walls vs ME-20 Ducts", plan[2].Now);
        }

        [Fact]
        public void Counting_per_side_gives_a_discipline_the_same_number_on_both_sides()
        {
            var book = Booked(Standard);

            var tests = new List<(string, string)>
            {
                ("t1", "ST-01 Steel vs ME-04 Ducts"),
                ("t2", "ME-04 Ducts vs AR-09 Walls"),
            };

            foreach (var one in tests) book.Bind(one.Item1, 1);

            var plan = NamingWrite.Plan(book, tests, "rowNumber", Series("1", 1, 2),
                                        perSide: true);

            // Ducts is one discipline: it reads 02 as a column in the first test and as a row in
            // the second. Numbering the row side alone left the two disagreeing.
            Assert.Equal("ST-01 Steel vs ME-02 Ducts", plan[0].Now);
            Assert.Equal("ME-02 Ducts vs AR-03 Walls", plan[1].Now);
        }

        [Fact]
        public void A_test_on_a_pattern_without_the_field_takes_no_number_from_the_run()
        {
            var book = Booked(Standard, "{row} | {column}");

            var tests = new List<(string, string)>
            {
                ("t1", "Trays | Pipes"),
                ("t2", "ST-01 Steel vs ME-04 Ducts"),
            };

            book.Bind("t1", 2);
            book.Bind("t2", 1);

            var plan = NamingWrite.Plan(book, tests, "rowNumber", Series("1", 1, 2),
                                        perSide: true);

            // Trays and Pipes cannot carry a number, so they do not consume 01 and 02 and leave
            // Steel starting at 03.
            Assert.Equal(NamingRefusal.NoToken, plan[0].Why);
            Assert.Equal("ST-01 Steel vs ME-02 Ducts", plan[1].Now);
        }

        [Fact]
        public void The_side_a_token_belongs_to_is_the_word_it_starts_with()
        {
            Assert.Equal("row", NamingWrite.Side("rowNumber"));
            Assert.Equal("row", NamingWrite.Side("row"));
            Assert.Equal("column", NamingWrite.Side("columnPhase"));

            // The test's own running number belongs to neither side, so it counts per test.
            Assert.Null(NamingWrite.Side("n"));
            Assert.Null(NamingWrite.Side("symbol"));
        }

        [Fact]
        public void A_field_is_drawn_everywhere_until_it_is_told_otherwise()
        {
            var field = new MatrixField("phase", "Phase");

            Assert.Equal(MatrixShown.Everywhere, field.Shown);
            Assert.Equal(MatrixShown.Everywhere, field.Copy().Shown);
        }

        [Fact]
        public void Where_a_field_is_drawn_survives_being_written_and_read()
        {
            var field = new MatrixField("phase", "Phase") { Shown = MatrixShown.Panel };

            var read = MatrixField.FromJson(JsonReader.Parse(field.ToJson().ToJson()));

            Assert.NotNull(read);
            Assert.Equal(MatrixShown.Panel, read!.Shown);
        }

        [Fact]
        public void A_field_drawn_everywhere_writes_nothing_about_it()
        {
            // A scheme nobody has hidden anything in saves the bytes it did before this existed.
            var json = new MatrixField("phase", "Phase").ToJson().ToJson();

            Assert.DoesNotContain("shown", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_field_drawn_nowhere_is_still_carried_by_the_line()
        {
            var scheme = new MatrixScheme();
            var line = new MatrixLine("Steel");

            scheme.Add("phase", "Phase").Shown = MatrixShown.Nowhere;

            line.Set("phase", "P2");

            // Hidden takes it off the screen, not off the line and not out of the names.
            Assert.Equal("P2", line.Value("phase"));
            Assert.Equal(MatrixShown.Nowhere, scheme.Field("phase")!.Shown);
        }

        [Fact]
        public void Moving_a_field_changes_the_order_the_scheme_lists_them_in()
        {
            var scheme = new MatrixScheme();

            scheme.Add("phase", "Phase");
            scheme.Add("package", "Package");

            var was = scheme.Fields.Select(one => one.Key).ToList();

            Assert.True(scheme.Move("package", was.IndexOf("phase")));

            var now = scheme.Fields.Select(one => one.Key).ToList();

            Assert.True(now.IndexOf("package") < now.IndexOf("phase"));
        }

        [Fact]
        public void A_scheme_absorbs_the_fields_it_does_not_have_and_keeps_the_ones_it_does()
        {
            var mine = new MatrixScheme();

            mine.Add("phase", "Phase");

            var theirs = new MatrixScheme();

            theirs.Add("phase", "Stage");
            theirs.Add("package", "Package");

            var took = mine.Absorb(theirs);

            Assert.Equal(1, took);
            Assert.Equal("Phase", mine.Field("phase")!.Title);
            Assert.True(mine.Has("package"));
        }
    }
}
