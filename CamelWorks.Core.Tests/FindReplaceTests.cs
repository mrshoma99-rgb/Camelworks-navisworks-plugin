using System;
using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Correcting a value everywhere it appears — the property somebody spelled two ways, which
    /// makes every filter and every schedule join quietly cover half of what it should.
    /// </summary>
    public class FindReplaceTests
    {
        private static Replacement Rule(string find, string with, MatchKind kind = MatchKind.Part,
                                        bool cased = false) =>
            new Replacement
            {
                Category = "Element", Name = "Discipline",
                Find = find, ReplaceWith = with, Kind = kind, CaseSensitive = cased,
            };

        [Fact]
        public void Every_occurrence_goes_not_just_the_first()
        {
            Assert.Equal("Mechanical duct Mechanical",
                         FindReplace.Apply("MECH duct MECH", Rule("MECH", "Mechanical")));
        }

        [Fact]
        public void Replacing_text_with_more_of_itself_terminates()
        {
            // Searching from inside the replacement instead of past it grows forever.
            Assert.Equal("aaaaaa", FindReplace.Apply("aaa", Rule("a", "aa")));
        }

        [Fact]
        public void Case_is_ignored_unless_it_is_asked_for()
        {
            Assert.Equal("Level 02", FindReplace.Apply("L02", Rule("l02", "Level 02")));
            Assert.Null(FindReplace.Apply("L02", Rule("l02", "Level 02", cased: true)));
        }

        [Fact]
        public void A_value_the_rule_does_not_touch_comes_back_null()
        {
            // Null rather than the unchanged value, so a caller can tell "did nothing here" from
            // "produced the same text" — and the second is worth reporting.
            Assert.Null(FindReplace.Apply("Structural", Rule("MECH", "Mechanical")));
        }

        [Fact]
        public void A_whole_match_replaces_the_whole_value()
        {
            Assert.Equal("Mechanical", FindReplace.Apply("MECH", Rule("MECH", "Mechanical", MatchKind.Whole)));
            Assert.Null(FindReplace.Apply("MECH duct", Rule("MECH", "Mechanical", MatchKind.Whole)));
        }

        [Fact]
        public void A_start_or_end_match_keeps_the_rest_of_the_value()
        {
            Assert.Equal("Level 02 duct",
                         FindReplace.Apply("L02 duct", Rule("L02", "Level 02", MatchKind.Start)));

            Assert.Equal("duct trimmed",
                         FindReplace.Apply("duct SUFFIX", Rule("SUFFIX", "trimmed", MatchKind.End)));
        }

        [Fact]
        public void A_trailing_space_can_be_taken_off()
        {
            // The one that breaks a schedule join and is invisible in every list the host shows.
            Assert.Equal("MECH", FindReplace.Apply("MECH ", Rule(" ", "", MatchKind.End)));
        }

        [Theory]
        [InlineData("AHU-01", "AHU-*", true)]
        [InlineData("AHU-01", "*-01", true)]
        [InlineData("AHU-01", "AHU-??", true)]
        [InlineData("AHU-01", "AHU-?", false)]
        [InlineData("AHU-01", "ahu-*", true)]
        [InlineData("Duct", "*u*t", true)]
        [InlineData("aaa", "*a", true)]
        [InlineData("xy", "*z*", false)]
        [InlineData("", "*", true)]
        [InlineData("abc", "", false)]
        public void A_pattern_describes_the_whole_value(string value, string pattern, bool matches)
        {
            Assert.Equal(matches, FindReplace.Matches(value, pattern, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_pattern_match_replaces_the_whole_value()
        {
            // Replacing "the matched part" means nothing when the pattern is what matched.
            Assert.Equal("Air handling",
                         FindReplace.Apply("AHU-01", Rule("AHU-*", "Air handling", MatchKind.Pattern)));
        }

        [Fact]
        public void An_empty_search_is_refused_rather_than_matching_everything()
        {
            var rule = Rule("", "anything");

            Assert.False(rule.IsUsable);
            Assert.Contains("every value", rule.Refusal);
            Assert.Null(FindReplace.Apply("MECH", rule));
        }

        [Fact]
        public void A_rule_naming_no_property_is_refused()
        {
            var rule = new Replacement { Find = "a", ReplaceWith = "b" };

            Assert.False(rule.IsUsable);
            Assert.Contains("which property", rule.Refusal);
        }

        [Fact]
        public void A_match_that_changes_nothing_is_counted_separately()
        {
            var count = FindReplace.Rehearse(
                new[] { "Mechanical", "MECH", "Structural" },
                Rule("MECH", "Mechanical", MatchKind.Whole));

            // "Mechanical" does not match a Whole search for "MECH", so one match, one change.
            Assert.Equal(3, count.Scope);
            Assert.Equal(1, count.Matched);
            Assert.Equal(1, count.Changed);
        }

        [Fact]
        public void Already_corrected_values_are_reported_rather_than_looking_like_a_failure()
        {
            var count = FindReplace.Rehearse(
                new[] { "Mechanical", "Mechanical", "MECH" },
                Rule("MECH", "Mechanical"));

            // The two that already read that way matched and changed nothing. Saying so is what
            // stops a reader concluding the tool did not work.
            Assert.Equal(3, count.Matched);
            Assert.Equal(1, count.Changed);
            Assert.Contains("already read that way", count.Explain());
        }

        [Fact]
        public void Nothing_matching_says_how_much_it_looked_at()
        {
            var count = FindReplace.Rehearse(new[] { "a", "b" }, Rule("zzz", "x"));

            Assert.Equal(0, count.Matched);
            Assert.Contains("out of 2 values", count.Explain());
        }

        [Fact]
        public void The_examples_are_capped_but_the_counts_are_not()
        {
            var many = Enumerable.Range(0, 40).Select(i => "MECH " + i).ToList();
            var count = FindReplace.Rehearse(many, Rule("MECH", "Mechanical"));

            Assert.Equal(40, count.Changed);
            Assert.Equal(8, count.Examples.Count);
        }
    }
}
