using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// DXF is a list of (group code, value) pairs and nothing else, so the tests read the file back
    /// the way a CAD package would rather than looking for substrings. Asserting on text would pass
    /// on a file with the pairs in the wrong order, which is precisely the way this format goes
    /// wrong.
    /// </summary>
    public class DxfWriterTests
    {
        private sealed class Pair
        {
            internal Pair(int code, string value) { Code = code; Value = value; }

            internal int Code { get; }

            internal string Value { get; }

            public override string ToString() => Code + ": " + Value;
        }

        private static IReadOnlyList<Pair> Read(string text)
        {
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var pairs = new List<Pair>();

            for (var i = 0; i + 1 < lines.Length; i += 2)
            {
                var code = lines[i].Trim();

                if (code.Length == 0) break;

                Assert.True(int.TryParse(code, out var number), "not a group code: \"" + lines[i] + "\"");

                pairs.Add(new Pair(number, lines[i + 1]));
            }

            return pairs;
        }

        /// <summary>The values of one code, in order, between one marker and the next.</summary>
        private static IReadOnlyList<string> Inside(IReadOnlyList<Pair> pairs, string section, int code)
        {
            var found = new List<string>();
            var within = false;

            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].Code == 2 && pairs[i].Value == section
                    && i > 0 && pairs[i - 1].Value == "SECTION")
                {
                    within = true;
                    continue;
                }

                if (within && pairs[i].Code == 0 && pairs[i].Value == "ENDSEC") break;
                if (within && pairs[i].Code == code) found.Add(pairs[i].Value);
            }

            return found;
        }

        private static DxfSolid Cube(string name, string layer = "")
        {
            // A unit tetrahedron is enough to be a real solid with shared vertices.
            var positions = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            var indices = new[] { 0, 1, 2, 0, 1, 3, 0, 2, 3, 1, 2, 3 };

            return new DxfSolid(name, positions, indices) { Layer = layer };
        }

        // ---- The shape of the file -------------------------------------------------------

        [Fact]
        public void What_comes_out_is_a_run_of_group_code_pairs_ending_in_the_marker_that_says_so()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            Assert.Equal(0, pairs[0].Code);
            Assert.Equal("SECTION", pairs[0].Value);

            Assert.Equal(0, pairs[pairs.Count - 1].Code);
            Assert.Equal("EOF", pairs[pairs.Count - 1].Value);
        }

        [Fact]
        public void Every_section_that_is_opened_is_closed()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            var opened = pairs.Count(p => p.Code == 0 && p.Value == "SECTION");
            var closed = pairs.Count(p => p.Code == 0 && p.Value == "ENDSEC");

            Assert.Equal(3, opened);
            Assert.Equal(opened, closed);

            Assert.Equal(new[] { "HEADER", "TABLES", "ENTITIES" },
                pairs.Where((p, i) => p.Code == 2 && i > 0 && pairs[i - 1].Value == "SECTION")
                     .Select(p => p.Value));
        }

        [Fact]
        public void The_header_says_which_dialect_it_is_and_that_it_is_in_metres()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            var version = pairs
                .Where((p, i) => p.Code == 1 && i > 0 && pairs[i - 1].Value == "$ACADVER")
                .Select(p => p.Value)
                .Single();

            Assert.Equal(DxfWriter.Version, version);

            var units = pairs
                .Where((p, i) => p.Code == 70 && i > 0 && pairs[i - 1].Value == "$INSUNITS")
                .Select(p => p.Value.Trim())
                .Single();

            Assert.Equal("6", units);
        }

        [Fact]
        public void Every_table_that_is_opened_is_closed()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            Assert.Equal(pairs.Count(p => p.Code == 0 && p.Value == "TABLE"),
                         pairs.Count(p => p.Code == 0 && p.Value == "ENDTAB"));
        }

        // ---- Layers ----------------------------------------------------------------------

        [Fact]
        public void Every_layer_used_is_declared_before_it_is_used()
        {
            var result = DxfWriter.Write(new[]
            {
                Cube("Duct", "Mechanical"),
                Cube("Pipe", "Plumbing"),
                Cube("Duct 2", "Mechanical"),
            });

            var pairs = Read(result.Text);

            var declared = Inside(pairs, "TABLES", 2)
                .Where(v => v != "APPID" && v != "LAYER" && v != DxfWriter.DefaultApplication)
                .ToList();

            Assert.Contains("Mechanical", declared, StringComparer.Ordinal);
            Assert.Contains("Plumbing", declared, StringComparer.Ordinal);

            var used = Inside(pairs, "ENTITIES", 8).Distinct().ToList();

            Assert.All(used, layer => Assert.Contains(layer, declared, StringComparer.Ordinal));

            Assert.Equal(new[] { "Mechanical", "Plumbing" }, result.Layers);
        }

        [Fact]
        public void The_declared_layer_count_is_the_number_of_layers_declared()
        {
            var pairs = Read(DxfWriter.Write(new[]
            {
                Cube("A", "One"), Cube("B", "Two"), Cube("C", "Three"),
            }).Text);

            var table = pairs.ToList().FindIndex(p => p.Code == 2 && p.Value == "LAYER");

            Assert.True(table > 0);
            Assert.Equal(70, pairs[table + 1].Code);
            Assert.Equal("3", pairs[table + 1].Value.Trim());
        }

        [Fact]
        public void Geometry_with_nothing_to_say_about_its_layer_still_lands_on_one()
        {
            var result = DxfWriter.Write(new[] { Cube("Wall") });

            Assert.Equal(new[] { DxfWriter.DefaultLayer }, result.Layers);
            Assert.All(Inside(Read(result.Text), "ENTITIES", 8),
                       layer => Assert.Equal(DxfWriter.DefaultLayer, layer));
        }

        [Fact]
        public void A_layer_name_the_format_refuses_is_made_into_one_it_accepts()
        {
            Assert.Equal("Level_03_Mech", DxfWriter.Layer("Level 03/Mech"));
            Assert.Equal("Duct_Supply", DxfWriter.Layer("Duct,Supply"));
            Assert.Equal(DxfWriter.DefaultLayer, DxfWriter.Layer("   "));
            Assert.Equal(DxfWriter.DefaultLayer, DxfWriter.Layer(null));
            Assert.Equal(DxfWriter.DefaultLayer, DxfWriter.Layer("///"));
        }

        [Fact]
        public void A_drawing_of_nothing_still_declares_a_layer_so_it_opens()
        {
            var result = DxfWriter.Write(Array.Empty<DxfSolid>());

            Assert.False(result.IsUsable);
            Assert.Equal(new[] { DxfWriter.DefaultLayer }, result.Layers);
            Assert.Equal("EOF", Read(result.Text).Last().Value);
            Assert.Contains("Nothing could be drawn", result.Explain(), StringComparison.Ordinal);
        }

        // ---- The mesh --------------------------------------------------------------------

        [Fact]
        public void An_element_is_one_mesh_rather_than_a_face_each()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            Assert.Single(pairs.Where(p => p.Code == 0 && p.Value == "POLYLINE"));
            Assert.Single(pairs.Where(p => p.Code == 0 && p.Value == "SEQEND"));
            Assert.DoesNotContain(pairs, p => p.Code == 0 && p.Value == "3DFACE");
        }

        [Fact]
        public void The_mesh_header_counts_the_vertices_and_faces_that_follow_it()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text).ToList();

            var head = pairs.FindIndex(p => p.Code == 0 && p.Value == "POLYLINE");

            Assert.True(head >= 0);

            var flag = Take(pairs, head, 70);
            var vertices = Take(pairs, head, 71);
            var faces = Take(pairs, head, 72);

            // 64 is what says "this is a polyface mesh" rather than a polyline through the points.
            Assert.Equal(64, flag);
            Assert.Equal(4, vertices);
            Assert.Equal(4, faces);

            var written = pairs.Skip(head)
                               .TakeWhile(p => !(p.Code == 0 && p.Value == "SEQEND"))
                               .Count(p => p.Code == 0 && p.Value == "VERTEX");

            Assert.Equal(vertices + faces, written);
        }

        [Fact]
        public void Every_face_names_corners_that_exist_counting_from_one()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text).ToList();

            var head = pairs.FindIndex(p => p.Code == 0 && p.Value == "POLYLINE");
            var vertices = Take(pairs, head, 71);

            var corners = pairs.Where(p => p.Code == 71 || p.Code == 72 || p.Code == 73)
                               .Skip(2)   // the two counts on the mesh header
                               .Select(p => int.Parse(p.Value.Trim(), System.Globalization.CultureInfo.InvariantCulture))
                               .ToList();

            Assert.Equal(4 * 3, corners.Count);
            Assert.All(corners, c => Assert.InRange(c, 1, vertices));
        }

        [Fact]
        public void A_vertex_carrying_a_point_and_one_naming_a_face_are_flagged_apart()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text).ToList();

            var flags = new List<int>();

            for (var i = 0; i < pairs.Count; i++)
            {
                if (!(pairs[i].Code == 0 && pairs[i].Value == "VERTEX")) continue;

                flags.Add(Take(pairs, i, 70));
            }

            // 192 = a mesh vertex carrying a point; 128 = a mesh vertex naming three of them.
            Assert.Equal(4, flags.Count(f => f == 192));
            Assert.Equal(4, flags.Count(f => f == 128));
        }

        [Fact]
        public void A_shared_corner_is_written_once_and_named_four_times()
        {
            // The tetrahedron's first vertex is in three of its four faces. A face-per-triangle
            // writer would put twelve points in the file; a mesh puts four.
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text).ToList();

            var head = pairs.FindIndex(p => p.Code == 0 && p.Value == "POLYLINE");

            Assert.Equal(4, Take(pairs, head, 71));
        }

        [Fact]
        public void An_element_too_big_for_one_mesh_is_split_and_every_triangle_still_arrives()
        {
            var count = DxfWriter.MaxMeshSize + 500;
            var positions = new List<float>();
            var indices = new List<int>();

            for (var i = 0; i < count; i++)
            {
                positions.AddRange(new float[] { i, 0, 0, i, 1, 0, i, 0, 1 });
                indices.AddRange(new[] { i * 3, i * 3 + 1, i * 3 + 2 });
            }

            var result = DxfWriter.Write(new[] { new DxfSolid("Long", positions, indices) });
            var pairs = Read(result.Text);

            var meshes = pairs.Count(p => p.Code == 0 && p.Value == "POLYLINE");

            Assert.Equal(count, result.Faces);
            Assert.Equal(1, result.Solids);
            Assert.True(meshes > 1, "it should have been split");
            Assert.Equal(meshes, pairs.Count(p => p.Code == 0 && p.Value == "SEQEND"));

            // Whatever the split, every face in the file names corners its own mesh declared.
            var declared = 0;
            var counted = 0;

            for (var i = 0; i < pairs.Count; i++)
            {
                if (!(pairs[i].Code == 0 && pairs[i].Value == "POLYLINE")) continue;

                declared += Take(pairs, i, 71);
                counted += Take(pairs, i, 72);

                Assert.True(Take(pairs, i, 71) <= DxfWriter.MaxMeshSize);
                Assert.True(Take(pairs, i, 72) <= DxfWriter.MaxMeshSize);
            }

            Assert.Equal(count, counted);
            Assert.Equal(count * 3, declared);
        }

        [Fact]
        public void Geometry_that_is_not_whole_triangles_is_counted_rather_than_written()
        {
            var result = DxfWriter.Write(new[]
            {
                Cube("Good"),
                new DxfSolid("Short", new float[] { 0, 0, 0 }, new[] { 0, 1, 2 }),
                new DxfSolid("Ragged", new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new[] { 0, 1 }),
                new DxfSolid("Beyond", new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new[] { 0, 1, 9 }),
            });

            Assert.Equal(1, result.Solids);
            Assert.Equal(3, result.Skipped);
            Assert.Contains("could not be read as triangles", result.Explain(), StringComparison.Ordinal);
        }

        // ---- Colour ----------------------------------------------------------------------

        [Fact]
        public void A_colour_becomes_the_nearest_one_the_format_can_name()
        {
            Assert.Equal(1, DxfWriter.Index(0.95, 0.05, 0.02));
            Assert.Equal(3, DxfWriter.Index(0.1, 0.9, 0.1));
            Assert.Equal(5, DxfWriter.Index(0, 0, 1));
            Assert.Equal(9, DxfWriter.Index(0.75, 0.75, 0.75));

            Assert.Equal("red", DxfWriter.Named(1));
            Assert.Equal("light grey", DxfWriter.Named(9));
            Assert.Equal("173", DxfWriter.Named(173));
        }

        [Fact]
        public void A_colour_outside_the_range_is_brought_into_it_rather_than_throwing()
        {
            Assert.Equal(1, DxfWriter.Index(4, -2, -9));
            Assert.Equal(7, DxfWriter.Index(2, 2, 2));
        }

        [Fact]
        public void The_colour_on_the_mesh_is_the_one_the_element_asked_for()
        {
            var solid = Cube("Duct", "Mechanical");
            solid.Red = 0; solid.Green = 1; solid.Blue = 1;

            var pairs = Read(DxfWriter.Write(new[] { solid }).Text).ToList();
            var head = pairs.FindIndex(p => p.Code == 0 && p.Value == "POLYLINE");

            Assert.Equal(4, Take(pairs, head, 62));
        }

        // ---- The data that makes it worth having -----------------------------------------

        [Fact]
        public void Properties_travel_with_the_element_under_a_declared_application()
        {
            var solid = Cube("Door 01", "Doors");

            solid.Data.Add(new KeyValuePair<string, string>("Mark", "D-01"));
            solid.Data.Add(new KeyValuePair<string, string>("Fire rating", "FD60"));

            var pairs = Read(DxfWriter.Write(new[] { solid }, "Riverside").Text).ToList();

            // Declared in the table, or a reader drops the data without saying anything.
            Assert.Contains("RIVERSIDE", Inside(pairs, "TABLES", 2), StringComparer.Ordinal);

            var owner = pairs.FindIndex(p => p.Code == 1001);

            Assert.True(owner > 0);
            Assert.Equal("RIVERSIDE", pairs[owner].Value);

            Assert.Equal(new[] { "Mark=D-01", "Fire rating=FD60" },
                pairs.Where(p => p.Code == 1000).Select(p => p.Value));
        }

        [Fact]
        public void The_data_is_written_once_per_element_and_not_once_per_triangle()
        {
            var solid = Cube("Door 01");
            solid.Data.Add(new KeyValuePair<string, string>("Mark", "D-01"));

            var pairs = Read(DxfWriter.Write(new[] { solid }).Text);

            Assert.Equal(4, solid.Triangles);
            Assert.Single(pairs.Where(p => p.Code == 1001));
            Assert.Single(pairs.Where(p => p.Code == 1000));
        }

        [Fact]
        public void The_data_sits_on_the_mesh_and_before_its_vertices()
        {
            var solid = Cube("Door 01");
            solid.Data.Add(new KeyValuePair<string, string>("Mark", "D-01"));

            var pairs = Read(DxfWriter.Write(new[] { solid }).Text).ToList();

            var head = pairs.FindIndex(p => p.Code == 0 && p.Value == "POLYLINE");
            var owner = pairs.FindIndex(p => p.Code == 1001);
            var first = pairs.FindIndex(p => p.Code == 0 && p.Value == "VERTEX");

            Assert.True(head < owner);
            Assert.True(owner < first);
        }

        [Fact]
        public void A_value_longer_than_the_format_allows_is_cut_and_counted_rather_than_refused()
        {
            var solid = Cube("Door 01");

            solid.Data.Add(new KeyValuePair<string, string>("Note", new string('x', 400)));

            var result = DxfWriter.Write(new[] { solid });

            Assert.Equal(1, result.Truncated);
            Assert.Contains("cut to the 255 characters", result.Explain(), StringComparison.Ordinal);

            var value = Read(result.Text).Single(p => p.Code == 1000).Value;

            Assert.Equal(DxfWriter.MaxDataLength, value.Length);
        }

        [Fact]
        public void A_value_with_a_line_break_in_it_does_not_break_the_file()
        {
            var solid = Cube("Door 01");

            solid.Data.Add(new KeyValuePair<string, string>("Note", "first\r\nsecond"));

            var pairs = Read(DxfWriter.Write(new[] { solid }).Text);

            Assert.Equal("Note=first  second", pairs.Single(p => p.Code == 1000).Value);
            Assert.Equal("EOF", pairs.Last().Value);
        }

        [Fact]
        public void A_property_with_no_name_is_passed_over_rather_than_written_as_an_equals_sign()
        {
            var solid = Cube("Door 01");

            solid.Data.Add(new KeyValuePair<string, string>("  ", "orphan"));
            solid.Data.Add(new KeyValuePair<string, string>("Mark", "D-01"));

            Assert.Equal(new[] { "Mark=D-01" },
                Read(DxfWriter.Write(new[] { solid }).Text).Where(p => p.Code == 1000)
                                                          .Select(p => p.Value));
        }

        [Fact]
        public void An_element_with_no_properties_carries_no_data_block_at_all()
        {
            var pairs = Read(DxfWriter.Write(new[] { Cube("Wall") }).Text);

            Assert.DoesNotContain(pairs, p => p.Code == 1001);
        }

        [Fact]
        public void An_application_name_the_format_refuses_is_made_into_one_it_accepts()
        {
            var solid = Cube("Wall");
            solid.Data.Add(new KeyValuePair<string, string>("Mark", "D-01"));

            var pairs = Read(DxfWriter.Write(new[] { solid }, "Riverside Phase 2").Text);

            Assert.Equal("RIVERSIDE_PHASE_2", pairs.First(p => p.Code == 1001).Value);
        }

        // ---- Coming in from the other writer ---------------------------------------------

        [Fact]
        public void A_mesh_already_read_for_the_three_d_export_can_be_drawn_without_reading_it_again()
        {
            var mesh = new GltfMesh("Duct", new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 }, new[] { 0, 1, 2 })
            {
                Red = 0, Green = 0, Blue = 1,
            };

            var solid = DxfSolid.From(mesh);

            Assert.NotNull(solid);
            Assert.Equal("Duct", solid!.Name);
            Assert.Equal(1, solid.Triangles);
            Assert.True(solid.IsUsable);
            Assert.Equal(5, DxfWriter.Index(solid.Red, solid.Green, solid.Blue));

            Assert.Null(DxfSolid.From(null));
        }

        private static int Take(IReadOnlyList<Pair> pairs, int from, int code)
        {
            for (var i = from + 1; i < pairs.Count; i++)
            {
                if (pairs[i].Code == 0 && i > from + 1) break;
                if (pairs[i].Code != code) continue;

                return int.Parse(pairs[i].Value.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }

            throw new InvalidOperationException(
                "no group code " + code + " on the entity at " + from);
        }
    }
}
