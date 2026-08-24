using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Report
{
    /// <summary>One element's triangles, in metres, ready to write.</summary>
    public sealed class GltfMesh
    {
        /// <summary>Create a mesh.</summary>
        /// <param name="name">What the node is called in the viewer's tree.</param>
        /// <param name="positions">Vertex positions, three floats each, in metres.</param>
        /// <param name="indices">Triangle indices into <paramref name="positions"/>, three per triangle.</param>
        public GltfMesh(string name, IReadOnlyList<float> positions, IReadOnlyList<int> indices)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "element" : name.Trim();
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
            Indices = indices ?? throw new ArgumentNullException(nameof(indices));
        }

        /// <summary>What the node is called.</summary>
        public string Name { get; }

        /// <summary>Vertex positions, three floats each.</summary>
        public IReadOnlyList<float> Positions { get; }

        /// <summary>Triangle indices, three per triangle.</summary>
        public IReadOnlyList<int> Indices { get; }

        /// <summary>Which material this uses. Meshes sharing a name share a material.</summary>
        public string Material { get; set; } = "default";

        /// <summary>Red, 0 to 1.</summary>
        public double Red { get; set; } = 0.75;

        /// <summary>Green, 0 to 1.</summary>
        public double Green { get; set; } = 0.75;

        /// <summary>Blue, 0 to 1.</summary>
        public double Blue { get; set; } = 0.75;

        /// <summary>Opacity, 0 to 1. Anything under 1 makes the material blend.</summary>
        public double Alpha { get; set; } = 1;

        /// <summary>How many triangles it holds.</summary>
        public int Triangles => Indices.Count / 3;

        /// <summary>How many vertices it holds.</summary>
        public int Vertices => Positions.Count / 3;

        /// <summary>True when there is something to write.</summary>
        public bool IsUsable =>
            Positions.Count >= 9 && Indices.Count >= 3 && Indices.Count % 3 == 0;

        /// <inheritdoc />
        public override string ToString() =>
            Name + " (" + Triangles.ToString("N0", CultureInfo.InvariantCulture) + " triangles)";
    }

    /// <summary>What one write produced.</summary>
    public sealed class GltfResult
    {
        internal GltfResult(byte[] bytes, int meshes, int triangles, int skipped, IReadOnlyList<string> notes)
        {
            Bytes = bytes;
            Meshes = meshes;
            Triangles = triangles;
            Skipped = skipped;
            Notes = notes;
        }

        /// <summary>The file.</summary>
        public byte[] Bytes { get; }

        /// <summary>How many meshes went in.</summary>
        public int Meshes { get; }

        /// <summary>How many triangles went in.</summary>
        public int Triangles { get; }

        /// <summary>How many meshes were left out for having no usable geometry.</summary>
        public int Skipped { get; }

        /// <summary>Anything the person should know about what they got.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            var s = Meshes.ToString("N0", CultureInfo.InvariantCulture) + " objects, "
                  + Triangles.ToString("N0", CultureInfo.InvariantCulture) + " triangles, "
                  + (Bytes.Length / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

            if (Skipped > 0)
                s += " - " + Skipped.ToString("N0", CultureInfo.InvariantCulture)
                   + " carried no triangles and are not in it";

            return s;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// A glTF 2.0 binary file, written by hand.
    ///
    /// GLB is what a client's viewer opens: a browser, a phone, a tablet on site, with nothing
    /// installed. Every other way of sending somebody a model asks them to install something, and
    /// the ones that do not are per-seat, per-format licences on top of the Navisworks they already
    /// bought.
    ///
    /// <b>Hand-written, like the PDF and the spreadsheet.</b> Same reason: a netstandard2.0 Core
    /// with no third-party runtime is what lets the whole format be tested on Linux CI, byte for
    /// byte, without a host or a graphics stack. glTF is a small enough format to write correctly
    /// and a well specified enough one to be sure about.
    ///
    /// <b>Z-up becomes Y-up at the root.</b> Navisworks is Z-up and glTF is Y-up, which is the one
    /// thing everybody gets wrong: the model opens on its side and nobody can say why. A single
    /// root node carrying a -90 degree rotation about X fixes it once, where a viewer can see what
    /// was done, rather than silently rewriting every coordinate.
    /// </summary>
    public static class GltfWriter
    {
        /// <summary>GLB's magic number: "glTF" little-endian.</summary>
        public const uint Magic = 0x46546C67;

        /// <summary>The JSON chunk's type marker.</summary>
        public const uint JsonChunk = 0x4E4F534A;

        /// <summary>The binary chunk's type marker.</summary>
        public const uint BinaryChunk = 0x004E4942;

        /// <summary>What the file says produced it.</summary>
        public const string Generator = "CamelWorks (bimcamel.com)";

        /// <summary>
        /// The quaternion that turns a Z-up model the right way up for glTF.
        ///
        /// -90 degrees about X, as (x, y, z, w). Written out rather than computed so the number in
        /// the file is the number in the source: a viewer showing a model on its side is a bug
        /// nobody reports as one, they just decide the export is broken.
        /// </summary>
        public static IReadOnlyList<double> ZUpToYUp { get; } =
            new[] { -0.7071067811865475, 0.0, 0.0, 0.7071067811865476 };

        /// <summary>
        /// Write a GLB.
        /// </summary>
        /// <param name="meshes">The elements. Anything without triangles is counted and left out.</param>
        /// <param name="title">What the scene is called.</param>
        public static GltfResult Write(IReadOnlyList<GltfMesh>? meshes, string? title = null)
        {
            var usable = (meshes ?? Array.Empty<GltfMesh>()).Where(m => m != null && m.IsUsable).ToList();
            var skipped = (meshes?.Count ?? 0) - usable.Count;
            var notes = new List<string>();

            var binary = new MemoryStream();
            var bufferViews = new List<JsonValue>();
            var accessors = new List<JsonValue>();
            var gltfMeshes = new List<JsonValue>();
            var nodes = new List<JsonValue>();
            var materials = new List<JsonValue>();
            var materialOf = new Dictionary<string, int>(StringComparer.Ordinal);

            var children = new List<JsonValue>();
            var triangles = 0;

            foreach (var mesh in usable)
            {
                var material = MaterialIndex(mesh, materials, materialOf);

                // Positions first, then indices, each on a four-byte boundary. The spec requires
                // an accessor's offset to be a multiple of its component size; both are four here,
                // so aligning the views is enough.
                Pad(binary);
                var positionOffset = (int)binary.Length;

                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

                for (var i = 0; i + 2 < mesh.Positions.Count; i += 3)
                {
                    var x = mesh.Positions[i];
                    var y = mesh.Positions[i + 1];
                    var z = mesh.Positions[i + 2];

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (z < minZ) minZ = z;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                    if (z > maxZ) maxZ = z;

                    WriteFloat(binary, x);
                    WriteFloat(binary, y);
                    WriteFloat(binary, z);
                }

                var positionLength = (int)binary.Length - positionOffset;

                Pad(binary);
                var indexOffset = (int)binary.Length;

                foreach (var index in mesh.Indices) WriteUInt(binary, (uint)Math.Max(0, index));

                var indexLength = (int)binary.Length - indexOffset;

                var positionView = bufferViews.Count;
                bufferViews.Add(View(positionOffset, positionLength, 34962));

                var indexView = bufferViews.Count;
                bufferViews.Add(View(indexOffset, indexLength, 34963));

                var positionAccessor = accessors.Count;

                // POSITION carries min and max, and only POSITION does. The spec requires them
                // there and a viewer uses them to frame the model without reading the buffer.
                accessors.Add(JsonValue.Object()
                    .Set("bufferView", JsonValue.Number((long)positionView))
                    .Set("componentType", JsonValue.Number(5126L))
                    .Set("count", JsonValue.Number((long)mesh.Vertices))
                    .Set("type", JsonValue.String("VEC3"))
                    .Set("min", JsonValue.Array(new[] { Ordinate(minX), Ordinate(minY), Ordinate(minZ) }))
                    .Set("max", JsonValue.Array(new[] { Ordinate(maxX), Ordinate(maxY), Ordinate(maxZ) })));

                var indexAccessor = accessors.Count;

                accessors.Add(JsonValue.Object()
                    .Set("bufferView", JsonValue.Number((long)indexView))
                    .Set("componentType", JsonValue.Number(5125L))
                    .Set("count", JsonValue.Number((long)mesh.Indices.Count))
                    .Set("type", JsonValue.String("SCALAR")));

                var meshIndex = gltfMeshes.Count;

                gltfMeshes.Add(JsonValue.Object()
                    .Set("name", JsonValue.String(mesh.Name))
                    .Set("primitives", JsonValue.Array(new[]
                    {
                        JsonValue.Object()
                            .Set("attributes", JsonValue.Object()
                                .Set("POSITION", JsonValue.Number((long)positionAccessor)))
                            .Set("indices", JsonValue.Number((long)indexAccessor))
                            .Set("material", JsonValue.Number((long)material))
                            .Set("mode", JsonValue.Number(4L)),
                    })));

                children.Add(JsonValue.Number((long)nodes.Count + 1));

                nodes.Add(JsonValue.Object()
                    .Set("name", JsonValue.String(mesh.Name))
                    .Set("mesh", JsonValue.Number((long)meshIndex)));

                triangles += mesh.Triangles;
            }

            // The root goes in front of the element nodes, which is why their indices are one
            // higher: it carries the rotation, and every element hangs off it.
            var root = JsonValue.Object()
                .Set("name", JsonValue.String(string.IsNullOrWhiteSpace(title) ? "Model" : title!.Trim()))
                .Set("rotation", JsonValue.Array(ZUpToYUp.Select(v => JsonValue.Number(v))));

            if (children.Count > 0) root.Set("children", JsonValue.Array(children));

            nodes.Insert(0, root);

            if (materials.Count == 0) materials.Add(Material(0.75, 0.75, 0.75, 1));

            var json = JsonValue.Object()
                .Set("asset", JsonValue.Object()
                    .Set("version", JsonValue.String("2.0"))
                    .Set("generator", JsonValue.String(Generator)))
                .Set("scene", JsonValue.Number(0L))
                .Set("scenes", JsonValue.Array(new[]
                {
                    JsonValue.Object().Set("nodes", JsonValue.Array(new[] { JsonValue.Number(0L) })),
                }))
                .Set("nodes", JsonValue.Array(nodes))
                .Set("materials", JsonValue.Array(materials));

            if (gltfMeshes.Count > 0)
            {
                json.Set("meshes", JsonValue.Array(gltfMeshes))
                    .Set("accessors", JsonValue.Array(accessors))
                    .Set("bufferViews", JsonValue.Array(bufferViews))
                    .Set("buffers", JsonValue.Array(new[]
                    {
                        JsonValue.Object().Set("byteLength", JsonValue.Number(binary.Length)),
                    }));
            }
            else
            {
                notes.Add("Nothing in scope carried any triangles, so the file has a scene in it and "
                          + "no geometry. A viewer will open it and show nothing, which is the truth.");
            }

            if (skipped > 0)
                notes.Add(skipped.ToString("N0", CultureInfo.InvariantCulture)
                          + " elements carried no triangles. Annotations, reference planes and "
                          + "grouping nodes have none, so some of that is expected.");

            var bytes = Pack(json.ToJson(indented: false), binary.ToArray());

            if (bytes.Length > 200 * 1024 * 1024)
                notes.Add("The file is over 200 MB. Most web viewers will struggle with that; export "
                          + "a selection or a level rather than the whole federation.");

            return new GltfResult(bytes, usable.Count, triangles, skipped, notes);
        }

        /// <summary>The material for a mesh, made on first use.</summary>
        private static int MaterialIndex(GltfMesh mesh, ICollection<JsonValue> materials,
                                         IDictionary<string, int> known)
        {
            var key = mesh.Material + "|" + mesh.Red.ToString("0.###", CultureInfo.InvariantCulture)
                    + "|" + mesh.Green.ToString("0.###", CultureInfo.InvariantCulture)
                    + "|" + mesh.Blue.ToString("0.###", CultureInfo.InvariantCulture)
                    + "|" + mesh.Alpha.ToString("0.###", CultureInfo.InvariantCulture);

            if (known.TryGetValue(key, out var found)) return found;

            var index = materials.Count;
            materials.Add(Material(mesh.Red, mesh.Green, mesh.Blue, mesh.Alpha, mesh.Material));
            known[key] = index;

            return index;
        }

        private static JsonValue Material(double r, double g, double b, double a, string? name = null)
        {
            var material = JsonValue.Object()
                .Set("pbrMetallicRoughness", JsonValue.Object()
                    .Set("baseColorFactor", JsonValue.Array(new[]
                    {
                        JsonValue.Number(Clamp(r)), JsonValue.Number(Clamp(g)),
                        JsonValue.Number(Clamp(b)), JsonValue.Number(Clamp(a)),
                    }))
                    .Set("metallicFactor", JsonValue.Number(0.0))
                    .Set("roughnessFactor", JsonValue.Number(0.9)))
                .Set("doubleSided", JsonValue.Bool(true));

            if (!string.IsNullOrWhiteSpace(name)) material.Set("name", JsonValue.String(name!));

            // Anything short of opaque has to say so, or a viewer draws it solid and the whole point
            // of a greyed-back structure layer is lost.
            if (Clamp(a) < 1) material.Set("alphaMode", JsonValue.String("BLEND"));

            return material;
        }

        private static double Clamp(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>
        /// One coordinate for the JSON, at the precision the buffer actually holds.
        ///
        /// Widening the float straight to a double writes 0.10000000149011612, which is exactly
        /// what is in the buffer and is unreadable. Seven significant figures is a float's whole
        /// precision, so this loses nothing and a person can read the bounds.
        /// </summary>
        private static JsonValue Ordinate(float value) =>
            JsonValue.Number(double.Parse(value.ToString("G7", CultureInfo.InvariantCulture),
                                          NumberStyles.Float, CultureInfo.InvariantCulture));

        private static JsonValue View(int offset, int length, long target) =>
            JsonValue.Object()
                .Set("buffer", JsonValue.Number(0L))
                .Set("byteOffset", JsonValue.Number((long)offset))
                .Set("byteLength", JsonValue.Number((long)length))
                .Set("target", JsonValue.Number(target));

        /// <summary>Header, JSON chunk, binary chunk. Every length is little-endian.</summary>
        private static byte[] Pack(string json, byte[] binary)
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            // The JSON chunk pads with spaces and the binary chunk pads with zeros. Not
            // interchangeable: a reader that trims the JSON chunk to its declared length would be
            // handed trailing nulls, and JSON parsers differ on what they do with those.
            var jsonPadding = (4 - (jsonBytes.Length % 4)) % 4;
            var binaryPadding = (4 - (binary.Length % 4)) % 4;

            var total = 12
                      + 8 + jsonBytes.Length + jsonPadding
                      + (binary.Length > 0 ? 8 + binary.Length + binaryPadding : 0);

            using (var output = new MemoryStream(total))
            {
                WriteUInt(output, Magic);
                WriteUInt(output, 2);
                WriteUInt(output, (uint)total);

                WriteUInt(output, (uint)(jsonBytes.Length + jsonPadding));
                WriteUInt(output, JsonChunk);
                output.Write(jsonBytes, 0, jsonBytes.Length);
                for (var i = 0; i < jsonPadding; i++) output.WriteByte(0x20);

                if (binary.Length > 0)
                {
                    WriteUInt(output, (uint)(binary.Length + binaryPadding));
                    WriteUInt(output, BinaryChunk);
                    output.Write(binary, 0, binary.Length);
                    for (var i = 0; i < binaryPadding; i++) output.WriteByte(0);
                }

                return output.ToArray();
            }
        }

        private static void Pad(Stream stream)
        {
            while (stream.Length % 4 != 0) stream.WriteByte(0);
        }

        private static void WriteUInt(Stream stream, uint value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
            stream.WriteByte((byte)((value >> 16) & 0xFF));
            stream.WriteByte((byte)((value >> 24) & 0xFF));
        }

        private static void WriteFloat(Stream stream, float value)
        {
            var bytes = BitConverter.GetBytes(value);

            // Written a byte at a time in a fixed order, because glTF is little-endian everywhere
            // and BitConverter follows the machine. Nothing ships on a big-endian machine today,
            // and a file that is silently wrong on one is worse than four extra writes.
            if (BitConverter.IsLittleEndian)
            {
                stream.Write(bytes, 0, 4);
                return;
            }

            for (var i = 3; i >= 0; i--) stream.WriteByte(bytes[i]);
        }
    }
}
