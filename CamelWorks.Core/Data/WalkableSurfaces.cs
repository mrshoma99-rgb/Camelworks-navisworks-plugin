using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// What counts as a walkable surface, and what the detection is allowed to lean on.
    ///
    /// Every length is metres. The geometric answer is the default: the two category options are
    /// off unless somebody turns them on, because the model's words about itself are the least
    /// reliable thing in it.
    /// </summary>
    public sealed class WalkableOptions
    {
        /// <summary>The clear height a surface is entitled to, in metres.</summary>
        public double ClearHeightMetres { get; set; } = 2.100;

        /// <summary>How far a surface may tilt from horizontal and still be walked on, in degrees.</summary>
        public double MaxSlopeDegrees { get; set; } = 10.0;

        /// <summary>
        /// The smallest patch worth reporting, in square metres.
        ///
        /// Not a furniture filter, whatever the size suggests. At 0.05 m² a desk top, a worktop,
        /// a window sill and the top of a wide duct all pass, and there is no geometric test that
        /// tells any of them from a floor.
        /// </summary>
        public double MinPatchAreaSquareMetres { get; set; } = 0.05;

        /// <summary>How coarsely triangles are banded by elevation before clustering, in metres.</summary>
        public double ElevationBucketMetres { get; set; } = 0.050;

        /// <summary>
        /// Take surfaces only from elements whose category or type names a floor.
        ///
        /// Off by default so the geometric answer stays the default. Offered because Navisworks
        /// does carry the category and a coordinator who knows their federation is named properly
        /// should be able to say so, not because the names can be trusted in general.
        /// </summary>
        public bool PreferFloorCategories { get; set; }

        /// <summary>
        /// Leave stairs out of detection.
        ///
        /// Off by default. Detection makes one patch per tread and then reports the flight above as
        /// low headroom over the treads below it, which is not a stair headroom check; measuring
        /// along the pitch line is not done yet.
        /// </summary>
        public bool ExcludeStairs { get; set; }

        /// <summary>The words that make a category or type read as a floor.</summary>
        public IList<string> FloorWords { get; } =
            new List<string> { "floor", "slab", "landing", "ramp", "platform", "deck" };

        /// <summary>The words that make a category or type read as a stair.</summary>
        public IList<string> StairWords { get; } = new List<string> { "stair" };

        /// <summary>
        /// What level each element sits on, where the caller knows. A patch takes its level from
        /// its element, and two patches on different levels are never merged by the stacked dedupe.
        /// </summary>
        public IDictionary<ElementKey, string> Levels { get; } = new Dictionary<ElementKey, string>();

        /// <summary>Whether a name reads as a floor.</summary>
        /// <param name="category">The element's category, or null.</param>
        /// <param name="typeName">The element's type, or null.</param>
        public bool NamesAFloor(string? category, string? typeName) => Names(FloorWords, category, typeName);

        /// <summary>Whether a name reads as a stair.</summary>
        /// <param name="category">The element's category, or null.</param>
        /// <param name="typeName">The element's type, or null.</param>
        public bool NamesAStair(string? category, string? typeName) => Names(StairWords, category, typeName);

        private static bool Names(IList<string> words, string? category, string? typeName)
        {
            var one = category ?? string.Empty;
            var two = typeName ?? string.Empty;

            foreach (var word in words)
                if (one.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0
                    || two.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            return false;
        }
    }

    /// <summary>
    /// One contiguous up-facing surface: its own triangles, its own outline, and where its top is.
    /// </summary>
    public sealed class WalkablePatch
    {
        internal WalkablePatch(string id, ElementKey source, string? level, BoundingBox footprint,
                               double topMetres, double areaSquareMetres, TriangleMesh mesh,
                               IReadOnlyList<Tri2> outline, int upAxis)
        {
            Id = id;
            Source = source;
            Level = level;
            Footprint = footprint;
            TopMetres = topMetres;
            AreaSquareMetres = areaSquareMetres;
            Mesh = mesh;
            Outline = outline;
            UpAxis = upAxis;
        }

        /// <summary>
        /// Derived from the level, the top elevation and the footprint, so the same surface in two
        /// runs of the same build has the same id and keeps whatever anybody decided about it.
        /// </summary>
        public string Id { get; }

        /// <summary>The element the surface came off.</summary>
        public ElementKey Source { get; }

        /// <summary>The level, where the caller supplied one.</summary>
        public string? Level { get; }

        /// <summary>The box the patch occupies, seeded from its own vertices.</summary>
        public BoundingBox Footprint { get; }

        /// <summary>The highest point on the surface, along the up axis, in metres.</summary>
        public double TopMetres { get; }

        /// <summary>How much surface there is, in square metres.</summary>
        public double AreaSquareMetres { get; }

        /// <summary>The patch's own triangles.</summary>
        public TriangleMesh Mesh { get; }

        /// <summary>
        /// The patch in plan, triangle by triangle. The clearance prism is extruded from this and
        /// not from <see cref="Footprint"/>: a rectangle over an L-shaped plate or a ring corridor
        /// sweeps in the courtyard, and everything standing in it becomes a finding.
        /// </summary>
        public IReadOnlyList<Tri2> Outline { get; }

        /// <summary>Which world axis was up when this was detected: 0 is X, 1 is Y, 2 is Z.</summary>
        public int UpAxis { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Id + ", " + AreaSquareMetres.ToString("0.00", CultureInfo.InvariantCulture) + " m² at "
            + TopMetres.ToString("0.000", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>
    /// What detection did, and what it could not do — the numbers the set-up screen states before
    /// anybody runs a check on the result.
    /// </summary>
    public sealed class WalkableDiagnostics
    {
        /// <summary>How many elements were read.</summary>
        public int ElementsRead { get; set; }

        /// <summary>How many elements the host would not give triangles for.</summary>
        public int ElementsRefused { get; set; }

        /// <summary>How many clusters were dropped for being smaller than the minimum.</summary>
        public int PatchesUnderMinimumArea { get; set; }

        /// <summary>How many patches the stacked dedupe folded into the one above them.</summary>
        public int PatchesMerged { get; set; }

        /// <summary>Which axis was taken as up: 0 is X, 1 is Y, 2 is Z.</summary>
        public int UpAxis { get; set; } = 2;

        /// <summary>Whether triangles were read, or bounding boxes stood in for them.</summary>
        public bool UsedTriangles { get; set; } = true;

        /// <summary>What the run has to say for itself, in the order it said it.</summary>
        public IList<string> Notes { get; } = new List<string>();

        /// <inheritdoc />
        public override string ToString() =>
            ElementsRead.ToString("N0", CultureInfo.InvariantCulture) + " elements read, "
            + ElementsRefused.ToString("N0", CultureInfo.InvariantCulture) + " refused";
    }

    /// <summary>
    /// Which world axis is up, taken from where the horizontal area is.
    ///
    /// A federation mixes Z-up and Y-up sources, and a check that assumes one of them reports
    /// nothing at all on the other. Its own helper rather than a step inside the detector, because
    /// the answer is worth having anywhere geometry is read.
    /// </summary>
    public static class UpAxis
    {
        /// <summary>
        /// The axis most of the near-horizontal area faces.
        ///
        /// Unsigned: both faces of a slab count, because a model may be wound either way and the
        /// question here is only which axis is vertical. Ties, and a model with no horizontal area
        /// at all, answer Z — the axis nearly every AEC model uses.
        /// </summary>
        /// <param name="meshes">The triangles to look at.</param>
        /// <param name="maxSlopeDegrees">How far from an axis a face may lean and still count.</param>
        /// <param name="areasByAxis">
        /// How much area faced each axis, X, Y then Z. Returned rather than recomputed: the engine
        /// this was ported from walked every triangle of every mesh twice, once to fill this and
        /// once to choose from it.
        /// </param>
        public static int Detect(IEnumerable<TriangleMesh> meshes, double maxSlopeDegrees,
                                 out double[] areasByAxis)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));

            areasByAxis = new double[3];

            var limit = Math.Cos(Math.Max(0, maxSlopeDegrees) * Math.PI / 180.0);

            foreach (var mesh in meshes)
            {
                if (mesh == null) continue;

                for (var t = 0; t < mesh.Indices.Count; t += 3)
                {
                    var raw = WalkableSurfaces.RawNormal(mesh, t, out var area);

                    if (area <= 0) continue;

                    var length = raw.Length;

                    for (var axis = 0; axis < 3; axis++)
                        if (Math.Abs(raw.Axis(axis)) / length >= limit)
                            areasByAxis[axis] += area;
                }
            }

            var best = 2;

            // Seeded at Z and compared with a strict greater-than, so a tie keeps Z.
            for (var axis = 0; axis < 3; axis++)
                if (areasByAxis[axis] > areasByAxis[best]) best = axis;

            return best;
        }
    }

    /// <summary>
    /// Finding the surfaces people stand on, from triangles rather than from what the model calls
    /// itself.
    ///
    /// What it can tell: which way a face points, how much contiguous area it has, and how high it
    /// is. What it cannot tell: a floor from a desk top. Those are the only discriminators there
    /// are, so a worktop, a duct top, a window sill and a plant skid all come back as walkable
    /// surfaces, and the screen that offers this says so in the option's own sentence rather than
    /// in a tooltip.
    ///
    /// Known limits, none of them fixed here:
    ///
    ///   * Elevation bands have hard edges on the triangle centroid, so a flat floor whose
    ///     centroids straddle a boundary comes back as two patches.
    ///   * Clustering is per element, so two abutting slabs are two patches.
    ///   * A grating tessellated as a perforated plate becomes many sub-threshold clusters and
    ///     disappears entirely.
    /// </summary>
    public static class WalkableSurfaces
    {
        /// <summary>How close in elevation two patches must be before one can absorb the other, in metres.</summary>
        public const double StackedToleranceMetres = 0.100;

        /// <summary>How much of the smaller patch must sit under the larger before it is absorbed.</summary>
        public const double StackedOverlapShare = 0.8;

        /// <summary>
        /// Detect the walkable surfaces in a set of meshes.
        /// </summary>
        /// <param name="meshes">
        /// One mesh per element, in metres. Welded again here whatever the caller did, because the
        /// cost is one dictionary pass against a flood fill and the failure it prevents is silent:
        /// unwelded input shares no vertex index, so every cluster is one triangle, every cluster
        /// falls under the minimum area, and a model full of floors reports no surface at all. See
        /// <see cref="TriangleMesh.Weld"/>.
        /// </param>
        /// <param name="options">What counts as walkable.</param>
        /// <param name="diagnostics">Filled in with what the run managed, or null.</param>
        public static IReadOnlyList<WalkablePatch> Detect(
            IEnumerable<KeyValuePair<ElementKey, TriangleMesh>> meshes,
            WalkableOptions options,
            WalkableDiagnostics? diagnostics = null)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var held = new List<KeyValuePair<ElementKey, TriangleMesh>>();

            // Welded here as well as at the seam. Everything below clusters by shared vertex index,
            // so an unwelded mesh detects as nothing at all rather than as something wrong, and a
            // source that forgets would be found on a model rather than in a test.
            foreach (var pair in meshes)
                if (pair.Value != null && pair.Value.Triangles > 0)
                    held.Add(new KeyValuePair<ElementKey, TriangleMesh>(pair.Key, pair.Value.Weld()));

            var bucket = options.ElevationBucketMetres > 0 ? options.ElevationBucketMetres : 0.050;
            var limit = Math.Cos(Math.Max(0, options.MaxSlopeDegrees) * Math.PI / 180.0);

            var meshesOnly = new List<TriangleMesh>(held.Count);
            foreach (var pair in held) meshesOnly.Add(pair.Value);

            var up = UpAxis.Detect(meshesOnly, options.MaxSlopeDegrees, out _);

            if (diagnostics != null) diagnostics.UpAxis = up;

            var patches = new List<WalkablePatch>();
            var dropped = 0;

            foreach (var pair in held)
            {
                var mesh = pair.Value;

                // Up-facing only, and signed. An unsigned test would take a slab's underside as a
                // surface, which is what the whole check is measuring the distance to.
                var walkable = new List<int>();

                for (var t = 0; t < mesh.Indices.Count; t += 3)
                {
                    var raw = RawNormal(mesh, t, out var area);

                    if (area <= 0) continue;

                    if (raw.Axis(up) / raw.Length >= limit) walkable.Add(t);
                }

                if (walkable.Count == 0) continue;

                var banded = new Dictionary<long, List<int>>();

                foreach (var t in walkable)
                {
                    var centre = (Vertex(mesh, mesh.Indices[t]).Axis(up)
                                + Vertex(mesh, mesh.Indices[t + 1]).Axis(up)
                                + Vertex(mesh, mesh.Indices[t + 2]).Axis(up)) / 3.0;

                    var band = Hash.Quantise(centre, bucket);

                    if (!banded.TryGetValue(band, out var list)) banded[band] = list = new List<int>();

                    list.Add(t);
                }

                foreach (var band in banded)
                    foreach (var cluster in Cluster(mesh, band.Value))
                    {
                        var patch = Make(pair.Key, mesh, cluster, up, options);

                        if (patch.AreaSquareMetres < options.MinPatchAreaSquareMetres)
                        {
                            dropped++;
                            continue;
                        }

                        patches.Add(patch);
                    }
            }

            if (diagnostics != null) diagnostics.PatchesUnderMinimumArea += dropped;

            var kept = Dedupe(patches, up, diagnostics);

            return Named(kept);
        }

        /// <summary>
        /// Detect over a scope, reading triangles where the host gives them and boxes where it
        /// does not.
        ///
        /// One walk of the scope, not two: the classification needs each element's category and
        /// type, and a second walk of a federation to fetch them costs as much as the detection.
        /// </summary>
        /// <param name="document">The document to walk.</param>
        /// <param name="triangles">The triangle source. An unavailable one degrades to boxes.</param>
        /// <param name="scope">What to walk.</param>
        /// <param name="metresPerUnit">
        /// How many metres one host unit is, applied to boxes. Triangles arrive in metres already.
        /// </param>
        /// <param name="options">What counts as walkable.</param>
        /// <param name="diagnostics">Filled in with what the run managed, or null.</param>
        /// <param name="onProgress">Called with the running element count; false stops the walk.</param>
        public static IReadOnlyList<WalkablePatch> Read(IModelDocument document,
                                                        ITriangleSource triangles,
                                                        TraversalScope scope,
                                                        double metresPerUnit,
                                                        WalkableOptions options,
                                                        WalkableDiagnostics? diagnostics = null,
                                                        Func<int, bool>? onProgress = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            return Read(document.Traverse(scope), triangles, metresPerUnit, options,
                        diagnostics, onProgress);
        }

        /// <summary>
        /// Detect the walkable surfaces in elements a caller has already picked out.
        ///
        /// The overload a source that is several sets, or whole models, is read through: those
        /// resolve to a stream of elements rather than to one <see cref="TraversalScope"/>.
        /// </summary>
        /// <param name="items">The elements to read triangles from.</param>
        /// <param name="triangles">Where the triangles come from.</param>
        /// <param name="metresPerUnit">How many metres one model unit is.</param>
        /// <param name="options">What counts as walkable.</param>
        /// <param name="diagnostics">Filled in with what the run managed, or null.</param>
        /// <param name="onProgress">Called with how many elements have been read; false stops it.</param>
        public static IReadOnlyList<WalkablePatch> Read(IEnumerable<IModelItem> items,
                                                        ITriangleSource triangles,
                                                        double metresPerUnit,
                                                        WalkableOptions options,
                                                        WalkableDiagnostics? diagnostics = null,
                                                        Func<int, bool>? onProgress = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var scale = metresPerUnit > 0 ? metresPerUnit : 1.0;

            // Asked again inside the walk, not once at the top. A source can only find out that
            // the seat has no primitive reader by being refused, so the whole-seat answer arrives
            // part way through the first sweep and a run that read it once would spend the rest of
            // that sweep counting per-element refusals for a seat-wide fault.
            var reading = triangles.CanReadTriangles;

            var meshes = new List<KeyValuePair<ElementKey, TriangleMesh>>();
            var read = 0;
            var refused = 0;
            var skipped = 0;

            foreach (var item in items)
            {
                if (!item.HasGeometry || item.Key.IsEmpty) continue;

                if (onProgress != null && !onProgress(read)) break;

                if (options.ExcludeStairs && options.NamesAStair(item.Category, item.TypeName))
                {
                    skipped++;
                    continue;
                }

                if (options.PreferFloorCategories && !options.NamesAFloor(item.Category, item.TypeName))
                {
                    skipped++;
                    continue;
                }

                read++;

                TriangleMesh? mesh = null;

                if (reading) reading = triangles.CanReadTriangles;

                if (reading && !triangles.TryRead(item.Key, out mesh)) refused++;

                meshes.Add(new KeyValuePair<ElementKey, TriangleMesh>(
                    item.Key, mesh ?? TopFace(item.Bounds, scale)));
            }

            if (diagnostics != null)
            {
                diagnostics.ElementsRead += read;
                diagnostics.ElementsRefused += refused;
                diagnostics.UsedTriangles = reading;

                // Two different faults and two different sentences. "n elements refused" on a seat
                // that cannot read primitives at all reads as a model problem, and it is not one:
                // everything in the model is then a bounding-box top, including every duct, ceiling
                // and grouping node over the minimum area.
                if (!reading)
                    diagnostics.Notes.Add("No triangles on this seat"
                        + (triangles.Unavailable == null
                            ? "."
                            : ": " + triangles.Unavailable.TrimEnd('.') + ".")
                        + " Every element's bounding-box top was taken as a surface, which is the "
                        + "coarser answer the check gave before.");
                else if (refused > 0)
                    diagnostics.Notes.Add(refused.ToString("N0", CultureInfo.InvariantCulture)
                        + " elements would not give triangles; their bounding-box tops were used "
                        + "instead.");

                if (skipped > 0)
                    diagnostics.Notes.Add(skipped.ToString("N0", CultureInfo.InvariantCulture)
                        + " elements were left out by what their category and type are called.");
            }

            return Detect(meshes, options, diagnostics);
        }

        /// <summary>
        /// The top face of a box, as two triangles wound to face up.
        ///
        /// What an element becomes when the host will not give its triangles. It is the box, so it
        /// is square where the element is not, and it is the check's older and coarser answer.
        /// </summary>
        private static TriangleMesh TopFace(BoundingBox box, double scale)
        {
            var minX = box.MinX * scale;
            var minY = box.MinY * scale;
            var maxX = box.MaxX * scale;
            var maxY = box.MaxY * scale;
            var top = box.MaxZ * scale;

            var positions = new[]
            {
                minX, minY, top,
                maxX, minY, top,
                maxX, maxY, top,
                minX, maxY, top,
            };

            return new TriangleMesh(positions, new[] { 0, 1, 2, 0, 2, 3 });
        }

        /// <summary>The unnormalised normal of one triangle, and the triangle's area.</summary>
        internal static Vec3 RawNormal(TriangleMesh mesh, int at, out double area)
        {
            var a = Vertex(mesh, mesh.Indices[at]);
            var b = Vertex(mesh, mesh.Indices[at + 1]);
            var c = Vertex(mesh, mesh.Indices[at + 2]);

            var raw = b.Less(a).Cross(c.Less(a));
            var length = raw.Length;

            // A tessellation always carries a few triangles with no area. They have no normal, and
            // dividing by their length is how a NaN gets into an area total.
            area = length < 1e-12 ? 0 : length / 2.0;

            return raw;
        }

        private static Vec3 Vertex(TriangleMesh mesh, int index) =>
            new Vec3(mesh.Positions[(index * 3) + 0],
                     mesh.Positions[(index * 3) + 1],
                     mesh.Positions[(index * 3) + 2]);

        /// <summary>
        /// The triangles of one elevation band, split into groups that share vertices.
        ///
        /// Shared vertex index, which is why the seam welds. Two triangles that meet along an edge
        /// but were emitted with their own copies of its two vertices share no index and land in
        /// two clusters.
        /// </summary>
        private static IEnumerable<List<int>> Cluster(TriangleMesh mesh, List<int> triangles)
        {
            var byVertex = new Dictionary<int, List<int>>();

            foreach (var t in triangles)
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = mesh.Indices[t + corner];

                    if (!byVertex.TryGetValue(vertex, out var list)) byVertex[vertex] = list = new List<int>();

                    list.Add(t);
                }

            var seen = new HashSet<int>();

            foreach (var start in triangles)
            {
                if (!seen.Add(start)) continue;

                var cluster = new List<int> { start };
                var pending = new Stack<int>();
                pending.Push(start);

                while (pending.Count > 0)
                {
                    var here = pending.Pop();

                    for (var corner = 0; corner < 3; corner++)
                        foreach (var next in byVertex[mesh.Indices[here + corner]])
                            if (seen.Add(next))
                            {
                                cluster.Add(next);
                                pending.Push(next);
                            }
                }

                yield return cluster;
            }
        }

        /// <summary>One cluster of triangles as a patch, with its own mesh and its own outline.</summary>
        private static WalkablePatch Make(ElementKey source, TriangleMesh mesh, List<int> cluster,
                                          int up, WalkableOptions options)
        {
            var moved = new Dictionary<int, int>();
            var positions = new List<double>();
            var indices = new List<int>();

            var area = 0.0;

            // Seeded from the cluster's own first vertex, never from an empty box at the origin:
            // on a site-coordinate model a box grown from the origin reaches back to it and the
            // footprint covers the whole quadrant. A cluster always holds at least one triangle,
            // so there is always a seed and no zero-box fallback to write.
            var seed = Vertex(mesh, mesh.Indices[cluster[0]]);
            var box = new BoxFit(seed);
            var top = seed.Axis(up);

            foreach (var t in cluster)
            {
                RawNormal(mesh, t, out var one);
                area += one;

                for (var corner = 0; corner < 3; corner++)
                {
                    var original = mesh.Indices[t + corner];

                    if (!moved.TryGetValue(original, out var at))
                    {
                        var point = Vertex(mesh, original);

                        at = positions.Count / 3;
                        moved[original] = at;
                        positions.Add(point.X);
                        positions.Add(point.Y);
                        positions.Add(point.Z);

                        box.Add(point);

                        if (point.Axis(up) > top) top = point.Axis(up);
                    }

                    indices.Add(at);
                }
            }

            var patch = new TriangleMesh(positions, indices);
            var outline = new List<Tri2>(cluster.Count);

            var across = up == 0 ? 1 : 0;
            var along = up == 2 ? 1 : 2;

            for (var t = 0; t < indices.Count; t += 3)
            {
                var a = Vertex(patch, indices[t]);
                var b = Vertex(patch, indices[t + 1]);
                var c = Vertex(patch, indices[t + 2]);

                outline.Add(new Tri2(a.Axis(across), a.Axis(along),
                                     b.Axis(across), b.Axis(along),
                                     c.Axis(across), c.Axis(along)));
            }

            options.Levels.TryGetValue(source, out var level);

            var footprint = box.Box;

            return new WalkablePatch(Identify(level, top, footprint, options.ElevationBucketMetres),
                                     source, level, footprint, top, area, patch, outline, up);
        }

        /// <summary>
        /// A patch's id, derived from where it is rather than from the order it was found in, so
        /// two runs of the same build agree and a decision made about a surface survives a re-run.
        /// </summary>
        private static string Identify(string? level, double top, BoundingBox footprint, double bucket)
        {
            var grid = bucket > 0 ? bucket : 0.050;

            return "p_" + Hash.Of(16,
                level ?? string.Empty,
                Hash.Quantise(top, grid).ToString(CultureInfo.InvariantCulture),
                Hash.Quantise(footprint.MinX, grid).ToString(CultureInfo.InvariantCulture),
                Hash.Quantise(footprint.MinY, grid).ToString(CultureInfo.InvariantCulture),
                Hash.Quantise(footprint.MaxX, grid).ToString(CultureInfo.InvariantCulture),
                Hash.Quantise(footprint.MaxY, grid).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The same patches with what each one actually holds folded into its id.
        ///
        /// Two surfaces can land on one derived id — the same rectangle at the same height on two
        /// elements of the same level — and an id that names two things detaches a decision from
        /// the surface it was made about.
        ///
        /// Appended to every patch, not only to the ones that collided in this run. Appending it on
        /// collision alone makes an id depend on what else the run found: a surface read alone
        /// keeps the short id, the same surface read again beside a new element gets the long one,
        /// and anything keyed on the id — a dropped patch, a status, a comment — stops matching
        /// without a word.
        /// </summary>
        private static IReadOnlyList<WalkablePatch> Named(IReadOnlyList<WalkablePatch> patches)
        {
            var named = new List<WalkablePatch>(patches.Count);

            foreach (var patch in patches)
            {
                var discriminator = Hash.Of(8, patch.Source.ToString(),
                    patch.AreaSquareMetres.ToString("R", CultureInfo.InvariantCulture),
                    patch.TopMetres.ToString("R", CultureInfo.InvariantCulture));

                named.Add(new WalkablePatch(patch.Id + "-" + discriminator, patch.Source, patch.Level,
                                            patch.Footprint, patch.TopMetres, patch.AreaSquareMetres,
                                            patch.Mesh, patch.Outline, patch.UpAxis));
            }

            return named;
        }

        /// <summary>
        /// Drop a patch that is the same surface as one just above it.
        ///
        /// A structural slab and the finish laid on it are two elements and two patches 40 mm
        /// apart, and reporting the clear height over both reports every breach twice. The one
        /// people stand on is the higher one, so the lower is absorbed. Patches on different levels
        /// are never merged.
        /// </summary>
        private static List<WalkablePatch> Dedupe(List<WalkablePatch> patches, int up,
                                                  WalkableDiagnostics? diagnostics)
        {
            // Ordered on the patches themselves rather than on List.Sort's comparison of tops
            // alone. That sort is unstable, so which of two patches at the same top survived
            // depended on the order the elements happened to be enumerated in — and the survivor
            // carries the Source that Focus and the register key on.
            var sorted = new List<WalkablePatch>(patches);

            sorted.Sort((a, b) =>
            {
                var byTop = b.TopMetres.CompareTo(a.TopMetres);
                if (byTop != 0) return byTop;

                // The larger surface absorbs the smaller one, so it is the one to keep.
                var byArea = b.AreaSquareMetres.CompareTo(a.AreaSquareMetres);
                if (byArea != 0) return byArea;

                var byId = string.CompareOrdinal(a.Id, b.Id);

                return byId != 0 ? byId : string.CompareOrdinal(a.Source.ToString(), b.Source.ToString());
            });

            var kept = new List<WalkablePatch>();
            var merged = 0;

            foreach (var patch in sorted)
            {
                var absorbed = false;

                // Backwards: the kept list is in descending order, so its last entry is the one
                // closest above this patch and the gap widens from there. Walking it forwards and
                // stopping at the first entry too far above would stop at the highest patch in the
                // model and never reach the finish 40 mm up.
                for (var i = kept.Count - 1; i >= 0; i--)
                {
                    var above = kept[i];

                    if (above.TopMetres - patch.TopMetres > StackedToleranceMetres) break;

                    if (!string.Equals(above.Level ?? string.Empty, patch.Level ?? string.Empty,
                                       StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (Share(above, patch, up) < StackedOverlapShare) continue;

                    absorbed = true;
                    break;
                }

                if (absorbed) { merged++; continue; }

                kept.Add(patch);
            }

            if (diagnostics != null) diagnostics.PatchesMerged += merged;

            return kept;
        }

        /// <summary>
        /// How much of the smaller patch the larger one is actually over.
        ///
        /// Measured against the outlines and not the plan rectangles. A rectangle is not the shape:
        /// a small patch sitting in the missing corner of an L is wholly inside the L's rectangle
        /// and under none of its surface, and absorbing it drops a real surface from the set.
        ///
        /// The rectangles are still used, as the cheap upper bound that decides whether the outline
        /// comparison is worth doing at all: nothing outside the overlapping rectangle can be
        /// covered, so a bound under the threshold is a definite miss.
        /// </summary>
        /// <param name="one">One patch.</param>
        /// <param name="two">The other.</param>
        /// <param name="up">Which axis is up.</param>
        private static double Share(WalkablePatch one, WalkablePatch two, int up)
        {
            var across = up == 0 ? 1 : 0;
            var along = up == 2 ? 1 : 2;

            var overlap = Overlap(Low(one.Footprint, across), High(one.Footprint, across),
                                  Low(two.Footprint, across), High(two.Footprint, across))
                        * Overlap(Low(one.Footprint, along), High(one.Footprint, along),
                                  Low(two.Footprint, along), High(two.Footprint, along));

            if (overlap <= 0) return 0;

            // In plan throughout, including the areas. The patch's own area is the area of a
            // surface that may be tilted, and comparing a tilted area with a plan overlap would
            // put the two sides of the ratio in different units.
            var areaOne = Plan(one.Outline);
            var areaTwo = Plan(two.Outline);

            var smaller = areaOne <= areaTwo ? one : two;
            var larger = ReferenceEquals(smaller, one) ? two : one;
            var area = Math.Min(areaOne, areaTwo);

            if (area <= 0) return 0;

            // Nothing outside the shared rectangle can be covered, so this is an upper bound on the
            // answer and a cheap way out of an outline comparison that cannot reach the threshold.
            if (overlap / area < StackedOverlapShare) return 0;

            var covered = 0.0;

            foreach (var triangle in smaller.Outline)
            {
                var piece = triangle.Area;

                if (piece <= 0) continue;

                var x = (triangle.Ax + triangle.Bx + triangle.Cx) / 3.0;
                var y = (triangle.Ay + triangle.By + triangle.Cy) / 3.0;

                foreach (var over in larger.Outline)
                    if (over.Holds(x, y)) { covered += piece; break; }
            }

            return covered / area;
        }

        /// <summary>How much ground an outline covers in plan, in square metres.</summary>
        /// <param name="outline">The patch in plan.</param>
        private static double Plan(IReadOnlyList<Tri2> outline)
        {
            var area = 0.0;

            for (var i = 0; i < outline.Count; i++) area += outline[i].Area;

            return area;
        }

        private static double Overlap(double lowOne, double highOne, double lowTwo, double highTwo)
        {
            var shared = Math.Min(highOne, highTwo) - Math.Max(lowOne, lowTwo);
            return shared > 0 ? shared : 0;
        }

        private static double Low(BoundingBox box, int axis) =>
            axis == 0 ? box.MinX : axis == 1 ? box.MinY : box.MinZ;

        private static double High(BoundingBox box, int axis) =>
            axis == 0 ? box.MaxX : axis == 1 ? box.MaxY : box.MaxZ;
    }
}
