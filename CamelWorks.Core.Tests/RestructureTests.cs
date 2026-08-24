using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Sets;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class RestructureTests
    {
        /// <summary>A level and a discipline, with a hole in each column.</summary>
        private static IReadOnlyList<IReadOnlyList<string?>> Federation() => new[]
        {
            new string?[] { "Level 01", "Mechanical" },
            new string?[] { "Level 01", "Mechanical" },
            new string?[] { "Level 01", "Electrical" },
            new string?[] { "Level 02", "Mechanical" },
            new string?[] { "Level 02", null },
            new string?[] { "Level 02", "  " },
            new string?[] { null, "Structural" },
        };

        private static RestructureRecipe Two(MissingValues level = MissingValues.Gather,
                                             MissingValues discipline = MissingValues.Gather)
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Level").Missing = level;
            recipe.Add("Item", "Discipline").Missing = discipline;
            return recipe;
        }

        [Fact]
        public void Every_element_lands_somewhere_when_the_holes_are_gathered()
        {
            var plan = Restructure.Plan(Two(), Federation());

            Assert.Null(plan.Refusal);
            Assert.True(plan.IsUsable);

            Assert.Equal(7, plan.Elements);
            Assert.Equal(7, plan.Placed);
            Assert.Equal(0, plan.Dropped);
            Assert.True(plan.IsWhole);

            Assert.Equal(
                new[]
                {
                    "Level 01 / Mechanical",
                    "Level 01 / Electrical",
                    "Level 02 / Mechanical",
                    "Level 02 / Not set",
                    "Not set / Structural",
                },
                plan.Sets.Select(s => s.FullName));

            Assert.Equal(new[] { 2, 1, 1, 2, 1 }, plan.Sets.Select(s => s.Elements));

            // Three folders: Level 01, Level 02, and the gathered one.
            Assert.Equal(3, plan.Folders);
        }

        [Fact]
        public void A_hole_is_counted_at_the_level_it_appeared()
        {
            var plan = Restructure.Plan(Two(), Federation());

            Assert.Equal(new[] { "Element / Level", "Item / Discipline" },
                         plan.Tallies.Select(t => t.Level.Address));

            // One element has no Level; two have no Discipline - the null and the two spaces.
            Assert.Equal(new[] { 1, 2 }, plan.Tallies.Select(t => t.Missing));
        }

        [Fact]
        public void A_level_told_to_leave_its_holes_drops_those_elements_and_says_so()
        {
            var plan = Restructure.Plan(Two(level: MissingValues.Leave), Federation());

            Assert.Equal(7, plan.Elements);
            Assert.Equal(6, plan.Placed);
            Assert.Equal(1, plan.Dropped);
            Assert.False(plan.IsWhole);

            Assert.DoesNotContain(plan.Sets, s => s.FullName.StartsWith("Not set", StringComparison.Ordinal));
            Assert.Contains("no value for a level", plan.Explain());
        }

        [Fact]
        public void The_gathered_branch_sorts_last_because_a_hole_is_not_a_finding()
        {
            var plan = Restructure.Plan(Two(), Federation());

            Assert.Equal(new[] { "Level 01", "Level 02", "Not set" },
                         plan.Roots.Select(r => r.Value));
        }

        [Fact]
        public void A_real_value_spelled_like_the_bucket_does_not_swallow_the_holes()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Zone");

            var plan = Restructure.Plan(recipe, new[]
            {
                new string?[] { "Not set" },
                new string?[] { "Not set" },
                new string?[] { null },
                new string?[] { "Zone A" },
            });

            // Two sets called "Not set" would be one set in the library, holding whichever was
            // published second. The hole gets a name of its own instead.
            Assert.Equal(new[] { "Not set", "Zone A", "Not set (no value)" },
                         plan.Sets.Select(s => s.Name));

            Assert.Equal(new[] { 2, 1, 1 }, plan.Sets.Select(s => s.Elements));

            Assert.Single(plan.Warnings);
            Assert.Contains("Not set (no value)", plan.Warnings[0]);
        }

        [Fact]
        public void The_gathered_set_looks_for_both_ways_a_property_can_be_absent()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Zone");

            var plan = Restructure.Plan(recipe, new[]
            {
                new string?[] { "Zone A" },
                new string?[] { null },
            });

            var hole = plan.Sets.Single(s => s.Name == "Not set");
            var described = hole.Expression!.Describe();

            // The preview counted the absent and the present-but-empty together, so the published
            // set has to find both or it comes back smaller than the number on screen.
            Assert.Contains("Zone", described, StringComparison.Ordinal);
            Assert.Contains(" or ", described, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_leaf_carries_every_level_so_it_survives_being_dragged_out_of_its_folder()
        {
            var plan = Restructure.Plan(Two(), Federation());

            var ducts = plan.Sets.Single(s => s.FullName == "Level 01 / Mechanical");
            var described = ducts.Expression!.Describe();

            Assert.Contains("Level", described, StringComparison.Ordinal);
            Assert.Contains("Mechanical", described, StringComparison.Ordinal);
        }

        [Fact]
        public void Each_level_reads_its_own_property_tab()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Level");
            recipe.Add("Project Data", "Package");

            var plan = Restructure.Plan(recipe, new[]
            {
                new string?[] { "Level 01", "P-01" },
            });

            var described = plan.Sets.Single().Expression!.Describe();

            Assert.Contains("Element", described, StringComparison.Ordinal);
            Assert.Contains("Project Data", described, StringComparison.Ordinal);
        }

        [Fact]
        public void A_branch_under_the_floor_is_left_out_and_counted()
        {
            var recipe = new RestructureRecipe { Floor = 2 };
            recipe.Add("Element", "Discipline");

            var plan = Restructure.Plan(recipe, new[]
            {
                new string?[] { "A" }, new string?[] { "A" }, new string?[] { "A" },
                new string?[] { "B" }, new string?[] { "C" },
            });

            Assert.Equal(new[] { "A" }, plan.Sets.Select(s => s.Name));
            Assert.Equal(3, plan.Placed);
            Assert.Equal(2, plan.Small);
            Assert.False(plan.IsWhole);
            Assert.Contains("under the floor", plan.Explain());
        }

        [Fact]
        public void The_root_folder_holds_the_whole_tree()
        {
            var recipe = Two();
            recipe.Root = "Restructure";

            var plan = Restructure.Plan(recipe, Federation());

            Assert.Single(plan.Roots);
            Assert.Equal("Restructure", plan.Roots[0].Value);
            Assert.Equal(7, plan.Roots[0].Elements);

            // Three level folders and the root above them.
            Assert.Equal(4, plan.Folders);

            Assert.StartsWith("Restructure / ", plan.Sets[0].FullName, StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_tree_is_not_wrapped_in_an_empty_root_folder()
        {
            var recipe = Two(level: MissingValues.Leave);
            recipe.Root = "Restructure";

            var plan = Restructure.Plan(recipe, new[] { new string?[] { null, "Mechanical" } });

            Assert.Empty(plan.Roots);
            Assert.False(plan.IsUsable);
            Assert.Contains("carry nothing for a level", plan.Explain());
        }

        [Fact]
        public void Nothing_to_split_on_is_a_refusal_that_says_what_to_do()
        {
            var plan = Restructure.Plan(new RestructureRecipe(), Federation());

            Assert.NotNull(plan.Refusal);
            Assert.Contains("at least one level", plan.Refusal!);
            Assert.False(plan.IsUsable);
        }

        [Fact]
        public void Five_levels_is_a_refusal_rather_than_a_truncation()
        {
            var recipe = new RestructureRecipe();

            foreach (var name in new[] { "A", "B", "C", "D", "E" }) recipe.Add("Element", name);

            var plan = Restructure.Plan(recipe, Federation());

            Assert.NotNull(plan.Refusal);
            Assert.Contains("Four levels", plan.Refusal!);
        }

        [Fact]
        public void The_same_property_twice_is_a_refusal_because_it_deepens_nothing()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Level");
            recipe.Add("element", "level");

            var plan = Restructure.Plan(recipe, Federation());

            Assert.NotNull(plan.Refusal);
            Assert.Contains("two levels", plan.Refusal!);
        }

        [Fact]
        public void Past_the_ceiling_it_makes_nothing_and_says_how_many_it_would_have_made()
        {
            var recipe = new RestructureRecipe { Ceiling = 3 };
            recipe.Add("Element", "Tag");

            var rows = Enumerable.Range(1, 10)
                .Select(n => (IReadOnlyList<string?>)new string?[] { "T" + n })
                .ToList();

            var plan = Restructure.Plan(recipe, rows);

            Assert.NotNull(plan.Refusal);
            Assert.Contains("10 sets", plan.Refusal!);
            Assert.Contains("ceiling is 3", plan.Refusal!);
            Assert.Empty(plan.Sets);
        }

        [Fact]
        public void An_empty_scope_says_so_rather_than_refusing()
        {
            var plan = Restructure.Plan(Two(), Array.Empty<IReadOnlyList<string?>>());

            Assert.Null(plan.Refusal);
            Assert.Equal("Nothing in scope.", plan.Explain());
            Assert.False(plan.IsWhole);
        }

        [Fact]
        public void A_null_row_list_is_an_empty_scope_not_a_crash()
        {
            var plan = Restructure.Plan(Two(), null);

            Assert.Null(plan.Refusal);
            Assert.Equal(0, plan.Elements);
        }

        [Fact]
        public void A_re_run_on_a_newer_model_says_what_moved()
        {
            var before = Restructure.Plan(Two(), Federation());

            var after = Restructure.Plan(Two(), new[]
            {
                new string?[] { "Level 01", "Mechanical" },
                new string?[] { "Level 01", "Mechanical" },
                new string?[] { "Level 01", "Mechanical" },   // grew by one
                new string?[] { "Level 01", "Electrical" },
                new string?[] { "Level 02", "Mechanical" },
                new string?[] { "Level 02", null },
                new string?[] { "Level 02", "  " },
                new string?[] { null, "Structural" },
                new string?[] { "Level 03", "Mechanical" },   // a new level
            });

            var delta = Restructure.Compare(before, after);

            Assert.False(delta.IsQuiet);

            Assert.Equal(new[] { "Level 03 / Mechanical" }, delta.Added.Select(c => c.Name));
            Assert.Empty(delta.Removed);

            var grew = Assert.Single(delta.Resized);
            Assert.Equal("Level 01 / Mechanical", grew.Name);
            Assert.Equal(2, grew.Before);
            Assert.Equal(3, grew.After);
            Assert.Equal(1, grew.Moved);

            Assert.Equal(4, delta.Same);
            Assert.Contains("1 new", delta.Explain());
        }

        [Fact]
        public void A_set_whose_value_has_left_the_model_reads_as_gone()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Discipline");

            var before = Restructure.Plan(recipe, new[]
            {
                new string?[] { "Mechanical" }, new string?[] { "Plumbing" },
            });

            var after = Restructure.Plan(recipe, new[] { new string?[] { "Mechanical" } });

            var delta = Restructure.Compare(before, after);

            var gone = Assert.Single(delta.Removed);
            Assert.Equal("Plumbing", gone.Name);
            Assert.True(gone.IsGone);
            Assert.Equal(1, gone.Before);
            Assert.Equal(0, gone.After);
        }

        [Fact]
        public void The_first_run_has_nothing_to_compare_against()
        {
            var after = Restructure.Plan(Two(), Federation());

            var delta = Restructure.Compare(null, after);

            Assert.Equal(5, delta.Added.Count);
            Assert.Equal(0, delta.Same);
            Assert.All(delta.Added, c => Assert.True(c.IsNew));
        }

        [Fact]
        public void An_identical_re_run_is_quiet()
        {
            var delta = Restructure.Compare(Restructure.Plan(Two(), Federation()),
                                            Restructure.Plan(Two(), Federation()));

            Assert.True(delta.IsQuiet);
            Assert.Equal(5, delta.Same);
            Assert.Contains("all the same size", delta.Explain());
        }

        [Fact]
        public void Comparing_two_empty_runs_says_there_is_nothing_to_compare()
        {
            var delta = Restructure.Compare(null, null);

            Assert.True(delta.IsQuiet);
            Assert.Equal("Nothing to compare against.", delta.Explain());
        }

        [Fact]
        public void A_recipe_survives_the_project_file()
        {
            var recipe = new RestructureRecipe
            {
                Name = "By package",
                Root = "Packages",
                Floor = 4,
                Ceiling = 250,
                NamePattern = "{path} - {value}",
            };

            recipe.Add("Element", "Level").Missing = MissingValues.Leave;

            var second = recipe.Add("Project Data", "Package");
            second.GatherAs = "Unpackaged";

            var read = RestructureRecipe.FromJson(JsonReader.Parse(recipe.ToJson().ToString()));

            Assert.Equal("By package", read.Name);
            Assert.Equal("Packages", read.Root);
            Assert.Equal(4, read.Floor);
            Assert.Equal(250, read.Ceiling);
            Assert.Equal("{path} - {value}", read.NamePattern);

            Assert.Equal(2, read.Levels.Count);
            Assert.Equal("Element", read.Levels[0].Category);
            Assert.Equal(MissingValues.Leave, read.Levels[0].Missing);
            Assert.Equal("Project Data", read.Levels[1].Category);
            Assert.Equal("Package", read.Levels[1].Property);
            Assert.Equal("Unpackaged", read.Levels[1].GatherAs);
            Assert.Equal(MissingValues.Gather, read.Levels[1].Missing);
        }

        [Fact]
        public void An_older_project_file_missing_the_newer_keys_reads_as_defaults()
        {
            var read = RestructureRecipe.FromJson(
                JsonReader.Parse("{\"levels\":[{\"property\":\"Level\"}]}"));

            Assert.Equal("Restructure", read.Name);
            Assert.Null(read.Root);
            Assert.Equal(RestructureRecipe.DefaultCeiling, read.Ceiling);

            var level = Assert.Single(read.Levels);
            Assert.Equal("Element", level.Category);
            Assert.Equal("Level", level.Property);
            Assert.Equal(MissingValues.Gather, level.Missing);
            Assert.Equal(TreeLevel.DefaultGatherAs, level.GatherAs);
        }

        [Fact]
        public void A_copied_recipe_shares_nothing_with_the_one_that_is_running()
        {
            var recipe = Two();
            var copy = recipe.Copy();

            copy.Levels[0].Property = "Storey";
            copy.Root = "Elsewhere";

            Assert.Equal("Level", recipe.Levels[0].Property);
            Assert.Null(recipe.Root);
            Assert.Equal("Storey", copy.Levels[0].Property);
        }

        [Fact]
        public void A_half_filled_level_is_ignored_rather_than_guessed_at()
        {
            var recipe = new RestructureRecipe();
            recipe.Add("Element", "Level");
            recipe.Add("Element", string.Empty);

            Assert.Equal(2, recipe.Levels.Count);
            Assert.Single(recipe.Filled);

            var plan = Restructure.Plan(recipe, Federation());

            Assert.Null(plan.Refusal);
            Assert.Equal(new[] { "Level 01", "Level 02", "Not set" }, plan.Sets.Select(s => s.Name));
        }

        [Fact]
        public void The_name_pattern_names_sets_and_leaves_folders_alone()
        {
            var recipe = Two();
            recipe.NamePattern = "SS - {value}";

            var plan = Restructure.Plan(recipe, Federation());

            Assert.Equal("Level 01", plan.Roots[0].Name);
            Assert.StartsWith("SS - ", plan.Roots[0].Children[0].Name, StringComparison.Ordinal);
        }

        [Fact]
        public void A_run_recorded_in_the_project_file_is_what_the_next_one_compares_against()
        {
            var friday = Restructure.Snapshot(Restructure.Plan(Two(), Federation()), 637_000_000_000_000_000L);

            Assert.Equal(5, friday.Count);
            Assert.Equal(637_000_000_000_000_000L, friday.WhenTicks);
            Assert.False(friday.IsEmpty);

            // Through the project file and back, because that is the only way it ever arrives.
            var read = RestructureSnapshot.FromJson(JsonReader.Parse(friday.ToJson().ToString()));

            Assert.Equal(friday.Count, read.Count);
            Assert.Equal(friday.WhenTicks, read.WhenTicks);
            Assert.Equal(2, read.Sizes["Level 01 / Mechanical"]);

            var newer = Federation().ToList();
            newer.Add(new string?[] { "Level 03", "Mechanical" });

            var tuesday = Restructure.Plan(Two(), newer);

            var delta = Restructure.Since(read, tuesday);

            Assert.Equal(new[] { "Level 03 / Mechanical" }, delta.Added.Select(c => c.Name));
            Assert.Equal(5, delta.Same);
        }

        [Fact]
        public void With_nothing_recorded_every_set_is_new()
        {
            var delta = Restructure.Since(null, Restructure.Plan(Two(), Federation()));

            Assert.Equal(5, delta.Added.Count);
            Assert.Equal(0, delta.Same);
        }

        [Fact]
        public void An_empty_snapshot_holds_nothing_and_says_so()
        {
            Assert.True(RestructureSnapshot.Empty.IsEmpty);
            Assert.Equal(0L, RestructureSnapshot.Empty.WhenTicks);
            Assert.True(RestructureSnapshot.FromJson(null).IsEmpty);
            Assert.True(RestructureSnapshot.FromJson(JsonReader.Parse("{}")).IsEmpty);
        }

        [Fact]
        public void Every_generated_set_carries_its_formula_in_its_comment()
        {
            var plan = Restructure.Plan(Two(), Federation());

            foreach (var leaf in plan.Sets)
                Assert.StartsWith("CamelWorks: ", SetTree.Comment(leaf), StringComparison.Ordinal);
        }
    }
}
