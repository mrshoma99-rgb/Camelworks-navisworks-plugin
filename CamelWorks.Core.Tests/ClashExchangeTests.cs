using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Project;
using CamelWorks.Core.Testing;
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

        // -----------------------------------------------------------------------------------
        // Where an applied answer is written down
        // -----------------------------------------------------------------------------------

        /// <summary>The finding the register holds for one board clash.</summary>
        private static Finding Recorded(ClashItem item)
        {
            var finding = Finding.Create(FindingSource.Clash, item.TestName, item.Name,
                                         new[] { item.Key.A, item.Key.B },
                                         ClashFindings.CellOf(item.Key));

            finding.TopicGuid = ClashBcf.TopicGuidFor(item.Key);

            return finding;
        }

        [Fact]
        public void An_applied_answer_is_in_the_register_after_it_is_closed_and_opened_again()
        {
            // The Manage tab works on findings and never sees these items, so an answer written
            // onto the board alone was gone the moment the tab was left.
            var one = Item("a");
            var two = Item("b");

            var fs = new InMemoryFileSystem();
            var project = ProjectStore.Open(fs, @"C:\job\Tower.nwf", null);
            var store = FindingStore.Open(fs, project);

            store.Findings.Put(Recorded(one));
            store.Findings.Put(Recorded(two));

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved, "Mechanical", "Duct moved.")),
                Pack("Structure", Answered(two, FindingStatus.Reviewed, "Structure", "Beam is fixed.")),
            }, new[] { one, two });

            var applied = ClashExchange.Apply(reading, store.Findings);

            Assert.Equal(2, applied.Moved);
            Assert.Equal(2, applied.Assigned);
            Assert.Equal(2, applied.Comments);
            Assert.Equal(0, applied.Missed);

            Assert.True(store.Save());

            var reopened = FindingStore.Open(fs, project).Findings;
            var kept = reopened.Find(Recorded(one).Id);

            Assert.NotNull(kept);
            Assert.Equal(FindingStatus.Resolved, kept!.Status);
            Assert.Equal("Mechanical", kept.AssignedTo);
            Assert.Equal("Duct moved.", Assert.Single(kept.Comments).Body);

            Assert.Equal(FindingStatus.Reviewed, reopened.Find(Recorded(two).Id)!.Status);
        }

        [Fact]
        public void The_same_pack_applied_twice_does_not_double_the_registers_comments()
        {
            var one = Item("a");

            var register = new FindingRegister();
            register.Put(Recorded(one));

            var packs = new List<ExchangePack>
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved, comment: "Duct moved.")),
            };

            ClashExchange.Apply(ClashExchange.Read(packs, new[] { one }), register);
            var again = ClashExchange.Apply(ClashExchange.Read(packs, new[] { one }), register);

            Assert.Equal(0, again.Comments);
            Assert.Single(register.Find(Recorded(one).Id)!.Comments);
        }

        [Fact]
        public void An_answer_to_a_clash_the_register_does_not_hold_is_counted_not_reported_as_applied()
        {
            // A screen that said "14 moved" over a register that took none of them is the failure
            // this counts.
            var one = Item("a");

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Resolved)) },
                new[] { one });

            var applied = ClashExchange.Apply(reading, new FindingRegister());

            Assert.Equal(0, applied.Moved);
            Assert.Equal(1, applied.Missed);
            Assert.Contains("the register does not hold", applied.Explain(),
                            System.StringComparison.Ordinal);
        }

        [Fact]
        public void A_stale_answer_cannot_undo_a_decision_the_register_already_holds()
        {
            var one = Item("a");

            var register = new FindingRegister();
            var finding = Recorded(one);

            finding.Status = FindingStatus.Approved;
            register.Put(finding);

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Active)) },
                new[] { one });

            ClashExchange.Apply(reading, register);

            Assert.Equal(FindingStatus.Approved, register.Find(finding.Id)!.Status);
        }

        [Fact]
        public void What_the_exchange_writes_is_held_until_the_host_takes_it()
        {
            // A field written and not held is reverted by the next press of "Read the results":
            // the fresh read carries the host's value and the merge takes it.
            var one = Item("a");

            var register = new FindingRegister();
            register.Put(Recorded(one));

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Reviewed, "Structure",
                                                    "Beam is fixed.")) },
                new[] { one });

            ClashExchange.Apply(reading, register);

            var kept = register.Find(Recorded(one).Id)!;

            Assert.Contains(FindingField.Status, kept.Pending);
            Assert.Contains(FindingField.AssignedTo, kept.Pending);
            Assert.Contains(FindingField.Comments, kept.Pending);
            Assert.False(kept.HostHasIt);
        }

        [Fact]
        public void Two_clashes_on_one_pair_cannot_both_land_on_one_finding()
        {
            // A duct that crosses one beam twice is two clashes. A neighbouring-cell match that
            // took the finding the other row needed exactly would write one party's answer onto
            // the other conflict, and report both as applied.
            var here = ClashFixture.Item("a", x: 0, y: 0, z: 0);
            var there = ClashFixture.Item("a", x: 12, y: 0, z: 0);

            Assert.NotEqual(here.Key, there.Key);

            var register = new FindingRegister();
            register.Put(Recorded(there));

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(here, FindingStatus.Resolved)),
                Pack("Structure", Answered(there, FindingStatus.Reviewed)),
            }, new[] { here, there });

            var applied = ClashExchange.Apply(reading, register);

            // The exact match wins the one finding there is; the other row is a miss, not a
            // second write onto the same record.
            Assert.Equal(1, applied.Missed);
            Assert.Equal(FindingStatus.Reviewed, register.Find(Recorded(there).Id)!.Status);
        }

        [Fact]
        public void A_finding_stamped_with_another_tools_topic_guid_is_still_reachable()
        {
            // Syncing a pack on the Manage tab first stamps the returning file's GUID onto the
            // finding. Indexed only under that, it was unreachable from the board.
            var one = Item("a");

            var finding = Recorded(one);
            finding.TopicGuid = "11111111-2222-3333-4444-555555555555";

            var register = new FindingRegister();
            register.Put(finding);

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Resolved)) },
                new[] { one });

            var applied = ClashExchange.Apply(reading, register);

            Assert.Equal(0, applied.Missed);
            Assert.Equal(FindingStatus.Resolved, register.Find(finding.Id)!.Status);
        }

        [Fact]
        public void Where_two_findings_share_a_topic_guid_the_answer_lands_on_the_one_this_test_found()
        {
            // Two overlapping tests reporting one pair at one point make two findings under one
            // derived GUID. Picking at random left one of them holding a stale status and
            // reported it as applied.
            var one = Item("a");

            var mine = Recorded(one);
            var other = Finding.Create(FindingSource.Clash, "Another test", one.Name,
                                       new[] { one.Key.A, one.Key.B },
                                       ClashFindings.CellOf(one.Key), identity: "test:other");

            var register = new FindingRegister();
            register.Put(other);
            register.Put(mine);

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Resolved)) },
                new[] { one });

            ClashExchange.Apply(reading, register);

            Assert.Equal(FindingStatus.Resolved, register.Find(mine.Id)!.Status);
            Assert.Equal(FindingStatus.New, register.Find(other.Id)!.Status);
        }

        [Fact]
        public void The_reviewers_own_fields_come_back_with_the_status()
        {
            // A due date and a priority set by the reviewer used to go nowhere, which is
            // discovered at the coordination meeting.
            var one = Item("a");

            var register = new FindingRegister();
            register.Put(Recorded(one));

            var answered = Answered(one, FindingStatus.Reviewed);

            answered.Review = new ClashAside(ClashBcf.TopicGuidFor(one.Key))
            {
                Priority = "High",
                DueTicks = new System.DateTime(2026, 4, 1, 0, 0, 0, System.DateTimeKind.Utc).Ticks,
            };

            ClashExchange.Apply(
                ClashExchange.Read(new[] { Pack("Mechanical", answered) }, new[] { one }),
                register);

            var kept = register.Find(Recorded(one).Id)!;

            Assert.Equal("High", kept.Priority);
            Assert.NotNull(kept.DueDayNumber);
        }

        [Fact]
        public void An_answer_the_register_cannot_take_still_reaches_the_board()
        {
            // An answer nobody can see on screen is worse than one that is only on screen, and
            // Missed is what says which happened.
            var one = Item("a");

            var reading = ClashExchange.Read(
                new[] { Pack("Mechanical", Answered(one, FindingStatus.Resolved)) },
                new[] { one });

            var applied = ClashExchange.Apply(reading, new FindingRegister());

            Assert.Equal(1, applied.Missed);
            Assert.Equal(FindingStatus.Resolved, one.Status);
        }

        [Fact]
        public void Three_parties_disagreeing_keeps_every_answer_readable()
        {
            // Three parties disagreeing is ordinary, and a resolution that hides what the other
            // two said is one nobody can check.
            var one = Item("a");

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(one, FindingStatus.Reviewed)),
                Pack("Electrical", Answered(one, FindingStatus.Approved)),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            Assert.True(row.IsThreeWay);
            Assert.Equal(3, row.Parties);
            Assert.Equal(1, reading.ThreeWay);

            row.Chosen = "Structure";
            row.Take = true;

            Assert.Equal(2, row.Overruled.Count);
            Assert.DoesNotContain(row.Overruled, say => say.Party == "Structure");
        }

        [Fact]
        public void The_coordinators_note_is_written_onto_the_finding()
        {
            var one = Item("a");

            var register = new FindingRegister();
            register.Put(Recorded(one));

            var reading = ClashExchange.Read(new[]
            {
                Pack("Mechanical", Answered(one, FindingStatus.Resolved)),
                Pack("Structure", Answered(one, FindingStatus.Reviewed)),
            }, new[] { one });

            var row = Assert.Single(reading.Rows);

            row.Chosen = "Structure";
            row.Take = true;
            row.Note = "Beam is fixed; mechanical to re-route. Agreed Thursday.";

            ClashExchange.Apply(reading, register);

            var kept = register.Find(Recorded(one).Id)!;

            Assert.Contains(kept.Comments,
                            one2 => one2.Body.StartsWith("Beam is fixed", System.StringComparison.Ordinal));
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
