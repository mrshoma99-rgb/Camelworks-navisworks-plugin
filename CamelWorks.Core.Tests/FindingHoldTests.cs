using System;
using System.Linq;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The half of a finding that used to be two side registers: what the host has not taken.
    ///
    /// The behaviour that matters is not that a field can be flagged - it is that a flagged field
    /// survives the next read of the model. A published NWD keeps reporting the status it had
    /// before anybody touched it, so a run that was allowed to win would revert the afternoon's
    /// work on every press of "Read the results", which is the exact failure the whole hold
    /// exists to prevent.
    /// </summary>
    public class FindingHoldTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.ifc");

        private static ElementKey Key(string name) =>
            ElementKey.FromInstanceGuid(Scope, Guid.Parse(name[0] + "0000000-0000-0000-0000-000000000000"));

        private static Finding Made(string title = "Duct through beam") =>
            Finding.Create(FindingSource.Clash, "MEP vs Structure", title,
                           new[] { Key("a"), Key("b") }, "1,2,3");

        [Fact]
        public void NothingIsWaitingOnAFreshFinding()
        {
            var finding = Made();

            Assert.False(finding.IsHeld);
            Assert.Equal("nothing", finding.Describe());
        }

        [Fact]
        public void WhatIsWaitingReadsBackInAPhrase()
        {
            var finding = Made();

            finding.Status = FindingStatus.Reviewed;
            finding.Hold(FindingField.Status);

            finding.AssignedTo = "Structures";
            finding.Hold(FindingField.AssignedTo);

            finding.Comments.Add(new FindingComment("ada", 1, "Beam cannot move."));
            finding.Hold(FindingField.Comments);

            Assert.True(finding.IsHeld);
            Assert.False(finding.HostHasIt);
            Assert.Equal("status Reviewed, to Structures, 1 comment", finding.Describe());
        }

        [Fact]
        public void AClearedAssignmentSaysSoRatherThanSayingNothing()
        {
            var finding = Made();

            finding.AssignedTo = null;
            finding.Hold(FindingField.AssignedTo);

            Assert.Equal("unassigned", finding.Describe());
        }

        [Fact]
        public void OneFieldLandingLeavesTheOthersWaiting()
        {
            // A sync that wrote three of four and cleared all four would lose the fourth.
            var finding = Made();

            finding.Hold(FindingField.Status);
            finding.Hold(FindingField.Group);

            finding.Landed(FindingField.Status);

            Assert.True(finding.IsHeld);
            Assert.False(finding.HostHasIt);

            finding.Landed(FindingField.Group);

            Assert.False(finding.IsHeld);
            Assert.True(finding.HostHasIt);
        }

        [Fact]
        public void AnAnsweredCommentIsNotOfferedTwice()
        {
            var finding = Made();

            finding.Comments.Add(new FindingComment("ada", 1, "Beam cannot move."));
            finding.Hold(FindingField.Comments);

            Assert.False(finding.Comments.Single().InHost);

            finding.Landed(FindingField.Comments);

            Assert.True(finding.Comments.Single().InHost);
        }

        [Fact]
        public void ARereadDoesNotRevertWhatIsWaiting()
        {
            // The whole point. The published NWD still reports New, unassigned, ungrouped.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Made() }, 1);

            var held = register.All.Single();

            held.Status = FindingStatus.Reviewed;
            held.Hold(FindingField.Status);
            held.AssignedTo = "Structures";
            held.Hold(FindingField.AssignedTo);
            held.Group = "Level 3";
            held.Hold(FindingField.Group);
            held.Title = "Duct clash at grid C4";
            held.Hold(FindingField.Title);

            register.Observe(FindingSource.Clash, new[] { Made() }, 2);

            var after = register.All.Single();

            Assert.Equal(FindingStatus.Reviewed, after.Status);
            Assert.Equal("Structures", after.AssignedTo);
            Assert.Equal("Level 3", after.Group);
            Assert.Equal("Duct clash at grid C4", after.Title);
        }

        [Fact]
        public void ARereadStillCorrectsWhatNobodyIsHolding()
        {
            // The other half: a run that reworded a finding nobody has touched must still be
            // allowed to, or the board would show last month's wording for ever.
            var register = new FindingRegister();

            register.Observe(FindingSource.Clash, new[] { Made() }, 1);
            register.Observe(FindingSource.Clash, new[] { Made("Duct through transfer beam") }, 2);

            Assert.Equal("Duct through transfer beam", register.All.Single().Title);
        }

        [Fact]
        public void WhatIsWaitingSurvivesTheFile()
        {
            var finding = Made();

            finding.Status = FindingStatus.Reviewed;
            finding.Hold(FindingField.Status);
            finding.TopicGuid = "topic-1";
            finding.HostProblem = "This document's results are read-only.";
            finding.Comments.Add(new FindingComment("ada", 1, "Answered") { InHost = true });
            finding.Comments.Add(new FindingComment("ada", 2, "Waiting"));

            var back = FindingJson.Read(JsonReader.Parse(FindingJson.Write(finding).ToJson()),
                                        out var renamed);

            Assert.False(renamed);
            Assert.NotNull(back);
            Assert.Equal(new[] { FindingField.Status }, back!.Pending);
            Assert.Equal("topic-1", back.TopicGuid);
            Assert.Equal("This document's results are read-only.", back.HostProblem);
            Assert.True(back.Comments[0].InHost);
            Assert.False(back.Comments[1].InHost);
        }

        [Fact]
        public void ADueDateIsADayAndNotAnInstant()
        {
            var finding = Made();

            finding.DueDayNumber = Finding.DayNumberFor(new DateTime(2026, 4, 15, 23, 30, 0));

            Assert.Equal(new DateTime(2026, 4, 15), finding.Due);
            Assert.True(finding.IsOverdue(Finding.DayNumberFor(new DateTime(2026, 4, 16))));
            Assert.False(finding.IsOverdue(Finding.DayNumberFor(new DateTime(2026, 4, 15))));
        }
    }
}
