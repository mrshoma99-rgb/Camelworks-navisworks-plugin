using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Report;

namespace CamelWorks.Nav
{
    /// <summary>
    /// The one part of comparison that needs the host: a signature of an element's actual
    /// triangles.
    ///
    /// <b>Why it exists.</b> Everything else a comparison knows about shape comes from the
    /// bounding box, which is all Navisworks exposes cheaply — and a box cannot see an element
    /// remodelled inside its own envelope. A 400 mm duct rerouted through the same opening, a
    /// stair rebuilt with different risers between the same two levels, a wall whose openings
    /// moved: same box, same centre, same properties, entirely different thing.
    ///
    /// <b>Why it is off by default.</b> <c>GenerateSimplePrimitives</c> is the only way in, it is a
    /// COM callback per fragment, and on a real federation it is minutes rather than seconds. So
    /// this is something somebody chooses to spend, on a scope they choose, having been told what
    /// it costs — never something a comparison does to them.
    ///
    /// <b>The signature is local and unordered.</b> Local, because a hash of world coordinates
    /// changes when an element is merely moved, and "moved" already has its own answer. Unordered,
    /// because tessellation is free to hand the same triangles over in a different order between
    /// two exports, and an order-sensitive hash would report a model nobody touched as entirely
    /// remodelled.
    /// </summary>
    public sealed class NavCompare
    {
        private readonly NavGeometry _geometry;

        /// <summary>How many elements a deepening pass reads before it stops of its own accord.</summary>
        public const int DefaultCap = 5000;

        /// <summary>Grid the vertices are quantised onto, in metres. A tenth of a millimetre.</summary>
        public const double Grid = 0.0001;

        /// <summary>Wrap a document.</summary>
        /// <param name="document">The open document.</param>
        public NavCompare(NavDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            Document = document;
            _geometry = new NavGeometry(document);
        }

        /// <summary>The document this reads from.</summary>
        public NavDocument Document { get; }

        /// <summary>
        /// One element's shape signature, or null when it carries no triangles.
        /// </summary>
        /// <param name="item">The element, as the seam hands it over.</param>
        public string? Signature(IModelItem? item)
        {
            if (item == null) return null;

            var mesh = _geometry.Read(item, item.DisplayName);

            return mesh == null || !mesh.IsUsable ? null : Signature(mesh);
        }

        /// <summary>
        /// Fill in shape signatures on a snapshot just taken from this document.
        /// </summary>
        /// <param name="snapshot">The snapshot to deepen, taken from this same document.</param>
        /// <param name="only">
        /// The scoped keys worth measuring — see <see cref="ModelCompare.WorthDeepening"/>. Null
        /// measures everything with geometry, which is what taking a baseline wants and what
        /// comparing against one almost never does.
        /// </param>
        /// <param name="cap">How many elements to read before stopping. Zero means all of them.</param>
        /// <param name="keepGoing">Called every few hundred elements; return false to stop.</param>
        /// <param name="onProgress">Called every few hundred elements with the number read.</param>
        /// <returns>How many elements gained a signature.</returns>
        public int Deepen(ModelSnapshot? snapshot,
                          IReadOnlyCollection<string>? only = null,
                          int cap = DefaultCap,
                          Func<bool>? keepGoing = null,
                          Action<int>? onProgress = null)
        {
            if (snapshot == null) return 0;

            var wanted = only == null ? null : new HashSet<string>(only, StringComparer.Ordinal);
            var limit = cap <= 0 ? int.MaxValue : cap;
            var read = 0;

            foreach (var print in snapshot.Elements)
            {
                if (read >= limit) break;
                if (!print.HasGeometry) continue;
                if (wanted != null && !wanted.Contains(print.Scoped)) continue;
                if (!print.TryKey(out var key)) continue;

                var item = Document.Resolve(key);
                if (item == null) continue;

                print.MeshHash = Signature(item);
                read++;

                if (read % 500 != 0) continue;

                onProgress?.Invoke(read);
                if (keepGoing != null && !keepGoing()) break;
            }

            onProgress?.Invoke(read);
            return read;
        }

        /// <summary>
        /// Append the old revision beside the current one, so what was removed can be looked at.
        /// </summary>
        /// <remarks>
        /// <b>Why the other layers do not move when this lands.</b> The document's element index is
        /// first-wins in document order and appended models go to the end, so every key the current
        /// model already has still resolves to the current element. The only keys the old revision
        /// claims are the ones nothing else holds — which is exactly the removed set.
        ///
        /// <c>TryAppendFiles</c> rather than <c>AppendFiles</c>: a file that will not open is
        /// something to say on a line, not an exception to take the pane down with.
        /// </remarks>
        /// <param name="paths">The files to append. See <see cref="Core.Compare.BaselineModels"/>.</param>
        /// <param name="problem">Why it did not work, or null.</param>
        /// <returns>How many models the document gained.</returns>
        public int Bring(IReadOnlyList<string>? paths, out string? problem)
        {
            problem = null;

            var wanted = (paths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (wanted.Count == 0) return 0;

            var before = Document.Models.Count;

            try
            {
                if (!Document.Document.TryAppendFiles(wanted))
                {
                    problem = "Navisworks would not open " + (wanted.Count == 1 ? wanted[0] : "those files")
                              + ". A file on a share that is offline, or one this Navisworks has no "
                              + "reader for, both land here.";
                    return 0;
                }
            }
            catch (Exception e)
            {
                problem = "Navisworks refused the append: " + e.Message;
                return 0;
            }

            Document.Invalidate();

            // Counted from what the document actually holds now rather than from what was asked
            // for: appending an NWF brings in everything it references, and saying "1 model" when
            // eleven arrived would be a number nobody could reconcile with the tree in front of
            // them.
            return Math.Max(0, Document.Models.Count - before);
        }

        /// <summary>
        /// Take back out the models that were brought in, leaving the document as it was.
        /// </summary>
        /// <param name="paths">The files to remove.</param>
        /// <param name="problem">Why some of it did not work, or null.</param>
        /// <returns>How many models the document lost.</returns>
        public int SendBack(IReadOnlyList<string>? paths, out string? problem)
        {
            problem = null;

            var wanted = new HashSet<string>(
                (paths ?? Array.Empty<string>())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(Normalise),
                StringComparer.Ordinal);

            if (wanted.Count == 0) return 0;

            var indices = new List<int>();

            foreach (var source in Document.Models)
            {
                if (!(source is NavModelSource nav)) continue;
                if (!wanted.Contains(Normalise(nav.LoadedPath ?? nav.SourcePath))) continue;

                try
                {
                    var at = Document.Document.Models.IndexOf(nav.Model);
                    if (at >= 0) indices.Add(at);
                }
                catch (Exception)
                {
                    // A model the host has already let go of. Nothing to remove.
                }
            }

            if (indices.Count == 0) return 0;

            var before = Document.Models.Count;
            var refused = 0;

            // Highest index first, because removing one renumbers everything after it and a
            // forwards walk would take out the wrong models from the second removal onward.
            foreach (var at in indices.OrderByDescending(i => i))
            {
                try
                {
                    if (!Document.Document.TryRemoveFile(at)) refused++;
                }
                catch (Exception)
                {
                    refused++;
                }
            }

            Document.Invalidate();

            if (refused > 0)
                problem = "Navisworks kept " + refused.ToString(CultureInfo.InvariantCulture)
                          + (refused == 1 ? " model" : " models")
                          + ". Removing it by hand in the selection tree does the same thing.";

            return Math.Max(0, before - Document.Models.Count);
        }

        private static string Normalise(string? path) =>
            string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path!.Trim().Replace('\\', '/').ToUpperInvariant();

        /// <summary>
        /// The signature of one mesh: every triangle, in the mesh's own coordinates, in no
        /// particular order.
        /// </summary>
        /// <param name="mesh">The triangles, in metres, in world coordinates.</param>
        public static string Signature(GltfMesh mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));

            var positions = mesh.Positions;
            var indices = mesh.Indices;

            if (positions.Count < 9 || indices.Count < 3) return string.Empty;

            Centre(positions, out var cx, out var cy, out var cz);

            ulong total = 0;

            for (var i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = Vertex(positions, indices[i], cx, cy, cz);
                var b = Vertex(positions, indices[i + 1], cx, cy, cz);
                var c = Vertex(positions, indices[i + 2], cx, cy, cz);

                // Sorted within the triangle so that a change of winding or of which corner is
                // listed first — both of which a re-tessellation is free to change — is not a
                // change of shape.
                if (a > b) { var t = a; a = b; b = t; }
                if (b > c) { var t = b; b = c; c = t; }
                if (a > b) { var t = a; a = b; b = t; }

                unchecked
                {
                    var h = 14695981039346656037UL;
                    h = Mix(h, a);
                    h = Mix(h, b);
                    h = Mix(h, c);

                    // Added rather than combined in sequence: the sum of the triangle hashes does
                    // not depend on the order the triangles arrived in, which is the whole point.
                    total += h;
                }
            }

            return total.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static void Centre(IReadOnlyList<float> positions,
                                   out double cx, out double cy, out double cz)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            for (var i = 0; i + 2 < positions.Count; i += 3)
            {
                double x = positions[i], y = positions[i + 1], z = positions[i + 2];

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            cx = (minX + maxX) / 2;
            cy = (minY + maxY) / 2;
            cz = (minZ + maxZ) / 2;
        }

        private static ulong Vertex(IReadOnlyList<float> positions, int index,
                                    double cx, double cy, double cz)
        {
            var at = index * 3;

            if (at < 0 || at + 2 >= positions.Count) return 0;

            unchecked
            {
                var h = 14695981039346656037UL;
                h = Mix(h, (ulong)Quantise(positions[at] - cx));
                h = Mix(h, (ulong)Quantise(positions[at + 1] - cy));
                h = Mix(h, (ulong)Quantise(positions[at + 2] - cz));
                return h;
            }
        }

        private static long Quantise(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? 0
                : (long)Math.Round(value / Grid, MidpointRounding.AwayFromZero);

        private static ulong Mix(ulong hash, ulong value)
        {
            unchecked
            {
                return (hash ^ value) * 1099511628211UL;
            }
        }
    }
}
