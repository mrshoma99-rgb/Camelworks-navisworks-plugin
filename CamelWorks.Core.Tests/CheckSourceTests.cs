using System.Linq;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What a check's source holds, what it writes into a project file, and what it reads out of a
    /// document.
    /// </summary>
    public class CheckSourceTests
    {
        [Fact]
        public void One_set_is_written_as_its_bare_name()
        {
            Assert.Equal("Level 03 Floors", CheckSource.Sets("Level 03 Floors").Written());
        }

        [Fact]
        public void A_bare_name_reads_back_as_that_one_set()
        {
            var source = CheckSource.Read("Level 03 Floors");

            Assert.Equal(CheckOrigin.Sets, source.Origin);
            Assert.Equal(new[] { "Level 03 Floors" }, source.Names);
        }

        [Theory]
        [InlineData(CheckSource.Everywhere, CheckOrigin.Everything)]
        [InlineData(CheckSource.Remainder, CheckOrigin.TheRest)]
        [InlineData(CheckSource.TheSelection, CheckOrigin.Selection)]
        [InlineData(CheckSource.Nowhere, CheckOrigin.Nothing)]
        public void The_reserved_words_an_older_build_wrote_still_read(string written, CheckOrigin origin)
        {
            Assert.Equal(origin, CheckSource.Read(written).Origin);
        }

        [Fact]
        public void An_absent_key_reads_as_the_fallback()
        {
            Assert.Equal(CheckOrigin.Everything,
                         CheckSource.Read(null, CheckSource.Everything()).Origin);
        }

        [Fact]
        public void Several_sets_survive_a_write_and_a_read()
        {
            var source = CheckSource.Sets("Level 03 Floors", "Level 04 Floors", "Ramps");

            var again = CheckSource.Read(source.Written());

            Assert.Equal(CheckOrigin.Sets, again.Origin);
            Assert.Equal(source.Names, again.Names);
        }

        [Fact]
        public void A_set_name_with_a_bar_in_it_survives_the_round_trip()
        {
            var source = CheckSource.Sets("Level 03 | Floors", "100% ducts");

            Assert.Equal(source.Names, CheckSource.Read(source.Written()).Names);
        }

        [Fact]
        public void A_set_named_like_a_reserved_word_is_not_written_bare()
        {
            var source = CheckSource.Sets(CheckSource.Everywhere);

            var again = CheckSource.Read(source.Written());

            Assert.Equal(CheckOrigin.Sets, again.Origin);
            Assert.Equal(new[] { CheckSource.Everywhere }, again.Names);
        }

        [Fact]
        public void Models_survive_a_write_and_a_read()
        {
            var source = CheckSource.Models("Structure.nwc", "Services.nwc");

            var again = CheckSource.Read(source.Written());

            Assert.Equal(CheckOrigin.Models, again.Origin);
            Assert.Equal(source.Names, again.Names);
        }

        [Fact]
        public void A_selection_survives_as_its_elements()
        {
            var key = ElementKey.FromTreePath(ElementKey.ScopeOf("C:/a.nwc"), "C:/a.nwc",
                                              "Model", "Wall");

            var again = CheckSource.Read(CheckSource.Selection(new[] { key }).Written());

            Assert.Equal(CheckOrigin.Selection, again.Origin);
            Assert.Equal(new[] { key }, again.Keys);
        }

        [Fact]
        public void An_empty_source_reads_nothing()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            document.AddItem(model, "Slab");

            Assert.True(CheckSource.None().IsEmpty);
            Assert.Empty(CheckSource.None().Items(document));
            Assert.Empty(CheckSource.Sets().Items(document));
        }

        [Fact]
        public void Two_sets_that_overlap_read_each_element_once()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            var a = document.AddItem(model, "Slab A");
            var b = document.AddItem(model, "Slab B");

            document.DefineSet("West", a.Key, b.Key);
            document.DefineSet("Level 03", b.Key);

            var read = CheckSource.Sets("West", "Level 03").Items(document).ToList();

            Assert.Equal(new[] { a.Key, b.Key }, read.Select(one => one.Key));
        }

        [Fact]
        public void Whole_models_reads_only_the_models_it_names()
        {
            var document = new FakeDocument();
            var structure = document.AddModel("Structure.nwc", "C:/models/structure.nwc");
            var services = document.AddModel("Services.nwc", "C:/models/services.nwc");

            var slab = document.AddItem(structure, "Slab");

            document.AddItem(services, "Duct");

            Assert.Equal(new[] { slab.Key },
                         CheckSource.Models("Structure.nwc").Items(document)
                                    .Select(one => one.Key));
        }

        [Fact]
        public void A_model_named_by_its_scope_still_resolves()
        {
            var document = new FakeDocument();
            var structure = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            var slab = document.AddItem(structure, "Slab");

            Assert.Equal(new[] { slab.Key },
                         CheckSource.Models(structure.Scope).Items(document)
                                    .Select(one => one.Key));
        }

        [Fact]
        public void A_selection_with_no_elements_listed_is_read_from_the_host()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            var a = document.AddItem(model, "Slab A");

            document.AddItem(model, "Slab B");
            document.Select(a.Key);

            Assert.Equal(new[] { a.Key },
                         CheckSource.Read(CheckSource.TheSelection).Items(document)
                                    .Select(one => one.Key));
        }

        [Fact]
        public void A_selection_that_listed_its_elements_is_not_re_read()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            var a = document.AddItem(model, "Slab A");
            var b = document.AddItem(model, "Slab B");

            document.Select(b.Key);

            Assert.Equal(new[] { a.Key },
                         CheckSource.Selection(new[] { a.Key }).Items(document)
                                    .Select(one => one.Key));
        }

        [Fact]
        public void Everything_and_the_rest_both_walk_the_whole_document()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure.nwc", "C:/models/structure.nwc");

            document.AddItem(model, "Slab A");
            document.AddItem(model, "Slab B");

            Assert.Equal(2, CheckSource.Everything().Items(document).Count());
            Assert.Equal(2, CheckSource.Rest().Items(document).Count());
            Assert.True(CheckSource.Everything().NeedsEverything);
            Assert.True(CheckSource.Rest().NeedsEverything);
            Assert.False(CheckSource.Sets("West").NeedsEverything);
        }

        [Fact]
        public void The_words_name_what_was_picked()
        {
            Assert.Equal("everything loaded", CheckSource.Everything().Said());
            Assert.Equal("Floors, Ramps", CheckSource.Sets("Floors", "Ramps").Said());
            Assert.Equal("A, B and 2 more",
                         CheckSource.Sets("A", "B", "C", "D").Said());
        }

        [Fact]
        public void A_tree_branch_reads_everything_under_it()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Federation.nwd", "C:/models/federation.nwd");

            var structure = document.AddItem(model, "Structure", hasGeometry: false);
            var services = document.AddItem(model, "Services", hasGeometry: false);

            var slab = document.AddItem(model, "Slab");
            var beam = document.AddItem(model, "Beam");
            var duct = document.AddItem(model, "Duct");

            slab.Above = structure;
            beam.Above = structure;
            duct.Above = services;

            var read = CheckSource.Branches(new[] { structure.Key }).Items(document).ToList();

            Assert.Equal(new[] { slab.Key, beam.Key }, read.Select(one => one.Key));
        }

        [Fact]
        public void Two_branches_that_overlap_read_each_element_once()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Federation.nwd", "C:/models/federation.nwd");

            var branch = document.AddItem(model, "Structure", hasGeometry: false);
            var under = document.AddItem(model, "Level 03", hasGeometry: false);
            var slab = document.AddItem(model, "Slab");

            under.Above = branch;
            slab.Above = under;

            var read = CheckSource.Branches(new[] { branch.Key, under.Key })
                                  .Items(document).ToList();

            Assert.Equal(new[] { slab.Key }, read.Select(one => one.Key));
        }

        [Fact]
        public void A_branch_the_document_no_longer_holds_is_skipped()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Federation.nwd", "C:/models/federation.nwd");

            var branch = document.AddItem(model, "Structure", hasGeometry: false);
            var slab = document.AddItem(model, "Slab");

            slab.Above = branch;

            var gone = ElementKey.FromTreePath(ElementKey.ScopeOf("C:/models/federation.nwd"),
                                               "C:/models/federation.nwd", "Federation", "Gone");

            var read = CheckSource.Branches(new[] { gone, branch.Key }).Items(document).ToList();

            Assert.Equal(new[] { slab.Key }, read.Select(one => one.Key));
        }

        [Fact]
        public void Tree_branches_survive_a_write_and_a_read()
        {
            var key = ElementKey.FromTreePath(ElementKey.ScopeOf("C:/a.nwc"), "C:/a.nwc",
                                              "Model", "Structure");

            var source = CheckSource.Branches(new[] { key });

            var again = CheckSource.Read(source.Written());

            Assert.Equal(CheckOrigin.Branches, again.Origin);
            Assert.Equal(new[] { key }, again.Keys);
            Assert.False(again.IsEmpty);
            Assert.True(CheckSource.Branches(new ElementKey[0]).IsEmpty);
        }

        [Fact]
        public void The_roots_are_the_elements_with_nothing_above_them()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Federation.nwd", "C:/models/federation.nwd");

            var branch = document.AddItem(model, "Structure", hasGeometry: false);
            var slab = document.AddItem(model, "Slab");

            slab.Above = branch;

            Assert.Equal(new[] { branch.Key }, document.Roots.Select(one => one.Key));
            Assert.Equal(new[] { slab.Key }, branch.Children.Select(one => one.Key));
            Assert.Empty(slab.Children);
        }
    }
}
