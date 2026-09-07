using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class IssueItemsTests
    {
        private static readonly string Scope = ElementKey.ScopeOf(@"C:\p\ARCH.nwc");

        private static ElementKey K(string name) =>
            ElementKey.FromTreePath(Scope, @"C:\p\ARCH.nwc", name);

        private static Issue Raised(string id = "CW-1", string title = "Door swings into the escape route")
        {
            var issue = new Issue(id, title)
            {
                Description = "It opens across the corridor and blocks half of it.",
                AssignedTo = "Architect",
                Priority = "High",
                Author = "A Coordinator",
                HasPoint = true,
                X = 4,
                Y = 8,
                Z = 3,
            };

            issue.Facts[Issue.LevelFact] = "Level 2";
            issue.Elements.Add(K("door"));
            issue.Elements.Add(K("corridor"));

            return issue;
        }

        [Fact]
        public void A_raised_issue_is_an_ordinary_board_item()
        {
            var register = new IssueRegister();
            register.Put(Raised());

            var item = Assert.Single(IssueItems.Open(register));

            Assert.Equal(IssueItems.RaisedByHand, item.TestName);
            Assert.Equal("Door swings into the escape route", item.CarriedGroup);
            Assert.Equal("Level 2", item.Level);
            Assert.Equal("A Coordinator", item.ModelA);
            Assert.Equal("High", item.CategoryB);
            Assert.Equal(3, item.Z);
        }

        [Fact]
        public void Only_open_issues_reach_the_board()
        {
            // Open is the lattice's, so Approved comes off the board as well as Resolved. Approved
            // means somebody has signed the issue off; the board is what is still to settle. Both
            // are kept as records: they are the evidence the week's work happened, and a board
            // carrying every issue ever raised would be unusable within a month.
            var register = new IssueRegister();

            register.Put(Raised());

            var closed = Raised("CW-2", "Closed one");
            closed.Status = FindingStatus.Resolved;
            register.Put(closed);

            var signedOff = Raised("CW-3", "Approved one");
            signedOff.Status = FindingStatus.Approved;
            register.Put(signedOff);

            var reviewed = Raised("CW-4", "Reviewed one");
            reviewed.Status = FindingStatus.Reviewed;
            register.Put(reviewed);

            Assert.Equal(4, register.Issues.Count);

            Assert.Equal(new[] { "Door swings into the escape route", "Reviewed one" },
                         IssueItems.Open(register).Select(i => i.CarriedGroup).ToArray());
        }

        [Fact]
        public void A_board_identity_is_kept_rather_than_recomputed()
        {
            // A clash key is quantised around a point, and the centroid of a selection moves the
            // moment somebody nudges an element. Re-deriving it on every board build would make the
            // issue look resolved and then new, and would orphan the group decision, the pinned
            // group and the per-key assignment held against the old key.
            var issue = Raised();

            var before = IssueItems.KeyFor(issue);

            Assert.False(before.IsEmpty);
            Assert.Equal(before.ToString(), issue.Facts[Issue.KeyFact]);

            issue.X = 999;
            issue.Y = -40;
            issue.Elements.Insert(0, K("something else"));

            Assert.Equal(before, IssueItems.KeyFor(issue));
            Assert.Equal(before, IssueItems.KeyFor(Issue.FromJson(issue.ToJson())));

            var register = new IssueRegister();
            register.Put(issue);

            Assert.Equal(before, Assert.Single(IssueItems.Open(register)).Key);
        }

        [Fact]
        public void An_issue_with_no_honest_key_has_none()
        {
            var thin = new Issue("CW-5", "No access to this valve");
            thin.Elements.Add(K("valve"));

            Assert.True(IssueItems.KeyFor(thin).IsEmpty);
            Assert.False(thin.Facts.ContainsKey(Issue.KeyFact));
            Assert.True(IssueItems.KeyFor(null).IsEmpty);
        }

        [Fact]
        public void Only_hand_raised_issues_reach_the_board()
        {
            // An issue made from a clash is already on the board as the clash it covers. Projecting
            // it a second time would show one conflict twice and count it twice.
            var register = new IssueRegister();

            var fromClash = Raised("CW-3", "From a clash");
            fromClash.Provenance = FindingSource.Clash;
            register.Put(fromClash);

            Assert.Empty(IssueItems.Open(register));
        }

        [Fact]
        public void An_issue_naming_fewer_than_two_elements_is_not_a_board_item()
        {
            // A board item is identified by a clash key, which names two participants. There is no
            // honest key for a sentence about one element.
            var register = new IssueRegister();

            var thin = new Issue("CW-4", "No access to this valve");
            thin.Elements.Add(K("valve"));
            register.Put(thin);

            Assert.Empty(IssueItems.Open(register));
        }

        [Fact]
        public void An_instruction_survives_the_project_file()
        {
            var issue = Raised();
            var said = new Resolution(ResolutionAction.Move, "Duct", ResolutionAxis.ZPlus, 0.15);

            issue.Facts[Issue.InstructionFact] = said.ToJson().ToString();

            var read = Issue.FromJson(issue.ToJson());

            Assert.NotNull(read);
            Assert.Equal(ResolutionAction.Move, IssueItems.Instruction(read).Action);
            Assert.Equal(ResolutionAction.NotStated, IssueItems.Instruction(Raised("CW-9")).Action);
        }
    }
}
