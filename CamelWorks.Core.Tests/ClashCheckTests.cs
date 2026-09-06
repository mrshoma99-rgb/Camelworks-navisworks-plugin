using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Issues;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Clash detection as a check, over <see cref="FakeClashSource"/>.
    ///
    /// The board that draws these is in CamelWorks.UI, which has no test project and needs a
    /// licensed seat to run at all, so everything that can be asserted about the clash pass is
    /// asserted here: what the read produces, what it speaks for, what the file leg does not have,
    /// and that a file board and the document do not end up holding one clash twice.
    /// </summary>
    public class ClashCheckTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\p\ARCH.nwc"), @"C:\p\ARCH.nwc",
                                    "L03", name);

        /// <summary>Two clash tests reporting one physical conflict, which is one topic guid.</summary>
        private static FakeClashSource Twice() =>
            new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .With("L03 MEP v STR, rerun", "Weekly", "t2")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3);

        private static FakeClashSource Seat() =>
            new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .Finding(Key("Duct 2"), Key("Beam 2"), 4, 5, 6, "Reviewed")
                .With("L04 MEP v STR", "Weekly", "t2")
                .Finding(Key("Duct 3"), Key("Beam 3"), 7, 8, 9);

        // ---------------------------------------------------------------------------------
        // The read
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_read_reports_one_run_per_host_test_with_the_hosts_own_wording()
        {
            var read = new ClashCheck(Seat()).Read(null, null);

            Assert.Equal(2, read.Runs.Count);

            var first = read.Runs[0];

            Assert.Equal("t1", first.Id);
            Assert.Equal("L03 MEP v STR", first.Name);
            Assert.Equal("Weekly", first.Folder);
            Assert.Equal("Hard", first.Kind);
            Assert.Equal("25 mm", first.Threshold);
            Assert.Equal(2, first.ResultCount);

            // A test the host owns is not ours to claim, whatever the board lets somebody do to it.
            Assert.False(first.IsOurs);
        }

        [Fact]
        public void A_test_whose_sides_name_no_sets_says_so_rather_than_guessing_a_discipline()
        {
            var run = new ClashCheck(Seat()).Read(null, null).Runs[0];

            Assert.Empty(run.Trades);
            Assert.Equal("two selections this test does not name", run.Between);
        }

        [Fact]
        public void The_read_produces_the_findings_the_existing_producer_produces()
        {
            var seat = Seat();

            var mine = new ClashCheck(seat).Read(null, null).Findings;

            var theirs = seat.Tests()
                .SelectMany(test => ClashFindings.ForTest(test.Id, test.Name, seat.Results(test.Id)))
                .ToList();

            Assert.Equal(theirs.Count, mine.Count);

            var byId = mine.ToDictionary(one => one.Id, StringComparer.Ordinal);

            foreach (var one in theirs)
            {
                // The same finding, and then the same finding in every field the board draws.
                // The id set alone passes with mangled titles, lost statuses and no facts at all.
                var same = byId[one.Id];

                Assert.Equal(one.Title, same.Title);
                Assert.Equal(one.Rule, same.Rule);
                Assert.Equal(one.Status, same.Status);
                Assert.Equal(one.AssignedTo, same.AssignedTo);
                Assert.Equal(one.Group, same.Group);
                Assert.Equal(one.Detail, same.Detail);
                Assert.Equal(one.TopicGuid, same.TopicGuid);
                Assert.Equal(one.X, same.X);
                Assert.Equal(one.Y, same.Y);
                Assert.Equal(one.Z, same.Z);
                Assert.Equal(one.Elements, same.Elements);
                Assert.Equal(one.HostHasIt, same.HostHasIt);
                Assert.True(same.HostHasIt);

                foreach (var fact in one.Facts)
                    Assert.Equal(fact.Value, same.Facts[fact.Key]);

                // And what the seam adds on top: the run the finding belongs to, which is the
                // fact the rail, the scope and every tally are derived from.
                Assert.Equal(new[] { one.Facts[ClashFindings.HostTest] }, CheckFacts.RunsOf(same));
            }
        }

        [Fact]
        public void Every_finding_names_the_run_it_came_from()
        {
            var read = new ClashCheck(Seat()).Read(null, null);

            Assert.Equal(new[] { "t1" }, CheckFacts.RunsOf(read.Findings[0]));
            Assert.Equal(2, read.CountFor("t1"));
            Assert.Equal(1, read.CountFor("t2"));
        }

        [Fact]
        public void Reading_one_test_reads_that_test_alone()
        {
            var read = new ClashCheck(Seat()).Read(new[] { "t2" }, null);

            Assert.Equal("t2", Assert.Single(read.Runs).Id);
            Assert.Single(read.Findings);
        }

        [Fact]
        public void Reading_a_test_the_host_no_longer_has_is_a_note_and_not_an_exception()
        {
            var read = new ClashCheck(Seat()).Read(new[] { "t9" }, null);

            Assert.Empty(read.Runs);
            Assert.Contains(read.Notes, note => note.Contains("t9", StringComparison.Ordinal));
        }

        [Fact]
        public void A_seat_with_no_clash_engine_reads_nothing_and_says_why()
        {
            var seat = Seat();
            seat.IsAvailable = false;

            var check = new ClashCheck(seat, unavailable: "This edition has no clash engine.");
            var read = check.Read(null, null);

            Assert.False(check.IsAvailable);
            Assert.Empty(read.Findings);
            Assert.Contains("no clash engine", Assert.Single(read.Notes), StringComparison.Ordinal);
        }

        [Fact]
        public void The_host_comment_count_rides_the_finding_rather_than_costing_a_call_per_row()
        {
            var seat = Seat();

            seat.Comment("t1", seat.Results("t1").Select(one => one.Id).Take(1).ToList(),
                         "Duct drops here", "Ana");

            var read = new ClashCheck(seat).Read(null, null);
            var finding = read.Findings.First(one => ClashCheck.CommentsOn(one) > 0);

            Assert.Equal(1, ClashCheck.CommentsOn(finding));
            Assert.Equal("Duct drops here", ClashCheck.LastCommentOn(finding));
        }

        // ---------------------------------------------------------------------------------
        // The scope
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Reading_one_test_does_not_mark_the_other_tests_findings_absent()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var register = new FindingRegister();

            var all = check.Read(null, null);

            register.Observe(FindingSource.Clash, all.Findings, 1, scope: all.Scope);

            var one = check.Read(new[] { "t1" }, null);

            register.Observe(FindingSource.Clash, one.Findings, 2, scope: one.Scope);

            Assert.All(register.From(FindingSource.Clash), finding => Assert.True(finding.IsPresent));

            // The scope is not simply false: it covers the test that was read and nothing else.
            // Hard-wired to false this assertion is what fails, and the one above is not.
            Assert.All(all.Findings.Where(f => CheckFacts.IsIn(f, "t1")),
                       finding => Assert.True(one.Scope(finding)));

            Assert.All(all.Findings.Where(f => CheckFacts.IsIn(f, "t2")),
                       finding => Assert.False(one.Scope(finding)));

            // So a result the read did cover, and that has since gone, is marked absent by it.
            var gone = all.Findings.First(f => CheckFacts.IsIn(f, "t1"));

            Resolved(seat, "t1");

            var again = check.Read(new[] { "t1" }, null);

            register.Observe(FindingSource.Clash, again.Findings, 3, scope: again.Scope);

            Assert.False(register.Find(gone.Id)!.IsPresent);
            Assert.All(register.From(FindingSource.Clash).Where(f => ClashCheck.TestOf(f) == "t2"),
                       finding => Assert.True(finding.IsPresent));
        }

        [Fact]
        public void A_result_the_host_has_stopped_reporting_is_marked_absent()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var register = new FindingRegister();

            var first = check.Read(null, null);

            register.Observe(FindingSource.Clash, first.Findings, 1, scope: first.Scope);

            var gone = first.Findings.First(one => CheckFacts.IsIn(one, "t2"));

            Resolved(seat, "t2");

            var second = check.Read(null, null);

            register.Observe(FindingSource.Clash, second.Findings, 2, scope: second.Scope);

            Assert.False(register.Find(gone.Id)!.IsPresent);
        }

        [Fact]
        public void A_finding_written_before_run_membership_was_a_fact_is_still_in_scope()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var register = new FindingRegister();

            var first = check.Read(null, null);

            // As an earlier version of this tool wrote them: the host test fact and no run fact.
            foreach (var finding in first.Findings) finding.Facts.Remove(CheckFacts.Runs);

            register.Observe(FindingSource.Clash, first.Findings, 1, scope: first.Scope);

            var gone = first.Findings.First(one => ClashCheck.TestOf(one) == "t2");

            Resolved(seat, "t2");

            var second = check.Read(null, null);

            register.Observe(FindingSource.Clash, second.Findings, 2, scope: second.Scope);

            Assert.False(register.Find(gone.Id)!.IsPresent);
        }

        // ---------------------------------------------------------------------------------
        // The host echo, and what it does with a refusal
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_status_is_echoed_into_the_test_the_finding_came_from()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(null, null);

            Assert.True(check.HostEcho!.SetStatus(read.Findings.ToList(), FindingStatus.Approved));

            Assert.All(seat.Results("t1"), result => Assert.Equal("Approved", result.HostStatus));
            Assert.All(seat.Results("t2"), result => Assert.Equal("Approved", result.HostStatus));
        }

        [Fact]
        public void A_document_that_refuses_the_write_says_so_rather_than_throwing()
        {
            var seat = Seat();
            seat.Refuses = "This document is read-only.";

            var check = new ClashCheck(seat);
            var read = check.Read(null, null);

            Assert.False(check.HostEcho!.SetStatus(read.Findings.ToList(), FindingStatus.Approved));
            Assert.Equal("This document is read-only.", check.HostEcho.LastProblem);
        }

        [Fact]
        public void An_echo_over_findings_with_no_host_handle_writes_nothing_and_says_no()
        {
            var check = new ClashCheck(Seat());

            var stray = Finding.Create(FindingSource.Clash, "By hand", "No host", Array.Empty<ElementKey>());

            Assert.False(check.HostEcho!.SetStatus(new[] { stray }, FindingStatus.Approved));
        }

        [Fact]
        public void A_renumber_reaches_the_host_as_one_write_per_test()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);

            // In the board's order, which here is the reverse of the document's. Handed over in
            // document order this cannot tell a renumber that follows the board from one that
            // follows the host, and the two do not agree.
            var findings = read.Findings.Reverse().ToList();
            var titles = new[] { "Corridor1", "Corridor2" };

            Assert.True(check.HostEcho!.SetTitle(findings, titles));

            // One write per test, which is the point of recognising a numbering at all: the
            // alternative is a host call per result, and a test of nine thousand results is nine
            // thousand calls that each locate the test again.
            Assert.Single(seat.Log.Where(said => said.StartsWith("RenamePicked", StringComparison.Ordinal)));

            // And the numbers count along the host's order, not the board's. The host verb takes
            // a stem and numbers what it is given in its own order, so a board sorted by depth or
            // by assignee gets a numbering that does not follow what is on screen. Pinned here
            // rather than left to be discovered: docs/apps/clash.md says the same, and the fix,
            // if the disagreement ever matters, is a rename per result and its cost.
            Assert.Equal(new[] { "Corridor1", "Corridor2" },
                         seat.Results("t1").Select(one => one.Name));

            // Said the other way round: the result the board listed first is called Corridor2.
            Assert.Equal("Corridor2",
                         seat.Results("t1")
                             .Single(one => one.Id == ClashCheck.ResultOf(findings[0])).Name);
        }

        [Fact]
        public void A_rename_that_is_not_a_numbering_names_each_result_exactly()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);

            var findings = read.Findings.ToList();

            Assert.True(check.HostEcho!.SetTitle(findings, new[] { "Riser", "Corridor duct" }));

            Assert.Equal(new[] { "Riser", "Corridor duct" },
                         seat.Results("t1").Select(one => one.Name));
        }

        // ---------------------------------------------------------------------------------
        // The capabilities, host against file
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_host_check_offers_every_capability_and_each_one_reaches_the_document()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);
            var findings = read.Findings.ToList();

            // Offered, and then asked: a capability that is not null and does nothing is the
            // failure this pass exists to prevent, and no null check can see it.
            Assert.True(check.HostEcho!.SetStatus(findings, FindingStatus.Approved));
            Assert.All(seat.Results("t1"), one => Assert.Equal("Approved", one.HostStatus));

            Assert.True(check.Grouping!.Regroup(new[]
            {
                new ClashGroupPath(new[] { "Level 3" },
                                   findings.Select(one => ClashCheck.ResultOf(one)!).ToList()),
            }).Took);

            Assert.All(seat.Results("t1"), one => Assert.Equal("Level 3", one.GroupName));

            Assert.True(check.Runner!.CanRun);
            Assert.True(check.Runner.Run("t1", null));

            Assert.True(check.Naming!.Rename("t1", "L03 renamed"));
            Assert.Equal("L03 renamed", seat.Tests().Single(one => one.Id == "t1").Name);

            Assert.NotNull(check.Properties!.ValuesFor(findings[0], 0, "Item", "Name"));

            Assert.Equal(2, check.Geometry!.Select(findings.Take(1).ToList(), add: false));
        }

        [Fact]
        public void The_file_check_offers_geometry_and_nothing_else()
        {
            var check = File(Seat());
            var findings = check.Read(null, null).Findings.ToList();

            Assert.Null(check.HostEcho);
            Assert.Null(check.Grouping);
            Assert.Null(check.Runner);
            Assert.Null(check.Naming);
            Assert.Null(check.Properties);

            // And the one it does offer is the one that answers: a file carries the elements, so
            // the board can point at them without an engine to ask.
            Assert.NotNull(check.Geometry);
            Assert.Null(check.Geometry!.Select(findings, add: false));
            Assert.NotEmpty(check.Geometry.Locate(findings));
        }

        [Fact]
        public void Selecting_from_the_document_goes_through_the_engines_own_handles()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);

            var selected = check.Geometry!.Select(read.Findings.ToList(), add: false);

            // What the engine was asked, rather than what it answered: a route that resolved the
            // element keys itself would return the same number and index the federation to get it.
            Assert.Equal(new[] { "SelectResults t1" },
                         seat.Log.Where(said => !said.StartsWith("Tests", StringComparison.Ordinal)
                                                && !said.StartsWith("Results", StringComparison.Ordinal)));
            Assert.Equal(seat.Results("t1").Select(one => one.Id), seat.Selected);
            Assert.Equal(4, selected);

            // And nothing to locate, which is what tells the caller to select rather than search.
            Assert.Empty(check.Geometry.Locate(read.Findings.ToList()));
        }

        [Fact]
        public void Selecting_from_a_file_is_refused_so_the_caller_falls_back_to_locating()
        {
            var check = File(Seat());
            var read = check.Read(null, null);

            Assert.Null(check.Geometry!.Select(read.Findings.ToList(), add: false));
            Assert.NotEmpty(check.Geometry.Locate(read.Findings.ToList()));
        }

        [Fact]
        public void The_run_a_check_offers_for_a_file_is_named_after_the_files_own_test()
        {
            var runs = File(Seat()).Runs();

            Assert.Contains(runs, run => run.Name == "L03 MEP v STR");
            Assert.All(runs, run => Assert.False(run.IsOurs));
        }

        // ---------------------------------------------------------------------------------
        // A file is a partial view of a run, not a run
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_file_read_marks_nothing_absent()
        {
            var seat = Seat();
            var register = new FindingRegister();

            var host = new ClashCheck(seat).Read(null, null);

            register.Observe(FindingSource.Clash, host.Findings, 1, scope: host.Scope);

            // A file holding one of the three results — one party's slice of the round.
            var one = seat.Results("t1").Take(1)
                .Select(result => Item(result, "L03 MEP v STR")).ToList();

            var file = new FileCheck(ClashFile.Read(ClashFixture.Written(one.ToArray()).Bytes));
            var read = file.Read(null, null);

            var held = register.Count;

            // Through the fold, as the board reads a file: observing without it leaves the very
            // duplicate the fold exists to prevent, so this would pass on a read that doubled
            // every result the file carried.
            var unheld = FileCheck.Fold(register, read.Findings, out _);

            register.Observe(FindingSource.Clash, unheld, 2, scope: read.Scope);

            Assert.All(register.From(FindingSource.Clash), finding => Assert.True(finding.IsPresent));
            Assert.Equal(held, register.Count);
        }

        [Fact]
        public void A_file_read_folds_onto_the_findings_the_document_already_raised()
        {
            var seat = Seat();
            var register = new FindingRegister();

            var host = new ClashCheck(seat).Read(null, null);

            register.Observe(FindingSource.Clash, host.Findings, 1, scope: host.Scope);

            var held = register.Count;

            var marked = seat.Results("t1").Take(1)
                .Select(result => Item(result, "L03 MEP v STR")).ToList();

            marked[0].Status = FindingStatus.Approved;

            var file = new FileCheck(ClashFile.Read(ClashFixture.Written(marked.ToArray()).Bytes));
            var read = file.Read(null, null);

            var left = FileCheck.Fold(register, read.Findings, out var folded);

            register.Observe(FindingSource.Clash, left, 2, scope: read.Scope);

            Assert.Equal(1, folded);
            Assert.Empty(left);
            Assert.Equal(held, register.Count);
            Assert.Contains(register.From(FindingSource.Clash),
                            finding => finding.Status == FindingStatus.Approved);
        }

        [Fact]
        public void A_host_read_takes_over_what_a_file_board_raised_rather_than_doubling_it()
        {
            var seat = Seat();
            var register = new FindingRegister();

            // A seat with no clash engine: the board was built from the file alone.
            var marked = seat.Results("t1").Take(1)
                .Select(result => Item(result, "L03 MEP v STR")).ToList();

            marked[0].Status = FindingStatus.Reviewed;
            marked[0].AssignedTo = "Ana";

            var file = new FileCheck(ClashFile.Read(ClashFixture.Written(marked.ToArray()).Bytes));
            var fromFile = file.Read(null, null);

            register.Observe(FindingSource.Clash, fromFile.Findings, 1, scope: fromFile.Scope);

            Assert.Equal(1, register.Count);

            // The Manage seat opens the model the file was cut from.
            var host = new ClashCheck(seat).Read(null, null);

            FileCheck.Adopt(register, host.Findings, out var adopted);

            register.Observe(FindingSource.Clash, host.Findings, 2, scope: host.Scope);

            Assert.Equal(1, adopted);
            Assert.Equal(3, register.Count);

            var landed = register.From(FindingSource.Clash)
                .Single(finding => finding.AssignedTo == "Ana");

            Assert.Equal(FindingStatus.Reviewed, landed.Status);
            Assert.Equal("t1", ClashCheck.TestOf(landed));
            Assert.True(landed.HostHasIt);
        }

        [Fact]
        public void A_host_read_over_findings_the_document_raised_adopts_nothing()
        {
            var seat = Seat();
            var register = new FindingRegister();

            var host = new ClashCheck(seat).Read(null, null);

            // A held finding for the same clash under another id, that the document reported: the
            // shape the id guard alone does not see. Adopting it would remove a finding the board
            // draws and copy its decisions onto a second one.
            var raised = new FileCheck(
                    ClashFile.Read(ClashFixture.Written(
                        seat.Results("t1").Take(1).Select(r => Item(r, "L03 MEP v STR")).ToArray()).Bytes))
                .Read(null, null).Findings[0];

            raised.AssignedTo = "Ana";
            raised.HostHasIt = true;

            register.Observe(FindingSource.Clash, new[] { raised }, 1, scope: f => false);

            // The premise: one topic guid, two ids. Without this the test proves nothing.
            Assert.Contains(host.Findings,
                            one => one.TopicGuid == raised.TopicGuid
                                   && !string.Equals(one.Id, raised.Id, StringComparison.Ordinal));

            var moved = FileCheck.Adopt(register, host.Findings, out var adopted);

            Assert.Equal(0, adopted);
            Assert.Empty(moved);
            Assert.NotNull(register.Find(raised.Id));
            Assert.Equal("Ana", register.Find(raised.Id)!.AssignedTo);
        }

        // ---------------------------------------------------------------------------------
        // Two findings, one topic guid
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// One physical conflict reported by two runs.
        ///
        /// The topic guid is derived from the element pair and the cell they meet in and not from
        /// the run, so these two findings have different ids and the same topic. It is the shape
        /// both directions of the fold have to claim once.
        /// </summary>
        /// <param name="results">The result both runs report.</param>
        private static (Finding First, Finding Second) Both(IReadOnlyList<ClashResultInfo> results)
        {
            var first = ClashFindings.ForTest("L03 MEP v STR", "L03 MEP v STR", results)[0];
            var second = ClashFindings.ForTest("L03 MEP v STR again", "L03 MEP v STR again",
                                               results)[0];

            CheckFacts.SetRuns(first, new[] { "L03 MEP v STR" });
            CheckFacts.SetRuns(second, new[] { "L03 MEP v STR again" });

            first.HostHasIt = false;
            second.HostHasIt = false;

            return (first, second);
        }

        [Fact]
        public void A_fold_lands_two_findings_sharing_a_topic_on_one_held_finding_at_most()
        {
            var seat = Seat();
            var register = new FindingRegister();

            var host = new ClashCheck(seat).Read(new[] { "t1" }, null);

            register.Observe(FindingSource.Clash, host.Findings, 1, scope: host.Scope);

            var held = register.Count;

            // A file carrying the same clash under two test names: two findings, two ids, one
            // topic guid.
            var (first, second) = Both(seat.Results("t1").Take(1).ToList());

            Assert.Equal(first.TopicGuid, second.TopicGuid);
            Assert.NotEqual(first.Id, second.Id);

            var left = FileCheck.Fold(register, new[] { first, second }, out var folded);

            register.Observe(FindingSource.Clash, left, 2, scope: _ => false);

            // One folded, and the other returned rather than dropped: it is neither drawn as
            // folded nor observed if the fold takes the same held finding twice.
            Assert.Equal(1, folded);
            Assert.Single(left);
            Assert.Equal(held + 1, register.Count);
        }

        [Fact]
        public void An_adopt_claims_a_file_born_finding_once_and_leaves_the_second_alone()
        {
            var seat = Twice();
            var register = new FindingRegister();

            // One clash, marked up on a file board.
            var marked = seat.Results("t1").Take(1)
                .Select(result => Item(result, "L03 MEP v STR")).ToList();

            marked[0].Status = FindingStatus.Reviewed;
            marked[0].AssignedTo = "Ana";

            var file = new FileCheck(ClashFile.Read(ClashFixture.Written(marked.ToArray()).Bytes));

            register.Observe(FindingSource.Clash, file.Read(null, null).Findings, 1,
                             scope: _ => false);

            Assert.Equal(1, register.Count);

            // Two clash tests reporting it, so two host findings with one topic guid between them.
            var host = new ClashCheck(seat).Read(null, null);

            Assert.Equal(2, host.Findings.Count);
            Assert.Equal(host.Findings[0].TopicGuid, host.Findings[1].TopicGuid);

            var moved = FileCheck.Adopt(register, host.Findings, out var adopted);

            register.Observe(FindingSource.Clash, host.Findings, 2, scope: host.Scope);

            // One approval is one approval: without a claim both take it, the file-born finding
            // is removed twice and the count reports two.
            Assert.Equal(1, adopted);
            Assert.Single(moved);
            Assert.Equal(2, register.Count);

            Assert.Single(register.From(FindingSource.Clash)
                                  .Where(one => one.Status == FindingStatus.Reviewed));

            Assert.Single(register.From(FindingSource.Clash)
                                  .Where(one => one.AssignedTo == "Ana"));
        }

        [Fact]
        public void Which_finding_an_adopt_claims_does_not_depend_on_the_registers_order()
        {
            var seat = Seat();
            var host = new ClashCheck(seat).Read(new[] { "t1" }, null);

            string Claimed(bool reversed)
            {
                var (first, second) = Both(seat.Results("t1").Take(1).ToList());

                var register = new FindingRegister();
                var held = reversed ? new[] { second, first } : new[] { first, second };

                register.Observe(FindingSource.Clash, held, 1, scope: _ => false);

                var moved = FileCheck.Adopt(register, host.Findings, out var adopted);

                Assert.Equal(1, adopted);

                return moved.Keys.Single();
            }

            // The candidates are ordered, so the same two findings answer the same way whichever
            // order the register enumerates them in.
            Assert.Equal(Claimed(false), Claimed(true));
        }

        [Fact]
        public void A_finding_a_file_raised_that_no_host_read_claims_stays_present()
        {
            var seat = Seat();
            var register = new FindingRegister();

            // A clash the file carries and this document does not report at all: different
            // elements, and a test name that is no host test id.
            var stray = ClashFindings.ForTest("L09 lifts", "L09 lifts", new[]
            {
                new ClashResultInfo("f1", Key("Duct 9"), Key("Beam 9"), 9, 9, 9, "New", null)
                {
                    Name = "Clash1",
                },
            })[0];

            CheckFacts.SetRuns(stray, new[] { "L09 lifts" });
            stray.HostHasIt = false;

            register.Observe(FindingSource.Clash, new[] { stray }, 1, scope: _ => false);

            var host = new ClashCheck(seat).Read(null, null);

            FileCheck.Adopt(register, host.Findings, out var adopted);

            register.Observe(FindingSource.Clash, host.Findings, 2, scope: host.Scope);

            // Nothing claims it and nothing closes it. That is the decision and not an oversight:
            // no host read speaks for a run this document has not got, so marking it absent would
            // close, on no evidence, every finding of every test a file carried and the document
            // does not run. A person deletes it, or a host read that does report the clash adopts
            // it. FileCheck's own remarks say so.
            Assert.Equal(0, adopted);
            Assert.True(register.Find(stray.Id)!.IsPresent);
            Assert.False(host.Scope(register.Find(stray.Id)!));
        }

        [Fact]
        public void An_issue_attached_to_a_finding_a_host_read_adopts_follows_it()
        {
            var seat = Seat();
            var register = new FindingRegister();

            var marked = seat.Results("t1").Take(1)
                .Select(result => Item(result, "L03 MEP v STR")).ToList();

            var file = new FileCheck(ClashFile.Read(ClashFixture.Written(marked.ToArray()).Bytes));
            var raised = file.Read(null, null).Findings[0];

            register.Observe(FindingSource.Clash, new[] { raised }, 1, scope: _ => false);

            // Raised on the file board, against the finding the file board holds.
            var issue = new Issue("I1", "Duct through transfer beam", FindingSource.Clash);

            IssueFindings.Attach(issue, new[] { raised });

            var host = new ClashCheck(seat).Read(null, null);
            var moved = FileCheck.Adopt(register, host.Findings, out var adopted);

            register.Observe(FindingSource.Clash, host.Findings, 2, scope: host.Scope);

            Assert.Equal(1, adopted);
            Assert.Equal(1, IssueFindings.Repoint(new[] { issue }, moved));

            // The issue still names one finding, and the register still holds it. Without the
            // re-point it names an id nothing holds: the count says one clash and the attachment
            // reaches none.
            var named = Assert.Single(issue.Findings);

            Assert.NotEqual(raised.Id, named);
            Assert.NotNull(register.Find(named));
            Assert.Single(IssueFindings.Attached(issue, register));
        }

        [Fact]
        public void The_host_comment_facts_are_written_every_read_so_a_deletion_clears_them()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);

            var first = seat.Results("t1")[0].Id;

            seat.Comment("t1", new[] { first }, "Duct drops here", "Ana");

            var said = check.Read(new[] { "t1" }, null).Findings
                .First(one => ClashCheck.ResultOf(one) == first);

            Assert.Equal(1, ClashCheck.CommentsOn(said));

            // The remark is taken off in the document, and the run read again. Facts merge and
            // none is ever removed, so a fact written only when there was something to say leaves
            // the deleted wording on the finding and in every pack it is exported to.
            foreach (var one in seat.CommentsOn("t1", first))
                Assert.True(seat.DeleteComment("t1", first, one.WhenTicks));

            var now = check.Read(new[] { "t1" }, null).Findings
                .First(one => ClashCheck.ResultOf(one) == first);

            Assert.Equal(0, ClashCheck.CommentsOn(now));
            Assert.Null(ClashCheck.LastCommentOn(now));
        }

        // ---------------------------------------------------------------------------------
        // Severity and the threshold behind it
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Severity_is_one_exactly_at_the_tests_own_tolerance()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(null, null);

            var finding = read.Findings[0];

            Assert.Equal(0.025, check.ThresholdMetres("t1"));

            finding.Facts["Distance"] = "-0.025";
            Assert.Equal(1.0, check.Severity(finding)!.Value, 6);

            finding.Facts["Distance"] = "-0.05";
            Assert.Equal(2.0, check.Severity(finding)!.Value, 6);

            finding.Facts.Remove("Distance");
            Assert.Null(check.Severity(finding));
        }

        [Fact]
        public void A_model_in_feet_is_measured_against_its_tolerance_in_metres()
        {
            // The host reports −0.082 ft, which is 25 mm, which is exactly this test's tolerance.
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .Deep(-0.082);

            var check = new ClashCheck(seat, metresPerUnit: 0.3048);
            var finding = check.Read(new[] { "t1" }, null).Findings[0];

            // Converted once, at the producer, so nothing above it has to know the leg.
            Assert.Equal(-0.025, ClashCheck.DistanceMetres(finding)!.Value, 4);
            Assert.Equal(1.0, check.Severity(finding)!.Value, 2);
        }

        [Fact]
        public void A_length_fact_is_metres_whatever_the_document_is_in()
        {
            ClashCheck Read(double metresPerUnit) =>
                new ClashCheck(new FakeClashSource()
                                   .With("L03 MEP v STR", "Weekly", "t1")
                                   .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                                   .Deep(metresPerUnit == 1.0 ? -0.025 : -0.082),
                               metresPerUnit);

            var metric = Read(1.0).Read(new[] { "t1" }, null).Findings[0];
            var feet = Read(0.3048).Read(new[] { "t1" }, null).Findings[0];

            // The same physical depth, so the same fact, to the millimetre.
            Assert.Equal(-0.025, ClashCheck.DistanceMetres(metric)!.Value, 4);
            Assert.Equal(-0.025, ClashCheck.DistanceMetres(feet)!.Value, 4);

            // And the point with it: a foot-unit model's clash at Z = 3 is 0.9144 m up.
            Assert.Equal(0.9144, feet.Z!.Value, 4);
            Assert.Equal(3.0, metric.Z!.Value, 4);
        }

        [Fact]
        public void The_depth_column_sorts_on_its_number_and_not_on_what_it_prints()
        {
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3).Deep(-0.0002)
                .Finding(Key("Duct 2"), Key("Beam 2"), 4, 5, 6).Deep(-0.0004);

            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);

            var worst = check.Columns.Single(one => one.Kind == ColumnKind.Number);

            // Both print as nothing at the resolution the board draws.
            Assert.Equal("-0 mm", worst.Format!(worst.Number!(read.Findings[0])!.Value));
            Assert.Equal("-0 mm", worst.Format!(worst.Number!(read.Findings[1])!.Value));

            // The deeper one still sorts below the shallower one.
            var sorted = read.Findings.OrderBy(one => worst.Number!(one) ?? double.MaxValue).ToList();

            Assert.Equal(-0.0004, worst.Number!(sorted[0])!.Value, 6);
            Assert.Equal(-0.0002, worst.Number!(sorted[1])!.Value, 6);
        }

        [Fact]
        public void A_depth_bands_the_same_read_from_the_host_and_read_from_a_file()
        {
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .Deep(-0.164);

            // A foot-unit document, and the same clash 50 mm deep in metres as a file would
            // carry it.
            var host = new ClashCheck(seat, metresPerUnit: 0.3048)
                .Read(new[] { "t1" }, null).Findings[0];

            var column = new ClashCheck(seat).Columns.Single(one => one.Kind == ColumnKind.Number);

            Assert.Equal(ClashColumns.Banded(-0.05), column.Bucket!(column.Number!(host)!.Value));
            Assert.Equal("25 to 100 mm", column.Bucket!(column.Number!(host)!.Value));
        }

        [Fact]
        public void A_run_with_no_tolerance_still_tones_but_reports_no_measure()
        {
            var seat = new FakeClashSource()
                .With("L03 MEP v STR", "Weekly", "t1")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .Deep(-0.030);

            seat.Retune("t1", 0, MatrixTest.Hard);

            var check = new ClashCheck(seat);
            var finding = check.Read(new[] { "t1" }, null).Findings[0];
            var worst = check.Columns.Single(one => one.Kind == ColumnKind.Number);

            // No threshold to normalise against, so the check says so rather than inventing one.
            Assert.Null(check.Severity(finding));
            Assert.Equal("no measure", check.SeverityBand(check.Severity(finding)));

            // The figure still tones, against clash's own default rather than the board's.
            Assert.True(worst.IsBad!(finding));
        }

        [Fact]
        public void A_clash_is_also_known_by_the_cells_around_it()
        {
            var check = new ClashCheck(Seat());
            var finding = check.Read(new[] { "t1" }, null).Findings[0];

            var aliases = check.Aliases(finding).ToList();

            Assert.Equal(26, aliases.Count);
            Assert.DoesNotContain(finding.Id, aliases);
            Assert.Equal(aliases.Count, aliases.Distinct(StringComparer.Ordinal).Count());
        }

        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_file_board_and_a_document_board_declare_the_same_columns()
        {
            var host = new ClashCheck(Seat()).Columns;
            var file = File(Seat()).Columns;

            Assert.Equal(host.Select(one => one.Key), file.Select(one => one.Key));
            Assert.Equal(host.Select(one => one.Title), file.Select(one => one.Title));
            Assert.Equal(host.Select(one => one.Kind), file.Select(one => one.Kind));

            // The same ranges, so one register slice read from both legs bands under one
            // vocabulary rather than two.
            var deep = -0.05;

            Assert.Equal(host.Single(one => one.Kind == ColumnKind.Number).Bucket!(deep),
                         file.Single(one => one.Kind == ColumnKind.Number).Bucket!(deep));
        }

        /// <summary>Take a test's results out of the document, as a re-run that found none does.</summary>
        /// <param name="seat">The fake host.</param>
        /// <param name="testId">Which test.</param>
        private static void Resolved(FakeClashSource seat, string testId)
        {
            seat.SetStatus(testId, seat.Results(testId).Select(one => one.Id).ToList(), "Resolved");
            seat.CompactTests(new[] { testId });
        }

        /// <summary>A file check over a BCF written from everything this seat holds.</summary>
        /// <param name="seat">The fake host.</param>
        private static FileCheck File(FakeClashSource seat)
        {
            var items = seat.Tests()
                .SelectMany(test => seat.Results(test.Id).Select(result => Item(result, test.Name)))
                .ToList();

            return new FileCheck(ClashFile.Read(ClashFixture.Written(items.ToArray()).Bytes), @"C:\p\round.bcfzip");
        }

        /// <summary>One host result as the item a BCF is written from.</summary>
        /// <param name="result">The result.</param>
        /// <param name="testName">The test it came from.</param>

        [Fact]
        public void A_file_read_keeps_the_element_names_and_the_models_the_pack_carried()
        {
            // The two name facts hold the display names of the clashing elements. They were read
            // off the item's ModelA and ModelB, which are the model files the elements are in, so
            // a seat reading a pack held a model name in the element-name column and sent it back
            // out that way. The model and discipline of each side have their own facts, and the
            // receiving seat has no federation to derive them from.
            var item = ClashFixture.Item("a", x: 1);
            var finding = ClashFixture.Found(item);

            finding.Facts[ClashFindings.NameA] = "Duct 400x250";
            finding.Facts[ClashFindings.NameB] = "Beam UB305x165";
            finding.Facts[ClashFindings.ModelA] = "MEP.nwc";
            finding.Facts[ClashFindings.ModelB] = "STR.nwc";
            finding.Facts[ClashFindings.DisciplineA] = "Mechanical";
            finding.Facts[ClashFindings.DisciplineB] = "Structural";

            var file = new FileCheck(ClashFile.Read(FindingBcf.Write(new[] { finding }).Bytes));
            var back = Assert.Single(file.Read(null, null).Findings);

            Assert.Equal("Duct 400x250", back.Facts[ClashFindings.NameA]);
            Assert.Equal("Beam UB305x165", back.Facts[ClashFindings.NameB]);
            Assert.Equal("MEP.nwc", back.Facts[ClashFindings.ModelA]);
            Assert.Equal("STR.nwc", back.Facts[ClashFindings.ModelB]);
            Assert.Equal("Mechanical", back.Facts[ClashFindings.DisciplineA]);
            Assert.Equal("Structural", back.Facts[ClashFindings.DisciplineB]);
        }

        [Fact]
        public void A_file_read_keeps_the_topic_each_result_arrived_under()
        {
            // Two tests on one conflict go out as two topics, the second under a guid of its own.
            // Re-deriving the guid from the key on the way in would send the second one's markup
            // home under the first one's name.
            var mine = ClashFixture.Item("a", x: 1);
            var theirs = ClashFixture.Item("a", x: 1, test: "Clearance");

            Assert.Equal(mine.Key, theirs.Key);

            var written = FindingBcf.Write(new[]
            {
                ClashFixture.Found(mine), ClashFixture.Found(theirs),
            });

            var file = new FileCheck(ClashFile.Read(written.Bytes));
            var back = file.Read(null, null).Findings;

            Assert.Equal(2, back.Count);
            Assert.Equal(2, back.Select(one => one.TopicGuid)
                              .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void A_result_naming_no_element_is_counted_so_the_seat_can_say_so()
        {
            // A markup of one of these could never land back on a model, and a reader that did
            // not count them left that to be discovered at the save.
            var topic = new BcfTopic("11111111-1111-1111-1111-111111111111", "Somebody's own note",
                                     DateTimeOffset.UnixEpoch, "ada");

            using var theirs = new MemoryStream();

            BcfWriter.Write(theirs, new[] { topic }, BcfVersion.V21);

            var file = new FileCheck(ClashFile.Read(theirs.ToArray()));

            file.Read(null, null);

            Assert.Equal(1, file.Unkeyed);
        }

        private static ClashItem Item(ClashResultInfo result, string testName) =>
            new ClashItem(result.Key(), testName)
            {
                Name = result.Name,
                Status = ClashStatusMap.Read(result.HostStatus ?? string.Empty),
                X = result.PointX,
                Y = result.PointY,
                Z = result.PointZ,
            };
    }
}
