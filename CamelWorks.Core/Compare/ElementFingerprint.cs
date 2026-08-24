using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>Which properties a snapshot keeps, and so what a comparison can say about them.</summary>
    public enum PropertyWatch
    {
        /// <summary>
        /// None. The snapshot is geometry, name, category and type only.
        ///
        /// The cheapest and smallest, and still answers most of the question: added, removed,
        /// moved, resized, rotated, renamed and retyped are all decidable without reading a single
        /// property.
        /// </summary>
        None = 0,

        /// <summary>
        /// The built-in list — <see cref="ComparePolicy.StandardNames"/>.
        ///
        /// The default, and the reason a comparison works on a raw model with nothing configured.
        /// Bounded by construction: a fixed set of property NAMES matched in any category, so the
        /// per-element cost is the same on a Revit federation, an IFC import and a DWG, and a model
        /// carrying none of them still compares fully on geometry and identity.
        /// </summary>
        Standard = 1,

        /// <summary>
        /// Every property every element carries.
        ///
        /// Complete, and the expensive one: a Revit-sourced element carries sixty-odd properties,
        /// so a two-hundred-thousand element federation is twelve million values. Offered because
        /// some jobs genuinely need it; never the default, and the screen says what it will cost.
        /// </summary>
        All = 2,

        /// <summary>Exactly the properties named in <see cref="CompareSettings.Watched"/>.</summary>
        Named = 3,
    }

    /// <summary>Names one property, as a comparison refers to it.</summary>
    public readonly struct PropertyRef : IEquatable<PropertyRef>
    {
        private readonly string? _category;
        private readonly string? _name;

        /// <summary>
        /// The property tab.
        ///
        /// Read through a field rather than held in an auto-property: a struct has a value nobody
        /// constructs, and <c>default(PropertyRef)</c> would give null from a property normalised
        /// only in the constructor - which <see cref="IsEmpty"/> right below would then throw on.
        /// </summary>
        public string Category => _category ?? string.Empty;

        /// <summary>The property name.</summary>
        public string Name => _name ?? string.Empty;

        /// <summary>Create a reference.</summary>
        /// <param name="category">The property tab.</param>
        /// <param name="name">The property name.</param>
        public PropertyRef(string? category, string? name)
        {
            _category = category;
            _name = name;
        }

        /// <summary>True for <c>default(PropertyRef)</c>.</summary>
        public bool IsEmpty => Name.Length == 0 && Category.Length == 0;

        /// <inheritdoc />
        public bool Equals(PropertyRef other) =>
            string.Equals(Category, other.Category, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PropertyRef p && Equals(p);

        /// <inheritdoc />
        public override int GetHashCode() =>
            StringComparer.OrdinalIgnoreCase.GetHashCode(Category) * 31
            ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

        /// <summary>Value equality.</summary>
        public static bool operator ==(PropertyRef a, PropertyRef b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(PropertyRef a, PropertyRef b) => !a.Equals(b);

        /// <summary><c>Category / Name</c>, or just the name when there is no category.</summary>
        public override string ToString() =>
            Category.Length == 0 ? Name : Category + " / " + Name;

        /// <summary>
        /// Parse what a person typed: <c>Category/Name</c>, or a bare name meaning "this name in
        /// any category".
        ///
        /// For a value being read back off disk use <see cref="FromStorage"/> instead — a property
        /// name may itself contain a slash, and only the stored form says where the category ends.
        /// </summary>
        /// <param name="text">The text to read.</param>
        public static PropertyRef Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return default;

            var s = text!.Trim();
            var slash = s.IndexOf('/');

            return slash < 0
                ? new PropertyRef(null, s)
                : new PropertyRef(s.Substring(0, slash).Trim(), s.Substring(slash + 1).Trim());
        }

        /// <summary>
        /// The unambiguous written form: the category, a slash, then the name verbatim.
        ///
        /// Always both halves, so a name that itself contains a slash — <c>L/100</c> is a real
        /// property on real models — reads back as the name it is rather than as a category
        /// nobody wrote.
        /// </summary>
        public string ToStorage() => Category + "/" + Name;

        /// <summary>Read back what <see cref="ToStorage"/> wrote.</summary>
        /// <param name="text">The stored form.</param>
        public static PropertyRef FromStorage(string? text)
        {
            if (string.IsNullOrEmpty(text)) return default;

            var slash = text!.IndexOf('/');

            return slash < 0
                ? new PropertyRef(null, text)
                : new PropertyRef(text.Substring(0, slash), text.Substring(slash + 1));
        }
    }

    /// <summary>One property as a snapshot recorded it.</summary>
    public readonly struct WatchedValue
    {
        /// <summary>Which property.</summary>
        public PropertyRef Property { get; }

        private readonly string? _value;

        /// <summary>What it held, truncated to <see cref="ComparePolicy.MaxValueLength"/>.</summary>
        public string Value => _value ?? string.Empty;

        /// <summary>Create a reading.</summary>
        /// <param name="property">Which property.</param>
        /// <param name="value">What it held.</param>
        public WatchedValue(PropertyRef property, string? value)
        {
            Property = property;
            _value = value;
        }

        /// <inheritdoc />
        public override string ToString() => Property + " = " + Value;
    }

    /// <summary>
    /// One element, as a snapshot remembers it.
    ///
    /// <b>Two identities, deliberately.</b> <see cref="Scoped"/> is the ordinary
    /// <see cref="ElementKey"/> value, which is scoped to the model's source path and is the right
    /// key whenever both sides came from the same file. <see cref="Local"/> is the same rung
    /// computed WITHOUT that scope, which is the only thing that can match an element in
    /// <c>STR-R03.nwc</c> to the same element in <c>STR-R04.nwc</c> — different paths, therefore
    /// different scopes, therefore no scoped key on one side can ever equal one on the other.
    ///
    /// <see cref="ElementKey.UnscopedValue"/> documents itself as diagnostic and never a match key,
    /// and that rule still holds everywhere else: a bare unscoped value would happily match two
    /// unrelated models that happen to share a tree path. It is safe here and only here because a
    /// comparison matches inside an explicit <see cref="ModelPairing"/> — somebody, or a rule that
    /// says why, has already decided these two models are two revisions of one thing.
    /// </summary>
    public sealed class ElementFingerprint
    {
        /// <summary>Create a fingerprint.</summary>
        /// <param name="rung">Which rung produced the identity.</param>
        /// <param name="local">The rung value without the model scope.</param>
        /// <param name="scoped">The rung value with it.</param>
        /// <param name="modelScope">The owning model's scope hash.</param>
        public ElementFingerprint(KeyRung rung, string local, string scoped, string modelScope)
        {
            Rung = rung;
            Local = local ?? string.Empty;
            Scoped = scoped ?? string.Empty;
            ModelScope = modelScope ?? string.Empty;
        }

        /// <summary>Which rung produced the identity. Lower is stronger.</summary>
        public KeyRung Rung { get; }

        /// <summary>The rung value WITHOUT the model scope — the cross-revision match key.</summary>
        public string Local { get; }

        /// <summary>The rung value WITH the model scope — the match key when both sides are one file.</summary>
        public string Scoped { get; }

        /// <summary>The owning model's scope hash.</summary>
        public string ModelScope { get; }

        /// <summary>How the host names it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Its category, where the source supplied one.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Its type or family name, where the source supplied one.</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Bounding-box centre, X.</summary>
        public double CentreX { get; set; }

        /// <summary>Bounding-box centre, Y.</summary>
        public double CentreY { get; set; }

        /// <summary>Bounding-box centre, Z.</summary>
        public double CentreZ { get; set; }

        /// <summary>Bounding-box extent along X, as the box sits in the world.</summary>
        public double SizeX { get; set; }

        /// <summary>Bounding-box extent along Y, as the box sits in the world.</summary>
        public double SizeY { get; set; }

        /// <summary>Bounding-box extent along Z, as the box sits in the world.</summary>
        public double SizeZ { get; set; }

        /// <summary>True when the element carries geometry rather than being a grouping node.</summary>
        public bool HasGeometry { get; set; }

        /// <summary>
        /// A hash of the element's actual triangles, or null when nobody paid for one.
        ///
        /// Null is the ordinary case and is not a gap: the bounding box already decides moved,
        /// resized and rotated. This exists for the one thing a box cannot see — a remodelled
        /// element that keeps its envelope — and reading it means a geometry callback per element,
        /// which is minutes on a federation. Off unless asked for, and asked for on a subset.
        /// </summary>
        public string? MeshHash { get; set; }

        /// <summary>
        /// The watched properties, always sorted by category and then name.
        ///
        /// <b>The sort is an invariant, not a tidy-up.</b> Comparing two elements' properties is a
        /// merge-walk of two ordered lists, which is silently wrong on an unordered one — it
        /// reports properties as arrived and departed that were only in a different place. Sorting
        /// on the way in means a snapshot somebody has hand-edited still compares correctly.
        /// </summary>
        public IReadOnlyList<WatchedValue> Values
        {
            get => _values;
            set => _values = Sorted(value);
        }

        private IReadOnlyList<WatchedValue> _values = Array.Empty<WatchedValue>();

        /// <summary>The comparison the sort and the merge-walk both use.</summary>
        /// <param name="a">One property.</param>
        /// <param name="b">The other.</param>
        public static int Order(PropertyRef a, PropertyRef b)
        {
            var byCategory = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return byCategory != 0
                ? byCategory
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<WatchedValue> Sorted(IReadOnlyList<WatchedValue>? values)
        {
            if (values == null || values.Count == 0) return Array.Empty<WatchedValue>();

            var copy = values.ToArray();
            Array.Sort(copy, static (a, b) => Order(a.Property, b.Property));
            return copy;
        }

        /// <summary>
        /// The element's key, rebuilt from what was written down.
        ///
        /// Goes back through <see cref="ElementKey.TryParse"/> and its wire form rather than
        /// reaching for a constructor, so a key that comes off disk is validated by exactly the
        /// same code as one that arrives over any other route. False for a fingerprint whose
        /// fields were never a key — a hand-edited snapshot, or one from a build that hashed
        /// differently.
        /// </summary>
        /// <param name="key">The key, when there is one.</param>
        public bool TryKey(out ElementKey key) =>
            ElementKey.TryParse(
                ((int)Rung).ToString(CultureInfo.InvariantCulture) + ":" + ModelScope + ":" + Scoped,
                out key);

        /// <summary>The extents sorted small to large — unchanged by a rotation about any axis.</summary>
        public double[] SortedExtents()
        {
            var e = new[] { Math.Abs(SizeX), Math.Abs(SizeY), Math.Abs(SizeZ) };
            Array.Sort(e);
            return e;
        }

        /// <summary>How far this element's centre is from another's.</summary>
        /// <param name="other">The fingerprint to measure against.</param>
        public double DistanceTo(ElementFingerprint other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            var dx = CentreX - other.CentreX;
            var dy = CentreY - other.CentreY;
            var dz = CentreZ - other.CentreZ;

            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        /// <summary>Look one watched property up, or null when the snapshot did not keep it.</summary>
        /// <param name="property">Which property.</param>
        public string? ValueOf(PropertyRef property)
        {
            for (var i = 0; i < _values.Count; i++)
                if (_values[i].Property == property)
                    return _values[i].Value;

            return null;
        }

        /// <summary>The size as a change list prints it — <c>1.200 x 0.400 x 0.250</c>.</summary>
        public string ShowSize() =>
            Number(SizeX) + " x " + Number(SizeY) + " x " + Number(SizeZ);

        /// <summary>The centre as a change list prints it.</summary>
        public string ShowCentre() =>
            Number(CentreX) + ", " + Number(CentreY) + ", " + Number(CentreZ);

        /// <summary>How the element reads on one line: its name, and its type when it has one.</summary>
        public string Label =>
            Name.Length == 0
                ? (Type.Length == 0 ? "(unnamed)" : Type)
                : (Type.Length == 0 || string.Equals(Type, Name, StringComparison.Ordinal)
                    ? Name
                    : Name + " — " + Type);

        /// <inheritdoc />
        public override string ToString() => Label + " @ " + ShowCentre();

        /// <summary>
        /// Take a fingerprint of a live element.
        /// </summary>
        /// <param name="item">The element.</param>
        /// <param name="options">What to watch, and how much of it to keep.</param>
        public static ElementFingerprint Of(IModelItem item, CompareSettings? options = null)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            var settings = options ?? CompareSettings.Default;
            var key = item.Key;
            var bounds = item.Bounds;

            var print = new ElementFingerprint(key.Rung, key.UnscopedValue, key.Value, key.ModelScope)
            {
                Name = item.DisplayName ?? string.Empty,
                Category = item.Category ?? string.Empty,
                Type = item.TypeName ?? string.Empty,
                HasGeometry = item.HasGeometry,
                CentreX = bounds.CentreX,
                CentreY = bounds.CentreY,
                CentreZ = bounds.CentreZ,
                SizeX = bounds.SizeX,
                SizeY = bounds.SizeY,
                SizeZ = bounds.SizeZ,
            };

            if (settings.Watch != PropertyWatch.None)
                print.Values = Read(item, settings);

            return print;
        }

        private static IReadOnlyList<WatchedValue> Read(IModelItem item, CompareSettings options)
        {
            var kept = new List<WatchedValue>();

            foreach (var reading in item.Properties())
            {
                if (kept.Count >= ComparePolicy.MaxWatchedPerElement) break;

                var reference = new PropertyRef(reading.Category, reading.Name);
                if (!options.Watches(reference)) continue;

                kept.Add(new WatchedValue(reference, ComparePolicy.Trim(reading.Value)));
            }

            // Left unsorted here on purpose: the Values setter owns that invariant, so there is
            // exactly one place that can get it wrong.
            return kept;
        }

        private static string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? "?"
                : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
