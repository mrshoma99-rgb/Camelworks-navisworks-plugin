using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class LevelPlanTests
    {
        private static LevelPlan Built()
        {
            var plan = new LevelPlan();

            plan.Add("Ground floor", 0);
            plan.Add("Level 1", 5);
            plan.Add("Level 2", 9);

            return plan;
        }

        [Fact]
        public void A_height_falls_on_the_storey_it_is_at_or_above()
        {
            var plan = Built();

            Assert.Equal("Ground floor", plan.LevelAt(0));
            Assert.Equal("Ground floor", plan.LevelAt(4.999));
            Assert.Equal("Level 1", plan.LevelAt(5));
            Assert.Equal("Level 2", plan.LevelAt(40));
        }

        [Fact]
        public void Below_the_lowest_storey_is_nothing_rather_than_the_lowest()
        {
            // A basement clash is not a ground-floor clash. Pushing it into the lowest storey
            // would send somebody to the wrong floor with no way to tell.
            Assert.Null(Built().LevelAt(-1));
        }

        [Fact]
        public void Storeys_are_held_in_height_order_whatever_order_they_were_typed()
        {
            var plan = new LevelPlan();

            plan.Add("Level 2", 9);
            plan.Add("Ground floor", 0);
            plan.Add("Level 1", 5);

            Assert.Equal(new[] { "Ground floor", "Level 1", "Level 2" },
                         plan.Storeys.Select(one => one.Name));
        }

        [Fact]
        public void A_storey_runs_up_to_the_next_one_and_the_top_one_runs_on()
        {
            var plan = Built();

            Assert.Equal(5, plan.Ceiling("Ground floor"));
            Assert.Equal(9, plan.Ceiling("Level 1"));
            Assert.Null(plan.Ceiling("Level 2"));
        }

        [Fact]
        public void Two_storeys_at_one_height_are_refused()
        {
            var plan = Built();

            // The upper one would be permanently empty, which reads on screen as a level nobody
            // built anything on.
            Assert.False(plan.Add("Mezzanine", 5));
            Assert.Equal(3, plan.Storeys.Count);
        }

        [Fact]
        public void A_name_already_used_is_refused_whatever_its_case()
        {
            var plan = Built();

            Assert.False(plan.Add("level 1", 12));
            Assert.Equal(3, plan.Storeys.Count);
        }

        [Fact]
        public void Raising_a_storey_puts_it_back_in_order()
        {
            var plan = Built();

            Assert.True(plan.Raise("Ground floor", 20));
            Assert.Equal("Ground floor", plan.Storeys[2].Name);
            Assert.Equal("Level 1", plan.LevelAt(6));
        }

        [Fact]
        public void Renaming_keeps_the_height()
        {
            var plan = Built();

            Assert.True(plan.Rename("Level 1", "First floor"));
            Assert.Equal("First floor", plan.LevelAt(6));
            Assert.Equal(5, plan.Find("First floor")!.Elevation);
        }

        [Fact]
        public void It_writes_and_reads_back_the_same()
        {
            var read = LevelPlan.FromJson(JsonReader.Parse(Built().ToJson()!.ToJson()));

            Assert.Equal(3, read.Storeys.Count);
            Assert.Equal("Level 1", read.LevelAt(7));
            Assert.Equal(9, read.Find("Level 2")!.Elevation);
        }

        [Fact]
        public void A_plan_nobody_has_filled_in_writes_nothing()
        {
            Assert.Null(new LevelPlan().ToJson());
            Assert.True(new LevelPlan().IsEmpty);
            Assert.Null(new LevelPlan().LevelAt(3));
        }

        [Fact]
        public void The_level_set_it_builds_says_the_project_declared_it()
        {
            var levels = Built().Levels();

            // Above every derived source: a coordinator naming the storeys is stating the
            // building, not guessing at it from what the models happen to carry.
            Assert.Equal(LevelSource.Declared, levels.Source);
            Assert.True(levels.Separates);
            Assert.Equal(3, levels.Bands.Count);
        }

        [Fact]
        public void The_built_set_follows_an_edit()
        {
            var plan = Built();

            Assert.Equal("Level 1", plan.LevelAt(6));

            plan.Add("Mezzanine", 6);

            // No telling it anything: the cache notices the list moved on by itself.
            Assert.Equal("Mezzanine", plan.LevelAt(6));
        }
    }
}
