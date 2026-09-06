using System;
using System.IO;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Findings and issues as BCF, and what a returned file says.
    ///
    /// The round trip is the point, so most of these go out through the writer and come back
    /// through the reader rather than asserting on a topic object. What matters is that a finding
    /// comes home to itself, that a stale file cannot undo a decision taken since it left, and
    /// that a topic nothing here answers to becomes an issue rather than a made-up finding.
    /// </summary>
    public class FindingBcfTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.ifc");

        private static ElementKey Key(char name) =>
            ElementKey.FromInstanceGuid(Scope,
                Guid.Parse(name + "0000000-0000-0000-0000-000000000000"));

        private static Finding Made(string title = "Duct through beam")
        {
            var finding = Finding.Create(FindingSource.Clash, "MEP vs Structure", title,
                                         new[] { Key('a'), Key('b') }, "1,2,3");

            finding.LastSeenTicks = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            finding.Detail = "Duct 300x200 against Beam UB457";
            finding.Facts["Distance"] = "-0.045";

            return finding;
        }

        /// <summary>Out through the writer, back through the reader, as a real archive.</summary>
        private static BcfTopic Around(BcfTopic topic, Action<BcfTopic>? edited = null)
        {
            using var first = new MemoryStream();
            BcfWriter.Write(first, new[] { topic }, BcfVersion.V21);
            first.Position = 0;

            var read = BcfReader.Read(first).Topics.Single();

            edited?.Invoke(read);

            using var second = new MemoryStream();
            BcfWriter.Write(second, new[] { read }, BcfVersion.V21);
            second.Position = 0;

            return BcfReader.Read(second).Topics.Single();
        }

        [Fact]
        public void AFindingGoesOutAsATopicAndComesBackAsItself()
        {
            var finding = Made();
            var topic = FindingBcf.ToTopic(finding);

            Assert.Equal(FindingBcf.TopicGuidFor(finding), topic.Guid);
            Assert.Equal("Clash", topic.TopicType);
            Assert.Equal("New", topic.TopicStatus);
            Assert.Contains("Found by MEP vs Structure (Clash).", topic.Description);
            Assert.Contains("Distance: -0.045", topic.Description);
            Assert.Contains(FindingBcf.SourceLabel + "Clash", topic.Labels);
            Assert.Contains(FindingBcf.RuleLabel + "MEP vs Structure", topic.Labels);
        }

        [Fact]
        public void TheTopicIsStampedOnSoTheFileCanComeHome()
        {
            // A file that went out under a name the register does not know cannot come home.
            var finding = Made();

            Assert.Null(finding.TopicGuid);

            var topic = FindingBcf.ToTopic(finding);

            Assert.Equal(topic.Guid, finding.TopicGuid);
        }

        [Fact]
        public void ATopicItAlreadyHasWinsOverAFreshDerivation()
        {
            var finding = Made();
            finding.TopicGuid = BcfGuid.For("it-came-out-as-this");

            Assert.Equal(finding.TopicGuid, FindingBcf.ToTopic(finding).Guid);
        }

        [Fact]
        public void WhatSomebodyElseSetComesHomeOntoTheFinding()
        {
            var finding = Made();
            var register = new FindingRegister();
            register.Put(finding);

            var back = Around(FindingBcf.ToTopic(finding), topic =>
            {
                topic.TopicStatus = "Reviewed";
                topic.Priority = "High";
                topic.Stage = "Construction";
                topic.AssignedTo = "Structures";
                topic.DueDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
                topic.Labels.Add("Site query");

                topic.Comments.Add(new BcfComment(
                    BcfGuid.For("theirs"),
                    new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero),
                    "ada", "The beam cannot move."));
            });

            var (answered, strangers) = FindingBcf.Merge(register, new[] { back }, "ada.bcfzip");

            Assert.Equal(1, answered);
            Assert.Empty(strangers);

            Assert.Equal(FindingStatus.Reviewed, finding.Status);
            Assert.Equal("High", finding.Priority);
            Assert.Equal("Construction", finding.Stage);
            Assert.Equal("Structures", finding.AssignedTo);
            Assert.Equal(new DateTime(2026, 4, 15), finding.Due);
            Assert.Contains("Site query", finding.Labels);
            Assert.Contains(finding.Comments, c => c.Body == "The beam cannot move.");
            Assert.Equal("ada.bcfzip", finding.Origin);
        }

        [Fact]
        public void AStaleFileCannotUndoADecisionTakenSinceItLeft()
        {
            // The lattice, on the one path where two people really did edit offline.
            var finding = Made();
            var topic = FindingBcf.ToTopic(finding);

            finding.Status = FindingStatus.Resolved;

            topic.TopicStatus = "Active";
            FindingBcf.Apply(topic, finding);

            Assert.Equal(FindingStatus.Resolved, finding.Status);
        }

        [Fact]
        public void OurOwnLabelsDoNotComeBackAsSomebodysTags()
        {
            // Otherwise the file grows a label a cycle, for ever.
            var finding = Made();
            var topic = FindingBcf.ToTopic(finding);

            FindingBcf.Apply(topic, finding);
            FindingBcf.Apply(topic, finding);

            Assert.Empty(finding.Labels);
        }

        [Fact]
        public void TheSameFileTwiceSaysNothingTheSecondTime()
        {
            var finding = Made();
            var register = new FindingRegister();
            register.Put(finding);

            var back = Around(FindingBcf.ToTopic(finding), t => t.TopicStatus = "Reviewed");

            Assert.Equal(1, FindingBcf.Merge(register, new[] { back }).Answered);
            Assert.Equal(0, FindingBcf.Merge(register, new[] { back }).Answered);
        }

        [Fact]
        public void ATopicNothingAnswersToIsAStrangerRatherThanAnInventedFinding()
        {
            // A finding is only ever something a check produced. A row no re-run can confirm or
            // clear is a row that never goes away.
            var register = new FindingRegister();
            register.Put(Made());

            var theirs = new BcfTopic(BcfGuid.For("their-own-issue"), "No access to this valve",
                                      DateTimeOffset.UtcNow, "ada");

            var (answered, strangers) = FindingBcf.Merge(register, new[] { theirs });

            Assert.Equal(0, answered);
            Assert.Equal("No access to this valve", strangers.Single().Title);
            Assert.Single(register.All);
        }

        [Fact]
        public void ElementsTravelAsIfcGlobalIdsSoTheOtherToolCanSelectThem()
        {
            // The GlobalId read off the element, which is the id the receiving model uses.
            var topic = FindingBcf.ToTopic(Made(), "CamelWorks",
                                           _ => "11111111-1111-1111-1111-111111111111");

            var viewpoint = topic.Viewpoints.Single();

            Assert.Equal(2, viewpoint.Selection.Count);
            Assert.All(viewpoint.Selection, c => Assert.True(c.IsResolvable));
        }

        [Fact]
        public void AnElementWithNoGlobalIdIsLeftOutRatherThanNamedWrongly()
        {
            // A key made from a Navisworks instance GUID is not an IFC GlobalId, and writing one
            // as though it were would name a different element in the receiving model.
            Assert.Empty(FindingBcf.ToTopic(Made()).Viewpoints);
        }

        [Fact]
        public void NoResolvableElementMeansNoViewpointRatherThanADeadOne()
        {
            // A viewpoint listing components nothing can find is a button that does nothing in
            // somebody else's tool, which is worse than no button.
            var finding = Finding.Create(FindingSource.Health, "Every element has a level",
                                         "No level", Array.Empty<ElementKey>());

            Assert.Empty(FindingBcf.ToTopic(finding).Viewpoints);
        }

        [Fact]
        public void AnIssueAndItsFindingsTravelRelatedBothWays()
        {
            var finding = Made();
            var issue = new Issue("I1", "Duct run through the transfer beam", FindingSource.Clash);

            IssueFindings.Attach(issue, new[] { finding });

            var topics = FindingBcf.Together(new[] { finding },
                                             new[] { IssueBcf.ToTopic(issue) },
                                             t => issue.Findings);

            Assert.Equal(2, topics.Count);

            var sent = topics.First(t => t.Guid == FindingBcf.TopicGuidFor(finding));
            var conversation = topics.First(t => t.Guid != sent.Guid);

            Assert.Contains(conversation.Guid, sent.RelatedTopics);
            Assert.Contains(sent.Guid, conversation.RelatedTopics);
        }

        [Fact]
        public void TheRelationSurvivesTheArchive()
        {
            var finding = Made();
            var issue = new Issue("I1", "Duct run through the transfer beam", FindingSource.Clash);

            IssueFindings.Attach(issue, new[] { finding });

            var topics = FindingBcf.Together(new[] { finding },
                                             new[] { IssueBcf.ToTopic(issue) },
                                             t => issue.Findings);

            using var stream = new MemoryStream();
            BcfWriter.Write(stream, topics, BcfVersion.V21);
            stream.Position = 0;

            var read = BcfReader.Read(stream).Topics;

            Assert.Equal(2, read.Count);
            Assert.All(read, t => Assert.Single(t.RelatedTopics));
        }

        [Fact]
        public void AFindingSaysHowManyPlacesAreOnIt()
        {
            var finding = Finding.CreateParent(FindingSource.Health, "Geometry",
                                               "37 elements sit on top of each other", "origin-stack");

            finding.AddLocation(new FindingLocation("x", new[] { Key('a') }));

            Assert.Contains("1 places are named on it.", FindingBcf.ToTopic(finding).Description);
        }

        [Fact]
        public void AFindingTheLatestRunNoLongerReportsSaysSo()
        {
            var finding = Made();
            finding.IsPresent = false;

            Assert.Contains("The latest run no longer reports this.",
                            FindingBcf.ToTopic(finding).Description);
        }

        [Fact]
        public void NothingToSendIsNotAFailure()
        {
            Assert.Empty(FindingBcf.Together(null, null, null));
            Assert.Equal((0, 0), Counts(FindingBcf.Merge(null, null)));
            Assert.Equal((0, 0), Counts(FindingBcf.Merge(new FindingRegister(), null)));
        }

        private static (int, int) Counts((int Answered, System.Collections.Generic.IReadOnlyList<BcfTopic> Strangers) result) =>
            (result.Answered, result.Strangers.Count);
    }
}
