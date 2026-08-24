using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Appearance
{
    /// <summary>What one element looked like, and enough to find it again somewhere else.</summary>
    public sealed class AppearanceEntry
    {
        /// <summary>What joins the display names of a tree path.</summary>
        public const string PathSeparator = " > ";

        /// <summary>Create an entry.</summary>
        /// <param name="key">The element, as the model it came from identified it.</param>
        /// <param name="path">Its display-name path, for finding it in a model that keys differently.</param>
        /// <param name="colour">Its colour override, or null.</param>
        /// <param name="transparency">Its transparency override, or null.</param>
        /// <param name="isHidden">Whether it was hidden.</param>
        public AppearanceEntry(ElementKey key, string? path, Colour? colour, double? transparency,
                               bool isHidden)
        {
            Key = key;
            Path = string.IsNullOrWhiteSpace(path) ? null : path!.Trim();
            Colour = colour;
            Transparency = transparency;
            IsHidden = isHidden;
        }

        /// <summary>The element as the source model identified it.</summary>
        public ElementKey Key { get; }

        /// <summary>
        /// The display-name path, which is the fallback when the key does not resolve.
        ///
        /// The key is exact and does not travel: a re-export renumbers, a different discipline's
        /// model of the same building keys nothing the same way. The path is what a person would
        /// use to find the thing again, and it is right far more often than it is wrong.
        /// </summary>
        public string? Path { get; }

        /// <summary>Its colour override, or null.</summary>
        public Colour? Colour { get; }

        /// <summary>Its transparency override, or null.</summary>
        public double? Transparency { get; }

        /// <summary>Whether it was hidden.</summary>
        public bool IsHidden { get; }

        /// <summary>True when the element carried nothing worth taking.</summary>
        public bool IsEmpty => Colour == null && Transparency == null && !IsHidden;

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("key", Key.ToString());

            if (Path != null) json.Set("path", Path);
            if (Colour != null) json.Set("colour", Colour.Value.ToString());
            if (Transparency != null) json.Set("transparency", Transparency.Value);
            if (IsHidden) json.Set("hidden", true);

            return json;
        }

        /// <summary>Read one back, or null when it names no element.</summary>
        public static AppearanceEntry? FromJson(JsonValue? json)
        {
            if (json == null) return null;
            if (!ElementKey.TryParse(json["key"].AsString(), out var key)) return null;

            Colour? colour = null;
            if (Abstractions.Colour.TryParse(json["colour"].AsString(), out var read)) colour = read;

            var transparency = json.Has("transparency")
                ? json["transparency"].AsDouble()
                : (double?)null;

            return new AppearanceEntry(key, json["path"].AsString(), colour, transparency,
                                       json["hidden"].AsBool());
        }

        /// <inheritdoc />
        public override string ToString() => Path ?? Key.ToString();
    }

    /// <summary>A model's overrides, written down so they can be replayed somewhere else.</summary>
    public sealed class AppearanceSnapshot
    {
        /// <summary>What version of this file this build writes and reads.</summary>
        public const int SchemaVersion = 1;

        private readonly List<AppearanceEntry> _entries = new List<AppearanceEntry>();

        /// <summary>Which model it was taken from.</summary>
        public string? SourceModel { get; set; }

        /// <summary>When, in ticks. Zero when it was never recorded.</summary>
        public long WhenTicks { get; set; }

        /// <summary>What it is, in the author's words.</summary>
        public string? Note { get; set; }

        /// <summary>The elements.</summary>
        public IList<AppearanceEntry> Entries => _entries;

        /// <summary>How many carried a colour.</summary>
        public int Coloured => _entries.Count(e => e.Colour != null);

        /// <summary>How many carried a transparency.</summary>
        public int Transparent => _entries.Count(e => e.Transparency != null);

        /// <summary>How many were hidden.</summary>
        public int Hidden => _entries.Count(e => e.IsHidden);

        /// <summary>How many carry a path to fall back on.</summary>
        public int Pathed => _entries.Count(e => e.Path != null);

        /// <summary>True when there is something to replay.</summary>
        public bool IsUsable => _entries.Count > 0;

        /// <summary>The one-line readout.</summary>
        public string Explain()
        {
            if (_entries.Count == 0) return "Nothing in this model is overridden.";

            var parts = new List<string>();

            if (Coloured > 0) parts.Add(Measure.Count(Coloured) + " coloured");
            if (Transparent > 0) parts.Add(Measure.Count(Transparent) + " transparent");
            if (Hidden > 0) parts.Add(Measure.Count(Hidden) + " hidden");

            return Measure.Count(_entries.Count) + " elements - "
                 + string.Join(", ", parts) + ".";
        }

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("schema", SchemaVersion);

            if (SourceModel != null) json.Set("model", SourceModel);
            if (WhenTicks > 0) json.Set("when", WhenTicks);
            if (Note != null) json.Set("note", Note);

            return json.Set("elements", JsonValue.Array(_entries.Select(e => e.ToJson())));
        }

        /// <summary>Read one back.</summary>
        public static AppearanceSnapshot FromJson(JsonValue? json)
        {
            var snapshot = new AppearanceSnapshot();

            if (json == null) return snapshot;

            snapshot.SourceModel = json["model"].AsString();
            snapshot.WhenTicks = json["when"].AsLong();
            snapshot.Note = json["note"].AsString();

            foreach (var item in json["elements"].Items)
            {
                var entry = AppearanceEntry.FromJson(item);
                if (entry != null && !entry.IsEmpty) snapshot._entries.Add(entry);
            }

            return snapshot;
        }

        /// <summary>
        /// Whether this file is one this build understands.
        ///
        /// A version from the future is refused rather than read as far as it goes: a snapshot half
        /// applied is a model somebody has to put back by hand, and the half that was dropped is
        /// invisible.
        /// </summary>
        /// <param name="json">The file, parsed.</param>
        public static string? Refuses(JsonValue? json)
        {
            if (json == null) return "There is nothing in that file.";

            var version = (int)json["schema"].AsLong();

            if (version == 0 && !json.Has("elements"))
                return "That file is not an appearance snapshot - it has no elements in it.";

            if (version > SchemaVersion)
                return "That snapshot was written by a newer CamelWorks (version "
                     + version.ToString(CultureInfo.InvariantCulture) + "; this one reads "
                     + SchemaVersion.ToString(CultureInfo.InvariantCulture) + "). Reading it as far "
                     + "as it goes would leave half a scheme applied and no way to see which half.";

            return null;
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>How a snapshot is replayed.</summary>
    public sealed class TransferOptions
    {
        /// <summary>Whether colours are carried.</summary>
        public bool Colours { get; set; } = true;

        /// <summary>Whether transparency is carried.</summary>
        public bool Transparency { get; set; } = true;

        /// <summary>
        /// Whether hiding is carried.
        ///
        /// Separable from the rest because they are separable decisions: a colour scheme is a way
        /// of reading a model, and a hidden element is a claim that something should not be looked
        /// at. Somebody taking the first rarely wants the second.
        /// </summary>
        public bool Hidden { get; set; } = true;

        /// <summary>Whether an element the key cannot find may be matched by its path instead.</summary>
        public bool ByPath { get; set; } = true;
    }

    /// <summary>What a snapshot found when it was held up against a model.</summary>
    public sealed class TransferMatch
    {
        internal TransferMatch(IReadOnlyList<AppearanceEntry> byKey, IReadOnlyList<AppearanceEntry> byPath,
                               IReadOnlyList<AppearanceEntry> missing, int ambiguous,
                               IReadOnlyDictionary<AppearanceEntry, ElementKey> landed)
        {
            ByKey = byKey; ByPath = byPath; Missing = missing; Ambiguous = ambiguous; Landed = landed;
        }

        /// <summary>Entries the target model had under the same key.</summary>
        public IReadOnlyList<AppearanceEntry> ByKey { get; }

        /// <summary>Entries found only by their display path.</summary>
        public IReadOnlyList<AppearanceEntry> ByPath { get; }

        /// <summary>Entries the target model has nothing for.</summary>
        public IReadOnlyList<AppearanceEntry> Missing { get; }

        /// <summary>How many paths matched more than one element and were left alone.</summary>
        public int Ambiguous { get; }

        /// <summary>Where each matched entry landed.</summary>
        public IReadOnlyDictionary<AppearanceEntry, ElementKey> Landed { get; }

        /// <summary>How many landed at all.</summary>
        public int Found => ByKey.Count + ByPath.Count;

        /// <summary>True when there is something to apply.</summary>
        public bool IsUsable => Found > 0;

        /// <summary>The one-line readout, which always says what did not land.</summary>
        public string Explain()
        {
            var total = Found + Missing.Count;

            if (total == 0) return "That snapshot is empty.";

            if (Found == 0)
                return "None of " + Measure.Count(total) + " elements in that snapshot are in this "
                     + "model. Usually it was taken from a different federation - or from the same "
                     + "one before it was re-exported, with the path fallback switched off.";

            var line = Measure.Count(Found) + " of " + Measure.Count(total) + " found";

            if (ByPath.Count > 0)
                line += ", " + Measure.Count(ByPath.Count) + " of them by name rather than by identity";

            if (Missing.Count > 0)
                line += Missing.Count == 1
                    ? "; one is not here"
                    : "; " + Measure.Count(Missing.Count) + " are not here";

            if (Ambiguous > 0)
                line += "; " + Measure.Count(Ambiguous) + " matched more than one element and were "
                      + "left alone";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }

    /// <summary>
    /// A colour scheme, taken off one model and put onto another.
    ///
    /// The layer stack is better than anything else here at deciding what a model looks like, and
    /// it cannot leave the machine. A scheme agreed in a workshop - services by system, structure
    /// by phase, everything else at eighty per cent - is an afternoon's work that exists in one
    /// person's document and dies with it.
    ///
    /// <b>Identity first, name second.</b> A key is exact and does not travel: a re-export
    /// renumbers, and another discipline's model of the same building keys nothing the same way.
    /// So the snapshot carries the display path too, and an element the key cannot find is looked
    /// for by the path a person would have used. Which way each one landed is reported, because
    /// "found by name" is a guess and the reader is entitled to know how many of them there were.
    ///
    /// <b>It arrives as a layer, not as a set of writes.</b> Replayed through the stack, it sits
    /// above or below what is already there, can be switched off, and can be taken back - where a
    /// direct write would be indistinguishable from the overrides it landed on top of.
    /// </summary>
    public static class AppearanceTransfer
    {
        /// <summary>Take the overrides off a model.</summary>
        /// <param name="states">What the host says every element is in.</param>
        /// <param name="paths">Each element's display-name path, where the caller can supply one.</param>
        public static AppearanceSnapshot Capture(IEnumerable<AppearanceState>? states,
                                                 IReadOnlyDictionary<ElementKey, string>? paths = null)
        {
            var snapshot = new AppearanceSnapshot();

            foreach (var state in states ?? Array.Empty<AppearanceState>())
            {
                // Pristine elements are the whole model. A snapshot of them would be a file the
                // size of the federation saying nothing.
                if (state.IsPristine) continue;

                // Read into a local rather than an out of a different nullability: the dictionary's
                // value is a plain string and this needs one that may be absent.
                string? path = null;

                if (paths != null && paths.TryGetValue(state.Key, out var known)) path = known;

                snapshot.Entries.Add(new AppearanceEntry(state.Key, path, state.Colour,
                                                         state.Transparency, state.IsHidden));
            }

            return snapshot;
        }

        /// <summary>Hold a snapshot up against a model.</summary>
        /// <param name="snapshot">What was taken.</param>
        /// <param name="present">Every element in the target, with its display path where known.</param>
        /// <param name="options">Whether the path fallback is allowed.</param>
        public static TransferMatch Match(AppearanceSnapshot? snapshot,
                                          IEnumerable<KeyValuePair<ElementKey, string?>>? present,
                                          TransferOptions? options = null)
        {
            var how = options ?? new TransferOptions();

            var keys = new HashSet<ElementKey>();
            var byPath = new Dictionary<string, ElementKey>(StringComparer.OrdinalIgnoreCase);
            var crowded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in present ?? Array.Empty<KeyValuePair<ElementKey, string?>>())
            {
                keys.Add(pair.Key);

                var path = (pair.Value ?? string.Empty).Trim();
                if (path.Length == 0) continue;

                // A path two elements share cannot decide between them, and picking the first is
                // how a scheme lands on the wrong wall.
                if (byPath.ContainsKey(path)) crowded.Add(path);
                else byPath[path] = pair.Key;
            }

            var found = new List<AppearanceEntry>();
            var named = new List<AppearanceEntry>();
            var missing = new List<AppearanceEntry>();
            var landed = new Dictionary<AppearanceEntry, ElementKey>();
            var ambiguous = 0;

            foreach (var entry in snapshot?.Entries ?? (IList<AppearanceEntry>)Array.Empty<AppearanceEntry>())
            {
                if (keys.Contains(entry.Key))
                {
                    found.Add(entry);
                    landed[entry] = entry.Key;
                    continue;
                }

                var path = entry.Path;

                if (!how.ByPath || path == null) { missing.Add(entry); continue; }

                if (crowded.Contains(path)) { ambiguous++; missing.Add(entry); continue; }

                if (byPath.TryGetValue(path, out var key))
                {
                    named.Add(entry);
                    landed[entry] = key;
                    continue;
                }

                missing.Add(entry);
            }

            return new TransferMatch(found, named, missing, ambiguous, landed);
        }

        /// <summary>
        /// The layers a match would add.
        ///
        /// One per distinct appearance rather than one per element: a scheme of four colours over
        /// nine thousand elements is four layers somebody can read, switch off and argue with, not
        /// nine thousand rows.
        /// </summary>
        /// <param name="match">What landed.</param>
        /// <param name="options">Which properties are carried.</param>
        /// <param name="stem">What each layer's id starts with.</param>
        /// <param name="name">What the scheme is called, for the layer names.</param>
        public static IReadOnlyList<AppearanceLayer> Layers(TransferMatch? match,
                                                            TransferOptions? options = null,
                                                            string stem = "transfer",
                                                            string? name = null)
        {
            var how = options ?? new TransferOptions();
            var layers = new List<AppearanceLayer>();

            if (match == null) return layers;

            var called = string.IsNullOrWhiteSpace(name) ? "Brought in" : name!.Trim();
            var id = string.IsNullOrWhiteSpace(stem) ? "transfer" : stem!.Trim();

            var groups = match.ByKey.Concat(match.ByPath)
                .Select(e => (Entry: e, Shape: Shape(e, how)))
                .Where(x => x.Shape != null)
                .Select(x => (x.Entry, Shape: x.Shape!))
                .GroupBy(x => x.Shape, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var n = 0;

            foreach (var group in groups)
            {
                var first = group.First().Entry;

                var wanted = group
                    .Select(x => match.Landed.TryGetValue(x.Entry, out var key) ? key : x.Entry.Key)
                    .Distinct()
                    .ToList();

                var describe = Describe(first, how);

                var layer = new AppearanceLayer(id + "-" + (++n).ToString(CultureInfo.InvariantCulture),
                                                called + ": " + describe,
                                                LayerTarget.Elements(wanted, describe))
                {
                    // Visibility is claimed only where the snapshot actually hid something. A layer
                    // asserting "visible" on everything it touched would overrule every layer under
                    // it, which is not what bringing a colour scheme in means.
                    Visible = how.Hidden && first.IsHidden ? false : (bool?)null,
                    Colour = how.Colours ? first.Colour : null,
                    Transparency = how.Transparency ? first.Transparency : null,
                    Note = "Brought in from " + describe + ". Switch it off and the model is back "
                         + "to what it was.",
                };

                if (!layer.IsEmpty) layers.Add(layer);
            }

            return layers;
        }

        /// <summary>The appearance an entry carries, as a key that groups identical ones.</summary>
        private static string? Shape(AppearanceEntry entry, TransferOptions how)
        {
            var hidden = how.Hidden && entry.IsHidden;
            var colour = how.Colours ? entry.Colour : null;
            var transparency = how.Transparency ? entry.Transparency : null;

            if (!hidden && colour == null && transparency == null) return null;

            return (hidden ? "h" : "-")
                 + "|" + (colour == null
                     ? "-"
                     : colour.Value.Packed.ToString(CultureInfo.InvariantCulture))
                 + "|" + (transparency == null
                     ? "-"
                     : Quantise(transparency.Value).ToString(CultureInfo.InvariantCulture));
        }

        private static string Describe(AppearanceEntry entry, TransferOptions how)
        {
            var parts = new List<string>();

            if (how.Hidden && entry.IsHidden) parts.Add("hidden");

            if (how.Colours && entry.Colour != null) parts.Add("coloured " + entry.Colour.Value);

            if (how.Transparency && entry.Transparency != null)
                parts.Add((entry.Transparency.Value * 100).ToString("0", CultureInfo.InvariantCulture)
                          + "% transparent");

            return parts.Count == 0 ? "untouched" : string.Join(" and ", parts);
        }

        // The planner's resolution, so a transparency written as 0.5 and read back as 128/255 does
        // not split one scheme into two layers that look identical.
        private static int Quantise(double transparency) =>
            (int)Math.Round(Math.Max(0, Math.Min(1, transparency)) * AppearancePlanner.TransparencySteps);
    }
}
