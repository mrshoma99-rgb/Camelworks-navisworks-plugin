using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>
    /// Turns a comparison into findings, so what changed can be triaged, assigned, signed off and
    /// sent out as BCF like anything else.
    ///
    /// A change list is an observation; a finding is a record with an owner and a status that
    /// survives the next run. That is what a reported change has to become before anybody acts on
    /// it.
    ///
    /// One parent per model per kind of change, with children underneath. Three hundred moved
    /// elements are one thing to decide about and three hundred places to look, and the board exists
    /// to keep the first from being buried by the second. Status and assignee live on the parent;
    /// children carry the locations.
    /// </summary>
    public static class CompareIssues
    {
        /// <summary>The rule name every finding from a comparison carries.</summary>
        public const string Rule = "Model compare";

        /// <summary>
        /// Raise findings from a comparison.
        ///
        /// Children come from <see cref="CompareResult.Changes"/>, which is capped — see
        /// <see cref="CompareSettings.ExampleCap"/>. Run the comparison uncapped when the findings
        /// are going out as BCF and every element has to be in them; the parent's title carries
        /// the true count either way, so a capped set of children is visible rather than silent.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        /// <param name="buckets">
        /// Which kinds of change to raise. Defaults to added, removed and changed — not moved,
        /// because on most revisions "moved" is hundreds of elements settling by a few millimetres
        /// and raising it by default is how a board becomes something nobody opens.
        /// </param>
        public static IReadOnlyList<Finding> Raise(CompareResult? result,
                                                   IReadOnlyList<ChangeBucket>? buckets = null)
        {
            var findings = new List<Finding>();
            if (result == null || result.IsEmpty) return findings;

            var wanted = buckets ?? new[] { ChangeBucket.Added, ChangeBucket.Removed, ChangeBucket.Changed };
            var against = result.Between();

            foreach (var group in result.Changes
                         .Where(c => wanted.Contains(c.Bucket))
                         .GroupBy(c => new { c.Model, c.Bucket })
                         .OrderBy(g => g.Key.Model, StringComparer.Ordinal)
                         .ThenBy(g => g.Key.Bucket))
            {
                var bucket = group.Key.Bucket;
                var total = Total(result, group.Key.Model, bucket, group.Count());

                var parent = Finding.CreateParent(
                    FindingSource.Compare, Rule,
                    group.Key.Model + " — " + ComparePolicy.Count(total) + " "
                    + ElementChange.Show(bucket).ToLowerInvariant() + " since " + against,
                    // The model and the kind of change are what make this finding that finding. The
                    // COUNT deliberately is not: a run that finds 301 moved elements instead of 300
                    // is the same thing to decide about, and putting the count in the identity would
                    // throw the decision away every week.
                    group.Key.Model + "/" + bucket);

                foreach (var change in group)
                {
                    if (!change.Print.TryKey(out var key)) continue;

                    parent.AddChild(Finding.Create(
                        FindingSource.Compare, Rule, change.Title, new[] { key },
                        change.Print.Local));
                }

                findings.Add(parent);
            }

            return findings;
        }

        /// <summary>
        /// One finding for one element, for raising a single change by hand off the change list.
        /// </summary>
        /// <param name="change">The change.</param>
        /// <param name="against">What the comparison was against.</param>
        public static Finding? One(ElementChange? change, string? against = null)
        {
            if (change == null) return null;
            if (!change.Print.TryKey(out var key)) return null;

            var title = change.Title;
            if (!string.IsNullOrWhiteSpace(against)) title += " (since " + against!.Trim() + ")";

            return Finding.Create(FindingSource.Compare, Rule, title, new[] { key }, change.Print.Local);
        }

        /// <summary>
        /// The true number in this model and bucket, not the number of rows that survived the cap.
        ///
        /// Only derivable per model when there is one model; with several, the per-model tally is
        /// the honest source. Falls back to the row count rather than guessing.
        /// </summary>
        private static int Total(CompareResult result, string model, ChangeBucket bucket, int rows)
        {
            var tally = result.Models.FirstOrDefault(m => string.Equals(m.Title, model, StringComparison.Ordinal));

            if (tally == null) return rows;

            var counted = bucket switch
            {
                ChangeBucket.Added => tally.Added,
                ChangeBucket.Removed => tally.Removed,
                ChangeBucket.Moved => tally.Moved,
                ChangeBucket.Changed => tally.Changed,
                _ => tally.Unchanged,
            };

            return counted > 0 ? counted : rows;
        }
    }
}
