using System;
using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ValueCleanerTests
    {
        [Fact]
        public void Nothing_set_changes_nothing()
        {
            var cleaner = ValueCleaner.Of(null, null, out var problem);

            Assert.Null(problem);
            Assert.True(cleaner.DoesNothing);
            Assert.Equal("12.5 m3", cleaner.Clean("12.5 m3"));
            Assert.Equal("Volume: 12.5 m3 (net)", cleaner.Clean("Volume: 12.5 m3 (net)"));
            Assert.Contains("exactly as the model wrote them", cleaner.Describe());
        }

        [Fact]
        public void The_first_number_pattern_gets_the_number_out_of_a_sentence()
        {
            var cleaner = ValueCleaner.Of(ValueCleaner.FirstNumber, null, out var problem);

            Assert.Null(problem);
            Assert.Equal("12.5", cleaner.Clean("Volume: 12.5 m3 (net)"));
            Assert.Equal("4.2", cleaner.Clean("approx 4.2m2"));
            Assert.Equal("8.75", cleaner.Clean("V=8.75"));
            Assert.Equal("12,500", cleaner.Clean("12,500"));
            Assert.Equal("-4", cleaner.Clean("-4 mm"));
        }

        [Fact]
        public void A_capture_group_says_which_part_is_the_number()
        {
            var cleaner = ValueCleaner.Of(@"net\s*=\s*([\d.]+)", null, out _);

            Assert.Equal("8.5", cleaner.Clean("gross = 12.0, net = 8.5"));
        }

        [Fact]
        public void With_no_capture_group_the_whole_match_is_the_number()
        {
            var cleaner = ValueCleaner.Of(@"[\d.]+", null, out _);

            Assert.Equal("12.0", cleaner.Clean("gross = 12.0, net = 8.5"));
        }

        [Fact]
        public void A_pattern_that_matches_nothing_leaves_the_value_alone()
        {
            var cleaner = ValueCleaner.Of(@"net\s*=\s*([\d.]+)", null, out _);

            // Not blanked. The takeoff will count this unreadable and name it, and it can only name
            // it if the text it could not read survived.
            Assert.Equal("no numbers here", cleaner.Clean("no numbers here"));
        }

        [Fact]
        public void A_group_that_did_not_participate_falls_back_to_the_whole_match()
        {
            var cleaner = ValueCleaner.Of(@"(?:net=(\d+))|(?:\d+)", null, out _);

            Assert.Equal("42", cleaner.Clean("42"));
            Assert.Equal("7", cleaner.Clean("net=7"));
        }

        [Fact]
        public void A_bad_pattern_is_reported_and_the_cleaner_still_works()
        {
            var cleaner = ValueCleaner.Of("([unclosed", null, out var problem);

            Assert.NotNull(problem);
            Assert.Contains("not a valid regular expression", problem!);
            Assert.True(cleaner.DoesNothing);
            Assert.Equal("12.5", cleaner.Clean("12.5"));
        }

        [Fact]
        public void A_bad_pattern_keeps_the_assumed_unit_that_was_also_asked_for()
        {
            var cleaner = ValueCleaner.Of("([unclosed", "m3", out var problem);

            Assert.NotNull(problem);
            Assert.False(cleaner.DoesNothing);
            Assert.Equal("12.5 m3", cleaner.Clean("12.5"));
        }

        [Fact]
        public void An_assumed_unit_is_added_only_to_a_bare_number()
        {
            var cleaner = ValueCleaner.Of(null, "m3", out var problem);

            Assert.Null(problem);
            Assert.Equal("12.5 m3", cleaner.Clean("12.5"));
            Assert.Equal("12,500 m3", cleaner.Clean("12,500"));
            Assert.Equal("-4 m3", cleaner.Clean("-4"));

            // Already stated, so left alone. Appending would give "12.5 ft3 m3", which parses as
            // nothing, and a value that was fine would become unreadable the moment somebody set an
            // assumed unit.
            Assert.Equal("12.5 ft3", cleaner.Clean("12.5 ft3"));
            Assert.Equal("3'-6\"", cleaner.Clean("3'-6\""));
            Assert.Equal("not a number", cleaner.Clean("not a number"));
        }

        [Fact]
        public void The_pattern_runs_before_the_unit_is_assumed()
        {
            var cleaner = ValueCleaner.Of(ValueCleaner.FirstNumber, "m3", out _);

            Assert.Equal("12.5 m3", cleaner.Clean("Volume: 12.5 (net)"));
        }

        [Fact]
        public void A_unit_the_parser_does_not_know_is_refused_by_name()
        {
            var cleaner = ValueCleaner.Of(null, "furlongs", out var problem);

            Assert.NotNull(problem);
            Assert.Contains("furlongs", problem!);
            Assert.True(cleaner.DoesNothing);
        }

        [Fact]
        public void The_units_it_offers_are_the_units_the_parser_reads()
        {
            Assert.NotEmpty(ValueCleaner.AssumableUnits);

            foreach (var unit in ValueCleaner.AssumableUnits)
            {
                Assert.True(Quantity.TryParse("1 " + unit, out _),
                    "the picker offers \"" + unit + "\" but the parser cannot read it");
            }
        }

        [Fact]
        public void What_it_cleans_is_what_the_takeoff_can_then_read()
        {
            var cleaner = ValueCleaner.Of(ValueCleaner.FirstNumber, "m3", out _);

            var lines = new[]
            {
                new TakeoffLine("a", "Walls", cleaner.Clean("Volume: 12.5 m3 (net)")),
                new TakeoffLine("b", "Walls", cleaner.Clean("8")),
                new TakeoffLine("c", "Walls", cleaner.Clean("approx 4.5")),
            };

            var result = Takeoff.Sum(lines);

            Assert.True(result.IsComplete);
            Assert.Equal(0, result.Unreadable);
            Assert.Equal(QuantityKind.Volume, result.Total!.Value.Kind);
            Assert.Equal(25.0, result.Total!.Value.Value, 6);
        }

        [Fact]
        public void Blank_and_null_come_back_untouched()
        {
            var cleaner = ValueCleaner.Of(ValueCleaner.FirstNumber, "m3", out _);

            Assert.Null(cleaner.Clean(null));
            Assert.Equal(string.Empty, cleaner.Clean(string.Empty));
            Assert.Equal("   ", cleaner.Clean("   "));
        }

        [Fact]
        public void It_says_on_screen_what_it_is_doing()
        {
            var described = ValueCleaner.Of(ValueCleaner.FirstNumber, "m3", out _).Describe();

            Assert.Contains("the number is taken from", described);
            Assert.Contains("read as m3", described);

            Assert.Equal(ValueCleaner.FirstNumber,
                ValueCleaner.Of(ValueCleaner.FirstNumber, null, out _).Pattern);

            Assert.Equal("m3", ValueCleaner.Of(null, " m3 ", out _).AssumedUnit);
            Assert.Null(ValueCleaner.None.Pattern);
            Assert.Null(ValueCleaner.None.AssumedUnit);
        }

        [Fact]
        public void A_runaway_pattern_gives_up_on_the_value_rather_than_the_run()
        {
            // Classic catastrophic backtracking, on a subject with no match at the end.
            var cleaner = ValueCleaner.Of(@"(a+)+b", null, out var problem);

            Assert.Null(problem);

            var subject = new string('a', 40) + "c";
            var cleaned = cleaner.Clean(subject);

            // Either it finished and matched nothing, or it timed out. Both leave the value as it
            // was; neither hangs the host.
            Assert.Equal(subject, cleaned);
            Assert.True(ValueCleaner.Patience.TotalMilliseconds > 0);
        }
    }
}
