using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
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
