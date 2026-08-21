using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class BcfResponseTests
    {
        private static readonly DateTimeOffset When = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

        private static BcfTopic Topic(string guid, string status, string? assignedTo = null) =>
            new BcfTopic(guid, "Duct through beam", When, "A Coordinator")
            {
                TopicStatus = status,
                AssignedTo = assignedTo,
            };

        /// <summary>
        /// What the project exported, keyed the way the reader keys it.
        ///
        /// By the topic's own GUID, not by the id it was made from: BcfTopic derives a schema-legal
        /// GUID from whatever it is handed, so a map built on the raw id would match nothing.
        /// </summary>
        private static Dictionary<string, string> Sent(params (BcfTopic Topic, string Group)[] rows) =>
            rows.ToDictionary(r => r.Topic.Guid, r => r.Group, StringComparer.Ordinal);

        private static Dictionary<string, FindingStatus> Board(params (string Group, FindingStatus Status)[] rows) =>
            rows.ToDictionary(r => r.Group, r => r.Status, StringComparer.Ordinal);

        [Theory]
        [InlineData("Open", FindingStatus.Active)]
        [InlineData("In Progress", FindingStatus.Active)]
        [InlineData("ReOpened", FindingStatus.Active)]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        [InlineData("Approved", FindingStatus.Approved)]
        [InlineData("accepted", FindingStatus.Approved)]
        [InlineData("Closed", FindingStatus.Resolved)]
        [InlineData("resolved", FindingStatus.Resolved)]
        public void The_conventional_vocabulary_maps(string word, FindingStatus expected)
        {
            Assert.Equal(expected, BcfResponse.StatusOf(word));
        }

        [Theory]
        [InlineData("Awaiting client")]
        [InlineData("")]
        [InlineData(null)]
        public void A_status_nobody_knows_is_not_guessed_at(string? word)
        {
            // BCF lets every project define its own vocabulary. Mapping an unknown word onto Active
            // would be inventing a decision nobody made.
            Assert.Null(BcfResponse.StatusOf(word));
        }

        [Fact]
        public void A_stale_open_does_not_undo_an_approval()
        {
            // Two people edited the same issue while neither could see the other. The file was
            // exported on Tuesday and the approval made on Wednesday.
            var topic = Topic("t1", "Open");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2 - MEP v STR")),
                Board(("Level 2 - MEP v STR", FindingStatus.Approved)));

            var only = Assert.Single(report.Matched);

            Assert.Equal(FindingStatus.Approved, only.Merged);
            Assert.False(only.Changes);
            Assert.True(only.Refused);
            Assert.Equal(1, report.Refused);
            Assert.Contains("stays where it is", only.Explain());
        }

        [Fact]
        public void An_explicit_re_open_is_honoured_even_downwards()
        {
            // Not a stale edit: somebody saying, in front of the evidence, that the thing you closed
            // is not fixed. Refusing it would make CamelWorks the tool that argues with the site.
            var topic = Topic("t1", "ReOpened");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2 - MEP v STR")),
                Board(("Level 2 - MEP v STR", FindingStatus.Resolved)));

            var only = Assert.Single(report.Matched);

            Assert.True(only.IsReopen);
            Assert.Equal(FindingStatus.Active, only.Merged);
            Assert.True(only.Changes);
            Assert.False(only.Refused);
            Assert.Equal(1, report.Reopened);
            Assert.Contains("re-opened it", only.Explain());
        }

        [Fact]
        public void Progress_in_the_file_is_taken()
        {
            var topic = Topic("t1", "Closed");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2")),
                Board(("Level 2", FindingStatus.Active)));

            var only = Assert.Single(report.Matched);

            Assert.Equal(FindingStatus.Resolved, only.Merged);
            Assert.True(only.Changes);
            Assert.Equal(1, report.Changes);
        }

        [Fact]
        public void An_unknown_status_changes_nothing_and_is_named()
        {
            var topic = Topic("t1", "Awaiting client");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2")),
                Board(("Level 2", FindingStatus.Active)));

            var only = Assert.Single(report.Matched);

            Assert.Equal("Awaiting client", only.UnknownStatus);
            Assert.Equal(FindingStatus.Active, only.Merged);
            Assert.False(only.Changes);
            Assert.Equal(1, report.Unknown);
            Assert.Contains("Awaiting client", only.Explain());
        }

        [Fact]
        public void A_topic_this_project_never_sent_is_kept_apart()
        {
            // Somebody else's coordinator raising issues in their own tool is the normal way a BCF
            // conversation starts. It is not an error and it is not this board's status to change.
            var mine = Topic("t1", "Closed");
            var theirs = Topic("stranger", "Open");

            var report = BcfResponse.Read(
                new[] { mine, theirs },
                Sent((mine, "Level 2")),
                Board(("Level 2", FindingStatus.Active)));

            Assert.Single(report.Matched);
            Assert.Equal(theirs.Guid, Assert.Single(report.Unmatched).Guid);
            Assert.Contains("never sent", report.Explain());
        }

        [Fact]
        public void A_group_the_board_no_longer_holds_is_treated_as_outstanding()
        {
            // The group was exported and has since been re-derived under another name. Starting it
            // at Active rather than New keeps a returned closure meaningful instead of throwing it
            // away for lack of a row to compare against.
            var topic = Topic("t1", "Closed");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2")),
                Board());

            var only = Assert.Single(report.Matched);

            Assert.Equal(FindingStatus.Active, only.Current);
            Assert.Equal(FindingStatus.Resolved, only.Merged);
        }

        [Fact]
        public void The_assignee_comes_across_when_the_file_carries_one()
        {
            var topic = Topic("t1", "Open", "Mechanical");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2")),
                Board(("Level 2", FindingStatus.Active)));

            Assert.Equal("Mechanical", Assert.Single(report.Matched).AssignedTo);
        }

        [Fact]
        public void An_empty_assignee_is_nothing_rather_than_blank()
        {
            var topic = Topic("t1", "Open", "   ");

            var report = BcfResponse.Read(
                new[] { topic },
                Sent((topic, "Level 2")),
                Board(("Level 2", FindingStatus.Active)));

            Assert.Null(Assert.Single(report.Matched).AssignedTo);
        }

        [Fact]
        public void Nothing_returned_is_a_readable_answer_rather_than_an_error()
        {
            var report = BcfResponse.Read(Array.Empty<BcfTopic>(), Sent(), Board());

            Assert.Empty(report.Matched);
            Assert.Empty(report.Unmatched);
            Assert.Equal(0, report.Changes);
            Assert.Contains("0 of 0", report.Explain());
        }
    }
}
