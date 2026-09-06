using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Store;
using CamelWorks.Core.Identity;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Clash results becoming findings — the first producer to move.
    ///
    /// The two that matter most: two crossings of one pair stay two findings, and a re-run cannot
    /// undo a decision by reporting the clash as New again.
    /// </summary>
    public class ClashFindingsTests
    {
        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("MEP.nwc"), Guid.Parse(guid));

        private static readonly ElementKey Duct = Key("11111111-1111-1111-1111-111111111111");
        private static readonly ElementKey Beam = Key("22222222-2222-2222-2222-222222222222");

        private static ClashResultInfo Result(string id, double x, double y, double z,
                                              string? status = null) =>
            new ClashResultInfo(id, Duct, Beam, x, y, z, status, null)
            {
                Name = "Clash" + id,
                NameA = "Duct 300x200",
                NameB = "Beam UB457",
                CategoryA = "Ducts",
                CategoryB = "Structural Framing",
                Distance = -0.045,
            };

        [Fact]
        public void AClashCarriesWhatTheRowShows()
        {
            var finding = ClashFindings.From("MEP vs Structure", Result("7", 12.5, 40.25, 7.8));

            Assert.Equal(FindingSource.Clash, finding.Source);
            Assert.Equal("MEP vs Structure", finding.Rule);
            Assert.Equal("Clash7", finding.Title);
            Assert.Equal(12.5, finding.X);
            Assert.Equal(40.25, finding.Y);
            Assert.Equal(7.8, finding.Z);
            Assert.Equal(2, finding.Elements.Count);
            Assert.Equal("Duct 300x200 against Beam UB457, -0.045 m", finding.Detail);
            Assert.Equal("Ducts", finding.Facts["Category 1"]);
            Assert.Equal("7", finding.Facts[ClashFindings.HostId]);
        }

        [Fact]
        public void TwoCrossingsOfOnePairAreTwoFindings()
        {
            // The same mistake that put two BCF topics at one path. A duct that crosses one beam
            // twice is two problems, and coarsely decomposed models make that ordinary.
            var first = ClashFindings.From("T", Result("1", 10, 20, 30));
            var second = ClashFindings.From("T", Result("2", 90, 20, 30));

            Assert.NotEqual(first.Id, second.Id);
        }

        [Fact]
        public void TheSameClashInTwoRunsIsOneFinding()
        {
            // Even though the engine reissues its own result ids every run.
            var monday = ClashFindings.From("T", Result("1", 10, 20, 30));
            var friday = ClashFindings.From("T", Result("998", 10, 20, 30));

            Assert.Equal(monday.Id, friday.Id);
        }

        [Fact]
        public void AClashThatShiftedInsideItsCellIsStillItself()
        {
            var before = ClashFindings.From("T", Result("1", 10.00, 20, 30));
            var after = ClashFindings.From("T", Result("1", 10.05, 20, 30));

            Assert.Equal(before.Id, after.Id);
        }

        [Fact]
        public void TheSameClashInTwoTestsIsTwoFindings()
        {
            // Two tests reporting one conflict are two things to decide about, and closing it in
            // one must not close it in the other.
            Assert.NotEqual(ClashFindings.From("MEP vs Structure", Result("1", 10, 20, 30)).Id,
                            ClashFindings.From("Ducts vs Beams", Result("1", 10, 20, 30)).Id);
        }

        [Theory]
        [InlineData("Reviewed", FindingStatus.Reviewed)]
        [InlineData("Approved", FindingStatus.Approved)]
        public void AStatusOnlyAHumanSetsIsTakenFromTheHost(string host, FindingStatus expected)
        {
            Assert.Equal(expected, ClashFindings.From("T", Result("1", 1, 2, 3, host)).Status);
        }

        [Theory]
        [InlineData("New")]
        [InlineData("Active")]
        [InlineData("Resolved")]
        public void AStatusTheEngineRecomputesIsNotTakenAsADecision(string host)
        {
            // The engine invents these on every run. Reading them back as truth would overwrite
            // somebody's judgement with a machine's guess.
            Assert.Equal(FindingStatus.New, ClashFindings.From("T", Result("1", 1, 2, 3, host)).Status);
        }

        [Fact]
        public void ARerunDoesNotUndoADecision()
        {
            // The whole point of the register, exercised through the real producer.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash,
                ClashFindings.ForTest("t1", "MEP vs Structure", new[] { Result("1", 10, 20, 30) }), 100);

            register.All.Single().Status = FindingStatus.Approved;
            register.All.Single().AssignedTo = "sam";

            // Friday: same clash, new host id, engine says Active.
            register.Observe(FindingSource.Clash,
                ClashFindings.ForTest("t1", "MEP vs Structure",
                    new[] { Result("312", 10, 20, 30, "Active") }), 200);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("sam", after.AssignedTo);
            Assert.Equal("312", after.Facts[ClashFindings.HostId]);
        }

        [Fact]
        public void RenamingATestKeepsEverythingDecidedUnderIt()
        {
            // Renaming a clash test is a first-class feature of this product. Identity made from
            // the name meant a rename gave every finding under it a new id and silently detached
            // its status, its assignee, its due date and its whole comment trail.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash,
                ClashFindings.ForTest("t1", "L03 MEP vs Structure",
                                      new[] { Result("1", 10, 20, 30) }), 100);

            register.All.Single().Status = FindingStatus.Approved;
            register.All.Single().AssignedTo = "sam";

            // The same test, renamed, read again.
            register.Observe(FindingSource.Clash,
                ClashFindings.ForTest("t1", "L03 Mechanical vs Structure",
                                      new[] { Result("1", 10, 20, 30, "Active") }), 200,
                scope: ClashFindings.Scope(new[] { "t1" }));

            var after = Assert.Single(register.All);

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("sam", after.AssignedTo);

            // The name is what a person reads, so it follows the rename.
            Assert.Equal("L03 Mechanical vs Structure", after.Rule);
        }

        [Fact]
        public void TwoTestsWithTheSameNameAreNotTheSameFinding()
        {
            // The other half of the same change. Two tests can carry one name - a job inherited
            // with four "Coordination check" tests is ordinary - and keying on the name folded
            // their results into one another.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, ClashFindings.ForTest(
                "t1", "Coordination check", new[] { Result("1", 10, 20, 30) }), 100);

            register.Observe(FindingSource.Clash, ClashFindings.ForTest(
                "t2", "Coordination check", new[] { Result("2", 10, 20, 30) }), 100,
                scope: ClashFindings.Scope(new[] { "t2" }));

            Assert.Equal(2, register.All.Count);
        }

        [Fact]
        public void ARegisterWrittenBeforeTheMoveComesBackOnTheNewIdentity()
        {
            // Read again on load rather than migrated by a pass of its own: the id is derived on
            // every read anyway, so a file written under the old scheme comes back under the new
            // one carrying everything it held.
            var found = ClashFindings.ForTest("t1", "L03 MEP vs Structure",
                                              new[] { Result("1", 10, 20, 30) }).Single();

            found.Status = FindingStatus.Approved;

            var json = FindingJson.Write(found);

            // What the old writer put there: an id derived from the test's name.
            json.Set("id", Finding.Create(FindingSource.Clash, "L03 MEP vs Structure",
                                          found.Title, found.Elements, found.Discriminator).Id);

            var read = FindingJson.Read(JsonReader.Parse(json.ToString()), out var renamed);

            Assert.NotNull(read);
            Assert.True(renamed);
            Assert.Equal(found.Id, read!.Id);
            Assert.Equal(FindingStatus.Approved, read.Status);
        }

        [Fact]
        public void TheHostIdsRideAlongSoTheProjectionCanWriteBack()
        {
            var found = ClashFindings.ForTest("test-42", "MEP vs Structure",
                new[] { Result("7", 1, 2, 3) }).Single();

            Assert.Equal("test-42", found.Facts[ClashFindings.HostTest]);
            Assert.Equal("7", found.Facts[ClashFindings.HostId]);
        }

        [Fact]
        public void TheCommentTrailComesAcross()
        {
            var result = Result("1", 1, 2, 3);
            result.Comments = new[]
            {
                new ClashComment("sam", 100, "Please reroute."),
                new ClashComment("ada", 200, "Rerouted."),
            };

            var finding = ClashFindings.From("T", result);

            Assert.Equal(2, finding.Comments.Count);
            Assert.Equal("Please reroute.", finding.Comments[0].Body);
        }

        [Fact]
        public void AResultWithNoElementsStillBecomesAFindingRatherThanVanishing()
        {
            var orphan = new ClashResultInfo("9", default, default, 0, 0, 0, null, null)
            {
                Name = "Clash9",
            };

            var finding = ClashFindings.From("T", orphan);

            Assert.Empty(finding.Elements);
            Assert.Equal("Clash9", finding.Title);
        }

        [Fact]
        public void ItCarriesTheTopicItWillExportAs()
        {
            // How a file coming back from somebody else's tool finds its way home: the markup
            // carries the topic, not the elements this model knows.
            var result = Result("7", 12.5, 40.25, 7.8);
            var finding = ClashFindings.From("MEP vs Structure", result);

            Assert.Equal(ClashBcf.TopicGuidFor(result.Key()), finding.TopicGuid);
        }

        [Fact]
        public void WhatWasReadOutOfTheDocumentIsNotWaitingToGoBackIn()
        {
            // Otherwise the first sync after a read would write every comment in the model back
            // into the model, and every conversation would double.
            var result = Result("7", 12.5, 40.25, 7.8);

            result.Comments = new[] { new ClashComment("ada", 1, "The beam cannot move.") };

            var finding = ClashFindings.From("MEP vs Structure", result);

            Assert.True(finding.HostHasIt);
            Assert.False(finding.IsHeld);
            Assert.True(finding.Comments.Single().InHost);
        }
    }
}
