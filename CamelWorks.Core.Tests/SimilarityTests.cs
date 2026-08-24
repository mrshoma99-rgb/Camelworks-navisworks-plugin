using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Sets;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// "Everything like this one" — the question people ask a model, which today sends them into
    /// the search dialog to rebuild by hand what they already have selected.
    /// </summary>
    public class SimilarityTests
    {
        private static PropertyValue P(string category, string name, string? value) =>
            new PropertyValue(category, name, value);

        private static PropertyValue[] Duct() => new[]
        {
            P("Item", "Name", "Duct 4213"),
            P("Item", "Type", "Rectangular Duct"),
            P("Item", "GUID", "8f2c-441a"),
            P("Element", "Category", "Ducts"),
            P("Element", "System Type", "Supply Air"),
            P("Element", "Level", "L02"),
            P("Element", "Length", "2400"),
        };

        [Fact]
        public void What_a_thing_is_starts_ticked_and_which_one_it_is_does_not()
        {
            var facets = Similarity.Facets(Duct());

            var ticked = facets.Where(f => f.Suggested).Select(f => f.Name).ToList();

            Assert.Contains("Category", ticked);
            Assert.Contains("Type", ticked);
            Assert.Contains("System Type", ticked);

            // Name and GUID identify one element. Ticking them matches only the one you picked.
            Assert.DoesNotContain("Name", ticked);
            Assert.DoesNotContain("GUID", ticked);

            // Length is neither: it describes this duct, not a kind of duct.
            Assert.DoesNotContain("Length", ticked);
        }

        [Fact]
        public void The_ticked_ones_are_listed_first()
        {
            var facets = Similarity.Facets(Duct());
            var firstUnticked = facets.ToList().FindIndex(f => !f.Suggested);

            Assert.True(facets.Take(firstUnticked).All(f => f.Suggested));
            Assert.True(facets.Skip(firstUnticked).All(f => !f.Suggested));
        }

        [Fact]
        public void A_property_repeated_across_tabs_is_offered_once_per_tab()
        {
            var facets = Similarity.Facets(new[]
            {
                P("Item", "Type", "A"),
                P("Element", "Type", "A"),
                P("Item", "Type", "A"),
            });

            // Two tabs, so two facets — they are different properties. The repeat within a tab is
            // not, and the host does emit those.
            Assert.Equal(2, facets.Count);
        }

        [Fact]
        public void Ticking_nothing_finds_nothing_rather_than_everything()
        {
            var expression = Similarity.Like(System.Array.Empty<Facet>());

            Assert.Same(SetExpression.Nothing, expression);
            Assert.Contains("nothing would be found", Similarity.Explain(System.Array.Empty<Facet>()));
        }

        [Fact]
        public void The_chosen_facets_are_ANDed_into_one_rule()
        {
            var facets = Similarity.Facets(Duct()).Where(f => f.Suggested).ToList();
            var expression = Similarity.Like(facets);

            // Every ticked facet has to match, which is what "like this one" means.
            Assert.NotSame(SetExpression.Nothing, expression);
            Assert.Contains("Category", Similarity.Explain(facets));
        }

        [Fact]
        public void A_blank_value_matches_the_property_existing_rather_than_being_empty()
        {
            var facets = Similarity.Facets(new[] { P("Element", "Classification", null) });

            var facet = Assert.Single(facets);
            Assert.True(facet.IsBlank);

            // The alternative — an Equals against nothing — is a rule that matches every element in
            // the model, which is the opposite of what was asked for.
            Assert.NotSame(SetExpression.Nothing, Similarity.Like(new[] { facet }));
        }

        [Fact]
        public void A_suffixed_name_still_counts_as_describing()
        {
            var facets = Similarity.Facets(new[]
            {
                P("Revit", "Revit Type", "Duct"),
                P("Revit", "Revit Family", "Rect"),
            });

            Assert.True(facets.All(f => f.Suggested));
        }

        [Fact]
        public void The_explanation_names_three_and_counts_the_rest()
        {
            var facets = Similarity.Facets(Duct()).Take(5).ToList();
            var line = Similarity.Explain(facets);

            Assert.Contains("and 2 more", line);
        }

        [Fact]
        public void An_element_with_no_properties_offers_nothing_without_failing()
        {
            Assert.Empty(Similarity.Facets(null));
            Assert.Empty(Similarity.Facets(System.Array.Empty<PropertyValue>()));
        }
    }
}
