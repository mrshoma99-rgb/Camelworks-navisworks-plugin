using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ClashExchangeTests
    {
        private static ClashItem Item(string name, FindingStatus status = FindingStatus.Active)
        {
            var item = ClashFixture.Item(name);

            item.Name = name;
            item.Status = status;

            return item;
        }

        /// <summary>One party's return: the same clashes, answered their way, through a real file.</summary>
        private static ExchangePack Pack(string party, params ClashItem[] answered)
        {
            var written = ClashBcf.Write(answered);

            return new ExchangePack(party, ClashFile.Read(written.Bytes), party + ".bcfzip");
        }

        /// <summary>The same clash, as one party answered it.</summary>
        private static ClashItem Answered(ClashItem board, FindingStatus status,
                                          string? party = null, string? comment = null)
        {
            var copy = new ClashItem(board.Key, board.TestName)
            {
                Name = board.Name,
                Status = status,
                AssignedTo = party,
                X = board.X, Y = board.Y, Z = board.Z,
                ModelA = board.ModelA, ModelB = board.ModelB,
                Level = board.Level,
            };

            if (comment != null)
                copy.Comments.Add(new ClashComment("A reviewer", 637_000_000_000_000_000L, comment));

            return copy;
        }

        // -----------------------------------------------------------------------------------
        // Reading several packs at once
        // -----------------------------------------------------------------------------------

        [Fact]
        public void Several_packs_land_on_one_row_per_clash()
        {
            var one = Item("a");
            var two = Item("b");
            var board = new[] { one, two };

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(two, FindingStatus.Reviewed)),
            }, board);

            Assert.Equal(2, reading.Rows.Count);
            Assert.Equal(0, reading.Disputed);
            Assert.All(reading.Rows, row => Assert.Single(row.Answers));
        }

        [Fact]
        public void Two_parties_answering_one_clash_the_same_way_is_not_a_dispute()
        {
            var one = Item("a");

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(one, FindingStatus.Resolved)),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            Assert.False(row.IsDisputed);
            Assert.Equal(2, row.Answers.Count);
            Assert.Equal(FindingStatus.Resolved, row.Would);
        }

        [Fact]
        public void A_pack_naming_elements_this_board_does_not_have_is_reported_not_dropped()
        {
            // A party working against last month's models is a thing the coordinator has to be
            // told about, so it is counted rather than quietly ignored.
            var mine = Item("a");
            var theirs = Item("z");

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(theirs, FindingStatus.Resolved)) },
                new[] { mine });

            Assert.Empty(reading.Rows);
            Assert.Single(reading.Strangers);
            Assert.Contains("matching no clash", reading.Explain(), System.StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------------------
        // Two people disagreeing
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_disputed_clash_is_not_applied_until_somebody_picks_a_side()
        {
            // They were sent the same pack at the same time and neither saw the other's answer.
            // Picking one silently is how a clash somebody closed re-opens a week later with
            // nobody able to say who did it.
            var one = Item("a");

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(one, FindingStatus.Reviewed)),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            Assert.True(row.IsDisputed);
            Assert.False(row.Take);
            Assert.Null(row.Settled);
            Assert.Equal(1, reading.Undecided);
            Assert.Equal(FindingStatus.Active, row.Would);

            var applied = ClashExchange.Apply(reading);

            Assert.Equal(0, applied.Moved);
            Assert.Equal(FindingStatus.Active, one.Status);
        }

        [Fact]
        public void Choosing_a_side_settles_the_row_and_applies_it()
        {
            var one = Item("a");

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(one, FindingStatus.Reviewed)),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            row.Chosen = "Structure";
            row.Take = true;

            Assert.Equal(FindingStatus.Reviewed, row.Would);
            Assert.Equal(0, reading.Undecided);

            var applied = ClashExchange.Apply(reading);

            Assert.Equal(1, applied.Moved);
            Assert.Equal(FindingStatus.Reviewed, one.Status);
        }

        // -----------------------------------------------------------------------------------
        // What applying does
        // -----------------------------------------------------------------------------------

        [Fact]
        public void A_stale_answer_cannot_undo_a_decision_the_board_already_holds()
        {
            // Two edits neither author knew about. Promote-only is what stops a reviewer's older
            // "Open" undoing an approval made since the pack went out.
            var one = Item("a", FindingStatus.Approved);

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Active)) },
                new[] { one });

            ClashExchange.Apply(reading);

            Assert.Equal(FindingStatus.Approved, one.Status);
        }

        [Fact]
        public void Comments_come_back_from_every_party_not_only_the_one_who_won()
        {
            // A comment is somebody's reasoning. Dropping the losing side's throws away the thing
            // that would have settled the argument.
            var one = Item("a");

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved, comment: "Duct moved.")),
                Pack("Structure", Answered(one, FindingStatus.Reviewed, comment: "Beam is fixed.")),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            row.Chosen = "Mechanical";
            row.Take = true;

            var applied = ClashExchange.Apply(reading);

            Assert.Equal(2, applied.Comments);
            Assert.Equal(2, one.Comments.Count);
        }

        [Fact]
        public void The_same_pack_applied_twice_does_not_double_its_comments()
        {
            var one = Item("a");

            var packs = new List<ExchangePack>
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved, comment: "Duct moved.")),
            };

            ClashExchange.Apply(ClashExchange.Read(packs, new[] { one }));
            ClashExchange.Apply(ClashExchange.Read(packs, new[] { one }));

            Assert.Single(one.Comments);
        }

        [Fact]
        public void A_row_can_be_left_out_by_hand()
        {
            var one = Item("a");

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Resolved)) },
                new[] { one });

            Assert.Single(reading.Rows).Take = false;

            var applied = ClashExchange.Apply(reading);

            Assert.Equal(0, applied.Moved);
            Assert.Equal(1, applied.Held);
            Assert.Equal(FindingStatus.Active, one.Status);
        }

        [Fact]
        public void An_assignment_comes_back_with_the_status()
        {
            var one = Item("a");

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Reviewed, "Structure")) },
                new[] { one });

            var applied = ClashExchange.Apply(reading);

            Assert.Equal(1, applied.Assigned);
            Assert.Equal("Structure", one.AssignedTo);
        }

        [Fact]
        public void Nothing_to_read_says_so_rather_than_looking_empty()
        {
            var reading = ClashExchange.Read(null, new[] { Item("a") });

            Assert.Empty(reading.Rows);
            Assert.Contains("answer no clash", reading.Explain(),
                            System.StringComparison.Ordinal);
        }
    }
}
