using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Field;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class DimensionsTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath("model", @"C:\Jobs\Riverside\Struct.nwc", name);

        private static FieldElement Box(string name, double x, double y, double z,
                                        double sx, double sy, double sz,
                                        bool hasGeometry = true) =>
            new FieldElement(Key(name), name,
                             new BoundingBox(x, y, z, x + sx, y + sy, z + sz), hasGeometry);

        /// <summary>A stick between two points, given a little width so it is not a line.</summary>
        private static FieldElement Stick(string name,
                                          double ax, double ay, double az,
                                          double bx, double by, double bz,
                                          double width = 0.1)
        {
            // Eight corners of a box that runs from a to b, fattened square to the run. Enough of
            // a body for the extent to have something to find, and deliberately not axis-aligned
            // in the tests that matter.
            var dx = bx - ax; var dy = by - ay; var dz = bz - az;
            var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            dx /= length; dy /= length; dz /= length;

            // Any two directions square to the run.
            var px = Math.Abs(dx) < 0.9 ? 1.0 : 0.0;
            var py = Math.Abs(dx) < 0.9 ? 0.0 : 1.0;

            var ux = dy * 0 - dz * py; var uy = dz * px - dx * 0; var uz = dx * py - dy * px;
            var un = Math.Sqrt(ux * ux + uy * uy + uz * uz);

            ux /= un; uy /= un; uz /= un;

            var vx = dy * uz - dz * uy; var vy = dz * ux - dx * uz; var vz = dx * uy - dy * ux;

            var points = new List<float>();
            var half = width / 2;

            foreach (var end in new[] { 0.0, 1.0 })
                foreach (var su in new[] { -1.0, 1.0 })
                    foreach (var sv in new[] { -1.0, 1.0 })
                    {
                        points.Add((float)(ax + dx * length * end + ux * half * su + vx * half * sv));
                        points.Add((float)(ay + dy * length * end + uy * half * su + vy * half * sv));
                        points.Add((float)(az + dz * length * end + uz * half * su + vz * half * sv));
                    }

            var xs = new List<double>(); var ys = new List<double>(); var zs = new List<double>();

            for (var at = 0; at < points.Count; at += 3)
            {
                xs.Add(points[at]); ys.Add(points[at + 1]); zs.Add(points[at + 2]);
            }

            var bounds = new BoundingBox(xs.Min(), ys.Min(), zs.Min(), xs.Max(), ys.Max(), zs.Max());

            return new FieldElement(Key(name), name, bounds, true, null, points);
        }

        // ---- the reason the oriented path exists -------------------------------------------

        [Fact]
        public void A_brace_on_the_diagonal_is_not_as_long_as_its_box()
        {
            // 3-4-5: a brace 5 m long lying in a box 3 by 4.
            var brace = Stick("Brace", 0, 0, 0, 3, 4, 0);
            var extent = Dimensions.ExtentOf(brace);

            Assert.True(extent.Oriented);
            Assert.Equal(5.0, extent.Run, 2);

            // The box that contains it has a diagonal of 5 but no side longer than 4, so anything
            // reading the box gets the length of the brace wrong by a fifth.
            Assert.True(brace.Bounds.SizeX < 3.2);
            Assert.True(brace.Bounds.SizeY < 4.2);
        }

        [Fact]
        public void The_same_brace_read_off_its_box_gives_the_wrong_length_and_says_so()
        {
            var brace = Stick("Brace", 0, 0, 0, 3, 4, 0);
            var boxOnly = new FieldElement(brace.Key, "Brace", brace.Bounds);

            var truth = Dimensions.ExtentOf(brace);
            var guess = Dimensions.ExtentOf(boxOnly);

            Assert.True(truth.Oriented);
            Assert.False(guess.Oriented);
            Assert.True(guess.Run < truth.Run - 0.5);
        }

        [Fact]
        public void A_level_run_reads_level_and_a_plumb_one_reads_plumb()
        {
            var duct = Dimensions.ExtentOf(Stick("Duct", 0, 0, 3, 12, 0, 3, 0.4));
            var column = Dimensions.ExtentOf(Stick("Column", 0, 0, 0, 0, 0, 4, 0.3));

            Assert.True(duct.IsFlat);
            Assert.False(duct.IsPlumb);
            Assert.True(column.IsPlumb);
            Assert.False(column.IsFlat);
        }

        [Fact]
        public void A_ramp_reports_the_pitch_it_actually_runs_at()
        {
            // One up, one along: forty-five degrees.
            var ramp = Dimensions.ExtentOf(Stick("Ramp", 0, 0, 0, 4, 0, 4, 0.05));

            Assert.Equal(45.0, Math.Abs(ramp.PitchDegrees), 1);
        }

        [Fact]
        public void A_cube_has_no_length_and_the_extent_admits_it()
        {
            var cube = Dimensions.ExtentOf(Stick("Cube", 0, 0, 0, 1, 0, 0, 1.0));

            Assert.True(cube.AxisUncertain);
        }

        [Fact]
        public void A_beam_has_a_length_and_the_extent_is_sure_of_it()
        {
            var beam = Dimensions.ExtentOf(Stick("Beam", 0, 0, 0, 8, 0, 0, 0.3));

            Assert.False(beam.AxisUncertain);
            Assert.Equal(8.0, beam.Run, 2);
            Assert.Equal(0.3, beam.Across, 2);
            Assert.Equal(0.3, beam.Through, 2);
        }

        [Fact]
        public void Two_members_that_run_opposite_ways_report_the_same_direction()
        {
            var up = Dimensions.ExtentOf(Stick("Up", 0, 0, 0, 3, 4, 0));
            var down = Dimensions.ExtentOf(Stick("Down", 3, 4, 0, 0, 0, 0));

            Assert.Equal(up.RunX, down.RunX, 3);
            Assert.Equal(up.RunY, down.RunY, 3);
            Assert.Equal(up.RunZ, down.RunZ, 3);
        }

        [Fact]
        public void An_element_with_no_triangles_falls_back_to_the_box_in_order()
        {
            var extent = Dimensions.ExtentOf(Box("Slab", 0, 0, 0, 2, 9, 0.3));

            Assert.False(extent.Oriented);
            Assert.Equal(9.0, extent.Run, 6);
            Assert.Equal(2.0, extent.Across, 6);
            Assert.Equal(0.3, extent.Through, 6);
        }

        // ---- separation ---------------------------------------------------------------------

        [Fact]
        public void Two_boxes_a_metre_apart_are_a_metre_clear()
        {
            var apart = Dimensions.Between(Box("A", 0, 0, 0, 1, 1, 1), Box("B", 2, 0, 0, 1, 1, 1));

            Assert.Equal(1.0, apart.Clear, 6);
            Assert.Equal(2.0, apart.Centres, 6);
            Assert.False(apart.Overlaps);
        }

        [Fact]
        public void Boxes_that_are_into_one_another_have_no_gap_to_report()
        {
            var apart = Dimensions.Between(Box("A", 0, 0, 0, 2, 2, 2), Box("B", 1, 1, 1, 2, 2, 2));

            Assert.True(apart.Overlaps);
            Assert.Equal(0, apart.Clear, 6);
        }

        [Fact]
        public void The_gap_across_a_corner_is_the_diagonal_not_one_side_of_it()
        {
            // Offset on two axes at once: the true face-to-face distance is across the corner.
            var apart = Dimensions.Between(Box("A", 0, 0, 0, 1, 1, 1), Box("B", 4, 3, 0, 1, 1, 1));

            Assert.Equal(Math.Sqrt(9 + 4), apart.Clear, 6);
        }

        [Fact]
        public void Plan_ignores_height_and_level_is_signed()
        {
            var up = Dimensions.Between(Box("A", 0, 0, 0, 1, 1, 1), Box("B", 3, 0, 4, 1, 1, 1));
            var down = Dimensions.Between(Box("B", 0, 0, 4, 1, 1, 1), Box("A", 3, 0, 0, 1, 1, 1));

            Assert.Equal(3.0, up.Plan, 6);
            Assert.Equal(4.0, up.Level, 6);
            Assert.Equal(-4.0, down.Level, 6);
            Assert.Equal(5.0, up.Centres, 6);
        }

        // ---- spacing ------------------------------------------------------------------------

        [Fact]
        public void Hangers_at_centres_read_as_even()
        {
            var hangers = Enumerable.Range(0, 6)
                .Select(i => Box("H" + i, i * 2.0, 0, 3, 0.1, 0.1, 0.5))
                .ToList();

            var run = Dimensions.Spacing(hangers);

            Assert.True(run.IsUsable);
            Assert.Equal(Along.X, run.Axis);
            Assert.Equal(5, run.Steps.Count);
            Assert.Equal(2.0, run.Mean, 6);
            Assert.Equal(0, run.Drift, 6);
        }

        [Fact]
        public void One_hanger_out_of_place_shows_up_as_drift()
        {
            var hangers = new List<FieldElement>
            {
                Box("H0", 0.0, 0, 3, 0.1, 0.1, 0.5),
                Box("H1", 2.0, 0, 3, 0.1, 0.1, 0.5),
                Box("H2", 5.5, 0, 3, 0.1, 0.1, 0.5),
                Box("H3", 6.0, 0, 3, 0.1, 0.1, 0.5),
            };

            var run = Dimensions.Spacing(hangers);

            Assert.True(run.IsUsable);
            Assert.Equal(0.5, run.Least, 6);
            Assert.Equal(3.5, run.Most, 6);
            Assert.True(run.Drift > 0.7);
        }

        [Fact]
        public void Elements_out_of_order_in_the_list_are_still_measured_along_the_run()
        {
            var jumbled = new List<FieldElement>
            {
                Box("C", 4.0, 0, 0, 0.1, 0.1, 0.1),
                Box("A", 0.0, 0, 0, 0.1, 0.1, 0.1),
                Box("B", 2.0, 0, 0, 0.1, 0.1, 0.1),
            };

            var run = Dimensions.Spacing(jumbled);

            Assert.Equal("A", run.Steps[0].FromLabel);
            Assert.Equal("B", run.Steps[0].ToLabel);
            Assert.Equal("C", run.Steps[1].ToLabel);
        }

        [Fact]
        public void The_run_is_ordered_along_whichever_way_the_selection_spreads_furthest()
        {
            var stack = Enumerable.Range(0, 4)
                .Select(i => Box("L" + i, 0, 0, i * 3.5, 1, 1, 0.2))
                .ToList();

            Assert.Equal(Along.Level, Dimensions.Spacing(stack).Axis);
        }

        [Fact]
        public void A_run_of_one_is_not_a_run()
        {
            var run = Dimensions.Spacing(new[] { Box("A", 0, 0, 0, 1, 1, 1) });

            Assert.False(run.IsUsable);
            Assert.NotNull(run.Refusal);
        }

        // ---- the schedule -------------------------------------------------------------------

        [Fact]
        public void With_nothing_set_up_at_all_it_still_measures_something()
        {
            var schedule = Dimensions.Schedule(
                new[] { Box("Beam", 0, 0, 0, 6, 0.3, 0.5) }, new DimensionRecipe());

            Assert.True(schedule.IsUsable);
            Assert.Contains(schedule.Rows, r => r.Kind == DimensionKind.Run);
            Assert.Contains(schedule.Rows, r => r.Kind == DimensionKind.Across);
            Assert.Contains(schedule.Rows, r => r.Kind == DimensionKind.Through);
        }

        [Fact]
        public void Nothing_selected_is_refused_with_a_reason_not_an_empty_table()
        {
            var schedule = Dimensions.Schedule(Array.Empty<FieldElement>(), new DimensionRecipe());

            Assert.False(schedule.IsUsable);
            Assert.NotNull(schedule.Refusal);
            Assert.Contains("nothing selected", schedule.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Grouping_nodes_with_no_body_are_passed_over_and_counted()
        {
            var schedule = Dimensions.Schedule(new[]
            {
                Box("Group", 0, 0, 0, 6, 6, 6, hasGeometry: false),
                Box("Beam", 0, 0, 0, 6, 0.3, 0.5),
            }, new DimensionRecipe());

            Assert.True(schedule.IsUsable);
            Assert.Equal(1, schedule.Elements);
            Assert.Equal(1, schedule.Bodiless);
            Assert.Contains("no body", schedule.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_schedule_read_off_boxes_says_so_where_somebody_will_see_it()
        {
            var schedule = Dimensions.Schedule(
                new[] { Box("Brace", 0, 0, 0, 3, 4, 0.2) }, new DimensionRecipe());

            Assert.Equal(1, schedule.Boxed);
            Assert.Contains("world box", schedule.Explain(), StringComparison.OrdinalIgnoreCase);
            Assert.All(schedule.Rows.Where(r => r.Kind == DimensionKind.Run),
                       r => Assert.False(r.Oriented));
        }

        [Fact]
        public void Heights_are_measured_from_the_level_an_element_stands_on()
        {
            var levels = LevelSet.FromGrid(new[]
            {
                new KeyValuePair<string, double>("Ground", 0.0),
                new KeyValuePair<string, double>("First", 4.0),
            });

            var recipe = new DimensionRecipe { Sizes = false, Heights = true };

            var schedule = Dimensions.Schedule(new[]
            {
                Box("Duct", 0, 0, 6.5, 4, 0.4, 0.4),
            }, recipe, levels);

            var height = Assert.Single(schedule.Rows.Where(r => r.Kind == DimensionKind.Height));

            Assert.Equal("First", height.Against);
            Assert.Equal(2.5, height.Metres, 6);
        }

        [Fact]
        public void Without_levels_the_heights_are_skipped_and_the_schedule_says_why()
        {
            var recipe = new DimensionRecipe { Sizes = true, Heights = true };

            var schedule = Dimensions.Schedule(new[] { Box("Duct", 0, 0, 6.5, 4, 0.4, 0.4) }, recipe);

            Assert.DoesNotContain(schedule.Rows, r => r.Kind == DimensionKind.Height);
            Assert.Contains("no levels", schedule.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_nearest_neighbour_is_the_nearest_one_not_the_next_one_in_the_list()
        {
            var recipe = new DimensionRecipe { Sizes = false, Heights = false, Neighbours = true };

            var schedule = Dimensions.Schedule(new[]
            {
                Box("A", 0, 0, 0, 1, 1, 1),
                Box("Far", 50, 0, 0, 1, 1, 1),
                Box("Near", 1.5, 0, 0, 1, 1, 1),
            }, recipe);

            var fromA = schedule.Rows.First(r => r.Subject == "A");

            Assert.Equal("Near", fromA.Against);
            Assert.Equal(0.5, fromA.Metres, 6);
        }

        [Fact]
        public void A_selection_too_big_to_compare_every_pair_says_so_rather_than_sampling()
        {
            var many = Enumerable.Range(0, DimensionRecipe.NeighbourCeiling + 1)
                .Select(i => Box("E" + i, i, 0, 0, 0.5, 0.5, 0.5))
                .ToList();

            var recipe = new DimensionRecipe { Sizes = true, Heights = false, Neighbours = true };
            var schedule = Dimensions.Schedule(many, recipe);

            Assert.DoesNotContain(schedule.Rows, r => r.Kind == DimensionKind.Clear);
            Assert.Contains("Narrow the selection", schedule.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void The_ceiling_stops_the_schedule_and_reports_what_it_dropped()
        {
            var many = Enumerable.Range(0, 40).Select(i => Box("E" + i, i, 0, 0, 1, 1, 1)).ToList();

            var recipe = new DimensionRecipe { Ceiling = 10, Heights = false };
            var schedule = Dimensions.Schedule(many, recipe);

            Assert.Equal(10, schedule.Rows.Count);
            Assert.True(schedule.Dropped > 0);
            Assert.Contains("ceiling", schedule.Explain(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Every_row_reference_is_its_own()
        {
            var many = Enumerable.Range(0, 12).Select(i => Box("E" + i, i, 0, 0, 1, 2, 3)).ToList();
            var schedule = Dimensions.Schedule(many, new DimensionRecipe { Diagonals = true });

            Assert.Equal(schedule.Rows.Count,
                         schedule.Rows.Select(r => r.Reference).Distinct().Count());
        }

        // ---- what comes out ------------------------------------------------------------------

        [Fact]
        public void The_csv_has_a_header_a_row_each_and_says_where_each_number_came_from()
        {
            var schedule = Dimensions.Schedule(
                new[] { Box("Beam", 0, 0, 0, 6, 0.3, 0.5) },
                new DimensionRecipe { Heights = false });

            var lines = Dimensions.Csv(schedule)
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            Assert.StartsWith("Ref,Measurement,Element,Against,Along,Metres,Source", lines[0]);
            Assert.Equal(schedule.Rows.Count + 1, lines.Length);
            Assert.All(lines.Skip(1), l => Assert.EndsWith(",box", l));
        }

        [Fact]
        public void A_name_with_a_comma_in_it_does_not_shift_every_column_after_it()
        {
            var schedule = Dimensions.Schedule(
                new[] { Box("Beam, 305 UB", 0, 0, 0, 6, 0.3, 0.5) },
                new DimensionRecipe { Heights = false });

            var line = Dimensions.Csv(schedule)
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[1];

            Assert.Contains("\"Beam, 305 UB\"", line);
            Assert.Equal(7, Split(line).Count);
        }

        [Fact]
        public void The_report_carries_the_table_and_the_cover_facts()
        {
            var schedule = Dimensions.Schedule(
                new[] { Box("Beam", 0, 0, 0, 6, 0.3, 0.5) },
                new DimensionRecipe { Heights = false });

            var document = Dimensions.Report(schedule, Measure.Millimetres);
            var table = document.Blocks.OfType<TableBlock>().Single();

            Assert.Equal("Dimension schedule", document.Title);
            Assert.Equal(schedule.Rows.Count, table.Rows.Count);
            Assert.Contains(document.CoverFacts, f => f.Key == "Measured off the world box");
            Assert.Contains("mm", table.Headers);
        }

        [Fact]
        public void The_report_of_a_refusal_is_the_refusal_not_an_empty_table()
        {
            var schedule = Dimensions.Schedule(Array.Empty<FieldElement>(), new DimensionRecipe());
            var document = Dimensions.Report(schedule);

            Assert.Empty(document.Blocks.OfType<TableBlock>());
            Assert.Contains(document.Blocks.OfType<ParagraphBlock>(),
                            p => p.Text.Contains("nothing selected", StringComparison.OrdinalIgnoreCase));
        }

        // ---- the recipe ----------------------------------------------------------------------

        [Fact]
        public void A_recipe_survives_being_written_down_and_read_back()
        {
            var recipe = new DimensionRecipe("Riser")
            {
                Prefix = "RS",
                Sizes = false,
                Diagonals = true,
                Heights = true,
                Neighbours = true,
                Spacing = true,
                SpacingAxis = Along.Level,
                Decimals = 4,
                Ceiling = 99,
            };

            var back = DimensionRecipe.FromJson(recipe.ToJson());

            Assert.Equal("Riser", back.Name);
            Assert.Equal("RS", back.Prefix);
            Assert.False(back.Sizes);
            Assert.True(back.Diagonals);
            Assert.True(back.Neighbours);
            Assert.True(back.Spacing);
            Assert.Equal(Along.Level, back.SpacingAxis);
            Assert.Equal(4, back.Decimals);
            Assert.Equal(99, back.Ceiling);
        }

        [Fact]
        public void A_recipe_with_every_switch_off_comes_back_measuring_something()
        {
            var recipe = new DimensionRecipe
            {
                Sizes = false, Heights = false, Neighbours = false, Spacing = false,
            };

            var back = DimensionRecipe.FromJson(recipe.ToJson());

            Assert.False(back.IsEmpty);
            Assert.True(back.Sizes);
        }

        [Fact]
        public void An_unreadable_recipe_comes_back_usable()
        {
            var back = DimensionRecipe.FromJson(null);

            Assert.True(back.Sizes);
            Assert.Equal("D", back.Prefix);
            Assert.Equal(DimensionRecipe.DefaultCeiling, back.Ceiling);
        }

        [Fact]
        public void A_copy_is_its_own()
        {
            var recipe = new DimensionRecipe("Riser") { Neighbours = true };
            var copy = recipe.Copy();

            copy.Neighbours = false;
            copy.Name = "Something else";

            Assert.True(recipe.Neighbours);
            Assert.Equal("Riser", recipe.Name);
        }

        [Fact]
        public void Asking_for_nothing_is_refused_before_anything_is_measured()
        {
            var recipe = new DimensionRecipe
            {
                Sizes = false, Heights = false, Neighbours = false, Spacing = false,
            };

            var schedule = Dimensions.Schedule(new[] { Box("Beam", 0, 0, 0, 6, 1, 1) }, recipe);

            Assert.False(schedule.IsUsable);
            Assert.Contains("No measurement has been asked for", schedule.Explain(), StringComparison.Ordinal);
        }

        private static List<string> Split(string line)
        {
            var cells = new List<string>();
            var cell = new System.Text.StringBuilder();
            var quoted = false;

            for (var at = 0; at < line.Length; at++)
            {
                var c = line[at];

                if (quoted)
                {
                    if (c != '"') { cell.Append(c); continue; }

                    if (at + 1 < line.Length && line[at + 1] == '"') { cell.Append('"'); at++; continue; }

                    quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }

            cells.Add(cell.ToString());

            return cells;
        }
    }
}
