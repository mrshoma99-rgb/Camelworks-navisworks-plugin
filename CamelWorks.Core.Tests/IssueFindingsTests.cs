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
    /// The behaviour under test is mostly about what does NOT happen: a cascade that fires without
    /// being asked, a status map that invents a claim about the building out of a note about a
    /// meeting, an attach that doubles, and a carry-over that drops a clash key before it has
    /// anywhere else to say the same thing.
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

            var issue = new Issue("I1", "Duct through transfer beam", IssueOrigin.Clash);

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
        public void OnlyTheTwoEndsOfTheRunMeanAnythingForTheModel()
        {
            // Draft, pending and in dispute are about the conversation. Mapping them would invent
            // a claim about the building out of a note about a meeting.
            Assert.Equal(FindingStatus.Resolved, IssueFindings.StatusFor("Closed"));
            Assert.Equal(FindingStatus.Reviewed, IssueFindings.StatusFor("Answered"));

            foreach (var quiet in new[] { "Draft", "Open", "Pending", "In dispute", null, "" })
                Assert.Null(IssueFindings.StatusFor(quiet));
        }

        [Fact]
        public void ClosingAnIssueProposesTheCascadeRatherThanDoingIt()
        {
            var finding = Made();
            var (issue, register) = Pair(finding);

            issue.Status = Issue.ClosedStatus;

            var cascade = IssueFindings.Proposed(issue, register);

            Assert.False(cascade.IsEmpty);
            Assert.Equal(FindingStatus.Resolved, cascade.To);
            Assert.Contains("Mark the finding attached to it as Resolved too?", cascade.Ask());

            // Nothing has happened yet. That is the whole point.
            Assert.Equal(FindingStatus.New, finding.Status);

            Assert.Equal(1, cascade.Apply());
            Assert.Equal(FindingStatus.Resolved, finding.Status);
        }

        [Fact]
        public void TheQuestionNamesTheCount()
        {
            var (issue, register) = Pair(Made(), Made('c', 'd'), Made('e', 'f'));

            issue.Status = Issue.ClosedStatus;

            Assert.Contains("Mark 3 attached findings as Resolved too?",
                            IssueFindings.Proposed(issue, register).Ask());
        }

        [Fact]
        public void AStatusThatSaysNothingAboutTheModelProposesNothing()
        {
            var (issue, register) = Pair(Made());

            issue.Status = "In dispute";

            Assert.True(IssueFindings.Proposed(issue, register).IsEmpty);
        }

        [Fact]
        public void AFindingAlreadyAtThatStatusIsNotOfferedAgain()
        {
            var finding = Made();
            var (issue, register) = Pair(finding);

            finding.Status = FindingStatus.Resolved;
            issue.Status = Issue.ClosedStatus;

            Assert.True(IssueFindings.Proposed(issue, register).IsEmpty);
        }

        [Fact]
        public void AnOlderIssuesClashKeysBecomeFindingIdsOnTheNextRead()
        {
            var finding = Made();
            var key = ClashKey.Create(Key('a'), Key('b'), 1, 2, 3);

            var issue = new Issue("I1", "Raised before findings existed", IssueOrigin.Clash);
            issue.Clashes.Add(key);

            var register = new IssueRegister();
            register.Put(issue);

            Assert.Equal(1, IssueFindings.Carry(register, k => k.Equals(key) ? finding : null));

            Assert.Equal(new[] { finding.Id }, issue.Findings);
            Assert.Empty(issue.Clashes);
        }

        [Fact]
        public void AKeyThisRunDidNotReportStaysWhereItIs()
        {
            // The issue still says which clash it is about, and next month's wider run may find
            // it. Dropping it would lose that for nothing.
            var issue = new Issue("I1", "Raised before findings existed", IssueOrigin.Clash);
            issue.Clashes.Add(ClashKey.Create(Key('a'), Key('b'), 1, 2, 3));

            var register = new IssueRegister();
            register.Put(issue);

            Assert.Equal(0, IssueFindings.Carry(register, _ => null));
            Assert.Single(issue.Clashes);
            Assert.Empty(issue.Findings);
        }

        [Fact]
        public void CarryingTwiceChangesNothing()
        {
            var finding = Made();
            var key = ClashKey.Create(Key('a'), Key('b'), 1, 2, 3);

            var issue = new Issue("I1", "Raised before findings existed", IssueOrigin.Clash);
            issue.Clashes.Add(key);

            var register = new IssueRegister();
            register.Put(issue);

            IssueFindings.Carry(register, k => k.Equals(key) ? finding : null);
            IssueFindings.Carry(register, k => k.Equals(key) ? finding : null);

            Assert.Single(issue.Findings);
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
