using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class FormulaTests
    {
        private static Func<string, string, string?> Element(params (string Path, string Value)[] properties)
        {
            var map = properties.ToDictionary(p => p.Path, p => p.Value, StringComparer.OrdinalIgnoreCase);

            return (category, name) =>
            {
                if (map.TryGetValue(category + "/" + name, out var exact)) return exact;

                // An empty category means "any tab", which is what a formula that only names a
                // property says.
                if (category.Length != 0) return null;

                foreach (var entry in map)
                {
                    var slash = entry.Key.LastIndexOf('/');
                    if (string.Equals(entry.Key.Substring(slash + 1), name, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                }

                return null;
            };
        }

        private static string? Value(string formula, params (string Path, string Value)[] properties) =>
            Formula.Parse(formula).Evaluate(Element(properties)).Value;

        [Fact]
        public void Two_properties_joined_with_something_in_the_middle()
        {
            Assert.Equal("D-12-L03",
                Value("\"D-\" & {Element/Mark} & \"-\" & {Element/Level}",
                    ("Element/Mark", "12"), ("Element/Level", "L03")));
        }

        [Fact]
        public void Plus_adds_and_ampersand_joins()
        {
            // One operator that did both would have to guess, and the guess is wrong exactly when
            // the values look like numbers but are not.
            Assert.Equal("15", Value("{a/x} + {a/y}", ("a/x", "12"), ("a/y", "3")));
            Assert.Equal("123", Value("{a/x} & {a/y}", ("a/x", "12"), ("a/y", "3")));
        }

        [Fact]
        public void Arithmetic_works_and_respects_precedence()
        {
            Assert.Equal("14", Value("2 + 3 * 4"));
            Assert.Equal("20", Value("(2 + 3) * 4"));
            Assert.Equal("2.5", Value("5 / 2"));
            Assert.Equal("-3", Value("-3"));
            Assert.Equal("1", Value("4 - 3"));
        }

        [Fact]
        public void A_property_that_reads_as_a_number_can_be_multiplied()
        {
            Assert.Equal("7.5", Value("{Element/Length} * {Element/Width}",
                ("Element/Length", "3"), ("Element/Width", "2.5")));
        }

        [Fact]
        public void A_missing_property_produces_nothing_rather_than_zero()
        {
            // An element that does not carry a width has no area. A zero would put a number into a
            // schedule that somebody adds up.
            var result = Formula.Parse("{Element/Length} * {Element/Width}")
                                .Evaluate(Element(("Element/Length", "3")));

            Assert.Null(result.Value);
            Assert.False(result.HasValue);
            Assert.Null(result.Problem);
            Assert.Equal(new PropertyRef("Element", "Width"), Assert.Single(result.Missing));
        }

        [Fact]
        public void A_blank_property_counts_as_missing()
        {
            var result = Formula.Parse("{a/x} & {a/y}").Evaluate(Element(("a/x", "1"), ("a/y", "   ")));

            Assert.Null(result.Value);
            Assert.Single(result.Missing);
        }

        [Fact]
        public void A_property_named_without_a_tab_is_looked_for_anywhere()
        {
            Assert.Equal("L03", Value("{Level}", ("Element/Level", "L03")));
        }

        [Fact]
        public void Text_that_is_not_a_number_says_so_rather_than_evaluating_to_something()
        {
            var result = Formula.Parse("{a/x} * 2").Evaluate(Element(("a/x", "wide")));

            Assert.Null(result.Value);
            Assert.NotNull(result.Problem);
            Assert.Contains("not a number", result.Problem);
            Assert.Contains("Use & to join it", result.Problem);
        }

        [Fact]
        public void Dividing_by_zero_is_a_problem_with_the_formula_not_a_missing_value()
        {
            var result = Formula.Parse("{a/x} / 0").Evaluate(Element(("a/x", "3")));

            Assert.Null(result.Value);
            Assert.Contains("divides by zero", result.Problem);
            Assert.Empty(result.Missing);
        }

        [Theory]
        [InlineData("", "There is no formula yet.")]
        [InlineData("   ", "There is no formula yet.")]
        [InlineData("{unclosed", "never closed")]
        [InlineData("{}", "names no property")]
        [InlineData("\"open", "never closed")]
        [InlineData("(1 + 2", "never closed")]
        [InlineData("1 +", "stops in the middle")]
        [InlineData("1 2", "after the end")]
        [InlineData("1 # 2", "not something a formula can contain")]
        [InlineData("* 2", "cannot start a value")]
        public void A_formula_that_cannot_be_read_says_why_and_does_not_throw(string text, string expected)
        {
            var formula = Formula.Parse(text);

            Assert.False(formula.IsUsable);
            Assert.Contains(expected, formula.Problem);
        }

        [Fact]
        public void An_unusable_formula_evaluates_to_its_problem_rather_than_crashing()
        {
            var result = Formula.Parse("{unclosed").Evaluate(Element());

            Assert.Null(result.Value);
            Assert.Contains("never closed", result.Problem);
        }

        [Fact]
        public void Every_property_it_reads_is_listed_once()
        {
            // So a screen can say what the formula needs before running it over a federation.
            var formula = Formula.Parse("{Element/Length} * {Element/Width} & \"-\" & {Element/Length}");

            Assert.True(formula.IsUsable);
            Assert.Equal(
                new[] { new PropertyRef("Element", "Length"), new PropertyRef("Element", "Width") },
                formula.References);
        }

        [Fact]
        public void A_number_reads_back_without_a_trailing_run_of_zeros()
        {
            Assert.Equal("3", Value("6 / 2"));
            Assert.Equal("0.3333", Value("1 / 3"));
        }

        [Fact]
        public void Whitespace_and_case_in_a_reference_do_not_matter()
        {
            Assert.Equal("L03", Value("{ element / level }", ("Element/Level", "L03")));
        }
    }
}
