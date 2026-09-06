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
            // A resolved issue is kept as a record and left off the board. It is the only evidence
            // the week's work happened, and a board carrying every issue ever raised would be
            // unusable within a month.
            var register = new IssueRegister();

            register.Put(Raised());

            var closed = Raised("CW-2", "Closed one");
            closed.Status = FindingStatus.Resolved;
            register.Put(closed);

            Assert.Equal(2, register.Issues.Count);
            Assert.Equal("Door swings into the escape route",
                         Assert.Single(IssueItems.Open(register)).CarriedGroup);
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
