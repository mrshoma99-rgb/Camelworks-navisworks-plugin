using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class PropertyAddressTests
    {
        [Fact]
        public void A_path_reads_back_the_way_it_was_written()
        {
            var path = PropertyAddress.Parse("Pset_WallCommon / FireRating");

            Assert.NotNull(path);
            Assert.Equal("Pset_WallCommon", path!.Value.Category);
            Assert.Equal("FireRating", path.Value.Name);
            Assert.Equal("Pset_WallCommon / FireRating", path.Value.ToString());
        }

        [Fact]
        public void A_name_on_its_own_matches_any_tab()
        {
            var path = PropertyAddress.Parse("Entity");

            Assert.NotNull(path);
            Assert.Equal(string.Empty, path!.Value.Category);
            Assert.True(path.Value.Holds(new PropertyValue("IFC", "Entity", "IfcWall")));
            Assert.True(path.Value.Holds(new PropertyValue("Element", "entity", "IfcWall")));
            Assert.False(path.Value.Holds(new PropertyValue("Element", "Type", "IfcWall")));
        }

        [Fact]
        public void Nothing_typed_is_no_path_rather_than_an_empty_one()
        {
            Assert.Null(PropertyAddress.Parse(null));
            Assert.Null(PropertyAddress.Parse("   "));
            Assert.Null(PropertyAddress.Parse("Element /  "));
        }

        [Fact]
        public void Case_does_not_make_two_paths_different()
        {
            Assert.Equal(new PropertyAddress("IFC", "Entity"), new PropertyAddress("ifc", "entity"));
            Assert.Equal(new PropertyAddress("IFC", "Entity").GetHashCode(),
                         new PropertyAddress("ifc", "entity").GetHashCode());
            Assert.True(new PropertyAddress("IFC", "Entity") == new PropertyAddress("ifc", "ENTITY"));
            Assert.True(new PropertyAddress("IFC", "Entity") != new PropertyAddress("IFC", "Type"));
        }
    }

    public class IdsProjectionTests
    {
        private static FakeDocument Federation(out FakeSource model)
        {
            var document = new FakeDocument();
            model = document.AddModel("Architecture", @"C:\Jobs\Site\ARC.nwc");
            return document;
        }

        private static IdsDocument WallRule() =>
            IdsFixture.Document(IdsFixture.WallsCarryAFireRating());

        [Fact]
        public void The_ifc_class_is_found_where_the_readers_put_it()
        {
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01").WithProperty("IFC", "Entity", "IfcWall");

            var projection = IdsProjection.For(WallRule());

            Assert.Equal("IFCWALL", projection.From(item)?.IfcType);
            Assert.Equal(1, projection.Classed);
        }

        [Fact]
        public void A_family_name_is_not_mistaken_for_a_class()
        {
            // Item / Type is on the ladder because an IFC import puts the entity there. The same
            // property on a Revit export says "Basic Wall", and believing that would put a made-up
            // entity name into every report.
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01")
                .WithProperty("Item", "Type", "Basic Wall")
                .WithProperty("Pset_WallCommon", "FireRating", "60");

            var element = IdsProjection.For(WallRule()).From(item);

            Assert.NotNull(element);
            Assert.Null(element!.IfcType);
        }

        [Fact]
        public void A_class_can_be_pointed_at_when_the_ladder_misses_it()
        {
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01").WithProperty("Custom", "BuiltAs", "IfcWallStandardCase");

            var told = IdsProjection.For(WallRule(), PropertyAddress.Parse("Custom / BuiltAs"));

            Assert.Equal("IFCWALLSTANDARDCASE", told.From(item)?.IfcType);
        }

        [Fact]
        public void A_pointed_at_property_is_taken_at_its_word()
        {
            // No prefix test on an explicit choice: somebody whose federation calls walls "WALL"
            // can still run an IDS written the same way.
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01").WithProperty("Custom", "Class", "Wall");

            var told = IdsProjection.For(WallRule(), PropertyAddress.Parse("Custom / Class"));

            Assert.Equal("WALL", told.From(item)?.IfcType);
        }

        [Fact]
        public void Only_the_properties_the_ids_asks_about_are_carried()
        {
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Pset_WallCommon", "FireRating", "60")
                .WithProperty("Revit Type", "Assembly Code", "B2010")
                .WithProperty("Item", "Layer", "Level 2");

            var element = IdsProjection.For(WallRule()).From(item);

            Assert.NotNull(element);
            var only = Assert.Single(element!.Properties);
            Assert.Equal("Pset_WallCommon", only.PropertySet);
            Assert.Equal("FireRating", only.Name);
            Assert.Equal("60", only.Value);
        }

        [Fact]
        public void An_element_the_ids_has_nothing_to_say_about_is_dropped()
        {
            var document = Federation(out var model);
            var pipe = document.AddItem(model, "Pipe-01").WithProperty("Item", "Layer", "Level 2");

            var projection = IdsProjection.For(WallRule());

            Assert.Null(projection.From(pipe));
            Assert.Equal(1, projection.Read);
            Assert.Equal(0, projection.Kept);
        }

        [Fact]
        public void A_dropped_element_could_never_have_been_applicable()
        {
            // The claim the drop rests on, checked rather than asserted in a comment: running the
            // checker over everything and over the survivors gives the same specification results.
            var document = Federation(out var model);
            var wall = document.AddItem(model, "Wall-01")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Pset_WallCommon", "FireRating", "45");
            var pipe = document.AddItem(model, "Pipe-01").WithProperty("Item", "Layer", "Level 2");

            var ids = WallRule();
            var lenient = IdsProjection.For(ids);
            var all = new[] { lenient.From(wall)!, new IdsElement(pipe.Key.ToString()) };
            var kept = IdsProjection.For(ids).FromAll(new IModelItem[] { wall, pipe }).ToList();

            var over = IdsChecker.Check(ids, all);
            var under = IdsChecker.Check(ids, kept);

            Assert.Equal(over.Results[0].Applicable, under.Results[0].Applicable);
            Assert.Equal(over.Results[0].Failed, under.Results[0].Failed);
            Assert.Equal(2, over.ElementsChecked);
            Assert.Equal(1, under.ElementsChecked);
        }

        [Fact]
        public void Attributes_are_carried_only_when_the_ids_asks_for_them()
        {
            var document = Federation(out var model);
            var item = document.AddItem(model, "Wall-01")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Element", "Tag", "WA-12");

            Assert.Empty(IdsProjection.For(WallRule()).From(item)!.Attributes);

            var named = new IdsSpecification("Everything is named");
            named.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            named.Requirements.Add(new IdsAttributeFacet(IdsValue.Simple("Name")));

            var element = IdsProjection.For(IdsFixture.Document(named)).From(item);

            Assert.NotNull(element);
            Assert.Equal("Wall-01", element!.Attributes["Name"]);
            Assert.Equal("WA-12", element.Attributes["Tag"]);
        }

        [Fact]
        public void A_classification_is_found_as_a_tab_or_as_a_property()
        {
            var coded = new IdsSpecification("Everything is classified");
            coded.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            coded.Requirements.Add(new IdsClassificationFacet(IdsValue.Simple("Uniclass 2015")));

            var ids = IdsFixture.Document(coded);
            var document = Federation(out var model);

            var byTab = document.AddItem(model, "Wall-01")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Uniclass 2015", "Code", "Ss_25_10_30");

            var byName = document.AddItem(model, "Wall-02")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Element", "Uniclass 2015", "Ss_25_10_30");

            Assert.Equal("Ss_25_10_30", IdsProjection.For(ids).From(byTab)!.Classifications.Single().Value);
            Assert.Equal("Ss_25_10_30", IdsProjection.For(ids).From(byName)!.Classifications.Single().Value);
        }

        [Fact]
        public void A_missing_class_reports_where_one_was_seen_instead()
        {
            var document = Federation(out var model);

            for (var n = 0; n < 3; n++)
            {
                document.AddItem(model, "Wall-0" + n)
                    .WithProperty("Custom", "BuiltAs", "IfcWall")
                    .WithProperty("Pset_WallCommon", "FireRating", "60");
            }

            var projection = IdsProjection.For(WallRule());

            foreach (var item in document.Traverse(TraversalScope.WholeDocument)) projection.From(item);

            Assert.Equal(0, projection.Classed);
            Assert.Equal(new PropertyAddress("Custom", "BuiltAs"), projection.Candidates.First());
        }

        [Fact]
        public void A_specification_a_federation_cannot_answer_is_named()
        {
            var aggregated = new IdsSpecification("Walls belong to a storey");
            aggregated.Applicability.Add(new IdsEntityFacet(IdsValue.Simple("IFCWALL")));
            aggregated.Requirements.Add(new IdsPartOfFacet(IdsValue.Simple("IFCBUILDINGSTOREY")));

            var ids = IdsFixture.Document(IdsFixture.WallsCarryAFireRating(), aggregated);

            Assert.Equal(new[] { "Walls belong to a storey" }, IdsProjection.Unanswerable(ids));
        }

        [Fact]
        public void A_whole_model_projects_in_one_pass()
        {
            var document = Federation(out var model);

            document.AddItem(model, "Wall-01")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Pset_WallCommon", "FireRating", "60");
            document.AddItem(model, "Wall-02")
                .WithProperty("IFC", "Entity", "IfcWall")
                .WithProperty("Pset_WallCommon", "FireRating", "yes");
            document.AddItem(model, "Duct-01").WithProperty("Item", "Layer", "Level 2");

            var projection = IdsProjection.For(WallRule());
            var report = IdsChecker.Check(WallRule(), projection.FromAll(document.Traverse(TraversalScope.WholeDocument)));

            Assert.Equal(3, projection.Read);
            Assert.Equal(2, projection.Kept);
            Assert.Equal(2, projection.Classed);
            Assert.Equal(2, report.Results[0].Applicable);
            Assert.Equal(1, report.Results[0].Passed);
            Assert.Equal(IdsFailure.WrongValue, report.Results[0].Violations.Single().Failure);
        }
    }
}
