using System;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Which clashes have gone out, and where each stood when it went.
    ///
    /// The profile's own stamp is per test, which answers "have I sent this test" and nothing a
    /// weekly issue is built from.
    /// </summary>
    public class ClashIssueLogTests
    {
        private const long Friday = 638_000_000_000_000_000L;

        private static ClashItem Item(string name, FindingStatus status = FindingStatus.New)
        {
            var item = ClashFixture.Item(name);

            item.Name = name;
            item.Status = status;

            return item;
        }

        [Fact]
        public void A_clash_nothing_has_carried_has_not_been_sent()
        {
            var log = new ClashIssueLog();

            Assert.Equal(ClashIssueLog.NotSent, log.Standing(Item("a")));
            Assert.Null(log.Sent(Item("a").Key));
        }

        [Fact]
        public void A_clash_that_has_gone_out_and_not_moved_is_unchanged()
        {
            var one = Item("a");
            var log = new ClashIssueLog();

            Assert.Equal(1, log.Stamp(new[] { one }, "Mechanical", Friday));
            Assert.Equal(ClashIssueLog.Unchanged, log.Standing(one));
            Assert.Equal("Mechanical", log.Sent(one.Key)!.Party);
        }

        [Fact]
        public void A_clash_that_has_moved_since_it_went_is_the_one_to_send_again()
        {
            var one = Item("a");
            var log = new ClashIssueLog();

            log.Stamp(new[] { one }, "Mechanical", Friday);

            one.Status = FindingStatus.Resolved;

            Assert.Equal(ClashIssueLog.Changed, log.Standing(one));
        }

        [Fact]
        public void The_log_survives_being_written_and_read()
        {
            var one = Item("a");
            var two = Item("b", FindingStatus.Reviewed);

            var log = new ClashIssueLog();

            log.Stamp(new[] { one }, "Mechanical", Friday);
            log.Stamp(new[] { two }, "Structure", Friday);

            var read = ClashIssueLog.FromJson(log.ToJson());

            Assert.Equal(2, read.Count);
            Assert.Equal("Structure", read.Sent(two.Key)!.Party);
            Assert.Equal(FindingStatus.Reviewed, read.Sent(two.Key)!.Status);
            Assert.Equal(Friday, read.Sent(one.Key)!.WhenTicks);
        }

        [Fact]
        public void An_unreadable_entry_is_skipped_rather_than_failing_the_load()
        {
            var json = CamelWorks.Core.Store.JsonValue.Object()
                .Set("nonsense", CamelWorks.Core.Store.JsonValue.Object().Set("when", 1L));

            Assert.Equal(0, ClashIssueLog.FromJson(json).Count);
            Assert.Equal(0, ClashIssueLog.FromJson(null).Count);
        }

        [Fact]
        public void A_profile_can_ask_for_everything_that_has_not_gone_out()
        {
            // "Everything new or reopened since the last pack to this party" is the filter a
            // weekly issue is, and a per-test stamp cannot express it.
            var sent = Item("a");
            var fresh = Item("b");

            var log = new ClashIssueLog();

            log.Stamp(new[] { sent }, "Mechanical", Friday);

            var pick = new ClashPick();

            pick.Add("issued").Values.Add(ClashIssueLog.NotSent);

            var kept = pick.Take(new[] { sent, fresh }, null, log);

            Assert.Equal("b", Assert.Single(kept).Name);
        }

        [Fact]
        public void The_facets_appear_only_once_something_has_gone_out()
        {
            var one = Item("a");

            Assert.DoesNotContain(ClashFacets.Every(new[] { one }, null, new ClashIssueLog()),
                                  facet => facet.Key == "issued");

            var log = new ClashIssueLog();

            log.Stamp(new[] { one }, "Mechanical", Friday);

            Assert.Contains(ClashFacets.Every(new[] { one }, null, log),
                            facet => facet.Key == "issued");

            Assert.NotNull(ClashFacets.Of("issuedTo", new[] { one }, null, log));
        }
    }
}
