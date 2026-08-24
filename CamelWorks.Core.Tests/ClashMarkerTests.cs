using System;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashMarkerTests
    {
        private static ClashItem At(string name, double x, double y = 0, double z = 0,
                                    string test = "L03 MEP v STR",
                                    FindingStatus status = FindingStatus.New)
        {
            var item = ClashFixture.Item(name, x: x, y: y, z: z, test: test);
            item.Status = status;
            item.X = x;
            item.Y = y;
            item.Z = z;
            return item;
        }

        private static double[] Vertices(string obj, char letter = 'v') =>
            obj.Split('\n')
               .Where(l => l.StartsWith(letter + " ", StringComparison.Ordinal))
               .SelectMany(l => l.Split(' ').Skip(1))
               .Select(n => double.Parse(n, CultureInfo.InvariantCulture))
               .ToArray();

        // -----------------------------------------------------------------------------------
        // The conversion this file exists for
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_millimetre_model_puts_its_markers_where_the_building_is()
        {
            // The host reports the point in the document's units; the OBJ reader takes vertices as
            // metres. Unconverted, a point at 12,000 mm lands twelve kilometres away and what
            // somebody sees is an empty model rather than an error.
            var mesh = ClashMarkers.Write(new[] { At("a", 12_000) }, new MarkerRecipe
            {
                MetresPerUnit = 0.001,
                RadiusMetres = 0.1,
                Shape = MarkerShape.Diamond,
            });

            Assert.Null(mesh.Refusal);

            var xs = Vertices(mesh.Text).Where((_, i) => i % 3 == 0).ToList();

            // Twelve metres, plus or minus the marker's own radius.
            Assert.Equal(12.1, xs.Max(), 6);
            Assert.Equal(11.9, xs.Min(), 6);
        }

        [Fact]
        public void A_metre_model_needs_no_conversion_and_gets_none()
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 12) },
                                          new MarkerRecipe { RadiusMetres = 0.1, Shape = MarkerShape.Diamond });

            var xs = Vertices(mesh.Text).Where((_, i) => i % 3 == 0).ToList();

            Assert.Equal(12.1, xs.Max(), 6);
        }

        [Fact]
        public void Units_nobody_knows_are_refused_rather_than_guessed_at()
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 1) }, new MarkerRecipe { MetresPerUnit = 0 });

            Assert.Contains("units are not known", mesh.Refusal!, StringComparison.Ordinal);
            Assert.False(mesh.IsUsable);
            Assert.Equal(string.Empty, mesh.Text);
        }

        // -----------------------------------------------------------------------------------
        // The shapes
        // -----------------------------------------------------------------------------------

        [Theory]
        [InlineData(0, 12, 20)]
        [InlineData(1, 42, 80)]
        [InlineData(2, 162, 320)]
        [InlineData(3, 642, 1280)]
        public void A_sphere_subdivides_into_the_counts_an_icosphere_has(int detail, int vertices, int faces)
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 0) },
                                          new MarkerRecipe { Detail = detail });

            Assert.Equal(faces, mesh.Triangles);
            Assert.Equal(vertices * 3, Vertices(mesh.Text).Length);
        }

        [Fact]
        public void Every_vertex_of_a_sphere_is_the_radius_from_the_centre()
        {
            // Which is what says the subdivision pushed the new vertices back onto the sphere
            // rather than leaving them on the flat of the triangle they came from.
            var mesh = ClashMarkers.Write(new[] { At("a", 5, 6, 7) },
                                          new MarkerRecipe { Detail = 2, RadiusMetres = 0.25 });

            var numbers = Vertices(mesh.Text);

            for (var i = 0; i < numbers.Length; i += 3)
            {
                var dx = numbers[i] - 5;
                var dy = numbers[i + 1] - 6;
                var dz = numbers[i + 2] - 7;

                Assert.Equal(0.25, Math.Sqrt(dx * dx + dy * dy + dz * dz), 5);
            }
        }

        [Fact]
        public void A_sphere_finer_than_the_ceiling_is_made_at_the_ceiling_and_said()
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 0) }, new MarkerRecipe { Detail = 9 });

            Assert.Equal(1280, mesh.Triangles);
            Assert.Contains("more triangles than a marker is worth",
                            Assert.Single(mesh.Warnings), StringComparison.Ordinal);
        }

        [Fact]
        public void The_other_two_shapes_are_what_they_say()
        {
            Assert.Equal(12, ClashMarkers.Write(new[] { At("a", 0) },
                new MarkerRecipe { Shape = MarkerShape.Cube }).Triangles);

            Assert.Equal(8, ClashMarkers.Write(new[] { At("a", 0) },
                new MarkerRecipe { Shape = MarkerShape.Diamond }).Triangles);
        }

        // -----------------------------------------------------------------------------------
        // The file
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_group_per_test_and_an_object_per_clash_carrying_its_status()
        {
            var mesh = ClashMarkers.Write(new[]
            {
                At("a", 0, status: FindingStatus.New),
                At("b", 4, status: FindingStatus.Resolved),
                At("c", 8, test: "L04 MEP v STR"),
            }, new MarkerRecipe { Shape = MarkerShape.Diamond });

            Assert.Equal(3, mesh.Markers);
            Assert.Equal(24, mesh.Triangles);

            var lines = mesh.Text.Split('\n');

            // A name is whitespace-delimited in this format, so a test called "L03 MEP v STR"
            // would arrive as an object called "L03" and three pieces of nonsense after it.
            Assert.Contains("g L03_MEP_v_STR", lines);
            Assert.Contains("g L04_MEP_v_STR", lines);

            Assert.Contains("o Clash1_New", lines);
            Assert.Contains("o Clash2_Resolved", lines);

            // Numbering restarts per test, so the tree reads as a list rather than a running total.
            Assert.Equal(2, lines.Count(l => l == "o Clash1_New"));
        }

        [Fact]
        public void Face_indices_run_across_the_whole_file_because_that_is_what_OBJ_says()
        {
            // Each marker's own vertices are 1..n, and the second marker's faces have to point at
            // n+1..2n. Restarting at one is the mistake that makes every marker after the first
            // collapse onto the first.
            var mesh = ClashMarkers.Write(new[] { At("a", 0), At("b", 4) },
                                          new MarkerRecipe { Shape = MarkerShape.Diamond });

            var faces = mesh.Text.Split('\n')
                            .Where(l => l.StartsWith("f ", StringComparison.Ordinal))
                            .SelectMany(l => l.Split(' ').Skip(1))
                            .Select(int.Parse)
                            .ToList();

            Assert.Equal(1, faces.Min());
            Assert.Equal(12, faces.Max());
            Assert.Equal(16 * 3, faces.Count);
        }

        [Fact]
        public void The_colours_are_the_board_s_own_and_only_the_ones_used_are_written()
        {
            var mesh = ClashMarkers.Write(new[]
            {
                At("a", 0, status: FindingStatus.New),
                At("b", 4, status: FindingStatus.Resolved),
            });

            Assert.Contains("mtllib clashes.mtl", mesh.Text, StringComparison.Ordinal);
            Assert.Contains("usemtl New", mesh.Text, StringComparison.Ordinal);
            Assert.Contains("usemtl Resolved", mesh.Text, StringComparison.Ordinal);

            Assert.Contains("newmtl New", mesh.Materials, StringComparison.Ordinal);
            Assert.Contains("newmtl Resolved", mesh.Materials, StringComparison.Ordinal);
            Assert.DoesNotContain("newmtl Active", mesh.Materials, StringComparison.Ordinal);

            // Red for new, green for done: what every clash tool has used for twenty years.
            Assert.Equal(0xC8, ClashMarkers.Colour(FindingStatus.New).R);
            Assert.Equal(0x8B, ClashMarkers.Colour(FindingStatus.Resolved).G);
        }

        [Fact]
        public void Turning_the_colours_off_leaves_no_materials_at_all()
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 0) },
                                          new MarkerRecipe { ColourByStatus = false });

            Assert.DoesNotContain("usemtl", mesh.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, mesh.Materials);
        }

        [Fact]
        public void The_file_says_its_vertices_are_metres_so_the_next_reader_knows()
        {
            Assert.Contains("Vertices are metres", ClashMarkers.Write(new[] { At("a", 0) }).Text,
                            StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------------------
        // Refusing
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Too_many_markers_is_a_refusal_that_says_how_many()
        {
            var many = Enumerable.Range(1, 30).Select(n => At("c" + n, n)).ToList();

            var mesh = ClashMarkers.Write(many, new MarkerRecipe { Ceiling = 10 });

            Assert.Contains("30 markers", mesh.Refusal!, StringComparison.Ordinal);
            Assert.Contains("ceiling is 10", mesh.Refusal!, StringComparison.Ordinal);
            Assert.Equal(0, mesh.Markers);
            Assert.Equal(mesh.Refusal, mesh.Explain());
        }

        [Fact]
        public void Nothing_to_mark_and_no_size_to_mark_it_with_are_both_said_plainly()
        {
            Assert.Contains("no clashes to mark", ClashMarkers.Write(null).Refusal!,
                            StringComparison.Ordinal);

            Assert.Contains("no clashes to mark",
                            ClashMarkers.Write(Array.Empty<ClashItem>()).Refusal!,
                            StringComparison.Ordinal);

            Assert.Contains("marker of no size",
                            ClashMarkers.Write(new[] { At("a", 0) },
                                               new MarkerRecipe { RadiusMetres = 0 }).Refusal!,
                            StringComparison.Ordinal);
        }

        [Fact]
        public void The_readout_counts_what_went_in()
        {
            var mesh = ClashMarkers.Write(new[] { At("a", 0), At("b", 4) },
                                          new MarkerRecipe { Shape = MarkerShape.Diamond });

            Assert.Equal("2 markers, 16 triangles.", mesh.Explain());
            Assert.True(mesh.IsUsable);
        }
    }
}
