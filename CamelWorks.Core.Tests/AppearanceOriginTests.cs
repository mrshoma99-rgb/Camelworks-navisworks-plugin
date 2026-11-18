using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Reporting an override at the level it was applied, not at every element that ended up with
    /// it.
    ///
    /// Navisworks applies an override to a node and every element under it takes it. One act on
    /// "Level 03" is four thousand coloured elements, and the Appearance Manager used to report
    /// that act four thousand times — so adopting it made a layer holding four thousand keys, and
    /// a model with a dozen such acts made a layer list nobody could read.
    ///
    /// The host does not treat the two kinds of state alike, and these tests pin both halves:
    /// hiding is recorded on the item it was applied to and the level is therefore read, while
    /// colour is stored on geometry and pushed down, so its level can only be inferred from the
    /// subtree.
    /// </summary>
    public class AppearanceOriginTests
    {
        private static readonly Colour Red = new Colour(200, 30, 30);
        private static readonly Colour Blue = new Colour(30, 30, 200);

        /// <summary>A tree, written as the walk would feed it.</summary>
        private sealed class Tree
        {
            private readonly OriginScan _scan = new OriginScan();
            private int _next;

            /// <summary>A grouping node with no geometry of its own. Closed with <see cref="Up"/>.</summary>
            internal int Group(bool hidden = false)
            {
                var id = _next++;
                _scan.Enter(id, hasGeometry: false, hidesHere: hidden);
                return id;
            }

            /// <summary>A geometry element, opened and closed.</summary>
            internal int Leaf(Colour? colour = null, double? transparency = null, bool hidden = false)
            {
                var id = _next++;
                _scan.Enter(id, hasGeometry: true, hidesHere: hidden,
                            colour: colour, transparency: transparency);
                _scan.Exit();
                return id;
            }

            internal void Up() => _scan.Exit();

            internal OriginScan Scan => _scan;

            internal IReadOnlyList<AppearanceOrigin> Origins() => _scan.Origins();
        }

        [Fact]
        public void A_parent_level_colour_is_one_origin_at_the_parent_not_one_per_element()
        {
            var t = new Tree();

            var level = t.Group();
            for (var i = 0; i < 400; i++) t.Leaf(Red);
            t.Up();

            var origins = t.Origins();

            // One row, not four hundred. This is the whole point: four hundred rows is four
            // hundred layers to adopt, and a layer list that cannot be read.
            var only = Assert.Single(origins);
            Assert.Equal(level, only.Node);
            Assert.Equal(400, only.Covers);
            Assert.Equal(Red, only.Colour);
        }

        [Fact]
        public void The_roll_up_climbs_as_far_as_the_agreement_goes()
        {
            var t = new Tree();

            // A model whose every element is red, three levels deep. The origin is the root.
            var root = t.Group();
            for (var storey = 0; storey < 3; storey++)
            {
                t.Group();
                for (var i = 0; i < 10; i++) t.Leaf(Red);
                t.Up();
            }
            t.Up();

            var only = Assert.Single(t.Origins());

            Assert.Equal(root, only.Node);
            Assert.Equal(0, only.Depth);
            Assert.Equal(30, only.Covers);
        }

        [Fact]
        public void It_stops_climbing_where_the_subtree_stops_agreeing()
        {
            var t = new Tree();

            var root = t.Group();

            var red = t.Group();
            for (var i = 0; i < 5; i++) t.Leaf(Red);
            t.Up();

            var blue = t.Group();
            for (var i = 0; i < 7; i++) t.Leaf(Blue);
            t.Up();

            t.Up();

            var origins = t.Origins();

            // Two origins at depth 1, not one wrong one at the root. The root is mixed, so it
            // cannot be where either was applied.
            Assert.Equal(2, origins.Count);
            Assert.DoesNotContain(root, origins.Select(o => o.Node));
            Assert.All(origins, o => Assert.Equal(1, o.Depth));

            // Biggest first within a depth, so two identical runs read the same.
            Assert.Equal(new[] { blue, red }, origins.Select(o => o.Node).ToArray());
            Assert.Equal(new[] { 7, 5 }, origins.Select(o => o.Covers).ToArray());
        }

        [Fact]
        public void One_untouched_element_stops_the_roll_up_rather_than_being_rounded_away()
        {
            var t = new Tree();

            var level = t.Group();
            for (var i = 0; i < 9; i++) t.Leaf(Red);
            t.Leaf();                                  // somebody's override was never applied here
            t.Up();

            var origins = t.Origins();

            // Nine rows at the leaves, not one row claiming the level is red. The level is not
            // red, and saying it is would make "put it back" clear an element nobody touched.
            Assert.Equal(9, origins.Count);
            Assert.DoesNotContain(level, origins.Select(o => o.Node));
            Assert.All(origins, o => Assert.Equal(1, o.Covers));
        }

        [Fact]
        public void Deeper_origins_come_first_even_when_a_shallower_one_covers_more()
        {
            var t = new Tree();

            t.Group();                                  // the model root, depth 0

            // Red was applied over the model, then blue over one branch inside it. Because the
            // host pushes colour down to the geometry, what is left on the leaves is all that is
            // left of either act: the storeys that are still wholly red roll up to themselves,
            // and the branch that is blue rolls up to itself. The storey holding both is mixed,
            // so it is not reported -- correctly, since it is not where either act landed.
            var whole = t.Group();                      // depth 1, all red
            for (var i = 0; i < 10; i++) t.Leaf(Red);
            t.Up();

            t.Group();                                  // depth 1, mixed
            var stillRed = t.Group();                   // depth 2, red
            for (var i = 0; i < 5; i++) t.Leaf(Red);
            t.Up();
            var nowBlue = t.Group();                    // depth 2, blue
            for (var i = 0; i < 3; i++) t.Leaf(Blue);
            t.Up();
            t.Up();

            t.Up();

            var origins = t.Origins();

            Assert.Equal(3, origins.Count);

            // Deepest first, and the shallower one covering twice as much does not promote it.
            // This is the ordering the layer stack needs: a layer on the deeper node has to decide
            // its elements before a layer on the shallower one gets to, or the deeper layer covers
            // elements and decides nothing about any of them -- which is what the stack reports as
            // a dead layer.
            Assert.Equal(new[] { stillRed, nowBlue, whole }, origins.Select(o => o.Node).ToArray());
            Assert.Equal(new[] { 2, 2, 1 }, origins.Select(o => o.Depth).ToArray());
            Assert.Equal(new[] { 5, 3, 10 }, origins.Select(o => o.Covers).ToArray());

            Assert.Equal(Blue, origins.Single(o => o.Node == nowBlue).Colour);
        }

        [Fact]
        public void A_hidden_parent_is_reported_once_at_the_parent_and_counts_what_it_hides()
        {
            var t = new Tree();

            var level = t.Group(hidden: true);
            for (var i = 0; i < 300; i++) t.Leaf();
            t.Up();

            var only = Assert.Single(t.Origins());

            // The fault this replaces: the scan read IsHidden off the geometry leaves, which all
            // report themselves visible, so a hidden storey showed as nothing hidden at all.
            Assert.Equal(level, only.Node);
            Assert.True(only.IsHidden);
            Assert.Equal(300, only.Covers);
        }

        [Fact]
        public void A_hidings_level_is_read_from_the_host_and_a_colours_level_is_inferred()
        {
            var t = new Tree();

            var hiddenLevel = t.Group(hidden: true);
            for (var i = 0; i < 3; i++) t.Leaf();
            t.Up();

            var redLevel = t.Group();
            for (var i = 0; i < 3; i++) t.Leaf(Red);
            t.Up();

            var origins = t.Origins();

            var hiding = origins.Single(o => o.Node == hiddenLevel);
            var colour = origins.Single(o => o.Node == redLevel);

            // Hiding is stored on the item it was applied to, so the level is a fact.
            Assert.True(hiding.IsRead);

            // Colour is stored on geometry, so the level is a reading of the subtree. Almost
            // certainly a parent-level override; possibly three individual ones that agree. A
            // screen can say which it is only because this says which it is.
            Assert.False(colour.IsRead);
        }

        [Fact]
        public void An_element_inside_a_hidden_parent_is_not_an_origin_of_its_own()
        {
            var t = new Tree();

            var level = t.Group(hidden: true);

            // The host would never report these as hidden, but a nested hide is legal and this
            // must not turn one act into two rows.
            var inner = t.Group(hidden: true);
            t.Leaf(hidden: true);
            t.Up();

            t.Up();

            var only = Assert.Single(t.Origins());

            Assert.Equal(level, only.Node);
            Assert.Equal(0, only.Depth);
        }

        [Fact]
        public void Elements_hidden_one_at_a_time_are_reported_one_at_a_time()
        {
            var t = new Tree();

            t.Group();
            var first = t.Leaf(hidden: true);
            var second = t.Leaf(hidden: true);
            t.Leaf();
            t.Up();

            var origins = t.Origins();

            // Two acts, two rows. Rolling these up to the parent would claim the parent was
            // hidden, which is false and would hide the third element when the layer was applied.
            Assert.Equal(2, origins.Count);
            Assert.Equal(new[] { first, second }, origins.Select(o => o.Node).OrderBy(n => n).ToArray());
            Assert.All(origins, o => Assert.True(o.IsHidden));
        }

        [Fact]
        public void A_branch_with_no_geometry_does_not_break_a_roll_up_above_it()
        {
            var t = new Tree();

            var root = t.Group();

            t.Group();                 // an empty folder, as a federation is full of
            t.Up();

            for (var i = 0; i < 6; i++) t.Leaf(Red);

            t.Up();

            var only = Assert.Single(t.Origins());

            // The folder says nothing either way. Treating it as disagreement would stop every
            // roll-up in a real model, because real models are full of them.
            Assert.Equal(root, only.Node);
            Assert.Equal(6, only.Covers);
        }

        [Fact]
        public void Transparency_rolls_up_on_its_own_and_alongside_a_colour()
        {
            var t = new Tree();

            var faded = t.Group();
            for (var i = 0; i < 4; i++) t.Leaf(transparency: 0.5);
            t.Up();

            var both = t.Group();
            for (var i = 0; i < 4; i++) t.Leaf(Red, 0.25);
            t.Up();

            var origins = t.Origins();

            var one = origins.Single(o => o.Node == faded);
            Assert.Null(one.Colour);
            Assert.Equal(0.5, one.Transparency);
            Assert.Equal("50% transparent", one.Describe());

            var two = origins.Single(o => o.Node == both);
            Assert.Equal(Red, two.Colour);
            Assert.Equal(0.25, two.Transparency);
            Assert.Equal("#c81e1e, 25% transparent", two.Describe());
        }

        [Fact]
        public void Two_branches_that_agree_roll_up_to_their_common_parent()
        {
            var t = new Tree();

            var root = t.Group();

            t.Group();
            for (var i = 0; i < 3; i++) t.Leaf(Red);
            t.Up();

            t.Group();
            for (var i = 0; i < 3; i++) t.Leaf(Red);
            t.Up();

            t.Up();

            var only = Assert.Single(t.Origins());

            // Both branches red means the parent is red, and one act is the likeliest explanation.
            Assert.Equal(root, only.Node);
            Assert.Equal(6, only.Covers);
        }

        [Fact]
        public void An_untouched_model_has_no_origins()
        {
            var t = new Tree();

            t.Group();
            for (var i = 0; i < 50; i++) t.Leaf();
            t.Up();

            Assert.Empty(t.Origins());
        }

        [Fact]
        public void The_walk_reports_how_much_it_covered()
        {
            var t = new Tree();

            t.Group();
            for (var i = 0; i < 40; i++) t.Leaf();
            t.Up();

            // Nodes is every node entered; Elements is only the geometry. A progress line wants
            // the second, and a "none of 40,000 elements" wants it too.
            Assert.Equal(41, t.Scan.Nodes);
            Assert.Equal(40, t.Scan.Elements);
        }

        [Fact]
        public void Asking_for_the_origins_mid_walk_refuses_rather_than_answering_early()
        {
            var t = new Tree();

            t.Group();
            t.Leaf(Red);

            // A partial roll-up is not a wrong number, it is a different model: the node being
            // walked has not seen the children that would make it mixed.
            var thrown = Assert.Throws<InvalidOperationException>(() => t.Origins());
            Assert.Contains("still inside", thrown.Message);
        }

        [Fact]
        public void Coming_up_out_of_nothing_refuses()
        {
            var scan = new OriginScan();

            Assert.Throws<InvalidOperationException>(() => scan.Exit());
        }

        // -----------------------------------------------------------------------------------
        // Through the seam, against a document that hides a branch the way the host does
        // -----------------------------------------------------------------------------------

        /// <summary>
        /// A model with one storey of interest, and optionally a second one to stop a roll-up.
        ///
        /// The second storey matters more than it looks. A roll-up climbs as far as the agreement
        /// goes, so in a model whose only storey is red the answer is the model, not the storey --
        /// correctly, since that is indistinguishable from somebody colouring the whole model. A
        /// test that wants the storey as the answer has to give the model something else in it.
        /// </summary>
        /// <param name="parts">Elements on Level 03, which the tests act on.</param>
        /// <param name="elsewhere">Untouched elements on Level 04, to stop a roll-up at Level 03.</param>
        private static (FakeDocument doc, FakeViewSession view, List<FakeItem> items) Storey(
            int parts, int elsewhere = 0)
        {
            var doc = new FakeDocument();
            var arch = doc.AddModel("ARCH", @"C:\proj\ARCH.nwc");

            var items = new List<FakeItem>();

            for (var i = 0; i < parts; i++)
                items.Add(doc.AddItem(arch, "Part " + i.ToString(), "Generic", null,
                                      default, true, "ARCH", "Level 03"));

            for (var i = 0; i < elsewhere; i++)
                doc.AddItem(arch, "Other " + i.ToString(), "Generic", null,
                            default, true, "ARCH", "Level 04");

            return (doc, new FakeViewSession(doc), items);
        }

        [Fact]
        public void A_colour_over_the_whole_model_is_reported_at_the_model_and_not_at_a_storey()
        {
            var (_, view, items) = Storey(60);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var only = Assert.Single(view.ScanOrigins(TraversalScope.WholeDocument, null).Origins);

            // Every element in the model is red and the model has one storey, so the storey is not
            // the answer: the model is. Climbing only as far as the storey would under-report the
            // act, and adopting it would leave anything added outside that storey uncovered.
            Assert.Equal("ARCH", only.Path);
            Assert.Equal(0, only.Origin.Depth);
            Assert.Equal(60, only.Origin.Covers);
        }

        [Fact]
        public void A_hidden_storey_is_found_even_though_every_element_in_it_reports_itself_visible()
        {
            var (doc, view, items) = Storey(250);

            doc.HideBranch("ARCH", "Level 03");

            // The host records the hiding on the storey and marks nothing below it, so this is
            // what the old leaf-by-leaf scan saw and why it reported nothing hidden.
            Assert.All(items, item => Assert.False(item.IsHidden));

            var report = view.ScanOrigins(TraversalScope.WholeDocument, null);

            var only = Assert.Single(report.Origins);

            Assert.True(only.Origin.IsHidden);
            Assert.True(only.Origin.IsRead);
            Assert.Equal(250, only.Origin.Covers);
            Assert.Equal("ARCH / Level 03", only.Path);
        }

        [Fact]
        public void A_storey_wide_colour_is_one_place_and_not_one_per_element()
        {
            var (_, view, items) = Storey(250, elsewhere: 20);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var report = view.ScanOrigins(TraversalScope.WholeDocument, null);

            var only = Assert.Single(report.Origins);

            Assert.Equal("ARCH / Level 03", only.Path);
            Assert.Equal(250, only.Origin.Covers);
            Assert.Equal(Red, only.Origin.Colour);

            // Inferred, not read: the host stores colour on geometry and keeps no note of the
            // level it was applied at.
            Assert.False(only.Origin.IsRead);
        }

        [Fact]
        public void A_layer_adopted_from_a_place_names_the_place_and_not_what_is_under_it()
        {
            var (_, view, items) = Storey(250, elsewhere: 20);

            foreach (var item in items) view.SeedForeignColour(item.Key, Red);

            var origin = view.ScanOrigins(TraversalScope.WholeDocument, null).Origins.Single();

            var layer = origin.Origin.AsLayer(new[] { origin.Item.Key }, "adopted-1", origin.Path);

            // One key, not two hundred and fifty. The host pushes an override on a node down
            // itself, so this applies in one call and keeps applying to anything added under that
            // node later -- which a list of the keys that happened to be there does not.
            var target = Assert.Single(layer.Target.Keys);
            Assert.Equal(origin.Item.Key, target);

            Assert.Equal(Red, layer.Colour);
            Assert.Null(layer.Visible);          // a colour layer must not start deciding visibility
            Assert.Contains("ARCH / Level 03", layer.Name);
        }

        [Fact]
        public void A_layer_adopted_from_a_hiding_decides_visibility_and_nothing_else()
        {
            var (doc, view, _) = Storey(10);

            doc.HideBranch("ARCH", "Level 03");

            var origin = view.ScanOrigins(TraversalScope.WholeDocument, null).Origins.Single();

            var layer = origin.Origin.AsLayer(new[] { origin.Item.Key }, "adopted-1", origin.Path);

            Assert.False(layer.Visible);
            Assert.Null(layer.Colour);
            Assert.Null(layer.Transparency);
        }

        [Fact]
        public void One_state_in_many_places_is_one_layer_naming_all_of_them()
        {
            var (_, view, items) = Storey(40);

            // Nobody applied this to the storey: forty separate acts that happen to agree, minus
            // one element, so there is no node to roll up to.
            for (var i = 0; i < 39; i++) view.SeedForeignColour(items[i].Key, Red);

            var origins = view.ScanOrigins(TraversalScope.WholeDocument, null).Origins;

            Assert.Equal(39, origins.Count);

            var layer = origins[0].Origin.AsLayer(
                origins.Select(o => o.Item.Key).ToList(), "adopted-1", "39 places");

            // One layer, thirty-nine nodes. Thirty-nine layers is the complaint this answers.
            Assert.Equal(39, layer.Target.Keys.Count);
            Assert.Equal(Red, layer.Colour);
        }

        [Fact]
        public void A_layer_needs_somewhere_to_point()
        {
            var (_, view, items) = Storey(4);

            view.SeedForeignColour(items[0].Key, Red);

            var origin = view.ScanOrigins(TraversalScope.WholeDocument, null).Origins.Single();

            Assert.Throws<ArgumentException>(
                () => origin.Origin.AsLayer(Array.Empty<ElementKey>(), "adopted-1", "nowhere"));
        }
    }
}
