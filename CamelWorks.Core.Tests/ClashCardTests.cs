using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Search;
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

            public bool Comment(IReadOnlyList<Finding> findings, string body) =>
                Did("comment " + body + " on " + findings.Count);

            public bool Raise(IReadOnlyList<Finding> findings, string title)
            {
                if (!Did("raise " + title + " on " + findings.Count)) return false;

                foreach (var one in findings) Raised[one.Id] = title;

                return true;
            }

            /// <summary>What has been attached to an issue, by finding id.</summary>
            internal Dictionary<string, string> Raised { get; } =
                new Dictionary<string, string>(StringComparer.Ordinal);

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

        /// <summary>A world with no document and no columns, as a file-read board has.</summary>
        private sealed class Nowhere : IResultWorld
        {
            public CamelWorks.Core.Abstractions.IModelItem? Element(
                CamelWorks.Core.Identity.ElementKey key) => null;

            public IReadOnlyList<CamelWorks.Core.Checks.ResultColumn> Columns(Finding finding) =>
                Array.Empty<CamelWorks.Core.Checks.ResultColumn>();

            public string? IssueOf(Finding finding) =>
                Written != null && Written.Raised.TryGetValue(finding.Id, out var said) ? said : null;

            public double MetresPerUnit => 1.0;

            /// <summary>The writer whose attachments this world reports, or null for none.</summary>
            internal Written? Written { get; set; }
        }

        /// <summary>
        /// The results a list of rules runs over.
        ///
        /// The first two carry a priority, so a rule can find exactly those two and the test can
        /// assert what the rules under it are then handed. Nothing here is clash-shaped beyond the
        /// source: the walk is over findings.
        /// </summary>
        /// <param name="many">How many.</param>
        private static List<Finding> Board(int many)
        {
            var found = new List<Finding>();

            for (var at = 0; at < many; at++)
            {
                var one = Finding.Create(FindingSource.Clash, "Hard clash", "Clash " + at,
                                         Array.Empty<CamelWorks.Core.Identity.ElementKey>(),
                                         discriminator: "c" + at);

                one.Priority = at < 2 ? "High" : "Low";

                found.Add(one);
            }

            return found;
        }

        /// <summary>A search that finds everything.</summary>
        private static ResultQuery Every() => new ResultQuery();

        /// <summary>A search over one standing field.</summary>
        /// <param name="field">The field.</param>
        /// <param name="op">The operator.</param>
        /// <param name="text">What it is compared with.</param>
        private static ResultQuery Where(string field, QueryOp op, string? text = null) =>
            new ResultQuery(new QueryTerm(field, op) { Text = text });

        [Fact]
        public void A_card_assigns_what_it_finds_and_says_how_many_it_changed()
        {
            var found = Board(3);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("MEP to Mechanical", Every(), ClashActionKind.Assign)
                {
                    Value = "Mechanical",
                },
            };

            var did = ClashCardRun.Apply(cards, found, new Nowhere(), writer);

            Assert.Equal(3, Assert.Single(did).Matched);
            Assert.Equal(3, did[0].Changed);
            Assert.Equal("assign Mechanical on 3", Assert.Single(writer.Log));
            Assert.All(found, one => Assert.Equal("Mechanical", one.AssignedTo));
        }

        [Fact]
        public void Running_the_same_card_twice_changes_nothing_the_second_time()
        {
            var found = Board(3);
            var writer = new Written();


            var cards = new[]
            {
                new ClashCard("Approve them", Every(), ClashActionKind.Status) { Value = "Approved" },
            };

            Assert.Equal(3, ClashCardRun.Apply(cards, found, new Nowhere(), writer)[0].Changed);

            var again = ClashCardRun.Apply(cards, found, new Nowhere(), writer)[0];

            // Found again, changed none: the card still matches every result, and every one of
            // them already holds what it sets.
            Assert.Equal(3, again.Matched);
            Assert.Equal(0, again.Changed);
            Assert.Single(writer.Log);
        }

        [Fact]
        public void A_card_that_takes_results_off_the_board_hides_them_from_the_cards_under_it()
        {
            var found = Board(4);
            var writer = new Written();

            // The whole reason the cards are a list and not a set: the first one runs, and the
            // second sees what is left.
            var cards = new[]
            {
                new ClashCard("One model against itself", Where(ResultFields.Priority, QueryOp.Is, "High"),
                              ClashActionKind.Suppress),
                new ClashCard("The rest to Structure", Every(), ClashActionKind.Assign)
                {
                    Value = "Structure",
                },
            };

            var did = ClashCardRun.Apply(cards, found, new Nowhere(), writer);

            Assert.Equal(2, did[0].Changed);
            Assert.Equal(2, did[1].Matched);
            Assert.Equal("assign Structure on 2", Assert.Single(writer.Log));
        }

        [Fact]
        public void A_plan_says_what_the_run_would_do_and_writes_nothing()
        {
            var found = Board(3);

            var cards = new[]
            {
                new ClashCard("Approve them", Every(), ClashActionKind.Status) { Value = "Approved" },
            };

            var planned = ClashCardRun.Plan(cards, found, new Nowhere());

            Assert.Equal(3, planned[0].Matched);
            Assert.Equal(3, planned[0].Changed);
            Assert.All(found, one => Assert.Equal(FindingStatus.New, one.Status));
        }

        [Fact]
        public void A_card_with_nothing_to_set_is_reported_rather_than_run()
        {
            var found = Board(2);
            var writer = new Written();

            var cards = new[] { new ClashCard("Assign to nobody", Every(), ClashActionKind.Assign) };

            var did = Assert.Single(ClashCardRun.Apply(cards, found, new Nowhere(), writer));

            Assert.Equal(0, did.Matched);
            Assert.Contains("party", did.Problem);
            Assert.Empty(writer.Log);
        }

        [Fact]
        public void A_document_that_refuses_the_write_leaves_the_findings_as_they_were()
        {
            var found = Board(2);
            var writer = new Written { Refuses = "This NWD is published." };

            var cards = new[]
            {
                new ClashCard("MEP to Mechanical", Every(), ClashActionKind.Assign)
                {
                    Value = "Mechanical",
                },
            };

            var did = Assert.Single(ClashCardRun.Apply(cards, found, new Nowhere(), writer));

            Assert.Equal(2, did.Matched);
            Assert.Equal(0, did.Changed);
            Assert.Contains("published", did.Problem);
            Assert.All(found, one => Assert.Null(one.AssignedTo));
        }

        [Fact]
        public void A_removed_result_is_marked_absent_rather_than_dropped_from_the_register()
        {
            // An issue that names one of these still has to resolve it to say what it was about.
            var found = Board(2);
            var writer = new Written();

            var cards = new[] { new ClashCard("Gone", Every(), ClashActionKind.Remove) };

            Assert.Equal(2, Assert.Single(ClashCardRun.Apply(cards, found, new Nowhere(), writer)).Changed);

            Assert.Equal("remove 2", Assert.Single(writer.Log));
            Assert.All(found, one => Assert.False(one.IsPresent));
        }

        [Fact]
        public void An_off_card_is_walked_past()
        {
            var found = Board(2);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("Off", Every(), ClashActionKind.Suppress) { IsEnabled = false },
            };

            Assert.Empty(ClashCardRun.Apply(cards, found, new Nowhere(), writer));
        }

        [Fact]
        public void A_remark_is_written_once_however_often_the_rule_runs()
        {
            var found = Board(2);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("Say it", Every(), ClashActionKind.Comment)
                {
                    Value = "Coordinated on the 3rd",
                },
            };

            Assert.Equal(2, Assert.Single(
                ClashCardRun.Apply(cards, found, new Nowhere(), writer)).Changed);

            Assert.All(found, one => Assert.Equal("Coordinated on the 3rd",
                                                  Assert.Single(one.Comments).Body));

            // The same rule again. It finds the same two and changes neither.
            var again = Assert.Single(ClashCardRun.Apply(cards, found, new Nowhere(), writer));

            Assert.Equal(2, again.Matched);
            Assert.Equal(0, again.Changed);
            Assert.All(found, one => Assert.Single(one.Comments));
        }

        [Fact]
        public void A_label_and_a_priority_are_written_to_the_register()
        {
            var found = Board(3);
            var writer = new Written();

            var cards = new[]
            {
                new ClashCard("Mark them", Every(), ClashActionKind.Label) { Value = "Level 03" },
                new ClashCard("Raise them", Every(), ClashActionKind.Priority) { Value = "High" },
            };

            var did = ClashCardRun.Apply(cards, found, new Nowhere(), writer);

            Assert.Equal(3, did[0].Changed);
            Assert.All(found, one => Assert.Contains("Level 03", one.Labels));

            // Two of the three already carry High, so only the third is a change.
            Assert.Equal(1, did[1].Changed);
            Assert.All(found, one => Assert.Equal("High", one.Priority));

            // Neither reaches the document: a label and a priority are the project's own fields.
            Assert.Empty(writer.Log);
        }

        [Fact]
        public void Attaching_to_an_issue_is_not_done_twice()
        {
            var found = Board(2);
            var writer = new Written();
            var world = new Nowhere { Written = writer };

            var cards = new[]
            {
                new ClashCard("Raise it", Every(), ClashActionKind.Issue)
                {
                    Value = "Level 03 riser",
                },
            };

            Assert.Equal(2, Assert.Single(ClashCardRun.Apply(cards, found, world, writer)).Changed);
            Assert.Equal("raise Level 03 riser on 2", Assert.Single(writer.Log));

            var again = Assert.Single(ClashCardRun.Apply(cards, found, world, writer));

            Assert.Equal(2, again.Matched);
            Assert.Equal(0, again.Changed);
            Assert.Single(writer.Log);
        }

        [Fact]
        public void Every_action_that_needs_a_value_says_so_rather_than_running_without_one()
        {
            var found = Board(1);
            var writer = new Written();

            foreach (var action in new[]
            {
                ClashActionKind.Group, ClashActionKind.Assign, ClashActionKind.Status,
                ClashActionKind.Comment, ClashActionKind.Label, ClashActionKind.Priority,
                ClashActionKind.Issue,
            })
            {
                var did = Assert.Single(ClashCardRun.Apply(
                    new[] { new ClashCard("No value", Every(), action) },
                    found, new Nowhere(), writer));

                Assert.Equal(0, did.Matched);
                Assert.NotNull(did.Problem);
            }

            Assert.Empty(writer.Log);
        }
    }
}
