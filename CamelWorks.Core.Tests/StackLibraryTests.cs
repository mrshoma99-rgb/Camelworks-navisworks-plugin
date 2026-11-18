using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// A layer stack per scene, which is how a per-view graphics system gets onto a host without
    /// one.
    ///
    /// Revit keeps visibility and graphics on the view. Navisworks does not: overrides are document
    /// state, one current appearance shared by every view, and a saved viewpoint can capture and
    /// restore it but does not own it. So the per-view behaviour is stored here rather than read
    /// from the host, and these tests pin the two things that would lose a user's work if they
    /// were wrong — a project written before stacks were per scene must still open with its stack,
    /// and a copied stack must not collide with the one it was copied from.
    /// </summary>
    public class StackLibraryTests
    {
        private static readonly Colour Red = new Colour(200, 30, 30);

        private static ElementKey E(string name) =>
            ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\p\A.nwc"), @"C:\p\A.nwc", name);

        private static AppearanceLayer Layer(string id, string name) =>
            new AppearanceLayer(id, name, LayerTarget.Elements(new[] { E(name) }))
            {
                Colour = Red,
                Note = "why " + name,
            };

        [Fact]
        public void The_default_views_stack_keeps_the_key_the_single_stack_always_used()
        {
            // A project written before stacks were per scene. Its one stack has to open as the
            // default view's, not as nobody's: anything else loses a coordinator's scheme on
            // upgrade, silently, which is the worst way to lose it.
            var section = JsonValue.Object();
            section.Set("stack", LayerStackJson.Write(new[] { Layer("layer-1", "Level 03") }));

            var read = StackLibrary.Read(section, null);

            Assert.Single(read);
            Assert.Equal("Level 03", read[0].Name);
            Assert.Equal("stack", StackLibrary.KeyFor(null));
            Assert.Equal("stack", StackLibrary.KeyFor(string.Empty));
        }

        [Fact]
        public void Each_viewpoint_gets_a_stack_of_its_own()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, null, new[] { Layer("a", "Default") });
            StackLibrary.Write(section, "Looking north", new[] { Layer("b", "North") });
            StackLibrary.Write(section, "Looking south", new[] { Layer("c", "South") });

            Assert.Equal("Default", StackLibrary.Read(section, null).Single().Name);
            Assert.Equal("North", StackLibrary.Read(section, "Looking north").Single().Name);
            Assert.Equal("South", StackLibrary.Read(section, "Looking south").Single().Name);

            // A scene nobody built a stack for has none, rather than inheriting somebody else's.
            Assert.Empty(StackLibrary.Read(section, "Looking east"));
        }

        [Fact]
        public void A_viewpoint_in_a_folder_is_still_one_scene()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, "Coordination/Level 03/North", new[] { Layer("a", "North") });

            Assert.Equal("North", StackLibrary.Read(section, "Coordination/Level 03/North").Single().Name);
            Assert.Equal(new string?[] { "Coordination/Level 03/North" }, StackLibrary.Scenes(section).ToArray());
        }

        [Fact]
        public void Emptying_a_scenes_stack_stops_it_being_listed_as_having_one()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, "Looking north", new[] { Layer("a", "North") });
            Assert.Single(StackLibrary.Scenes(section));

            StackLibrary.Write(section, "Looking north", Array.Empty<AppearanceLayer>());

            // Written as an empty array it would still be listed, and a list of scenes where half
            // the entries hold nothing is a list nobody reads.
            Assert.Empty(StackLibrary.Scenes(section));
            Assert.Empty(StackLibrary.Read(section, "Looking north"));
        }

        [Fact]
        public void The_default_view_is_listed_first_because_it_is_where_nobody_chose_a_viewpoint()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, "Looking north", new[] { Layer("a", "North") });
            StackLibrary.Write(section, null, new[] { Layer("b", "Default") });

            Assert.Equal(new string?[] { null, "Looking north" }, StackLibrary.Scenes(section).ToArray());
        }

        [Fact]
        public void A_copied_stack_gets_fresh_ids_so_two_copies_can_sit_in_one_scene()
        {
            var original = new List<AppearanceLayer> { Layer("layer-1", "a"), Layer("layer-2", "b") };

            var first = StackLibrary.Copy(original, 1000);
            var second = StackLibrary.Copy(original, 2000);

            var ids = first.Concat(second).Select(l => l.Id).ToList();

            // An id names a layer within the stack it is in, and the fold's explanations name the
            // layer that won by its id. Two layers answering to one name is a report nobody can
            // read, and copying a stack twice onto one scene is an ordinary thing to do.
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain("layer-1", ids);
        }

        [Fact]
        public void A_copy_carries_everything_the_layer_decides_and_its_order()
        {
            var original = new List<AppearanceLayer>
            {
                new AppearanceLayer("under", "Everything grey", LayerTarget.Everything())
                {
                    Colour = new Colour(120, 120, 120),
                    Transparency = 0.3,
                    Note = "the base",
                    IsEnabled = false,
                },
                new AppearanceLayer("over", "Level 03", LayerTarget.Elements(new[] { E("a") }))
                {
                    Visible = false,
                },
            };

            var copy = StackLibrary.Copy(original, 500);

            // The order is what decides which layer wins, so a copy that reordered them would be a
            // different scheme wearing the same names.
            Assert.Equal(new[] { "Everything grey", "Level 03" }, copy.Select(l => l.Name).ToArray());

            Assert.Equal(new Colour(120, 120, 120), copy[0].Colour);
            Assert.Equal(0.3, copy[0].Transparency);
            Assert.Equal("the base", copy[0].Note);
            Assert.False(copy[0].IsEnabled);
            Assert.True(copy[0].Target.ReResolves);

            Assert.False(copy[1].Visible);
            Assert.Equal(E("a"), copy[1].Target.Keys.Single());
        }

        [Fact]
        public void Copying_a_stack_onto_a_scene_leaves_the_one_it_came_from_alone()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, null, new[] { Layer("layer-1", "Level 03") });

            var copied = StackLibrary.Copy(StackLibrary.Read(section, null), 700);
            StackLibrary.Write(section, "Looking north", copied);

            var home = StackLibrary.Read(section, null);
            var away = StackLibrary.Read(section, "Looking north");

            Assert.Equal("Level 03", home.Single().Name);
            Assert.Equal("Level 03", away.Single().Name);
            Assert.NotEqual(home.Single().Id, away.Single().Id);
        }

        [Fact]
        public void A_section_with_nothing_in_it_reads_as_no_stacks_rather_than_throwing()
        {
            Assert.Empty(StackLibrary.Read(null, null));
            Assert.Empty(StackLibrary.Read(JsonValue.Object(), "Looking north"));
            Assert.Empty(StackLibrary.Scenes(null));
            Assert.Empty(StackLibrary.Scenes(JsonValue.Object()));
        }

        [Fact]
        public void Keys_that_are_not_stacks_are_not_mistaken_for_scenes()
        {
            var section = JsonValue.Object();

            StackLibrary.Write(section, "Looking north", new[] { Layer("a", "North") });

            // Something else's key in the same section, and a prefix with nothing after it.
            section.Set("lastUsed", "Looking north");
            section.Set("stack:", LayerStackJson.Write(Array.Empty<AppearanceLayer>()));

            Assert.Equal(new string?[] { "Looking north" }, StackLibrary.Scenes(section).ToArray());
        }
    }
}
