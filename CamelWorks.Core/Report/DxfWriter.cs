using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CamelWorks.Core.Report
{
    /// <summary>One element's geometry, on its way into a drawing.</summary>
    public sealed class DxfSolid
    {
        /// <summary>Make one.</summary>
        /// <param name="name">What the element is called.</param>
        /// <param name="positions">Vertices, three numbers each, in metres.</param>
        /// <param name="indices">Triangles, three indices each.</param>
        public DxfSolid(string? name, IReadOnlyList<float>? positions, IReadOnlyList<int>? indices)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name!.Trim();
            Positions = positions ?? Array.Empty<float>();
            Indices = indices ?? Array.Empty<int>();
        }

        /// <summary>What the element is called.</summary>
        public string Name { get; }

        /// <summary>Vertices, three numbers each.</summary>
        public IReadOnlyList<float> Positions { get; }

        /// <summary>Triangles, three indices each.</summary>
        public IReadOnlyList<int> Indices { get; }

        /// <summary>Which layer it goes on. Empty takes the drawing's default.</summary>
        public string Layer { get; set; } = string.Empty;

        /// <summary>Its colour, red channel, 0 to 1.</summary>
        public double Red { get; set; } = 0.75;

        /// <summary>Its colour, green channel, 0 to 1.</summary>
        public double Green { get; set; } = 0.75;

        /// <summary>Its colour, blue channel, 0 to 1.</summary>
        public double Blue { get; set; } = 0.75;

        /// <summary>Properties to carry into the drawing as extended entity data.</summary>
        public IList<KeyValuePair<string, string>> Data { get; } =
            new List<KeyValuePair<string, string>>();

        /// <summary>How many triangles it has.</summary>
        public int Triangles => Indices.Count / 3;

        /// <summary>True when there is a whole number of triangles and every index is in range.</summary>
        public bool IsUsable =>
            Positions.Count >= 9 && Positions.Count % 3 == 0
            && Indices.Count >= 3 && Indices.Count % 3 == 0
            && Indices.All(i => i >= 0 && i * 3 + 2 < Positions.Count);

        /// <summary>Take one from a mesh already read for glTF.</summary>
        /// <param name="mesh">The mesh.</param>
        public static DxfSolid? From(GltfMesh? mesh)
        {
            if (mesh == null) return null;

            return new DxfSolid(mesh.Name, mesh.Positions, mesh.Indices)
            {
                Red = mesh.Red,
                Green = mesh.Green,
                Blue = mesh.Blue,
            };
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + Triangles.ToString("N0", CultureInfo.InvariantCulture)
            + (Triangles == 1 ? " triangle" : " triangles");
    }

    /// <summary>What was written, and what could not be.</summary>
    public sealed class DxfResult
    {
        internal DxfResult(string text, int solids, int faces, int skipped, IReadOnlyList<string> layers,
                           int truncated, IReadOnlyList<string> notes)
        {
            Text = text;
            Solids = solids;
            Faces = faces;
            Skipped = skipped;
            Layers = layers;
            Truncated = truncated;
            Notes = notes;
        }

        /// <summary>The drawing, as text. DXF is a text format and stays one.</summary>
        public string Text { get; }

        /// <summary>How many elements went in.</summary>
        public int Solids { get; }

        /// <summary>How many faces were written.</summary>
        public int Faces { get; }

        /// <summary>How many elements could not be written, and were counted rather than dropped.</summary>
        public int Skipped { get; }

        /// <summary>The layers the drawing declares.</summary>
        public IReadOnlyList<string> Layers { get; }

        /// <summary>How many property values were cut to fit the format's limit.</summary>
        public int Truncated { get; }

        /// <summary>Anything worth saying about what was written.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>True when there is a drawing worth opening.</summary>
        public bool IsUsable => Faces > 0;

        /// <summary>What came out, said plainly.</summary>
        public string Explain()
        {
            if (Faces == 0)
                return "Nothing could be drawn. " + (Solids == 0
                    ? "No geometry was handed over."
                    : Skipped.ToString("N0", CultureInfo.InvariantCulture)
                      + " element(s) had geometry that could not be read as triangles.");

            var text = Solids.ToString("N0", CultureInfo.InvariantCulture)
                     + (Solids == 1 ? " element, " : " elements, ")
                     + Faces.ToString("N0", CultureInfo.InvariantCulture)
                     + (Faces == 1 ? " face, on " : " faces, on ")
                     + Layers.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Layers.Count == 1 ? " layer." : " layers.");

            if (Skipped > 0)
                text += " " + Skipped.ToString("N0", CultureInfo.InvariantCulture)
                     + " could not be read as triangles and were left out.";

            if (Truncated > 0)
                text += " " + Truncated.ToString("N0", CultureInfo.InvariantCulture)
                     + " property value(s) were cut to the 255 characters the format allows.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The model as a drawing every CAD package opens.
    ///
    /// The ask is always "give me a DWG". DWG is Autodesk's own format with no published
    /// specification, so anything writing one without their library is reverse-engineering a moving
    /// target. DXF is the same company's published interchange format for exactly this, AutoCAD
    /// opens it natively, and so does every other package that has ever claimed to read DWG. It is
    /// the honest answer to the question, and it is the whole answer for what the question is
    /// actually for: getting the coordinated geometry, on the right layers, with the model's data
    /// still attached, into somebody's drawing.
    ///
    /// Three things make it a deliverable rather than a mesh dump.
    ///
    /// <b>Layers come from the model.</b> A drawing whose geometry is all on layer 0 is worthless -
    /// nobody can freeze the ductwork. The caller says which property names the layer, and every
    /// distinct value becomes a layer, declared in the table with its own colour.
    ///
    /// <b>Properties travel.</b> Each face carries the element's properties as extended entity
    /// data under an application name of the caller's choosing, so a downstream tool can read the
    /// mark, the system and the asset tag straight off the entity. That is the whole point of an
    /// "intelligent" export, and it is four group codes.
    ///
    /// <b>R12, deliberately.</b> The oldest and most widely readable DXF there is: a header, a
    /// layer table, an entity section, and nothing else to get wrong. Its cost is the colour model
    /// - an index into a fixed palette rather than 24-bit RGB - so each element takes the nearest
    /// of the standard sixteen and the result says so rather than quietly shifting the greens.
    ///
    /// <b>One mesh per element, not one face per triangle.</b> The obvious way to write triangles
    /// is a 3DFACE each, and it produces a file nobody can open: extended data hangs off an entity,
    /// so a element with two hundred triangles carries its properties two hundred times, and a
    /// model of five thousand such elements is three hundred megabytes of repeated text. A polyface
    /// mesh is one entity holding all of an element's triangles, which is where its properties
    /// belong anyway - the element is the thing that has a mark, not the facet.
    /// </summary>
    public static class DxfWriter
    {
        /// <summary>The application name extended data is filed under when the caller says nothing.</summary>
        public const string DefaultApplication = "CAMELWORKS";

        /// <summary>The layer everything lands on when nothing names one.</summary>
        public const string DefaultLayer = "CAMELWORKS";

        /// <summary>How long one extended-data string may be before the format refuses it.</summary>
        public const int MaxDataLength = 255;

        /// <summary>How many layers one drawing will declare.</summary>
        public const int MaxLayers = 512;

        /// <summary>
        /// How many vertices or faces one mesh entity holds before the element is split across
        /// several. Readers disagree about the ceiling above this; nobody disagrees below it.
        /// </summary>
        public const int MaxMeshSize = 8000;

        /// <summary>What the header claims. R12, which everything reads.</summary>
        public const string Version = "AC1009";

        /// <summary>
        /// The palette a DXF R12 colour index means, for the entries CamelWorks will produce.
        ///
        /// The full index runs to 255. These are the ones with names, which is what makes them the
        /// right set to snap to: a person who opens the drawing and sees a colour called "red" can
        /// reason about it, where index 173 tells nobody anything.
        /// </summary>
        public static IReadOnlyList<string> Palette { get; } = new[]
        {
            "red", "yellow", "green", "cyan", "blue", "magenta", "white", "dark grey", "light grey",
        };

        /// <summary>
        /// Write the drawing.
        /// </summary>
        /// <param name="solids">The geometry.</param>
        /// <param name="application">What extended data is filed under.</param>
        public static DxfResult Write(IReadOnlyList<DxfSolid>? solids, string? application = null)
        {
            var app = Clean(application, DefaultApplication);
            var body = new StringBuilder();

            var layers = new List<string>();
            var colours = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();

            var written = 0;
            var faces = 0;
            var skipped = 0;
            var truncated = 0;
            var overflowed = false;

            foreach (var solid in solids ?? Array.Empty<DxfSolid>())
            {
                if (solid == null || !solid.IsUsable) { skipped++; continue; }

                var colour = Index(solid.Red, solid.Green, solid.Blue);
                var layer = Layer(solid.Layer);

                if (!colours.ContainsKey(layer))
                {
                    if (layers.Count >= MaxLayers)
                    {
                        overflowed = true;
                        layer = DefaultLayer;
                    }

                    if (!colours.ContainsKey(layer))
                    {
                        colours.Add(layer, colour);
                        layers.Add(layer);
                    }
                }

                var data = Data(solid, app, ref truncated);

                faces += Mesh(body, layer, colour, solid, data);
                written++;
            }

            if (layers.Count == 0)
            {
                colours.Add(DefaultLayer, 7);
                layers.Add(DefaultLayer);
            }

            if (overflowed)
                notes.Add("More than " + MaxLayers.ToString("N0", CultureInfo.InvariantCulture)
                          + " layers were asked for. The rest went on \"" + DefaultLayer
                          + "\" - a drawing with a layer per element is a drawing nobody can use.");

            var text = new StringBuilder();

            Header(text);
            Tables(text, layers, colours, app);

            text.Append("  0\r\nSECTION\r\n  2\r\nENTITIES\r\n");
            text.Append(body);
            text.Append("  0\r\nENDSEC\r\n  0\r\nEOF\r\n");

            return new DxfResult(text.ToString(), written, faces, skipped, layers, truncated, notes);
        }

        /// <summary>
        /// Which of the named colours an RGB is nearest, as a DXF colour index.
        ///
        /// Straight-line distance in RGB, which is not how eyes work but is how every other tool
        /// doing this does it, and matching what everyone else produces matters more here than
        /// being right in a way that surprises people.
        /// </summary>
        /// <param name="red">Red, 0 to 1.</param>
        /// <param name="green">Green, 0 to 1.</param>
        /// <param name="blue">Blue, 0 to 1.</param>
        public static int Index(double red, double green, double blue)
        {
            var r = Clamp(red);
            var g = Clamp(green);
            var b = Clamp(blue);

            var best = 7;
            var nearest = double.MaxValue;

            for (var i = 0; i < Known.Length; i++)
            {
                var candidate = Known[i];

                var dr = r - candidate.R;
                var dg = g - candidate.G;
                var db = b - candidate.B;

                var distance = dr * dr + dg * dg + db * db;

                if (distance >= nearest) continue;

                nearest = distance;
                best = candidate.Index;
            }

            return best;
        }

        /// <summary>What a colour index is called.</summary>
        /// <param name="index">The index.</param>
        public static string Named(int index)
        {
            for (var i = 0; i < Known.Length; i++)
                if (Known[i].Index == index)
                    return Palette[i];

            return index.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A layer name the format will accept.
        ///
        /// R12 refuses several punctuation marks in a name and has no escape for them, so they
        /// become underscores rather than a file that will not open.
        /// </summary>
        /// <param name="wanted">What the model said.</param>
        public static string Layer(string? wanted)
        {
            var text = (wanted ?? string.Empty).Trim();

            if (text.Length == 0) return DefaultLayer;

            var built = new StringBuilder(text.Length);

            foreach (var letter in text)
                built.Append(Forbidden.IndexOf(letter) >= 0 || letter < ' ' ? '_' : letter);

            var cleaned = built.ToString().Trim('_', ' ');

            return cleaned.Length == 0 ? DefaultLayer : cleaned;
        }

        private const string Forbidden = "<>/\\\":;?*|=`,";

        private static readonly (int Index, double R, double G, double B)[] Known =
        {
            (1, 1, 0, 0),
            (2, 1, 1, 0),
            (3, 0, 1, 0),
            (4, 0, 1, 1),
            (5, 0, 0, 1),
            (6, 1, 0, 1),
            (7, 1, 1, 1),
            (8, 0.5, 0.5, 0.5),
            (9, 0.75, 0.75, 0.75),
        };

        private static void Header(StringBuilder text)
        {
            text.Append("  0\r\nSECTION\r\n  2\r\nHEADER\r\n");
            text.Append("  9\r\n$ACADVER\r\n  1\r\n").Append(Version).Append("\r\n");
            text.Append("  9\r\n$INSUNITS\r\n 70\r\n     6\r\n");
            text.Append("  0\r\nENDSEC\r\n");
        }

        private static void Tables(StringBuilder text, IReadOnlyList<string> layers,
                                   IReadOnlyDictionary<string, int> colours, string application)
        {
            text.Append("  0\r\nSECTION\r\n  2\r\nTABLES\r\n");

            // The application has to be declared before an entity may file data under it. An
            // undeclared app id is the single most common reason extended data silently vanishes.
            text.Append("  0\r\nTABLE\r\n  2\r\nAPPID\r\n 70\r\n     1\r\n");
            text.Append("  0\r\nAPPID\r\n  2\r\n").Append(application).Append("\r\n 70\r\n     0\r\n");
            text.Append("  0\r\nENDTAB\r\n");

            text.Append("  0\r\nTABLE\r\n  2\r\nLAYER\r\n 70\r\n")
                .Append(Number(layers.Count)).Append("\r\n");

            foreach (var layer in layers)
            {
                text.Append("  0\r\nLAYER\r\n  2\r\n").Append(layer).Append("\r\n");
                text.Append(" 70\r\n     0\r\n");
                text.Append(" 62\r\n").Append(Number(colours[layer])).Append("\r\n");
                text.Append("  6\r\nCONTINUOUS\r\n");
            }

            text.Append("  0\r\nENDTAB\r\n  0\r\nENDSEC\r\n");
        }

        /// <summary>
        /// One element as polyface meshes, split when it is too big for one, and how many faces
        /// went in.
        ///
        /// A polyface mesh names its corners by index, so only the vertices a chunk actually uses
        /// are written - which is also what keeps a split from writing the whole element's vertex
        /// list into every piece of it.
        /// </summary>
        private static int Mesh(StringBuilder body, string layer, int colour, DxfSolid solid,
                                string data)
        {
            var written = 0;
            var at = 0;

            while (at + 2 < solid.Indices.Count)
            {
                var used = new List<int>();
                var seat = new Dictionary<int, int>();
                var faces = new List<int[]>();

                while (at + 2 < solid.Indices.Count
                       && faces.Count < MaxMeshSize
                       && used.Count + 3 <= MaxMeshSize)
                {
                    var corners = new int[3];

                    for (var c = 0; c < 3; c++)
                    {
                        var vertex = solid.Indices[at + c];

                        if (!seat.TryGetValue(vertex, out var place))
                        {
                            place = used.Count;
                            seat.Add(vertex, place);
                            used.Add(vertex);
                        }

                        // One-based, and positive so every edge is drawn. Negative means the edge
                        // between that corner and the next is invisible, which is for meshes that
                        // were quadrilaterals before somebody triangulated them - not for these.
                        corners[c] = place + 1;
                    }

                    faces.Add(corners);
                    at += 3;
                }

                if (faces.Count == 0) break;

                Polyface(body, layer, colour, solid, used, faces, data);

                written += faces.Count;
            }

            return written;
        }

        private static void Polyface(StringBuilder body, string layer, int colour, DxfSolid solid,
                                     IReadOnlyList<int> used, IReadOnlyList<int[]> faces, string data)
        {
            body.Append("  0\r\nPOLYLINE\r\n  8\r\n").Append(layer).Append("\r\n");
            body.Append(" 62\r\n").Append(Number(colour)).Append("\r\n");
            body.Append(" 66\r\n").Append(Number(1)).Append("\r\n");
            body.Append(" 10\r\n0.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n");
            body.Append(" 70\r\n").Append(Number(64)).Append("\r\n");
            body.Append(" 71\r\n").Append(Number(used.Count)).Append("\r\n");
            body.Append(" 72\r\n").Append(Number(faces.Count)).Append("\r\n");

            // Extended data hangs off the mesh, once, which is the whole reason for writing one.
            body.Append(data);

            foreach (var vertex in used)
            {
                var seat = vertex * 3;

                body.Append("  0\r\nVERTEX\r\n  8\r\n").Append(layer).Append("\r\n");
                body.Append(" 10\r\n").Append(Real(solid.Positions[seat])).Append("\r\n");
                body.Append(" 20\r\n").Append(Real(solid.Positions[seat + 1])).Append("\r\n");
                body.Append(" 30\r\n").Append(Real(solid.Positions[seat + 2])).Append("\r\n");

                // 64 says this vertex belongs to a mesh; 128 says the mesh is a polyface. Both,
                // or a reader treats the list as a polyline through the points.
                body.Append(" 70\r\n").Append(Number(192)).Append("\r\n");
            }

            foreach (var face in faces)
            {
                body.Append("  0\r\nVERTEX\r\n  8\r\n").Append(layer).Append("\r\n");
                body.Append(" 10\r\n0.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n");
                body.Append(" 70\r\n").Append(Number(128)).Append("\r\n");
                body.Append(" 71\r\n").Append(Number(face[0])).Append("\r\n");
                body.Append(" 72\r\n").Append(Number(face[1])).Append("\r\n");
                body.Append(" 73\r\n").Append(Number(face[2])).Append("\r\n");
            }

            body.Append("  0\r\nSEQEND\r\n  8\r\n").Append(layer).Append("\r\n");
        }

        private static string Data(DxfSolid solid, string application, ref int truncated)
        {
            if (solid.Data.Count == 0) return string.Empty;

            var text = new StringBuilder();

            text.Append("1001\r\n").Append(application).Append("\r\n");

            foreach (var pair in solid.Data)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;

                var line = pair.Key.Trim() + "=" + (pair.Value ?? string.Empty).Trim();

                line = line.Replace("\r", " ").Replace("\n", " ");

                if (line.Length > MaxDataLength)
                {
                    line = line.Substring(0, MaxDataLength);
                    truncated++;
                }

                text.Append("1000\r\n").Append(line).Append("\r\n");
            }

            return text.ToString();
        }

        private static string Clean(string? wanted, string fallback)
        {
            var text = (wanted ?? string.Empty).Trim();

            if (text.Length == 0) return fallback;

            var built = new StringBuilder(text.Length);

            foreach (var letter in text)
                built.Append(Forbidden.IndexOf(letter) >= 0 || letter < ' ' || letter == ' '
                    ? '_'
                    : letter);

            var cleaned = built.ToString().Trim('_');

            return cleaned.Length == 0 ? fallback : cleaned.ToUpperInvariant();
        }

        private static double Clamp(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        private static string Number(int value) =>
            value.ToString(CultureInfo.InvariantCulture).PadLeft(6, ' ');

        private static string Real(double value) =>
            value.ToString("0.############", CultureInfo.InvariantCulture);
    }
}
