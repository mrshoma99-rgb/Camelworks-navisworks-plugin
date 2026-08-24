using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SettingOutTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Struct.nwc", name);

        private static SetOutElement Element(string name, double x, double y, double z,
                                             double sx, double sy, double sz,
                                             bool hasGeometry = true, string? mark = null) =>
            new SetOutElement(Key(name), name,
                              new BoundingBox(x, y, z, x + sx, y + sy, z + sz), hasGeometry, mark);

        private static SetOutRecipe Recipe(params PointKind[] kinds)
        {
            var recipe = new SetOutRecipe("Columns");

            recipe.Kinds.Clear();

            foreach (var kind in kinds) recipe.Kinds.Add(kind);

            return recipe;
        }

        // ---- Where the point is ----------------------------------------------------------

        [Fact]
        public void The_base_point_is_the_middle_of_the_bottom_which_is_what_a_column_is_set_out_from()
        {
            var result = SettingOut.Of(new[] { Element("C-01", 10, 20, 30, 0.4, 0.4, 4) },
                                       Recipe(PointKind.Base));

            Assert.Null(result.Refusal);
            Assert.Single(result.Points);

            var point = result.Points[0];

            Assert.Equal(20.2, point.Northing, 6);
            Assert.Equal(10.2, point.Easting, 6);
            Assert.Equal(30, point.Level, 6);
            Assert.Equal(PointKind.Base, point.Kind);
        }

        [Fact]
        public void The_top_and_the_centre_are_where_they_say_they_are()
        {
            var result = SettingOut.Of(new[] { Element("C-01", 0, 0, 10, 1, 1, 4) },
                                       Recipe(PointKind.Base, PointKind.Centre, PointKind.Top));

            Assert.Equal(3, result.Points.Count);
            Assert.Equal(new[] { 10.0, 12.0, 14.0 }, result.Points.Select(p => p.Level));
        }

        [Fact]
        public void A_long_run_has_two_ends_and_they_are_the_ends_of_its_longest_side()
        {
            var duct = Element("D-01", 0, 5, 3, 20, 0.5, 0.5);

            var result = SettingOut.Of(new[] { duct }, Recipe(PointKind.Start, PointKind.End));

            Assert.Equal(2, result.Points.Count);
            Assert.Equal(0, result.Points[0].Easting, 6);
            Assert.Equal(20, result.Points[1].Easting, 6);
            Assert.Equal(5.25, result.Points[0].Northing, 6);
        }

        [Fact]
        public void A_riser_runs_up_so_its_ends_are_top_and_bottom()
        {
            var result = SettingOut.Of(new[] { Element("R-01", 0, 0, 0, 0.5, 0.5, 12) },
                                       Recipe(PointKind.Start, PointKind.End));

            Assert.Equal(2, result.Points.Count);
            Assert.Equal(0, result.Points[0].Level, 6);
            Assert.Equal(12, result.Points[1].Level, 6);
        }

        [Fact]
        public void A_box_has_a_middle_and_not_a_run_so_it_gets_no_ends()
        {
            // Two arbitrary corners of a slab are not the ends of anything.
            var result = SettingOut.Of(new[] { Element("S-01", 0, 0, 0, 6, 5, 0.3) },
                                       Recipe(PointKind.Start, PointKind.End));

            Assert.NotNull(result.Refusal);
            Assert.Contains("not long enough", result.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void An_element_too_square_for_ends_still_gives_the_points_that_do_mean_something()
        {
            var result = SettingOut.Of(new[] { Element("S-01", 0, 0, 0, 6, 5, 0.3) },
                                       Recipe(PointKind.Base, PointKind.Start));

            Assert.Single(result.Points);
            Assert.Equal(PointKind.Base, result.Points[0].Kind);
            Assert.Contains(result.Notes, n => n.Contains("A box has a middle", StringComparison.Ordinal));
        }

        [Fact]
        public void Which_way_a_thing_runs_is_the_ratio_of_its_longest_side_to_the_next()
        {
            Assert.True(SettingOut.Run(new BoundingBox(0, 0, 0, 20, 1, 1)).Long);
            Assert.Equal(0, SettingOut.Run(new BoundingBox(0, 0, 0, 20, 1, 1)).Axis);

            Assert.True(SettingOut.Run(new BoundingBox(0, 0, 0, 1, 1, 12)).Long);
            Assert.Equal(2, SettingOut.Run(new BoundingBox(0, 0, 0, 1, 1, 12)).Axis);

            Assert.False(SettingOut.Run(new BoundingBox(0, 0, 0, 6, 5, 0.3)).Long);
            Assert.False(SettingOut.Run(default).Long);
        }

        // ---- Site coordinates ------------------------------------------------------------

        [Fact]
        public void The_survey_offset_moves_every_point_by_the_same_amount()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.NorthAtOrigin = 500000;
            recipe.EastAtOrigin = 300000;
            recipe.LevelAtOrigin = 42;

            var point = SettingOut.Of(new[] { Element("C-01", 10, 20, 3, 1, 1, 4) }, recipe)
                                  .Points[0];

            Assert.Equal(500020.5, point.Northing, 6);
            Assert.Equal(300010.5, point.Easting, 6);
            Assert.Equal(45, point.Level, 6);
        }

        [Fact]
        public void A_project_angle_turns_the_model_clockwise_onto_the_site_grid()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.RotationDegrees = 90;

            // A point ten metres east of the model origin, turned ninety degrees clockwise, is ten
            // metres south of it - which is the sign everybody gets wrong the first time.
            var point = SettingOut.Of(new[] { Element("C-01", 10, 0, 0, 0, 0, 0) }, recipe).Points[0];

            Assert.Equal(-10, point.Northing, 6);
            Assert.Equal(0, point.Easting, 6);
        }

        [Fact]
        public void Turning_the_model_all_the_way_round_puts_it_back_where_it_started()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.RotationDegrees = 360;

            var point = SettingOut.Of(new[] { Element("C-01", 7, 11, 0, 0, 0, 0) }, recipe).Points[0];

            Assert.Equal(11, point.Northing, 6);
            Assert.Equal(7, point.Easting, 6);
        }

        [Fact]
        public void Saying_nothing_about_where_the_site_is_is_called_out_before_it_reaches_an_instrument()
        {
            var result = SettingOut.Of(new[] { Element("C-01", 0, 0, 0, 1, 1, 1) },
                                       Recipe(PointKind.Base));

            Assert.True(result.Recipe.IsUnshifted);
            Assert.Contains(result.Notes,
                            n => n.Contains("These are model coordinates", StringComparison.Ordinal));
        }

        [Fact]
        public void The_coordinates_are_rounded_to_the_places_that_were_asked_for()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.Decimals = 2;
            recipe.EastAtOrigin = 0.005;

            var point = SettingOut.Of(new[] { Element("C-01", 1.2345, 0, 0, 0, 0, 0) }, recipe)
                                  .Points[0];

            Assert.Equal(1.24, point.Easting, 6);
        }

        // ---- What the points are called --------------------------------------------------

        [Fact]
        public void Points_are_numbered_in_order_behind_the_prefix_that_was_asked_for()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.Prefix = "RIV";

            var result = SettingOut.Of(new[]
            {
                Element("C-01", 0, 0, 0, 1, 1, 1),
                Element("C-02", 5, 0, 0, 1, 1, 1),
            }, recipe);

            Assert.Equal(new[] { "RIV001", "RIV002" }, result.Points.Select(p => p.Number));
        }

        [Fact]
        public void A_point_is_named_after_the_mark_where_the_model_carries_one()
        {
            var result = SettingOut.Of(new[]
            {
                Element("Structural Column [418922]", 0, 0, 0, 1, 1, 1, mark: "C-14"),
                Element("Structural Column [418923]", 5, 0, 0, 1, 1, 1),
            }, Recipe(PointKind.Base));

            Assert.Equal("C-14 — base", result.Points[0].Description);
            Assert.Equal("Structural Column [418923] — base", result.Points[1].Description);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void Nothing_selected_says_what_to_select()
        {
            Assert.Contains("Select what is going on site",
                            SettingOut.Of(null, Recipe(PointKind.Base)).Refusal ?? string.Empty,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void No_kind_of_point_asked_for_says_which_one_is_usual()
        {
            var recipe = new SetOutRecipe("Empty");

            recipe.Kinds.Clear();

            Assert.Contains("the base of each element is the usual one",
                            SettingOut.Of(new[] { Element("C-01", 0, 0, 0, 1, 1, 1) }, recipe).Refusal
                                ?? string.Empty,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void Grouping_nodes_are_counted_rather_than_setting_out_the_origin()
        {
            var result = SettingOut.Of(new[]
            {
                Element("C-01", 3, 4, 0, 1, 1, 1),
                Element("Group", 0, 0, 0, 0, 0, 0, hasGeometry: false),
            }, Recipe(PointKind.Base));

            Assert.Single(result.Points);
            Assert.Equal(1, result.Bodiless);
            Assert.Contains("no body to take a point off", result.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Every_element_being_a_grouping_node_says_so()
        {
            var result = SettingOut.Of(new[] { Element("Group", 0, 0, 0, 0, 0, 0, hasGeometry: false) },
                                       Recipe(PointKind.Base));

            Assert.NotNull(result.Refusal);
            Assert.Contains("Grouping nodes", result.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_run_stops_at_its_ceiling_and_says_how_many_it_left()
        {
            var many = Enumerable.Range(0, 30)
                .Select(i => Element("C-" + i, i, 0, 0, 1, 1, 1))
                .ToList();

            var recipe = Recipe(PointKind.Base);

            recipe.Ceiling = 20;

            var result = SettingOut.Of(many, recipe);

            Assert.Equal(20, result.Points.Count);
            Assert.Equal(10, result.Dropped);
            Assert.Contains("did not fit under the ceiling", result.Explain(), StringComparison.Ordinal);
        }

        // ---- The file that goes to the instrument ----------------------------------------

        [Fact]
        public void The_file_says_what_the_coordinates_are_referenced_to_before_it_says_anything_else()
        {
            var recipe = Recipe(PointKind.Base);

            recipe.NorthAtOrigin = 500000;
            recipe.EastAtOrigin = 300000;
            recipe.LevelAtOrigin = 42.5;
            recipe.RotationDegrees = 13.5;

            var csv = SettingOut.Csv(
                SettingOut.Of(new[] { Element("C-01", 0, 0, 0, 1, 1, 1) }, recipe), "Riverside");

            Assert.StartsWith("# Riverside", csv, StringComparison.Ordinal);
            Assert.Contains("bimcamel.com", csv, StringComparison.Ordinal);
            Assert.Contains("N 500000.000, E 300000.000, level 42.500", csv, StringComparison.Ordinal);
            Assert.Contains("rotated 13.5° clockwise", csv, StringComparison.Ordinal);
            Assert.Contains("Northing before easting", csv, StringComparison.Ordinal);
        }

        [Fact]
        public void The_columns_are_the_ones_an_instrument_expects_and_in_that_order()
        {
            var csv = SettingOut.Csv(
                SettingOut.Of(new[] { Element("C-01", 10, 20, 30, 0, 0, 0) }, Recipe(PointKind.Base)));

            var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var heading = lines.Single(l => !l.StartsWith("#", StringComparison.Ordinal)
                                            && l.StartsWith("Point", StringComparison.Ordinal));

            Assert.Equal("Point,Northing,Easting,Level,Description", heading);
            Assert.Contains("SO001,20.000,10.000,30.000,C-01 — base", csv, StringComparison.Ordinal);
        }

        [Fact]
        public void A_description_with_a_comma_in_it_does_not_break_the_file()
        {
            var csv = SettingOut.Csv(
                SettingOut.Of(new[] { Element("Column, upper", 0, 0, 0, 1, 1, 1) },
                              Recipe(PointKind.Base)));

            Assert.Contains("\"Column, upper — base\"", csv, StringComparison.Ordinal);
        }

        [Fact]
        public void There_is_no_file_when_there_are_no_points()
        {
            Assert.Equal(string.Empty, SettingOut.Csv(null));
            Assert.Equal(string.Empty, SettingOut.Csv(SettingOut.Of(null, Recipe(PointKind.Base))));
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void A_setting_out_run_survives_being_written_down_and_read_back()
        {
            var recipe = Recipe(PointKind.Base, PointKind.Top);

            recipe.Name = "Steel bases";
            recipe.Prefix = "STL";
            recipe.NorthAtOrigin = 500000.25;
            recipe.EastAtOrigin = 300000.5;
            recipe.LevelAtOrigin = 42.5;
            recipe.RotationDegrees = 13.5;
            recipe.Decimals = 4;
            recipe.Ceiling = 500;

            var back = SetOutRecipe.FromJson(recipe.ToJson());

            Assert.Equal("Steel bases", back.Name);
            Assert.Equal("STL", back.Prefix);
            Assert.Equal(new[] { PointKind.Base, PointKind.Top }, back.Kinds);
            Assert.Equal(500000.25, back.NorthAtOrigin, 6);
            Assert.Equal(300000.5, back.EastAtOrigin, 6);
            Assert.Equal(42.5, back.LevelAtOrigin, 6);
            Assert.Equal(13.5, back.RotationDegrees, 6);
            Assert.Equal(4, back.Decimals);
            Assert.Equal(500, back.Ceiling);
        }

        [Fact]
        public void An_unreadable_run_comes_back_taking_base_points_and_nothing_else()
        {
            var empty = SetOutRecipe.FromJson(null);

            Assert.Equal(new[] { PointKind.Base }, empty.Kinds);
            Assert.Equal("SO", empty.Prefix);
            Assert.Equal(3, empty.Decimals);
            Assert.True(empty.IsUnshifted);
        }

        [Fact]
        public void Saving_one_under_a_name_already_used_replaces_it()
        {
            var first = new SetOutRecipe("Steel bases");
            var kept = SettingOut.Keep(SettingOut.Saved(null), first);

            Assert.Single(SettingOut.Saved(kept));

            var second = Recipe(PointKind.Top);
            second.Name = "STEEL BASES";

            var again = SettingOut.Saved(SettingOut.Keep(SettingOut.Saved(kept), second));

            Assert.Single(again);
            Assert.Equal(new[] { PointKind.Top }, again[0].Kinds);
        }

        [Fact]
        public void A_copy_can_be_edited_without_disturbing_what_was_saved()
        {
            var saved = Recipe(PointKind.Base);
            var working = saved.Copy();

            working.Prefix = "XX";
            working.Kinds.Add(PointKind.Top);

            Assert.Equal("SO", saved.Prefix);
            Assert.Single(saved.Kinds);
        }
    }
}
