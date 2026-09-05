using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Data
{
    /// <summary>One level, and the slice of the model it owns.</summary>
    public sealed class LevelBand
    {
        internal LevelBand(string name, double elevation, double top, bool isDerived, int support)
        {
            Name = name; Elevation = elevation; Top = top; IsDerived = isDerived; Support = support;
        }

        /// <summary>Its name.</summary>
        public string Name { get; }

        /// <summary>Its floor level, in model units.</summary>
        public double Elevation { get; }

        /// <summary>Where the next level starts. The last band runs to infinity.</summary>
        public double Top { get; }

        /// <summary>True when the band was inferred from geometry rather than read from the model.</summary>
        public bool IsDerived { get; }

        /// <summary>How many elements sat at this elevation, when the band was derived.</summary>
        public int Support { get; }

        /// <summary>True when the height falls in this band.</summary>
        public bool Contains(double z) => z >= Elevation && z < Top;

        /// <inheritdoc />
        public override string ToString() =>
            Name + " @ " + Elevation.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Where a set of levels came from, best first.</summary>
    public enum LevelSource
    {
        /// <summary>There are none.</summary>
        None = 0,

        /// <summary>
        /// The coordination grid the federation shares.
        ///
        /// The strongest source there is: a grid system is the thing every discipline set out
        /// against, its level names are the ones on the drawings, and nobody has to agree to use
        /// it because they already did.
        /// </summary>
        Grid = 1,

        /// <summary>A Level property the models carry.</summary>
        Property = 2,

        /// <summary>A histogram of where elements actually sit.</summary>
        Inferred = 3,
    }

    /// <summary>
    /// The levels of a federation, whether the model had any or not.
    ///
    /// Most of the product needs a level: the clash board groups on it, reports break down by it,
    /// takeoff subtotals by it. A federated model routinely has none usable — one discipline exports
    /// storeys as properties, one puts them in the tree, one has a single flat model with nothing.
    /// Refusing to work in that case would make the zero-setup rule fail on exactly the federations
    /// that need this product most, so levels are inferred from geometry when they cannot be read.
    ///
    /// Inferred levels are named after their elevation, never numbered. "Level +3.600" is evidently
    /// derived; "Level 2" is a claim about the building, and guessing it wrong is worse than not
    /// guessing, because a report that says Level 2 when the client calls it Level 1 gets the whole
    /// report doubted.
    /// </summary>
    public sealed class LevelSet
    {
        private LevelSet(IReadOnlyList<LevelBand> bands, int elementsConsidered, int discarded,
                         LevelSource source)
        {
            Bands = bands;
            ElementsConsidered = elementsConsidered;
            DiscardedClusters = discarded;
            Source = bands.Count == 0 ? LevelSource.None : source;
        }

        /// <summary>Default clustering resolution in metres — a floor slab's worth.</summary>
        public const double DefaultTolerance = 0.30;

        /// <summary>Default share of elements a candidate level needs before it is believed.</summary>
        public const double DefaultSupportFraction = 0.01;

        /// <summary>The bands, lowest first.</summary>
        public IReadOnlyList<LevelBand> Bands { get; }

        /// <summary>How many elements the derivation looked at.</summary>
        public int ElementsConsidered { get; }

        /// <summary>
        /// How many candidate elevations were rejected for want of support.
        ///
        /// Worth showing. A model whose derivation discards forty clusters is a model with objects
        /// scattered at every height, and its levels are a guess the coordinator should look at
        /// before trusting a report broken down by them.
        /// </summary>
        public int DiscardedClusters { get; }

        /// <summary>True when the levels came from the model rather than from geometry.</summary>
        public bool IsFromModel => Bands.Count > 0 && !Bands[0].IsDerived;

        /// <summary>Which of the three sources produced these.</summary>
        public LevelSource Source { get; }

        /// <summary>
        /// How many distinct floor elevations these bands actually sit at.
        ///
        /// Not the same as the band count, and the difference is a real failure that reached a
        /// user: a coordination grid reported three levels with every elevation at 0.000. Three
        /// bands, one height. Nothing can be grouped by that - every element falls in the first
        /// band - but it counts as three levels to anything that only asks how many there are.
        ///
        /// Grids come out this way when the levels were authored for their names and nobody set
        /// their heights, which is common in a grid exported from a drawing.
        /// </summary>
        public int DistinctElevations =>
            Bands.Select(b => Math.Round(b.Elevation, 3)).Distinct().Count();

        /// <summary>
        /// True when these levels can actually separate a model into storeys.
        ///
        /// One band is a usable answer for a single-storey building. Several bands at one height
        /// is not an answer at all, and is worth rejecting in favour of a source that is honest
        /// about guessing.
        /// </summary>
        public bool Separates => Bands.Count > 0 && (Bands.Count == 1 || DistinctElevations > 1);

        /// <summary>
        /// What the source is, and what it therefore misses.
        ///
        /// Every one of the three is wrong in its own way, and a coordinator reading a report broken
        /// down by level needs to know which way. Naming the source without naming its blind spot
        /// is only half the disclosure.
        /// </summary>
        public string Provenance() => Source switch
        {
            LevelSource.Grid =>
                "Read from the coordination grid. These are the level names on the drawings. Anything "
                + "outside the grid's extent still lands in the nearest band, so a detached plant "
                + "building will be filed under whatever level it is level with.",

            LevelSource.Property =>
                "Read from the models' own Level property. Exactly what each author meant - and each "
                + "author separately, so two disciplines that spell the same storey differently give "
                + "you two levels here.",

            LevelSource.Inferred =>
                "The models carry no grid and no usable level property, so these were inferred from a "
                + "histogram of element base heights. They are named after their elevation and never "
                + "numbered: a level called \"Level +3.600\" is honest about being a guess in a way "
                + "that \"Level 2\" is not.",

            _ => "Nothing loaded carries a grid, a level property, or geometry to infer them from.",
        };

        /// <summary>Nothing at all — a model with no levels and no geometry to guess from.</summary>
        public static LevelSet Empty { get; } =
            new LevelSet(Array.Empty<LevelBand>(), 0, 0, LevelSource.None);

        /// <summary>
        /// The levels the model itself declares.
        /// </summary>
        /// <param name="levels">Name and floor elevation pairs, in any order.</param>
        /// <param name="source">Which source these came from.</param>
        public static LevelSet FromModel(IEnumerable<KeyValuePair<string, double>> levels,
                                         LevelSource source = LevelSource.Property)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));

            var ordered = levels
                .Where(l => !string.IsNullOrWhiteSpace(l.Key))
                .GroupBy(l => l.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, double>(g.Key, g.Min(l => l.Value)))
                .OrderBy(l => l.Value)
                .ThenBy(l => l.Key, StringComparer.Ordinal)
                .ToList();

            return new LevelSet(Band(ordered, derived: false, support: null), 0, 0, source);
        }

        /// <summary>
        /// The levels the coordination grid declares.
        ///
        /// Same shape as <see cref="FromModel"/> and a different provenance, which is the whole
        /// difference: a grid level is one authored fact with one name, where a level property is
        /// one authored fact per model, and three models spelling a storey three ways is a
        /// federation with three levels where the building has one.
        /// </summary>
        /// <param name="levels">Name and elevation pairs from the grid, in any order.</param>
        public static LevelSet FromGrid(IEnumerable<KeyValuePair<string, double>> levels) =>
            FromModel(levels, LevelSource.Grid);

        /// <summary>
        /// Infer the levels from where things actually sit.
        ///
        /// A histogram rather than a clustering pass, for the same reason clash proximity grouping
        /// snaps to a grid: clustering is order-dependent, and two runs over identical data would
        /// produce different levels. Everything downstream — group names, report sections, takeoff
        /// subtotals — is keyed on those names.
        /// </summary>
        /// <param name="baseElevations">The lowest point of each element, in model units.</param>
        /// <param name="tolerance">How far apart two elevations must be to be different levels.</param>
        /// <param name="supportFraction">
        /// The share of elements a candidate level needs before it is believed. Without it, one
        /// mis-placed family at 47.3m becomes a storey, and every report grows a level nobody
        /// recognises.
        /// </param>
        public static LevelSet Derive(IEnumerable<double> baseElevations,
                                      double tolerance = DefaultTolerance,
                                      double supportFraction = DefaultSupportFraction)
        {
            if (baseElevations == null) throw new ArgumentNullException(nameof(baseElevations));
            if (tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be positive");

            var values = baseElevations.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (values.Count == 0) return Empty;

            // Quantise, then count. Snapping to a fixed grid is what makes this reproducible: a
            // tolerance band would let values chain, and which cluster an element joined would
            // depend on the order it arrived in.
            var histogram = new Dictionary<long, int>();
            foreach (var value in values)
            {
                var bin = Hash.Quantise(value, tolerance);
                histogram[bin] = histogram.TryGetValue(bin, out var count) ? count + 1 : 1;
            }

            var threshold = Math.Max(1, (int)Math.Ceiling(values.Count * supportFraction));

            // Merge neighbouring bins before testing support, so a floor whose elements straddle a
            // bin edge is one level with full support rather than two that each fall short.
            var merged = Merge(histogram, tolerance);

            var kept = merged.Where(c => c.Support >= threshold)
                             .OrderBy(c => c.Elevation)
                             .ToList();

            if (kept.Count == 0)
            {
                // Everything was below the threshold, which means the elements are spread evenly
                // rather than stacked in storeys. One band over the whole model is the honest
                // answer; inventing storeys out of noise is not.
                var lowest = values.Min();
                return new LevelSet(
                    new[] { new LevelBand(NameFor(lowest), lowest, double.PositiveInfinity, true, values.Count) },
                    values.Count,
                    merged.Count,
                    LevelSource.Inferred);
            }

            var pairs = kept.Select(c => new KeyValuePair<string, double>(NameFor(c.Elevation), c.Elevation)).ToList();
            var supports = kept.Select(c => c.Support).ToList();

            return new LevelSet(Band(pairs, derived: true, support: supports),
                                values.Count,
                                merged.Count - kept.Count,
                                LevelSource.Inferred);
        }

        /// <summary>
        /// Which level a point is on, or null when it is below the lowest.
        ///
        /// This is what the clash board uses: the level of a clash is the level of the clash point,
        /// not of either element. A riser passing through six storeys clashes on the storey where
        /// the conflict is, and putting it on the storey the pipe starts from sends somebody to the
        /// wrong floor.
        /// </summary>
        public string? LevelAt(double z)
        {
            for (var i = Bands.Count - 1; i >= 0; i--)
                if (z >= Bands[i].Elevation) return Bands[i].Name;

            return null;
        }

        /// <summary>
        /// Which level an element belongs to: the one containing its lowest point.
        ///
        /// A column from L02 to L03 is an L02 column, which is how everybody who has to count them
        /// thinks about it.
        /// </summary>
        public string? LevelOf(double baseZ, double topZ) => LevelAt(Math.Min(baseZ, topZ));

        /// <summary>
        /// Every level an element passes through.
        ///
        /// Reported separately from <see cref="LevelOf"/> because both answers are needed and
        /// neither is the other: a riser is counted once on the level it starts from, and appears
        /// on every level's drawing.
        /// </summary>
        public IReadOnlyList<string> Spans(double baseZ, double topZ)
        {
            var low = Math.Min(baseZ, topZ);
            var high = Math.Max(baseZ, topZ);

            return Bands.Where(b => b.Elevation <= high && b.Top > low).Select(b => b.Name).ToList();
        }

        /// <summary>The one-line readout.</summary>
        public override string ToString()
        {
            if (Bands.Count == 0) return "no levels";

            var s = Bands.Count.ToString(CultureInfo.InvariantCulture)
                  + (Bands.Count == 1 ? " level" : " levels")
                  + Source switch
                    {
                        LevelSource.Grid => " from the coordination grid",
                        LevelSource.Property => " from the models' level property",
                        _ => " inferred from geometry",
                    };

            if (DiscardedClusters > 0)
                s += " · " + DiscardedClusters.ToString(CultureInfo.InvariantCulture)
                   + " candidate elevations discarded for want of support";

            return s;
        }

        // Elevation-derived, never numbered: "Level 2" is a claim about the building, and getting
        // it wrong gets the whole report doubted.
        private static string NameFor(double elevation) =>
            "Level " + (elevation >= 0 ? "+" : "-")
            + Math.Abs(elevation).ToString("0.000", CultureInfo.InvariantCulture);

        private static IReadOnlyList<LevelBand> Band(IReadOnlyList<KeyValuePair<string, double>> ordered,
                                                     bool derived, IReadOnlyList<int>? support)
        {
            var bands = new List<LevelBand>(ordered.Count);

            for (var i = 0; i < ordered.Count; i++)
            {
                var top = i + 1 < ordered.Count ? ordered[i + 1].Value : double.PositiveInfinity;
                bands.Add(new LevelBand(ordered[i].Key, ordered[i].Value, top, derived,
                                        support != null && i < support.Count ? support[i] : 0));
            }

            return bands;
        }

        private static List<(double Elevation, int Support)> Merge(Dictionary<long, int> histogram, double tolerance)
        {
            var clusters = new List<(double Elevation, int Support)>();
            var bins = histogram.Keys.OrderBy(b => b).ToList();

            var i = 0;
            while (i < bins.Count)
            {
                var first = bins[i];
                var last = first;
                var support = histogram[first];

                // Only strictly adjacent bins merge. Anything further apart is a different level,
                // however sparse — otherwise a tall building merges into one band from the bottom up.
                while (i + 1 < bins.Count && bins[i + 1] == last + 1)
                {
                    i++;
                    last = bins[i];
                    support += histogram[last];
                }

                // The floor of the merged run, so a level sits at the lowest elevation its elements
                // were found at rather than at their average — a slab top is a real height, an
                // average of a slab and its screed is not.
                clusters.Add((first * tolerance, support));
                i++;
            }

            return clusters;
        }
    }
}
