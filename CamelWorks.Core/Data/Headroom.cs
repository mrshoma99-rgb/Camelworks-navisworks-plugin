using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Data
{
    /// <summary>One element, as the headroom check sees it. Metres, always.</summary>
    public sealed class HeadroomElement
    {
        /// <summary>Create an element.</summary>
        /// <param name="id">How it will be named in a result.</param>
        /// <param name="name">How it reads.</param>
        public HeadroomElement(string id, string? name = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("id is required", nameof(id)) : id;
            Name = name ?? id;
        }

        /// <summary>Stable identity.</summary>
        public string Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Bounding box, in metres.</summary>
        public double MinX { get; set; }

        /// <summary>Bounding box, in metres.</summary>
        public double MinY { get; set; }

        /// <summary>Bounding box, in metres.</summary>
        public double MinZ { get; set; }

        /// <summary>Bounding box, in metres.</summary>
        public double MaxX { get; set; }

        /// <summary>Bounding box, in metres.</summary>
        public double MaxY { get; set; }

        /// <summary>Bounding box, in metres.</summary>
        public double MaxZ { get; set; }

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>One place where the clear height is less than it should be.</summary>
    public sealed class HeadroomSpan
    {
        internal HeadroomSpan(HeadroomElement floor, HeadroomElement obstruction, double clear,
                              double x, double y, double z)
        {
            Floor = floor; Obstruction = obstruction; Clear = clear; X = x; Y = y; Z = z;
        }

        /// <summary>The walkable surface.</summary>
        public HeadroomElement Floor { get; }

        /// <summary>What is in the way above it.</summary>
        public HeadroomElement Obstruction { get; }

        /// <summary>The clear height, in metres.</summary>
        public double Clear { get; }

        /// <summary>Where to look, in metres — the centre of the overlap.</summary>
        public double X { get; }

        /// <summary>Where to look, in metres.</summary>
        public double Y { get; }

        /// <summary>
        /// The surface itself at that point, in metres.
        ///
        /// So a result is a place rather than a pair of numbers and an implied third. The engine
        /// this was ported from reported which band an element was in and nothing else — no depth,
        /// no clear height, no point — which leaves a list nobody can sort or prioritise.
        /// </summary>
        public double Z { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Clear.ToString("0.000", CultureInfo.InvariantCulture) + " m under "
            + Obstruction.Name + " over " + Floor.Name;
    }

    /// <summary>What the check found.</summary>
    public sealed class HeadroomResult
    {
        internal HeadroomResult(IReadOnlyList<HeadroomSpan> spans, int floors, int obstructions,
                                double minimum)
        {
            Spans = spans; Floors = floors; Obstructions = obstructions; Minimum = minimum;
        }

        /// <summary>Every violation, worst first.</summary>
        public IReadOnlyList<HeadroomSpan> Spans { get; }

        /// <summary>How many walkable surfaces were checked.</summary>
        public int Floors { get; }

        /// <summary>How many things above them were considered.</summary>
        public int Obstructions { get; }

        /// <summary>The clear height required, in metres.</summary>
        public double Minimum { get; }

        /// <summary>The worst clear height found, or null when nothing was below the minimum.</summary>
        public double? Worst => Spans.Count == 0 ? (double?)null : Spans[0].Clear;

        /// <inheritdoc />
        public override string ToString()
        {
            var s = Floors.ToString("N0", CultureInfo.InvariantCulture) + " surfaces against "
                  + Obstructions.ToString("N0", CultureInfo.InvariantCulture) + " elements above them, at "
                  + Minimum.ToString("0.000", CultureInfo.InvariantCulture) + " m";

            return Spans.Count == 0
                ? s + ": nothing below it"
                : s + ": " + Spans.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + " places below it, worst " + Worst!.Value.ToString("0.000", CultureInfo.InvariantCulture) + " m";
        }
    }

    /// <summary>
    /// Clear height between a walkable surface and whatever is above it.
    ///
    /// The check a clash test cannot do. A duct 1.9 m above a corridor floor clashes with nothing —
    /// there is air between them — so no clash test at any tolerance reports it, and the first
    /// anybody hears of it is on site.
    ///
    /// One row per surface, not per pair. A slab under a plant room has a thousand things above it
    /// and one of them is the lowest; reporting all thousand buries the answer. So each surface
    /// contributes its single worst obstruction, which is the one that has to move.
    /// </summary>
    public static class Headroom
    {
        /// <summary>The clear height most codes settle on when nobody states one, in metres.</summary>
        public const double DefaultMinimum = 2.100;

        /// <summary>How big the lookup cells are, in metres. Large enough to be cheap, small enough to prune.</summary>
        public const double DefaultCell = 4.0;

        /// <summary>
        /// Run the check.
        /// </summary>
        /// <param name="floors">The walkable surfaces, in metres.</param>
        /// <param name="obstructions">What might be above them, in metres.</param>
        /// <param name="minimumMetres">The clear height required.</param>
        /// <param name="cellMetres">The lookup cell size.</param>
        public static HeadroomResult Check(IEnumerable<HeadroomElement> floors,
                                           IEnumerable<HeadroomElement> obstructions,
                                           double minimumMetres = DefaultMinimum,
                                           double cellMetres = DefaultCell) =>
            Check(floors, null, obstructions, minimumMetres, cellMetres);

        /// <summary>
        /// Run the check over detected walkable surfaces.
        ///
        /// A patch is a shape, not a rectangle, so the volume swept above it is its own outline
        /// extruded and not its bounding rectangle. On an L-shaped plate, a ring corridor or a
        /// floor around a stair void the rectangle covers ground nobody stands on, and every
        /// column, railing and riser inside it would be reported as low headroom.
        ///
        /// Z-up only, and refused rather than assumed. This maps the patch footprint to plan and
        /// its top to Z, which is wrong for a patch detected with X or Y up:
        /// <see cref="WalkablePatch.UpAxis"/> exists because a federation mixes up-axes, and a
        /// Y-up deck put through here detects normally and then reports no spans at all, with
        /// nothing on screen to say why. The guard is here rather than on a screen so every caller
        /// gets it.
        /// </summary>
        /// <param name="patches">
        /// The detected surfaces. Z-up only: a patch detected with another axis up is refused, for
        /// the reason below.
        /// </param>
        /// <param name="obstructions">What might be above them, in metres.</param>
        /// <param name="minimumMetres">The clear height required.</param>
        /// <param name="cellMetres">The lookup cell size.</param>
        public static HeadroomResult Check(IEnumerable<WalkablePatch> patches,
                                           IEnumerable<HeadroomElement> obstructions,
                                           double minimumMetres = DefaultMinimum,
                                           double cellMetres = DefaultCell)
        {
            if (patches == null) throw new ArgumentNullException(nameof(patches));

            var floors = new List<HeadroomElement>();
            var outlines = new Dictionary<string, IReadOnlyList<Tri2>>(StringComparer.Ordinal);

            foreach (var patch in patches)
            {
                if (patch == null) continue;

                if (patch.UpAxis != 2)
                    throw new ArgumentException(
                        "patch " + patch.Id + " was detected with "
                        + (patch.UpAxis == 0 ? "X" : "Y") + " as its up axis. This measures the "
                        + "footprint in plan and the top along Z, so it would report no spans over "
                        + "that patch rather than failing. Transpose the patch to Z-up first.",
                        nameof(patches));

                floors.Add(new HeadroomElement(patch.Id, Named(patch))
                {
                    MinX = patch.Footprint.MinX, MaxX = patch.Footprint.MaxX,
                    MinY = patch.Footprint.MinY, MaxY = patch.Footprint.MaxY,
                    MinZ = patch.Footprint.MinZ, MaxZ = patch.TopMetres,
                });

                outlines[patch.Id] = patch.Outline;
            }

            return Check(floors, outlines, obstructions, minimumMetres, cellMetres);
        }

        /// <summary>How a detected surface reads in a result.</summary>
        private static string Named(WalkablePatch patch) =>
            (patch.Level == null ? "Surface" : patch.Level + " surface")
            + " " + patch.AreaSquareMetres.ToString("0.0", CultureInfo.InvariantCulture) + " m²";

        private static HeadroomResult Check(IEnumerable<HeadroomElement> floors,
                                            IReadOnlyDictionary<string, IReadOnlyList<Tri2>>? outlines,
                                            IEnumerable<HeadroomElement> obstructions,
                                            double minimumMetres,
                                            double cellMetres)
        {
            if (floors == null) throw new ArgumentNullException(nameof(floors));
            if (obstructions == null) throw new ArgumentNullException(nameof(obstructions));
            if (minimumMetres <= 0) throw new ArgumentOutOfRangeException(nameof(minimumMetres));
            if (cellMetres <= 0) throw new ArgumentOutOfRangeException(nameof(cellMetres));

            var surfaces = floors.ToList();
            var above = obstructions.Where(Finite).ToList();

            // Bucketed by plan position. Without this the check is every surface against every
            // element, which on a federation is tens of billions of comparisons and never finishes.
            var grid = new Dictionary<long, List<HeadroomElement>>();

            foreach (var element in above)
                foreach (var cell in Buckets(element, cellMetres))
                {
                    if (!grid.TryGetValue(cell, out var bucket)) grid[cell] = bucket = new List<HeadroomElement>();
                    bucket.Add(element);
                }

            var spans = new List<HeadroomSpan>();

            foreach (var floor in surfaces)
            {
                if (!Finite(floor)) continue;

                HeadroomElement? worst = null;
                var clear = double.MaxValue;
                double atX = 0, atY = 0;

                var seen = new HashSet<string>(StringComparer.Ordinal);

                IReadOnlyList<Tri2>? outline = null;
                outlines?.TryGetValue(floor.Id, out outline);

                foreach (var candidate in Candidates(floor, grid, cellMetres))
                {
                    if (ReferenceEquals(candidate, floor)) continue;
                    if (string.Equals(candidate.Id, floor.Id, StringComparison.Ordinal)) continue;
                    if (!seen.Add(candidate.Id)) continue;

                    // Strictly above, and only the gap between the floor's top and the element's
                    // underside. An element that starts below the floor's top is through it, which
                    // is a clash and not a headroom problem.
                    //
                    // This is also the whole of the vertical test. An element whose underside is
                    // between the floor's top and the clear height it is entitled to is in that
                    // volume by definition, so a second test against the prism as a range answers
                    // true whenever this line has already let the element through.
                    var gap = candidate.MinZ - floor.MaxZ;
                    if (gap < 0 || gap >= minimumMetres || gap >= clear) continue;

                    var overlapX = Math.Min(floor.MaxX, candidate.MaxX) - Math.Max(floor.MinX, candidate.MinX);
                    var overlapY = Math.Min(floor.MaxY, candidate.MaxY) - Math.Max(floor.MinY, candidate.MinY);

                    if (overlapX <= 0 || overlapY <= 0) continue;

                    var lowX = Math.Max(floor.MinX, candidate.MinX);
                    var highX = Math.Min(floor.MaxX, candidate.MaxX);
                    var lowY = Math.Max(floor.MinY, candidate.MinY);
                    var highY = Math.Min(floor.MaxY, candidate.MaxY);

                    if (outline != null && !Covers(outline, lowX, lowY, highX, highY)) continue;

                    worst = candidate;
                    clear = gap;
                    atX = (lowX + highX) / 2;
                    atY = (lowY + highY) / 2;

                    // The centre of two overlapping rectangles can be off the surface itself — the
                    // missing corner of an L, the courtyard inside a ring. A point nobody can stand
                    // on is a point nobody can go and look at, so it is moved onto the patch.
                    if (outline != null) Onto(outline, lowX, lowY, highX, highY, ref atX, ref atY);
                }

                if (worst != null) spans.Add(new HeadroomSpan(floor, worst, clear, atX, atY, floor.MaxZ));
            }

            spans.Sort((a, b) => a.Clear.CompareTo(b.Clear));

            return new HeadroomResult(spans, surfaces.Count, above.Count, minimumMetres);
        }

        /// <summary>
        /// How many cells an element covers. Capped, because one element with a corrupt bounding
        /// box spanning kilometres would otherwise generate millions of buckets on its own.
        /// </summary>
        private const long Cap = 4096;

        /// <summary>The bucket for elements too large to place in cells at all.</summary>
        private const long Oversized = long.MinValue;

        /// <summary>Whether any triangle of the outline reaches into a plan rectangle.</summary>
        private static bool Covers(IReadOnlyList<Tri2> outline, double minX, double minY,
                                   double maxX, double maxY)
        {
            for (var i = 0; i < outline.Count; i++)
                if (outline[i].Overlaps(minX, minY, maxX, maxY)) return true;

            return false;
        }

        /// <summary>
        /// Move the worst point onto the outline, if it is not already on it.
        ///
        /// The point has to satisfy both halves at once: it must be on the surface, and it must be
        /// under the obstruction. A patch triangle's own centroid satisfies only the first, and on
        /// an L-shaped plate under a duct crossing the missing corner it lands metres away from
        /// anything the duct is over, carrying a clear height that is not the clear height there.
        /// So each triangle is clipped to the overlap rectangle and the largest clipped piece's
        /// centroid is taken: a convex polygon's centroid is inside it, and that polygon is inside
        /// both the triangle and the rectangle.
        ///
        /// Largest rather than first, because the first triangle in list order is an artefact of
        /// how the mesh was tessellated, and a piece with area is a place somebody can stand.
        /// </summary>
        /// <param name="outline">The patch in plan.</param>
        /// <param name="minX">The overlap rectangle, low across.</param>
        /// <param name="minY">The overlap rectangle, low along.</param>
        /// <param name="maxX">The overlap rectangle, high across.</param>
        /// <param name="maxY">The overlap rectangle, high along.</param>
        /// <param name="x">The point, across.</param>
        /// <param name="y">The point, along.</param>
        private static void Onto(IReadOnlyList<Tri2> outline, double minX, double minY,
                                 double maxX, double maxY, ref double x, ref double y)
        {
            for (var i = 0; i < outline.Count; i++)
                if (outline[i].Holds(x, y)) return;

            var bestArea = 0.0;
            double bestX = 0, bestY = 0;

            for (var i = 0; i < outline.Count; i++)
            {
                var triangle = outline[i];

                if (triangle.Area <= 0 || !triangle.Overlaps(minX, minY, maxX, maxY)) continue;

                if (!Clipped(triangle, minX, minY, maxX, maxY, out var area, out var cx, out var cy))
                    continue;

                if (area <= bestArea) continue;

                bestArea = area;
                bestX = cx;
                bestY = cy;
            }

            if (bestArea > 0)
            {
                x = bestX;
                y = bestY;
                return;
            }

            // Nothing clipped to any area. The overlap test accepts a triangle that only touches
            // the rectangle along an edge or a corner, and there is no interior point to take
            // there, so the middle of a triangle the obstruction reaches is what is left.
            for (var i = 0; i < outline.Count; i++)
            {
                var triangle = outline[i];

                if (triangle.Area <= 0 || !triangle.Overlaps(minX, minY, maxX, maxY)) continue;

                x = (triangle.Ax + triangle.Bx + triangle.Cx) / 3.0;
                y = (triangle.Ay + triangle.By + triangle.Cy) / 3.0;
                return;
            }
        }

        /// <summary>
        /// One outline triangle clipped to an axis-aligned rectangle, as an area and a centroid.
        ///
        /// Sutherland-Hodgman against the rectangle's four edges: both shapes are convex, so the
        /// result is one convex polygon and its area centroid is a point inside it. False when the
        /// two share no area.
        /// </summary>
        /// <param name="triangle">The outline triangle.</param>
        /// <param name="minX">The rectangle, low across.</param>
        /// <param name="minY">The rectangle, low along.</param>
        /// <param name="maxX">The rectangle, high across.</param>
        /// <param name="maxY">The rectangle, high along.</param>
        /// <param name="area">How much area is shared.</param>
        /// <param name="x">The centroid, across.</param>
        /// <param name="y">The centroid, along.</param>
        private static bool Clipped(Tri2 triangle, double minX, double minY, double maxX, double maxY,
                                    out double area, out double x, out double y)
        {
            area = 0;
            x = 0;
            y = 0;

            var polygon = new List<double>
            {
                triangle.Ax, triangle.Ay,
                triangle.Bx, triangle.By,
                triangle.Cx, triangle.Cy,
            };

            for (var edge = 0; edge < 4 && polygon.Count > 0; edge++)
                polygon = Clip(polygon, edge, minX, minY, maxX, maxY);

            if (polygon.Count < 6) return false;

            // The shoelace formula, which gives the signed area and the area centroid in one pass.
            var twice = 0.0;
            var sumX = 0.0;
            var sumY = 0.0;

            for (var i = 0; i < polygon.Count; i += 2)
            {
                var nx = polygon[(i + 2) % polygon.Count];
                var ny = polygon[(i + 3) % polygon.Count];

                var cross = (polygon[i] * ny) - (nx * polygon[i + 1]);

                twice += cross;
                sumX += (polygon[i] + nx) * cross;
                sumY += (polygon[i + 1] + ny) * cross;
            }

            area = Math.Abs(twice) / 2.0;

            if (area <= 0) return false;

            x = sumX / (3.0 * twice);
            y = sumY / (3.0 * twice);

            return true;
        }

        /// <summary>
        /// A polygon clipped against one edge of the rectangle, as flat x,y pairs.
        /// </summary>
        /// <param name="polygon">The polygon, as x,y pairs.</param>
        /// <param name="edge">Which edge: 0 low across, 1 high across, 2 low along, 3 high along.</param>
        /// <param name="minX">The rectangle, low across.</param>
        /// <param name="minY">The rectangle, low along.</param>
        /// <param name="maxX">The rectangle, high across.</param>
        /// <param name="maxY">The rectangle, high along.</param>
        private static List<double> Clip(List<double> polygon, int edge,
                                         double minX, double minY, double maxX, double maxY)
        {
            var kept = new List<double>(polygon.Count + 4);

            for (var i = 0; i < polygon.Count; i += 2)
            {
                var cx = polygon[i];
                var cy = polygon[i + 1];
                var px = polygon[(i + polygon.Count - 2) % polygon.Count];
                var py = polygon[(i + polygon.Count - 1) % polygon.Count];

                var inside = Inside(cx, cy, edge, minX, minY, maxX, maxY);
                var was = Inside(px, py, edge, minX, minY, maxX, maxY);

                if (inside != was)
                {
                    var at = Cross(px, py, cx, cy, edge, minX, minY, maxX, maxY);

                    kept.Add(px + ((cx - px) * at));
                    kept.Add(py + ((cy - py) * at));
                }

                if (inside)
                {
                    kept.Add(cx);
                    kept.Add(cy);
                }
            }

            return kept;
        }

        /// <summary>Whether a point is on the inside of one rectangle edge.</summary>
        /// <param name="x">The point, across.</param>
        /// <param name="y">The point, along.</param>
        /// <param name="edge">Which edge.</param>
        /// <param name="minX">The rectangle, low across.</param>
        /// <param name="minY">The rectangle, low along.</param>
        /// <param name="maxX">The rectangle, high across.</param>
        /// <param name="maxY">The rectangle, high along.</param>
        private static bool Inside(double x, double y, int edge,
                                   double minX, double minY, double maxX, double maxY) =>
            edge == 0 ? x >= minX
            : edge == 1 ? x <= maxX
            : edge == 2 ? y >= minY
            : y <= maxY;

        /// <summary>How far along a segment it crosses one rectangle edge, from 0 to 1.</summary>
        /// <param name="px">The segment's start, across.</param>
        /// <param name="py">The segment's start, along.</param>
        /// <param name="cx">The segment's end, across.</param>
        /// <param name="cy">The segment's end, along.</param>
        /// <param name="edge">Which edge.</param>
        /// <param name="minX">The rectangle, low across.</param>
        /// <param name="minY">The rectangle, low along.</param>
        /// <param name="maxX">The rectangle, high across.</param>
        /// <param name="maxY">The rectangle, high along.</param>
        private static double Cross(double px, double py, double cx, double cy, int edge,
                                    double minX, double minY, double maxX, double maxY)
        {
            var along = edge < 2;
            var at = edge == 0 ? minX : edge == 1 ? maxX : edge == 2 ? minY : maxY;

            var from = along ? px : py;
            var to = along ? cx : cy;
            var span = to - from;

            return Math.Abs(span) < 1e-15 ? 0 : (at - from) / span;
        }

        private static bool Finite(HeadroomElement e) =>
            !double.IsNaN(e.MinX) && !double.IsInfinity(e.MinX)
            && !double.IsNaN(e.MinY) && !double.IsInfinity(e.MinY)
            && !double.IsNaN(e.MinZ) && !double.IsInfinity(e.MinZ)
            && !double.IsNaN(e.MaxX) && !double.IsInfinity(e.MaxX)
            && !double.IsNaN(e.MaxY) && !double.IsInfinity(e.MaxY)
            && !double.IsNaN(e.MaxZ) && !double.IsInfinity(e.MaxZ);

        private static IEnumerable<long> Buckets(HeadroomElement element, double cell) =>
            Spread(element, cell) > Cap ? new[] { Oversized } : Cells(element, cell);

        /// <summary>
        /// What a surface has to be compared against.
        ///
        /// The oversized bucket is added to every query, not just to oversized queries. Miss that
        /// and a slab spanning a whole floor plate becomes invisible to every normal element under
        /// it — the exact case headroom exists to catch.
        /// </summary>
        private static IEnumerable<HeadroomElement> Candidates(HeadroomElement floor,
                                                              IReadOnlyDictionary<long, List<HeadroomElement>> grid,
                                                              double cell)
        {
            if (Spread(floor, cell) > Cap)
            {
                foreach (var bucket in grid.Values)
                    foreach (var element in bucket)
                        yield return element;

                yield break;
            }

            foreach (var key in Cells(floor, cell))
                if (grid.TryGetValue(key, out var bucket))
                    foreach (var element in bucket)
                        yield return element;

            if (grid.TryGetValue(Oversized, out var oversized))
                foreach (var element in oversized)
                    yield return element;
        }

        private static long Spread(HeadroomElement element, double cell)
        {
            var x = (long)Math.Floor(element.MaxX / cell) - (long)Math.Floor(element.MinX / cell) + 1;
            var y = (long)Math.Floor(element.MaxY / cell) - (long)Math.Floor(element.MinY / cell) + 1;

            if (x < 1 || y < 1) return Cap + 1;
            if (x > Cap || y > Cap) return Cap + 1;

            return x * y;
        }

        private static IEnumerable<long> Cells(HeadroomElement element, double cell)
        {
            var x0 = (int)Math.Floor(element.MinX / cell);
            var x1 = (int)Math.Floor(element.MaxX / cell);
            var y0 = (int)Math.Floor(element.MinY / cell);
            var y1 = (int)Math.Floor(element.MaxY / cell);

            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    // Packed rather than combined: shifting and XORing two coordinates collides,
                    // and a collision here quietly compares elements that are nowhere near
                    // each other.
                    yield return ((long)x << 32) | (uint)y;
        }
    }
}
