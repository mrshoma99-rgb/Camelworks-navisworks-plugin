using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class PropertyDiffTests
    {
        private const string Tab = "CamelWorks";

        private static FakeDocument Model(out FakeSource source)
        {
            var document = new FakeDocument();
            source = document.AddModel("Architecture", @"C:\Jobs\Site\ARC.nwc");
            return document;
        }

        [Fact]
        public void The_shown_rows_are_capped_and_the_rows_to_write_are_not()
        {
            // Two screens wrote from Changes, which is the preview: a code table applied to eight
            // hundred elements landed on twenty-five, and the button said "Write 25 elements", so
            // nothing anywhere disagreed with anything else. Every is the work list.
            var document = Model(out var source);

            for (var i = 0; i < 90; i++) document.AddItem(source, "Wall-" + i);

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "North");

            Assert.Equal(90, diff.Added);
            Assert.Equal(PropertyDiff.DefaultExampleCap, diff.Changes.Count);
            Assert.Equal(90, diff.Every.Count);
            Assert.Equal(90, diff.Writes(ExistingValues.Keep));

            // And the shown rows really are the front of the full list, not a separate reading.
            Assert.Equal(diff.Changes.Select(c => c.Key),
                         diff.Every.Take(diff.Changes.Count).Select(c => c.Key));
        }

        [Fact]
        public void An_element_already_holding_the_value_is_in_neither_list()
        {
            var document = Model(out var source);

            document.AddItem(source, "Wall-01").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "Wall-02");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "North");

            Assert.Single(diff.Every);
            Assert.Equal("Wall-02", diff.Every[0].Label);
            Assert.Equal(1, diff.Unchanged);
        }

        [Fact]
        public void The_three_cases_are_counted_apart()
        {
            var document = Model(out var source);

            document.AddItem(source, "Wall-01");
            document.AddItem(source, "Wall-02").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "Wall-03").WithProperty(Tab, "Zone", "South");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "South");

            Assert.Equal(1, diff.Added);
            Assert.Equal(1, diff.Changed);
            Assert.Equal(1, diff.Unchanged);
            Assert.Equal(3, diff.Scope);
            Assert.True(diff.WouldOverwrite);
        }

        [Fact]
        public void Keeping_what_is_there_writes_only_the_empty_ones()
        {
            // The whole point: a second person running the same rule must not silently replace the
            // first person's answers.
            var document = Model(out var source);

            document.AddItem(source, "Wall-01");
            document.AddItem(source, "Wall-02").WithProperty(Tab, "Zone", "North");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "South");

            Assert.Equal(1, diff.Writes(ExistingValues.Keep));
            Assert.Equal(2, diff.Writes(ExistingValues.Overwrite));
        }

        [Fact]
        public void A_re_run_over_an_unchanged_model_is_no_writes_at_all()
        {
            var document = Model(out var source);

            for (var n = 0; n < 5; n++)
                document.AddItem(source, "Wall-0" + n).WithProperty(Tab, "Zone", "North");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "North");

            Assert.Equal(5, diff.Unchanged);
            Assert.True(diff.IsEmpty);
            Assert.Equal(0, diff.Writes(ExistingValues.Overwrite));
            Assert.Empty(diff.Changes);
        }

        [Fact]
        public void Emptying_a_value_is_its_own_case()
        {
            var document = Model(out var source);

            document.AddItem(source, "Wall-01").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "Wall-02");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), (string?)null);

            Assert.Equal(1, diff.Cleared);
            Assert.Equal(1, diff.Unchanged);
            Assert.Equal(0, diff.Added);
            Assert.True(diff.WouldOverwrite);
        }

        [Fact]
        public void A_key_that_stopped_resolving_is_reported_not_dropped()
        {
            var document = Model(out var source);
            var live = document.AddItem(source, "Wall-01").Key;
            var gone = ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\Jobs\Site\ARC.nwc"),
                                               @"C:\Jobs\Site\ARC.nwc", "Wall-99");

            var diff = PropertyDiff.Of(Tab, "Zone",
                PropertyDiff.Resolve(document, new[] { live, gone }), "North");

            Assert.Equal(gone, Assert.Single(diff.Missing));
            Assert.Equal(2, diff.Scope);
            Assert.Contains("1 no longer found", diff.Explain());
        }

        [Fact]
        public void The_rows_shown_are_the_ones_that_change()
        {
            // A diff whose first twenty rows all say "already correct" hides the one row that is not.
            var document = Model(out var source);

            for (var n = 0; n < 10; n++)
                document.AddItem(source, "Wall-0" + n).WithProperty(Tab, "Zone", "North");

            document.AddItem(source, "Odd-one-out").WithProperty(Tab, "Zone", "South");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "North");

            var only = Assert.Single(diff.Changes);
            Assert.Equal("Odd-one-out", only.Label);
            Assert.Equal("South", only.Before);
            Assert.Equal("North", only.After);
            Assert.Equal(WriteChange.Changed, only.Kind);
        }

        [Fact]
        public void What_is_there_now_comes_back_grouped()
        {
            var document = Model(out var source);

            document.AddItem(source, "a").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "b").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "c").WithProperty(Tab, "Zone", "South");
            document.AddItem(source, "d");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "East");

            Assert.Equal(3, diff.Before.Count);
            Assert.Equal("North", diff.Before[0].Value);
            Assert.Equal(2, diff.Before[0].Count);
            Assert.Contains(diff.Before, v => v.Value == null && v.Count == 1);
        }

        [Fact]
        public void A_value_per_element_is_diffed_the_same_way()
        {
            var document = Model(out var source);

            document.AddItem(source, "Wall-01").WithProperty(Tab, "Name", "Wall-01");
            document.AddItem(source, "Wall-02").WithProperty(Tab, "Name", "stale");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Name", PropertyDiff.Resolve(document, keys),
                                       item => item.DisplayName);

            Assert.Equal(1, diff.Unchanged);
            Assert.Equal(1, diff.Changed);
        }

        [Fact]
        public void A_census_says_what_a_column_holds_without_proposing_to_change_it()
        {
            var document = Model(out var source);

            document.AddItem(source, "a").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "b").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "c").WithProperty(Tab, "Zone", "South");
            document.AddItem(source, "d");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var census = PropertyDiff.Census(Tab, "Zone", PropertyDiff.Resolve(document, keys));

            Assert.Equal(3, census.Present);
            Assert.Equal(4, census.Scope);
            Assert.True(census.IsEmpty);
            Assert.Equal(4, census.Unchanged);
        }

        [Fact]
        public void How_many_already_carry_it_is_not_any_of_the_other_four_counts()
        {
            // Both an element already holding the value and an element holding nothing that gets
            // nothing are Unchanged, so Unchanged cannot answer "how many carry this column".
            var document = Model(out var source);

            document.AddItem(source, "a").WithProperty(Tab, "Zone", "North");
            document.AddItem(source, "b");

            var keys = document.Traverse(TraversalScope.WholeDocument).Select(i => i.Key).ToList();
            var diff = PropertyDiff.Of(Tab, "Zone", PropertyDiff.Resolve(document, keys), "North");

            Assert.Equal(1, diff.Present);
            Assert.Equal(1, diff.Unchanged);
            Assert.Equal(1, diff.Added);
        }

        [Theory]
        [InlineData(null, "x", WriteChange.Added)]
        [InlineData("", "x", WriteChange.Added)]
        [InlineData("a", "x", WriteChange.Changed)]
        [InlineData("x", "x", WriteChange.Unchanged)]
        [InlineData("a", null, WriteChange.Cleared)]
        [InlineData("a", "", WriteChange.Cleared)]
        [InlineData(null, null, WriteChange.Unchanged)]
        public void Classification_is_by_what_is_there_not_by_whether_a_string_is_null(
            string? before, string? after, WriteChange expected)
        {
            // An empty property and an absent one are the same thing to the person reading the
            // model, and a diff that called one Added and the other Changed would be lying twice.
            Assert.Equal(expected, PropertyDiff.Classify(before, after));
        }

        [Theory]
        [InlineData(WriteChange.Added, ExistingValues.Keep, true)]
        [InlineData(WriteChange.Changed, ExistingValues.Keep, false)]
        [InlineData(WriteChange.Cleared, ExistingValues.Keep, false)]
        [InlineData(WriteChange.Unchanged, ExistingValues.Keep, false)]
        [InlineData(WriteChange.Changed, ExistingValues.Overwrite, true)]
        [InlineData(WriteChange.Unchanged, ExistingValues.Overwrite, false)]
        public void Only_the_agreed_cases_are_written(WriteChange kind, ExistingValues existing, bool expected)
        {
            Assert.Equal(expected, PropertyDiff.ShouldWrite(kind, existing));
        }
    }
}
