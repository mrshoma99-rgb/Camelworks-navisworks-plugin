using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Clash
{
    /// <summary>What a marker is shaped like.</summary>
    public enum MarkerShape
    {
        /// <summary>A ball. Reads the same from every angle, which is what a marker is for.</summary>
        Sphere = 0,

        /// <summary>A box. Cheaper, and squarer against a model that is mostly boxes.</summary>
        Cube = 1,

        /// <summary>An octahedron: eight faces, and unmistakably not part of the model.</summary>
        Diamond = 2,
    }

    /// <summary>How the markers are made.</summary>
    public sealed class MarkerRecipe
    {
        /// <summary>The most markers worth writing before it refuses.</summary>
        public const int DefaultCeiling = 5000;

        /// <summary>The finest sphere it will make.</summary>
        public const int MaxDetail = 3;

        /// <summary>How big a marker is, in metres.</summary>
        public double RadiusMetres { get; set; } = 0.15;

        /// <summary>What shape they are.</summary>
        public MarkerShape Shape { get; set; } = MarkerShape.Sphere;

        /// <summary>
        /// How round a sphere is: 0 gives twenty faces, 3 gives one thousand two hundred and eighty.
        ///
        /// One by default. A marker is a thing somebody looks at from across a room, and a
        /// thousand-face ball times four hundred clashes is a model that will not spin.
        /// </summary>
        public int Detail { get; set; } = 1;

        /// <summary>
        /// What one model unit is in metres.
        ///
        /// The whole reason this needs saying: the host reports clash points in the document's own
        /// units, and its OBJ reader takes every vertex as metres. A millimetre model whose markers
        /// were written unconverted puts them a kilometre away, and the person who exported them
        /// sees an empty model rather than an error.
        /// </summary>
        public double MetresPerUnit { get; set; } = 1;

        /// <summary>Whether each status gets its own colour.</summary>
        public bool ColourByStatus { get; set; } = true;

        /// <summary>How many markers it will write before refusing outright.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>
        /// What a marker's name starts with when the result has no name of its own.
        ///
        /// A fallback only: a result the engine named is written under that name, so the person
        /// opening the file elsewhere sees what the coordinator sees.
        /// </summary>
        public string Prefix { get; set; } = "Clash";
    }

    /// <summary>The geometry, ready to be appended to a document.</summary>
    public sealed class MarkerMesh
    {
        internal MarkerMesh(string text, string materials, string materialFile, int markers,
                            int triangles, IReadOnlyList<string> warnings, string? refusal)
        {
            Text = text; Materials = materials; MaterialFile = materialFile;
            Markers = markers; Triangles = triangles; Warnings = warnings; Refusal = refusal;
        }

        /// <summary>The .obj.</summary>
        public string Text { get; }

        /// <summary>The .mtl that goes beside it.</summary>
        public string Materials { get; }

        /// <summary>What the .mtl has to be called for the .obj to find it.</summary>
        public string MaterialFile { get; }

        /// <summary>How many markers went in.</summary>
        public int Markers { get; }

        /// <summary>How many triangles that came to.</summary>
        public int Triangles { get; }

        /// <summary>Anything worth saying about what was written.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Why nothing was written, or null.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is geometry.</summary>
        public bool IsUsable => Refusal == null && Markers > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain() =>
            Refusal
            ?? (Markers == 0
                ? "Nothing to mark."
                : Measure.Count(Markers) + (Markers == 1 ? " marker, " : " markers, ")
                  + Measure.Count(Triangles) + " triangles.");

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// Clash points as a file somebody else opens.
    ///
    /// <b>Not the marker you press a button for.</b> Dropping a ball on a clash while you are
    /// looking at it is <see cref="MarkerOverlay"/>: temporary graphics, up on the next frame and
    /// down when you say so. This is the other thing - the mesh a walkthrough, a site meeting or a
    /// subcontractor's own copy of the model needs, still there tomorrow and openable without this
    /// product installed. It is only ever reached through an explicit export.
    ///
    /// <b>Metres, always.</b> The host reports clash points in the document's own units and its OBJ
    /// reader takes every vertex as metres. A millimetre model whose markers went out unconverted
    /// puts them a kilometre from the building, and what the person sees is an empty model rather
    /// than an error. That conversion is the single thing this file exists to get right.
    ///
    /// <b>Named so the tree is useful.</b> A group per test and an object per clash, each carrying
    /// its status, so the Selection Tree collapses to a line per test and expands to the clashes -
    /// which is what makes them selectable, hideable and colourable by the tools already here.
    /// </summary>
    public static class ClashMarkers
    {
        /// <summary>Make the geometry.</summary>
        /// <param name="items">The clashes to mark.</param>
        /// <param name="recipe">Size, shape, units and the ceiling.</param>
        /// <param name="materialFile">What the .mtl beside it will be called.</param>
        public static MarkerMesh Write(IEnumerable<ClashItem>? items, MarkerRecipe? recipe = null,
                                       string materialFile = "clashes.mtl")
        {
            var how = recipe ?? new MarkerRecipe();
            var all = (items ?? Array.Empty<ClashItem>()).Where(i => i != null).ToList();
            var warnings = new List<string>();

            if (all.Count == 0)
                return Refuse("There are no clashes to mark.", materialFile);

            if (how.RadiusMetres <= 0)
                return Refuse("A marker radius of zero produces nothing visible. Set a radius.",
                              materialFile);

            if (how.MetresPerUnit <= 0)
                return Refuse("The model's units are not known, so the markers cannot be placed. "
                    + "One model unit has to be some number of metres.", materialFile);

            if (all.Count > Math.Max(1, how.Ceiling))
                return Refuse("That would write " + Measure.Count(all.Count) + " markers, and the "
                    + "ceiling is " + Measure.Count(Math.Max(1, how.Ceiling)) + ". A model with "
                    + "that many markers will not navigate acceptably. Mark one test at a time, or "
                    + "raise the ceiling deliberately.", materialFile);

            var detail = how.Detail < 0 ? 0 : how.Detail > MarkerRecipe.MaxDetail
                ? MarkerRecipe.MaxDetail
                : how.Detail;

            if (how.Detail > MarkerRecipe.MaxDetail)
                warnings.Add("A sphere finer than " + MarkerRecipe.MaxDetail.ToString(CultureInfo.InvariantCulture)
                    + " is more triangles than a marker is worth, so it was made at that.");

            var unit = Shape(how.Shape, detail);

            var body = new StringBuilder();
            var used = new HashSet<FindingStatus>();

            body.Append("# CamelWorks clash markers\n");
            body.Append("# Vertices are metres, which is what the Navisworks OBJ reader assumes.\n");
            body.Append("mtllib ").Append(materialFile).Append('\n');

            var offset = 1;   // OBJ indices are 1-based and run across the whole file
            var written = 0;
            var triangles = 0;

            foreach (var test in all
                         .GroupBy(i => i.TestName, StringComparer.Ordinal)
                         .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                body.Append("g ").Append(Clean(test.Key)).Append('\n');

                var n = 0;
                var taken = new Dictionary<string, int>(StringComparer.Ordinal);

                foreach (var item in test)
                {
                    // The engine's own name for the result when it has one, because the person on
                    // the other end has to be able to say "Clash117" back to the person who sent
                    // it. A counter otherwise, which is all a result with no name can offer.
                    n++;

                    var stem = string.IsNullOrWhiteSpace(item.Name)
                        ? Clean(how.Prefix) + n.ToString(CultureInfo.InvariantCulture)
                        : Clean(item.Name!);

                    var name = stem + "_" + Word(item.Status);

                    // Two results of one test can carry the same name - a renumber that collided,
                    // or a name somebody typed twice. Two objects of one name in a selection tree
                    // is a tree where one of them cannot be pointed at.
                    if (taken.TryGetValue(name, out var seen))
                    {
                        taken[name] = seen + 1;
                        name += "_" + (seen + 1).ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        taken[name] = 1;
                    }

                    body.Append("o ").Append(name).Append('\n');

                    if (how.ColourByStatus)
                    {
                        body.Append("usemtl ").Append(Word(item.Status)).Append('\n');
                        used.Add(item.Status);
                    }

                    // The conversion, at the only place it can be wrong.
                    var x = item.X * how.MetresPerUnit;
                    var y = item.Y * how.MetresPerUnit;
                    var z = item.Z * how.MetresPerUnit;

                    foreach (var vertex in unit.Vertices)
                        body.Append("v ")
                            .Append(Number(x + vertex.X * how.RadiusMetres)).Append(' ')
                            .Append(Number(y + vertex.Y * how.RadiusMetres)).Append(' ')
                            .Append(Number(z + vertex.Z * how.RadiusMetres)).Append('\n');

                    foreach (var face in unit.Faces)
                        body.Append("f ")
                            .Append(face.A + offset).Append(' ')
                            .Append(face.B + offset).Append(' ')
                            .Append(face.C + offset).Append('\n');

                    offset += unit.Vertices.Count;
                    triangles += unit.Faces.Count;
                    written++;
                }
            }

            return new MarkerMesh(body.ToString(), Palette(used, how), materialFile, written,
                                  triangles, warnings, null);
        }

        /// <summary>The colours, one per status that was actually written.</summary>
        private static string Palette(ICollection<FindingStatus> used, MarkerRecipe how)
        {
            if (!how.ColourByStatus || used.Count == 0) return string.Empty;

            var s = new StringBuilder();

            s.Append("# CamelWorks clash markers\n");

            foreach (var status in new[]
            {
                FindingStatus.New, FindingStatus.Active, FindingStatus.Reviewed,
                FindingStatus.Approved, FindingStatus.Resolved,
            })
            {
                if (!used.Contains(status)) continue;

                var colour = Colour(status);

                s.Append("newmtl ").Append(Word(status)).Append('\n');
                s.Append("Kd ").Append(Number(colour.R / 255.0)).Append(' ')
                 .Append(Number(colour.G / 255.0)).Append(' ')
                 .Append(Number(colour.B / 255.0)).Append('\n');
                s.Append("Ka 0 0 0\n");
                s.Append("d 1\n");
                s.Append("illum 1\n");
            }

            return s.ToString();
        }

        /// <summary>
        /// The status colours, which are the board's own.
        ///
        /// Red for new, amber for active, blue for looked at, green for done. Not a palette anybody
        /// chose here - it is what every clash tool has used for twenty years, and a marker that
        /// disagrees with the board it came from is a marker somebody has to translate.
        /// </summary>
        /// <param name="status">Where the clash stands.</param>
        public static Colour Colour(FindingStatus status) => status switch
        {
            FindingStatus.Active => new Colour(0xE8, 0x8B, 0x00),
            FindingStatus.Reviewed => new Colour(0x1F, 0x6F, 0xB2),
            FindingStatus.Approved => new Colour(0x6B, 0x7A, 0x8A),
            FindingStatus.Resolved => new Colour(0x2E, 0x8B, 0x3D),
            _ => new Colour(0xC8, 0x10, 0x2E),
        };

        private static string Word(FindingStatus status) => status switch
        {
            FindingStatus.Active => "Active",
            FindingStatus.Reviewed => "Reviewed",
            FindingStatus.Approved => "Approved",
            FindingStatus.Resolved => "Resolved",
            _ => "New",
        };

        /// <summary>
        /// A name an OBJ reader will not choke on.
        ///
        /// Names are whitespace-delimited in the format, so a test called "L03 MEP v STR" would
        /// arrive as an object called "L03" and three pieces of nonsense after it.
        /// </summary>
        private static string Clean(string? name)
        {
            var text = (name ?? string.Empty).Trim();

            if (text.Length == 0) return "Unnamed";

            var s = new StringBuilder(text.Length);

            foreach (var c in text)
                s.Append(c == ' ' || c == '\t' || c == '\n' || c == '\r' ? '_' : c);

            return s.ToString();
        }

        private static string Number(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private static MarkerMesh Refuse(string why, string materialFile) =>
            new MarkerMesh(string.Empty, string.Empty, materialFile, 0, 0,
                           Array.Empty<string>(), why);

        // -------------------------------------------------------------------------------------
        // The shapes, all of them unit-sized and centred on nothing
        // -------------------------------------------------------------------------------------

        /// <summary>
        /// One vertex of a unit shape.
        ///
        /// Not called Point. WPF has one, Navisworks has one, and a name three assemblies share is
        /// a name somebody eventually has to qualify at every use.
        /// </summary>
        internal readonly struct Vertex
        {
            internal Vertex(double x, double y, double z) { X = x; Y = y; Z = z; }

            internal double X { get; }

            internal double Y { get; }

            internal double Z { get; }

            internal Vertex Normalised()
            {
                var length = Math.Sqrt(X * X + Y * Y + Z * Z);
                return length <= 0 ? this : new Vertex(X / length, Y / length, Z / length);
            }
        }

        /// <summary>One triangle, by vertex index.</summary>
        internal readonly struct Face
        {
            internal Face(int a, int b, int c) { A = a; B = b; C = c; }

            internal int A { get; }

            internal int B { get; }

            internal int C { get; }
        }

        /// <summary>A unit shape: vertices on the unit sphere, and the triangles between them.</summary>
        internal sealed class Solid
        {
            internal Solid(IReadOnlyList<Vertex> vertices, IReadOnlyList<Face> faces)
            {
                Vertices = vertices; Faces = faces;
            }

            internal IReadOnlyList<Vertex> Vertices { get; }

            internal IReadOnlyList<Face> Faces { get; }
        }

        private static Solid Shape(MarkerShape shape, int detail) => shape switch
        {
            MarkerShape.Cube => Cube(),
            MarkerShape.Diamond => Octahedron(),
            _ => Sphere(detail),
        };

        /// <summary>
        /// An icosphere: an icosahedron, subdivided, with every vertex pushed onto the unit sphere.
        ///
        /// Rather than a lat-long ball, which crowds triangles at the poles and wastes most of them
        /// where nobody is looking. Twenty faces at detail 0, and four times that per level.
        /// </summary>
        private static Solid Sphere(int detail)
        {
            var t = (1 + Math.Sqrt(5)) / 2;

            var vertices = new List<Vertex>
            {
                new Vertex(-1, t, 0), new Vertex(1, t, 0), new Vertex(-1, -t, 0), new Vertex(1, -t, 0),
                new Vertex(0, -1, t), new Vertex(0, 1, t), new Vertex(0, -1, -t), new Vertex(0, 1, -t),
                new Vertex(t, 0, -1), new Vertex(t, 0, 1), new Vertex(-t, 0, -1), new Vertex(-t, 0, 1),
            };

            for (var i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].Normalised();

            var faces = new List<Face>
            {
                new Face(0, 11, 5), new Face(0, 5, 1), new Face(0, 1, 7), new Face(0, 7, 10),
                new Face(0, 10, 11), new Face(1, 5, 9), new Face(5, 11, 4), new Face(11, 10, 2),
                new Face(10, 7, 6), new Face(7, 1, 8), new Face(3, 9, 4), new Face(3, 4, 2),
                new Face(3, 2, 6), new Face(3, 6, 8), new Face(3, 8, 9), new Face(4, 9, 5),
                new Face(2, 4, 11), new Face(6, 2, 10), new Face(8, 6, 7), new Face(9, 8, 1),
            };

            for (var round = 0; round < detail; round++)
            {
                var middles = new Dictionary<long, int>();
                var next = new List<Face>(faces.Count * 4);

                int Middle(int a, int b)
                {
                    // Keyed on the pair, so neighbouring triangles share the vertex between them
                    // rather than each making its own and leaving a seam.
                    var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

                    if (middles.TryGetValue(key, out var found)) return found;

                    var one = vertices[a];
                    var two = vertices[b];

                    vertices.Add(new Vertex((one.X + two.X) / 2, (one.Y + two.Y) / 2,
                                           (one.Z + two.Z) / 2).Normalised());

                    middles[key] = vertices.Count - 1;
                    return vertices.Count - 1;
                }

                foreach (var face in faces)
                {
                    var ab = Middle(face.A, face.B);
                    var bc = Middle(face.B, face.C);
                    var ca = Middle(face.C, face.A);

                    next.Add(new Face(face.A, ab, ca));
                    next.Add(new Face(face.B, bc, ab));
                    next.Add(new Face(face.C, ca, bc));
                    next.Add(new Face(ab, bc, ca));
                }

                faces = next;
            }

            return new Solid(vertices, faces);
        }

        private static Solid Cube()
        {
            // On the unit sphere, so a cube and a sphere of the same radius look the same size.
            var h = 1 / Math.Sqrt(3);

            var vertices = new List<Vertex>
            {
                new Vertex(-h, -h, -h), new Vertex(h, -h, -h), new Vertex(h, h, -h), new Vertex(-h, h, -h),
                new Vertex(-h, -h, h), new Vertex(h, -h, h), new Vertex(h, h, h), new Vertex(-h, h, h),
            };

            var faces = new List<Face>
            {
                new Face(0, 2, 1), new Face(0, 3, 2),   // bottom
                new Face(4, 5, 6), new Face(4, 6, 7),   // top
                new Face(0, 1, 5), new Face(0, 5, 4),
                new Face(1, 2, 6), new Face(1, 6, 5),
                new Face(2, 3, 7), new Face(2, 7, 6),
                new Face(3, 0, 4), new Face(3, 4, 7),
            };

            return new Solid(vertices, faces);
        }

        private static Solid Octahedron()
        {
            var vertices = new List<Vertex>
            {
                new Vertex(1, 0, 0), new Vertex(-1, 0, 0), new Vertex(0, 1, 0),
                new Vertex(0, -1, 0), new Vertex(0, 0, 1), new Vertex(0, 0, -1),
            };

            var faces = new List<Face>
            {
                new Face(0, 2, 4), new Face(2, 1, 4), new Face(1, 3, 4), new Face(3, 0, 4),
                new Face(2, 0, 5), new Face(1, 2, 5), new Face(3, 1, 5), new Face(0, 3, 5),
            };

            return new Solid(vertices, faces);
        }
    }
}
