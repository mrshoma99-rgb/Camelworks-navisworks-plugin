using System;
using System.Globalization;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// One fall-hazard run's settings, typed, over the string bag a <see cref="CheckRunSpec"/>
    /// holds.
    ///
    /// The same arrangement as <see cref="ClearanceSettings"/> and for the same reasons: the bag is
    /// where a run is stored, nothing above this parses it, and a key this build does not know is
    /// left in the bag untouched. Every length is metres.
    ///
    /// Nine settings, which is why the set-up screen puts them in windows off the action row
    /// rather than laying them beside the run list.
    /// </summary>
    public sealed class FallSettings
    {
        /// <summary>What the run treats as floor.</summary>
        public const string FloorsKey = "floors";

        /// <summary>What the run treats as handrail.</summary>
        public const string HandrailsKey = "handrails";

        /// <summary>What the run treats as an obstruction plugging an opening.</summary>
        public const string ObstructionsKey = "obstructions";

        /// <summary>The elevation the plan is cut at, in metres.</summary>
        public const string LevelKey = "level";

        /// <summary>How far above and below the level an element still counts, in metres.</summary>
        public const string BandKey = "band";

        /// <summary>How wide one grid cell is, in metres.</summary>
        public const string CellKey = "cell";

        /// <summary>The gap that needs a handrail, in metres.</summary>
        public const string LimitKey = "limit";

        /// <summary>How close a handrail must be to protect an edge, in metres.</summary>
        public const string ToleranceKey = "handrailTolerance";

        /// <summary>The shortest break in the protection worth reporting, in metres.</summary>
        public const string PassageKey = "minPassage";

        /// <summary>What a side is set to for whatever the host has selected.</summary>
        public const string Selected = "Current selection";

        /// <summary>What a side is set to for everything loaded.</summary>
        public const string Loaded = "Everything loaded";

        /// <summary>What a side is set to when there is nothing to read for it.</summary>
        public const string Nothing = "Nothing";

        /// <summary>What the run treats as floor. A saved set, the selection, or everything.</summary>
        public string Floors { get; set; } = Loaded;

        /// <summary>
        /// What the run treats as handrail.
        ///
        /// Nothing by default, which is the honest first answer: a run with no handrails named
        /// reports every edge over the limit as unprotected, and that is the state a coordinator
        /// starts from rather than one the check should guess its way out of.
        /// </summary>
        public string Handrails { get; set; } = Nothing;

        /// <summary>What plugs an opening: equipment, ducts, pipes. Nothing by default.</summary>
        public string Obstructions { get; set; } = Nothing;

        /// <summary>The elevation the plan is cut at, in metres.</summary>
        public double LevelMetres { get; set; }

        /// <summary>
        /// How far above and below the level an element still counts, in metres.
        ///
        /// A selection band and not a slice: an element whose box reaches into it contributes its
        /// whole plan silhouette, so this does not have to match a slab thickness. It is also why
        /// a column through the band reads as solid floor, which is written down on
        /// <see cref="CamelWorks.Core.Data.FloorGaps"/> as the largest thing this check gets wrong.
        /// </summary>
        public double BandMetres { get; set; } = 1.0;

        /// <summary>
        /// How wide one grid cell is, in metres.
        ///
        /// Everything thinner than this is a coin flip, so a run judging a 200 mm gap wants a cell
        /// a few times under it.
        /// </summary>
        public double CellMetres { get; set; } = 0.1;

        /// <summary>The gap that needs a handrail, in metres.</summary>
        public double LimitMetres { get; set; } = 0.2;

        /// <summary>How close a handrail must be to a dangerous edge to protect it, in metres.</summary>
        public double HandrailToleranceMetres { get; set; } = 0.3;

        /// <summary>
        /// The shortest unprotected break worth reporting, in metres. Zero reports every break.
        /// </summary>
        public double MinPassageMetres { get; set; }

        /// <summary>True when the run has handrails to read.</summary>
        public bool HasHandrails => Names(Handrails);

        /// <summary>True when the run has obstructions to read.</summary>
        public bool HasObstructions => Names(Obstructions);

        /// <summary>What this run covers, as the rail's one sentence.</summary>
        public string Between =>
            Floors + " at " + Metres(LevelMetres) + " m (±" + Metres(BandMetres) + " m)"
            + (HasHandrails ? ", handrails from " + Handrails : ", no handrails named")
            + (HasObstructions ? ", plugged by " + Obstructions : string.Empty);

        /// <summary>The threshold, worded, as the rail prints it.</summary>
        public string Threshold =>
            Metres(LimitMetres) + " m gap, " + Metres(CellMetres) + " m cells";

        /// <summary>Read a run's settings.</summary>
        /// <param name="spec">The saved run, or null for the defaults.</param>
        public static FallSettings From(CheckRunSpec? spec)
        {
            var settings = new FallSettings();

            if (spec == null) return settings;

            settings.Floors = Word(spec, FloorsKey) ?? settings.Floors;
            settings.Handrails = Word(spec, HandrailsKey) ?? settings.Handrails;
            settings.Obstructions = Word(spec, ObstructionsKey) ?? settings.Obstructions;
            settings.LevelMetres = Number(spec, LevelKey, settings.LevelMetres);
            settings.BandMetres = Number(spec, BandKey, settings.BandMetres);
            settings.CellMetres = Number(spec, CellKey, settings.CellMetres);
            settings.LimitMetres = Number(spec, LimitKey, settings.LimitMetres);
            settings.HandrailToleranceMetres = Number(spec, ToleranceKey, settings.HandrailToleranceMetres);
            settings.MinPassageMetres = Number(spec, PassageKey, settings.MinPassageMetres);

            return settings;
        }

        /// <summary>Write these settings into a run, leaving keys this build does not own alone.</summary>
        /// <param name="spec">The saved run.</param>
        public void Write(CheckRunSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            spec.Settings[FloorsKey] = Floors;
            spec.Settings[HandrailsKey] = Handrails;
            spec.Settings[ObstructionsKey] = Obstructions;
            spec.Settings[LevelKey] = Written(LevelMetres);
            spec.Settings[BandKey] = Written(BandMetres);
            spec.Settings[CellKey] = Written(CellMetres);
            spec.Settings[LimitKey] = Written(LimitMetres);
            spec.Settings[ToleranceKey] = Written(HandrailToleranceMetres);
            spec.Settings[PassageKey] = Written(MinPassageMetres);
        }

        /// <summary>Why this run cannot be run as it stands, or null when it can.</summary>
        public string? Wrong()
        {
            if (!Names(Floors)) return "This run names nothing to read the floor from.";
            if (CellMetres <= 0) return "The cell size must be more than nothing.";
            if (BandMetres <= 0) return "The band must be more than nothing.";
            if (LimitMetres <= 0) return "The gap that needs a handrail must be more than nothing.";

            return null;
        }

        /// <summary>Whether a side names something to read rather than nothing.</summary>
        /// <param name="side">What the side is set to.</param>
        private static bool Names(string? side) =>
            !string.IsNullOrWhiteSpace(side)
            && !string.Equals(side, Nothing, StringComparison.OrdinalIgnoreCase);

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

        /// <inheritdoc />
        public override string ToString() => Between + " at " + Threshold;
    }
}
