using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CamelWorks.Core.Data
{
    /// <summary>
    /// Turns what a property actually says into something a quantity parser can read.
    ///
    /// The takeoff parser handles "12.5 m3" and "3'-6"". What it cannot handle is what models
    /// actually carry: "Volume: 12.5 m3 (net)", "12,500", "approx 4.2m2", "V=8.75". Those are not
    /// broken models, they are how a dozen different authoring tools and a hundred different
    /// template authors write a number, and a takeoff that reports them as unreadable is a takeoff
    /// that reports a total nobody can use.
    ///
    /// So: an optional pattern that says which part of the text is the number, and an optional unit
    /// to assume when the text has none. Both off by default — a value that already parses is left
    /// exactly alone, which is the zero-setup case and the common one.
    ///
    /// <b>It never invents a number.</b> A pattern that matches nothing leaves the value untouched
    /// so the takeoff still counts it as unreadable and names it. Silently turning a value it could
    /// not clean into a zero would move the failure from a line that says "3 unreadable" into a
    /// total that is quietly wrong.
    /// </summary>
    public sealed class ValueCleaner
    {
        /// <summary>
        /// How long one pattern may run before it is abandoned.
        ///
        /// A user writes the pattern and a federation supplies a hundred thousand values, so a
        /// pattern that backtracks catastrophically would hang the host with no way out. The host
        /// is single-threaded and its message loop is the same thread; there is no cancel button
        /// that could run.
        /// </summary>
        public static readonly TimeSpan Patience = TimeSpan.FromMilliseconds(50);

        /// <summary>
        /// The units a bare number can be told to mean.
        ///
        /// Taken from the parser rather than written out again here. A picker offering a unit the
        /// parser does not know would turn every value it was applied to into an unreadable one,
        /// which is the exact opposite of what the person reaching for it wanted.
        /// </summary>
        public static IReadOnlyList<string> AssumableUnits => Quantity.KnownUnits;

        private readonly Regex? _pattern;
        private readonly string? _unit;

        private ValueCleaner(Regex? pattern, string? unit)
        {
            _pattern = pattern;
            _unit = unit;
        }

        /// <summary>A cleaner that changes nothing.</summary>
        public static ValueCleaner None { get; } = new ValueCleaner(null, null);

        /// <summary>True when this cleaner would leave every value exactly as it found it.</summary>
        public bool DoesNothing => _pattern == null && _unit == null;

        /// <summary>The pattern, as written, or null.</summary>
        public string? Pattern => _pattern?.ToString();

        /// <summary>The unit a bare number is taken to be in, or null.</summary>
        public string? AssumedUnit => _unit;

        /// <summary>
        /// Build one, reporting a bad pattern rather than throwing.
        /// </summary>
        /// <param name="pattern">
        /// A regular expression. The first capturing group, or the whole match when there is none,
        /// is taken as the number. Null or blank for no pattern.
        /// </param>
        /// <param name="assumeUnit">
        /// What a value with no unit is in. Null or blank to leave bare numbers bare.
        /// </param>
        /// <param name="problem">Why the pattern was refused, or null.</param>
        public static ValueCleaner Of(string? pattern, string? assumeUnit, out string? problem)
        {
            problem = null;

            var unit = string.IsNullOrWhiteSpace(assumeUnit) ? null : assumeUnit!.Trim();

            if (unit != null && !Quantity.Knows(unit))
            {
                problem = "\"" + unit + "\" is not a unit this understands. Use one of: "
                        + string.Join(", ", AssumableUnits) + ".";
                return None;
            }

            if (string.IsNullOrWhiteSpace(pattern))
                return unit == null ? None : new ValueCleaner(null, unit);

            try
            {
                var regex = new Regex(pattern!, RegexOptions.CultureInvariant, Patience);
                return new ValueCleaner(regex, unit);
            }
            catch (ArgumentException e)
            {
                problem = "That pattern is not a valid regular expression: " + e.Message;
                return unit == null ? None : new ValueCleaner(null, unit);
            }
        }

        /// <summary>
        /// The obvious cleaner, for people who do not write regular expressions.
        ///
        /// Takes the first number in the text, decimal point or comma, sign and thousands
        /// separators included, and throws away everything around it. That is what somebody means
        /// by "just get the number out of it" nine times in ten, and it means the box on screen can
        /// have a button rather than only a blank field with a manual beside it.
        /// </summary>
        public const string FirstNumber = @"-?\d[\d,]*\.?\d*";

        /// <summary>
        /// Clean one value.
        /// </summary>
        /// <param name="value">What the property said.</param>
        /// <returns>Something the quantity parser has a better chance with, or the original.</returns>
        public string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return value;

            var text = value!.Trim();

            if (_pattern != null)
            {
                string? taken = null;

                try
                {
                    var match = _pattern.Match(text);

                    if (match.Success)
                    {
                        // The first capturing group when the author wrote one, the whole match when
                        // they did not. Group 0 is always the whole match, so "Groups.Count > 1"
                        // is the test for "the author said which part they meant".
                        taken = match.Groups.Count > 1 && match.Groups[1].Success
                            ? match.Groups[1].Value
                            : match.Value;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // One value defeated the pattern. The value stays as it was and the takeoff
                    // counts it unreadable, which is true and is what the next hundred thousand
                    // values need this to do rather than stop.
                }

                // A pattern that matched nothing leaves the value alone. Replacing it with an empty
                // string would turn "unreadable, and here is what it said" into "unreadable, and we
                // have thrown away the evidence".
                if (!string.IsNullOrWhiteSpace(taken)) text = taken!.Trim();
            }

            if (_unit != null && IsBare(text)) text = text + " " + _unit;

            return text;
        }

        /// <summary>How this cleaner reads on screen.</summary>
        public string Describe()
        {
            if (DoesNothing) return "values are read exactly as the model wrote them";

            var parts = new List<string>();

            if (_pattern != null) parts.Add("the number is taken from \"" + _pattern + "\"");
            if (_unit != null) parts.Add("a value with no unit is read as " + _unit);

            return string.Join(", and ", parts);
        }

        /// <inheritdoc />
        public override string ToString() => Describe();

        /// <summary>
        /// True when the text is only a number, and so has room for an assumed unit.
        ///
        /// Checked rather than assumed: appending "m3" to "12.5 ft3" would produce "12.5 ft3 m3",
        /// which parses as nothing, so a value that already states its unit would become unreadable
        /// the moment somebody set an assumed one.
        /// </summary>
        private static bool IsBare(string text)
        {
            var seen = false;

            foreach (var c in text)
            {
                if (char.IsDigit(c)) { seen = true; continue; }

                if (c == '.' || c == ',' || c == '-' || c == '+' || c == ' ') continue;

                return false;
            }

            return seen && double.TryParse(
                text.Replace(",", string.Empty).Replace(" ", string.Empty),
                NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        }
    }
}
