using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Publish;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ExtractTests
    {
        private static ReportElement Element(string name, string model, double z, double top,
                                             bool hasGeometry = true,
                                             params (string Category, string Name, string? Value)[] properties)
        {
            var key = ElementKey.FromTreePath(model, @"C:\Jobs\Riverside\" + model + ".nwc", name);

            return new ReportElement(key, name, "Generic", "Generic", model, new[] { model, name },
                                     properties.Select(p => new PropertyValue(p.Category, p.Name, p.Value)),
                                     new BoundingBox(0, 0, z, 1, 1, top), hasGeometry);
        }

        private static IReadOnlyList<ReportElement> Building()
        {
            return new[]
            {
                Element("Duct 01", "Mech", 0, 3, true, ("Set", "Zone", "Plant Room")),
                Element("Duct 02", "Mech", 4, 7, true, ("Set", "Zone", "Riser")),
                Element("Wall 01", "Arch", 0, 3, true, ("Set", "Zone", "Plant Room")),
                Element("Wall 02", "Arch", 4, 7, true),
                Element("Group", "Arch", 0, 0, false, ("Set", "Zone", "Plant Room")),
            };
        }

        private static LevelSet Levels() =>
            LevelSet.FromModel(new[]
            {
                new KeyValuePair<string, double>("Level 00", 0),
                new KeyValuePair<string, double>("Level 01", 4),
                new KeyValuePair<string, double>("Level 02", 8),
            });

        // ---- The axes --------------------------------------------------------------------

        [Fact]
        public void An_extract_of_the_selection_takes_what_was_handed_to_it()
        {
            var scope = Building();
            var chosen = new[] { scope[0].Key, scope[2].Key };

            var plan = Extract.Plan(scope, new ExtractRecipe("Selected"), chosen);

            Assert.Null(plan.Refusal);
            Assert.Equal(2, plan.Count);
            Assert.Equal(chosen, plan.Keys);
            Assert.Equal(5, plan.Scope);
        }

        [Fact]
        public void An_extract_of_a_viewpoint_takes_what_the_viewpoint_holds()
        {
            var scope = Building();

            var recipe = new ExtractRecipe("Coordination view")
            {
                Axis = ExtractAxis.Viewpoint,
                Named = "Level 00 plant",
            };

            var plan = Extract.Plan(scope, recipe, new[] { scope[0].Key });

            Assert.Equal(1, plan.Count);
            Assert.Contains("viewpoint \"Level 00 plant\"", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_extract_of_one_level_takes_what_stands_on_it()
        {
            var scope = Building();

            var recipe = new ExtractRecipe("Level 00")
            {
                Axis = ExtractAxis.Level,
                Named = "Level 00",
            };

            var plan = Extract.Plan(scope, recipe, null, Levels());

            Assert.Equal(2, plan.Count);
            Assert.Contains(scope[0].Key, plan.Keys);
            Assert.Contains(scope[2].Key, plan.Keys);
            Assert.DoesNotContain(scope[1].Key, plan.Keys);
        }

        [Fact]
        public void A_thing_that_runs_between_two_levels_belongs_to_the_one_it_stands_on()
        {
            // A column floor to floor is on the level it starts from, not the one its middle is in.
            var tall = new[] { Element("Column", "Struct", 0, 7.5) };

            var recipe = new ExtractRecipe("Level 00") { Axis = ExtractAxis.Level, Named = "Level 00" };

            Assert.Equal(1, Extract.Plan(tall, recipe, null, Levels()).Count);

            recipe.Named = "Level 01";

            Assert.Equal(0, Extract.Plan(tall, recipe, null, Levels()).Count);
        }

        [Fact]
        public void An_extract_by_a_property_value_takes_everything_carrying_it()
        {
            var scope = Building();

            var recipe = new ExtractRecipe("Plant Room")
            {
                Axis = ExtractAxis.Where,
                Property = new PropertyRef("Set", "Zone"),
                Value = "Plant Room",
            };

            var plan = Extract.Plan(scope, recipe);

            // Three carry it, but the grouping node has no geometry and is left out.
            Assert.Equal(2, plan.Count);
            Assert.Equal(1, plan.WithoutGeometry);
            Assert.Contains("carries", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_extract_by_a_property_with_no_value_named_takes_everything_that_has_one()
        {
            var recipe = new ExtractRecipe("Zoned")
            {
                Axis = ExtractAxis.Where,
                Property = new PropertyRef("Set", "Zone"),
            };

            // Four carry a zone; the one without geometry is dropped, so three are left.
            Assert.Equal(3, Extract.Plan(Building(), recipe).Count);
        }

        [Fact]
        public void An_extract_of_one_model_takes_only_that_model()
        {
            var recipe = new ExtractRecipe("Mechanical") { Axis = ExtractAxis.Model, Named = "Mech" };

            var plan = Extract.Plan(Building(), recipe);

            Assert.Equal(2, plan.Count);
            Assert.Single(plan.Models);
            Assert.Equal("Mech", plan.Models[0].Model);
        }

        [Fact]
        public void An_extract_by_a_box_takes_what_the_box_reaches()
        {
            var scope = Building();

            var recipe = new ExtractRecipe("Lower")
            {
                Axis = ExtractAxis.Box,
                Box = new ExtractBox(-1, -1, -1, 2, 2, 3.5),
            };

            Assert.Equal(2, Extract.Plan(scope, recipe).Count);

            recipe.Reach = BoxReach.Inside;

            Assert.Equal(2, Extract.Plan(scope, recipe).Count);

            recipe.Box = new ExtractBox(-1, -1, -1, 2, 2, 1);

            // Touching still catches them; wholly-inside no longer does.
            Assert.Equal(0, Extract.Plan(scope, recipe).Count);

            recipe.Reach = BoxReach.Touching;

            Assert.Equal(2, Extract.Plan(scope, recipe).Count);
        }

        [Fact]
        public void A_box_that_only_has_to_hold_the_centre_holds_what_is_centred_in_it()
        {
            var scope = new[] { Element("Beam", "Struct", 0, 4) };

            var recipe = new ExtractRecipe("Middling")
            {
                Axis = ExtractAxis.Box,
                Box = new ExtractBox(-1, -1, 1.5, 2, 2, 2.5),
                Reach = BoxReach.Centre,
            };

            Assert.Equal(1, Extract.Plan(scope, recipe).Count);

            recipe.Reach = BoxReach.Inside;

            Assert.Equal(0, Extract.Plan(scope, recipe).Count);
        }

        [Fact]
        public void The_corners_of_a_box_can_be_given_in_either_order()
        {
            var box = new ExtractBox(5, 5, 5, 1, 1, 1);

            Assert.Equal(1, box.MinX);
            Assert.Equal(5, box.MaxX);
            Assert.False(box.IsFlat);
            Assert.True(new ExtractBox(0, 0, 0, 1, 1, 0).IsFlat);
        }

        [Fact]
        public void A_box_can_be_taken_from_what_is_already_there()
        {
            var box = ExtractBox.Around(Building());

            Assert.NotNull(box);
            Assert.Equal(0, box!.MinZ);
            Assert.Equal(7, box.MaxZ);

            var padded = ExtractBox.Around(Building(), 2);

            Assert.NotNull(padded);
            Assert.Equal(-2, padded!.MinZ);
            Assert.Equal(9, padded.MaxZ);

            // The grouping node has no geometry and no box, so it contributes nothing.
            Assert.Null(ExtractBox.Around(new[] { Element("Group", "Arch", 0, 0, false) }));
            Assert.Null(ExtractBox.Around(null));
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void Each_way_of_choosing_says_what_is_missing_in_its_own_words()
        {
            var scope = Building();

            Assert.Contains("no elements in scope",
                Extract.Plan(Array.Empty<ReportElement>(), new ExtractRecipe()).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("Nothing is selected",
                Extract.Plan(scope, new ExtractRecipe()).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("which viewpoint",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Viewpoint }).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("which set",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Set }).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("no levels to extract by",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Level }).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("Pick the property",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Where }).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("no box yet",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Box }).Refusal ?? string.Empty,
                StringComparison.Ordinal);

            Assert.Contains("which model",
                Extract.Plan(scope, new ExtractRecipe { Axis = ExtractAxis.Model }).Refusal ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_named_thing_that_holds_nothing_here_says_it_may_be_from_another_revision()
        {
            var recipe = new ExtractRecipe("Old view")
            {
                Axis = ExtractAxis.Viewpoint,
                Named = "Level 09",
            };

            Assert.Contains("different revision",
                Extract.Plan(Building(), recipe, Array.Empty<ElementKey>()).Refusal ?? string.Empty,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_box_with_no_thickness_is_refused_rather_than_returning_nothing_silently()
        {
            var recipe = new ExtractRecipe("Flat")
            {
                Axis = ExtractAxis.Box,
                Box = new ExtractBox(0, 0, 3, 5, 5, 3),
            };

            Assert.Contains("no thickness",
                Extract.Plan(Building(), recipe).Refusal ?? string.Empty, StringComparison.Ordinal);
        }

        [Fact]
        public void An_axis_that_matches_nothing_says_so_rather_than_reading_as_success()
        {
            var recipe = new ExtractRecipe("Nowhere") { Axis = ExtractAxis.Model, Named = "Landscape" };

            var plan = Extract.Plan(Building(), recipe);

            Assert.Null(plan.Refusal);
            Assert.False(plan.IsUsable);
            Assert.Contains("Nothing matched", plan.Explain(), StringComparison.Ordinal);
        }

        // ---- What it reports -------------------------------------------------------------

        [Fact]
        public void It_says_which_models_the_piece_came_from_biggest_first()
        {
            var scope = Building();

            var recipe = new ExtractRecipe("All of it")
            {
                Axis = ExtractAxis.Where,
                Property = new PropertyRef("Set", "Zone"),
            };

            var plan = Extract.Plan(scope, recipe);

            Assert.Equal(2, plan.Models.Count);
            Assert.Equal("Mech", plan.Models[0].Model);
            Assert.Equal(2, plan.Models[0].Count);
            Assert.Equal("Arch", plan.Models[1].Model);
            Assert.Equal(1, plan.Models[1].Count);
        }

        [Fact]
        public void It_measures_the_piece_so_a_person_can_tell_it_is_the_right_one()
        {
            var recipe = new ExtractRecipe("Mechanical") { Axis = ExtractAxis.Model, Named = "Mech" };

            var plan = Extract.Plan(Building(), recipe);

            Assert.NotNull(plan.Bounds);
            Assert.Equal(0, plan.Bounds!.MinZ);
            Assert.Equal(7, plan.Bounds.MaxZ);
            Assert.Contains("1 x 1 x 7 m", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Grouping_nodes_can_be_kept_when_somebody_wants_the_tree_as_well()
        {
            var recipe = new ExtractRecipe("Plant Room")
            {
                Axis = ExtractAxis.Where,
                Property = new PropertyRef("Set", "Zone"),
                Value = "Plant Room",
                GeometryOnly = false,
            };

            var plan = Extract.Plan(Building(), recipe);

            Assert.Equal(3, plan.Count);
            Assert.Equal(0, plan.WithoutGeometry);
        }

        [Fact]
        public void An_extract_stops_at_its_ceiling_and_says_how_many_it_left()
        {
            var many = Enumerable.Range(0, 40)
                .Select(i => Element("Part " + i, "Mech", 0, 1))
                .ToList();

            var recipe = new ExtractRecipe("Capped")
            {
                Axis = ExtractAxis.Model,
                Named = "Mech",
                Ceiling = 25,
            };

            var plan = Extract.Plan(many, recipe);

            Assert.Equal(25, plan.Count);
            Assert.Equal(15, plan.Dropped);
            Assert.Contains("did not fit under the ceiling", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void An_extract_by_a_box_says_that_a_thing_running_through_it_comes_whole()
        {
            var recipe = new ExtractRecipe("Area")
            {
                Axis = ExtractAxis.Box,
                Box = new ExtractBox(-1, -1, -1, 2, 2, 3.5),
            };

            Assert.Contains(Extract.Plan(Building(), recipe).Notes,
                            n => n.Contains("come whole rather than cut", StringComparison.Ordinal));
        }

        // ---- Offering the choices --------------------------------------------------------

        [Fact]
        public void The_values_a_property_takes_are_listed_so_nobody_has_to_guess_at_one()
        {
            Assert.Equal(new[] { "Plant Room", "Riser" },
                Extract.Values(Building(), new PropertyRef("Set", "Zone")));

            Assert.Empty(Extract.Values(Building(), new PropertyRef("Set", "Nothing")));
            Assert.Empty(Extract.Values(Building(), default));
            Assert.Empty(Extract.Values(null, new PropertyRef("Set", "Zone")));
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void An_extract_survives_being_written_down_and_read_back()
        {
            var recipe = new ExtractRecipe("Level 03 mechanical")
            {
                Axis = ExtractAxis.Box,
                Named = "Level 03",
                Property = new PropertyRef("Set", "Zone"),
                Value = "Riser",
                Box = new ExtractBox(1, 2, 3, 4, 5, 6),
                Reach = BoxReach.Centre,
                GeometryOnly = false,
                Ceiling = 500,
            };

            var back = ExtractRecipe.FromJson(recipe.ToJson());

            Assert.Equal("Level 03 mechanical", back.Name);
            Assert.Equal(ExtractAxis.Box, back.Axis);
            Assert.Equal("Level 03", back.Named);
            Assert.Equal("Zone", back.Property.Name);
            Assert.Equal("Set", back.Property.Category);
            Assert.Equal("Riser", back.Value);
            Assert.Equal(BoxReach.Centre, back.Reach);
            Assert.False(back.GeometryOnly);
            Assert.Equal(500, back.Ceiling);

            Assert.NotNull(back.Box);
            Assert.Equal(1, back.Box!.MinX);
            Assert.Equal(6, back.Box.MaxZ);
        }

        [Fact]
        public void An_unreadable_extract_comes_back_as_a_plain_one_rather_than_throwing()
        {
            var empty = ExtractRecipe.FromJson(null);

            Assert.Equal(ExtractAxis.Selection, empty.Axis);
            Assert.Equal(ExtractRecipe.DefaultCeiling, empty.Ceiling);
            Assert.Null(empty.Box);
            Assert.True(empty.GeometryOnly);
        }

        [Fact]
        public void A_copy_can_be_edited_without_disturbing_what_was_saved()
        {
            var saved = new ExtractRecipe("Level 00") { Axis = ExtractAxis.Level, Named = "Level 00" };
            var working = saved.Copy();

            working.Named = "Level 01";
            working.Axis = ExtractAxis.Model;

            Assert.Equal("Level 00", saved.Named);
            Assert.Equal(ExtractAxis.Level, saved.Axis);
        }

        [Fact]
        public void Saving_an_extract_under_a_name_already_used_replaces_it()
        {
            var first = new ExtractRecipe("Level 00") { Axis = ExtractAxis.Level, Named = "Level 00" };

            var kept = Extract.Keep(Extract.Saved(null), first);

            Assert.Single(Extract.Saved(kept));

            var second = new ExtractRecipe("LEVEL 00") { Axis = ExtractAxis.Model, Named = "Mech" };
            var again = Extract.Saved(Extract.Keep(Extract.Saved(kept), second));

            Assert.Single(again);
            Assert.Equal(ExtractAxis.Model, again[0].Axis);
        }

        // ---- Getting it out --------------------------------------------------------------

        [Fact]
        public void The_file_is_named_after_the_extract_and_will_open_on_any_machine()
        {
            Assert.Equal("Level 03 mechanical.ifc",
                Extract.FileName(new ExtractRecipe("Level 03 mechanical"), ".ifc"));

            Assert.Equal("Level 03-Mech.dxf",
                Extract.FileName(new ExtractRecipe("Level 03/Mech"), ".dxf"));

            Assert.Equal("Extract.dxf", Extract.FileName(new ExtractRecipe("///"), ".dxf"));
            Assert.Equal("Extract.dxf", Extract.FileName(null, ".dxf"));
        }

        [Fact]
        public void What_the_extract_takes_reads_as_a_phrase_a_person_would_say()
        {
            Assert.Equal("what is selected", new ExtractRecipe().Describe());

            Assert.Equal("everything on Level 03",
                new ExtractRecipe { Axis = ExtractAxis.Level, Named = "Level 03" }.Describe());

            Assert.Equal("everything whose Zone is \"Riser\"",
                new ExtractRecipe
                {
                    Axis = ExtractAxis.Where,
                    Property = new PropertyRef("Set", "Zone"),
                    Value = "Riser",
                }.Describe());

            Assert.Contains("wholly inside",
                new ExtractRecipe
                {
                    Axis = ExtractAxis.Box,
                    Box = new ExtractBox(0, 0, 0, 1, 1, 1),
                    Reach = BoxReach.Inside,
                }.Describe(),
                StringComparison.Ordinal);
        }
    }
}
