using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Data
{
    /// <summary>One storey as the project declares it: a name, and the height it starts at.</summary>
    public sealed class LevelStorey
    {
        /// <summary>Declare a storey.</summary>
        /// <param name="name">What the project calls it.</param>
        /// <param name="elevation">Where its floor is, in metres.</param>
        public LevelStorey(string? name, double elevation)
        {
            Name = (name ?? string.Empty).Trim();
            Elevation = elevation;
        }

        /// <summary>What the project calls it. The client's word, not the model's.</summary>
        public string Name { get; }

        /// <summary>Where its floor is, in metres above the model's zero.</summary>
        public double Elevation { get; }

        /// <inheritdoc />
        public override string ToString() =>
            Name + " @ " + Elevation.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The storeys of a building, as the project says they are.
    ///
    /// <see cref="LevelSet"/> can already read levels off a coordination grid, off a Level
    /// property, or infer them from where things sit. All three answer the question the model can
    /// answer. None answers the one a coordinator actually asks — "which floor is this clash on?"
    /// — because a federation routinely has no grid, spells its storeys three ways across three
    /// disciplines, and infers levels named after their own elevation.
    ///
    /// This is the coordinator saying it outright: the ground floor starts at zero, the first
    /// floor at five metres, and so on up. It is one list per project, typed once, and it outranks
    /// every derived source because it is not a derivation.
    ///
    /// A storey runs from its own elevation up to the next one's, and the topmost runs to the top
    /// of the building. That is why only a floor level is asked for: a range per storey would let
    /// somebody leave a gap between five and six metres that nothing falls in, and a clash there
    /// would vanish from every band on the screen.
    /// </summary>
    public sealed class LevelPlan
    {
        /// <summary>Where the plan sits in the project file's profile section.</summary>
        public const string Key = "levels";

        private readonly List<LevelStorey> _storeys = new List<LevelStorey>();

        private LevelSet? _built;
        private int _builtAt = -1;

        /// <summary>The storeys, lowest first.</summary>
        public IReadOnlyList<LevelStorey> Storeys => _storeys;

        /// <summary>Whether the project has declared any.</summary>
        public bool IsEmpty => _storeys.Count == 0;

        /// <summary>
        /// Bumped whenever the list changes, so the built set knows to be thrown away.
        /// </summary>
        public int Revision { get; private set; }

        /// <summary>
        /// Add a storey, keeping the list in height order.
        ///
        /// Refuses a name already used and a height already taken. Two storeys at one height put
        /// every element in the lower one and leave the upper one permanently empty, which reads
        /// on screen as a level nobody built anything on.
        /// </summary>
        /// <param name="name">What the project calls it.</param>
        /// <param name="elevation">Where its floor is, in metres.</param>
        /// <returns>True when it was added.</returns>
        public bool Add(string? name, double elevation)
        {
            var said = (name ?? string.Empty).Trim();

            if (said.Length == 0 || double.IsNaN(elevation) || double.IsInfinity(elevation))
                return false;

            if (Has(said) || At(elevation) != null) return false;

            _storeys.Add(new LevelStorey(said, elevation));
            Sort();

            return true;
        }

        /// <summary>Take a storey out.</summary>
        /// <param name="name">Its name.</param>
        /// <returns>True when it was there.</returns>
        public bool Remove(string? name)
        {
            var found = Find(name);

            if (found == null) return false;

            _storeys.Remove(found);
            Revision++;

            return true;
        }

        /// <summary>Rename a storey, keeping its height.</summary>
        /// <param name="name">What it is called now.</param>
        /// <param name="wanted">What it should be called.</param>
        /// <returns>True when it changed.</returns>
        public bool Rename(string? name, string? wanted)
        {
            var found = Find(name);
            var said = (wanted ?? string.Empty).Trim();

            if (found == null || said.Length == 0) return false;
            if (string.Equals(found.Name, said, StringComparison.Ordinal)) return false;
            if (Has(said)) return false;

            _storeys[_storeys.IndexOf(found)] = new LevelStorey(said, found.Elevation);
            Revision++;

            return true;
        }

        /// <summary>Move a storey to a different height.</summary>
        /// <param name="name">Its name.</param>
        /// <param name="elevation">Where its floor should be, in metres.</param>
        /// <returns>True when it changed.</returns>
        public bool Raise(string? name, double elevation)
        {
            var found = Find(name);

            if (found == null || double.IsNaN(elevation) || double.IsInfinity(elevation))
                return false;

            if (Math.Abs(found.Elevation - elevation) < 0.0005) return false;

            var taken = At(elevation);

            if (taken != null && !ReferenceEquals(taken, found)) return false;

            _storeys[_storeys.IndexOf(found)] = new LevelStorey(found.Name, elevation);
            Sort();

            return true;
        }

        /// <summary>Whether a name is already used, whatever its case.</summary>
        /// <param name="name">The name.</param>
        public bool Has(string? name) => Find(name) != null;

        /// <summary>The storey by that name, or null.</summary>
        /// <param name="name">The name.</param>
        public LevelStorey? Find(string? name)
        {
            var said = (name ?? string.Empty).Trim();

            return said.Length == 0
                ? null
                : _storeys.FirstOrDefault(
                    one => string.Equals(one.Name, said, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Where a storey's ceiling is: the next one's floor, or nothing above the top.</summary>
        /// <param name="name">The storey.</param>
        /// <returns>The height in metres, or null for the topmost.</returns>
        public double? Ceiling(string? name)
        {
            var found = Find(name);

            if (found == null) return null;

            var at = _storeys.IndexOf(found);

            return at >= 0 && at < _storeys.Count - 1 ? _storeys[at + 1].Elevation : (double?)null;
        }

        /// <summary>
        /// These storeys as a level set, so everything that already groups by level uses them.
        ///
        /// Built once per change rather than per call: a board bands six hundred rows on one
        /// redraw, and each of those asks which level a point is on.
        /// </summary>
        public LevelSet Levels()
        {
            if (_built != null && _builtAt == Revision) return _built;

            _built = _storeys.Count == 0
                ? LevelSet.Empty
                : LevelSet.Declared(_storeys.Select(
                    one => new KeyValuePair<string, double>(one.Name, one.Elevation)));

            _builtAt = Revision;

            return _built;
        }

        /// <summary>Which storey a height is on, or null when it is below the lowest.</summary>
        /// <param name="metres">The height, in metres.</param>
        public string? LevelAt(double metres) => Levels().LevelAt(metres);

        /// <summary>Write it down, or null when nothing has been declared.</summary>
        public JsonValue? ToJson()
        {
            if (_storeys.Count == 0) return null;

            return JsonValue.Array(_storeys.Select(
                one => JsonValue.Object()
                    .Set("name", one.Name)
                    .Set("at", one.Elevation)));
        }

        /// <summary>Read one back, falling to an empty plan.</summary>
        /// <param name="json">What was written.</param>
        public static LevelPlan FromJson(JsonValue? json)
        {
            var made = new LevelPlan();

            if (json == null || json.Items == null) return made;

            foreach (var held in json.Items)
                made.Add(held["name"].AsString(), held["at"].AsDouble());

            return made;
        }

        /// <summary>The storey at exactly this height, or null.</summary>
        private LevelStorey? At(double elevation) =>
            _storeys.FirstOrDefault(one => Math.Abs(one.Elevation - elevation) < 0.0005);

        /// <summary>Put them back in height order, and say the list moved.</summary>
        private void Sort()
        {
            _storeys.Sort((one, other) =>
            {
                var by = one.Elevation.CompareTo(other.Elevation);

                return by != 0
                    ? by
                    : string.Compare(one.Name, other.Name, StringComparison.Ordinal);
            });

            Revision++;
        }

        /// <inheritdoc />
        public override string ToString() =>
            _storeys.Count == 0
                ? "no levels declared"
                : _storeys.Count.ToString(CultureInfo.InvariantCulture)
                  + (_storeys.Count == 1 ? " level" : " levels")
                  + " from " + _storeys[0].Elevation.ToString("0.###", CultureInfo.InvariantCulture)
                  + " m";
    }
}
