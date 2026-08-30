using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Issues
{
    /// <summary>What a cascade would do, so somebody can be asked before it does it.</summary>
    public sealed class Cascade
    {
        internal Cascade(string issueStatus, FindingStatus to, IReadOnlyList<Finding> findings)
        {
            IssueStatus = issueStatus;
            To = to;
            Findings = findings;
        }

        /// <summary>The status the issue was just put into.</summary>
        public string IssueStatus { get; }

        /// <summary>What its findings would be marked.</summary>
        public FindingStatus To { get; }

        /// <summary>The findings that would change. Never includes one already at that status.</summary>
        public IReadOnlyList<Finding> Findings { get; }

        /// <summary>Whether there is anything to ask about.</summary>
        public bool IsEmpty => Findings.Count == 0;

        /// <summary>The question, in the words the board would use.</summary>
        public string Ask() =>
            "This issue is now " + IssueStatus.ToLowerInvariant() + ". Mark "
            + (Findings.Count == 1
                ? "the finding attached to it"
                : Findings.Count.ToString(CultureInfo.InvariantCulture) + " attached findings")
            + " as " + FindingStatusLattice.ToName(To) + " too?";

        /// <summary>Do it. Returns how many changed.</summary>
        public int Apply()
        {
            foreach (var finding in Findings) finding.Status = To;

            return Findings.Count;
        }
    }

    /// <summary>
    /// The join between an issue and the findings it is about.
    ///
    /// <b>Why an issue is not a finding.</b> A finding is what a check produced — a clash, a
    /// clearance breach, a rule a model failed. An issue is what somebody decided to do about one
    /// or several of them, and it can exist with no finding at all ("no access to this valve").
    /// Collapsing the two would mean either inventing machine findings for things no machine
    /// found, or giving every one of eighteen thousand clashes a conversation nobody will have.
    ///
    /// <b>Why the cascade asks.</b> Closing an issue usually means its clashes are dealt with, and
    /// usually is not always: an issue closed because the RFI was answered says nothing about
    /// whether the duct moved. An automatic invariant would quietly mark eighteen thousand
    /// findings resolved on the strength of one dropdown, and the board would go green over a
    /// building that had not changed. So it is offered, with the count, at the moment of the edit.
    /// </summary>
    public static class IssueFindings
    {
        /// <summary>
        /// What an issue's status means for its findings, or null when it means nothing.
        ///
        /// Only the two ends of the run map: an issue being closed says its findings are dealt
        /// with, and one being answered says somebody has looked. The middle states - draft,
        /// pending, in dispute - are about the conversation rather than about the model, and
        /// mapping them would be inventing a claim about the building out of a note about a
        /// meeting.
        /// </summary>
        /// <param name="issueStatus">The issue's status.</param>
        public static FindingStatus? StatusFor(string? issueStatus)
        {
            if (string.Equals(issueStatus, Issue.ClosedStatus, StringComparison.OrdinalIgnoreCase))
                return FindingStatus.Resolved;

            if (string.Equals(issueStatus, "Answered", StringComparison.OrdinalIgnoreCase))
                return FindingStatus.Reviewed;

            return null;
        }

        /// <summary>The findings an issue is about, in register order, skipping any it has lost.</summary>
        /// <param name="issue">The issue.</param>
        /// <param name="register">Where the findings are.</param>
        public static IReadOnlyList<Finding> Attached(Issue? issue, FindingRegister? register)
        {
            if (issue == null || register == null) return Array.Empty<Finding>();

            return issue.Findings
                        .Select(register.Find)
                        .Where(f => f != null)
                        .Select(f => f!)
                        .ToList();
        }

        /// <summary>
        /// Attach findings to an issue, skipping the ones already on it.
        /// </summary>
        /// <param name="issue">The issue.</param>
        /// <param name="findings">What to attach.</param>
        /// <returns>How many were newly attached.</returns>
        public static int Attach(Issue? issue, IEnumerable<Finding>? findings)
        {
            if (issue == null) return 0;

            var added = 0;

            foreach (var finding in findings ?? Array.Empty<Finding>())
            {
                if (finding == null || issue.Findings.Contains(finding.Id, StringComparer.Ordinal))
                    continue;

                issue.Findings.Add(finding.Id);
                added++;
            }

            return added;
        }

        /// <summary>Take findings off an issue. Detaching what is not attached is not an error.</summary>
        /// <param name="issue">The issue.</param>
        /// <param name="findingIds">The ids.</param>
        /// <returns>How many came off.</returns>
        public static int Detach(Issue? issue, IEnumerable<string>? findingIds)
        {
            if (issue == null) return 0;

            var gone = 0;

            foreach (var id in findingIds ?? Array.Empty<string>())
                if (id != null && issue.Findings.Remove(id)) gone++;

            return gone;
        }

        /// <summary>
        /// What marking an issue would do to its findings, so the question can be asked before it
        /// is done. Never null; an empty cascade is the ordinary answer.
        /// </summary>
        /// <param name="issue">The issue, already in its new status.</param>
        /// <param name="register">Where the findings are.</param>
        public static Cascade Proposed(Issue? issue, FindingRegister? register)
        {
            var status = issue == null ? null : StatusFor(issue.Status);

            if (status == null)
                return new Cascade(issue?.Status ?? string.Empty, FindingStatus.New,
                                   Array.Empty<Finding>());

            var changing = Attached(issue, register)
                           .Where(f => f.Status != status.Value)
                           .ToList();

            return new Cascade(issue!.Status, status.Value, changing);
        }

        /// <summary>
        /// Swap an older issue's clash keys for finding ids, using the findings a read just made.
        ///
        /// <b>Why this is not a one-time migration.</b> A clash key names two elements and a place
        /// but not the test that found them, so nothing can turn one into a finding id on its own.
        /// It takes a read of the results, where the key and the finding are in hand together —
        /// which is why this runs on every read and is written to be idempotent. A key whose
        /// result this run did not report stays where it is rather than being dropped: the issue
        /// still says which clash it is about, and next month's wider run may well find it.
        /// </summary>
        /// <param name="register">The issues.</param>
        /// <param name="found">The finding for a clash key, or null when this run reported none.</param>
        /// <returns>How many issues took at least one finding id they did not have.</returns>
        public static int Carry(IssueRegister? register, Func<ClashKey, Finding?>? found)
        {
            if (register == null || found == null) return 0;

            var moved = 0;

            foreach (var issue in register.Issues)
            {
                if (issue.Clashes.Count == 0) continue;

                var swapped = false;

                foreach (var key in issue.Clashes.ToList())
                {
                    var finding = found(key);

                    if (finding == null) continue;

                    if (!issue.Findings.Contains(finding.Id, StringComparer.Ordinal))
                    {
                        issue.Findings.Add(finding.Id);
                        swapped = true;
                    }

                    // Off the old list only once it is on the new one, so a crash between the two
                    // leaves the issue saying which clash it is about rather than saying nothing.
                    issue.Clashes.Remove(key);
                }

                if (swapped) moved++;
            }

            return moved;
        }
    }
}
