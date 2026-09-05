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

        /// <summary>What a token caught, or empty when the pattern does not carry it.</summary>
        /// <param name="token">The token, with or without its braces.</param>
        public string Value(string? token)
        {
            var named = (token ?? string.Empty).Trim().Trim('{', '}');

            return _held.TryGetValue(named, out var said) ? said : string.Empty;
        }

        /// <summary>Every token the pattern caught something for.</summary>
        public IReadOnlyDictionary<string, string> Held => _held;

        /// <inheritdoc />
        public override string ToString() =>
            string.Join(", ", _held.Select(one => one.Key + "=" + one.Value));
    }

    /// <summary>
    /// A test-naming pattern, read backwards: what a name has to look like, and what each part
    /// of it means.
    ///
    /// <b>The tool was guessing at something the user already knows.</b> A matrix read out of a
    /// document was built from what each test is <i>between</i> - the saved sets on its two sides
    /// - because that is the one thing the host will always answer. But a job's test names are
    /// not decoration: "ST-01 Steel vs ME-04 Ductwork (B)" carries the discipline codes, the
    /// numbers, the two names as the coordinator writes them and which kind of test it is, all
    /// laid out in an order that job agreed on months ago. Reading a matrix out of the sets threw
    /// every one of those away and left a grid named after whatever the sets happened to be
    /// called, which is usually not what anybody puts in front of a client.
    ///
    /// <b>And the same tokens already exist, pointing the other way.</b> The matrix builds names
    /// out of <see cref="MatrixNaming.Tokens"/>; this reads them back out. A person who has told
    /// the tool how their tests are named has told it how to read them, and the two halves cannot
    /// drift because they are the same list of tokens.
    ///
    /// <b>More than one pattern, because a job has more than one convention.</b> Tests set up in
    /// March and tests set up in September are named differently on every real project, and
    /// asking somebody to pick the one pattern that fits both is asking them to rename a hundred
    /// tests first. The patterns are tried in order and the first that fits a name claims it.
    /// </summary>
    public sealed class MatrixNamePattern
    {
        private readonly Regex? _reading;

        private MatrixNamePattern(string pattern, Regex? reading, IReadOnlyList<string> tokens,
                                  string? trouble)
        {
            Pattern = pattern; _reading = reading; Tokens = tokens; Trouble = trouble;
        }

        /// <summary>The pattern as it was written.</summary>
        public string Pattern { get; }

        /// <summary>Which tokens it carries, in the order they appear.</summary>
        public IReadOnlyList<string> Tokens { get; }

        /// <summary>Why it cannot be used, or null when it can.</summary>
        public string? Trouble { get; }

        /// <summary>Whether it can be matched against anything.</summary>
        public bool IsUsable => _reading != null;

        /// <summary>
        /// Read a pattern, saying why rather than throwing when it cannot be used.
        ///
        /// A person types a pattern a character at a time, so most of what this sees is halfway
        /// written. What comes back is either something that matches names or a sentence saying
        /// what is wrong with it, and the window puts that sentence under the box.
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
                        "There is a “{” with no “}” after it.");

                var named = said.Substring(at + 1, shut - at - 1).Trim();

                if (!Known(named))
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” is not one of the tokens.");

                // <b>Two tokens with nothing between them cannot be told apart.</b> "{row}{column}"
                // over "SteelDuctwork" has no answer - every split of it is as good as every
                // other - so it is refused here rather than matched arbitrarily and quietly
                // producing a matrix of nonsense.
                if (lastWasToken)
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” follows another token with nothing between "
                        + "them, so there is no way to tell where one ends and the next begins. "
                        + "Put a space, a dash or a word between them.");

                // A token twice over is a name that repeats something, which is fine to write and
                // impossible to read back: the second one would have to agree with the first, and
                // nothing here can make it.
                if (!seen.Add(named))
                    return new MatrixNamePattern(said, null, tokens,
                        "“{" + named + "}” is in this pattern twice, and a name can "
                        + "only be read one way.");

                tokens.Add(named);
                regex.Append("(?<").Append(named).Append('>').Append(Shape(named)).Append(')');

                lastWasToken = true;
                at = shut + 1;
            }

            regex.Append('$');

            if (tokens.Count == 0)
                return new MatrixNamePattern(said, null, tokens,
                    "This pattern has no tokens in it, so it would read nothing out of a name.");

            try
            {
                // Case-insensitive, because a convention written as "vs" and typed as "VS" is the
                // same convention, and a matrix that dropped forty tests over that would be
                // debugged by eye against a wall of names.
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

        /// <summary>What a name says, or null when this pattern does not fit it.</summary>
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

        /// <summary>How many of a list of names this pattern fits.</summary>
        /// <param name="names">The names.</param>
        public int Fits(IEnumerable<string?>? names) =>
            (names ?? Array.Empty<string?>()).Count(one => Match(one) != null);

        /// <summary>Whether a token is one the naming vocabulary has.</summary>
        /// <param name="named">The token, without braces.</param>
        public static bool Known(string? named) =>
            MatrixNaming.Tokens.Any(one => string.Equals(one.Token, "{" + (named ?? string.Empty) + "}",
                                                         StringComparison.Ordinal));

        /// <summary>
        /// What a token is allowed to swallow.
        ///
        /// The numbers are digits, which is what lets "{rowNumber} {row}" split "01 Steel" at the
        /// space rather than anywhere. Everything else takes as little as it can, so the literal
        /// after it decides where it stops.
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

        /// <summary>An example of what this pattern would read, for a preview.</summary>
        /// <param name="row">A row to stand in.</param>
        /// <param name="column">A column to stand in.</param>
        /// <param name="symbol">A symbol to stand in.</param>
        public string Example(MatrixLine row, MatrixLine column, MatrixSymbol? symbol) =>
            MatrixNaming.Fill(Pattern, row, column, symbol, 1);

        /// <summary>The number a token caught, or nought.</summary>
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
