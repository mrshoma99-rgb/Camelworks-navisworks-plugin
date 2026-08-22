using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Sets;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SetTreeTests
    {
        /// <summary>Level, discipline, category — the shape every federation has.</summary>
        private static IEnumerable<IReadOnlyList<string?>> Federation() => new[]
        {
            new string?[] { "Level 01", "Mechanical", "Ducts" },
            new string?[] { "Level 01", "Mechanical", "Ducts" },
            new string?[] { "Level 01", "Mechanical", "Pipes" },
            new string?[] { "Level 01", "Structure", "Beams" },
            new string?[] { "Level 02", "Mechanical", "Ducts" },
            new string?[] { "Level 02", "Structure", "Beams" },
        };

        private static readonly string[] Three = { "Level", "Discipline", "Category" };

        [Fact]
        public void A_folder_per_level_a_folder_per_discipline_a_set_per_category()
        {
            var plan = SetTree.Of("Element", Three, Federation());

            Assert.Null(plan.Refusal);
            Assert.True(plan.IsUsable);

            Assert.Equal(new[] { "Level 01", "Level 02" }, plan.Roots.Select(r => r.Value));
            Assert.Equal(new[] { 4, 2 }, plan.Roots.Select(r => r.Elements));

            // Two levels of folder: two levels, each holding two disciplines, so six.
            Assert.Equal(6, plan.Folders);

            Assert.Equal(
                new[]
                {
                    "Level 01/Mechanical/Ducts",
                    "Level 01/Mechanical/Pipes",
                    "Level 01/Structure/Beams",
                    "Level 02/Mechanical/Ducts",
                    "Level 02/Structure/Beams",
                },
                plan.Sets.Select(s => string.Join("/", s.Path)).OrderBy(s => s, StringComparer.Ordinal));

            Assert.All(plan.Sets, s => Assert.True(s.IsLeaf));
            Assert.All(plan.Roots, r => Assert.False(r.IsLeaf));
        }

        [Fact]
        public void Biggest_branch_first_because_that_is_the_one_worth_having()
        {
            var plan = SetTree.Of("Element", new[] { "Level" }, Federation());

            Assert.Equal(new[] { "Level 01", "Level 02" }, plan.Sets.Select(s => s.Value));
            Assert.Equal(new[] { 4, 2 }, plan.Sets.Select(s => s.Elements));
        }

        [Fact]
        public void A_set_says_the_whole_path_so_it_survives_being_dragged_out_of_its_folder()
        {
            var plan = SetTree.Of("Element", Three, Federation());

            var ducts = plan.Sets.Single(s => string.Join("/", s.Path) == "Level 01/Mechanical/Ducts");
            var described = ducts.Expression!.Describe();

            Assert.Contains("Level 01", described);
            Assert.Contains("Mechanical", described);
            Assert.Contains("Ducts", described);

            // Not just the leaf condition: the folder is a place, not a filter, and a set that only
            // said "Category = Ducts" would silently mean every duct in the job once somebody moved
            // it.
            Assert.Contains(" and ", described, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void One_property_is_one_set_per_value_with_no_folder_at_all()
        {
            var plan = SetTree.Of("Element", new[] { "Level" }, Federation());

            Assert.Equal(0, plan.Folders);
            Assert.Equal(2, plan.Sets.Count);
            Assert.All(plan.Sets, s => Assert.Null(s.Folder));
            Assert.All(plan.Sets, s => Assert.Equal(s.Name, s.FullName));
        }

        [Fact]
        public void The_folder_is_the_path_without_the_set()
        {
            var plan = SetTree.Of("Element", Three, Federation());

            var ducts = plan.Sets.Single(s => string.Join("/", s.Path) == "Level 01/Mechanical/Ducts");

            Assert.Equal("Level 01/Mechanical", ducts.Folder);
            Assert.Equal("Level 01 / Mechanical / Ducts", ducts.FullName);
            Assert.Equal("Ducts", ducts.Name);
        }

        [Fact]
        public void Four_properties_is_refused_and_says_why()
        {
            var plan = SetTree.Of("Element", new[] { "Level", "Discipline", "Category", "Type" },
                                  Federation());

            Assert.NotNull(plan.Refusal);
            Assert.False(plan.IsUsable);
            Assert.Empty(plan.Sets);
            Assert.Contains("Three properties", plan.Refusal!);
            Assert.Equal(plan.Refusal, plan.Explain());
        }

        [Fact]
        public void No_properties_is_refused_rather_than_producing_one_set_of_everything()
        {
            var plan = SetTree.Of("Element", Array.Empty<string>(), Federation());

            Assert.NotNull(plan.Refusal);
            Assert.Contains("at least one property", plan.Refusal!);
        }

        [Fact]
        public void The_same_property_twice_counts_once()
        {
            var plan = SetTree.Of("Element", new[] { "Level", "level", "Level " }, Federation());

            Assert.Null(plan.Refusal);
            Assert.Equal(new[] { "Level" }, plan.Properties);
            Assert.Equal(0, plan.Folders);
        }

        [Fact]
        public void Past_the_ceiling_it_makes_nothing_rather_than_the_first_two_hundred()
        {
            var rows = Enumerable.Range(0, 40)
                .Select(i => (IReadOnlyList<string?>)new string?[] { "V" + i })
                .ToList();

            var plan = SetTree.Of("Element", new[] { "Level" }, rows, new SetTreeOptions { Ceiling = 10 });

            Assert.NotNull(plan.Refusal);
            Assert.Empty(plan.Sets);
            Assert.Contains("40 sets", plan.Refusal!);
            Assert.Contains("ceiling is 10", plan.Refusal!);
        }

        [Fact]
        public void Raising_the_ceiling_deliberately_lets_it_through()
        {
            var rows = Enumerable.Range(0, 40)
                .Select(i => (IReadOnlyList<string?>)new string?[] { "V" + i })
                .ToList();

            var plan = SetTree.Of("Element", new[] { "Level" }, rows, new SetTreeOptions { Ceiling = 100 });

            Assert.Null(plan.Refusal);
            Assert.Equal(40, plan.Sets.Count);
        }

        [Fact]
        public void The_floor_leaves_out_the_ones_holding_almost_nothing()
        {
            var plan = SetTree.Of("Element", new[] { "Level" }, Federation(),
                                  new SetTreeOptions { Floor = 3 });

            Assert.Equal(new[] { "Level 01" }, plan.Sets.Select(s => s.Value));

            // Still reports what it read, so "4 of 6" says plainly that two elements are in nothing.
            Assert.Equal(6, plan.Elements);
            Assert.Contains("4 of 6 elements", plan.Explain());
        }

        [Fact]
        public void A_blank_value_is_counted_and_left_out_rather_than_collected()
        {
            var rows = new[]
            {
                new string?[] { "Level 01", "Mechanical" },
                new string?[] { "Level 01", null },
                new string?[] { "Level 01", "   " },
                new string?[] { null, "Structure" },
            };

            var plan = SetTree.Of("Element", new[] { "Level", "Discipline" }, rows);

            Assert.Equal(4, plan.Elements);
            Assert.Equal(3, plan.Blanks);

            // One set, and it is the one row that had both values. No "(none)" branch: that set
            // cannot be written as a search the host will run, so the preview would promise a
            // count the published set could not find.
            var set = Assert.Single(plan.Sets);
            Assert.Equal("Level 01/Mechanical", string.Join("/", set.Path));
            Assert.Equal(1, set.Elements);

            Assert.Contains("1 of 4 elements", plan.Explain());
            Assert.Contains("3 carry nothing", plan.Explain());
            Assert.Contains("in none of them", plan.Explain());

            Assert.All(plan.Sets, s => Assert.DoesNotContain("(none)", s.Expression!.Describe()));
        }

        [Fact]
        public void Every_row_blank_reads_as_nothing_carrying_the_property()
        {
            var rows = new[]
            {
                new string?[] { null },
                new string?[] { "  " },
            };

            var plan = SetTree.Of("Element", new[] { "Level" }, rows);

            Assert.Null(plan.Refusal);
            Assert.False(plan.IsUsable);
            Assert.Equal(2, plan.Blanks);
            Assert.Contains("nothing in scope carries", plan.Explain());
        }

        [Fact]
        public void A_row_shorter_than_the_property_list_is_a_blank_not_a_crash()
        {
            var rows = new[]
            {
                new string?[] { "Level 01", "Mechanical" },
                new string?[] { "Level 01" },
                Array.Empty<string?>(),
            };

            var plan = SetTree.Of("Element", new[] { "Level", "Discipline" }, rows);

            Assert.Null(plan.Refusal);
            Assert.Equal(3, plan.Elements);
            Assert.Equal(2, plan.Blanks);
        }

        [Fact]
        public void Values_that_run_together_are_still_different_branches()
        {
            var rows = new[]
            {
                new string?[] { "AB", "C" },
                new string?[] { "A", "BC" },
            };

            var plan = SetTree.Of("Element", new[] { "One", "Two" }, rows);

            Assert.Equal(2, plan.Sets.Count);
            Assert.Equal(2, plan.Roots.Count);
        }

        [Fact]
        public void Nothing_in_scope_carries_the_property_and_it_says_so()
        {
            var plan = SetTree.Of("Element", new[] { "Level" }, Array.Empty<IReadOnlyList<string?>>());

            Assert.Null(plan.Refusal);
            Assert.False(plan.IsUsable);
            Assert.Contains("nothing in scope carries", plan.Explain());
        }

        [Fact]
        public void Leaves_walks_down_from_any_branch()
        {
            var plan = SetTree.Of("Element", Three, Federation());

            var levelOne = plan.Roots.Single(r => r.Value == "Level 01");

            Assert.Equal(3, levelOne.Leaves().Count());
            Assert.All(levelOne.Leaves(), leaf => Assert.Equal("Level 01", leaf.Path[0]));

            var leaf = plan.Sets[0];
            Assert.Same(leaf, Assert.Single(leaf.Leaves()));
        }

        [Fact]
        public void The_name_pattern_names_the_set_and_leaves_the_folders_alone()
        {
            var plan = SetTree.Of("Element", Three, Federation(),
                                  new SetTreeOptions { NamePattern = "SS - {value}" });

            var ducts = plan.Sets.Single(s => string.Join("/", s.Path) == "Level 01/Mechanical/Ducts");

            Assert.Equal("SS - Ducts", ducts.Name);
            Assert.Equal("Level 01 / Mechanical / SS - Ducts", ducts.FullName);
            Assert.Equal("Level 01", plan.Roots.Single(r => r.Value == "Level 01").Name);
        }

        [Fact]
        public void The_pattern_can_ask_for_the_path_and_does_not_leave_a_dangling_dash()
        {
            Assert.Equal("Level 01 / Mechanical - Ducts",
                SetTree.Name("{path} - {value}", "Ducts", new[] { "Level 01", "Mechanical" }));

            Assert.Equal("Ducts", SetTree.Name("{path} - {value}", "Ducts", Array.Empty<string>()));
            Assert.Equal("Ducts", SetTree.Name(null, "Ducts", null));
            Assert.Equal("Ducts", SetTree.Name("   ", "Ducts", null));

            // A pattern with nothing in it but tokens that came out empty still names the set.
            Assert.Equal("Ducts", SetTree.Name("{path}", "Ducts", Array.Empty<string>()));
        }

        [Fact]
        public void The_comment_is_the_formula_a_colleague_can_read_without_the_plug_in()
        {
            var plan = SetTree.Of("Element", Three, Federation());
            var ducts = plan.Sets.Single(s => string.Join("/", s.Path) == "Level 01/Mechanical/Ducts");

            var comment = SetTree.Comment(ducts);

            Assert.StartsWith("CamelWorks: ", comment);
            Assert.Contains("Ducts", comment);
            Assert.Equal("CamelWorks: " + ducts.Expression!.Describe(), comment);

            // A folder has no formula, and inventing one would be a lie about what it holds.
            Assert.Equal(string.Empty, SetTree.Comment(plan.Roots[0]));
            Assert.Equal(string.Empty, SetTree.Comment(null));
        }

        [Fact]
        public void Null_rows_are_read_as_nothing_at_all()
        {
            var plan = SetTree.Of("Element", new[] { "Level" }, null);

            Assert.Null(plan.Refusal);
            Assert.Equal(0, plan.Elements);
            Assert.Empty(plan.Sets);
        }

        [Fact]
        public void The_readout_counts_sets_folders_and_coverage()
        {
            var explained = SetTree.Of("Element", Three, Federation()).Explain();

            Assert.Contains("5 sets", explained);
            Assert.Contains("6 folders", explained);
            Assert.Contains("6 of 6 elements", explained);
        }
    }
}
