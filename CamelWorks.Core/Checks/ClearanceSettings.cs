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

        /// <summary>Whether the walkable surfaces are found inside the source rather than taken whole.</summary>
        public const string DetectKey = "detect";

        /// <summary>What the run measures up to.</summary>
        public const string TargetsKey = "targets";

        /// <summary>The levels the run covers, semicolon separated. Empty is every level.</summary>
        public const string LevelsKey = "levels";

        /// <summary>The saved set the run is confined to, or nothing for the whole federation.</summary>

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

        /// <summary>How far a detected surface may tilt from horizontal, in degrees.</summary>
        public const string MaxSlopeKey = "maxSlope";

        /// <summary>The detected surfaces somebody dropped, by patch id, semicolon separated.</summary>
        public const string ExcludedKey = "excluded";

        /// <summary>
        /// What the surface side was set to when this run last measured.
        ///
        /// Written by the runner rather than by this screen, and read by nothing else. A detected
        /// surface is keyed by where it is and a named one by its element, so changing the side
        /// re-derives every finding under the run: the run says so in its notes, and this is what
        /// it compares against. Not a setting anybody edits, which is why it has no property here.
        /// </summary>
        public const string LastSurfacesKey = "lastSurfaces";

        /// <summary>What either side is set to for whatever the host has selected.</summary>
        public const string Selected = CheckSource.TheSelection;

        /// <summary>What either side is set to for everything loaded.</summary>
        public const string Loaded = CheckSource.Everywhere;

        /// <summary>What the obstruction side is set to for everything but the surfaces.</summary>
        public const string NotTheSurfaces = CheckSource.Remainder;

        private static readonly char[] Separator = { ';' };

        /// <summary>The clear height the design requires, in metres.</summary>
        public double MinimumMetres { get; set; } = 2.100;

        /// <summary>
        /// How far under the minimum is worth reporting, in metres.
        ///
        /// Distinct from the minimum because they answer different questions: a 2.100 m corridor
        /// measured at 2.098 m is a survey difference and a 2.100 m corridor at 1.850 m is a
        /// coordination issue. A breach shallower than this is not reported.
        ///
        /// 25 mm by default rather than zero. At zero a 2.099 m soffit is a row, and the first run
        /// on a federation is thousands of them; at 25 mm every shortfall anyone argues about is
        /// still reported. Set it to zero to report all of them.
        /// </summary>
        public double ToleranceMetres { get; set; } = 0.025;

        /// <summary>
        /// What the run measures up from: saved sets, whole models, the selection, or everything
        /// loaded.
        ///
        /// A source, never a mode. Detection refines what is named here rather than replacing it,
        /// so naming "Level 03 Floors" and leaving <see cref="DetectSurfaces"/> on sweeps that set
        /// for walkable surfaces instead of sweeping the federation and discarding the set.
        /// </summary>
        public CheckSource Surfaces { get; set; } = CheckSource.Everything();

        /// <summary>
        /// Whether the walkable surfaces are found in the triangles of the source.
        ///
        /// On by default, which is the answer for a source that is not already a floors set: taking
        /// every element in "Everything loaded" as a surface to measure from is not a headroom
        /// check. Turn it off when the source names the slabs, so their whole top faces are used.
        /// </summary>
        public bool DetectSurfaces { get; set; } = true;

        /// <summary>What the run measures up to.</summary>
        public CheckSource Targets { get; set; } = CheckSource.Rest();

        /// <summary>The levels the run covers. Empty is every level.</summary>
        public IList<string> Levels { get; } = new List<string>();

        /// <summary>
        /// Categories kept off the obstruction side.
        ///
        /// Without it the first federation run reports every sprinkler head, light fitting and
        /// smoke detector, and the first act on the board is deleting several thousand rows. So the
        /// three that cost the most start in the list, matched as substrings of the category name
        /// so they hold across authoring tools, and the set-up screen shows them rather than
        /// applying them out of sight.
        /// </summary>
        public IList<string> Ignore { get; } = new List<string>(Unreported);

        /// <summary>
        /// The categories a new run leaves off the obstruction side.
        ///
        /// Substrings, not exact names: "Lighting" catches "Lighting Fixtures" and "Lighting
        /// Devices", "Sprinkler" catches "Sprinklers", "Fire Alarm" catches the detector families.
        /// </summary>
        public static IReadOnlyList<string> Unreported { get; } =
            new[] { "Sprinkler", "Lighting", "Fire Alarm" };

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
        /// How far a detected surface may tilt from horizontal and still be walked on, in degrees.
        ///
        /// 10 degrees covers a floor laid to falls and a level-access ramp. It was fixed at that
        /// and never shown, so a car-park ramp at 1:5 (11.3 degrees) was not a walkable surface and
        /// the envelope over the steepest ramp in the building went unchecked with no note.
        /// </summary>
        public double MaxSlopeDegrees { get; set; } = 10.0;

        /// <summary>
        /// The detected surfaces somebody dropped, by patch id.
        ///
        /// In the run's own settings rather than in memory: a patch id is derived from where the
        /// surface is, so it survives a re-run of the same build, and dropping the same forty desk
        /// tops on every run is the work this setting exists to stop.
        /// </summary>
        public ISet<string> Excluded { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>True when the surfaces are found in the triangles rather than taken whole.</summary>
        public bool IsDetecting => DetectSurfaces;

        /// <summary>
        /// What this run covers, as the rail's one sentence.
        ///
        /// Written here because the board has no way to word one check's settings: it is handed
        /// the sentence and prints it.
        /// </summary>
        public string Between =>
            (DetectSurfaces ? "walkable surfaces in " + Surfaces.Said() : Surfaces.Said())
            + " under " + Targets.Said()
            + (Levels.Count == 0 ? string.Empty : ", " + string.Join(", ", Levels));

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
            settings.Surfaces = CheckSource.Read(Word(spec, SurfacesKey), settings.Surfaces);
            settings.DetectSurfaces = Yes(spec, DetectKey, settings.DetectSurfaces);
            settings.Targets = CheckSource.Read(Word(spec, TargetsKey), settings.Targets);
            settings.WorstPerPatch = Yes(spec, WorstKey, settings.WorstPerPatch);
            settings.ExcludeStairs = Yes(spec, StairsKey, settings.ExcludeStairs);
            settings.PreferFloorCategories = Yes(spec, PreferFloorsKey, settings.PreferFloorCategories);
            settings.MinPatchAreaSquareMetres =
                Number(spec, MinAreaKey, settings.MinPatchAreaSquareMetres);
            settings.MaxSlopeDegrees = Number(spec, MaxSlopeKey, settings.MaxSlopeDegrees);

            foreach (var level in Many(spec, LevelsKey)) settings.Levels.Add(level);

            // The ignore list starts populated, so a saved run has to be able to say "none": the
            // key being present at all means this run's list is what was saved, empty included.
            if (spec.Settings.ContainsKey(IgnoreKey)) settings.Ignore.Clear();

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
            spec.Settings[SurfacesKey] = Surfaces.Written();
            spec.Settings[DetectKey] = DetectSurfaces ? "true" : "false";
            spec.Settings[TargetsKey] = Targets.Written();
            spec.Settings[LevelsKey] = string.Join(";", Levels);
            spec.Settings[IgnoreKey] = string.Join(";", Ignore);
            spec.Settings[WorstKey] = WorstPerPatch ? "true" : "false";
            spec.Settings[StairsKey] = ExcludeStairs ? "true" : "false";
            spec.Settings[PreferFloorsKey] = PreferFloorCategories ? "true" : "false";
            spec.Settings[MinAreaKey] = Written(MinPatchAreaSquareMetres);
            spec.Settings[MaxSlopeKey] = Written(MaxSlopeDegrees);
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
