using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The card list: what it finds, what it changes, and in what order.
    ///
    /// The Filter tab writes through this, and every write it makes is one somebody would
    /// otherwise make by hand on the Manage board over a picked set of rows. What is asserted
    /// here is the three things that make a list of cards different from a set of rules: order
    /// carries, running twice changes nothing the second time, and a preview says exactly what
    /// the run would do.
    /// </summary>
    public class ClashCardTests
    {
        /// <summary>Where the writes went, so a test can see them without a host.</summary>
        private sealed class Written : IClashCardWriter
        {
            internal List<string> Log { get; } = new List<string>();

            internal string? Refuses { get; set; }

            public string? LastProblem { get; private set; }

            public bool Status(IReadOnlyList<Finding> findings, FindingStatus status) =>
                Did("status " + status + " on " + findings.Count);

            public bool Assign(IReadOnlyList<Finding> findings, string party) =>
                Did("assign " + party + " on " + findings.Count);

            public bool Group(IReadOnlyList<Finding> findings, string name) =>
                Did("group " + name + " on " + findings.Count);

            public bool Remove(IReadOnlyList<Finding> findings) =>
                Did("remove " + findings.Count);

            private bool Did(string what)
            {
                if (Refuses != null)
                {
                    LastProblem = Refuses;
                    return false;
                }

                Log.Add(what);

                return true;
            }
        }

        /// <summary>Items and the findings behind them, keyed the way a board keys them.</summary>
        private static (List<ClashItem> Items, Dictionary<string, Finding> Found) Board(int many)
        {
            var items = new List<ClashItem>();
            var found = new Dictionary<string, Finding>(StringComparer.Ordinal);

            // The first two are one model against itself, so a card can find exactly those two
            // and the test can assert what the cards under it are then handed.
            for (var at = 0; at < many; at++)
            {
                var item = ClashFixture.Item("c" + at,
                                                   modelB: at < 2 ? "ARCH" : "MEP");
                var finding = ClashFixture.Found(item);

                item.FindingId = finding.Id;
                found[finding.Id] = finding;
                items.Add(item);
            }

            return (items, found);
        }

        private static PredicateSpec Every() => new PredicateSpec("always");

        [Fact]
        public void A_card_assigns_what_it_finds_and_says_how_many_it_changed()
        {
            var (items, found) = Board(3);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("MEP to Mechanical", Every(), ClashActionKind.Assign)
                {
                    Value = "Mechanical",
                },
            };

            var did = ClashCardRun.Apply(cards, items, id => found.TryGetValue(id ?? string.Empty,
                                                                              out var one) ? one : null,
                                         writer);

            Assert.Equal(3, Assert.Single(did).Matched);
            Assert.Equal(3, did[0].Changed);
            Assert.Equal("assign Mechanical on 3", Assert.Single(writer.Log));
            Assert.All(found.Values, one => Assert.Equal("Mechanical", one.AssignedTo));
        }

        [Fact]
        public void Running_the_same_card_twice_changes_nothing_the_second_time()
        {
            var (items, found) = Board(3);
            var writer = new Written();

            Finding? Of(string? id) => found.TryGetValue(id ?? string.Empty, out var one) ? one : null;

            var cards = new[]
            {
                new ClashCard("Approve them", Every(), ClashActionKind.Status) { Value = "Approved" },
            };

            Assert.Equal(3, ClashCardRun.Apply(cards, items, Of, writer)[0].Changed);

            var again = ClashCardRun.Apply(cards, items, Of, writer)[0];

            // Found again, changed none: the card still matches every result, and every one of
            // them already holds what it sets.
            Assert.Equal(3, again.Matched);
            Assert.Equal(0, again.Changed);
            Assert.Single(writer.Log);
        }

        [Fact]
        public void A_card_that_takes_results_off_the_board_hides_them_from_the_cards_under_it()
        {
            var (items, found) = Board(4);
            var writer = new Written();

            // The whole reason the cards are a list and not a set: the first one runs, and the
            // second sees what is left.
            var cards = new[]
            {
                new ClashCard("One model against itself", new PredicateSpec("sameModel"),
                              ClashActionKind.Suppress),
                new ClashCard("The rest to Structure", Every(), ClashActionKind.Assign)
                {
                    Value = "Structure",
                },
            };

            var did = ClashCardRun.Apply(cards, items,
                                         id => found.TryGetValue(id ?? string.Empty, out var one)
                                             ? one : null,
                                         writer);

            Assert.Equal(2, did[0].Changed);
            Assert.Equal(2, did[1].Matched);
            Assert.Equal("assign Structure on 2", Assert.Single(writer.Log));
        }

        [Fact]
        public void A_plan_says_what_the_run_would_do_and_writes_nothing()
        {
            var (items, found) = Board(3);

            var cards = new[]
            {
                new ClashCard("Approve them", Every(), ClashActionKind.Status) { Value = "Approved" },
            };

            var planned = ClashCardRun.Plan(cards, items,
                                            id => found.TryGetValue(id ?? string.Empty, out var one)
                                                ? one : null);

            Assert.Equal(3, planned[0].Matched);
            Assert.Equal(3, planned[0].Changed);
            Assert.All(found.Values, one => Assert.Equal(FindingStatus.New, one.Status));
        }

        [Fact]
        public void A_card_with_nothing_to_set_is_reported_rather_than_run()
        {
            var (items, found) = Board(2);
            var writer = new Written();

            var cards = new[] { new ClashCard("Assign to nobody", Every(), ClashActionKind.Assign) };

            var did = Assert.Single(ClashCardRun.Apply(
                cards, items,
                id => found.TryGetValue(id ?? string.Empty, out var one) ? one : null, writer));

            Assert.Equal(0, did.Matched);
            Assert.Contains("party", did.Problem);
            Assert.Empty(writer.Log);
        }

        [Fact]
        public void A_document_that_refuses_the_write_leaves_the_findings_as_they_were()
        {
            var (items, found) = Board(2);
            var writer = new Written { Refuses = "This NWD is published." };

            var cards = new[]
            {
                new ClashCard("MEP to Mechanical", Every(), ClashActionKind.Assign)
                {
                    Value = "Mechanical",
                },
            };

            var did = Assert.Single(ClashCardRun.Apply(
                cards, items,
                id => found.TryGetValue(id ?? string.Empty, out var one) ? one : null, writer));

            Assert.Equal(2, did.Matched);
            Assert.Equal(0, did.Changed);
            Assert.Contains("published", did.Problem);
            Assert.All(found.Values, one => Assert.Null(one.AssignedTo));
        }

        [Fact]
        public void A_removed_result_is_marked_absent_rather_than_dropped_from_the_register()
        {
            // An issue that names one of these still has to resolve it to say what it was about.
            var (items, found) = Board(2);
            var writer = new Written();

            var cards = new[] { new ClashCard("Gone", Every(), ClashActionKind.Remove) };

            Assert.Equal(2, ClashCardRun.Apply(
                cards, items,
                id => found.TryGetValue(id ?? string.Empty, out var one) ? one : null,
                writer)[0].Changed);

            Assert.Equal("remove 2", Assert.Single(writer.Log));
            Assert.All(found.Values, one => Assert.False(one.IsPresent));
        }

        [Fact]
        public void An_off_card_is_walked_past()
        {
            var (items, found) = Board(2);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("Off", Every(), ClashActionKind.Suppress) { IsEnabled = false },
            };

            Assert.Empty(ClashCardRun.Apply(
                cards, items,
                id => found.TryGetValue(id ?? string.Empty, out var one) ? one : null, writer));
        }
    }
}
