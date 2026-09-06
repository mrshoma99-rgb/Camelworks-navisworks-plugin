using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// The facts a check writes onto a finding that the seam itself reads back.
    ///
    /// Run membership is a declared fact rather than a map built while reading. A read cannot hold
    /// the map: <see cref="FindingRegister.Observe"/> merges a fresh finding into the held one and
    /// the board draws the held object, so a map keyed on the objects a read produced answers for
    /// objects nobody draws. It also has nothing to say about findings held from runs this read did
    /// not cover, which the rail still has to tally. The producer therefore writes the run ids onto
    /// the finding, and both the read's scope and the per-run tallies derive from that one fact.
    /// </summary>
    public static class CheckFacts
    {
        /// <summary>
        /// The runs a finding belongs to, semicolon separated.
        ///
        /// A set rather than a single id, because one result can belong to more than one run: two
        /// clash tests reporting one physical conflict, a sequencing conflict spanning two
        /// sequences. A finding in two runs is counted under both and drawn once.
        /// </summary>
        public const string Runs = "Runs";

        private static readonly char[] Separator = { ';' };

        /// <summary>The runs a finding names. Empty when it names none.</summary>
        /// <param name="finding">The finding.</param>
        public static IReadOnlyList<string> RunsOf(Finding? finding)
        {
            var written = finding == null ? null
                : finding.Facts.TryGetValue(Runs, out var value) ? value : null;

            if (written == null) return Array.Empty<string>();

            var found = new List<string>();

            foreach (var part in written.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
            {
                var id = part.Trim();

                if (id.Length > 0 && !found.Contains(id, StringComparer.Ordinal)) found.Add(id);
            }

            return found;
        }

        /// <summary>
        /// Say which runs a finding belongs to, replacing whatever it named before.
        ///
        /// Replacing rather than adding: facts merge additively and never expire, so a finding
        /// carried over from a run it is no longer reported by would keep naming that run for good.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="runIds">The run ids. Null or empty removes the fact.</param>
        public static void SetRuns(Finding? finding, IEnumerable<string>? runIds)
        {
            if (finding == null) return;

            var kept = new List<string>();

            foreach (var id in runIds ?? Array.Empty<string>())
            {
                var trimmed = (id ?? string.Empty).Trim();

                if (trimmed.Length > 0 && !kept.Contains(trimmed, StringComparer.Ordinal))
                    kept.Add(trimmed);
            }

            if (kept.Count == 0) finding.Facts.Remove(Runs);
            else finding.Facts[Runs] = string.Join(";", kept);
        }

        /// <summary>True when a finding names this run.</summary>
        /// <param name="finding">The finding.</param>
        /// <param name="runId">The run id.</param>
        public static bool IsIn(Finding? finding, string? runId) =>
            runId != null && RunsOf(finding).Contains(runId, StringComparer.Ordinal);

        /// <summary>
        /// The predicate a read of these runs is scoped by.
        ///
        /// A finding naming none of them is out of scope, so a read of three of twelve runs never
        /// marks the other nine absent. A finding naming no run at all is out of scope of every
        /// restricted read, which errs towards keeping a finding present rather than marking it
        /// absent on the strength of a fact its producer did not write.
        /// </summary>
        /// <param name="runIds">The runs being read. Null or empty scopes to nothing.</param>
        public static Func<Finding, bool> ScopeOf(IEnumerable<string>? runIds)
        {
            var wanted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var id in runIds ?? Array.Empty<string>())
                if (!string.IsNullOrWhiteSpace(id)) wanted.Add(id.Trim());

            return finding => RunsOf(finding).Any(wanted.Contains);
        }
    }
}
