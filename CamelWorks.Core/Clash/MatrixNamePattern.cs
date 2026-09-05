using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CamelWorks.Core.Clash
{
    /// <summary>What one test's name turned out to say.</summary>
    public sealed class MatrixNameMatch
    {
        private readonly IReadOnlyDictionary<string, string> _held;

        internal MatrixNameMatch(IReadOnlyDictionary<string, string> held) { _held = held; }

        /// <summary>The value for a token, or empty when the pattern does not use it.</summary>
        /// <param name="token">The token, with or without its braces.</param>
        public string Value(string? token)
        {
            var named = (token ?? string.Empty).Trim().Trim('{', '}');

            return _held.TryGetValue(named, out var said) ? said : string.Empty;
        }

        /// <summary>Every token that captured a value.</summary>
        public IReadOnlyDictionary<string, string> Held => _held;

        /// <inheritdoc />
        public override string ToString() =>
            string.Join(", ", _held.Select(one => one.Key + "=" + one.Value));
    }

    /// <summary>
    /// A test-naming pattern used in reverse: matches a test name and extracts the values its
    /// parts stand for.
    ///
    /// The matrix generates test names from <see cref="MatrixNaming.Tokens"/>. The same token
    /// list is used here to parse names, so the two directions cannot diverge. Reading names
    /// recovers the discipline codes, numbers, folders and symbols a project already encodes in
    /// them, which reading the saved sets alone does not.
    ///
    /// Several patterns can be supplied; they are matched in order and the first match wins,
    /// since projects usually accumulate more than one naming convention.
    /// </summary>
    public sealed class MatrixNamePattern
    {
        private readonly Regex? _reading;

        private MatrixNamePattern(string pattern, Regex? reading, IReadOnlyList<string> tokens,
                                  string? trouble)
        {
            Pattern = pattern; _reading = reading; Tokens = tokens; Trouble = trouble;
        }

        /// <summary>The pattern text as supplied.</summary>
        public string Pattern { get; }

        /// <summary>The tokens it uses, in order of appearance.</summary>
        public IReadOnlyList<string> Tokens { get; }

        /// <summary>Why the pattern is unusable, or null when it is valid.</summary>
        public string? Trouble { get; }

        /// <summary>Whether the pattern compiled.</summary>
        public bool IsUsable => _reading != null;

        /// <summary>
        /// Parses a pattern. Never throws: an unusable pattern comes back with
        /// <see cref="Trouble"/> set, because the editor calls this on every keystroke.
        /// </summary>
        /// <param name="pattern">The pattern.</param>
        public static MatrixNamePattern Parse(string? pattern)
        {
            var said = (pattern ?? string.Empty).Trim();
            var tokens = new List<string>();

            if (said.Length == 0)
                return new MatrixNamePattern(said, null, tokens, "Write a pattern first.");

            var regex = new StringBuilder("^");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var lastWasToken = false;
            var at = 0;

            while (at < said.Length)
            {
                if (said[at] != '{')
                {
                    regex.Append(Regex.Escape(said[at].ToString()));
                    lastWasToken = false;
                    at++;
                    continue;
                }

                var shut = said.IndexOf('}', at);

                if (shut < 0)
                    return new MatrixNamePattern(said, null, tokens,
                        "Unclosed “{”.");

                var named = said.Substring(at + 1, shut - at - 1).Trim();

                if (!Known(named))
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” is not a known token.");

                // Adjacent tokens with no separator are ambiguous: "{row}{column}" against
                // "SteelDuctwork" has no determinate split. Rejected rather than matched
                // arbitrarily.
                if (lastWasToken)
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” follows another token directly. Separate them with "
                        + "a space, a dash or other text so the boundary is defined.");

                // A repeated token cannot be parsed: the second occurrence would have to agree
                // with the first, which regex alternation cannot enforce here.
                if (!seen.Add(named))
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” appears twice. Each token may be used once.");

                tokens.Add(named);
                regex.Append("(?<").Append(named).Append('>').Append(Shape(named)).Append(')');

                lastWasToken = true;
                at = shut + 1;
            }

            regex.Append('$');

            if (tokens.Count == 0)
                return new MatrixNamePattern(said, null, tokens,
                    "No tokens: this pattern would extract nothing.");

            try
            {
                // Case-insensitive: "vs" and "VS" are the same separator in practice.
                return new MatrixNamePattern(said,
                    new Regex(regex.ToString(),
                              RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                    tokens, null);
            }
            catch (ArgumentException e)
            {
                return new MatrixNamePattern(said, null, tokens, e.Message);
            }
        }

        /// <summary>The extracted values, or null when the name does not match.</summary>
        /// <param name="name">The test's name.</param>
        public MatrixNameMatch? Match(string? name)
        {
            if (_reading == null || string.IsNullOrWhiteSpace(name)) return null;

            var found = _reading.Match(name!.Trim());

            if (!found.Success) return null;

            var held = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var token in Tokens)
            {
                var group = found.Groups[token];

                if (group.Success) held[token] = group.Value.Trim();
            }

            return new MatrixNameMatch(held);
        }

        /// <summary>How many of the given names this pattern matches.</summary>
        /// <param name="names">The names.</param>
        public int Fits(IEnumerable<string?>? names) =>
            (names ?? Array.Empty<string?>()).Count(one => Match(one) != null);

        /// <summary>Whether a token name is in <see cref="MatrixNaming.Tokens"/>.</summary>
        /// <param name="named">The token, without braces.</param>
        public static bool Known(string? named) =>
            MatrixNaming.Tokens.Any(one => string.Equals(one.Token, "{" + (named ?? string.Empty) + "}",
                                                         StringComparison.Ordinal));

        /// <summary>
        /// The sub-expression a token compiles to.
        ///
        /// Numeric tokens match digits so "{rowNumber} {row}" splits "01 Steel" at the space.
        /// Everything else is lazy, so the following literal determines where it ends.
        /// </summary>
        /// <param name="named">The token, without braces.</param>
        private static string Shape(string named) => named switch
        {
            "rowNumber" => "[0-9]+",
            "columnNumber" => "[0-9]+",
            "n" => "[0-9]+",
            "tolerance" => "[0-9]+(?:[.,][0-9]+)?",
            _ => ".+?",
        };

        /// <inheritdoc />
        public override string ToString() => Pattern;

        /// <summary>The name this pattern would generate for a cell, for previewing.</summary>
        /// <param name="row">A row to stand in.</param>
        /// <param name="column">A column to stand in.</param>
        /// <param name="symbol">A symbol to stand in.</param>
        public string Example(MatrixLine row, MatrixLine column, MatrixSymbol? symbol) =>
            MatrixNaming.Fill(Pattern, row, column, symbol, 1);

        /// <summary>The numeric value of a token, or 0 when absent or unparseable.</summary>
        /// <param name="match">What the name said.</param>
        /// <param name="token">The token, without braces.</param>
        public static double Number(MatrixNameMatch? match, string token)
        {
            var said = (match?.Value(token) ?? string.Empty).Replace(',', '.');

            return double.TryParse(said, NumberStyles.Float, CultureInfo.InvariantCulture,
                                   out var held)
                ? held
                : 0;
        }
    }
}
