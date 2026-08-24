using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;

namespace CamelWorks.Core.Field
{
    /// <summary>How a centre of gravity was arrived at, which decides how far to trust it.</summary>
    public enum MassBasis
    {
        /// <summary>Nothing could be worked out.</summary>
        None = 0,

        /// <summary>Weighted by a mass the model carries. The only one that is really a centre of gravity.</summary>
        Weight,

        /// <summary>Weighted by enclosed volume, which is right where the assembly is one material.</summary>
        Volume,

        /// <summary>Weighted by surface area, because the geometry does not enclose anything.</summary>
        Surface,
    }

    /// <summary>One element, as the mass sum reads it.</summary>
    public sealed class MassPart
    {
        /// <summary>Measure one.</summary>
        /// <param name="key">Which element.</param>
        /// <param name="name">What it is called.</param>
        /// <param name="positions">Vertices, three numbers each, in metres.</param>
        /// <param name="indices">Triangles, three indices each.</param>
        /// <param name="mass">Its mass in kilograms, where the model says.</param>
        public MassPart(ElementKey key, string? name, IReadOnlyList<float>? positions,
                        IReadOnlyList<int>? indices, double? mass = null)
        {
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name!.Trim();
            Positions = positions ?? Array.Empty<float>();
            Indices = indices ?? Array.Empty<int>();
            Mass = mass;
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; }

        /// <summary>Vertices, three numbers each.</summary>
        public IReadOnlyList<float> Positions { get; }

        /// <summary>Triangles, three indices each.</summary>
        public IReadOnlyList<int> Indices { get; }

        /// <summary>Its mass in kilograms, where the model says.</summary>
        public double? Mass { get; }

        /// <summary>True when there is a whole number of triangles and every index is in range.</summary>
        public bool IsUsable =>
            Positions.Count >= 9 && Positions.Count % 3 == 0
            && Indices.Count >= 3 && Indices.Count % 3 == 0
            && Indices.All(i => i >= 0 && i * 3 + 2 < Positions.Count);

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>Where the weight of something sits, and how sure that is.</summary>
    public sealed class MassResult
    {
        internal MassResult(MassBasis basis, double x, double y, double z, double weight,
                           double volume, double surface, int parts, int unusable, int unweighted,
                           IReadOnlyList<string> notes, string? refusal)
        {
            Basis = basis;
            X = x; Y = y; Z = z;
            Weight = weight;
            Volume = volume;
            Surface = surface;
            Parts = parts;
            Unusable = unusable;
            Unweighted = unweighted;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What it was weighted by.</summary>
        public MassBasis Basis { get; }

        /// <summary>Where it sits, X, in metres.</summary>
        public double X { get; }

        /// <summary>Where it sits, Y, in metres.</summary>
        public double Y { get; }

        /// <summary>Where it sits, Z, in metres.</summary>
        public double Z { get; }

        /// <summary>Total mass in kilograms, where the model said.</summary>
        public double Weight { get; }

        /// <summary>Total enclosed volume in cubic metres.</summary>
        public double Volume { get; }

        /// <summary>Total surface area in square metres.</summary>
        public double Surface { get; }

        /// <summary>How many elements went in.</summary>
        public int Parts { get; }

        /// <summary>How many were handed over with geometry that could not be read.</summary>
        public int Unusable { get; }

        /// <summary>How many carried no mass, on a sum that was weighted by mass.</summary>
        public int Unweighted { get; }

        /// <summary>Anything worth saying about how sure it is.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is no answer, when there is none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is a point to show.</summary>
        public bool IsUsable => Refusal == null && Basis != MassBasis.None;

        /// <summary>
        /// True only when it is really a centre of gravity rather than a centre of something else.
        ///
        /// The distinction that matters on a lift: a centroid weighted by volume is where the
        /// middle of the shape is, and on an assembly of steel and insulation that is not where it
        /// will hang from.
        /// </summary>
        public bool IsTrueCentreOfGravity => Basis == MassBasis.Weight && Unweighted == 0;

        /// <summary>What it found, said plainly.</summary>
        /// <param name="units">How to write the position.</param>
        public string Explain(Measure? units = null)
        {
            if (Refusal != null) return Refusal;

            var where = units == null
                ? Point(X) + ", " + Point(Y) + ", " + Point(Z) + " m"
                : units.Bare(X) + ", " + units.Bare(Y) + ", " + units.Bare(Z)
                  + " " + units.LengthUnit;

            var text = Word(Basis) + " of " + Parts.ToString("N0", CultureInfo.InvariantCulture)
                     + (Parts == 1 ? " element" : " elements") + " is at " + where + ".";

            if (Weight > 0)
                text += " They weigh " + Weight.ToString("N0", CultureInfo.InvariantCulture) + " kg.";

            if (Volume > 0)
                text += " They enclose " + Volume.ToString("0.###", CultureInfo.InvariantCulture) + " m³.";

            if (Unweighted > 0)
                text += " " + Unweighted.ToString("N0", CultureInfo.InvariantCulture)
                     + (Unweighted == 1 ? " element carries" : " elements carry")
                     + " no mass and " + (Unweighted == 1 ? "was" : "were")
                     + " weighted by volume instead, so this is an estimate.";

            if (Unusable > 0)
                text += " " + Unusable.ToString("N0", CultureInfo.InvariantCulture)
                     + (Unusable == 1 ? " element had" : " elements had")
                     + " geometry that could not be read and " + (Unusable == 1 ? "is" : "are")
                     + " not in it.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();

        private static string Word(MassBasis basis) => basis switch
        {
            MassBasis.Weight => "The centre of gravity",
            MassBasis.Volume => "The centre of volume",
            MassBasis.Surface => "The centre of surface",
            _ => "Nothing",
        };

        private static string Point(double metres) =>
            metres.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Where the weight of an assembly sits.
    ///
    /// The question a lift plan is: pick the module, and say where the crane's hook has to be over.
    /// Getting it from the model rather than from a spreadsheet of guesses is the whole value, and
    /// it is arithmetic over the triangles that are already there.
    ///
    /// <b>Three answers, and it says which one it gave.</b> Weighted by a mass the model carries is
    /// the only one that is really a centre of gravity. Weighted by enclosed volume is right when
    /// the assembly is one material and wrong when it is steel bolted to insulation. Weighted by
    /// surface area is the fallback for geometry that does not enclose anything, which in a
    /// federation is most of it - a duct exported as a surface has no inside.
    ///
    /// A centroid that quietly changed basis is the failure this exists to prevent: the number
    /// looks identical either way, and only one of them will hold the load.
    /// </summary>
    public static class MassProperties
    {
        /// <summary>
        /// How small an enclosed volume may be and still be believed, in cubic metres.
        ///
        /// A cubic millimetre. A mesh whose every edge is shared and which still sums to nothing is
        /// degenerate - a solid flattened to a plane - and its centroid means no more than an open
        /// one's.
        /// </summary>
        public const double ClosedVolume = 1e-9;

        /// <summary>
        /// How close two corners have to be, in metres, to be the same corner.
        ///
        /// A micron. Closure is decided by whether every edge is shared, and a host that hands over
        /// one set of vertices per triangle - which Navisworks does - shares no index with itself,
        /// so the edges have to be matched on where they are rather than on what they are called.
        /// </summary>
        public const double SameCorner = 1e-6;

        /// <summary>The property a mass is looked for under when nothing else says.</summary>
        public static IReadOnlyList<string> MassNames { get; } = new[]
        {
            "Mass", "Weight", "Total Weight", "Unit Weight",
        };

        /// <summary>
        /// Work out where the weight sits.
        /// </summary>
        /// <param name="parts">The elements, with their geometry.</param>
        /// <param name="byMass">
        /// Whether to weight by the mass the model carries. Off, and it is a centre of volume -
        /// which is the honest answer when nothing has been told what anything weighs.
        /// </param>
        public static MassResult Of(IEnumerable<MassPart>? parts, bool byMass = true)
        {
            var all = (parts ?? Enumerable.Empty<MassPart>()).Where(p => p != null).ToList();
            var notes = new List<string>();

            if (all.Count == 0)
                return Nothing(notes, "There is nothing selected to weigh. Select the assembly - a "
                    + "module, a lift, a spool - and read it again.");

            var usable = all.Where(p => p.IsUsable).ToList();
            var unusable = all.Count - usable.Count;

            if (usable.Count == 0)
                return Nothing(notes, "None of those " + all.Count.ToString("N0", CultureInfo.InvariantCulture)
                    + " elements handed over geometry that could be read as triangles, so there is "
                    + "nothing to weigh. Grouping nodes and elements the host draws some other way "
                    + "both look like this.");

            var weighted = new List<(double Weight, double X, double Y, double Z)>();

            var totalMass = 0.0;
            var totalVolume = 0.0;
            var totalSurface = 0.0;
            var unweighted = 0;
            var open = 0;

            foreach (var part in usable)
            {
                var body = Body(part);

                totalSurface += body.Surface;

                if (body.Closed) totalVolume += Math.Abs(body.Volume);
                else open++;

                var by = byMass && part.Mass.HasValue && part.Mass.Value > 0
                    ? part.Mass.Value
                    : body.Closed ? Math.Abs(body.Volume) : body.Surface;

                if (byMass && (!part.Mass.HasValue || part.Mass.Value <= 0)) unweighted++;

                if (part.Mass.HasValue && part.Mass.Value > 0) totalMass += part.Mass.Value;

                if (by <= 0) continue;

                weighted.Add((by, body.X, body.Y, body.Z));
            }

            if (weighted.Count == 0)
                return Nothing(notes, "Every one of those elements came out with no size at all - no "
                    + "volume, no area, no mass. There is nothing to take a centre of.");

            var total = weighted.Sum(w => w.Weight);

            var x = weighted.Sum(w => w.Weight * w.X) / total;
            var y = weighted.Sum(w => w.Weight * w.Y) / total;
            var z = weighted.Sum(w => w.Weight * w.Z) / total;

            var basis = byMass && unweighted < usable.Count
                ? MassBasis.Weight
                : open == usable.Count ? MassBasis.Surface : MassBasis.Volume;

            if (open > 0 && open < usable.Count)
                notes.Add(open.ToString("N0", CultureInfo.InvariantCulture)
                          + (open == 1 ? " element does" : " elements do")
                          + " not enclose a volume - exported as surfaces rather than solids - so "
                          + (open == 1 ? "it was" : "they were")
                          + " weighted by area instead. Where they are heavy, this is off.");

            if (basis == MassBasis.Surface)
                notes.Add("Nothing here encloses a volume, so this is the centre of the surfaces "
                          + "and not of the weight. It is a starting point, not a lift plan.");

            // Only meaningful where the sum really was weighted by mass: on a centre of volume,
            // "none of them carried a mass" is the basis, not a caveat on it.
            var missing = basis == MassBasis.Weight ? unweighted : 0;

            return new MassResult(basis, x, y, z, totalMass, totalVolume, totalSurface,
                                  usable.Count, unusable, missing, notes, null);
        }

        /// <summary>
        /// A mass in kilograms out of whatever the model wrote it as.
        ///
        /// A model says "1250 kg", "1.25 t", "2755 lb" and "1250" in four places for the same
        /// column. The last one is the interesting case: a bare number is taken as kilograms,
        /// because that is what a mass property means in every model that has one, and refusing it
        /// would refuse most of them.
        /// </summary>
        /// <param name="text">What the property held.</param>
        /// <param name="kilograms">The mass.</param>
        /// <returns>False when it is not a number at all.</returns>
        public static bool TryMass(string? text, out double kilograms)
        {
            kilograms = 0;

            if (!Quantity.TryParse(text, out var quantity)) return false;

            switch (quantity.Kind)
            {
                case QuantityKind.Mass:
                    kilograms = quantity.Value;
                    return true;

                case QuantityKind.Scalar:
                    kilograms = quantity.Value;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether one mesh encloses anything, and its volume, surface area and centroid.
        ///
        /// The volume is the signed sum of tetrahedra from the origin, which is the standard way
        /// and is only meaningful on a mesh that closes. <b>Whether it closes is a separate
        /// question and is asked separately</b>: every edge shared by exactly two triangles. The
        /// tempting shortcut - "an open mesh sums to about nothing" - is simply false. A single
        /// triangle three metres above the origin encloses two cubic metres of the tetrahedron it
        /// makes with it, and a wall exported as one surface will report a volume with as much
        /// conviction as a solid.
        /// </summary>
        /// <param name="part">The element.</param>
        public static (bool Closed, double Volume, double Surface, double X, double Y, double Z) Body(
            MassPart? part)
        {
            if (part == null || !part.IsUsable) return (false, 0, 0, 0, 0, 0);

            var edges = new Dictionary<(long, long, long, long, long, long), int>();

            var volume = 0.0;
            var vx = 0.0; var vy = 0.0; var vz = 0.0;

            var area = 0.0;
            var ax = 0.0; var ay = 0.0; var az = 0.0;

            for (var t = 0; t + 2 < part.Indices.Count; t += 3)
            {
                var a = part.Indices[t] * 3;
                var b = part.Indices[t + 1] * 3;
                var c = part.Indices[t + 2] * 3;

                double ax1 = part.Positions[a], ay1 = part.Positions[a + 1], az1 = part.Positions[a + 2];
                double bx1 = part.Positions[b], by1 = part.Positions[b + 1], bz1 = part.Positions[b + 2];
                double cx1 = part.Positions[c], cy1 = part.Positions[c + 1], cz1 = part.Positions[c + 2];

                // b x c, then a . (b x c) - six times the signed volume of the tetrahedron the
                // triangle makes with the origin.
                var nx = by1 * cz1 - bz1 * cy1;
                var ny = bz1 * cx1 - bx1 * cz1;
                var nz = bx1 * cy1 - by1 * cx1;

                var six = ax1 * nx + ay1 * ny + az1 * nz;

                volume += six / 6.0;

                // The tetrahedron's own centroid is a quarter of the way along from the origin.
                vx += six * (ax1 + bx1 + cx1) / 4.0;
                vy += six * (ay1 + by1 + cy1) / 4.0;
                vz += six * (az1 + bz1 + cz1) / 4.0;

                // (b - a) x (c - a), whose length is twice the triangle's area.
                var ux = bx1 - ax1; var uy = by1 - ay1; var uz = bz1 - az1;
                var wx = cx1 - ax1; var wy = cy1 - ay1; var wz = cz1 - az1;

                var mx = uy * wz - uz * wy;
                var my = uz * wx - ux * wz;
                var mz = ux * wy - uy * wx;

                var face = Math.Sqrt(mx * mx + my * my + mz * mz) / 2.0;

                area += face;

                ax += face * (ax1 + bx1 + cx1) / 3.0;
                ay += face * (ay1 + by1 + cy1) / 3.0;
                az += face * (az1 + bz1 + cz1) / 3.0;

                Edge(edges, ax1, ay1, az1, bx1, by1, bz1);
                Edge(edges, bx1, by1, bz1, cx1, cy1, cz1);
                Edge(edges, cx1, cy1, cz1, ax1, ay1, az1);
            }

            var closed = edges.Count > 0
                         && edges.Values.All(count => count == 2)
                         && Math.Abs(volume) >= ClosedVolume;

            if (closed)
                return (true, volume, area,
                        vx / (volume * 6.0), vy / (volume * 6.0), vz / (volume * 6.0));

            return area > 0
                ? (false, volume, area, ax / area, ay / area, az / area)
                : (false, volume, area, 0, 0, 0);
        }

        /// <summary>Count one edge, in whichever direction the triangle happened to name it.</summary>
        private static void Edge(IDictionary<(long, long, long, long, long, long), int> edges,
                                 double x1, double y1, double z1, double x2, double y2, double z2)
        {
            var a = (Snap(x1), Snap(y1), Snap(z1));
            var b = (Snap(x2), Snap(y2), Snap(z2));

            // Ordered, so the same edge walked the other way by the neighbouring triangle - which
            // is what a consistently wound closed mesh does - lands on the same key.
            var key = Before(a, b)
                ? (a.Item1, a.Item2, a.Item3, b.Item1, b.Item2, b.Item3)
                : (b.Item1, b.Item2, b.Item3, a.Item1, a.Item2, a.Item3);

            edges[key] = edges.TryGetValue(key, out var seen) ? seen + 1 : 1;
        }

        private static long Snap(double value) => (long)Math.Round(value / SameCorner);

        private static bool Before((long, long, long) a, (long, long, long) b) =>
            a.Item1 != b.Item1 ? a.Item1 < b.Item1
            : a.Item2 != b.Item2 ? a.Item2 < b.Item2
            : a.Item3 < b.Item3;

        private static MassResult Nothing(IReadOnlyList<string> notes, string refusal) =>
            new MassResult(MassBasis.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, notes, refusal);
    }
}
