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
        /// </summary>
        /// <param name="patches">The detected surfaces. Z-up: the patches carry which axis was up.</param>
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

                // The volume this surface is entitled to. An element that never reaches into it
                // cannot lower the clear height over it, whatever its plan position.
                var prism = new Span1(floor.MaxZ, floor.MaxZ + minimumMetres);

                foreach (var candidate in Candidates(floor, grid, cellMetres))
                {
                    if (ReferenceEquals(candidate, floor)) continue;
                    if (string.Equals(candidate.Id, floor.Id, StringComparison.Ordinal)) continue;
                    if (!seen.Add(candidate.Id)) continue;

                    // Strictly above, and only the gap between the floor's top and the element's
                    // underside. An element that starts below the floor's top is through it, which
                    // is a clash and not a headroom problem.
                    var gap = candidate.MinZ - floor.MaxZ;
                    if (gap < 0 || gap >= minimumMetres || gap >= clear) continue;

                    var overlapX = Math.Min(floor.MaxX, candidate.MaxX) - Math.Max(floor.MinX, candidate.MinX);
                    var overlapY = Math.Min(floor.MaxY, candidate.MaxY) - Math.Max(floor.MinY, candidate.MinY);

                    if (overlapX <= 0 || overlapY <= 0) continue;

                    if (!prism.Overlaps(new Span1(candidate.MinZ, candidate.MaxZ))) continue;

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

        /// <summary>Move a point onto the outline, if it is not already on it.</summary>
        private static void Onto(IReadOnlyList<Tri2> outline, double minX, double minY,
                                 double maxX, double maxY, ref double x, ref double y)
        {
            for (var i = 0; i < outline.Count; i++)
                if (outline[i].Holds(x, y)) return;

            // The nearest thing to the overlap that is on the surface: the middle of a patch
            // triangle the obstruction actually passes over.
            for (var i = 0; i < outline.Count; i++)
            {
                var triangle = outline[i];

                if (triangle.Area <= 0 || !triangle.Overlaps(minX, minY, maxX, maxY)) continue;

                x = (triangle.Ax + triangle.Bx + triangle.Cx) / 3.0;
                y = (triangle.Ay + triangle.By + triangle.Cy) / 3.0;
                return;
            }
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
