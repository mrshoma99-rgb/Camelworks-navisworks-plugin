using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// One clearance run's settings, typed, over the string bag a <see cref="CheckRunSpec"/> holds.
    ///
    /// The bag is where the settings are stored, so a new check adds a run type without a schema
    /// change. Nothing above this parses it: the set-up screen writes through these properties and
    /// the runner reads through them, so the two cannot disagree about what a key means or what
    /// unit a number is in. Every length here is metres.
    ///
    /// A key this build does not know is left in the bag untouched, because
    /// <see cref="Write(CheckRunSpec)"/> sets the keys it owns rather than replacing the bag.
    /// </summary>
    public sealed class ClearanceSettings
    {
        /// <summary>The clear height the design requires, in metres.</summary>
        public const string MinimumKey = "minimum";

        /// <summary>How far under the minimum is worth reporting, in metres.</summary>
        public const string ToleranceKey = "tolerance";

        /// <summary>What the run measures up from.</summary>
        public const string SurfacesKey = "surfaces";

        /// <summary>What the run measures up to.</summary>
        public const string TargetsKey = "targets";

        /// <summary>The levels the run covers, semicolon separated. Empty is every level.</summary>
        public const string LevelsKey = "levels";

        /// <summary>The saved set the run is confined to, or nothing for the whole federation.</summary>
        public const string ZoneKey = "zone";

        /// <summary>Categories kept off the obstruction side, semicolon separated.</summary>
        public const string IgnoreKey = "ignore";

        /// <summary>Whether each surface reports its worst breach only.</summary>
        public const string WorstKey = "worstPerPatch";

        /// <summary>Whether stairs are left out of surface detection.</summary>
        public const string StairsKey = "stairs";

        /// <summary>Whether detection prefers elements whose category names a floor.</summary>
        public const string PreferFloorsKey = "preferFloors";

        /// <summary>The smallest detected surface the run keeps, in square metres.</summary>
        public const string MinAreaKey = "minArea";

        /// <summary>The detected surfaces somebody dropped, by patch id, semicolon separated.</summary>
        public const string ExcludedKey = "excluded";

        /// <summary>What the surface side is set to when detection finds the surfaces.</summary>
        public const string Detected = "Detected walkable surfaces";

        /// <summary>What the surface side is set to when the category names are guessed at.</summary>
        public const string Guessed = "Walking surfaces (a guess)";

        /// <summary>What either side is set to for whatever the host has selected.</summary>
        public const string Selected = "Current selection";

        /// <summary>What either side is set to for everything loaded.</summary>
        public const string Loaded = "Everything loaded";

        /// <summary>What the obstruction side is set to for everything but the surfaces.</summary>
        public const string NotTheSurfaces = "Everything except the surfaces";

        private static readonly char[] Separator = { ';' };

        /// <summary>The clear height the design requires, in metres.</summary>
        public double MinimumMetres { get; set; } = 2.100;

        /// <summary>
        /// How far under the minimum is worth reporting, in metres.
        ///
        /// Distinct from the minimum because they answer different questions: a 2.100 m corridor
        /// measured at 2.098 m is a survey difference and a 2.100 m corridor at 1.850 m is a
        /// coordination issue. A breach shallower than this is not reported.
        /// </summary>
        public double ToleranceMetres { get; set; }

        /// <summary>What the run measures up from.</summary>
        public string Surfaces { get; set; } = Detected;

        /// <summary>What the run measures up to.</summary>
        public string Targets { get; set; } = NotTheSurfaces;

        /// <summary>The levels the run covers. Empty is every level.</summary>
        public IList<string> Levels { get; } = new List<string>();

        /// <summary>The saved set the run is confined to, or null for the whole federation.</summary>
        public string? Zone { get; set; }

        /// <summary>
        /// Categories kept off the obstruction side.
        ///
        /// Without it the first federation run reports every sprinkler head, light fitting and
        /// smoke detector, and the first act on the board is deleting several thousand rows.
        /// </summary>
        public IList<string> Ignore { get; } = new List<string>();

        /// <summary>
        /// Whether each surface reports its worst breach only.
        ///
        /// The right first pass — a slab under a plant room has one lowest thing over it — and the
        /// wrong answer for a coordinator who wants every duct listed, so it is a setting rather
        /// than a fixed reduction.
        /// </summary>
        public bool WorstPerPatch { get; set; } = true;

        /// <summary>
        /// Whether stairs are left out of surface detection.
        ///
        /// Off by default: detection makes one surface per tread and the flight above then reads
        /// as low headroom over the treads below, but a stair with a soffit too low over it is
        /// still worth reporting.
        /// </summary>
        public bool ExcludeStairs { get; set; }

        /// <summary>Whether detection prefers elements whose category names a floor.</summary>
        public bool PreferFloorCategories { get; set; }

        /// <summary>The smallest detected surface the run keeps, in square metres.</summary>
        public double MinPatchAreaSquareMetres { get; set; } = 0.05;

        /// <summary>
        /// The detected surfaces somebody dropped, by patch id.
        ///
        /// In the run's own settings rather than in memory: a patch id is derived from where the
        /// surface is, so it survives a re-run of the same build, and dropping the same forty desk
        /// tops on every run is the work this setting exists to stop.
        /// </summary>
        public ISet<string> Excluded { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when the surfaces are found in the triangles rather than named.</summary>
        public bool IsDetecting =>
            string.Equals(Surfaces, Detected, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// What this run covers, as the rail's one sentence.
        ///
        /// Written here because the board has no way to word one check's settings: it is handed
        /// the sentence and prints it.
        /// </summary>
        public string Between =>
            Said(Surfaces) + " under " + Said(Targets)
            + (Levels.Count == 0 ? string.Empty : ", " + string.Join(", ", Levels))
            + (string.IsNullOrWhiteSpace(Zone) ? string.Empty : ", in " + Zone);

        /// <summary>The threshold, worded, as the rail prints it.</summary>
        public string Threshold =>
            Metres(MinimumMetres) + " m"
            + (ToleranceMetres > 0 ? ", reported under " + Metres(MinimumMetres - ToleranceMetres) + " m"
                                   : string.Empty);

        /// <summary>The height a breach has to be under before it is reported, in metres.</summary>
        public double ReportUnderMetres =>
            ToleranceMetres > 0 ? Math.Max(0, MinimumMetres - ToleranceMetres) : MinimumMetres;

        /// <summary>Read a run's settings.</summary>
        /// <param name="spec">The saved run, or null for the defaults.</param>
        public static ClearanceSettings From(CheckRunSpec? spec)
        {
            var settings = new ClearanceSettings();

            if (spec == null) return settings;

            settings.MinimumMetres = Number(spec, MinimumKey, settings.MinimumMetres);
            settings.ToleranceMetres = Number(spec, ToleranceKey, settings.ToleranceMetres);
            settings.Surfaces = Word(spec, SurfacesKey) ?? settings.Surfaces;
            settings.Targets = Word(spec, TargetsKey) ?? settings.Targets;
            settings.Zone = Word(spec, ZoneKey);
            settings.WorstPerPatch = Yes(spec, WorstKey, settings.WorstPerPatch);
            settings.ExcludeStairs = Yes(spec, StairsKey, settings.ExcludeStairs);
            settings.PreferFloorCategories = Yes(spec, PreferFloorsKey, settings.PreferFloorCategories);
            settings.MinPatchAreaSquareMetres =
                Number(spec, MinAreaKey, settings.MinPatchAreaSquareMetres);

            foreach (var level in Many(spec, LevelsKey)) settings.Levels.Add(level);
            foreach (var category in Many(spec, IgnoreKey)) settings.Ignore.Add(category);
            foreach (var patch in Many(spec, ExcludedKey)) settings.Excluded.Add(patch);

            return settings;
        }

        /// <summary>
        /// Write these settings into a run, leaving every key this build does not own alone.
        /// </summary>
        /// <param name="spec">The saved run.</param>
        public void Write(CheckRunSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            spec.Settings[MinimumKey] = Written(MinimumMetres);
            spec.Settings[ToleranceKey] = Written(ToleranceMetres);
            spec.Settings[SurfacesKey] = Surfaces;
            spec.Settings[TargetsKey] = Targets;
            spec.Settings[LevelsKey] = string.Join(";", Levels);
            spec.Settings[ZoneKey] = Zone;
            spec.Settings[IgnoreKey] = string.Join(";", Ignore);
            spec.Settings[WorstKey] = WorstPerPatch ? "true" : "false";
            spec.Settings[StairsKey] = ExcludeStairs ? "true" : "false";
            spec.Settings[PreferFloorsKey] = PreferFloorCategories ? "true" : "false";
            spec.Settings[MinAreaKey] = Written(MinPatchAreaSquareMetres);
            spec.Settings[ExcludedKey] = string.Join(";", Excluded.OrderBy(id => id, StringComparer.Ordinal));
        }

        /// <summary>True when this level is one the run covers.</summary>
        /// <param name="level">The level a surface stands on, or null when it is not known.</param>
        public bool CoversLevel(string? level)
        {
            if (Levels.Count == 0) return true;

            // An unplaced surface is kept. A level scope narrows what is reported; it is not
            // evidence that a surface nobody could place is outside the scope.
            if (string.IsNullOrWhiteSpace(level)) return true;

            return Levels.Any(one => string.Equals(one, level, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when this category is one the obstruction side leaves out.</summary>
        /// <param name="category">The element's category, or null.</param>
        public bool Ignores(string? category)
        {
            if (Ignore.Count == 0 || string.IsNullOrWhiteSpace(category)) return false;

            return Ignore.Any(one =>
                category!.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string Said(string? side) =>
            string.IsNullOrWhiteSpace(side) ? "everything" : side!;

        private static string Metres(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Written(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        private static string? Word(CheckRunSpec spec, string key) =>
            spec.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value!.Trim()
                : null;

        private static double Number(CheckRunSpec spec, string key, double fallback) =>
            Word(spec, key) is string written
            && double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;

        private static bool Yes(CheckRunSpec spec, string key, bool fallback) =>
            Word(spec, key) is string written
                ? string.Equals(written, "true", StringComparison.OrdinalIgnoreCase)
                : fallback;

        private static IEnumerable<string> Many(CheckRunSpec spec, string key) =>
            (Word(spec, key) ?? string.Empty)
                .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
                .Select(one => one.Trim())
                .Where(one => one.Length > 0);

        /// <inheritdoc />
        public override string ToString() => Between + " at " + Threshold;
    }
}
