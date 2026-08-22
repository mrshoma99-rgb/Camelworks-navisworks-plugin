using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class GltfWriterTests
    {
        /// <summary>A unit tetrahedron: four vertices, four faces, real geometry.</summary>
        private static GltfMesh Tetrahedron(string name = "thing")
        {
            var positions = new float[]
            {
                0, 0, 0,
                1, 0, 0,
                0, 1, 0,
                0, 0, 1,
            };

            var indices = new[] { 0, 1, 2, 0, 1, 3, 0, 2, 3, 1, 2, 3 };

            return new GltfMesh(name, positions, indices);
        }

        private static uint Word(byte[] bytes, int at) =>
            (uint)(bytes[at] | (bytes[at + 1] << 8) | (bytes[at + 2] << 16) | (bytes[at + 3] << 24));

        /// <summary>Pull the JSON chunk back out of a GLB and parse it.</summary>
        private static JsonValue Json(byte[] glb)
        {
            var length = (int)Word(glb, 12);
            var text = Encoding.UTF8.GetString(glb, 20, length).TrimEnd(' ');

            Assert.True(JsonReader.TryParse(text, out var json), "the JSON chunk did not parse: " + text);
            return json;
        }

        [Fact]
        public void The_header_is_the_one_glb_specifies()
        {
            var glb = GltfWriter.Write(new[] { Tetrahedron() }).Bytes;

            Assert.Equal(GltfWriter.Magic, Word(glb, 0));
            Assert.Equal(0x46546C67u, Word(glb, 0));
            Assert.Equal(2u, Word(glb, 4));

            // The declared total is the actual total. A viewer reads this and trusts it.
            Assert.Equal((uint)glb.Length, Word(glb, 8));

            Assert.Equal(GltfWriter.JsonChunk, Word(glb, 16));
        }

        [Fact]
        public void Every_chunk_is_four_byte_aligned_and_padded_with_the_right_byte()
        {
            // A name of an awkward length, so the JSON almost certainly needs padding.
            var glb = GltfWriter.Write(new[] { Tetrahedron("abc") }).Bytes;

            Assert.Equal(0, glb.Length % 4);

            var jsonLength = (int)Word(glb, 12);
            Assert.Equal(0, jsonLength % 4);

            // JSON pads with spaces, never with nulls: a reader handed trailing nulls inside the
            // declared length is at the mercy of its parser.
            for (var i = 20; i < 20 + jsonLength; i++)
                Assert.False(glb[i] == 0, "there is a null byte inside the JSON chunk, at " + i);


            var binaryAt = 20 + jsonLength;
            Assert.Equal(0, binaryAt % 4);

            var binaryLength = (int)Word(glb, binaryAt);
            Assert.Equal(0, binaryLength % 4);
            Assert.Equal(GltfWriter.BinaryChunk, Word(glb, binaryAt + 4));

            Assert.Equal(glb.Length, binaryAt + 8 + binaryLength);
        }

        [Fact]
        public void The_scene_says_it_is_gltf_two_and_who_made_it()
        {
            var json = Json(GltfWriter.Write(new[] { Tetrahedron() }).Bytes);

            Assert.Equal("2.0", json["asset"]["version"].AsString());
            Assert.Contains("CamelWorks", json["asset"]["generator"].AsString());
            Assert.Contains("bimcamel.com", json["asset"]["generator"].AsString());

            Assert.Equal(0, (int)json["scene"].AsDouble());
            Assert.Single(json["scenes"].Items);
        }

        [Fact]
        public void A_root_node_turns_a_z_up_model_the_right_way_up()
        {
            var json = Json(GltfWriter.Write(new[] { Tetrahedron() }, "Riverside").Bytes);

            var root = json["nodes"].Items[0];

            Assert.Equal("Riverside", root["name"].AsString());

            var rotation = root["rotation"].Items.Select(v => v.AsDouble()).ToList();

            // -90 degrees about X, as (x, y, z, w). Without it the model opens on its side and
            // nobody reports it as a bug, they decide the export is broken.
            Assert.Equal(4, rotation.Count);
            Assert.Equal(-0.70710678, rotation[0], 6);
            Assert.Equal(0.0, rotation[1], 6);
            Assert.Equal(0.0, rotation[2], 6);
            Assert.Equal(0.70710678, rotation[3], 6);

            // Every element hangs off the root, so the rotation reaches all of them.
            Assert.Single(root["children"].Items);
            Assert.Equal(1, (int)root["children"].Items[0].AsDouble());
        }

        [Fact]
        public void Each_element_becomes_a_named_node_with_a_mesh()
        {
            var json = Json(GltfWriter.Write(new[]
            {
                Tetrahedron("Wall 41"),
                Tetrahedron("Duct 12"),
            }).Bytes);

            Assert.Equal(3, json["nodes"].Items.Count);
            Assert.Equal("Wall 41", json["nodes"].Items[1]["name"].AsString());
            Assert.Equal("Duct 12", json["nodes"].Items[2]["name"].AsString());

            Assert.Equal(2, json["meshes"].Items.Count);
            Assert.Equal(new[] { 0, 1 }, json["nodes"].Items.Skip(1).Select(n => (int)n["mesh"].AsDouble()));

            // The names go in the viewer's tree. An export of 400 objects called "mesh_0" is one
            // nobody can navigate.
            Assert.Equal("Wall 41", json["meshes"].Items[0]["name"].AsString());
        }

        [Fact]
        public void The_position_accessor_carries_the_bounds_the_spec_requires()
        {
            var json = Json(GltfWriter.Write(new[] { Tetrahedron() }).Bytes);

            var position = json["accessors"].Items[0];

            Assert.Equal(5126, (int)position["componentType"].AsDouble());
            Assert.Equal("VEC3", position["type"].AsString());
            Assert.Equal(4, (int)position["count"].AsDouble());

            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, position["min"].Items.Select(v => v.AsDouble()));
            Assert.Equal(new[] { 1.0, 1.0, 1.0 }, position["max"].Items.Select(v => v.AsDouble()));

            var indices = json["accessors"].Items[1];

            Assert.Equal(5125, (int)indices["componentType"].AsDouble());
            Assert.Equal("SCALAR", indices["type"].AsString());
            Assert.Equal(12, (int)indices["count"].AsDouble());

            // No min/max on indices. Only POSITION needs them and writing them elsewhere is noise
            // a strict validator complains about.
            Assert.Equal(JsonKind.Null, indices["min"].Kind);
        }

        [Fact]
        public void Every_buffer_view_starts_on_a_four_byte_boundary_and_names_its_target()
        {
            var glb = GltfWriter.Write(new[] { Tetrahedron("a"), Tetrahedron("bb"), Tetrahedron("ccc") }).Bytes;
            var json = Json(glb);

            var views = json["bufferViews"].Items;

            Assert.Equal(6, views.Count);

            foreach (var view in views)
            {
                // The spec requires an accessor's offset to be a multiple of its component size.
                // Both components here are four bytes, so a four-byte view boundary settles it.
                Assert.Equal(0, (int)view["byteOffset"].AsDouble() % 4);
                Assert.True((int)view["byteLength"].AsDouble() > 0);
                Assert.Equal(0, (int)view["buffer"].AsDouble());
            }

            Assert.Equal(34962, (int)views[0]["target"].AsDouble());
            Assert.Equal(34963, (int)views[1]["target"].AsDouble());

            // The declared buffer length is the binary chunk's length, less its padding.
            var jsonLength = (int)Word(glb, 12);
            var binaryLength = (int)Word(glb, 20 + jsonLength);
            var declared = (int)json["buffers"].Items[0]["byteLength"].AsDouble();

            Assert.True(declared <= binaryLength, "the buffer claims more bytes than the chunk holds");
            Assert.True(binaryLength - declared < 4, "the binary chunk is padded by more than three bytes");
        }

        [Fact]
        public void The_positions_in_the_buffer_are_the_positions_that_went_in()
        {
            var glb = GltfWriter.Write(new[] { Tetrahedron() }).Bytes;
            var json = Json(glb);

            var jsonLength = (int)Word(glb, 12);
            var binaryAt = 20 + jsonLength + 8;

            var view = json["bufferViews"].Items[0];
            var at = binaryAt + (int)view["byteOffset"].AsDouble();

            var read = new List<float>();
            for (var i = 0; i < 12; i++) read.Add(BitConverter.ToSingle(glb, at + (i * 4)));

            Assert.Equal(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 }, read);

            var indexView = json["bufferViews"].Items[1];
            var indexAt = binaryAt + (int)indexView["byteOffset"].AsDouble();

            Assert.Equal(0u, Word(glb, indexAt));
            Assert.Equal(1u, Word(glb, indexAt + 4));
            Assert.Equal(2u, Word(glb, indexAt + 8));
        }

        [Fact]
        public void Meshes_of_the_same_colour_share_one_material()
        {
            var red = Tetrahedron("a");
            red.Material = "Structure";
            red.Red = 1; red.Green = 0; red.Blue = 0;

            var alsoRed = Tetrahedron("b");
            alsoRed.Material = "Structure";
            alsoRed.Red = 1; alsoRed.Green = 0; alsoRed.Blue = 0;

            var blue = Tetrahedron("c");
            blue.Material = "Services";
            blue.Red = 0; blue.Green = 0; blue.Blue = 1;

            var json = Json(GltfWriter.Write(new[] { red, alsoRed, blue }).Bytes);

            Assert.Equal(2, json["materials"].Items.Count);
            Assert.Equal("Structure", json["materials"].Items[0]["name"].AsString());

            var used = json["meshes"].Items
                .Select(m => (int)m["primitives"].Items[0]["material"].AsDouble())
                .ToList();

            Assert.Equal(new[] { 0, 0, 1 }, used);
        }

        [Fact]
        public void Something_see_through_says_so_or_a_viewer_draws_it_solid()
        {
            var ghost = Tetrahedron();
            ghost.Alpha = 0.35;

            var json = Json(GltfWriter.Write(new[] { ghost }).Bytes);
            var material = json["materials"].Items[0];

            Assert.Equal("BLEND", material["alphaMode"].AsString());
            Assert.Equal(0.35, material["pbrMetallicRoughness"]["baseColorFactor"].Items[3].AsDouble(), 4);

            // Opaque is the default and says nothing, which is what the spec wants.
            var solid = Json(GltfWriter.Write(new[] { Tetrahedron() }).Bytes);
            Assert.Equal(JsonKind.Null, solid["materials"].Items[0]["alphaMode"].Kind);
        }

        [Fact]
        public void Double_sided_is_a_boolean_and_not_this_project_s_own_yes()
        {
            var json = Json(GltfWriter.Write(new[] { Tetrahedron() }).Bytes);

            Assert.Equal(JsonKind.Bool, json["materials"].Items[0]["doubleSided"].Kind);
        }

        [Fact]
        public void A_colour_outside_the_range_is_clamped_rather_than_written_out()
        {
            var wrong = Tetrahedron();
            wrong.Red = 4.5; wrong.Green = -2; wrong.Blue = 0.5;

            var json = Json(GltfWriter.Write(new[] { wrong }).Bytes);
            var colour = json["materials"].Items[0]["pbrMetallicRoughness"]["baseColorFactor"].Items;

            Assert.Equal(1.0, colour[0].AsDouble(), 6);
            Assert.Equal(0.0, colour[1].AsDouble(), 6);
            Assert.Equal(0.5, colour[2].AsDouble(), 6);
        }

        [Fact]
        public void An_element_with_no_triangles_is_counted_and_left_out()
        {
            var empty = new GltfMesh("annotation", Array.Empty<float>(), Array.Empty<int>());

            Assert.False(empty.IsUsable);

            var result = GltfWriter.Write(new[] { Tetrahedron(), empty });

            Assert.Equal(1, result.Meshes);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(4, result.Triangles);
            Assert.Contains("carried no triangles", string.Join(" ", result.Notes));
            Assert.Contains("1 carried no triangles", result.Explain());
        }

        [Fact]
        public void Nothing_at_all_still_writes_a_file_a_viewer_will_open()
        {
            var result = GltfWriter.Write(Array.Empty<GltfMesh>());

            Assert.Equal(GltfWriter.Magic, Word(result.Bytes, 0));
            Assert.Equal((uint)result.Bytes.Length, Word(result.Bytes, 8));

            var json = Json(result.Bytes);

            Assert.Single(json["nodes"].Items);
            Assert.Equal(JsonKind.Null, json["meshes"].Kind);

            // No BIN chunk at all rather than an empty one: a zero-length chunk is legal and some
            // readers still balk at it, and there is nothing to put in it.
            Assert.Equal(20 + (int)Word(result.Bytes, 12), result.Bytes.Length);

            Assert.Contains("no geometry", string.Join(" ", result.Notes));
        }

        [Fact]
        public void Nulls_in_the_list_do_not_take_the_export_with_them()
        {
            var meshes = new List<GltfMesh> { Tetrahedron() };
            meshes.Add(null!);

            var result = GltfWriter.Write(meshes);

            Assert.Equal(1, result.Meshes);
            Assert.Equal(1, result.Skipped);
        }

        [Fact]
        public void A_mesh_with_an_index_count_that_is_not_a_multiple_of_three_is_refused()
        {
            var broken = new GltfMesh("half a triangle",
                                      new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
                                      new[] { 0, 1, 2, 0 });

            Assert.False(broken.IsUsable);
            Assert.Equal(1, GltfWriter.Write(new[] { broken }).Skipped);
        }

        [Fact]
        public void The_readout_counts_objects_triangles_and_megabytes()
        {
            var explained = GltfWriter.Write(new[] { Tetrahedron(), Tetrahedron() }).Explain();

            Assert.Contains("2 objects", explained);
            Assert.Contains("8 triangles", explained);
            Assert.Contains("MB", explained);
        }
    }
}
