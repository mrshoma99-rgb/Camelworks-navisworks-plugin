using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;

namespace CamelWorks.Core.Issues
{
    /// <summary>
    /// The join between an issue and the findings it is about.
    ///
    /// A finding is what a check produced — a clash, a clearance breach, a rule a model failed. An
    /// issue is what somebody decided to do about one or several of them, and it can exist with no
    /// finding at all ("no access to this valve"). Collapsing the two would mean either inventing
    /// machine findings for things no machine found, or giving every one of eighteen thousand
    /// clashes a conversation nobody will have.
    ///
    /// What marking an issue does to its findings is <see cref="IssueCascade"/>.
    /// </summary>
    public static class IssueFindings
    {
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
    }
}
