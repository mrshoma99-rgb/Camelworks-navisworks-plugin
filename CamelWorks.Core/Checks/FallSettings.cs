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
    /// and does not lay them beside the run list.
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

        /// <summary>The storey the elevation was taken from, when it was picked from the list.</summary>
        public const string LevelNameKey = "levelName";

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

        /// <summary>
        /// What the floor side was set to when this run last swept.
        ///
        /// Written by the runner and read by nothing else, the same arrangement as
        /// <see cref="ClearanceSettings.LastSurfacesKey"/>. A hazard's id is derived from the run,
        /// the cell it sits in and the elements around it, so changing the floor side re-derives
        /// every finding under the run and detaches the status, the assignee, the comments and any
        /// issue link. Changing the level does the same. The run says so on the next sweep.
        /// </summary>
        public const string LastFloorsKey = "lastFloors";

        /// <summary>What a side is set to for whatever the host has selected.</summary>
        public const string Selected = "Current selection";

        /// <summary>What a side is set to for everything loaded.</summary>
        public const string Loaded = "Everything loaded";

        /// <summary>What a side is set to when there is nothing to read for it.</summary>
        public const string Nothing = "Nothing";

        /// <summary>
        /// What the run treats as floor. A saved set, the selection, or everything.
        ///
        /// Nothing by default, so the run refuses rather than reading the whole federation at the
        /// cut elevation: every wall, duct and column through the band projects its own outline as
        /// though it were floor, and the plan that produces is not the floor. Naming the floors is
        /// the one thing this check cannot guess, which is why it is asked for rather than assumed.
        /// </summary>
        public string Floors { get; set; } = Nothing;

        /// <summary>
        /// What the run treats as handrail. Nothing by default.
        ///
        /// A run with no handrails named reports every edge over the limit as unprotected, which
        /// is the state a model without modelled rails is actually in. Guessing at a rail set
        /// would report edges as protected on no evidence.
        /// </summary>
        public string Handrails { get; set; } = Nothing;

        /// <summary>What plugs an opening: equipment, ducts, pipes. Nothing by default.</summary>
        public string Obstructions { get; set; } = Nothing;

        private double _levelMetres;

        /// <summary>
        /// The elevation the plan is cut at, in metres.
        ///
        /// Setting it marks the run as having been told where to cut. There is no safe default: on
        /// a model in site or shared coordinates, zero is below the foundations, the band catches
        /// nothing and the run returns nothing at all. So the run is refused until this is set
        /// rather than silently swept at zero.
        /// </summary>
        public double LevelMetres
        {
            get => _levelMetres;
            set { _levelMetres = value; HasLevel = true; }
        }

        /// <summary>True when the run has been told what elevation to cut the plan at.</summary>
        public bool HasLevel { get; private set; }

        /// <summary>
        /// The storey the elevation was taken from, or null when it was typed.
        ///
        /// Kept so the picker can show which storey is selected, and so a run reads as "Level 03"
        /// rather than as 10.5. The elevation stays the authority: a storey whose height is edited
        /// does not silently move a run that was already swept.
        /// </summary>
        public string? LevelName { get; set; }

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
        ///
        /// A person's width, not a void's: a 40 mm break between two rails is not somewhere anyone
        /// falls through, and at zero every one of them is a row.
        /// </summary>
        public double MinPassageMetres { get; set; } = 0.3;

        /// <summary>True when the run names something to read the floor from.</summary>
        public bool HasFloors => Names(Floors);

        /// <summary>True when the run has handrails to read.</summary>
        public bool HasHandrails => Names(Handrails);

        /// <summary>True when the run has obstructions to read.</summary>
        public bool HasObstructions => Names(Obstructions);

        /// <summary>What this run covers, as the rail's one sentence.</summary>
        public string Between =>
            Floors + " at " + Cut + " (±" + Metres(BandMetres) + " m)"
            + (HasHandrails ? ", handrails from " + Handrails : ", no handrails named")
            + (HasObstructions ? ", plugged by " + Obstructions : string.Empty);

        /// <summary>Where the plan is cut, as a storey name when there is one and a height when not.</summary>
        public string Cut =>
            string.IsNullOrWhiteSpace(LevelName)
                ? Metres(LevelMetres) + " m"
                : LevelName + " (" + Metres(LevelMetres) + " m)";

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
            if (Word(spec, LevelKey) is string cut
                && double.TryParse(cut, NumberStyles.Float, CultureInfo.InvariantCulture, out var metres))
                settings.LevelMetres = metres;

            settings.LevelName = Word(spec, LevelNameKey);
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

            // Left out entirely when nothing has set it, so "no elevation" survives a save and a
            // reload rather than reading back as a deliberate zero.
            if (HasLevel) spec.Settings[LevelKey] = Written(LevelMetres);

            spec.Settings[LevelNameKey] = LevelName;
            spec.Settings[BandKey] = Written(BandMetres);
            spec.Settings[CellKey] = Written(CellMetres);
            spec.Settings[LimitKey] = Written(LimitMetres);
            spec.Settings[ToleranceKey] = Written(HandrailToleranceMetres);
            spec.Settings[PassageKey] = Written(MinPassageMetres);
        }

        /// <summary>Why this run cannot be run as it stands, or null when it can.</summary>
        public string? Wrong()
        {
            if (!Names(Floors))
                return "This run names nothing to read the floor from. Name a set of floors or "
                       + "slabs: read from everything loaded, a wall or a duct through the band "
                       + "projects its outline as though it were floor.";

            if (!HasLevel)
                return "This run does not say what elevation the plan is cut at. Pick the storey.";

            if (CellMetres <= 0) return "The cell size must be more than nothing.";
            if (BandMetres <= 0) return "The band must be more than nothing.";
            if (LimitMetres <= 0) return "The gap that needs a handrail must be more than nothing.";

            return null;
        }

        /// <summary>Whether a side names something to read.</summary>
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
