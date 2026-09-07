using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// What marking an issue does to the findings attached to it.
    ///
    /// Two things are pinned here because both were live faults: a cascade must merge rather than
    /// assign, so answering an issue cannot pull an approved finding back, and it must apply to
    /// every status rather than to the two the old mapping table covered.
    /// </summary>
    public class IssueCascadeTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.ifc");

        private static ElementKey Key(int n) =>
            ElementKey.FromTreePath(Scope, "MEP.ifc", "Duct " + n);

        private static Finding Made(int n) =>
            Finding.Create(FindingSource.Clash, "MEP vs Structure", "Duct through beam",
                           new[] { Key(n) }, n.ToString());

        private static (Issue, FindingRegister) Over(params Finding[] findings)
        {
            var register = new FindingRegister();

            foreach (var finding in findings) register.Put(finding);

            var issue = new Issue("CW-1", "Duct run through the transfer beam");
            IssueFindings.Attach(issue, findings);

            return (issue, register);
        }

        [Fact]
        public void An_approved_finding_is_not_demoted_by_a_cascade_to_reviewed()
        {
            // The live fault: the cascade assigned rather than merged, so answering an issue pulled
            // an approved finding back to Reviewed and lost somebody's sign-off.
            var approved = Made(1);
            approved.Status = FindingStatus.Approved;

            var fresh = Made(2);

            var (issue, register) = Over(approved, fresh);

            var cascade = IssueCascade.Plan(issue, FindingStatus.Reviewed, register);

            Assert.Same(fresh, Assert.Single(cascade.Promoted));
            Assert.Same(approved, Assert.Single(cascade.Higher));
            Assert.Same(approved, Assert.Single(cascade.Unchanged));

            Assert.Equal(1, cascade.Apply());

            Assert.Equal(FindingStatus.Approved, approved.Status);
            Assert.Equal(FindingStatus.Reviewed, fresh.Status);
        }

        [Fact]
        public void A_finding_already_at_the_target_is_not_written()
        {
            var settled = Made(1);
            settled.Status = FindingStatus.Reviewed;

            var (issue, register) = Over(settled);

            var cascade = IssueCascade.Plan(issue, FindingStatus.Reviewed, register);

            Assert.True(cascade.IsEmpty);
            Assert.Empty(cascade.Higher);
            Assert.Same(settled, Assert.Single(cascade.Unchanged));
            Assert.Equal(0, cascade.Apply());
        }

        [Fact]
        public void Every_status_cascades()
        {
            // With the issue on the lattice there is no mapping table left to be incomplete. The
            // old one covered Closed and Answered and said nothing for the other four.
            foreach (var to in new[]
                     {
                         FindingStatus.Active, FindingStatus.Reviewed,
                         FindingStatus.Approved, FindingStatus.Resolved,
                     })
            {
                var finding = Made(1);
                var (issue, register) = Over(finding);

                Assert.Equal(1, IssueCascade.Plan(issue, to, register).Apply());
                Assert.Equal(to, finding.Status);
            }
        }

        [Fact]
        public void A_thousand_findings_are_written_and_three_lost_ids_are_reported()
        {
            var findings = Enumerable.Range(0, 1000).Select(Made).ToArray();
            var (issue, register) = Over(findings);

            foreach (var gone in new[] { "gone-1", "gone-2", "gone-3" }) issue.Findings.Add(gone);

            var cascade = IssueCascade.Plan(issue, FindingStatus.Resolved, register);

            Assert.Equal(1000, cascade.Promoted.Count);
            Assert.Equal(new[] { "gone-1", "gone-2", "gone-3" }, cascade.Missing);
            Assert.Equal(1000, cascade.Apply());
            Assert.All(findings, f => Assert.Equal(FindingStatus.Resolved, f.Status));

            Assert.Contains("1,000 findings move to Resolved", cascade.Describe());
            Assert.Contains("3 are no longer in the register", cascade.Describe());
        }

        [Fact]
        public void One_finding_on_two_issues_takes_the_higher_of_the_two_cascades()
        {
            // The lattice does this without a rule of its own.
            var finding = Made(1);
            var register = new FindingRegister();
            register.Put(finding);

            var reviewed = new Issue("CW-1", "One conversation");
            var resolved = new Issue("CW-2", "Another conversation");

            IssueFindings.Attach(reviewed, new[] { finding });
            IssueFindings.Attach(resolved, new[] { finding });

            IssueCascade.Plan(resolved, FindingStatus.Resolved, register).Apply();
            IssueCascade.Plan(reviewed, FindingStatus.Reviewed, register).Apply();

            Assert.Equal(FindingStatus.Resolved, finding.Status);
        }

        [Fact]
        public void An_issue_with_nothing_attached_cascades_nothing()
        {
            var cascade = IssueCascade.Plan(new Issue("CW-1", "Typed by hand"),
                                            FindingStatus.Resolved, new FindingRegister());

            Assert.True(cascade.IsEmpty);
            Assert.Equal("No findings are attached to this issue.", cascade.Describe());
        }

        [Fact]
        public void The_issues_own_status_is_a_direct_edit_and_may_be_demoted()
        {
            // A person deciding about an issue may put it back; the screen confirms on the demotion
            // rather than the record refusing it.
            var issue = new Issue("CW-1", "Reopened") { Status = FindingStatus.Resolved };

            Assert.True(FindingStatusLattice.IsDemotion(issue.Status, FindingStatus.Active));

            issue.Status = FindingStatus.Active;

            Assert.True(issue.IsOpen);
        }
    }
}
