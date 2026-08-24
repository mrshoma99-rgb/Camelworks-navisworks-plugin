using System;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class PartyLedgerTests
    {
        private static readonly DateTimeOffset LastWeek =
            new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

        private static readonly DateTimeOffset ThisWeek =
            new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

        /// <summary>A board of N groups, each with a party and a list of statuses.</summary>
        private static ClashPipelineResult Board(params (string? Party, FindingStatus[] Items)[] rows)
        {
            var items = rows
                .SelectMany((row, r) => row.Items.Select((_, i) =>
                    ClashFixture.Item("g" + r + "i" + i, x: r * 1000 + i)))
                .ToList();

            // One group per row: pin each row's keys under its own name so grouping is exactly the
            // shape the test states.
            var pinned = rows
                .Select((row, r) => new
                {
                    Name = "Group " + r,
                    Keys = (System.Collections.Generic.IReadOnlyList<string>)items
                        .Skip(rows.Take(r).Sum(p => p.Items.Length))
                        .Take(row.Items.Length)
                        .Select(i => i.Key.ToString())
                        .ToList(),
                })
                .ToDictionary(g => g.Name, g => g.Keys, StringComparer.Ordinal);

            var board = ClashPipeline.Run(items, new ClashPipelineOptions { PinnedGroups = pinned });

            for (var r = 0; r < rows.Length; r++)
            {
                var group = board.Groups.Single(g => g.Name == "Group " + r);

                if (rows[r].Party != null) group.AssignByHand(rows[r].Party);

                for (var i = 0; i < group.Items.Count; i++) group.Items[i].Status = rows[r].Items[i];
            }

            return board;
        }

        [Fact]
        public void Every_party_gets_its_open_and_closed_count()
        {
            var board = Board(
                ("Mechanical", new[] { FindingStatus.Active, FindingStatus.New, FindingStatus.Resolved }),
                ("Structures", new[] { FindingStatus.Approved }));

            var ledger = PartyLedger.Of(board.Groups);

            var mechanical = ledger.Lines.Single(l => l.Party == "Mechanical");

            Assert.Equal(2, mechanical.Open);
            Assert.Equal(1, mechanical.Closed);
            Assert.Equal(0, ledger.Lines.Single(l => l.Party == "Structures").Open);
            Assert.Equal(2, ledger.Open);
            Assert.Equal(2, ledger.Closed);
        }

        [Fact]
        public void Reviewed_is_open_because_looking_at_a_problem_does_not_move_it()
        {
            var ledger = PartyLedger.Of(Board(("Mechanical", new[] { FindingStatus.Reviewed })).Groups);

            Assert.Equal(1, ledger.Open);
            Assert.Equal(0, ledger.Closed);
        }

        [Fact]
        public void An_unassigned_group_is_somebody_line_rather_than_missing_from_the_table()
        {
            var ledger = PartyLedger.Of(Board((null, new[] { FindingStatus.Active })).Groups);

            Assert.Equal(PartyLedger.Nobody, Assert.Single(ledger.Lines).Party);
        }

        [Fact]
        public void With_no_previous_run_the_change_column_is_absent_rather_than_zero_filled()
        {
            // "Did not move" and "was never measured" are different facts, and a zero claims the
            // first about a week nobody observed.
            var ledger = PartyLedger.Of(Board(("Mechanical", new[] { FindingStatus.Active })).Groups);

            Assert.False(ledger.HasPrevious);
            Assert.Null(Assert.Single(ledger.Lines).Delta);
            Assert.Equal(string.Empty, ledger.Lines[0].DeltaText);
            Assert.Equal(3, ledger.Table().Headers.Count);
            Assert.Contains("no previous run", ledger.Provenance());
        }

        [Fact]
        public void Against_a_previous_run_the_change_is_signed()
        {
            var lastWeek = ClashSnapshot.Of(
                Board(("Mechanical", new[] { FindingStatus.Active, FindingStatus.Active, FindingStatus.Active }),
                      ("Structures", new[] { FindingStatus.Active })),
                takenUtc: LastWeek);

            var ledger = PartyLedger.Of(
                Board(("Mechanical", new[] { FindingStatus.Resolved, FindingStatus.Active, FindingStatus.Active }),
                      ("Structures", new[] { FindingStatus.Active, FindingStatus.Active })).Groups,
                lastWeek, "Level 3", ThisWeek);

            Assert.True(ledger.HasPrevious);
            Assert.Equal(5, ledger.Table().Headers.Count);

            var mechanical = ledger.Lines.Single(l => l.Party == "Mechanical");
            var structures = ledger.Lines.Single(l => l.Party == "Structures");

            Assert.Equal(3, mechanical.PreviousOpen);
            Assert.Equal(-1, mechanical.Delta);
            Assert.Equal("-1", mechanical.DeltaText);
            Assert.Equal("+1", structures.DeltaText);
            Assert.Contains("Level 3", ledger.Provenance());
            Assert.Contains("14 August 2026", ledger.Provenance());
            Assert.Contains("21 August 2026", ledger.Provenance());
        }

        [Fact]
        public void A_party_that_has_gone_to_zero_keeps_its_line_because_that_is_the_best_number()
        {
            var lastWeek = ClashSnapshot.Of(
                Board(("Mechanical", new[] { FindingStatus.Active, FindingStatus.Active })),
                takenUtc: LastWeek);

            var ledger = PartyLedger.Of(
                Board(("Mechanical", new[] { FindingStatus.Resolved, FindingStatus.Resolved })).Groups,
                lastWeek);

            var line = Assert.Single(ledger.Lines);

            Assert.Equal(0, line.Open);
            Assert.Equal(2, line.Closed);
            Assert.Equal("-2", line.DeltaText);
            Assert.Contains("2 fewer open", ledger.Explain());
        }

        [Fact]
        public void A_party_that_only_existed_last_week_still_gets_a_line()
        {
            var lastWeek = ClashSnapshot.Of(
                Board(("Gone", new[] { FindingStatus.Active })), takenUtc: LastWeek);

            var ledger = PartyLedger.Of(
                Board(("Mechanical", new[] { FindingStatus.Active })).Groups, lastWeek);

            var gone = ledger.Lines.Single(l => l.Party == "Gone");

            Assert.Equal(0, gone.Open);
            Assert.Equal(0, gone.Closed);
            Assert.Equal("-1", gone.DeltaText);
        }

        [Fact]
        public void A_party_that_is_new_this_week_reads_as_having_been_at_zero()
        {
            var lastWeek = ClashSnapshot.Of(
                Board(("Mechanical", new[] { FindingStatus.Active })), takenUtc: LastWeek);

            var ledger = PartyLedger.Of(
                Board(("Mechanical", new[] { FindingStatus.Active }),
                      ("Fire", new[] { FindingStatus.New, FindingStatus.New })).Groups,
                lastWeek);

            var fire = ledger.Lines.Single(l => l.Party == "Fire");

            Assert.Equal(0, fire.PreviousOpen);
            Assert.Equal("+2", fire.DeltaText);
            Assert.Contains("2 more open", ledger.Explain());
        }

        [Fact]
        public void The_busiest_party_is_first_because_that_is_the_one_being_looked_for()
        {
            var ledger = PartyLedger.Of(Board(
                ("Quiet", new[] { FindingStatus.Active }),
                ("Busy", new[] { FindingStatus.Active, FindingStatus.Active, FindingStatus.Active })).Groups);

            Assert.Equal("Busy", ledger.Lines[0].Party);
        }

        [Fact]
        public void An_empty_board_is_a_readable_answer_rather_than_an_error()
        {
            var ledger = PartyLedger.Of(null);

            Assert.Empty(ledger.Lines);
            Assert.Equal("nothing on the board", ledger.Explain());
        }
    }
}
