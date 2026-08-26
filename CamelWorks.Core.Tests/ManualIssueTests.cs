using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ManualIssueTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");

        private static ElementKey K(string name) =>
            ElementKey.FromTreePath(Scope, @"C:\p\ARCH.nwc", name);

        private static ClashKey Key(string a = "door", string b = "corridor") =>
            ClashKey.Create(K(a), K(b), 4, 8, 3);

        private static ManualIssue Raised(string id = "issue-1") =>
            new ManualIssue(id, Key(), "Door swings into the escape route")
            {
                Note = "It opens across the corridor and blocks half of it.",
                AssignedTo = "Architect",
                Priority = "High",
                Level = "Level 2",
                Author = "A Coordinator",
                RaisedTicks = 638_000_000_000_000_000,
                X = 4,
                Y = 8,
                Z = 3,
            };

        [Fact]
        public void An_issue_reads_back_exactly_as_it_was_written()
        {
            var issue = Raised();
            issue.Elements.Add(K("door"));
            issue.Elements.Add(K("corridor"));

            var back = ManualIssue.FromJson(issue.ToJson());

            Assert.NotNull(back);
            Assert.Equal(issue.Id, back!.Id);
            Assert.Equal(issue.Key, back.Key);
            Assert.Equal(issue.Title, back.Title);
            Assert.Equal(issue.Note, back.Note);
            Assert.Equal(issue.AssignedTo, back.AssignedTo);
            Assert.Equal(issue.Priority, back.Priority);
            Assert.Equal(issue.Level, back.Level);
            Assert.Equal(issue.Author, back.Author);
            Assert.Equal(issue.RaisedTicks, back.RaisedTicks);
            Assert.Equal(issue.X, back.X);
            Assert.Equal(2, back.Elements.Count);
        }

        [Fact]
        public void A_raised_issue_is_an_ordinary_board_item()
        {
            // The whole design: it groups, assigns, reports and exports exactly like a clash,
            // because downstream there is no difference.
            var item = Raised().ToItem();

            Assert.Equal(ManualIssue.RaisedByHand, item.TestName);
            Assert.Equal("Door swings into the escape route", item.CarriedGroup);
            Assert.Equal("Level 2", item.Level);
            Assert.Equal(4, item.X);
            Assert.Equal(FindingStatus.New, item.Status);
        }

        [Fact]
        public void Its_identity_is_kept_rather_than_recomputed()
        {
            // A key is quantised around a point, and the centroid of a selection moves the moment
            // somebody nudges an element. Recomputing it would make the issue look resolved and
            // then new, every week, forever.
            var issue = Raised();
            var before = issue.Key;

            issue.X = 999;
            issue.Y = -40;

            Assert.Equal(before, issue.Key);
            Assert.Equal(before, ManualIssue.FromJson(issue.ToJson())!.Key);
        }

        [Fact]
        public void A_record_with_no_usable_key_is_not_half_read()
        {
            var json = JsonValue.Object()
                .Set("id", JsonValue.String("issue-1"))
                .Set("key", JsonValue.String("not a key"))
                .Set("title", JsonValue.String("Something"));

            Assert.Null(ManualIssue.FromJson(json));
        }

        [Fact]
        public void A_list_round_trips_and_counts_what_it_could_not_read()
        {
            var list = new ManualIssueList();
            list.Put(Raised("issue-1"));
            list.Put(Raised("issue-2"));

            var json = list.ToJson();

            var broken = JsonValue.Array(new[]
            {
                list.Issues[0].ToJson(),
                JsonValue.Object().Set("id", JsonValue.String("bad")),
            });

            Assert.Equal(2, ManualIssueList.FromJson(json).Issues.Count);

            var read = ManualIssueList.FromJson(broken);

            Assert.Single(read.Issues);
            Assert.Equal(1, read.Unreadable);
        }

        [Fact]
        public void Putting_the_same_id_twice_replaces_rather_than_duplicates()
        {
            var list = new ManualIssueList();

            list.Put(Raised("issue-1"));
            list.Put(new ManualIssue("issue-1", Key(), "Renamed"));

            Assert.Equal("Renamed", Assert.Single(list.Issues).Title);
        }

        [Fact]
        public void Ids_do_not_collide()
        {
            var list = new ManualIssueList();

            var first = list.NextId();
            list.Put(new ManualIssue(first, Key(), "One"));

            var second = list.NextId();

            Assert.NotEqual(first, second);
            Assert.Null(list.Find(second));
        }

        [Fact]
        public void Only_open_issues_reach_the_board()
        {
            // A resolved issue is kept as a record and left off the board. It is the only evidence
            // the week's work happened, and a board carrying every issue ever raised would be
            // unusable within a month.
            var list = new ManualIssueList();

            list.Put(Raised("issue-1"));
            list.Put(new ManualIssue("issue-2", Key("valve", "beam"), "Closed one")
            {
                Status = FindingStatus.Resolved,
            });

            Assert.Equal(2, list.Issues.Count);
            Assert.Equal("Door swings into the escape route", Assert.Single(list.Open()).CarriedGroup);
        }

        [Fact]
        public void Removing_something_that_is_not_there_is_not_an_error()
        {
            var list = new ManualIssueList();
            list.Put(Raised("issue-1"));

            list.Remove("issue-9");
            list.Remove(null);

            Assert.Single(list.Issues);

            list.Remove("issue-1");
            Assert.Empty(list.Issues);
        }

        [Fact]
        public void An_issue_with_no_title_still_reads_as_something()
        {
            Assert.Equal("Untitled issue", new ManualIssue("issue-1", Key(), "   ").Title);
        }

        // ---------------------------------------------------------------------------------
        // Clashes attached to an issue
        // ---------------------------------------------------------------------------------

        [Fact]
        public void An_issue_holds_the_clashes_it_was_given()
        {
            // The shape the product is for: eleven conflicts that are one conversation. Without
            // this, the coordinator sends eleven items with the same sentence retyped on each.
            var list = new ManualIssueList();
            var issue = Raised();

            list.Put(issue);

            var attached = list.Attach(issue, new[] { Key("pipe", "beam"), Key("duct", "beam") });

            Assert.Equal(2, attached);
            Assert.Equal(2, issue.Attached.Count);
        }

        [Fact]
        public void Attaching_the_same_clash_twice_does_not_hold_it_twice()
        {
            var list = new ManualIssueList();
            var issue = Raised();
            var clash = Key("pipe", "beam");

            list.Put(issue);

            Assert.Equal(1, list.Attach(issue, new[] { clash }));
            Assert.Equal(0, list.Attach(issue, new[] { clash }));
            Assert.Single(issue.Attached);
        }

        [Fact]
        public void A_clash_belongs_to_one_issue_at_a_time()
        {
            // Two issues both claiming one conflict is two people each believing the other is
            // dealing with it, and neither count can be added to the other.
            var list = new ManualIssueList();
            var first = Raised("issue-1");
            var second = Raised("issue-2");
            var clash = Key("pipe", "beam");

            list.Put(first);
            list.Put(second);

            list.Attach(first, new[] { clash });
            list.Attach(second, new[] { clash });

            Assert.Empty(first.Attached);
            Assert.Single(second.Attached);
            Assert.Same(second, list.Holding(clash));
        }

        [Fact]
        public void Nothing_holds_a_clash_that_was_never_attached()
        {
            var list = new ManualIssueList();
            list.Put(Raised());

            Assert.Null(list.Holding(Key("pipe", "beam")));
            Assert.Null(list.Holding(default));
        }

        [Fact]
        public void Detaching_takes_it_off_whichever_issue_had_it()
        {
            var list = new ManualIssueList();
            var issue = Raised();
            var clash = Key("pipe", "beam");

            list.Put(issue);
            list.Attach(issue, new[] { clash });

            Assert.Equal(1, list.Detach(new[] { clash }));
            Assert.Empty(issue.Attached);
            Assert.Equal(0, list.Detach(new[] { clash }));
        }

        [Fact]
        public void An_empty_key_is_not_something_to_attach()
        {
            var list = new ManualIssueList();
            var issue = Raised();

            list.Put(issue);

            Assert.Equal(0, list.Attach(issue, new[] { default(ClashKey) }));
            Assert.Empty(issue.Attached);
        }

        [Fact]
        public void What_is_attached_survives_a_round_trip_through_the_project_file()
        {
            // The issue outlives the run that produced the results, which is the whole reason the
            // attachment is held as keys.
            var list = new ManualIssueList();
            var issue = Raised();

            list.Put(issue);
            list.Attach(issue, new[] { Key("pipe", "beam"), Key("duct", "beam") });

            var back = ManualIssueList.FromJson(list.ToJson());
            var read = back.Find("issue-1");

            Assert.NotNull(read);
            Assert.Equal(2, read!.Attached.Count);
            Assert.Same(read, back.Holding(Key("pipe", "beam")));
        }

        [Fact]
        public void An_unreadable_attachment_does_not_cost_the_sentence_somebody_wrote()
        {
            var json = Raised().ToJson();
            json.Set("attached", JsonValue.Array(new[] { JsonValue.String("not a key") }));

            var read = ManualIssue.FromJson(json);

            Assert.NotNull(read);
            Assert.Equal("Door swings into the escape route", read!.Title);
            Assert.Empty(read.Attached);
        }

    }
}
