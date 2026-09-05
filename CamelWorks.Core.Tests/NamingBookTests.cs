using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class NamingBookTests
    {
        private static NamingBook Booked(params string[] patterns)
        {
            var book = new NamingBook();

            book.Scheme.Add("phase", "Phase");
            book.Patterned(patterns);

            return book;
        }

        [Fact]
        public void A_bound_test_is_read_by_the_pattern_it_was_bound_to()
        {
            var book = Booked("{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column}");

            book.Bind("t1", 1);

            var read = book.Read("t1", "ST-01 Steel vs ME-04 Ducts");

            Assert.NotNull(read);
            Assert.Equal("ST", read!.Value("rowCode"));
            Assert.Equal("04", read.Value("columnNumber"));
            Assert.Equal("Ducts", read.Value("column"));
        }

        [Fact]
        public void An_unbound_test_reads_as_nothing()
        {
            var book = Booked("{row} vs {column}");

            Assert.Null(book.Read("t1", "Steel vs Ducts"));
            Assert.False(book.Knows("t1"));
        }

        [Fact]
        public void A_test_bound_to_no_pattern_reads_as_nothing_but_is_known()
        {
            var book = Booked("{row} vs {column}");

            book.Bind("t1", 0);

            Assert.Null(book.Read("t1", "Steel vs Ducts"));
            Assert.True(book.Knows("t1"));
        }

        [Fact]
        public void A_binding_never_falls_through_to_another_pattern()
        {
            var book = Booked("{row} vs {column}", "{row} | {column}");

            book.Bind("t1", 2);

            // Pattern 1 would fit. The binding says 2, and 2 does not, so nothing is read.
            Assert.Null(book.Read("t1", "Steel vs Ducts"));
        }

        [Fact]
        public void The_fitting_pattern_is_a_proposal_for_a_test_nobody_has_bound()
        {
            var book = Booked("{row} | {column}", "{row} vs {column}");

            Assert.Equal(2, book.Fitting("Steel vs Ducts"));
            Assert.Equal(0, book.Fitting("Coordination check 7"));
        }

        [Fact]
        public void Changing_one_token_leaves_the_rest_of_the_name_alone()
        {
            var book = Booked("{rowCode}-{rowNumber} {row} vs {columnCode}-{columnNumber} {column}");

            book.Bind("t1", 1);

            var said = book.Written("t1", "ST-01 Steel vs ME-04 Ducts", "rowNumber", "05");

            Assert.Equal("ST-05 Steel vs ME-04 Ducts", said);
        }

        [Fact]
        public void Changing_a_custom_token_writes_the_name_back()
        {
            var book = Booked("{rowPhase} {row} vs {columnPhase} {column}");

            book.Bind("t1", 1);

            var said = book.Written("t1", "P2 Steel vs P3 Ducts", "columnPhase", "P4");

            Assert.Equal("P2 Steel vs P4 Ducts", said);
        }

        [Fact]
        public void A_token_the_pattern_does_not_carry_cannot_be_written()
        {
            var book = Booked("{row} vs {column}");

            book.Bind("t1", 1);

            Assert.Null(book.Written("t1", "Steel vs Ducts", "rowNumber", "05"));
        }

        [Fact]
        public void A_test_whose_name_stopped_fitting_cannot_be_written()
        {
            var book = Booked("{row} vs {column}");

            book.Bind("t1", 1);

            Assert.Null(book.Written("t1", "Coordination check 7", "row", "Steel"));
        }

        [Fact]
        public void Rebuilding_keeps_every_separator_the_pattern_carries()
        {
            var pattern = MatrixNamePattern.Parse("JOB-A / {row} :: {column} (final)");

            var made = pattern.Rebuild(new System.Collections.Generic.Dictionary<string, string>
            {
                ["row"] = "Steel",
                ["column"] = "Ducts",
            });

            Assert.Equal("JOB-A / Steel :: Ducts (final)", made);
        }

        [Fact]
        public void Replacing_the_patterns_drops_a_binding_that_points_past_the_end()
        {
            var book = Booked("{row} vs {column}", "{row} | {column}");

            book.Bind("t1", 2);
            book.Patterned(new[] { "{row} vs {column}" });

            Assert.False(book.Knows("t1"));
        }

        [Fact]
        public void Adding_the_same_pattern_twice_returns_where_it_already_is()
        {
            var book = new NamingBook();

            Assert.Equal(1, book.Add("{row} vs {column}"));
            Assert.Equal(1, book.Add("{row} vs {column}"));
            Assert.Single(book.Patterns);
        }

        [Fact]
        public void The_book_survives_a_round_trip()
        {
            var book = Booked("{rowPhase} {row} vs {columnPhase} {column}");

            book.Bind("t1", 1);
            book.Bind("t2", 0);

            var read = NamingBook.FromJson(
                JsonReader.Parse(book.ToJson()!.ToJson(indented: false)));

            Assert.Equal(book.Patterns, read.Patterns);
            Assert.True(read.Knows("t1"));
            Assert.True(read.Knows("t2"));
            Assert.Equal("Phase", read.Scheme.Field("phase")!.Title);
            Assert.Equal("P2", read.Read("t1", "P2 Steel vs P3 Ducts")!.Value("rowPhase"));
        }

        [Fact]
        public void A_book_nobody_has_set_up_writes_nothing()
        {
            Assert.Null(new NamingBook().ToJson());
        }

        [Fact]
        public void Unread_counts_the_tests_no_pattern_reaches()
        {
            var book = Booked("{row} vs {column}");

            book.Bind("t1", 1);
            book.Bind("t2", 1);

            var tests = new[]
            {
                ("t1", "Steel vs Ducts"),
                ("t2", "Coordination check 7"),
                ("t3", "Steel vs Pipes"),
            };

            // t2 is bound but does not fit, t3 is not bound at all.
            Assert.Equal(2, book.Unread(tests));
        }

        [Fact]
        public void A_field_added_to_the_scheme_becomes_a_token_the_patterns_accept()
        {
            var book = new NamingBook();

            book.Patterned(new[] { "{rowZone} vs {columnZone}" });

            Assert.False(book.Compiled(1)!.IsUsable);

            book.Scheme.Add("zone", "Zone");

            // No telling the book anything: it notices the scheme moved on by itself.
            Assert.True(book.Compiled(1)!.IsUsable);
        }
    }
}
