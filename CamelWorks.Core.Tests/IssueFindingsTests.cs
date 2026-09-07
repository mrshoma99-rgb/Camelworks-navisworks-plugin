using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The join between an issue and the findings it is about.
    ///
    /// The behaviour under test is mostly about what does NOT happen: an attach that doubles, a
    /// lookup that throws over a finding the register has lost, and a second issue over one finding
    /// being refused. What marking an issue does to its findings is IssueCascadeTests.
    /// </summary>
    public class IssueFindingsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.ifc");

        private static ElementKey Key(char name) =>
            ElementKey.FromInstanceGuid(Scope, Guid.Parse(name + "0000000-0000-0000-0000-000000000000"));

        private static Finding Made(char a = 'a', char b = 'b', string where = "1,2,3") =>
            Finding.Create(FindingSource.Clash, "MEP vs Structure", "Duct through beam",
                           new[] { Key(a), Key(b) }, where);

        private static (Issue, FindingRegister) Pair(params Finding[] findings)
        {
            var register = new FindingRegister();

            foreach (var finding in findings) register.Put(finding);

            var issue = new Issue("I1", "Duct through transfer beam", FindingSource.Clash);

            IssueFindings.Attach(issue, findings);

            return (issue, register);
        }

        [Fact]
        public void AnIssueCanHoldFindingsOfAnySource()
        {
            var clash = Made();

            var health = Finding.Create(FindingSource.Health, "Every element has a level",
                                        "No level", new[] { Key('c') });

            var (issue, register) = Pair(clash, health);

            Assert.Equal(new[] { clash.Id, health.Id }, issue.Findings);
            Assert.Equal(2, IssueFindings.Attached(issue, register).Count);
        }

        [Fact]
        public void AttachingTwiceAttachesOnce()
        {
            var finding = Made();
            var (issue, _) = Pair(finding);

            Assert.Equal(0, IssueFindings.Attach(issue, new[] { finding }));
            Assert.Single(issue.Findings);
        }

        [Fact]
        public void OneFindingMaySitOnTwoIssues()
        {
            // The same duct through the same beam can be a coordination issue and an RFI at once.
            // Refusing the second would be refusing to describe the job.
            var finding = Made();
            var register = new IssueRegister();

            foreach (var id in new[] { "I1", "I2" })
            {
                var issue = new Issue(id, "About the duct");
                IssueFindings.Attach(issue, new[] { finding });
                register.Put(issue);
            }

            Assert.Equal(2, register.Holding(finding.Id).Count);
        }

        [Fact]
        public void AFindingTheRegisterHasLostIsSkippedRatherThanThrown()
        {
            var issue = new Issue("I1", "About something gone");
            issue.Findings.Add("a-finding-that-is-not-there");

            Assert.Empty(IssueFindings.Attached(issue, new FindingRegister()));
        }

        [Fact]
        public void WhatIsAttachedSurvivesTheProjectFile()
        {
            var finding = Made();
            var (issue, _) = Pair(finding);

            var back = Issue.FromJson(JsonReader.Parse(issue.ToJson().ToJson()));

            Assert.NotNull(back);
            Assert.Equal(new[] { finding.Id }, back!.Findings);
        }

        [Fact]
        public void DetachingWhatIsNotAttachedIsNotAnError()
        {
            var (issue, _) = Pair(Made());

            Assert.Equal(0, IssueFindings.Detach(issue, new[] { "not-on-it" }));
            Assert.Equal(1, IssueFindings.Detach(issue, issue.Findings.ToList()));
            Assert.Empty(issue.Findings);
        }
    }
}
