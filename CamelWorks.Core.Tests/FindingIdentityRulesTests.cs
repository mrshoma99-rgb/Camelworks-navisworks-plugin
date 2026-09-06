using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The three rules that keep a sign-off attached across runs of this build, and the two writers
    /// that have to agree on a clash's BCF topic.
    ///
    /// A finding's id is derived from its source, rule, title, elements and discriminator on every
    /// load. Anything that re-derives an id detaches every status, assignee, comment and issue
    /// attachment in a live project, silently, so each rule is pinned rather than described.
    /// </summary>
    public class FindingIdentityRulesTests
    {
        private static readonly string Scope = ElementKey.ScopeOf("MEP.nwc");

        private static ElementKey Key(char name) =>
            ElementKey.FromInstanceGuid(Scope, Guid.Parse(name + "0000000-0000-0000-0000-000000000000"));

        private static JsonValue Record(string? source)
        {
            var json = JsonValue.Object()
                .Set("rule", "MEP vs Structure")
                .Set("title", "Duct through beam")
                .Set("status", "New")
                .Set("elements", JsonValue.Array(new[] { JsonValue.String(Key('a').ToString()) }));

            if (source != null) json.Set("source", source);

            return json;
        }

        [Fact]
        public void The_source_names_are_the_ones_that_are_written()
        {
            // Each of these is hashed into every finding id under it and written to the sidecar.
            // Renaming one re-identifies a whole check's worth of findings at once, so the strings
            // are pinned here rather than left to a refactor.
            Assert.Equal("Clash", FindingSource.Clash);
            Assert.Equal("Manual", FindingSource.Manual);
            Assert.Equal("Health", FindingSource.Health);
            Assert.Equal("Headroom", FindingSource.Headroom);
            Assert.Equal("Ids", FindingSource.Ids);
            Assert.Equal("SetDrift", FindingSource.SetDrift);
            Assert.Equal("Compare", FindingSource.Compare);
            Assert.Equal("FallHazard", FindingSource.FallHazard);

            Assert.Equal(new[]
            {
                "Clash", "Manual", "Health", "Headroom", "Ids", "SetDrift", "Compare", "FallHazard",
            }, FindingSource.Known);
        }

        [Fact]
        public void A_record_with_no_source_is_counted_unreadable_and_produces_no_finding()
        {
            // A finding with an empty source would have its id derived from the empty string and
            // would collide with every other record that lost its source.
            var missing = FindingJson.Read(Record(null), out _);
            var blank = FindingJson.Read(Record("   "), out _);

            Assert.Null(missing);
            Assert.Null(blank);

            var register = FindingRegister.FromJson(
                JsonValue.Object().Set("findings", JsonValue.Array(new[] { Record(null) })));

            Assert.Equal(0, register.Count);
        }

        [Fact]
        public void An_unknown_but_non_empty_source_reads_through()
        {
            // A finding written by a check this build does not have survives a round trip rather
            // than being dropped by a build that has not learned the key yet.
            var read = FindingJson.Read(Record("Clearance"), out _);

            Assert.NotNull(read);
            Assert.Equal("Clearance", read!.Source);
            Assert.DoesNotContain("Clearance", FindingSource.Known);

            var again = FindingJson.Read(FindingJson.Write(read), out var renamed);

            Assert.False(renamed);
            Assert.Equal(read.Id, again!.Id);
        }

        [Fact]
        public void The_clash_identity_branch_is_ordinal_and_case_sensitive()
        {
            // The branch reads the host's id for the test out of the facts and hashes that instead
            // of the rule name. Matched case-insensitively, or with a producer emitting "clash",
            // every clash finding in the project re-identifies at once.
            var lower = Record("clash").Set("facts",
                JsonValue.Object().Set(ClashFindings.HostTest, "host-test-7"));

            var upper = Record(FindingSource.Clash).Set("facts",
                JsonValue.Object().Set(ClashFindings.HostTest, "host-test-7"));

            var read = FindingJson.Read(lower, out _);
            var clash = FindingJson.Read(upper, out _);

            Assert.NotNull(read);
            Assert.Equal("clash", read!.Source);

            // The lower-case record did not take the clash branch, so its identity came from the
            // rule name rather than the host test id.
            Assert.Equal(Finding.Create("clash", "MEP vs Structure", "Duct through beam",
                                        new[] { Key('a') }).Id,
                         read.Id);

            Assert.Equal(Finding.Create(FindingSource.Clash, "MEP vs Structure", "Duct through beam",
                                        new[] { Key('a') }, identity: ClashFindings.Identity("host-test-7")).Id,
                         clash!.Id);

            Assert.NotEqual(read.Id, clash.Id);
        }

        [Fact]
        public void A_clash_topic_guid_is_the_same_whichever_writer_exports_it()
        {
            // ClashFindings.From stamps the key-derived topic onto the finding and
            // FindingBcf.TopicGuidFor prefers the stamped value. Dropping either half gives one
            // clash two guids and a file coming back matches neither.
            var result = new ClashResultInfo("7", Key('a'), Key('b'), 1, 2, 3, null, null)
            {
                Name = "Clash7",
            };

            var finding = ClashFindings.From("MEP vs Structure", result);

            Assert.Equal(ClashBcf.TopicGuidFor(result.Key(ClashKey.DefaultCell)),
                         FindingBcf.TopicGuidFor(finding));
        }
    }
}
