using System.Linq;
using CamelWorks.Core.Sets;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SetFamilyTests
    {
        private static readonly string[] Levels =
        {
            "Level 1", "Level 1", "Level 1", "Level 2", "Level 2", "Level 3",
        };

        [Fact]
        public void One_set_per_distinct_value_largest_first()
        {
            var plan = SetFamily.Of("Element", "Level", Levels);

            Assert.Equal(new[] { "Level 1", "Level 2", "Level 3" }, plan.Sets.Select(s => s.Name));
            Assert.Equal(new[] { 3, 2, 1 }, plan.Sets.Select(s => s.Elements));
            Assert.Equal(3, plan.Selected);
        }

        [Fact]
        public void Each_set_is_a_rule_rather_than_a_list()
        {
            // The whole difference between a family that stays true and one that has to be
            // regenerated after every model issue.
            var set = SetFamily.Of("Element", "Level", Levels).Sets.First();

            var condition = Assert.IsType<ConditionExpression>(set.Expression);

            Assert.Equal("Element", condition.Condition.Category);
            Assert.Equal("Level", condition.Condition.Property);
            Assert.Equal(SetOperator.Equals, condition.Condition.Operator);
            Assert.Equal("Level 1", condition.Condition.Value);
        }

        [Fact]
        public void Case_does_not_make_two_levels()
        {
            // "Level 2" and "LEVEL 2" are one level on every job that has ever existed.
            var plan = SetFamily.Of("Element", "Level", new[] { "Level 2", "LEVEL 2", "level 2" });

            var only = Assert.Single(plan.Sets);
            Assert.Equal(3, only.Elements);
            Assert.Equal("Level 2", only.Name);
        }

        [Fact]
        public void Elements_with_no_value_are_reported_not_collected()
        {
            // An element with no level is not a member of a level called nothing. A set that
            // quietly collects them makes the gap look handled.
            var plan = SetFamily.Of("Element", "Level", new[] { "Level 1", null, "", "   ", "Level 1" });

            Assert.Equal(3, plan.Blanks);
            Assert.Single(plan.Sets);
            Assert.Contains("3 elements carry no value", plan.Explain());
        }

        [Fact]
        public void A_name_the_library_already_has_is_shown_but_not_ticked()
        {
            var plan = SetFamily.Of("Element", "Level", Levels,
                new FamilyOptions { Existing = new[] { "Level 1" } });

            var known = plan.Sets.Single(s => s.Name == "Level 1");

            Assert.False(known.IsSelected);
            Assert.Contains("already in the library", known.Note);
            Assert.Equal(2, plan.Selected);
        }

        [Fact]
        public void A_value_almost_nothing_carries_is_proposed_but_not_ticked()
        {
            var plan = SetFamily.Of("Element", "Level", Levels, new FamilyOptions { Floor = 2 });

            var thin = plan.Sets.Single(s => s.Name == "Level 3");

            Assert.False(thin.IsSelected);
            Assert.Contains("only 1 element carries this", thin.Note);
            Assert.Equal(2, plan.Selected);
        }

        [Fact]
        public void The_name_template_is_filled_in()
        {
            var plan = SetFamily.Of("Element", "Level", new[] { "02" },
                new FamilyOptions { NamePattern = "{n}. L{value} - everything" });

            Assert.Equal("1. L02 - everything", plan.Sets.Single().Name);
        }

        [Fact]
        public void Two_values_that_would_share_a_name_are_told_apart()
        {
            var plan = SetFamily.Of("Element", "Level", new[] { "a", "b" },
                new FamilyOptions { NamePattern = "Zone" });

            Assert.Equal(2, plan.Sets.Select(s => s.Name).Distinct().Count());
        }

        [Fact]
        public void Past_the_cap_the_rest_are_counted_rather_than_produced()
        {
            var values = Enumerable.Range(0, 30).Select(n => "value " + n).ToList();
            var plan = SetFamily.Of("Element", "Level", values, new FamilyOptions { Cap = 5 });

            Assert.Equal(5, plan.Sets.Count);
            Assert.Equal(25, plan.Dropped);
            Assert.Contains("25 values beyond the cap", plan.Explain());
        }

        [Fact]
        public void Nothing_in_the_model_makes_an_empty_family_rather_than_an_error()
        {
            var plan = SetFamily.Of("Element", "Level", new string?[0]);

            Assert.Empty(plan.Sets);
            Assert.Equal(0, plan.Blanks);
            Assert.Equal(0, plan.Selected);
        }
    }
}
