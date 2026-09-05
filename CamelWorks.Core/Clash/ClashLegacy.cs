using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// The two registers the clash board used to keep, read once into findings.
    ///
    /// Before findings, the board kept two side registers in the project file: the edits a read-only
    /// document refused, keyed by test and result id, and the properties the host has no field for,
    /// keyed by BCF topic. Both are now fields on the finding itself. Anyone who marked up an NWD
    /// before the change has that work in those two keys, and a release that stopped reading them
    /// would discard it without saying so.
    ///
    /// So this reads them, lays them onto the findings they belong to, and reports how many landed.
    /// It never writes them back and never deletes them: folding is idempotent — a status set twice
    /// is the same status, and a comment already in the trail is not added again — so leaving the old
    /// keys in place costs nothing and means a project opened by an older build still finds what it
    /// expects.
    /// </summary>
    public static class ClashLegacy
    {
        /// <summary>Where the register of refused edits used to live.</summary>
        public const string HeldKey = "heldEdits";

        /// <summary>Where the properties the host has no field for used to live.</summary>
        public const string AsideKey = "asides";

        /// <summary>
        /// Fold both old registers onto the findings they belong to.
        /// </summary>
        /// <param name="held">What was under <see cref="HeldKey"/>, or null.</param>
        /// <param name="asides">What was under <see cref="AsideKey"/>, or null.</param>
        /// <param name="findings">The findings to lay them onto - one run's worth is enough.</param>
        /// <returns>How many findings took something from the old registers.</returns>
        public static int Fold(JsonValue? held, JsonValue? asides,
                               IEnumerable<Finding>? findings)
        {
            var all = (findings ?? Array.Empty<Finding>()).ToList();

            if (all.Count == 0) return 0;

            var touched = new HashSet<string>(StringComparer.Ordinal);

            var byHost = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);
            var byTopic = new Dictionary<string, Finding>(StringComparer.OrdinalIgnoreCase);

            foreach (var finding in all)
            {
                if (finding.Facts.TryGetValue(ClashFindings.HostTest, out var test)
                    && finding.Facts.TryGetValue(ClashFindings.HostId, out var result)
                    && test != null && result != null)
                    byHost[Where(test, result)] = finding;

                if (!string.IsNullOrEmpty(finding.TopicGuid)) byTopic[finding.TopicGuid!] = finding;
            }

            foreach (var record in (held ?? JsonValue.Object())[HeldKey].Items)
            {
                var test = record["test"].AsString();
                var result = record["result"].AsString();

                if (test == null || result == null) continue;
                if (!byHost.TryGetValue(Where(test, result), out var finding)) continue;

                if (FindingStatusLattice.TryParse(record["status"].AsString(), out var status))
                {
                    finding.Status = FindingStatusLattice.Merge(finding.Status, status);
                    finding.Hold(FindingField.Status);
                }

                if (record["assigned"].AsString() is string who)
                {
                    finding.AssignedTo = who;
                    finding.Hold(FindingField.AssignedTo);
                }

                if (record["group"].AsString() is string band)
                {
                    finding.Group = band;
                    finding.Hold(FindingField.Group);
                }

                if (record["name"].AsString() is string named)
                {
                    finding.Title = named;
                    finding.Hold(FindingField.Title);
                }

                foreach (var comment in record["comments"].Items)
                {
                    if (comment["body"].AsString() is not string body) continue;

                    var written = new FindingComment(comment["author"].AsString(),
                                                     comment["when"].AsLong(), body);

                    if (finding.Comments.Any(c => c.Repeats(written))) continue;

                    finding.Comments.Add(written);
                    finding.Hold(FindingField.Comments);
                }

                touched.Add(finding.Id);
            }

            foreach (var record in (asides ?? JsonValue.Object())[AsideKey].Items)
            {
                var aside = ClashAside.FromJson(record);

                if (aside == null || aside.IsEmpty) continue;
                if (!byTopic.TryGetValue(aside.TopicGuid, out var finding)) continue;

                aside.ApplyTo(finding);
                touched.Add(finding.Id);
            }

            return touched.Count;
        }

        // A unit separator: it cannot appear in a host id, so no pair of ids can collide with
        // another pair by running into each other.
        private static string Where(string test, string result) => test + (char)31 + result;
    }
}
