using System;
using System.Linq;
using CamelWorks.Core.OpenBim;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Six packs come back on Thursday holding the same forty issues. Bringing them together is the
    /// difference between a tool one person drives and one a team uses.
    /// </summary>
    public class BcfMergeTests
    {
        private static readonly DateTimeOffset Monday = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

        private static BcfTopic Topic(string id, string status = "Open", string? assigned = null)
        {
            var topic = new BcfTopic(id, "Issue " + id, Monday, "coordinator")
            {
                TopicStatus = status,
                AssignedTo = assigned,
            };

            return topic;
        }

        private static BcfTopic Answered(string id, string status, string? assigned = null,
                                         int daysLater = 1, string? comment = null)
        {
            var topic = Topic(id, status, assigned);
            topic.ModifiedDate = Monday.AddDays(daysLater);
            topic.ModifiedAuthor = "reviewer";

            if (comment != null)
                topic.Comments.Add(new BcfComment(id + "-c" + daysLater.ToString(), Monday.AddDays(daysLater),
                                                  "reviewer", comment));

            return topic;
        }

        [Fact]
        public void An_answer_nobody_contradicts_is_taken()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1"), Topic("2") },
                new[] { new MergeReturn("Mechanical", new[] { Answered("1", "Closed") }) });

            Assert.Equal("Closed", result.Topics.Single(t => t.Title == "Issue 1").TopicStatus);
            Assert.Equal("Open", result.Topics.Single(t => t.Title == "Issue 2").TopicStatus);
            Assert.Equal(1, result.Changed);
            Assert.True(result.IsClean);
        }

        [Fact]
        public void Two_reviewers_who_disagree_settle_nothing_and_are_both_named()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[]
                {
                    new MergeReturn("Mechanical", new[] { Answered("1", "Closed") }),
                    new MergeReturn("Structural", new[] { Answered("1", "Rejected") }),
                });

            // The topic keeps what it had. Picking one silently is how a closed issue reopens
            // itself a week later with nobody able to say who did it.
            Assert.Equal("Open", result.Topics.Single().TopicStatus);

            var conflict = Assert.Single(result.Conflicts);
            Assert.Equal("status", conflict.Field);
            Assert.Equal("Open", conflict.Baseline);
            Assert.Equal(new[] { "Mechanical", "Structural" }, conflict.Answers.Select(a => a.Party));
            Assert.False(result.IsClean);
        }

        [Fact]
        public void Three_reviewers_disagreeing_is_one_conflict_naming_three()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[]
                {
                    new MergeReturn("A", new[] { Answered("1", "Closed") }),
                    new MergeReturn("B", new[] { Answered("1", "Rejected") }),
                    new MergeReturn("C", new[] { Answered("1", "ReOpened") }),
                });

            Assert.Single(result.Conflicts);
            Assert.Equal(3, result.Conflicts[0].Answers.Count);
        }

        [Fact]
        public void Under_latest_the_most_recent_answer_wins()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[]
                {
                    new MergeReturn("Mechanical", new[] { Answered("1", "Closed", daysLater: 1) }),
                    new MergeReturn("Structural", new[] { Answered("1", "Rejected", daysLater: 3) }),
                },
                StatusConflicts.Latest);

            Assert.Equal("Rejected", result.Topics.Single().TopicStatus);
            Assert.Empty(result.Conflicts);
        }

        [Fact]
        public void Two_reviewers_who_agree_are_not_a_conflict()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[]
                {
                    new MergeReturn("Mechanical", new[] { Answered("1", "Closed") }),
                    new MergeReturn("Structural", new[] { Answered("1", "Closed") }),
                });

            Assert.Equal("Closed", result.Topics.Single().TopicStatus);
            Assert.Empty(result.Conflicts);
        }

        [Fact]
        public void Every_comment_survives_even_when_the_answers_conflict()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[]
                {
                    new MergeReturn("Mechanical", new[] { Answered("1", "Closed", daysLater: 1, comment: "Moved the duct.") }),
                    new MergeReturn("Structural", new[] { Answered("1", "Rejected", daysLater: 2, comment: "Beam cannot move.") }),
                });

            // The status is unresolved; the reasoning that would settle it is not thrown away.
            var topic = result.Topics.Single();

            Assert.Equal(2, topic.Comments.Count);
            Assert.Equal(2, result.Comments);
            Assert.Single(result.Conflicts);
        }

        [Fact]
        public void Merging_the_same_pack_twice_does_not_double_its_comments()
        {
            var baseline = new[] { Topic("1") };
            var returned = new MergeReturn("Mechanical", new[] { Answered("1", "Closed", comment: "Moved it.") });

            var once = BcfMerge.Merge(baseline, new[] { returned });
            var twice = BcfMerge.Merge(once.Topics, new[] { returned });

            Assert.Single(twice.Topics.Single().Comments);
        }

        [Fact]
        public void An_issue_a_reviewer_raised_is_kept()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1") },
                new[] { new MergeReturn("Mechanical", new[] { Topic("1"), Answered("99", "Open", comment: "Found this too.") }) });

            Assert.Equal(2, result.Topics.Count);
            Assert.Equal(1, result.Added);
            Assert.Contains(result.Topics, t => t.Title == "Issue 99");
        }

        [Fact]
        public void An_assignment_is_merged_like_a_status()
        {
            var result = BcfMerge.Merge(
                new[] { Topic("1", assigned: "nobody") },
                new[]
                {
                    new MergeReturn("Mechanical", new[] { Answered("1", "Open", assigned: "Ali") }),
                    new MergeReturn("Structural", new[] { Answered("1", "Open", assigned: "Sam") }),
                });

            Assert.Equal("nobody", result.Topics.Single().AssignedTo);
            Assert.Equal("assignment", Assert.Single(result.Conflicts).Field);
        }

        [Fact]
        public void An_empty_return_changes_nothing()
        {
            var result = BcfMerge.Merge(new[] { Topic("1") }, Array.Empty<MergeReturn>());

            Assert.Single(result.Topics);
            Assert.Equal(0, result.Changed);
            Assert.True(result.IsClean);
        }

        [Fact]
        public void Nothing_returned_says_so()
        {
            var result = BcfMerge.Merge(null, null);

            Assert.Empty(result.Topics);
            Assert.Contains("Nothing was returned", result.Explain());
        }
    }
}
