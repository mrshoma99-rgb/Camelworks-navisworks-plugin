using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The register every check writes into.
    ///
    /// The behaviours here are the ones the whole architecture rests on: a re-run must not undo a
    /// decision, a finding that stops being reported must not vanish, and a finding must survive
    /// the file with the same id it went in with — because an issue points at that id.
    /// </summary>
    public class FindingRegisterTests
    {
        private static ElementKey Key(string guid) =>
            ElementKey.FromInstanceGuid(ElementKey.ScopeOf("MEP.nwc"), Guid.Parse(guid));

        private static readonly ElementKey A = Key("11111111-1111-1111-1111-111111111111");
        private static readonly ElementKey B = Key("22222222-2222-2222-2222-222222222222");
        private static readonly ElementKey C = Key("33333333-3333-3333-3333-333333333333");

        private static Finding Clash(ElementKey a, ElementKey b, string test = "MEP vs Structure",
                                     string? where = null) =>
            Finding.Create(FindingSource.Clash, test, "Clash", new[] { a, b }, where);

        // -----------------------------------------------------------------------------------

        [Fact]
        public void ARerunKeepsTheDecisionAndTakesTheFacts()
        {
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 100);

            var held = register.All.Single();
            held.Status = FindingStatus.Approved;
            held.AssignedTo = "sam";

            // Next week: the engine reports it again, as New, as engines do.
            var again = Clash(A, B);
            again.Detail = "0.045 m";
            again.X = 12.5;

            var intake = register.Observe(FindingSource.Clash, new[] { again }, 200);

            Assert.Equal(0, intake.Added);
            Assert.Equal(1, intake.Updated);

            var after = register.All.Single();

            // The decision stands. The facts are fresh.
            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Equal("sam", after.AssignedTo);
            Assert.Equal("0.045 m", after.Detail);
            Assert.Equal(12.5, after.X);
            Assert.Equal(200, after.LastSeenTicks);
        }

        [Fact]
        public void AFindingTheRunNoLongerReportsIsKeptAndMarked()
        {
            // It is the record of the thing having been fixed, and an issue may point at it.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Clash(A, B), Clash(A, C) }, 100);

            var intake = register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 200);

            Assert.Equal(1, intake.Absent);
            Assert.Equal(2, register.Count);
            Assert.Single(register.Present);

            var gone = register.All.Single(f => !f.IsPresent);

            Assert.Equal(Clash(A, C).Id, gone.Id);
        }

        [Fact]
        public void AFindingThatComesBackIsPresentAgain()
        {
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 100);
            register.Observe(FindingSource.Clash, Array.Empty<Finding>(), 200);

            Assert.False(register.All.Single().IsPresent);

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 300);

            Assert.True(register.All.Single().IsPresent);
        }

        [Fact]
        public void OneCheckNeverMarksAnotherCheckAbsent()
        {
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 100);
            register.Observe(FindingSource.Ids,
                new[] { Finding.Create(FindingSource.Ids, "Fire rating", "Missing", new[] { C }) }, 100);

            // A clash run says nothing about IDS.
            register.Observe(FindingSource.Clash, Array.Empty<Finding>(), 200);

            Assert.True(register.From(FindingSource.Ids).Single().IsPresent);
            Assert.False(register.From(FindingSource.Clash).Single().IsPresent);
        }

        [Fact]
        public void ARunOverPartOfTheBoardLeavesTheRestAlone()
        {
            // Running three of twelve clash tests must not close the other nine.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash,
                new[] { Clash(A, B, "Test 1"), Clash(A, C, "Test 2") }, 100);

            register.Observe(FindingSource.Clash, Array.Empty<Finding>(), 200,
                             scope: f => f.Rule == "Test 1");

            Assert.False(register.All.Single(f => f.Rule == "Test 1").IsPresent);
            Assert.True(register.All.Single(f => f.Rule == "Test 2").IsPresent);
        }

        [Fact]
        public void AStatusIsNeverDemotedByARerun()
        {
            // The lattice's whole job: an engine that recomputes New every run cannot undo an
            // Approved somebody gave it.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 100);
            register.All.Single().Status = FindingStatus.Resolved;

            register.Observe(FindingSource.Clash, new[] { Clash(A, B) }, 200);

            Assert.Equal(FindingStatus.Resolved, register.All.Single().Status);
        }

        [Fact]
        public void TheSameRemarkIsNotAddedTwice()
        {
            var register = new FindingRegister();

            var first = Clash(A, B);
            first.Comments.Add(new FindingComment("sam", 1, "Please reroute."));

            register.Observe(FindingSource.Clash, new[] { first }, 100);

            var again = Clash(A, B);
            again.Comments.Add(new FindingComment("sam", 1, "Please reroute."));
            again.Comments.Add(new FindingComment("ada", 2, "Rerouted."));

            register.Observe(FindingSource.Clash, new[] { again }, 200);

            Assert.Equal(2, register.All.Single().Comments.Count);
        }

        [Fact]
        public void AFindingFromAnotherSourceIsRefusedAndSaidSo()
        {
            var register = new FindingRegister();

            var intake = register.Observe(FindingSource.Clash,
                new[] { Finding.Create(FindingSource.Ids, "r", "t", new[] { A }) }, 100);

            Assert.Equal(0, intake.Added);
            Assert.True(register.IsEmpty);
            Assert.Contains(intake.Notes, n => n.Contains("Ids"));
        }

        // -----------------------------------------------------------------------------------

        [Fact]
        public void EverythingSurvivesTheFile()
        {
            var register = new FindingRegister();

            var finding = Clash(A, B, "MEP vs Structure", "cell-40-80-120");
            finding.Status = FindingStatus.Reviewed;
            finding.AssignedTo = "sam";
            finding.Priority = "High";
            finding.Stage = "Construction";
            finding.Group = "Level 3 ducts";
            finding.Detail = "0.045 m";
            finding.DueDayNumber = 20_500;
            finding.Origin = "ada.bcfzip";
            finding.LastSeenTicks = 999;
            finding.IsPresent = false;
            finding.HostHasIt = true;
            finding.HostProblem = null;
            finding.X = 1.5; finding.Y = -2.5; finding.Z = 3.25;
            finding.Labels.Add("Site query");
            finding.Comments.Add(new FindingComment("sam", 7, "Please reroute."));
            finding.Facts["Level"] = "L03";
            finding.Facts["Grid"] = "C-4";

            register.Put(finding);

            var back = FindingRegister.FromJson(JsonReader.Parse(register.ToJson().ToJson()));
            var read = back.Find(finding.Id);

            Assert.NotNull(read);
            Assert.Equal(FindingStatus.Reviewed, read!.Status);
            Assert.Equal("sam", read.AssignedTo);
            Assert.Equal("High", read.Priority);
            Assert.Equal("Construction", read.Stage);
            Assert.Equal("Level 3 ducts", read.Group);
            Assert.Equal("0.045 m", read.Detail);
            Assert.Equal(20_500, read.DueDayNumber);
            Assert.Equal("ada.bcfzip", read.Origin);
            Assert.Equal(999, read.LastSeenTicks);
            Assert.False(read.IsPresent);
            Assert.True(read.HostHasIt);
            Assert.Equal(1.5, read.X);
            Assert.Equal(-2.5, read.Y);
            Assert.Equal(3.25, read.Z);
            Assert.Equal(new[] { "Site query" }, read.Labels);
            Assert.Equal("Please reroute.", read.Comments.Single().Body);
            Assert.Equal("L03", read.Facts["Level"]);
            Assert.Equal(2, read.Elements.Count);
        }

        [Fact]
        public void TheIdIsDerivedAgainOnLoadAndStillMatches()
        {
            // An issue points at the id. If loading invented a different one, every link would
            // break silently.
            var register = new FindingRegister();
            var finding = Clash(A, B, "MEP vs Structure", "cell-40-80-120");

            register.Put(finding);

            var back = FindingRegister.FromJson(JsonReader.Parse(register.ToJson().ToJson()));

            Assert.Equal(finding.Id, back.All.Single().Id);
        }

        [Fact]
        public void AParentKeepsItsChildrenAndTheirLocations()
        {
            var parent = Finding.CreateParent(FindingSource.Ids, "Fire rating", "12 walls unrated");

            parent.AddChild(Finding.Create(FindingSource.Ids, "Fire rating", "Wall 1", new[] { A }));
            parent.AddChild(Finding.Create(FindingSource.Ids, "Fire rating", "Wall 2", new[] { B }));

            parent.Status = FindingStatus.Active;

            var register = new FindingRegister();
            register.Put(parent);

            var back = FindingRegister.FromJson(JsonReader.Parse(register.ToJson().ToJson()));
            var read = back.All.Single();

            Assert.Equal(2, read.Children.Count);
            Assert.Equal(FindingStatus.Active, read.Status);
            Assert.All(read.Children, c => Assert.Equal(FindingStatus.New, c.Status));
            Assert.Equal(2, read.AllElements().Count);
        }

        [Fact]
        public void AnUnreadableRecordIsLeftOutAndCounted()
        {
            var json = JsonValue.Object().Set("findings", JsonValue.Array(new[]
            {
                JsonValue.Object().Set("rule", "r"),                       // no title, no source
                JsonValue.Object().Set("source", "Clash").Set("rule", "r").Set("title", "t"),
            }));

            var notes = new List<string>();
            var register = FindingRegister.FromJson(json, notes);

            Assert.Equal(1, register.Count);
            Assert.Contains(notes, n => n.Contains("could not be read"));
        }

        [Fact]
        public void NothingSavedGivesAnEmptyRegister()
        {
            Assert.True(FindingRegister.FromJson(null).IsEmpty);
            Assert.True(FindingRegister.FromJson(JsonValue.Null).IsEmpty);
            Assert.True(FindingRegister.FromJson(JsonValue.Object()).IsEmpty);
        }

        [Fact]
        public void MergingSomebodyElsesRegisterPromotesRatherThanOverwrites()
        {
            var mine = new FindingRegister();
            var ours = Clash(A, B);
            ours.Status = FindingStatus.Approved;
            mine.Put(ours);

            var theirs = new FindingRegister();
            var yours = Clash(A, B);
            yours.Status = FindingStatus.Active;
            yours.Comments.Add(new FindingComment("ada", 5, "Looked at it."));
            theirs.Put(yours);

            mine.Merge(theirs, theirsIsNewer: true);

            var after = mine.All.Single();

            Assert.Equal(FindingStatus.Approved, after.Status);
            Assert.Single(after.Comments);
        }
    }
}
