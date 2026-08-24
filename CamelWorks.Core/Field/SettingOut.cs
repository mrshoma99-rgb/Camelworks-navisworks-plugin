using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Field
{
    /// <summary>Which point on an element a setting-out point is.</summary>
    public enum PointKind
    {
        /// <summary>The middle of its base - where a column or a plinth is set out from.</summary>
        Base = 0,

        /// <summary>The middle of its top - where a hanger drops from.</summary>
        Top,

        /// <summary>The middle of it.</summary>
        Centre,

        /// <summary>One end of its longest run - where a pipe or a duct starts.</summary>
        Start,

        /// <summary>The other end.</summary>
        End,
    }

    /// <summary>One element, as the setting-out reads it.</summary>
    public sealed class SetOutElement
    {
        /// <summary>Name one.</summary>
        /// <param name="key">Which element.</param>
        /// <param name="name">What it is called.</param>
        /// <param name="bounds">Its world box, in metres.</param>
        /// <param name="hasGeometry">Whether it has a body to set out at all.</param>
        /// <param name="mark">Its mark, where the model carries one, so the point is named for it.</param>
        public SetOutElement(ElementKey key, string? name, BoundingBox bounds,
                             bool hasGeometry = true, string? mark = null)
        {
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name!.Trim();
            Bounds = bounds;
            HasGeometry = hasGeometry;
            Mark = string.IsNullOrWhiteSpace(mark) ? null : mark!.Trim();
        }

        /// <summary>Which element.</summary>
        public ElementKey Key { get; }

        /// <summary>What it is called.</summary>
        public string Name { get; }

        /// <summary>Its world box, in metres.</summary>
        public BoundingBox Bounds { get; }

        /// <summary>Whether it has a body to set out at all.</summary>
        public bool HasGeometry { get; }

        /// <summary>Its mark, or null.</summary>
        public string? Mark { get; }

        /// <summary>What the points off it are named after.</summary>
        public string Label => Mark ?? Name;

        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>One point, in site coordinates, ready to go to an instrument.</summary>
    public sealed class SetOutPoint
    {
        internal SetOutPoint(string number, string description, PointKind kind, ElementKey key,
                             double northing, double easting, double level)
        {
            Number = number;
            Description = description;
            Kind = kind;
            Key = key;
            Northing = northing;
            Easting = easting;
            Level = level;
        }

        /// <summary>What the instrument calls it. Unique within one export.</summary>
        public string Number { get; }

        /// <summary>What it is, in words, so somebody on site knows what they are looking at.</summary>
        public string Description { get; }

        /// <summary>Which point on the element it is.</summary>
        public PointKind Kind { get; }

        /// <summary>Which element it came off.</summary>
        public ElementKey Key { get; }

        /// <summary>North, in metres.</summary>
        public double Northing { get; }

        /// <summary>East, in metres.</summary>
        public double Easting { get; }

        /// <summary>Level, in metres.</summary>
        public double Level { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Number + " " + Description + " "
            + Northing.ToString("0.###", CultureInfo.InvariantCulture) + "N "
            + Easting.ToString("0.###", CultureInfo.InvariantCulture) + "E "
            + Level.ToString("0.###", CultureInfo.InvariantCulture) + "m";
    }

    /// <summary>What setting-out points to take, and where site zero is.</summary>
    public sealed class SetOutRecipe
    {
        /// <summary>How many points one run will produce.</summary>
        public const int DefaultCeiling = 20000;

        /// <summary>Start one.</summary>
        /// <param name="name">What to call it.</param>
        public SetOutRecipe(string? name = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Setting out" : name!.Trim();
        }

        /// <summary>What to call it.</summary>
        public string Name { get; set; }

        /// <summary>What goes in front of every point number.</summary>
        public string Prefix { get; set; } = "SO";

        /// <summary>Which points to take off each element.</summary>
        public IList<PointKind> Kinds { get; } = new List<PointKind> { PointKind.Base };

        /// <summary>
        /// Where the model's origin sits in site coordinates: north, in metres.
        ///
        /// The survey offset every job has and nobody writes down. Wrong, and every point is
        /// wrong by the same amount - which is the one kind of error a setting-out file can carry
        /// that nobody notices until the steel does not fit.
        /// </summary>
        public double NorthAtOrigin { get; set; }

        /// <summary>Where the model's origin sits in site coordinates: east, in metres.</summary>
        public double EastAtOrigin { get; set; }

        /// <summary>Where the model's zero sits in site levels, in metres.</summary>
        public double LevelAtOrigin { get; set; }

        /// <summary>
        /// How far the model is rotated from grid north, clockwise, in degrees.
        ///
        /// Positive turns the model clockwise onto the site grid, which is how a project angle is
        /// written on every drawing that has one.
        /// </summary>
        public double RotationDegrees { get; set; }

        /// <summary>How many decimal places the coordinates are written to.</summary>
        public int Decimals { get; set; } = 3;

        /// <summary>How many points before it stops.</summary>
        public int Ceiling { get; set; } = DefaultCeiling;

        /// <summary>True when the model sits exactly on the site grid, so nothing is transformed.</summary>
        public bool IsUnshifted =>
            NorthAtOrigin == 0 && EastAtOrigin == 0 && LevelAtOrigin == 0 && RotationDegrees == 0;

        /// <summary>An independent copy, so a screen can edit without disturbing what is saved.</summary>
        public SetOutRecipe Copy() => FromJson(ToJson());

        /// <summary>Write it down.</summary>
        public JsonValue ToJson() =>
            JsonValue.Object()
                .Set("name", Name)
                .Set("prefix", Prefix)
                .Set("kinds", JsonValue.Array(Kinds.Select(k => JsonValue.String(k.ToString()))))
                .Set("north", NorthAtOrigin)
                .Set("east", EastAtOrigin)
                .Set("level", LevelAtOrigin)
                .Set("rotation", RotationDegrees)
                .Set("decimals", Decimals)
                .Set("ceiling", Ceiling);

        /// <summary>Read one back. An unreadable one comes back taking base points and nothing else.</summary>
        /// <param name="json">What was written.</param>
        public static SetOutRecipe FromJson(JsonValue? json)
        {
            var recipe = new SetOutRecipe(json?["name"].AsString());

            if (json == null) return recipe;

            var prefix = (json["prefix"].AsString() ?? string.Empty).Trim();

            recipe.Prefix = prefix.Length == 0 ? "SO" : prefix;

            recipe.Kinds.Clear();

            foreach (var item in json["kinds"].Items)
                if (Enum.TryParse<PointKind>(item.AsString() ?? string.Empty, out var kind)
                    && !recipe.Kinds.Contains(kind))
                    recipe.Kinds.Add(kind);

            if (recipe.Kinds.Count == 0) recipe.Kinds.Add(PointKind.Base);

            recipe.NorthAtOrigin = json["north"].AsDouble();
            recipe.EastAtOrigin = json["east"].AsDouble();
            recipe.LevelAtOrigin = json["level"].AsDouble();
            recipe.RotationDegrees = json["rotation"].AsDouble();

            var decimals = (int)json["decimals"].AsLong();

            recipe.Decimals = decimals >= 0 && decimals <= 6 ? decimals : 3;

            var ceiling = (int)json["ceiling"].AsLong();

            recipe.Ceiling = ceiling > 0 ? ceiling : DefaultCeiling;

            return recipe;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " — " + string.Join(", ", Kinds.Select(SettingOut.Word));
    }

    /// <summary>The points, and what could not be taken.</summary>
    public sealed class SetOutResult
    {
        internal SetOutResult(SetOutRecipe recipe, IReadOnlyList<SetOutPoint> points, int elements,
                              int bodiless, int dropped, IReadOnlyList<string> notes, string? refusal)
        {
            Recipe = recipe;
            Points = points;
            Elements = elements;
            Bodiless = bodiless;
            Dropped = dropped;
            Notes = notes;
            Refusal = refusal;
        }

        /// <summary>What was asked for.</summary>
        public SetOutRecipe Recipe { get; }

        /// <summary>The points.</summary>
        public IReadOnlyList<SetOutPoint> Points { get; }

        /// <summary>How many elements they came off.</summary>
        public int Elements { get; }

        /// <summary>How many elements carried no body to take a point off.</summary>
        public int Bodiless { get; }

        /// <summary>How many points did not fit under the ceiling.</summary>
        public int Dropped { get; }

        /// <summary>Anything worth saying about them.</summary>
        public IReadOnlyList<string> Notes { get; }

        /// <summary>Why there are no points, when there are none.</summary>
        public string? Refusal { get; }

        /// <summary>True when there is a file worth writing.</summary>
        public bool IsUsable => Refusal == null && Points.Count > 0;

        /// <summary>What it found, said plainly.</summary>
        public string Explain()
        {
            if (Refusal != null) return Refusal;

            var text = Points.Count.ToString("N0", CultureInfo.InvariantCulture)
                     + (Points.Count == 1 ? " point off " : " points off ")
                     + Elements.ToString("N0", CultureInfo.InvariantCulture)
                     + (Elements == 1 ? " element." : " elements.");

            if (Bodiless > 0)
                text += " " + Bodiless.ToString("N0", CultureInfo.InvariantCulture)
                     + (Bodiless == 1 ? " element has" : " elements have")
                     + " no body to take a point off and " + (Bodiless == 1 ? "was" : "were")
                     + " passed over.";

            if (Dropped > 0)
                text += " " + Dropped.ToString("N0", CultureInfo.InvariantCulture)
                     + " more did not fit under the ceiling.";

            if (Notes.Count > 0) text += " " + string.Join(" ", Notes);

            return text;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// The model, as points somebody can set out on site.
    ///
    /// The last hundred metres of a coordination job. Everything upstream produces a model that is
    /// right; this produces the file the engineer loads into a total station and walks the floor
    /// with. Doing it by hand is somebody reading coordinates off a screen into a spreadsheet, and
    /// the transcription error that follows is discovered by the steel not fitting.
    ///
    /// <b>Site coordinates, not model coordinates.</b> A model's origin is almost never the site's,
    /// and it is almost never square to the site grid either. So the offset and the project angle
    /// are stated, applied, and written into the file's own header - because a setting-out file
    /// that does not say what it is referenced to is a file nobody on site is allowed to use.
    ///
    /// <b>Northing before easting.</b> The order every instrument expects and the one everybody
    /// gets wrong the first time, which is why the columns are named rather than positional in
    /// everything this writes.
    /// </summary>
    public static class SettingOut
    {
        /// <summary>Where saved setting-out runs live in the project store.</summary>
        public const string StoreKey = "settingOut";

        /// <summary>
        /// How long an element has to be, relative to its other sides, before its ends are worth
        /// setting out separately.
        /// </summary>
        public const double LongEnough = 2.0;

        /// <summary>Take the points.</summary>
        /// <param name="elements">The elements in scope.</param>
        /// <param name="recipe">Which points, and where site zero is.</param>
        public static SetOutResult Of(IEnumerable<SetOutElement>? elements, SetOutRecipe? recipe)
        {
            var wanted = recipe ?? new SetOutRecipe();
            var notes = new List<string>();

            var all = (elements ?? Enumerable.Empty<SetOutElement>()).Where(e => e != null).ToList();

            if (all.Count == 0)
                return Nothing(wanted, notes,
                    "There is nothing selected to set out. Select what is going on site - the "
                    + "columns, the bases, the hangers - and read it again.");

            if (wanted.Kinds.Count == 0)
                return Nothing(wanted, notes,
                    "No point has been asked for. Pick at least one - the base of each element is "
                    + "the usual one.");

            var withBody = all.Where(e => e.HasGeometry).ToList();

            if (withBody.Count == 0)
                return Nothing(wanted, notes,
                    "None of those elements has a body, so there is nowhere to take a point. "
                    + "Grouping nodes in the selection tree look like this.");

            var ceiling = wanted.Ceiling > 0 ? wanted.Ceiling : SetOutRecipe.DefaultCeiling;
            var points = new List<SetOutPoint>();
            var dropped = 0;
            var at = 1;
            var stubby = 0;

            var sin = Math.Sin(wanted.RotationDegrees * Math.PI / 180.0);
            var cos = Math.Cos(wanted.RotationDegrees * Math.PI / 180.0);

            foreach (var element in withBody)
            {
                var run = Run(element.Bounds);

                foreach (var kind in wanted.Kinds)
                {
                    if ((kind == PointKind.Start || kind == PointKind.End) && !run.Long)
                    {
                        stubby++;
                        continue;
                    }

                    if (points.Count >= ceiling) { dropped++; continue; }

                    var local = At(element.Bounds, kind, run);

                    // Rotated clockwise onto the site grid, then shifted. Clockwise because that is
                    // the direction a project angle is written in on every drawing that has one,
                    // and the sign of it is the mistake this would otherwise make silently.
                    var east = local.X * cos + local.Y * sin;
                    var north = -local.X * sin + local.Y * cos;

                    points.Add(new SetOutPoint(
                        wanted.Prefix + at.ToString("000", CultureInfo.InvariantCulture),
                        element.Label + " — " + Word(kind),
                        kind,
                        element.Key,
                        Round(north + wanted.NorthAtOrigin, wanted.Decimals),
                        Round(east + wanted.EastAtOrigin, wanted.Decimals),
                        Round(local.Z + wanted.LevelAtOrigin, wanted.Decimals)));

                    at++;
                }
            }

            if (stubby > 0)
                notes.Add(stubby.ToString("N0", CultureInfo.InvariantCulture)
                          + (stubby == 1 ? " element is" : " elements are")
                          + " not long enough in any direction for its ends to mean anything, so "
                          + (stubby == 1 ? "it has" : "they have")
                          + " no start or end point. A box has a middle, not a run.");

            if (wanted.IsUnshifted)
                notes.Add("These are model coordinates: nothing has been said about where the "
                          + "model sits on the site grid. Set the offset and the project angle "
                          + "before this goes to an instrument.");

            if (points.Count == 0)
                return Nothing(wanted, notes,
                    "Nothing came out. The points asked for are the ends of a run, and none of "
                    + "these elements is long enough in any direction to have one.");

            return new SetOutResult(wanted, points, withBody.Count, all.Count - withBody.Count,
                                    dropped, notes, null);
        }

        /// <summary>
        /// The points as the text an instrument reads.
        ///
        /// Number, northing, easting, level, description - the order every total station expects,
        /// with a header that says what the coordinates are referenced to. A setting-out file
        /// without that line is one nobody on site is allowed to use, and putting it in a separate
        /// email is how it stops being attached to the file.
        /// </summary>
        /// <param name="result">The points.</param>
        /// <param name="job">What the job is called, for the header.</param>
        public static string Csv(SetOutResult? result, string? job = null)
        {
            var text = new StringBuilder();

            if (result == null || result.Points.Count == 0) return string.Empty;

            var recipe = result.Recipe;

            text.Append("# ").Append(job ?? recipe.Name).Append(" — ").Append(recipe.Name)
                .Append("\r\n");

            text.Append("# Written by CamelWorks (bimcamel.com)\r\n");

            text.Append("# Model origin sits at N ")
                .Append(Text(recipe.NorthAtOrigin, recipe.Decimals))
                .Append(", E ").Append(Text(recipe.EastAtOrigin, recipe.Decimals))
                .Append(", level ").Append(Text(recipe.LevelAtOrigin, recipe.Decimals))
                .Append(", rotated ")
                .Append(recipe.RotationDegrees.ToString("0.####", CultureInfo.InvariantCulture))
                .Append("° clockwise onto the site grid.\r\n");

            text.Append("# All values in metres. Northing before easting.\r\n");

            text.Append("Point,Northing,Easting,Level,Description\r\n");

            foreach (var point in result.Points)
                text.Append(Quote(point.Number)).Append(',')
                    .Append(Text(point.Northing, recipe.Decimals)).Append(',')
                    .Append(Text(point.Easting, recipe.Decimals)).Append(',')
                    .Append(Text(point.Level, recipe.Decimals)).Append(',')
                    .Append(Quote(point.Description)).Append("\r\n");

            return text.ToString();
        }

        /// <summary>Every saved setting-out run, in the order they were saved.</summary>
        /// <param name="json">What the store holds under <see cref="StoreKey"/>.</param>
        public static IReadOnlyList<SetOutRecipe> Saved(JsonValue? json)
        {
            var saved = new List<SetOutRecipe>();

            if (json == null) return saved;

            foreach (var item in json.Items) saved.Add(SetOutRecipe.FromJson(item));

            return saved;
        }

        /// <summary>The list with one run put in it, replacing any of the same name.</summary>
        /// <param name="saved">What is saved now.</param>
        /// <param name="recipe">The one to keep.</param>
        public static JsonValue Keep(IEnumerable<SetOutRecipe>? saved, SetOutRecipe? recipe)
        {
            var kept = (saved ?? Enumerable.Empty<SetOutRecipe>()).ToList();

            if (recipe != null)
            {
                kept.RemoveAll(r => string.Equals(r.Name, recipe.Name, StringComparison.OrdinalIgnoreCase));
                kept.Add(recipe);
            }

            return JsonValue.Array(kept.Select(r => r.ToJson()));
        }

        /// <summary>What a kind of point is called, in words somebody on site would use.</summary>
        /// <param name="kind">Which one.</param>
        public static string Word(PointKind kind) => kind switch
        {
            PointKind.Top => "top",
            PointKind.Centre => "centre",
            PointKind.Start => "start of run",
            PointKind.End => "end of run",
            _ => "base",
        };

        /// <summary>
        /// Which way an element runs, and whether it runs at all.
        ///
        /// A duct has a start and an end; a plinth has a middle. Telling them apart is the ratio of
        /// the longest side to the next longest, because an element that is only slightly longer
        /// one way is a slab, and the two "ends" of a slab are two arbitrary corners.
        /// </summary>
        /// <param name="bounds">The element's box.</param>
        public static (bool Long, int Axis) Run(BoundingBox bounds)
        {
            var sides = new[] { Math.Abs(bounds.SizeX), Math.Abs(bounds.SizeY), Math.Abs(bounds.SizeZ) };

            var axis = 0;

            for (var i = 1; i < 3; i++) if (sides[i] > sides[axis]) axis = i;

            var rest = Enumerable.Range(0, 3).Where(i => i != axis).Select(i => sides[i]).Max();

            return (rest > 0 && sides[axis] / rest >= LongEnough, axis);
        }

        private static (double X, double Y, double Z) At(BoundingBox bounds, PointKind kind,
                                                         (bool Long, int Axis) run)
        {
            var x = (bounds.MinX + bounds.MaxX) / 2;
            var y = (bounds.MinY + bounds.MaxY) / 2;

            switch (kind)
            {
                case PointKind.Top: return (x, y, bounds.MaxZ);
                case PointKind.Centre: return (x, y, (bounds.MinZ + bounds.MaxZ) / 2);

                case PointKind.Start:
                case PointKind.End:
                {
                    var far = kind == PointKind.End;
                    var z = (bounds.MinZ + bounds.MaxZ) / 2;

                    return run.Axis switch
                    {
                        0 => (far ? bounds.MaxX : bounds.MinX, y, z),
                        1 => (x, far ? bounds.MaxY : bounds.MinY, z),
                        _ => (x, y, far ? bounds.MaxZ : bounds.MinZ),
                    };
                }

                default: return (x, y, bounds.MinZ);
            }
        }

        private static double Round(double value, int decimals) =>
            Math.Round(value, decimals < 0 ? 0 : decimals > 6 ? 6 : decimals,
                       MidpointRounding.AwayFromZero);

        private static string Text(double value, int decimals) =>
            value.ToString("F" + (decimals < 0 ? 0 : decimals > 6 ? 6 : decimals).ToString(
                CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        private static string Quote(string cell)
        {
            var needs = cell.IndexOf(',') >= 0 || cell.IndexOf('"') >= 0
                     || cell.IndexOf('\n') >= 0 || cell.IndexOf('\r') >= 0;

            return needs ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }

        private static SetOutResult Nothing(SetOutRecipe recipe, IReadOnlyList<string> notes,
                                            string refusal) =>
            new SetOutResult(recipe, Array.Empty<SetOutPoint>(), 0, 0, 0, notes, refusal);
    }
}
