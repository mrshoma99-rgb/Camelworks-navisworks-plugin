using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The tree's shape written down as properties, so everything downstream can join on it.
    /// </summary>
    public class BreakdownTests
    {
        private static KeyValuePair<ElementKey, IReadOnlyList<string>> At(string id, params string[] path) =>
            new KeyValuePair<ElementKey, IReadOnlyList<string>>(
                ElementKey.FromTreePath("mech", "M.nwc", id), path);

        [Fact]
        public void The_file_and_the_element_drop_out_leaving_the_shape_between()
        {
            var plan = Breakdown.Of(new[]
            {
                At("1", "M.nwc", "Zone A", "Supply air", "AHU-01", "Duct 3"),
            });

            var entry = plan.Entries.Single();

            // The outermost entry is the file, which every element in the model shares; the
            // innermost is the element's own name, which is not a breakdown level.
            Assert.Equal(new[] { "Zone A", "Supply air", "AHU-01" }, entry.Levels);
            Assert.Equal("Zone A.Supply air.AHU-01", entry.Composite);
        }

        [Fact]
        public void The_composite_is_what_a_schedule_joins_on()
        {
            var plan = Breakdown.Of(
                new[] { At("1", "M.nwc", "Zone A", "Supply air", "Duct") },
                new BreakdownRecipe { Separator = " / " });

            Assert.Equal("Zone A / Supply air", plan.Entries.Single().Composite);
        }

        [Fact]
        public void Two_elements_under_one_branch_share_a_key()
        {
            var plan = Breakdown.Of(new[]
            {
                At("1", "M.nwc", "Zone A", "Supply air", "Duct 1"),
                At("2", "M.nwc", "Zone A", "Supply air", "Duct 2"),
                At("3", "M.nwc", "Zone B", "Supply air", "Duct 3"),
            });

            Assert.Equal(3, plan.Entries.Count);
            Assert.Equal(2, plan.Distinct);
        }

        [Fact]
        public void Deeper_than_asked_for_keeps_the_outermost_levels()
        {
            var plan = Breakdown.Of(
                new[] { At("1", "M.nwc", "A", "B", "C", "D", "E", "Duct") },
                new BreakdownRecipe { Levels = 2 });

            // The outer levels are the ones a schedule groups by; the inner ones are detail.
            Assert.Equal(new[] { "A", "B" }, plan.Entries.Single().Levels);
        }

        [Fact]
        public void An_element_with_nowhere_to_sit_is_counted_not_guessed_at()
        {
            var plan = Breakdown.Of(new[]
            {
                At("1", "M.nwc", "Duct"),
                At("2", "M.nwc", "Zone A", "Duct"),
            });

            // The first has only a file and its own name, so nothing is left between them.
            Assert.Single(plan.Entries);
            Assert.Equal(1, plan.Shallow);
        }

        [Fact]
        public void An_unnamed_group_keeps_its_place_rather_than_closing_the_gap()
        {
            var plan = Breakdown.Of(new[]
            {
                At("1", "M.nwc", "Zone A", "", "AHU-01", "Duct"),
                At("2", "M.nwc", "Zone A", "Supply", "AHU-01", "Duct"),
            });

            // Closing the gap would put AHU-01 at level 2 in one and level 3 in the other, and the
            // two would come out with keys that cannot be compared.
            Assert.Equal(new[] { "Zone A", "(unnamed)", "AHU-01" }, plan.Entries[0].Levels);
            Assert.Equal(3, plan.Entries[0].Levels.Count);
            Assert.Equal(3, plan.Entries[1].Levels.Count);
        }

        [Fact]
        public void Keeping_the_root_is_a_choice_for_a_single_model()
        {
            var plan = Breakdown.Of(
                new[] { At("1", "M.nwc", "Zone A", "Duct") },
                new BreakdownRecipe { SkipRoot = false });

            Assert.Equal(new[] { "M.nwc", "Zone A" }, plan.Entries.Single().Levels);
        }

        [Fact]
        public void Keeping_the_leaf_is_a_choice_too()
        {
            var plan = Breakdown.Of(
                new[] { At("1", "M.nwc", "Zone A", "Duct 3") },
                new BreakdownRecipe { SkipLeaf = false });

            Assert.Equal(new[] { "Zone A", "Duct 3" }, plan.Entries.Single().Levels);
        }

        [Fact]
        public void The_deepest_reach_is_reported_so_the_level_count_can_be_chosen()
        {
            var plan = Breakdown.Of(new[]
            {
                At("1", "M.nwc", "A", "Duct"),
                At("2", "M.nwc", "A", "B", "C", "Duct"),
            });

            Assert.Equal(3, plan.Deepest);
        }

        [Fact]
        public void Level_properties_are_named_from_the_prefix()
        {
            Assert.Equal("WBS 1", Breakdown.LevelName(null, 1));
            Assert.Equal("Zone 3", Breakdown.LevelName(new BreakdownRecipe { Prefix = "Zone" }, 3));
            Assert.Equal("WBS 2", Breakdown.LevelName(new BreakdownRecipe { Prefix = "  " }, 2));
        }

        [Fact]
        public void Nothing_in_is_nothing_out_without_complaining()
        {
            var plan = Breakdown.Of(null);

            Assert.True(plan.IsEmpty);
            Assert.Contains("Nothing to break down", plan.Explain());
        }
    }
}
