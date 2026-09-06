using System;
using System.Collections.Generic;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Abstractions
{
    /// <summary>
    /// World-space triangles in metres, as a service sees them.
    ///
    /// Neutral on purpose: not the report writer's mesh type. A mesh that crosses this seam is
    /// read by the clearance check and the fall-hazard raster, neither of which writes a file, and
    /// tying the geometry seam to an export type would put a format's decisions inside a check.
    ///
    /// Positions are three doubles per vertex; indices are three per triangle and address
    /// vertices, not doubles.
    ///
    /// Doubles the whole way, with no float door in. These are world coordinates: at site
    /// coordinates a float ulp is about 31 mm, which is coarser than the 0.1 mm weld tolerance and
    /// the 50 mm elevation bucket that surface detection works to, so a mesh that has been through
    /// float cannot be welded or banded reliably on the models the site-coordinate rules exist for.
    /// A host that marshals floats widens each coordinate as it collects it.
    /// </summary>
    public sealed class TriangleMesh
    {
        private BoundingBox? _bounds;

        /// <summary>Create a mesh.</summary>
        /// <param name="positions">Vertex positions, three doubles each, in metres.</param>
        /// <param name="indices">Triangle indices into <paramref name="positions"/>, three per triangle.</param>
        public TriangleMesh(IReadOnlyList<double> positions, IReadOnlyList<int> indices)
        {
            Positions = positions ?? throw new ArgumentNullException(nameof(positions));
            Indices = indices ?? throw new ArgumentNullException(nameof(indices));

            if (positions.Count % 3 != 0)
                throw new ArgumentException("positions must be three doubles per vertex", nameof(positions));

            if (indices.Count % 3 != 0)
                throw new ArgumentException("indices must be three per triangle", nameof(indices));

            var vertices = positions.Count / 3;

            for (var i = 0; i < indices.Count; i++)
                if (indices[i] < 0 || indices[i] >= vertices)
                    throw new ArgumentException("an index addresses a vertex that is not there", nameof(indices));
        }

        /// <summary>An empty mesh, for an element the host read but that carries no triangles.</summary>
        public static TriangleMesh Empty { get; } =
            new TriangleMesh(Array.Empty<double>(), Array.Empty<int>());

        /// <summary>Vertex positions, three doubles each, in metres.</summary>
        public IReadOnlyList<double> Positions { get; }

        /// <summary>Triangle indices, three per triangle.</summary>
        public IReadOnlyList<int> Indices { get; }

        /// <summary>How many vertices.</summary>
        public int Vertices => Positions.Count / 3;

        /// <summary>How many triangles.</summary>
        public int Triangles => Indices.Count / 3;

        /// <summary>
        /// The mesh's own box, seeded from the first vertex and grown from there.
        ///
        /// Never a default box grown by containment: that starts at the world origin, and on a
        /// site-coordinate model the box then reaches back to it and covers the whole quadrant.
        /// For the same reason an empty mesh has no box rather than a zero box at the origin —
        /// a zero box at the origin is a place, and every containment test against it answers
        /// about that place. Ask <see cref="Vertices"/> first.
        /// </summary>
        /// <exception cref="InvalidOperationException">The mesh has no vertices to seed a box from.</exception>
        public BoundingBox Bounds
        {
            get
            {
                if (_bounds.HasValue) return _bounds.Value;

                if (Vertices == 0)
                    throw new InvalidOperationException("an empty mesh has no box: it is not a zero "
                        + "box at the world origin, which is a place a containment test answers about");

                var box = new BoxFit(Positions[0], Positions[1], Positions[2]);

                for (var v = 1; v < Vertices; v++)
                    box.Add(Positions[(v * 3) + 0], Positions[(v * 3) + 1], Positions[(v * 3) + 2]);

                _bounds = box.Box;
                return _bounds.Value;
            }
        }

        /// <summary>
        /// The same triangles with coincident vertices merged and the indices rewritten.
        ///
        /// Mandatory before any connectivity work, not an optimisation. The host emits a fresh
        /// vertex for every corner of every triangle, and the walkable-surface flood fill walks
        /// shared vertex indices: fed an unwelded mesh no two triangles ever share an index, every
        /// cluster is one triangle, every cluster falls under the minimum patch area, and a model
        /// full of floors reports no walkable surface at all.
        ///
        /// Vertices are snapped onto a grid of <paramref name="toleranceMetres"/> and merged by
        /// cell. Two vertices a hair apart across a cell boundary are left separate; that is a
        /// known limit of grid welding and it does not affect the case this exists for, where the
        /// duplicates carry bit-identical coordinates. Triangles that collapse to a line or a
        /// point once merged are dropped.
        /// </summary>
        /// <param name="toleranceMetres">How close two vertices must be to become one.</param>
        public TriangleMesh Weld(double toleranceMetres = 0.0001)
        {
            if (toleranceMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(toleranceMetres), "the tolerance must be positive");

            if (Vertices == 0) return this;

            var moved = new int[Vertices];
            var cells = new Dictionary<string, int>(StringComparer.Ordinal);
            var positions = new List<double>();

            for (var v = 0; v < Vertices; v++)
            {
                var x = Positions[(v * 3) + 0];
                var y = Positions[(v * 3) + 1];
                var z = Positions[(v * 3) + 2];

                var cell = Hash.Quantise(x, toleranceMetres) + "," + Hash.Quantise(y, toleranceMetres)
                         + "," + Hash.Quantise(z, toleranceMetres);

                if (cells.TryGetValue(cell, out var already))
                {
                    moved[v] = already;
                    continue;
                }

                moved[v] = positions.Count / 3;
                cells[cell] = moved[v];
                positions.Add(x);
                positions.Add(y);
                positions.Add(z);
            }

            var indices = new List<int>(Indices.Count);

            for (var t = 0; t < Indices.Count; t += 3)
            {
                var a = moved[Indices[t]];
                var b = moved[Indices[t + 1]];
                var c = moved[Indices[t + 2]];

                if (a == b || b == c || a == c) continue;

                indices.Add(a);
                indices.Add(b);
                indices.Add(c);
            }

            return new TriangleMesh(positions, indices);
        }

        /// <inheritdoc />
        public override string ToString() => Vertices + " vertices, " + Triangles + " triangles";
    }

    /// <summary>
    /// Triangles for an element, behind the seam.
    ///
    /// Availability is declared, as it is on <see cref="IClashSource"/>: the host's primitive
    /// reader can refuse one element and it can refuse a whole seat, and a check that has to
    /// null-check before every use will forget once. A caller that finds
    /// <see cref="CanReadTriangles"/> false degrades to <see cref="IModelItem.Bounds"/> and says
    /// on screen that it did.
    /// </summary>
    public interface ITriangleSource
    {
        /// <summary>
        /// False when this seat cannot produce triangles at all, as against one element the host
        /// refused.
        ///
        /// Whole-seat, and it is the answer a caller changes its behaviour on: a caller that finds
        /// this false takes bounding boxes for every element and says on screen that it did. An
        /// implementation that answers true on a seat where every element refuses reports "n
        /// elements refused" instead, which reads as a model problem rather than a seat problem.
        /// </summary>
        bool CanReadTriangles { get; }

        /// <summary>Why triangles are unavailable, or null when they are available.</summary>
        string? Unavailable { get; }

        /// <summary>
        /// One element's triangles.
        ///
        /// Two obligations on an implementation:
        ///
        ///   * False means the host refused the element, and only that. An element that carries no
        ///     triangles — a grouping node, a line, an annotation — was read and answers true with
        ///     <see cref="TriangleMesh.Empty"/>. A source that answers false for both makes the
        ///     caller degrade every such element to its bounding box, so every duct, ceiling and
        ///     grouped node over the minimum area becomes a detected walkable surface.
        ///   * A mesh handed over is welded (<see cref="TriangleMesh.Weld"/>). Everything
        ///     downstream clusters by shared vertex index and a host emits a fresh vertex per
        ///     triangle corner, so an unwelded mesh detects as nothing at all. Detection welds
        ///     again defensively; that does not make this optional, because a source is read by
        ///     more than the detector.
        /// </summary>
        /// <param name="key">Which element.</param>
        /// <param name="mesh">The triangles, or null when the host refused.</param>
        bool TryRead(ElementKey key, out TriangleMesh? mesh);

        /// <summary>
        /// A scope, streamed rather than materialised, so a federation sweep reports progress and
        /// can be stopped.
        ///
        /// Same two obligations as <see cref="TryRead"/>: what is yielded is welded, and an element
        /// the host refused is left out rather than yielded empty.
        /// </summary>
        /// <param name="scope">What to walk.</param>
        /// <param name="onProgress">
        /// Called with the running count of elements walked; false stops the walk. The count is a
        /// liveness figure and not a total.
        /// </param>
        IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> Read(TraversalScope scope, Func<int, bool>? onProgress);
    }
}
