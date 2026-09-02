using System;
using System.Linq;
using CamelWorks.Core.People;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// A party has to survive the round trip through the host's one assignee string, and the
    /// roster has to answer "is this row architecture's?" the same way every screen asks it.
    /// </summary>
    public class RosterTests
    {
        [Fact]
        public void APersonIsWrittenBare()
        {
            // So every assignment made before parties existed still reads as that person, and the
            // native clash window shows a name rather than a token.
            Assert.Equal("Sam Reed", Responsible.Person("Sam Reed").Written);
        }

        [Fact]
        public void AGroupingCarriesItsKindInFront()
        {
            Assert.Equal("role:BIM Coordinator",
                         new Responsible(ResponsibleKind.Role, "BIM Coordinator").Written);
            Assert.Equal("team:ARC", new Responsible(ResponsibleKind.Team, "ARC").Written);
            Assert.Equal("company:Acme", new Responsible(ResponsibleKind.Company, "Acme").Written);
        }

        [Theory]
        [InlineData(ResponsibleKind.Person, "Sam Reed")]
        [InlineData(ResponsibleKind.Role, "Fire engineer")]
        [InlineData(ResponsibleKind.Company, "Acme Partners")]
        [InlineData(ResponsibleKind.Team, "MEP")]
        public void EveryKindReadsBackAsItself(ResponsibleKind kind, string name)
        {
            var party = new Responsible(kind, name);

            Assert.Equal(party, Responsible.Read(party.Written));
        }

        [Fact]
        public void ANameWithAColonInItIsStillAPerson()
        {
            // Only the three prefixes mark a grouping, matched whole and at the front. Anything
            // else is somebody's name, however it is punctuated.
            var read = Responsible.Read("Reed: Sam");

            Assert.Equal(ResponsibleKind.Person, read.Kind);
            Assert.Equal("Reed: Sam", read.Name);
        }

        [Fact]
        public void NothingReadsBackAsNobody()
        {
            Assert.True(Responsible.Read(null).IsEmpty);
            Assert.True(Responsible.Read("   ").IsEmpty);
        }

        [Fact]
        public void RolesCompaniesAndTeamsComeFromThePeople()
        {
            var roster = Filled();

            Assert.Equal(new[] { "Architect", "BIM Coordinator" },
                         roster.Roles);
            Assert.Equal(new[] { "Acme", "Borden" }, roster.Parties);
            Assert.Equal(new[] { "ARC", "STR" }, roster.Teams);
        }

        [Fact]
        public void TakingTheLastArchitectOffTakesTheRoleWithThem()
        {
            var roster = Filled();

            roster.Remove("ada blake");

            Assert.DoesNotContain("Architect", roster.Roles);
        }

        [Fact]
        public void APersonFallsUnderTheirRoleCompanyAndTeam()
        {
            var roster = Filled();
            var ada = Responsible.Person("Ada Blake");

            Assert.True(roster.Covers(ada, new Responsible(ResponsibleKind.Role, "Architect")));
            Assert.True(roster.Covers(ada, new Responsible(ResponsibleKind.Company, "Acme")));
            Assert.True(roster.Covers(ada, new Responsible(ResponsibleKind.Team, "ARC")));
        }

        [Fact]
        public void APersonDoesNotFallUnderSomebodyElsesTeam()
        {
            var roster = Filled();

            Assert.False(roster.Covers(Responsible.Person("Ada Blake"),
                                       new Responsible(ResponsibleKind.Team, "STR")));
        }

        [Fact]
        public void WorkGivenToATeamIsNotAnyOneMembersWork()
        {
            // Otherwise "show me Ada's clashes" would hand her everything architecture was ever
            // given, which is the opposite of what somebody filtering for their own work wants.
            var roster = Filled();

            Assert.False(roster.Covers(new Responsible(ResponsibleKind.Team, "ARC"),
                                       Responsible.Person("Ada Blake")));
        }

        [Fact]
        public void OneGroupingNeverFallsUnderAnother()
        {
            var roster = Filled();

            Assert.False(roster.Covers(new Responsible(ResponsibleKind.Team, "ARC"),
                                       new Responsible(ResponsibleKind.Company, "Acme")));
        }

        [Fact]
        public void NobodyIsAskedForAndEverythingAnswers()
        {
            var roster = Filled();

            Assert.True(roster.Covers(Responsible.Person("Ada Blake"), default));
            Assert.False(roster.Covers(default, new Responsible(ResponsibleKind.Team, "ARC")));
        }

        [Fact]
        public void SomebodyNotOnTheRosterFallsUnderNothingButThemselves()
        {
            var roster = Filled();
            var stranger = Responsible.Person("Unlisted Person");

            Assert.True(roster.Covers(stranger, stranger));
            Assert.False(roster.Covers(stranger, new Responsible(ResponsibleKind.Team, "ARC")));
        }

        [Fact]
        public void TheRosterSurvivesBeingWrittenAndReadBack()
        {
            var roster = Filled();
            var back = TeamRoster.FromJson(roster.ToJson());

            Assert.Equal(roster.Members.Count, back.Members.Count);

            var ada = back.Find("Ada Blake");

            Assert.NotNull(ada);
            Assert.Equal("Architect", ada!.Role);
            Assert.Equal("Acme", ada.Party);
            Assert.Equal("ARC", ada.Team);
        }

        [Fact]
        public void PuttingTheSameNameTwiceUpdatesRatherThanDuplicates()
        {
            var roster = Filled();

            roster.Add("Ada Blake").Role = "Lead architect";

            Assert.Equal(3, roster.Members.Count);
            Assert.Equal("Lead architect", roster.Find("Ada Blake")!.Role);
        }

        [Fact]
        public void SomebodyWithNoNameIsNotSomebody()
        {
            var roster = new TeamRoster();

            Assert.Throws<ArgumentException>(() => roster.Add("   "));
            Assert.Empty(roster.Members);
        }

        [Fact]
        public void EverythingAssignableIsOfferedPeopleFirst()
        {
            var parties = Filled().Responsibles().ToList();

            Assert.Equal(3, parties.Count(p => p.Kind == ResponsibleKind.Person));
            Assert.Contains(new Responsible(ResponsibleKind.Team, "STR"), parties);
            Assert.All(parties.Take(3), p => Assert.Equal(ResponsibleKind.Person, p.Kind));
        }

        private static TeamRoster Filled()
        {
            var roster = new TeamRoster();

            var ada = roster.Add("Ada Blake", "Acme");
            ada.Role = "Architect";
            ada.Team = "ARC";

            var cy = roster.Add("Cy Okafor", "Acme");
            cy.Role = "BIM Coordinator";
            cy.Team = "ARC";

            var mo = roster.Add("Mo Farrell", "Borden");
            mo.Role = "BIM Coordinator";
            mo.Team = "STR";

            return roster;
        }
    }
}
