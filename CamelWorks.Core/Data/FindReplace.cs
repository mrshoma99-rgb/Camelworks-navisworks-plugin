using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CamelWorks.Core.Data
{
    /// <summary>How a rule decides a value is the one it is looking for.</summary>
    public enum MatchKind
    {
        /// <summary>The whole value is the search text.</summary>
        Whole,

        /// <summary>The search text appears somewhere in the value.</summary>
        Part,

        /// <summary>The value begins with the search text.</summary>
        Start,

        /// <summary>The value ends with the search text.</summary>
        End,

        /// <summary>The value matches a pattern with <c>*</c> and <c>?</c> in it.</summary>
        Pattern,
    }

    /// <summary>One correction to make across a property.</summary>
    public sealed class Replacement
    {
        /// <summary>The property tab.</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>The property name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What to look for.</summary>
        public string Find { get; set; } = string.Empty;

        /// <summary>What to put instead. Empty clears the matched text.</summary>
        public string ReplaceWith { get; set; } = string.Empty;

        /// <summary>How the search text is matched.</summary>
        public MatchKind Kind { get; set; } = MatchKind.Part;

        /// <summary>Whether case has to agree.</summary>
        public bool CaseSensitive { get; set; }

        /// <summary>
        /// Why this rule cannot run, or null when it can.
        ///
        /// An empty search matches every value in the model and replaces all of them, which is the
        /// one thing nobody means by leaving the box blank. It is refused here rather than in the
        /// panel so no caller can reach the write without passing this.
        /// </summary>
        public string? Refusal =>
            string.IsNullOrEmpty(Name) ? "Say which property to correct."
            : Find.Length == 0 ? "Say what to look for. An empty search matches every value there is."
            : null;

        /// <summary>True when the rule can run.</summary>
        public bool IsUsable => Refusal == null;

        /// <inheritdoc />
        public override string ToString() =>
            Name + ": " + Kind + " \"" + Find + "\" -> \"" + ReplaceWith + "\"";
    }

    /// <summary>
    /// Correcting a value everywhere it appears.
    ///
    /// A federation always carries a property somebody spelled two ways: "Mechanical" and "MECH",
    /// "L02" and "Level 02", a supplier code with a trailing space. Every filter, every set rule,
    /// every schedule join then quietly covers half of what it should, and the usual discovery is a
    /// count that is wrong by a number nobody can explain.
    ///
    /// The whole tool is one decision repeated: does this value match, and what does it become. That
    /// is here, alone and testable, because everything around it - reading the model, previewing the
    /// diff, writing it back - already exists and is the same machinery the other property writes
    /// use.
    /// </summary>
    public static class FindReplace
    {
        /// <summary>
        /// What a value becomes, or null when the rule leaves it alone.
        ///
        /// Null rather than the unchanged value on purpose: a caller building a diff needs to tell
        /// "this rule did nothing here" from "this rule produced the same text", and the second is
        /// worth reporting as a match that changed nothing rather than hiding as a non-match.
        /// </summary>
        /// <param name="value">What the element holds now.</param>
        /// <param name="rule">The correction.</param>
        public static string? Apply(string? value, Replacement? rule)
        {
            if (rule == null || !rule.IsUsable || value == null) return null;

            var comparison = rule.CaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            switch (rule.Kind)
            {
                case MatchKind.Whole:
                    return value.Equals(rule.Find, comparison) ? rule.ReplaceWith : null;

                case MatchKind.Start:
                    return value.StartsWith(rule.Find, comparison)
                        ? rule.ReplaceWith + value.Substring(rule.Find.Length)
                        : null;

                case MatchKind.End:
                    return value.EndsWith(rule.Find, comparison)
                        ? value.Substring(0, value.Length - rule.Find.Length) + rule.ReplaceWith
                        : null;

                case MatchKind.Pattern:
                    // A pattern describes the whole value, so a match replaces the whole value.
                    // Replacing "the matched part" of a pattern means nothing when the pattern is
                    // what matched.
                    return Matches(value, rule.Find, comparison) ? rule.ReplaceWith : null;

                default:
                    return ReplaceEvery(value, rule.Find, rule.ReplaceWith, comparison);
            }
        }

        /// <summary>Whether a value matches a <c>*</c> and <c>?</c> pattern.</summary>
        /// <param name="value">The value.</param>
        /// <param name="pattern">The pattern.</param>
        /// <param name="comparison">How to compare.</param>
        public static bool Matches(string? value, string? pattern, StringComparison comparison)
        {
            var text = value ?? string.Empty;
            var shape = pattern ?? string.Empty;

            // Iterative rather than recursive, with one backtrack point for the last star. A
            // pattern of thirty stars against a long value recurses into a stack overflow, and a
            // property value is user data.
            int t = 0, s = 0, star = -1, mark = 0;

            while (t < text.Length)
            {
                if (s < shape.Length && (shape[s] == '?' || Same(text[t], shape[s], comparison)))
                {
                    t++; s++;
                }
                else if (s < shape.Length && shape[s] == '*')
                {
                    star = s++;
                    mark = t;
                }
                else if (star >= 0)
                {
                    s = star + 1;
                    t = ++mark;
                }
                else
                {
                    return false;
                }
            }

            while (s < shape.Length && shape[s] == '*') s++;

            return s == shape.Length;
        }

        private static bool Same(char a, char b, StringComparison comparison) =>
            comparison == StringComparison.Ordinal
                ? a == b
                : char.ToUpperInvariant(a) == char.ToUpperInvariant(b);

        private static string? ReplaceEvery(string value, string find, string with, StringComparison comparison)
        {
            var at = value.IndexOf(find, comparison);
            if (at < 0) return null;

            var built = new StringBuilder();
            var from = 0;

            while (at >= 0)
            {
                built.Append(value, from, at - from).Append(with);
                from = at + find.Length;

                // Past the replacement, never from inside it, so replacing "a" with "aa" ends
                // rather than growing forever.
                at = from > value.Length ? -1 : value.IndexOf(find, from, comparison);
            }

            return built.Append(value, from, value.Length - from).ToString();
        }

        /// <summary>What a rule would do, counted, without touching anything.</summary>
        /// <param name="values">What each element holds now.</param>
        /// <param name="rule">The correction.</param>
        public static ReplacementCount Rehearse(IEnumerable<string?>? values, Replacement? rule)
        {
            var matched = 0;
            var changed = 0;
            var scope = 0;
            var examples = new List<(string From, string To)>();

            foreach (var value in values ?? Enumerable.Empty<string?>())
            {
                scope++;

                var after = Apply(value, rule);
                if (after == null) continue;

                matched++;

                if (string.Equals(after, value, StringComparison.Ordinal)) continue;

                changed++;

                if (examples.Count < 8) examples.Add((value ?? string.Empty, after));
            }

            return new ReplacementCount(scope, matched, changed, examples);
        }
    }

    /// <summary>What a correction would come to.</summary>
    public sealed class ReplacementCount
    {
        internal ReplacementCount(int scope, int matched, int changed,
                                  IReadOnlyList<(string From, string To)> examples)
        {
            Scope = scope; Matched = matched; Changed = changed; Examples = examples;
        }

        /// <summary>How many values were looked at.</summary>
        public int Scope { get; }

        /// <summary>How many the rule matched.</summary>
        public int Matched { get; }

        /// <summary>How many would end up holding something different.</summary>
        public int Changed { get; }

        /// <summary>A handful of before-and-afters.</summary>
        public IReadOnlyList<(string From, string To)> Examples { get; }

        /// <summary>One line saying what it comes to.</summary>
        public string Explain()
        {
            if (Scope == 0) return "Nothing in scope.";

            if (Matched == 0)
                return "Nothing matched, out of " + Scope.ToString("N0", CultureInfo.InvariantCulture)
                       + (Scope == 1 ? " value." : " values.");

            var line = Changed.ToString("N0", CultureInfo.InvariantCulture)
                       + (Changed == 1 ? " value" : " values") + " would change";

            // A match that changes nothing is worth saying out loud: it means the correction has
            // already been made, and the reader is about to conclude the tool did not work.
            if (Matched > Changed)
                line += ", and " + (Matched - Changed).ToString("N0", CultureInfo.InvariantCulture)
                        + " already read that way";

            return line + ".";
        }

        /// <inheritdoc />
        public override string ToString() => Explain();
    }
}
