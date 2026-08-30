using System;
using System.IO;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The clash properties Navisworks has nowhere to keep.
    ///
    /// The point of the register is that the board can show a due date the host has no column for,
    /// and that the next export sends it back out. Both directions are tested, because carrying it
    /// one way only is how a reviewer's priority gets quietly reset every cycle.
    /// </summary>
    public class ClashAsidesTests
    {
        private static ClashItem Item()
        {
            var scope = ElementKey.ScopeOf("MEP.ifc");
            var a = ElementKey.FromInstanceGuid(scope, Guid.Parse("11111111-1111-1111-1111-111111111111"));
            var b = ElementKey.FromInstanceGuid(scope, Guid.Parse("22222222-2222-2222-2222-222222222222"));

            return new ClashItem(ClashKey.Create(a, b, 1, 2, 3), "MEP vs Structure");
        }

        [Fact]
        public void EveryFieldSurvivesTheProjectFile()
        {
            var register = new ClashAsides();

            var aside = register.For("topic-1");
            aside.TestId = "T1";
            aside.ResultId = "R1";
            aside.Priority = "High";
            aside.Stage = "Construction";
            aside.Title = "Duct through transfer beam";
            aside.DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks;
            aside.Source = "ada.bcfzip";
            aside.WhenTicks = 123456789L;
            aside.Labels.Add("Site query");

            var back = ClashAsides.FromJson(JsonReader.Parse(register.ToJson().ToJson()));
            var read = back.Find("topic-1");

            Assert.NotNull(read);
            Assert.Equal("T1", read!.TestId);
            Assert.Equal("R1", read.ResultId);
            Assert.Equal("High", read.Priority);
            Assert.Equal("Construction", read.Stage);
            Assert.Equal("Duct through transfer beam", read.Title);
            Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), read.Due);
            Assert.Equal("ada.bcfzip", read.Source);
            Assert.Equal(123456789L, read.WhenTicks);
            Assert.Equal(new[] { "Site query" }, read.Labels);
        }

        [Fact]
        public void ALaterMarkupReplacesAnEarlierOneOutright()
        {
            // A reviewer who cleared a due date has said something. A merge that kept the old one
            // would refuse to hear it.
            var register = new ClashAsides();

            register.Put(new ClashAside("topic-1")
            {
                Priority = "High",
                DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks,
            });

            register.Put(new ClashAside("topic-1") { Priority = "Low" });

            var read = register.Find("topic-1");

            Assert.Equal("Low", read!.Priority);
            Assert.Equal(0, read.DueTicks);
        }

        [Fact]
        public void ARecordHoldingNothingRemovesTheEntry()
        {
            var register = new ClashAsides();

            register.Put(new ClashAside("topic-1") { Priority = "High" });
            Assert.Equal(1, register.Count);

            register.Put(new ClashAside("topic-1"));
            Assert.True(register.IsEmpty);
        }

        [Fact]
        public void OverdueIsCounted()
        {
            var now = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
            var register = new ClashAsides();

            register.Put(new ClashAside("late")
                { DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks });

            register.Put(new ClashAside("soon")
                { DueTicks = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc).Ticks });

            register.Put(new ClashAside("undated") { Priority = "Low" });

            Assert.Equal(1, register.Overdue(now));
            Assert.True(register.Find("late")!.IsOverdue(now));
            Assert.False(register.Find("undated")!.IsOverdue(now));
        }

        [Fact]
        public void ItReadsShortInAColumnAndFullInAMessage()
        {
            var aside = new ClashAside("t")
            {
                Priority = "High",
                DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks,
            };

            Assert.Equal("High · due 15 Apr", aside.Describe());
            Assert.Equal("priority High; due 2026-04-15", aside.Explain());
            Assert.Equal("nothing", new ClashAside("t").Explain());
        }

        // -----------------------------------------------------------------------------------

        [Fact]
        public void WhatAReviewerSetComesBackAsARecord()
        {
            var item = Item();

            using var mine = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(mine).Topics.Single();

            topic.Priority = "High";
            topic.DueDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);
            topic.Labels.Add("Site query");

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);
            theirs.Position = 0;

            var result = ClashBcf.Read(theirs).Results.Single();
            var review = ClashBcf.Review(result, result.Name, "ada.bcfzip");

            Assert.NotNull(review);
            Assert.Equal(ClashBcf.TopicGuidFor(item.Key), review!.TopicGuid);
            Assert.Equal("High", review.Priority);
            Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), review.Due);
            Assert.Equal(new[] { "Site query" }, review.Labels);
            Assert.Equal("ada.bcfzip", review.Source);
        }

        [Fact]
        public void AResultNobodyTouchedMakesNoRecord()
        {
            // Otherwise the register grows by the size of the board every sync, holding nothing.
            using var stream = new MemoryStream(ClashBcf.Write(new[] { Item() }).Bytes);

            var result = ClashBcf.Read(stream).Results.Single();

            Assert.Null(ClashBcf.Review(result, result.Name));
        }

        [Fact]
        public void ItGoesBackOutOnTheNextExport()
        {
            // The half that stops a reviewer's priority being reset every time the file is resent.
            var item = Item();

            item.Review = new ClashAside(ClashBcf.TopicGuidFor(item.Key))
            {
                Priority = "High",
                Stage = "Construction",
                DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks,
            };
            item.Review.Labels.Add("Site query");

            using var stream = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(stream).Topics.Single();

            Assert.Equal("High", topic.Priority);
            Assert.Equal("Construction", topic.Stage);
            Assert.Equal(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), topic.DueDate);
            Assert.Contains("Site query", topic.Labels);
        }

        [Fact]
        public void ItSurvivesAWholeCycleWithoutDegrading()
        {
            // Out, answered, home, out again. The reviewer sets a priority once and it is still
            // there two exports later - which is the whole point.
            var item = Item();

            using var first = new MemoryStream(ClashBcf.Write(new[] { item }).Bytes);

            var topic = BcfReader.Read(first).Topics.Single();
            topic.Priority = "High";
            topic.DueDate = new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero);

            using var answered = new MemoryStream();
            BcfWriter.Write(answered, new[] { topic }, BcfVersion.V21);
            answered.Position = 0;

            var home = ClashBcf.Read(answered).Results.Single();

            var register = new ClashAsides();
            register.Put(ClashBcf.Review(home, home.Name, "ada.bcfzip"));

            // Next week's export, built from the model plus the register.
            var again = Item();
            again.Review = register.Find(ClashBcf.TopicGuidFor(again.Key));

            using var second = new MemoryStream(ClashBcf.Write(new[] { again }).Bytes);

            var sentAgain = BcfReader.Read(second).Topics.Single();

            Assert.Equal("High", sentAgain.Priority);
            Assert.Equal(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), sentAgain.DueDate);
        }
    }
}
