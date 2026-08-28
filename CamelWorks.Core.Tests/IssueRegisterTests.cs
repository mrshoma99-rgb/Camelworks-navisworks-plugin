using System;
using System.Linq;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class IssueRegisterTests
    {
        private static Issue Raised(string id, string title = "A door swings into the corridor") =>
            new Issue(id, title)
            {
                Description = "The riser door hits the handrail.",
                AssignedTo = "Mechanical",
                Author = "coordinator",
                Priority = "High",
                Level = "Level 03",
                CreatedTicks = 638_000_000_000_000_000L,
                HasPoint = true,
                X = 1, Y = 2, Z = 3,
            };

        [Fact]
        public void An_issue_survives_a_round_trip_through_the_project_file()
        {
            var issue = Raised("CW-1");
            issue.Say("coordinator", "Raised on site walk.", 638_000_000_000_000_001L);
            issue.Elements.Add(ClashFixture.Mep("Duct"));

            var back = Issue.FromJson(JsonReader.Parse(issue.ToJson().ToString()));

            Assert.NotNull(back);
            Assert.Equal("CW-1", back!.Id);
            Assert.Equal(issue.Title, back.Title);
            Assert.Equal("Mechanical", back.AssignedTo);
            Assert.Equal("High", back.Priority);
            Assert.True(back.HasPoint);
            Assert.Equal(3, back.Z);
            Assert.Equal("Raised on site walk.", back.LastComment!.Body);
            Assert.Single(back.Elements);
        }

        [Fact]
        public void The_origin_of_an_issue_survives_the_round_trip()
        {
            // The one field nobody may edit is worthless if it does not come back.
            var issue = new Issue("CW-9", "From the clearance check", IssueOrigin.Clearance);

            var back = Issue.FromJson(JsonReader.Parse(issue.ToJson().ToString()));

            Assert.Equal(IssueOrigin.Clearance, back!.Origin);
        }

        [Fact]
        public void An_unreadable_record_is_counted_rather_than_thrown()
        {
            var json = JsonValue.Array(new[]
            {
                Raised("CW-1").ToJson(),
                JsonValue.Object().Set("title", "no id at all"),
            });

            var register = IssueRegister.FromJson(json);

            Assert.Single(register.Issues);
            Assert.Equal(1, register.Unreadable);
        }

        [Fact]
        public void Ids_do_not_collide_with_one_that_is_already_taken()
        {
            var register = new IssueRegister();
            register.Put(Raised("CW-1"));
            register.Put(Raised("CW-2"));

            Assert.Equal("CW-3", register.NextId());

            register.Put(Raised("CW-3"));
            register.Remove("CW-1");

            // Two issues left, so the count says CW-3 - which is taken. It has to keep looking.
            Assert.Equal("CW-4", register.NextId());
        }

        [Fact]
        public void One_clash_belongs_to_at_most_the_issue_that_holds_it()
        {
            var key = ClashKey.Create(ClashFixture.Mep("Duct"), ClashFixture.Arch("Beam"), 1, 1, 1);

            var register = new IssueRegister();
            var issue = Raised("CW-1");
            issue.Clashes.Add(key);
            register.Put(issue);

            Assert.Same(issue, register.Holding(key));
            Assert.Null(register.Holding(ClashKey.Create(ClashFixture.Mep("Pipe"),
                                                         ClashFixture.Arch("Wall"), 9, 9, 9)));
        }

        [Fact]
        public void Open_and_overdue_are_counted_the_way_the_list_reads_them()
        {
            var now = 638_100_000_000_000_000L;

            var register = new IssueRegister();

            var open = Raised("CW-1");
            open.DueTicks = now - 1;

            var closed = Raised("CW-2");
            closed.Status = Issue.ClosedStatus;
            closed.DueTicks = now - 1;

            register.Put(open);
            register.Put(closed);

            Assert.Equal(1, register.Open);
            Assert.Equal(1, register.Of(Issue.ClosedStatus));

            // A closed issue past its date is not overdue. It is done.
            Assert.Equal(1, register.Overdue(now));
        }
    }

    public class IssueBcfTests
    {
        private static BcfTopic Topic(string guid, string title, string status = "Open")
        {
            var topic = new BcfTopic(guid, title, new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero),
                                     "reviewer")
            {
                TopicStatus = status,
                TopicType = "Clash",
                Description = "Sent back by the client.",
                AssignedTo = "Structural",
            };

            topic.Comments.Add(new BcfComment(BcfGuid.For(guid + "/1"),
                new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero), "reviewer", "Please move it."));

            return topic;
        }

        [Fact]
        public void An_imported_topic_becomes_an_issue_that_says_where_it_came_from()
        {
            var register = new IssueRegister();

            var result = IssueBcf.Merge(register, new[] { Topic(BcfGuid.For("t1"), "Duct through beam") },
                                        "client.bcf");

            var issue = Assert.Single(register.Issues);

            Assert.Equal(1, result.Added);
            Assert.Equal(IssueOrigin.Imported, issue.Origin);
            Assert.Equal("client.bcf", issue.Source);
            Assert.Equal("Duct through beam", issue.Title);
            Assert.Equal("Structural", issue.AssignedTo);
            Assert.Single(issue.Comments);
        }

        [Fact]
        public void Reading_the_same_file_twice_updates_rather_than_duplicates()
        {
            // The fortnight-long exchange: every file they send carries the whole conversation.
            var register = new IssueRegister();
            var topic = Topic(BcfGuid.For("t1"), "Duct through beam");

            IssueBcf.Merge(register, new[] { topic }, "first.bcf");

            topic.TopicStatus = "Closed";
            topic.Comments.Add(new BcfComment(BcfGuid.For("t1/2"),
                new DateTimeOffset(2026, 3, 8, 9, 0, 0, TimeSpan.Zero), "reviewer", "Moved it."));

            var second = IssueBcf.Merge(register, new[] { topic }, "second.bcf");

            var issue = Assert.Single(register.Issues);

            Assert.Equal(0, second.Added);
            Assert.Equal(1, second.Updated);
            Assert.Equal(1, second.Comments);
            Assert.Equal("Closed", issue.Status);
            Assert.Equal(2, issue.Comments.Count);
        }

        [Fact]
        public void A_status_another_tool_spells_differently_still_reads_as_closed()
        {
            var register = new IssueRegister();

            IssueBcf.Merge(register, new[] { Topic(BcfGuid.For("t2"), "Answered elsewhere", "Resolved") });

            Assert.Equal(Issue.ClosedStatus, register.Issues[0].Status);
        }

        [Fact]
        public void A_status_nobody_here_has_a_word_for_is_kept_as_it_was_written()
        {
            var register = new IssueRegister();

            IssueBcf.Merge(register, new[] { Topic(BcfGuid.For("t3"), "Odd one", "Awaiting client") });

            Assert.Equal("Awaiting client", register.Issues[0].Status);
        }

        [Fact]
        public void An_exported_topic_carries_the_register_number_so_a_reply_can_be_matched()
        {
            var issue = new Issue("CW-7", "Riser has no opening")
            {
                Author = "coordinator",
                CreatedTicks = 638_000_000_000_000_000L,
                DueTicks = 638_100_000_000_000_000L,
                Status = "Open",
                Type = "Coordination",
            };

            issue.Say("coordinator", "Needs a builder's work opening.", 638_000_000_000_000_001L);

            var topic = IssueBcf.ToTopic(issue);

            Assert.Contains("CW-7", topic.Labels);
            Assert.Equal("Riser has no opening", topic.Title);
            Assert.Equal("Open", topic.TopicStatus);
            Assert.NotNull(topic.DueDate);
            Assert.Single(topic.Comments);
        }

        [Fact]
        public void An_issue_out_and_back_again_is_the_same_issue()
        {
            var register = new IssueRegister();

            var issue = new Issue("CW-1", "Valve cannot be pulled")
            {
                Author = "coordinator",
                CreatedTicks = 638_000_000_000_000_000L,
            };

            var topic = IssueBcf.ToTopic(issue);
            issue.BcfGuid = topic.Guid;
            register.Put(issue);

            var back = IssueBcf.Merge(register, new[] { topic }, "returned.bcf");

            Assert.Equal(0, back.Added);
            Assert.Single(register.Issues);
        }
    }
}
