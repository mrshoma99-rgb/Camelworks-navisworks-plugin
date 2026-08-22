using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>Where one element was, and how big it was, at the moment of a freeze.</summary>
    public sealed class FrozenElement
    {
        /// <summary>Create a record.</summary>
        /// <param name="key">The element.</param>
        /// <param name="bounds">Its extent in metres, as the document held it.</param>
        public FrozenElement(ElementKey key, BoundingBox bounds)
        {
            Key = key;
            MidX = (bounds.MinX + bounds.MaxX) / 2;
            MidY = (bounds.MinY + bounds.MaxY) / 2;
            MidZ = (bounds.MinZ + bounds.MaxZ) / 2;
            SizeX = bounds.MaxX - bounds.MinX;
            SizeY = bounds.MaxY - bounds.MinY;
            SizeZ = bounds.MaxZ - bounds.MinZ;
        }

        internal FrozenElement(ElementKey key, double midX, double midY, double midZ,
                              double sizeX, double sizeY, double sizeZ)
        {
            Key = key;
            MidX = midX; MidY = midY; MidZ = midZ;
            SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        }

        /// <summary>The element.</summary>
        public ElementKey Key { get; }

        /// <summary>Centre, X, in metres.</summary>
        public double MidX { get; }

        /// <summary>Centre, Y, in metres.</summary>
        public double MidY { get; }

        /// <summary>Centre, Z, in metres.</summary>
        public double MidZ { get; }

        /// <summary>Extent, X, in metres.</summary>
        public double SizeX { get; }

        /// <summary>Extent, Y, in metres.</summary>
        public double SizeY { get; }

        /// <summary>Extent, Z, in metres.</summary>
        public double SizeZ { get; }

        /// <summary>Write it down, rounded to the millimetre so the record is stable.</summary>
        public JsonValue ToJson() =>
            JsonValue.Array(new[]
            {
                JsonValue.String(Key.ToString()),
                JsonValue.Number(Round(MidX)), JsonValue.Number(Round(MidY)), JsonValue.Number(Round(MidZ)),
                JsonValue.Number(Round(SizeX)), JsonValue.Number(Round(SizeY)), JsonValue.Number(Round(SizeZ)),
            });

        /// <summary>Read one back, or null.</summary>
        /// <param name="json">What was written.</param>
        public static FrozenElement? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Array || json.Items.Count != 7) return null;

            return ElementKey.TryParse(json.Items[0].AsString(), out var key)
                ? new FrozenElement(key,
                    json.Items[1].AsDouble(), json.Items[2].AsDouble(), json.Items[3].AsDouble(),
                    json.Items[4].AsDouble(), json.Items[5].AsDouble(), json.Items[6].AsDouble())
                : null;
        }

        /// <inheritdoc />
        public override string ToString() => Key.ToString();

        internal static double Round(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Round(value, 4);
    }

    /// <summary>What happened to one frozen element since the freeze.</summary>
    public sealed class FrozenChange
    {
        internal FrozenChange(FrozenElement was, double moved, double resized, bool missing)
        {
            Was = was;
            Moved = moved;
            Resized = resized;
            IsMissing = missing;
        }

        /// <summary>Where it was.</summary>
        public FrozenElement Was { get; }

        /// <summary>How far its centre moved, in metres.</summary>
        public double Moved { get; }

        /// <summary>How much its largest dimension changed, in metres.</summary>
        public double Resized { get; }

        /// <summary>True when it is no longer in the model at all.</summary>
        public bool IsMissing { get; }

        /// <summary>How it reads on a change notice.</summary>
        /// <param name="units">How this project writes a length.</param>
        public string Describe(Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            if (IsMissing) return "gone from the model";

            var parts = new List<string>();

            if (Moved > 0) parts.Add("moved " + measure.Length(Moved));
            if (Resized > 0) parts.Add("resized by " + measure.Length(Resized));

            return parts.Count == 0 ? "unchanged" : string.Join(", ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Was.Key + ": " + Describe();
    }

    /// <summary>
    /// One immutable record that a scope was agreed, and what it was when it was.
    ///
    /// Two things a coordination board has to be able to say and cannot: this level was signed off
    /// on the fourteenth, and these hangers were released to fabrication on the twentieth. Both are
    /// the same act — somebody put their name to a state of the model — and both are worthless
    /// unless the tool can afterwards say what has changed since.
    ///
    /// So it is one mechanism. A sign-off is a freeze with a signatory; a fabrication release is a
    /// freeze with a release id. The record holds where every element was and how big it was, and
    /// the Change Notice is the difference between that and now.
    ///
    /// <b>Immutable, and hashed.</b> Nothing edits a freeze. The hash is over its own contents, so
    /// a record that has been altered in the project file no longer verifies — which is the least a
    /// document somebody signed can offer.
    /// </summary>
    public sealed class Freeze
    {
        private Freeze(string id, string scope, DateTimeOffset when, string by,
                       string? signatory, string? release, IReadOnlyList<string> groups,
                       IReadOnlyList<FrozenElement> elements, string hash)
        {
            Id = id; Scope = scope; When = when; By = by;
            Signatory = signatory; Release = release;
            Groups = groups; Elements = elements; Hash = hash;
        }

        /// <summary>Stable id, derived from what was frozen and when.</summary>
        public string Id { get; }

        /// <summary>What was frozen, in the words of the screen that froze it.</summary>
        public string Scope { get; }

        /// <summary>When, UTC.</summary>
        public DateTimeOffset When { get; }

        /// <summary>Who pressed the button.</summary>
        public string By { get; }

        /// <summary>Who put their name to it, when this is a sign-off.</summary>
        public string? Signatory { get; }

        /// <summary>The release reference, when this is a fabrication release.</summary>
        public string? Release { get; }

        /// <summary>The board groups it covered.</summary>
        public IReadOnlyList<string> Groups { get; }

        /// <summary>Where every element was.</summary>
        public IReadOnlyList<FrozenElement> Elements { get; }

        /// <summary>A hash over the contents, so an altered record no longer verifies.</summary>
        public string Hash { get; }

        /// <summary>True when the record still hashes to what it says it does.</summary>
        public bool IsIntact => string.Equals(Hash, HashOf(Elements), StringComparison.Ordinal);

        /// <summary>What kind of freeze it is, for a screen to say.</summary>
        public string Kind =>
            Release != null ? "released for fabrication"
            : Signatory != null ? "signed off"
            : "frozen";

        /// <summary>Make one.</summary>
        /// <param name="scope">What is being frozen, in words.</param>
        /// <param name="by">Who is pressing the button.</param>
        /// <param name="when">When, UTC. Passed in so a record written twice is the same record.</param>
        /// <param name="groups">The board groups it covers.</param>
        /// <param name="elements">Where every element is right now.</param>
        /// <param name="signatory">Who is putting their name to it, for a sign-off.</param>
        /// <param name="release">The release reference, for a fabrication release.</param>
        public static Freeze Of(string? scope, string? by, DateTimeOffset when,
                                IEnumerable<string>? groups, IEnumerable<FrozenElement>? elements,
                                string? signatory = null, string? release = null)
        {
            var members = (elements ?? Array.Empty<FrozenElement>())
                .Where(e => e != null)
                .GroupBy(e => e.Key.ToString(), StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal)
                .ToList();

            var named = (groups ?? Array.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(g => g, StringComparer.Ordinal)
                .ToList();

            var text = string.IsNullOrWhiteSpace(scope) ? "the whole board" : scope!.Trim();

            var hash = HashOf(members);

            return new Freeze(
                Identity.Hash.Of(12, text, when.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), hash),
                text,
                when,
                string.IsNullOrWhiteSpace(by) ? "somebody" : by!.Trim(),
                Trimmed(signatory),
                Trimmed(release),
                named,
                members,
                hash);
        }

        /// <summary>
        /// What has changed since, given where everything is now.
        /// </summary>
        /// <param name="now">Every element's extent in the model as it stands, keyed by element.</param>
        /// <param name="tolerance">
        /// How far something may move before it counts, in metres. A model re-export nudges
        /// coordinates in the last decimal place, and a change notice that reported every one of
        /// those would be a change notice nobody reads.
        /// </param>
        public ChangeNotice Since(IReadOnlyDictionary<string, BoundingBox>? now,
                                  double tolerance = ChangeNotice.DefaultTolerance)
        {
            var here = now ?? new Dictionary<string, BoundingBox>(StringComparer.Ordinal);
            var band = double.IsNaN(tolerance) || tolerance < 0 ? ChangeNotice.DefaultTolerance : tolerance;

            var changes = new List<FrozenChange>();

            foreach (var was in Elements)
            {
                if (!here.TryGetValue(was.Key.ToString(), out var bounds))
                {
                    changes.Add(new FrozenChange(was, 0, 0, missing: true));
                    continue;
                }

                var isNow = new FrozenElement(was.Key, bounds);

                var dx = isNow.MidX - was.MidX;
                var dy = isNow.MidY - was.MidY;
                var dz = isNow.MidZ - was.MidZ;

                var moved = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                var resized = Math.Max(Math.Abs(isNow.SizeX - was.SizeX),
                    Math.Max(Math.Abs(isNow.SizeY - was.SizeY), Math.Abs(isNow.SizeZ - was.SizeZ)));

                if (moved <= band && resized <= band) continue;

                changes.Add(new FrozenChange(was, moved > band ? moved : 0,
                                             resized > band ? resized : 0, missing: false));
            }

            return new ChangeNotice(this, changes, band);
        }

        /// <summary>How the freeze reads on a screen.</summary>
        public string Describe()
        {
            var s = Scope + " " + Kind + " on "
                  + When.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                  + " by " + By;

            if (Signatory != null) s += ", signed " + Signatory;
            if (Release != null) s += ", release " + Release;

            s += " - " + Measure.Count(Elements.Count)
               + (Elements.Count == 1 ? " element" : " elements");

            if (!IsIntact) s += " - THIS RECORD HAS BEEN ALTERED";

            return s;
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object()
                .Set("id", JsonValue.String(Id))
                .Set("scope", JsonValue.String(Scope))
                .Set("when", JsonValue.Number(When.ToUnixTimeSeconds()))
                .Set("by", JsonValue.String(By))
                .Set("hash", JsonValue.String(Hash))
                .Set("groups", JsonValue.Array(Groups.Select(JsonValue.String)))
                .Set("elements", JsonValue.Array(Elements.Select(e => e.ToJson())));

            if (Signatory != null) json.Set("signatory", JsonValue.String(Signatory));
            if (Release != null) json.Set("release", JsonValue.String(Release));

            return json;
        }

        /// <summary>Read one back, or null when it is not a freeze.</summary>
        /// <param name="json">What was written.</param>
        public static Freeze? FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return null;

            var id = json["id"].AsString();
            if (id == null) return null;

            var elements = json["elements"].Items
                .Select(FrozenElement.FromJson)
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();

            var groups = json["groups"].Items
                .Select(g => g.AsString())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!)
                .ToList();

            return new Freeze(
                id,
                json["scope"].AsString() ?? "the whole board",
                DateTimeOffset.FromUnixTimeSeconds(json["when"].AsLong()),
                json["by"].AsString() ?? "somebody",
                json["signatory"].AsString(),
                json["release"].AsString(),
                groups,
                elements,
                json["hash"].AsString() ?? string.Empty);
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        private static string? Trimmed(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text!.Trim();

        /// <summary>
        /// A hash over the members, in a fixed order, at the resolution they were stored at.
        ///
        /// Not a security measure — anybody who can edit the file can recompute it. It catches the
        /// ordinary case: a record damaged by a merge, a hand edit, or two people writing the
        /// project file at once, where the alternative is a document somebody signed quietly
        /// meaning something else.
        /// </summary>
        private static string HashOf(IReadOnlyList<FrozenElement> elements)
        {
            var parts = new List<string?>();

            foreach (var e in elements)
            {
                parts.Add(e.Key.ToString());
                parts.Add(Fixed(e.MidX)); parts.Add(Fixed(e.MidY)); parts.Add(Fixed(e.MidZ));
                parts.Add(Fixed(e.SizeX)); parts.Add(Fixed(e.SizeY)); parts.Add(Fixed(e.SizeZ));
            }

            return Identity.Hash.Of(16, parts.ToArray());
        }

        private static string Fixed(double value) =>
            FrozenElement.Round(value).ToString("0.0000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// What has moved since somebody put their name to it.
    ///
    /// The half of a freeze that earns it. A record that a level was signed off is a date in a
    /// file; a list of the eleven things that have moved since, with how far, is the thing that
    /// stops a hanger being fabricated to a drawing that is three weeks old.
    /// </summary>
    public sealed class ChangeNotice
    {
        /// <summary>How far something may move before it counts. A millimetre.</summary>
        public const double DefaultTolerance = 0.001;

        internal ChangeNotice(Freeze against, IReadOnlyList<FrozenChange> changes, double tolerance)
        {
            Against = against;
            Tolerance = tolerance;

            // Worst first, because a notice is read from the top and the thing that moved 400 mm is
            // the thing somebody has to know about. Gone-from-the-model outranks everything.
            Changes = changes
                .OrderByDescending(c => c.IsMissing)
                .ThenByDescending(c => c.Moved)
                .ThenByDescending(c => c.Resized)
                .ThenBy(c => c.Was.Key.ToString(), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The freeze this is measured against.</summary>
        public Freeze Against { get; }

        /// <summary>Everything that has changed, worst first.</summary>
        public IReadOnlyList<FrozenChange> Changes { get; }

        /// <summary>How far something had to move to count.</summary>
        public double Tolerance { get; }

        /// <summary>True when nothing frozen has moved.</summary>
        public bool IsClear => Changes.Count == 0;

        /// <summary>How many have gone from the model.</summary>
        public int Missing => Changes.Count(c => c.IsMissing);

        /// <summary>How many have moved.</summary>
        public int Moved => Changes.Count(c => c.Moved > 0);

        /// <summary>How many have changed size.</summary>
        public int Resized => Changes.Count(c => c.Resized > 0);

        /// <summary>The furthest anything has moved, in metres.</summary>
        public double Worst => Changes.Count == 0 ? 0 : Changes.Max(c => c.Moved);

        /// <summary>The one-line readout.</summary>
        /// <param name="units">How this project writes a length.</param>
        public string Explain(Measure? units = null)
        {
            var measure = units ?? Measure.Metric;

            if (IsClear)
                return "nothing frozen has moved - all " + Measure.Count(Against.Elements.Count)
                     + " elements are where they were on "
                     + Against.When.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

            var parts = new List<string>();

            if (Moved > 0) parts.Add(Measure.Count(Moved) + " moved");
            if (Resized > 0) parts.Add(Measure.Count(Resized) + " resized");
            if (Missing > 0) parts.Add(Measure.Count(Missing) + " gone from the model");

            var s = string.Join(", ", parts) + " since " + Against.Kind + " on "
                  + Against.When.UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

            if (Worst > 0) s += " - the furthest by " + measure.Length(Worst);

            return s;
        }

        /// <summary>The notice, as a report or a screen shows it.</summary>
        /// <param name="units">How this project writes a length.</param>
        /// <param name="name">How to read an element's key as a name, when the caller can.</param>
        public TableBlock Table(Measure? units = null, Func<ElementKey, string?>? name = null)
        {
            var measure = units ?? Measure.Metric;

            var table = new TableBlock("Element", "What changed",
                                       "Moved (" + measure.LengthUnit + ")",
                                       "Resized (" + measure.LengthUnit + ")");

            foreach (var change in Changes)
                table.Row(name?.Invoke(change.Was.Key) ?? change.Was.Key.ToString(),
                          change.Describe(measure),
                          change.Moved > 0 ? measure.Bare(change.Moved) : string.Empty,
                          change.Resized > 0 ? measure.Bare(change.Resized) : string.Empty);

            return table;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
