using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Checks;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
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
            Assert.Equal(theirs.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal),
                         mine.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal));
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

            var findings = read.Findings.ToList();
            var titles = new[] { "Corridor1", "Corridor2" };

            Assert.True(check.HostEcho!.SetTitle(findings, titles));

            Assert.Single(seat.Log.Where(said => said.StartsWith("RenamePicked", StringComparison.Ordinal)));
            Assert.Equal(new[] { "Corridor1", "Corridor2" },
                         seat.Results("t1").Select(one => one.Name));
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
        public void The_host_check_offers_every_capability()
        {
            var check = new ClashCheck(Seat());

            Assert.NotNull(check.HostEcho);
            Assert.NotNull(check.Grouping);
            Assert.NotNull(check.Runner);
            Assert.NotNull(check.Naming);
            Assert.NotNull(check.Properties);
            Assert.NotNull(check.Geometry);
        }

        [Fact]
        public void The_file_check_offers_geometry_and_nothing_else()
        {
            var check = File(Seat());

            Assert.Null(check.HostEcho);
            Assert.Null(check.Grouping);
            Assert.Null(check.Runner);
            Assert.Null(check.Naming);
            Assert.Null(check.Properties);
            Assert.NotNull(check.Geometry);
        }

        [Fact]
        public void Selecting_from_the_document_goes_through_the_engines_own_handles()
        {
            var seat = Seat();
            var check = new ClashCheck(seat);
            var read = check.Read(new[] { "t1" }, null);

            var selected = check.Geometry!.Select(read.Findings.ToList(), add: false);

            Assert.Equal(4, selected);
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

            var file = new FileCheck(ClashFile.Read(ClashBcf.Write(one).Bytes));
            var read = file.Read(null, null);

            register.Observe(FindingSource.Clash, read.Findings, 2, scope: read.Scope);

            Assert.All(register.From(FindingSource.Clash), finding => Assert.True(finding.IsPresent));
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

            var file = new FileCheck(ClashFile.Read(ClashBcf.Write(marked).Bytes));
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

            var file = new FileCheck(ClashFile.Read(ClashBcf.Write(marked).Bytes));
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

            var first = new ClashCheck(seat).Read(null, null);

            register.Observe(FindingSource.Clash, first.Findings, 1, scope: first.Scope);

            var second = new ClashCheck(seat).Read(null, null);

            FileCheck.Adopt(register, second.Findings, out var adopted);

            Assert.Equal(0, adopted);
            Assert.Equal(3, register.Count);
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
            var seat = Seat();
            var check = new ClashCheck(seat, metresPerUnit: 0.3048);
            var finding = check.Read(new[] { "t1" }, null).Findings[0];

            // 0.082 ft is 25 mm, which is exactly this test's tolerance.
            finding.Facts["Distance"] = "-0.082";

            Assert.Equal(1.0, check.Severity(finding)!.Value, 2);
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

            return new FileCheck(ClashFile.Read(ClashBcf.Write(items).Bytes), @"C:\p\round.bcfzip");
        }

        /// <summary>One host result as the item a BCF is written from.</summary>
        /// <param name="result">The result.</param>
        /// <param name="testName">The test it came from.</param>
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
