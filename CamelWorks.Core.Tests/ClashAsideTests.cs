using System;
using System.IO;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The clash properties Navisworks has nowhere to keep.
    ///
    /// The point is that the board can show a due date the host has no column for, and that the
    /// next export sends it back out. Both directions are tested, because carrying it one way
    /// only is how a reviewer's priority gets quietly reset every cycle.
    ///
    /// These live on the finding now. What a markup said is read into a record, laid onto the
    /// finding it belongs to, and read back off it when the file goes out again - so the cycle
    /// tests go through a finding rather than through a register of their own.
    /// </summary>
    public class ClashAsideTests
    {
        /// <summary>A clash finding for an item, the way a read of the model would make one.</summary>
        private static Finding Made(ClashItem item)
        {
            var finding = Core.Findings.Finding.Create(
                FindingSource.Clash, "MEP vs Structure", item.TestName,
                new[] { item.Key.A, item.Key.B }, ClashFindings.CellOf(item.Key));

            finding.TopicGuid = ClashBcf.TopicGuidFor(item.Key);

            return finding;
        }

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
            var aside = new ClashAside("topic-1");
            aside.TestId = "T1";
            aside.ResultId = "R1";
            aside.Priority = "High";
            aside.Stage = "Construction";
            aside.Title = "Duct through transfer beam";
            aside.DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks;
            aside.Source = "ada.bcfzip";
            aside.WhenTicks = 123456789L;
            aside.Labels.Add("Site query");

            var read = ClashAside.FromJson(JsonReader.Parse(aside.ToJson().ToJson()));

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
        public void WhatTheMarkupSaidLandsOnTheFinding()
        {
            var finding = Made(Item());

            new ClashAside("topic-1")
            {
                Priority = "High",
                Stage = "Construction",
                Title = "Duct through transfer beam",
                DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks,
                Source = "ada.bcfzip",
            }.ApplyTo(finding);

            Assert.Equal("High", finding.Priority);
            Assert.Equal("Construction", finding.Stage);
            Assert.Equal("Duct through transfer beam", finding.Title);
            Assert.Equal(new DateTime(2026, 4, 15), finding.Due);
            Assert.Equal("ada.bcfzip", finding.Origin);
            Assert.Equal("topic-1", finding.TopicGuid);
        }

        [Fact]
        public void AToolThatSaysNothingClearsNothing()
        {
            // A reviewer's tool with no stage picker sends no stage. Reading that as "clear the
            // stage" would let the least capable tool in the round trip erase what the rest set.
            var finding = Made(Item());

            new ClashAside("topic-1") { Priority = "High", Stage = "Construction" }.ApplyTo(finding);
            new ClashAside("topic-1") { Priority = "Low" }.ApplyTo(finding);

            Assert.Equal("Low", finding.Priority);
            Assert.Equal("Construction", finding.Stage);
        }

        [Fact]
        public void AFindingNobodySetAnythingOnSendsNothingBack()
        {
            // Otherwise every export would carry a record per clash, holding nothing.
            Assert.Null(ClashAside.Of(Made(Item())));
        }

        [Fact]
        public void OverdueIsToldFromTheFinding()
        {
            var finding = Made(Item());

            new ClashAside("topic-1")
                { DueTicks = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc).Ticks }
                .ApplyTo(finding);

            Assert.True(finding.IsOverdue(Finding.DayNumberFor(new DateTime(2026, 5, 1))));
            Assert.False(finding.IsOverdue(Finding.DayNumberFor(new DateTime(2026, 4, 1))));
            Assert.False(Made(Item()).IsOverdue(
                Finding.DayNumberFor(new DateTime(2026, 5, 1))));
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

            var finding = Made(Item());
            ClashBcf.Review(home, home.Name, "ada.bcfzip")!.ApplyTo(finding);

            // Next week's export, built from the model plus what the finding kept.
            var again = Item();
            again.Review = ClashAside.Of(finding);

            using var second = new MemoryStream(ClashBcf.Write(new[] { again }).Bytes);

            var sentAgain = BcfReader.Read(second).Topics.Single();

            Assert.Equal("High", sentAgain.Priority);
            Assert.Equal(new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), sentAgain.DueDate);
        }
    }
}
