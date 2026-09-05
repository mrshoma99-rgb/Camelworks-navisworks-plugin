using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>What a pattern found in one test's name, and which pattern found it.</summary>
    public sealed class NamingRead
    {
        internal NamingRead(int pattern, MatrixNameMatch match)
        {
            Pattern = pattern;
            Match = match;
        }

        /// <summary>Which pattern read it, from one.</summary>
        public int Pattern { get; }

        /// <summary>What it extracted.</summary>
        public MatrixNameMatch Match { get; }

        /// <summary>One token's value, or empty.</summary>
        /// <param name="token">The token, without braces.</param>
        public string Value(string token) => Match.Value(token);
    }

    /// <summary>
    /// The project's naming: the fields a line carries, the patterns its test names are written
    /// in, and which pattern reads each test.
    ///
    /// It lives on the project rather than on a matrix because the Clash Manager needs it with no
    /// matrix open. Grouping a board by phase, filtering it by sub-discipline and renumbering a
    /// selection all read a test's name, and a coordinator working through last month's results
    /// has a document full of tests and no board at all. A scheme that lived on the board would
    /// have made every one of those unavailable exactly when it was wanted.
    ///
    /// The binding is the part worth stating. A pattern is not matched afresh each time: the
    /// project records which pattern reads which test, by the host's own id. Tests CamelWorks
    /// built from a matrix are bound as they are built and never have to be asked about; tests
    /// read from the model are bound in the read window; anything else is asked once and
    /// remembered. Re-matching instead would put back the thing the read window removed — where
    /// two patterns both fit a name, the order of the list silently deciding, and a test assigned
    /// by hand not staying assigned.
    /// </summary>
    public sealed class NamingBook
    {
        /// <summary>Where the book sits in the project file's clash section.</summary>
        public const string Key = "naming";

        private readonly List<string> _patterns = new List<string>();

        private readonly Dictionary<string, int> _bound =
            new Dictionary<string, int>(StringComparer.Ordinal);

        // Compiled once per distinct pattern string. Parsing is cheap but a board redraw reads
        // every name on every pass, and a hundred and sixty tests times a dozen redraws is enough
        // to be felt.
        private readonly Dictionary<string, MatrixNamePattern> _built =
            new Dictionary<string, MatrixNamePattern>(StringComparer.Ordinal);

        private MatrixScheme _scheme = new MatrixScheme();

        private int _revision = -1;

        /// <summary>The fields a line carries, which are what the tokens name.</summary>
        public MatrixScheme Scheme
        {
            get => _scheme;
            set => _scheme = value ?? new MatrixScheme();
        }

        /// <summary>The patterns, in the order the read window lists them.</summary>
        public IReadOnlyList<string> Patterns => _patterns;

        /// <summary>Which pattern reads a test, by host id, from one. Zero for none.</summary>
        public IReadOnlyDictionary<string, int> Bound => _bound;

        /// <summary>Add a pattern, or return where an identical one already sits.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <returns>Its position, from one.</returns>
        public int Add(string? pattern)
        {
            var said = (pattern ?? string.Empty).Trim();

            if (said.Length == 0) return 0;

            var at = _patterns.FindIndex(one => string.Equals(one, said, StringComparison.Ordinal));

            if (at >= 0) return at + 1;

            _patterns.Add(said);

            return _patterns.Count;
        }

        /// <summary>Replace the whole pattern list, keeping bindings that still point somewhere.</summary>
        /// <param name="patterns">The patterns, in order.</param>
        public void Patterned(IEnumerable<string>? patterns)
        {
            _patterns.Clear();
            _built.Clear();

            foreach (var one in patterns ?? Array.Empty<string>())
            {
                var said = (one ?? string.Empty).Trim();

                if (said.Length > 0) _patterns.Add(said);
            }

            // A binding past the end of the new list names a pattern that is gone. Dropped rather
            // than clamped: pointing it at whatever now sits at that position would bind a test to
            // a pattern nobody chose for it.
            foreach (var key in _bound.Where(one => one.Value > _patterns.Count)
                                      .Select(one => one.Key).ToList())
                _bound.Remove(key);
        }

        /// <summary>Say which pattern reads a test.</summary>
        /// <param name="testId">The host's id for the test.</param>
        /// <param name="pattern">Its pattern, from one, or 0 for none.</param>
        public void Bind(string? testId, int pattern)
        {
            var id = (testId ?? string.Empty).Trim();

            if (id.Length == 0) return;

            if (pattern <= 0) _bound[id] = 0;
            else if (pattern <= _patterns.Count) _bound[id] = pattern;
        }

        /// <summary>Forget a test's binding, so it is unread again.</summary>
        /// <param name="testId">The host's id for the test.</param>
        public void Unbind(string? testId) => _bound.Remove((testId ?? string.Empty).Trim());

        /// <summary>Whether a test has been bound to a pattern, whether or not one fits.</summary>
        /// <param name="testId">The host's id for the test.</param>
        public bool Knows(string? testId) =>
            _bound.ContainsKey((testId ?? string.Empty).Trim());

        /// <summary>The compiled pattern at a position, or null.</summary>
        /// <param name="at">The position, from one.</param>
        public MatrixNamePattern? Compiled(int at)
        {
            if (at <= 0 || at > _patterns.Count) return null;

            // A field added or removed changes which tokens are known, so what was compiled
            // against the old scheme is thrown away rather than left to answer for the new one.
            if (_revision != _scheme.Revision)
            {
                _built.Clear();
                _revision = _scheme.Revision;
            }

            var said = _patterns[at - 1];

            if (_built.TryGetValue(said, out var held)) return held;

            var made = MatrixNamePattern.Parse(said, _scheme);

            _built[said] = made;

            return made;
        }

        /// <summary>
        /// What a test's name says, or null when nothing reads it.
        ///
        /// Only the bound pattern is tried. A test bound to a pattern its name does not fit reads
        /// as nothing rather than falling through to another pattern, because falling through
        /// would undo the binding — which is the behaviour the read window exists to remove.
        /// </summary>
        /// <param name="testId">The host's id for the test.</param>
        /// <param name="name">The test's name.</param>
        public NamingRead? Read(string? testId, string? name)
        {
            var id = (testId ?? string.Empty).Trim();

            if (id.Length == 0 || !_bound.TryGetValue(id, out var at) || at <= 0) return null;

            var pattern = Compiled(at);

            if (pattern == null || !pattern.IsUsable) return null;

            var match = pattern.Match(name);

            return match == null ? null : new NamingRead(at, match);
        }

        /// <summary>
        /// The pattern that would fit a name, for offering a binding somebody has not made.
        ///
        /// This is the proposal the read window shows, and it is the only place order still
        /// decides anything: it is a suggestion nobody has agreed to, so the first that fits is as
        /// good an opening bid as any.
        /// </summary>
        /// <param name="name">The test's name.</param>
        /// <returns>The position, from one, or 0 when none fits.</returns>
        public int Fitting(string? name)
        {
            for (var at = 1; at <= _patterns.Count; at++)
            {
                var pattern = Compiled(at);

                if (pattern != null && pattern.IsUsable && pattern.Match(name) != null) return at;
            }

            return 0;
        }

        /// <summary>
        /// A test's name with one token changed, or null when it cannot be written.
        ///
        /// The name is taken apart with the test's own pattern and put back together from the same
        /// pattern, so everything the pattern did not name survives exactly: changing the number in
        /// "ST-01 Steel vs ME-04 Ducts" moves the number and leaves both disciplines, the
        /// separator and the spacing as they were.
        ///
        /// Null when the test has no pattern, when the name does not fit the one it has, or when
        /// the pattern carries no such token — there being nowhere in the name to put the value,
        /// which is a thing to report rather than to guess at.
        /// </summary>
        /// <param name="testId">The host's id for the test.</param>
        /// <param name="name">The test's name now.</param>
        /// <param name="token">The token to change, without braces.</param>
        /// <param name="value">What it should say.</param>
        public string? Written(string? testId, string? name, string token, string? value)
        {
            var read = Read(testId, name);

            if (read == null) return null;

            var pattern = Compiled(read.Pattern);

            if (pattern == null || !pattern.Tokens.Contains(token, StringComparer.Ordinal))
                return null;

            var held = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var one in pattern.Tokens)
                held[one] = string.Equals(one, token, StringComparison.Ordinal)
                    ? (value ?? string.Empty).Trim()
                    : read.Value(one);

            return pattern.Rebuild(held);
        }

        /// <summary>Write it down, or null when nothing has been set up.</summary>
        public JsonValue? ToJson()
        {
            var fields = _scheme.ToJson();

            if (fields == null && _patterns.Count == 0 && _bound.Count == 0) return null;

            var json = JsonValue.Object();

            if (fields != null) json.Set("fields", fields);

            if (_patterns.Count > 0)
                json.Set("patterns", JsonValue.Array(_patterns.Select(JsonValue.String)));

            if (_bound.Count > 0)
            {
                var held = JsonValue.Object();

                foreach (var one in _bound.OrderBy(v => v.Key, StringComparer.Ordinal))
                    held.Set(one.Key, (long)one.Value);

                json.Set("bound", held);
            }

            return json;
        }

        /// <summary>Read one back, falling to an empty book.</summary>
        /// <param name="json">What was written.</param>
        public static NamingBook FromJson(JsonValue? json)
        {
            var made = new NamingBook();

            if (json == null || json.Kind != JsonKind.Object) return made;

            made.Scheme = MatrixScheme.FromJson(json["fields"]);

            foreach (var one in json["patterns"].Items)
            {
                var said = (one.AsString() ?? string.Empty).Trim();

                if (said.Length > 0) made._patterns.Add(said);
            }

            var bound = json["bound"];

            if (bound.Kind == JsonKind.Object)
                foreach (var key in bound.Keys)
                {
                    var at = (int)bound[key].AsDouble(0);

                    if (at >= 0 && at <= made._patterns.Count) made._bound[key] = at;
                }

            return made;
        }

        /// <summary>How many of the given tests have no pattern that reads them.</summary>
        /// <param name="tests">The tests, as id and name.</param>
        public int Unread(IEnumerable<(string Id, string Name)>? tests) =>
            (tests ?? Array.Empty<(string, string)>()).Count(one => Read(one.Id, one.Name) == null);

        /// <inheritdoc />
        public override string ToString() =>
            _patterns.Count.ToString(CultureInfo.InvariantCulture) + " pattern(s), "
            + _bound.Count.ToString(CultureInfo.InvariantCulture) + " bound";
    }
}
