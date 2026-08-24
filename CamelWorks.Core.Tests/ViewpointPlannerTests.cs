using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Naming a run of generated viewpoints — the half that goes wrong quietly.
    /// </summary>
    public class ViewpointPlannerTests
    {
        private static KeyValuePair<ElementKey, string?> El(string id, string? name) =>
            new KeyValuePair<ElementKey, string?>(
                ElementKey.FromTreePath("structure", "S.nwc", id), name);

        private static List<KeyValuePair<ElementKey, string?>> Some(params string[] names) =>
            names.Select((n, i) => El("e" + i, n)).ToList();

        [Fact]
        public void Numbers_are_padded_so_a_list_sorts_the_way_it_reads()
        {
            // Without padding a host's alphabetical list puts 10 before 2, which is the reason
            // hand-made viewpoint folders are always slightly out of order.
            var run = ViewpointPlanner.Plan(Some("Duct", "Pipe", "Beam"), new ViewpointRecipe
            {
                Pattern = "{n}",
                Padding = 3,
            });

            Assert.Equal(new[] { "001", "002", "003" }, run.Views.Select(v => v.Name));
        }

        [Fact]
        public void The_run_starts_where_it_is_told_to()
        {
            var run = ViewpointPlanner.Plan(Some("Duct", "Pipe"), new ViewpointRecipe
            {
                Pattern = "{n}",
                Start = 40,
                Padding = 2,
            });

            Assert.Equal(new[] { "40", "41" }, run.Views.Select(v => v.Name));
        }

        [Fact]
        public void The_pattern_can_put_the_number_anywhere()
        {
            var run = ViewpointPlanner.Plan(Some("Duct"), new ViewpointRecipe
            {
                Pattern = "Level 2 - {name} - VP{n}",
                Padding = 2,
            });

            Assert.Equal("Level 2 - Duct - VP01", run.Views.Single().Name);
        }

        [Fact]
        public void An_element_with_no_name_still_gets_a_viewpoint()
        {
            var run = ViewpointPlanner.Plan(Some("Duct", "", "Pipe"), new ViewpointRecipe
            {
                Pattern = "{name}",
            });

            Assert.Equal(new[] { "Duct", "(unnamed)", "Pipe" }, run.Views.Select(v => v.Name));
        }

        [Fact]
        public void Two_elements_with_the_same_name_do_not_get_the_same_viewpoint_name()
        {
            var run = ViewpointPlanner.Plan(Some("Duct", "Duct", "Duct"), new ViewpointRecipe
            {
                Pattern = "{name}",
            });

            Assert.Equal(new[] { "Duct", "Duct (2)", "Duct (3)" }, run.Views.Select(v => v.Name));
        }

        [Fact]
        public void A_name_the_document_already_has_is_stepped_around()
        {
            var run = ViewpointPlanner.Plan(Some("Duct"), new ViewpointRecipe { Pattern = "{name}" },
                                            existing: new[] { "Duct" });

            Assert.Equal("Duct (2)", run.Views.Single().Name);
        }

        [Fact]
        public void An_element_with_no_identity_is_reported_rather_than_framed()
        {
            var elements = new List<KeyValuePair<ElementKey, string?>>
            {
                El("real", "Duct"),
                new KeyValuePair<ElementKey, string?>(default, "Ghost"),
            };

            var run = ViewpointPlanner.Plan(elements, new ViewpointRecipe());

            Assert.Single(run.Views);
            Assert.Single(run.Skipped);
        }

        [Fact]
        public void The_cap_stops_the_run_and_says_it_stopped()
        {
            var run = ViewpointPlanner.Plan(Some("a", "b", "c", "d"), new ViewpointRecipe(), cap: 2);

            Assert.Equal(2, run.Views.Count);
            Assert.Contains("more than 2 viewpoints", run.Skipped[0]);
        }

        [Fact]
        public void An_empty_pattern_still_produces_a_usable_name()
        {
            var run = ViewpointPlanner.Plan(Some("Duct"), new ViewpointRecipe { Pattern = "  " });

            Assert.False(string.IsNullOrWhiteSpace(run.Views.Single().Name));
        }

        [Fact]
        public void Nothing_selected_says_so_rather_than_failing()
        {
            var run = ViewpointPlanner.Plan(new List<KeyValuePair<ElementKey, string?>>(), new ViewpointRecipe());

            Assert.True(run.IsEmpty);
            Assert.Contains("nothing to make a viewpoint of", run.Explain());
        }
    }
}
