using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.OpenBim;

namespace CamelWorks.Core.Findings
{
    /// <summary>
    /// The labels this product writes onto a BCF topic, and the one test for whether a label is
    /// one of ours.
    ///
    /// BCF has one bag for tags, so the labels a writer puts on and the tags a reviewer adds
    /// arrive in the same list. The return leg has to tell them apart: a label of ours read back
    /// as a reviewer's tag is appended to the finding permanently and re-exported next cycle, so
    /// the tag list grows by six on every round trip. There were two vocabularies over one format
    /// — the clash writer's six and the finding writer's two — and neither reader knew the other's
    /// prefixes, so a file written by one and read by the other did exactly that.
    ///
    /// One vocabulary, one <see cref="Ours"/>. Every prefix is declared here, including the ones a
    /// fact travels under: a check that wants a fact on the topic's tags adds a row to
    /// <see cref="FromFacts"/> rather than a prefix of its own.
    /// </summary>
    public static class FindingLabels
    {
        /// <summary>Which check produced the topic, so a reply can be placed.</summary>
        public const string Check = "Check: ";

        /// <summary>The rule or run that found it — a clash test's name, a clearance run's name.</summary>
        public const string Rule = "Rule: ";

        /// <summary>The folder the run sits in.</summary>
        public const string Folder = "Folder: ";

        /// <summary>The group the finding was filed under on the board.</summary>
        public const string Group = "Group: ";

        /// <summary>The level it is on.</summary>
        public const string Level = "Level: ";

        /// <summary>The grid reference it is nearest.</summary>
        public const string Grid = "Grid: ";

        /// <summary>The zone it is in.</summary>
        public const string Zone = "Zone: ";

        /// <summary>The register's own number for an issue, which BCF has no field for.</summary>
        public const string Issue = "Issue: ";

        /// <summary>Every prefix this product writes.</summary>
        public static IReadOnlyList<string> Prefixes { get; } = new[]
        {
            Check, Rule, Folder, Group, Level, Grid, Zone, Issue,
        };

        /// <summary>
        /// The facts that travel as labels, and the prefix each travels under.
        ///
        /// Declared rather than inferred. A fact is per-check data and most of it means nothing to
        /// a reviewer, so only the ones a person filters a topic list by are worth a tag; and a
        /// mapping written down is the only thing that lets the reader take the label off again
        /// instead of keeping it as somebody's tag.
        /// </summary>
        public static IReadOnlyDictionary<string, string> FromFacts { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Level"] = Level,
                ["Grid"] = Grid,
                ["Zone"] = Zone,
                ["Folder"] = Folder,
            };

        /// <summary>Whether a label is one this product wrote rather than one a reviewer set.</summary>
        /// <param name="label">The label.</param>
        public static bool Ours(string? label) =>
            label != null
            && Prefixes.Any(prefix => label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The value of one prefixed label on a topic, or null when it carries none.
        /// </summary>
        /// <param name="topic">The topic.</param>
        /// <param name="prefix">One of the prefixes declared here.</param>
        public static string? Value(BcfTopic? topic, string? prefix)
        {
            if (topic == null || string.IsNullOrEmpty(prefix)) return null;

            foreach (var label in topic.Labels)
            {
                if (label == null
                    || !label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = label.Substring(prefix!.Length).Trim();

                if (value.Length > 0) return value;
            }

            return null;
        }

        /// <summary>
        /// A prefixed label, or null when there is nothing to say.
        /// </summary>
        /// <param name="prefix">One of the prefixes declared here.</param>
        /// <param name="value">What it says.</param>
        public static string? Written(string prefix, string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : prefix + value!.Trim();
    }
}
