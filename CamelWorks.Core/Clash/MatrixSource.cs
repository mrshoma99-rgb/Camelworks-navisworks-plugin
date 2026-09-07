using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>Where the elements behind one row or column of the matrix come from.</summary>
    public enum MatrixSourceKind
    {
        /// <summary>Nothing yet. A row can be drawn before it is pointed at anything.</summary>
        None = 0,

        /// <summary>A saved selection set, by name.</summary>
        SelectionSet = 1,

        /// <summary>A saved search set, by name. Re-runs its search every time the test does.</summary>
        SearchSet = 2,

        /// <summary>Whole loaded models, by file name.</summary>
        Models = 3,

        /// <summary>Whatever was selected when the row was pointed at it, captured.</summary>
        CurrentSelection = 4,

        /// <summary>Whatever was visible when the row was pointed at it, captured.</summary>
        VisibleElements = 5,

        /// <summary>Branches picked out of the selection tree, captured.</summary>
        TreePick = 6,
    }

    /// <summary>
    /// What one row or column of the matrix is: a name, and where its elements come from.
    ///
    /// Two kinds of source, and the difference matters. A saved set and a list of models are
    /// references: the test re-resolves them every run, so a wall added tomorrow is in tomorrow's
    /// run. A selection, the visible elements, or a pick out of the tree are captures: a list of
    /// elements as they were when the row was pointed at them, and nothing added later joins. Both
    /// are legitimate — a package handed over as a fixed list of objects is a capture — but a matrix
    /// that did not record which was which would go stale without saying so.
    ///
    /// Captures hold keys rather than a live selection, because the matrix is a document that
    /// outlives the selection it was drawn from, is saved into the project and is read back later.
    /// </summary>
    public sealed class MatrixSource
    {
        private MatrixSource(MatrixSourceKind kind, string? name,
                             IReadOnlyList<string>? scopes, IReadOnlyList<ElementKey>? keys,
                             string? folder = null)
        {
            Kind = kind;
            Folder = (folder ?? string.Empty).Trim().Trim('/');
            Name = (name ?? string.Empty).Trim();
            Scopes = scopes ?? Array.Empty<string>();
            Keys = keys ?? Array.Empty<ElementKey>();
        }

        /// <summary>Which kind of source it is.</summary>
        public MatrixSourceKind Kind { get; }

        /// <summary>The set's name, for the two set kinds. Empty otherwise.</summary>
        public string Name { get; }

        /// <summary>
        /// The folders the set sits in, innermost last, joined with slashes. Empty when the set
        /// is at the top or when a matrix written before this was recorded is being read.
        /// </summary>
        public string Folder { get; }

        /// <summary>The model file names, for <see cref="MatrixSourceKind.Models"/>.</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>The captured elements, for the three capturing kinds.</summary>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>Nothing is behind this row yet, so no test on it could run.</summary>
        public bool IsEmpty =>
            Kind == MatrixSourceKind.None
            || (Kind == MatrixSourceKind.SelectionSet && Name.Length == 0)
            || (Kind == MatrixSourceKind.SearchSet && Name.Length == 0)
            || (Kind == MatrixSourceKind.Models && Scopes.Count == 0)
            || (Capturing && Keys.Count == 0);

        /// <summary>Whether this is a captured list rather than something that re-resolves.</summary>
        public bool Capturing =>
            Kind == MatrixSourceKind.CurrentSelection
            || Kind == MatrixSourceKind.VisibleElements
            || Kind == MatrixSourceKind.TreePick;

        /// <summary>Whether this names a saved set, of either kind.</summary>
        public bool IsSet =>
            Kind == MatrixSourceKind.SelectionSet || Kind == MatrixSourceKind.SearchSet;

        /// <summary>Nothing chosen.</summary>
        public static MatrixSource Nothing { get; } =
            new MatrixSource(MatrixSourceKind.None, null, null, null);

        /// <summary>A saved selection set.</summary>
        /// <param name="name">The set's name in the host.</param>
        /// <param name="folder">The folders it sits in, innermost last, or null.</param>
        public static MatrixSource SelectionSet(string name, string? folder = null) =>
            new MatrixSource(MatrixSourceKind.SelectionSet, name, null, null, folder);

        /// <summary>A saved search set, which re-runs its search on every run.</summary>
        /// <param name="name">The set's name in the host.</param>
        /// <param name="folder">The folders it sits in, innermost last, or null.</param>
        public static MatrixSource SearchSet(string name, string? folder = null) =>
            new MatrixSource(MatrixSourceKind.SearchSet, name, null, null, folder);

        /// <summary>Whole loaded models.</summary>
        /// <param name="scopes">Their file names.</param>
        public static MatrixSource Models(IReadOnlyList<string> scopes) =>
            new MatrixSource(MatrixSourceKind.Models, null, scopes, null);

        /// <summary>A captured list of elements.</summary>
        /// <param name="kind">Which capturing kind it was.</param>
        /// <param name="keys">The elements, as they were.</param>
        public static MatrixSource Captured(MatrixSourceKind kind, IReadOnlyList<ElementKey> keys) =>
            new MatrixSource(kind, null, null, keys);

        /// <summary>What this reads as on a header and in the sanity check.</summary>
        public string Describe() => Kind switch
        {
            MatrixSourceKind.SelectionSet => Name.Length == 0 ? "a selection set" : Named(true),
            MatrixSourceKind.SearchSet => Name.Length == 0 ? "a search set" : Named(true),
            MatrixSourceKind.Models => Scopes.Count == 0 ? "models"
                : Scopes.Count == 1 ? Scopes[0]
                : Scopes.Count + " models",
            MatrixSourceKind.CurrentSelection => Keys.Count + " selected elements",
            MatrixSourceKind.VisibleElements => Keys.Count + " visible elements",
            MatrixSourceKind.TreePick => Keys.Count + " elements from the tree",
            _ => "nothing yet",
        };

        /// <summary>What kind of thing this is, in one word, for a header's second line.</summary>
        public string Kinded() => Kind switch
        {
            MatrixSourceKind.SelectionSet => "selection set",
            MatrixSourceKind.SearchSet => "search set",
            MatrixSourceKind.Models => "models",
            MatrixSourceKind.CurrentSelection => "captured selection",
            MatrixSourceKind.VisibleElements => "captured visible",
            MatrixSourceKind.TreePick => "captured from the tree",
            _ => "not pointed at anything",
        };

        /// <summary>This source as the side of a clash test.</summary>
        public ClashSide AsSide() => Kind switch
        {
            MatrixSourceKind.SelectionSet => ClashSide.Set(Name, Folder),
            MatrixSourceKind.SearchSet => ClashSide.Set(Name, Folder),
            MatrixSourceKind.Models => ClashSide.Models(Scopes),
            _ => ClashSide.Elements(Keys),
        };

        /// <summary>
        /// What makes two sources the same source, for spotting the cell that plans a test some
        /// other cell already plans.
        ///
        /// A capture is identified by its kind and its element count rather than by its contents:
        /// comparing two thousand keys to decide whether to draw a warning is not worth the walk,
        /// and two captures of different things that happen to be the same size are a warning
        /// somebody can read and dismiss.
        /// </summary>
        public string Fingerprint() => _fingerprint ??= Printed();

        private string? _fingerprint;

        private string Printed() => Kind switch
        {
            MatrixSourceKind.SelectionSet => "set:" + Named(),
            MatrixSourceKind.SearchSet => "set:" + Named(),
            MatrixSourceKind.Models => "models:" + string.Join("|",
                Scopes.Select(s => s.ToLowerInvariant()).OrderBy(s => s, StringComparer.Ordinal)),
            MatrixSourceKind.None => "none",
            _ => "captured:" + (int)Kind + ":" + Keys.Count + ":" + Digest(Keys),
        };

        /// <summary>
        /// A fingerprint of the elements a captured source holds.
        ///
        /// This was previously the count of them. Two rows pointing at two different tree picks that
        /// held the same number of elements were, to everything downstream, the same source, and
        /// three things went wrong at once and silently. The planner drops a pair whose fingerprint
        /// it has already seen, so the second row's tests were never built: a cell showing a symbol,
        /// counted in the total, producing nothing. The difference matched the second pair against
        /// the first one's test. And the mirror wrote one row's cells into another's.
        ///
        /// Sorted, so the same elements captured in a different order are the same source. FNV-1a
        /// rather than the string hash, because that one is seeded per process in modern .NET and a
        /// fingerprint that changed on every restart would match nothing in a saved document.
        /// </summary>
        /// <param name="keys">The captured elements.</param>
        private static string Digest(IReadOnlyList<ElementKey> keys)
        {
            var hash = 14695981039346656037UL;

            foreach (var word in keys.Select(k => k.ToString())
                                     .OrderBy(w => w, StringComparer.Ordinal))
            {
                foreach (var letter in word)
                {
                    hash ^= letter;
                    hash *= 1099511628211UL;
                }

                // A break between one key and the next, so two keys running together cannot
                // fingerprint the same as one longer one.
                hash ^= 0x1F;
                hash *= 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The set as this source names it: its folders and its name when both are known.
        ///
        /// Used for telling two sources apart, which has to be exact. What a test in the
        /// document is matched against is <see cref="Matched"/>, because the document only ever
        /// reports a set's name.
        /// </summary>
        /// <param name="read">True to keep it as it is written, for somebody to read.</param>
        private string Named(bool read = false)
        {
            var said = Folder.Length == 0 ? Name : Folder + "/" + Name;

            return read ? said : said.ToLowerInvariant();
        }

        /// <summary>
        /// The form of this source that can be compared with a test the document already holds.
        ///
        /// The document does not record which folder a test's set came from; it reports a name. So
        /// matching a plan against it happens on names, while telling one row from another happens
        /// on the whole path. Two different questions with two answers; folding them into one made a
        /// matrix either lose its tests or lose its results, depending on which way it was folded.
        /// </summary>
        public string Matched() => Kind switch
        {
            MatrixSourceKind.SelectionSet => "set:" + Name.ToLowerInvariant(),
            MatrixSourceKind.SearchSet => "set:" + Name.ToLowerInvariant(),
            _ => Fingerprint(),
        };

        /// <summary>Write it down.</summary>
        public JsonValue ToJson()
        {
            var json = JsonValue.Object().Set("kind", (double)(int)Kind);

            if (Name.Length > 0) json.Set("name", Name);

            if (Folder.Length > 0) json.Set("folder", Folder);

            if (Scopes.Count > 0)
                json.Set("scopes", JsonValue.Array(Scopes.Select(JsonValue.String)));

            if (Keys.Count > 0)
                json.Set("keys", JsonValue.Array(
                    Keys.Select(k => JsonValue.String(k.ToString()))));

            return json;
        }

        /// <summary>Read one back, or nothing when there is nothing to read.</summary>
        /// <param name="json">The stored source, or null.</param>
        public static MatrixSource FromJson(JsonValue? json)
        {
            if (json == null || json.Kind != JsonKind.Object) return Nothing;

            var kind = (MatrixSourceKind)(int)json["kind"].AsDouble();

            var scopes = json["scopes"].Kind == JsonKind.Array
                ? json["scopes"].Items.Select(i => i.AsString() ?? string.Empty)
                                      .Where(s => s.Length > 0).ToList()
                : new List<string>();

            var keys = new List<ElementKey>();

            if (json["keys"].Kind == JsonKind.Array)
                foreach (var held in json["keys"].Items)
                    if (ElementKey.TryParse(held.AsString(), out var key) && !key.IsEmpty)
                        keys.Add(key);

            return new MatrixSource(kind, json["name"].AsString(), scopes, keys,
                                    json["folder"].AsString());
        }

        /// <inheritdoc />
        public override string ToString() => Describe();
    }
}
