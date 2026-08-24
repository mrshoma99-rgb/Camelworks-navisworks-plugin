using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Data;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Field
{
    /// <summary>What a measured row is a measurement of.</summary>
    public enum DimensionKind
    {
        /// <summary>The longest way through one element.</summary>
        Run = 0,

        /// <summary>The second longest, square to the run.</summary>
        Across,

        /// <summary>The shortest, square to both.</summary>
        Through,

        /// <summary>Corner to corner.</summary>
        Diagonal,

        /// <summary>How far the underside of an element sits above the level it stands on.</summary>
        Height,

        /// <summary>Middle of one element to the middle of another.</summary>
        Centres,

        /// <summary>The gap between two elements, face to face.</summary>
        Clear,

        /// <summary>The step from one element to the next along a run of them.</summary>
        Spacing,
    }

    /// <summary>Which direction a measurement between two elements was taken along.</summary>
    public enum Along
    {
        /// <summary>Whichever way the elements spread furthest. What a run of anything is ordered on.</summary>
        Longest = 0,

        /// <summary>The model's X. East, on a model that is square to the site grid.</summary>
        X,

        /// <summary>The model's Y. North, on a model that is square to the site grid.</summary>
        Y,

        /// <summary>Height. Level is level whatever the model is rotated to, because a project angle turns about Z.</summary>
        Level,
    }

    /// <summary>How far through an element it is, in each of its own three directions.</summary>
    public sealed class Extent
    {
        internal Extent(ElementKey key, string label, bool oriented, bool axisUncertain,
                        double run, double across, double through,
                        double runX, double runY, double runZ)
        {
            Key = key;
            Label = label;
            Oriented = oriented;
            AxisUncertain = axisUncertain;
            Run = run;
            Across = across;
            Through = through;
            RunX = runX;
            RunY = runY;
            RunZ = runZ;
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>What it is called.</summary>
        public string Label { get; }

        /// <summary>
        /// True when this came off the element's own triangles, false when it is the world box.
        ///
        /// The whole difference between a length and a guess. A brace on the diagonal has a box
        /// half again as long as the brace, and a schedule that does not say which of the two it
        /// measured is a schedule nobody can order steel from.
        /// </summary>
        public bool Oriented { get; }

        /// <summary>True when nothing about the element says which way it runs - a cube, a sphere, a blob.</summary>
        public bool AxisUncertain { get; }

        /// <summary>The longest way through it, in metres.</summary>
        public double Run { get; }

        /// <summary>The second longest, square to the run, in metres.</summary>
        public double Across { get; }

        /// <summary>The shortest, square to both, in metres.</summary>
        public double Through { get; }

        /// <summary>Corner to corner, in metres.</summary>
        public double Diagonal =>
            Math.Sqrt(Run * Run + Across * Across + Through * Through);

        /// <summary>Which way the run points: X of a unit vector.</summary>
        public double RunX { get; }

        /// <summary>Which way the run points: Y of a unit vector.</summary>
        public double RunY { get; }

        /// <summary>Which way the run points: Z of a unit vector.</summary>
        public double RunZ { get; }

        /// <summary>True when the run is level to within a degree, which is what a pipe or a duct usually is.</summary>
        public bool IsFlat => Math.Abs(RunZ) <= 0.0175;

        /// <summary>True when the run is plumb to within a degree, which is what a column usually is.</summary>
        public bool IsPlumb => Math.Abs(RunZ) >= 0.9998;

        /// <summary>How far off level the run is, in degrees, signed up-positive.</summary>
        public double PitchDegrees =>
            Math.Asin(Math.Max(-1.0, Math.Min(1.0, RunZ))) * 180.0 / Math.PI;

        /// <inheritdoc />
        public override string ToString() =>
            Label + " " + Run.ToString("0.###", CultureInfo.InvariantCulture)
            + " x " + Across.ToString("0.###", CultureInfo.InvariantCulture)
            + " x " + Through.ToString("0.###", CultureInfo.InvariantCulture)
            + (Oriented ? " m" : " m (box)");
    }

    /// <summary>How far apart two elements are.</summary>
    public sealed class Separation
    {
        internal Separation(ElementKey from, string fromLabel, ElementKey to, string toLabel,
                            double centres, double plan, double level, double clear, bool overlaps)
        {
            From = from;
            FromLabel = fromLabel;
            To = to;
            ToLabel = toLabel;
            Centres = centres;
            Plan = plan;
            Level = level;
            Clear = clear;
            Overlaps = overlaps;
        }

        /// <summary>The element measured from.</summary>
        public ElementKey From { get; }

        /// <summary>What it is called.</summary>
        public string FromLabel { get; }

        /// <summary>The element measured to.</summary>
        public ElementKey To { get; }

        /// <summary>What it is called.</summary>
        public string ToLabel { get; }

        /// <summary>Middle to middle, in metres.</summary>
        public double Centres { get; }

        /// <summary>Middle to middle ignoring height - what a dimension on a plan reads, in metres.</summary>
        public double Plan { get; }

        /// <summary>How much higher the second element's middle is than the first, in metres. Signed.</summary>
        public double Level { get; }

        /// <summary>
        /// The gap between them, face to face, in metres. Zero when they touch or overlap.
        ///
        /// Taken between the two world boxes, so it is the honest answer for anything box-shaped
        /// and an under-estimate of the real gap for anything that is not - a box never sits
        /// inside its contents. Under-estimating a clearance is the safe direction to be wrong in
        /// and is why it is taken this way round.
        /// </summary>
        public double Clear { get; }

        /// <summary>True when the two boxes are into one another, so there is no gap to report.</summary>
        public bool Overlaps { get; }

        /// <inheritdoc />
        public override string ToString() =>
            FromLabel + " to " + ToLabel + " "
            + Centres.ToString("0.###", CultureInfo.InvariantCulture) + " m centres, "
            + (Overlaps ? "overlapping"
                        : Clear.ToString("0.###", CultureInfo.InvariantCulture) + " m clear");
    }

    /// <summary>A run of elements, and the step from each to the next.</summary>
    public sealed class SpacingRun
    {
        internal SpacingRun(Along axis, IReadOnlyList<Separation> steps, string? refusal)
        {
            Axis = axis;
            Steps = steps;
            Refusal = refusal;
        }

        /// <summary>Which direction they were put in order along.</summary>
        public Along Axis { get; }

        /// <summary>Each element to the next one along.</summary>
        public IReadOnlyList<Separation> Steps { get; }

        /// <summary>Why there is no run, when there is none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is a run worth reading.</summary>
        public bool IsUsable => Refusal == null && Steps.Count > 0;

        /// <summary>The closest two consecutive elements get, in metres.</summary>
        public double Least => Steps.Count == 0 ? 0 : Steps.Min(s => s.Centres);

        /// <summary>The furthest, in metres.</summary>
        public double Most => Steps.Count == 0 ? 0 : Steps.Max(s => s.Centres);

        /// <summary>The average step, in metres.</summary>
        public double Mean => Steps.Count == 0 ? 0 : Steps.Average(s => s.Centres);

        /// <summary>
        /// How far the worst step is off the average, as a fraction of it.
        ///
        /// The number that says whether a run of hangers is at centres or just roughly in a line.
        /// </summary>
        public double Drift
        {
            get
            {
                var mean = Mean;

                if (Steps.Count == 0 || mean <= Dimensions.Tiny) return 0;

                return Steps.Max(s => Math.Abs(s.Centres - mean)) / mean;
            }
        }

        /// <inheritdoc />
        public override string ToString() =>
            Refusal ?? (Steps.Count.ToString("N0", CultureInfo.InvariantCulture)
                        + " steps, " + Mean.ToString("0.###", CultureInfo.InvariantCulture)
                        + " m average");
    }

    /// <summary>One line of a dimension schedule.</summary>
    public sealed class DimensionRow
    {
        internal DimensionRow(string reference, DimensionKind kind, string subject, string? against,
                              Along axis, double metres, bool oriented)
        {
            Reference = reference;
            Kind = kind;
            Subject = subject;
            Against = against;
            Axis = axis;
            Metres = metres;
            Oriented = oriented;
        }

        /// <summary>What the schedule calls this line. Unique within one run.</summary>
        public string Reference { get; }

        /// <summary>What is being measured.</summary>
        public DimensionKind Kind { get; }

        /// <summary>The element it is a measurement of.</summary>
        public string Subject { get; }

        /// <summary>The element it is a measurement to, where there is one.</summary>
        public string? Against { get; }

        /// <summary>Which direction it was taken along.</summary>
        public Along Axis { get; }

        /// <summary>The measurement, in metres. Signed only where the kind is signed.</summary>
        public double Metres { get; }

        /// <summary>True when it came off real geometry rather than the world box.</summary>
        public bool Oriented { get; }

        /// <summary>The measurement, in whatever the project reads in.</summary>
        /// <param name="units">The project's unit formatter.</param>
        public string Say(Measure? units) => (units ?? Measure.Metric).Length(Metres);

        /// <inheritdoc />
        public override string ToString() =>
            Reference + " " + Dimensions.Word(Kind) + " " + Subject
            + (Against == null ? string.Empty : " to " + Against) + " "
            + Metres.ToString("0.###", CultureInfo.InvariantCulture) + " m";
    }

    /// <summary>What to measure.</summary>
    public sealed class DimensionRecipe
    {
        /// <summary>How many rows one run will produce before it stops.</summary>
        public const int DefaultCeiling = 20000;

        /// <summary>How many elements the nearest-neighbour pass will look at before it gives up.</summary>
        public const int NeighbourCeiling = 2500;

        /// <summary>Start one. Everything it needs it already has.</summary>
        /// <param name="name">What to call it.</param>
        public DimensionRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Dimensions" : name!.Trim();
        }

        /// <summary>What to call it.</summary>
        public string Name { get; set; }

        /// <summary>What goes in front of every reference.</summary>
        public string Prefix { get; set; } = "D";

        /// <summary>Measure how big each element is. On, because it needs nothing and always says something.</summary>
        public bool Sizes { get; set; } = true;

        /// <summary>Include the corner-to-corner as well as the three sides.</summary>
        public bool Diagonals { get; set; }

        /// <summary>Measure how far each element sits above the level it stands on.</summary>
        public bool Heights { get; set; } = true;

        /// <summary>Measure each element to the nearest other one in the selection.</summary>
        public bool Neighbours { get; set; }

        /// <summary>Measure the step from each element to the next along a run.</summary>
        public bool Spacing { get; set; }

        /// <summary>Which direction the run is ordered along.</summary>
        public Along SpacingAxis { get; set; } = Along.Longest;

        /// <summary>How many decimal places the schedule is written to.</summary>
        public int Decimals { get; set; } = 3;

        /// <summary>How many rows before it stops.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>True when nothing at all has been asked for.</summary>
        public bool IsEmpty => !Sizes && !Heights && !Neighbours && !Spacing;

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public DimensionRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("name", Name)
                .Set("prefix", Prefix)
                .Set("sizes", Sizes)
                .Set("diagonals", Diagonals)
                .Set("heights", Heights)
                .Set("neighbours", Neighbours)
                .Set("spacing", Spacing)
                .Set("spacingAxis", SpacingAxis.ToString())
                .Set("decimals", Decimals)
                .Set("ceiling", Ceiling);

        /// <summary>Read one back. An unreadable one comes back measuring sizes and heights, which is the default.</summary>
        /// <param name="json">What was written.</param>
        public static DimensionRecipe FromJson(JsonValue? json)
        {
            var recipe = new DimensionRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            var prefix = (json["prefix"].AsString() ?? string.Empty).Trim();

            recipe.Prefix = prefix.Length == 0 ? "D" : prefix;
            recipe.Sizes = json["sizes"].AsBool(true);
            recipe.Diagonals = json["diagonals"].AsBool(false);
            recipe.Heights = json["heights"].AsBool(true);
            recipe.Neighbours = json["neighbours"].AsBool(false);
            recipe.Spacing = json["spacing"].AsBool(false);

            recipe.SpacingAxis =
                Enum.TryParse<Along>(json["spacingAxis"].AsString() ?? string.Empty, out var axis)
                    ? axis
                    : Along.Longest;

            var decimals = (int)json["decimals"].AsLong();

            recipe.Decimals = decimals >= 0 && decimals <= 6 ? decimals : 3;

            var ceiling = (int)json["ceiling"].AsLong();

            recipe.Ceiling = ceiling > 0 ? ceiling : DefaultCeiling;

            // Every switch off is a schedule with no rows in it and no way for the screen to say
            // why, so an empty recipe comes back as the default rather than as nothing at all.
            if (recipe.IsEmpty) { recipe.Sizes = true; recipe.Heights = true; }

            return recipe;
        }

        /// <summary>What it will measure, in words, short enough to sit on a heading.</summary>
        public string Says
        {
            get
            {
                var parts = new List<string>();

                if (Sizes) parts.Add(Diagonals ? "sizes and diagonals" : "sizes");
                if (Heights) parts.Add("heights above level");
                if (Neighbours) parts.Add("nearest neighbour");
                if (Spacing) parts.Add("spacing along " + Dimensions.Word(SpacingAxis));

                return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
            }
        }

        /// <inheritdoc />
        public override string ToString() => Name + " — " + Says;
    }

    /// <summary>The schedule, and what could not be measured.</summary>
    public sealed class DimensionSchedule
    {
        internal DimensionSchedule(DimensionRecipe recipe, IReadOnlyList<DimensionRow> rows,
                                   IReadOnlyList<Extent> extents, int elements, int bodiless,
                                   int boxed, int dropped, IReadOnlyList<string> notes,
                                   string? refusal)
        {
            Recipe = recipe;
            Rows = rows;
            Extents = extents;
            Elements = elements;
            Bodiless = bodiless;
            Boxed = boxed;
            Dropped = dropped;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What was asked for.</summary>
        public DimensionRecipe Recipe { get; }

        /// <summary>The schedule.</summary>
        public IReadOnlyList<DimensionRow> Rows { get; }

        /// <summary>What each element measured, kept alongside the rows for a screen to sort on.</summary>
        public IReadOnlyList<Extent> Extents { get; }

        /// <summary>How many elements were measured.</summary>
        public int Elements { get; }

        /// <summary>How many carried no body at all and were passed over.</summary>
        public int Bodiless { get; }

        /// <summary>How many were measured off the world box because their triangles were not read.</summary>
        public int Boxed { get; }

        /// <summary>How many rows did not fit under the ceiling.</summary>
        public int Dropped { get; }

        /// <summary>Anything worth saying about the schedule.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there is no schedule, when there is none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is a schedule worth writing out.</summary>
        public bool IsUsable => Refusal == null && Rows.Count > 0;

        /// <summary>What it measured, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            var text = Rows.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Rows.Count == 1 ? " dimension off " : " dimensions off ")
                     + Elements.ToString("N0", CultureInfo.InvariantCulture)
                     + (Elements == 1 ? " element." : " elements.");

            if (Boxed > 0)
                text += " " + Boxed.ToString("N0", CultureInfo.InvariantCulture) + " of them "
                     + (Boxed == 1 ? "was" : "were")
                     + " measured off the world box rather than off geometry, so anything on the "
                     + "diagonal reads longer than it is.";

            if (Bodiless > 0)
                text += " " + Bodiless.ToString("N0", CultureInfo.InvariantCulture)
                     + (Bodiless == 1 ? " element has" : " elements have")
                     + " no body to measure and " + (Bodiless == 1 ? "was" : "were")
                     + " passed over.";

            if (Dropped > 0)
                text += " " + Dropped.ToString("N0", CultureInfo.InvariantCulture)
                     + " more rows did not fit under the ceiling.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The model, measured.
    ///
    /// Somebody asks how long that run is, how far it sits off the slab, whether those hangers are
    /// at centres. The answer is in the model and the usual way of getting it out is to draw a
    /// measurement on the screen, read it, and type it into an email - once per question, with a
    /// transcription error waiting at the end of each one. This asks the question of every element
    /// at once and puts the answers in a table that goes on a report.
    ///
    /// <b>It measures the body, not the box, wherever the body has been read.</b> A world box is
    /// the cheapest thing to get off a Navisworks element and it is right for anything that is
    /// square to the model. It is badly wrong for anything that is not: a brace on the diagonal has
    /// a box a good deal longer than the brace. So where the triangles have been read this works
    /// out which way the element actually runs and measures along that, and where they have not it
    /// says so on the row rather than quietly handing back the box.
    ///
    /// <b>It measures; it does not judge.</b> There is a clearance checker elsewhere that says
    /// whether a gap is big enough. This says what the gap is. The two are deliberately not the
    /// same tool, because a schedule that has already made up its mind is no use to the person who
    /// wants to see the numbers.
    /// </summary>
    public static class Dimensions
    {
        /// <summary>Where saved dimension runs live in the project store.</summary>
        public const string StoreKey = "dimensions";

        /// <summary>Anything smaller than this is treated as zero, in metres.</summary>
        public const double Tiny = 1e-9;

        /// <summary>
        /// How much longer the run has to be than the next direction before it is worth calling
        /// a run at all, as a fraction. Below it, nothing about the element says which way it goes.
        /// </summary>
        public const double RunMargin = 0.02;

        /// <summary>How many rounds the run direction is looked for over.</summary>
        public const int Rounds = 64;

        /// <summary>How far through an element it is, in each of its own three directions.</summary>
        /// <param name="element">The element to measure.</param>
        public static Extent ExtentOf(FieldElement? element)
        {
            if (element == null)
                return new Extent(default, "(nothing)", false, true, 0, 0, 0, 1, 0, 0);

            return element.HasBody ? FromBody(element) : FromBox(element);
        }

        /// <summary>How far apart two elements are.</summary>
        /// <param name="from">The element measured from.</param>
        /// <param name="to">The element measured to.</param>
        public static Separation Between(FieldElement? from, FieldElement? to)
        {
            if (from == null || to == null)
                return new Separation(default, "(nothing)", default, "(nothing)", 0, 0, 0, 0, false);

            var a = from.Bounds;
            var b = to.Bounds;

            var dx = b.CentreX - a.CentreX;
            var dy = b.CentreY - a.CentreY;
            var dz = b.CentreZ - a.CentreZ;

            // Gap per axis: how far the boxes are apart along it, or zero where they already
            // overlap on it. The three together give the true face-to-face distance, and boxes
            // that overlap on every axis are into one another and have no gap at all.
            var gx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            var gy = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            var gz = Math.Max(0, Math.Max(a.MinZ - b.MaxZ, b.MinZ - a.MaxZ));

            var overlaps = gx <= Tiny && gy <= Tiny && gz <= Tiny && a.Intersects(b);

            return new Separation(
                from.Key, from.Label, to.Key, to.Label,
                Math.Sqrt(dx * dx + dy * dy + dz * dz),
                Math.Sqrt(dx * dx + dy * dy),
                dz,
                overlaps ? 0 : Math.Sqrt(gx * gx + gy * gy + gz * gz),
                overlaps);
        }

        /// <summary>The step from each element to the next along a run of them.</summary>
        /// <param name="elements">The elements in the run.</param>
        /// <param name="axis">Which direction to put them in order along.</param>
        public static SpacingRun Spacing(IEnumerable<FieldElement>? elements, Along axis = Along.Longest)
        {
            var all = (elements ?? Enumerable.Empty<FieldElement>())
                      .Where(e => e != null && e.HasGeometry).ToList();

            if (all.Count < 2)
                return new SpacingRun(axis, Array.Empty<Separation>(),
                    "A run needs at least two elements. Select the hangers, the supports or the "
                    + "bays that are meant to be at centres and read it again.");

            var chosen = axis == Along.Longest ? Widest(all) : axis;
            var ordered = all.OrderBy(e => Position(e.Bounds, chosen)).ToList();
            var steps = new List<Separation>(ordered.Count - 1);

            for (var at = 1; at < ordered.Count; at++)
                steps.Add(Between(ordered[at - 1], ordered[at]));

            return new SpacingRun(chosen, steps, null);
        }

        /// <summary>Measure everything asked for.</summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">What to measure.</param>
        /// <param name="levels">The levels, for the heights. Without them the heights are skipped and it says so.</param>
        public static DimensionSchedule Schedule(IEnumerable<FieldElement>? elements,
                                                 DimensionRecipe? recipe,
                                                 LevelSet? levels = null)
        {
            var wanted = recipe ?? new DimensionRecipe();
            var notes = new List<string>();

            var all = (elements ?? Enumerable.Empty<FieldElement>()).Where(e => e != null).ToList();

            if (all.Count == 0)
                return Nothing(wanted, notes,
                    "There is nothing selected to measure. Select what the question is about and "
                    + "read it again.");

            if (wanted.IsEmpty)
                return Nothing(wanted, notes,
                    "No measurement has been asked for. How big each element is, is the usual one.");

            var withBody = all.Where(e => e.HasGeometry).ToList();

            if (withBody.Count == 0)
                return Nothing(wanted, notes,
                    "None of those elements has a body, so there is nothing to measure. Grouping "
                    + "nodes in the selection tree look like this.");

            var ceiling = wanted.Ceiling > 0 ? wanted.Ceiling : DimensionRecipe.DefaultCeiling;
            var rows = new List<DimensionRow>();
            var extents = new List<Extent>(withBody.Count);
            var dropped = 0;
            var at = 1;
            var boxed = 0;
            var uncertain = 0;

            foreach (var element in withBody)
            {
                var extent = ExtentOf(element);

                extents.Add(extent);

                if (!extent.Oriented) boxed++;
                if (extent.AxisUncertain) uncertain++;

                if (!wanted.Sizes) continue;

                Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Run,
                    element.Label, null, Along.Longest, extent.Run, extent.Oriented);
                Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Across,
                    element.Label, null, Along.Longest, extent.Across, extent.Oriented);
                Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Through,
                    element.Label, null, Along.Longest, extent.Through, extent.Oriented);

                if (wanted.Diagonals)
                    Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Diagonal,
                        element.Label, null, Along.Longest, extent.Diagonal, extent.Oriented);
            }

            if (wanted.Heights) Heights(rows, ref at, ref dropped, ceiling, wanted, withBody, levels, notes);

            if (wanted.Neighbours) Neighbours(rows, ref at, ref dropped, ceiling, wanted, withBody, notes);

            if (wanted.Spacing)
            {
                var run = Spacing(withBody, wanted.SpacingAxis);

                if (!run.IsUsable)
                {
                    if (run.Refusal != null) notes.Add(run.Refusal);
                }
                else
                {
                    foreach (var step in run.Steps)
                        Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Spacing,
                            step.FromLabel, step.ToLabel, run.Axis, step.Centres, false);

                    notes.Add("Ordered along " + Word(run.Axis) + ": "
                              + run.Steps.Count.ToString("N0", CultureInfo.InvariantCulture)
                              + " steps averaging "
                              + Round(run.Mean, wanted.Decimals).ToString("0.###", CultureInfo.InvariantCulture)
                              + " m, the worst "
                              + (run.Drift * 100).ToString("0.#", CultureInfo.InvariantCulture)
                              + "% off that.");
                }
            }

            if (boxed > 0)
                notes.Add("Where an element's triangles have not been read it is measured off its "
                          + "world box, which is right for anything square to the model and too "
                          + "long for anything on the diagonal. The schedule says which is which.");

            if (uncertain > 0)
                notes.Add(uncertain.ToString("N0", CultureInfo.InvariantCulture)
                          + (uncertain == 1 ? " element is" : " elements are")
                          + " the same size in more than one direction, so which way "
                          + (uncertain == 1 ? "it runs" : "they run")
                          + " is arbitrary. A cube has three equal sides and no length.");

            if (rows.Count == 0)
                return Nothing(wanted, notes,
                    "Nothing came out. What was asked for does not apply to any of these elements.");

            return new DimensionSchedule(wanted, rows, extents, withBody.Count,
                                         all.Count - withBody.Count, boxed, dropped, notes, null);
        }

        /// <summary>
        /// The schedule as the text a spreadsheet reads.
        ///
        /// Written in metres to the recipe's own decimals rather than in the project's display
        /// unit, because a file that goes into a calculation has to carry one unit and say which.
        /// </summary>
        /// <param name="schedule">What was measured.</param>
        public static string Csv(DimensionSchedule? schedule)
        {
            var text = new StringBuilder();

            text.Append("Ref,Measurement,Element,Against,Along,Metres,Source\r\n");

            if (schedule == null) return text.ToString();

            var places = schedule.Recipe.Decimals;

            foreach (var row in schedule.Rows)
            {
                text.Append(Cell(row.Reference)).Append(',');
                text.Append(Cell(Word(row.Kind))).Append(',');
                text.Append(Cell(row.Subject)).Append(',');
                text.Append(Cell(row.Against ?? string.Empty)).Append(',');
                text.Append(Cell(Word(row.Axis))).Append(',');
                text.Append(Round(row.Metres, places).ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
                text.Append(row.Oriented ? "geometry" : "box").Append("\r\n");
            }

            return text.ToString();
        }

        /// <summary>The schedule as a report, so it goes out as HTML, a workbook or a PDF like everything else.</summary>
        /// <param name="schedule">What was measured.</param>
        /// <param name="units">The project's unit formatter.</param>
        public static ReportDocument Report(DimensionSchedule? schedule, Measure? units = null)
        {
            var measure = units ?? Measure.Metric;
            var document = new ReportDocument("Dimension schedule");

            if (schedule == null)
            {
                document.Paragraph("Nothing has been measured.");
                return document;
            }

            document.Subtitle = schedule.Recipe.Name;
            document.Fact("Elements", Measure.Count(schedule.Elements));
            document.Fact("Dimensions", Measure.Count(schedule.Rows.Count));
            document.Fact("Measured off geometry",
                          Measure.Count(Math.Max(0, schedule.Elements - schedule.Boxed)));
            document.Fact("Measured off the world box", Measure.Count(schedule.Boxed));

            document.Paragraph(schedule.Explain());

            if (!schedule.IsUsable) return document;

            var table = new TableBlock("Ref", "Measurement", "Element", "Against", "Along",
                                       measure.LengthUnit, "Source");

            foreach (var row in schedule.Rows)
                table.Row(row.Reference, Word(row.Kind), row.Subject, row.Against ?? Measure.Nothing,
                          Word(row.Axis), row.Say(measure), row.Oriented ? "geometry" : "box");

            document.Add(table);

            return document;
        }

        /// <summary>What a measurement is called, in words.</summary>
        /// <param name="kind">The measurement.</param>
        public static string Word(DimensionKind kind) => kind switch
        {
            DimensionKind.Run => "Run",
            DimensionKind.Across => "Across",
            DimensionKind.Through => "Through",
            DimensionKind.Diagonal => "Diagonal",
            DimensionKind.Height => "Above level",
            DimensionKind.Centres => "Centres",
            DimensionKind.Clear => "Clear",
            DimensionKind.Spacing => "Spacing",
            _ => kind.ToString(),
        };

        /// <summary>What a direction is called, in words.</summary>
        /// <param name="axis">The direction.</param>
        public static string Word(Along axis) => axis switch
        {
            Along.Longest => "the longest spread",
            Along.X => "model X",
            Along.Y => "model Y",
            Along.Level => "level",
            _ => axis.ToString(),
        };

        private static void Heights(List<DimensionRow> rows, ref int at, ref int dropped, int ceiling,
                                    DimensionRecipe wanted, IReadOnlyList<FieldElement> elements,
                                    LevelSet? levels, List<string> notes)
        {
            if (levels == null || levels.Bands.Count == 0)
            {
                notes.Add("No heights above level: the model has no levels to measure from, and "
                          + "none could be worked out from it.");
                return;
            }

            var homeless = 0;

            foreach (var element in elements)
            {
                var band = levels.Bands.FirstOrDefault(b => b.Contains(element.Bounds.MinZ));

                if (band == null) { homeless++; continue; }

                Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Height,
                    element.Label, band.Name, Along.Level,
                    element.Bounds.MinZ - band.Elevation, false);
            }

            if (homeless > 0)
                notes.Add(homeless.ToString("N0", CultureInfo.InvariantCulture)
                          + (homeless == 1 ? " element sits" : " elements sit")
                          + " below the lowest level or above the highest, so there is nothing to "
                          + "measure " + (homeless == 1 ? "its" : "their") + " height from.");
        }

        private static void Neighbours(List<DimensionRow> rows, ref int at, ref int dropped, int ceiling,
                                       DimensionRecipe wanted, IReadOnlyList<FieldElement> elements,
                                       List<string> notes)
        {
            if (elements.Count < 2)
            {
                notes.Add("No nearest neighbour: one element has nothing to be near.");
                return;
            }

            // Every element against every other. Quadratic, and honest about where it stops rather
            // than sampling and calling the answer the nearest neighbour when it is only the
            // nearest of the ones it got round to looking at.
            if (elements.Count > DimensionRecipe.NeighbourCeiling)
            {
                notes.Add("No nearest neighbour: "
                          + elements.Count.ToString("N0", CultureInfo.InvariantCulture)
                          + " elements is more than the "
                          + DimensionRecipe.NeighbourCeiling.ToString("N0", CultureInfo.InvariantCulture)
                          + " this compares each against all of. Narrow the selection to the run "
                          + "you are asking about.");
                return;
            }

            foreach (var element in elements)
            {
                Separation? closest = null;

                foreach (var other in elements)
                {
                    if (ReferenceEquals(other, element)) continue;

                    var apart = Between(element, other);

                    if (closest == null || apart.Clear < closest.Clear
                        || (apart.Clear <= closest.Clear && apart.Centres < closest.Centres))
                        closest = apart;
                }

                if (closest == null) continue;

                Put(rows, ref at, ref dropped, ceiling, wanted, DimensionKind.Clear,
                    closest.FromLabel, closest.ToLabel, Along.Longest, closest.Clear, false);
            }
        }

        private static void Put(List<DimensionRow> rows, ref int at, ref int dropped, int ceiling,
                                DimensionRecipe wanted, DimensionKind kind, string subject,
                                string? against, Along axis, double metres, bool oriented)
        {
            if (rows.Count >= ceiling) { dropped++; return; }

            rows.Add(new DimensionRow(
                wanted.Prefix + at.ToString("000", CultureInfo.InvariantCulture),
                kind, subject, against, axis, Round(metres, wanted.Decimals), oriented));

            at++;
        }

        private static Extent FromBox(FieldElement element)
        {
            var box = element.Bounds;

            var sides = new[]
            {
                (Size: box.SizeX, X: 1.0, Y: 0.0, Z: 0.0),
                (Size: box.SizeY, X: 0.0, Y: 1.0, Z: 0.0),
                (Size: box.SizeZ, X: 0.0, Y: 0.0, Z: 1.0),
            }.OrderByDescending(s => s.Size).ToArray();

            return new Extent(element.Key, element.Label, false,
                              sides[0].Size - sides[1].Size <= sides[0].Size * RunMargin,
                              sides[0].Size, sides[1].Size, sides[2].Size,
                              sides[0].X, sides[0].Y, sides[0].Z);
        }

        private static Extent FromBody(FieldElement element)
        {
            var points = element.Positions;
            var count = points.Count / 3;

            double cx = 0, cy = 0, cz = 0;

            for (var at = 0; at < count; at++)
            {
                cx += points[at * 3];
                cy += points[at * 3 + 1];
                cz += points[at * 3 + 2];
            }

            cx /= count; cy /= count; cz /= count;

            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            double furthest = -1, fx = 0, fy = 0, fz = 0;

            for (var at = 0; at < count; at++)
            {
                var dx = points[at * 3] - cx;
                var dy = points[at * 3 + 1] - cy;
                var dz = points[at * 3 + 2] - cz;

                xx += dx * dx; xy += dx * dy; xz += dx * dz;
                yy += dy * dy; yz += dy * dz; zz += dz * dz;

                var far = dx * dx + dy * dy + dz * dz;

                if (far > furthest) { furthest = far; fx = dx; fy = dy; fz = dz; }
            }

            if (furthest <= Tiny) return FromBox(element);

            // Seeded with the vertex furthest from the middle rather than with an arbitrary
            // direction. For anything long that is already almost the answer, so the rounds below
            // are confirming it rather than searching for it, and there is no starting vector that
            // happens to sit square to the one being looked for.
            var length = Math.Sqrt(furthest);
            double ux = fx / length, uy = fy / length, uz = fz / length;

            for (var round = 0; round < Rounds; round++)
            {
                var nx = xx * ux + xy * uy + xz * uz;
                var ny = xy * ux + yy * uy + yz * uz;
                var nz = xz * ux + yz * uy + zz * uz;

                var size = Math.Sqrt(nx * nx + ny * ny + nz * nz);

                if (size <= Tiny) break;

                ux = nx / size; uy = ny / size; uz = nz / size;
            }

            // Two directions square to the run, then the spread in the plane they make. Solved
            // rather than searched for: a two by two is exact, and the run direction has already
            // taken everything out of the problem that made it three-dimensional.
            var (ax, ay, az) = Square(ux, uy, uz);
            var (bx, by, bz) = (uy * az - uz * ay, uz * ax - ux * az, ux * ay - uy * ax);

            var aa = Quadratic(xx, xy, xz, yy, yz, zz, ax, ay, az, ax, ay, az);
            var ab = Quadratic(xx, xy, xz, yy, yz, zz, ax, ay, az, bx, by, bz);
            var bb = Quadratic(xx, xy, xz, yy, yz, zz, bx, by, bz, bx, by, bz);

            var middle = (aa + bb) / 2;
            var half = Math.Sqrt(Math.Max(0, (aa - bb) * (aa - bb) / 4 + ab * ab));
            var top = middle + half;

            double vx, vy, vz;

            if (Math.Abs(ab) > Tiny)
            {
                var e1 = ab;
                var e2 = top - aa;
                var scale = Math.Sqrt(e1 * e1 + e2 * e2);

                vx = (ax * e1 + bx * e2) / scale;
                vy = (ay * e1 + by * e2) / scale;
                vz = (az * e1 + bz * e2) / scale;
            }
            else if (aa >= bb) { vx = ax; vy = ay; vz = az; }
            else { vx = bx; vy = by; vz = bz; }

            var (wx, wy, wz) = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx);

            var u = Spread(points, count, cx, cy, cz, ux, uy, uz);
            var v = Spread(points, count, cx, cy, cz, vx, vy, vz);
            var w = Spread(points, count, cx, cy, cz, wx, wy, wz);

            var sides = new[]
            {
                (Size: u, X: ux, Y: uy, Z: uz),
                (Size: v, X: vx, Y: vy, Z: vz),
                (Size: w, X: wx, Y: wy, Z: wz),
            }.OrderByDescending(s => s.Size).ToArray();

            // Pointed the way a drawing points it. A run is a direction, not an arrow, so the one
            // that reads up or east is the same run as the one that reads down or west - and
            // picking one of them keeps two identical members from reading as opposites.
            var (rx, ry, rz) = Forward(sides[0].X, sides[0].Y, sides[0].Z);

            // Whether the run direction means anything is a question about the spread, not about
            // the extents: the eight corners of a cube spread the same amount every way, and the
            // longest way across one is a body diagonal a good deal longer than any side. Reading
            // that difference as a run would put a direction on the row that nothing in the
            // element supports. The spread is what the two eigenvalues are, so they are what it
            // is asked of - rooted, because a spread goes as the square of a length.
            var first = Quadratic(xx, xy, xz, yy, yz, zz, ux, uy, uz, ux, uy, uz);
            var second = Math.Max(0, top);

            var uncertain = first <= Tiny
                            || Math.Sqrt(second) >= Math.Sqrt(first) * (1 - RunMargin);

            return new Extent(element.Key, element.Label, true, uncertain,
                              sides[0].Size, sides[1].Size, sides[2].Size, rx, ry, rz);
        }

        private static double Spread(IReadOnlyList<float> points, int count,
                                     double cx, double cy, double cz,
                                     double ax, double ay, double az)
        {
            double least = double.MaxValue, most = double.MinValue;

            for (var at = 0; at < count; at++)
            {
                var along = (points[at * 3] - cx) * ax
                          + (points[at * 3 + 1] - cy) * ay
                          + (points[at * 3 + 2] - cz) * az;

                if (along < least) least = along;
                if (along > most) most = along;
            }

            return most - least;
        }

        private static double Quadratic(double xx, double xy, double xz, double yy, double yz, double zz,
                                        double ax, double ay, double az,
                                        double bx, double by, double bz) =>
            ax * (xx * bx + xy * by + xz * bz)
            + ay * (xy * bx + yy * by + yz * bz)
            + az * (xz * bx + yz * by + zz * bz);

        private static (double X, double Y, double Z) Square(double ux, double uy, double uz)
        {
            // Crossed with whichever world axis the run is least like, so the cross is never taken
            // against something it is parallel to and never comes back as nothing.
            var (px, py, pz) = Math.Abs(ux) <= Math.Abs(uy) && Math.Abs(ux) <= Math.Abs(uz)
                ? (1.0, 0.0, 0.0)
                : Math.Abs(uy) <= Math.Abs(uz) ? (0.0, 1.0, 0.0) : (0.0, 0.0, 1.0);

            var cx = uy * pz - uz * py;
            var cy = uz * px - ux * pz;
            var cz = ux * py - uy * px;

            var size = Math.Sqrt(cx * cx + cy * cy + cz * cz);

            return size <= Tiny ? (0.0, 0.0, 1.0) : (cx / size, cy / size, cz / size);
        }

        private static (double X, double Y, double Z) Forward(double x, double y, double z)
        {
            // Decided on whichever component is biggest. The small ones on a run that is level, or
            // square to an axis, are rounding noise with an arbitrary sign, and settling the
            // direction on one of those is what would make two identical members - the same brace
            // modelled from either end - read as running opposite ways.
            var pick = Math.Abs(z) >= Math.Abs(x) && Math.Abs(z) >= Math.Abs(y) ? z
                     : Math.Abs(x) >= Math.Abs(y) ? x : y;

            return pick < 0 ? (-x, -y, -z) : (x, y, z);
        }

        private static Along Widest(IReadOnlyList<FieldElement> elements)
        {
            var x = elements.Max(e => e.Bounds.CentreX) - elements.Min(e => e.Bounds.CentreX);
            var y = elements.Max(e => e.Bounds.CentreY) - elements.Min(e => e.Bounds.CentreY);
            var z = elements.Max(e => e.Bounds.CentreZ) - elements.Min(e => e.Bounds.CentreZ);

            if (x >= y && x >= z) return Along.X;

            return y >= z ? Along.Y : Along.Level;
        }

        private static double Position(BoundingBox box, Along axis) => axis switch
        {
            Along.X => box.CentreX,
            Along.Y => box.CentreY,
            Along.Level => box.CentreZ,
            _ => box.CentreX,
        };

        private static DimensionSchedule Nothing(DimensionRecipe recipe, List<string> notes, string why) =>
            new DimensionSchedule(recipe, Array.Empty<DimensionRow>(), Array.Empty<Extent>(),
                                  0, 0, 0, 0, notes, why);

        private static double Round(double value, int places) =>
            Math.Round(value, Math.Max(0, Math.Min(6, places)), MidpointRounding.AwayFromZero);

        private static string Cell(string text)
        {
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return text;

            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
