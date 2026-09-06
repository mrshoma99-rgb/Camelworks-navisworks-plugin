using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Issues
{
    /// <summary>
    /// What marking an issue does to the findings attached to it, worked out before anything is
    /// written.
    ///
    /// Applied rather than offered. The offer existed because the mapping from an issue's status to
    /// a finding's covered two of six words and could demote what it touched; with the issue on the
    /// same lattice as the findings, every status cascades and none of them can demote. The plan is
    /// shown first — how many move, how many are already there, how many ids the register no longer
    /// holds — and <see cref="Apply"/> is one write.
    ///
    /// Every attached finding takes <see cref="FindingStatusLattice.Merge"/>, never a direct
    /// assignment. A direct assignment is what pulled a finding somebody had approved back to
    /// Reviewed when the issue over it was answered.
    ///
    /// Locations follow their finding and are never written: a location carries no decision.
    /// </summary>
    public sealed class IssueCascade
    {
        private IssueCascade(FindingStatus to, IReadOnlyList<Finding> promoted,
                             IReadOnlyList<Finding> unchanged, IReadOnlyList<Finding> higher,
                             IReadOnlyList<string> missing)
        {
            To = to;
            Promoted = promoted;
            Unchanged = unchanged;
            Higher = higher;
            Missing = missing;
        }

        /// <summary>What the issue was put into, and what its findings are merged towards.</summary>
        public FindingStatus To { get; }

        /// <summary>Findings the merge moves up.</summary>
        public IReadOnlyList<Finding> Promoted { get; }

        /// <summary>Findings already at or above the target. Not written.</summary>
        public IReadOnlyList<Finding> Unchanged { get; }

        /// <summary>
        /// Findings above the target, which is the part of <see cref="Unchanged"/> worth saying out
        /// loud. Reported, never demoted.
        /// </summary>
        public IReadOnlyList<Finding> Higher { get; }

        /// <summary>Attached ids the register no longer holds. Reported by id.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>Whether there is anything to write.</summary>
        public bool IsEmpty => Promoted.Count == 0;

        /// <summary>
        /// Work out what marking an issue would do. Never null; an empty cascade is the ordinary
        /// answer for an issue with nothing attached.
        /// </summary>
        /// <param name="issue">The issue.</param>
        /// <param name="to">The status it is being put into.</param>
        /// <param name="register">Where the findings are.</param>
        public static IssueCascade Plan(Issue? issue, FindingStatus to, FindingRegister? register)
        {
            var promoted = new List<Finding>();
            var unchanged = new List<Finding>();
            var higher = new List<Finding>();
            var missing = new List<string>();

            foreach (var id in issue?.Findings ?? Array.Empty<string>())
            {
                var finding = register?.Find(id);

                if (finding == null) { missing.Add(id); continue; }

                if (FindingStatusLattice.Merge(finding.Status, to) != finding.Status)
                {
                    promoted.Add(finding);
                    continue;
                }

                unchanged.Add(finding);

                if (finding.Status > to) higher.Add(finding);
            }

            return new IssueCascade(to, promoted, unchanged, higher, missing);
        }

        /// <summary>Do it. Returns how many findings changed.</summary>
        public int Apply()
        {
            foreach (var finding in Promoted)
                finding.Status = FindingStatusLattice.Merge(finding.Status, To);

            return Promoted.Count;
        }

        /// <summary>What the cascade would do, in the words a screen shows before it is applied.</summary>
        public string Describe()
        {
            var name = FindingStatusLattice.ToName(To);

            if (Promoted.Count == 0 && Unchanged.Count == 0 && Missing.Count == 0)
                return "No findings are attached to this issue.";

            var said = new List<string>
            {
                Count(Promoted.Count) + " " + (Promoted.Count == 1 ? "finding moves" : "findings move")
                + " to " + name,
            };

            if (Unchanged.Count > 0)
                said.Add(Count(Unchanged.Count) + " " + (Unchanged.Count == 1 ? "is" : "are")
                         + " already at " + name + " or beyond"
                         + (Higher.Count > 0 ? ", " + Count(Higher.Count) + " of them beyond it" : string.Empty));

            if (Missing.Count > 0)
                said.Add(Count(Missing.Count) + " " + (Missing.Count == 1 ? "is" : "are")
                         + " no longer in the register");

            return string.Join(", ", said) + ".";
        }

        private static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public override string ToString() => Describe();
    }
}
