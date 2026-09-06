using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// The contract every clash adapter has to keep.
    ///
    /// Written against <see cref="FakeClashSource"/>. The seam had no fake at all, so everything
    /// downstream of the clash read — the board, the matrix, the exchange, the naming — was
    /// verified only by hand on a licensed seat.
    /// </summary>
    public class ClashSeamContractTests
    {
        private static ElementKey Key(string name) =>
            ElementKey.FromTreePath(ElementKey.ScopeOf(@"C:\p\ARCH.nwc"), @"C:\p\ARCH.nwc",
                                    "L03", name);

        private static FakeClashSource Seat() =>
            new FakeClashSource()
                .With("L03 MEP v STR", "Weekly")
                .Finding(Key("Duct 1"), Key("Beam 1"), 1, 2, 3)
                .Finding(Key("Duct 2"), Key("Beam 2"), 4, 5, 6, "Reviewed");

        private static string TestId(FakeClashSource seat) => seat.Tests()[0].Id;

        // ---------------------------------------------------------------------------------
        // Reading
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_test_says_what_it_is_called_and_how_much_it_found()
        {
            var test = Assert.Single(Seat().Tests());

            Assert.Equal("L03 MEP v STR", test.Name);
            Assert.Equal("Weekly", test.Folder);
            Assert.Equal(2, test.ResultCount);
        }

        [Fact]
        public void A_result_carries_both_elements_the_point_and_the_hosts_own_status()
        {
            var seat = Seat();
            var results = seat.Results(TestId(seat));

            Assert.Equal(2, results.Count);
            Assert.Equal(Key("Duct 1"), results[0].A);
            Assert.Equal(3, results[0].PointZ);
            Assert.Equal("Reviewed", results[1].HostStatus);
        }

        [Fact]
        public void Reading_everything_gives_each_test_its_shape_and_its_results()
        {
            var read = Assert.Single(Seat().ReadEverything(null));

            Assert.Equal(2, read.Results.Count);
            Assert.NotNull(read.Shape);
            Assert.Equal(0.025, read.Shape!.ToleranceMetres);
        }

        [Fact]
        public void A_read_that_is_cancelled_stops_where_it_was_asked_to()
        {
            var seat = Seat().With("A second test");

            Assert.Single(seat.ReadEverything(seen => seen < 2));
        }

        // ---------------------------------------------------------------------------------
        // Writing — the reads have to show it afterwards
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_status_written_is_a_status_read_back()
        {
            var seat = Seat();
            var id = TestId(seat);
            var first = seat.Results(id)[0].Id;

            Assert.True(seat.SetStatus(id, new[] { first }, "Approved"));
            Assert.Equal("Approved", seat.Results(id)[0].HostStatus);
            Assert.Equal("Reviewed", seat.Results(id)[1].HostStatus);
        }

        [Fact]
        public void An_assignee_and_a_comment_come_back_on_the_result_they_were_written_to()
        {
            var seat = Seat();
            var id = TestId(seat);
            var first = seat.Results(id)[0].Id;

            seat.Assign(id, new[] { first }, "Mechanical");
            seat.Comment(id, new[] { first }, "Duct moved.", "A Coordinator");

            Assert.Equal("Mechanical", seat.Results(id)[0].HostAssignedTo);
            Assert.Equal("Duct moved.", Assert.Single(seat.CommentsOn(id, first)).Body);
        }

        [Fact]
        public void A_regroup_puts_the_named_results_in_the_named_group()
        {
            var seat = Seat();
            var id = TestId(seat);

            Assert.True(seat.Regroup(id, new[] { seat.Results(id)[0].Id }, "Corridor C"));

            Assert.Equal("Corridor C", seat.Results(id)[0].GroupName);
            Assert.Null(seat.Results(id)[1].GroupName);
        }

        [Fact]
        public void A_nested_regroup_says_whether_the_document_nested_it()
        {
            var seat = Seat();
            var id = TestId(seat);

            var flat = seat.Regroup(id, new[]
            {
                new ClashGroupPath(new[] { "Level 3", "Ducts" },
                                   new[] { seat.Results(id)[0].Id }),
            });

            Assert.True(flat.Took);
            Assert.False(flat.Nested);
            Assert.NotNull(flat.Problem);
        }

        [Fact]
        public void Renaming_a_test_does_not_disturb_its_results()
        {
            var seat = Seat();
            var id = TestId(seat);

            Assert.True(seat.RenameTest(id, "L03 Mech v Struct"));
            Assert.Equal("L03 Mech v Struct", seat.Tests()[0].Name);
            Assert.Equal(2, seat.Results(id).Count);
        }

        // ---------------------------------------------------------------------------------
        // Refusal is ordinary, and says so
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_seat_with_no_clash_engine_reads_nothing_and_refuses_every_write()
        {
            var seat = Seat();

            seat.IsAvailable = false;

            Assert.Empty(seat.Tests());
            Assert.False(seat.Run("anything"));
            Assert.Contains("no clash engine", seat.LastProblem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_document_that_will_not_take_an_edit_says_why_rather_than_throwing()
        {
            var seat = Seat();
            var id = TestId(seat);

            seat.Refuses = "This document is open read-only.";

            Assert.False(seat.SetStatus(id, new[] { seat.Results(id)[0].Id }, "Approved"));
            Assert.Equal("This document is open read-only.", seat.LastProblem);
            Assert.Equal("New", seat.Results(id)[0].HostStatus);
        }

        [Fact]
        public void A_write_to_a_test_the_document_no_longer_has_is_refused_by_name()
        {
            var seat = Seat();

            Assert.False(seat.SetStatus("gone", new[] { "r1" }, "Approved"));
            Assert.Contains("no longer in the document", seat.LastProblem, StringComparison.Ordinal);
        }

        // ---------------------------------------------------------------------------------
        // What the board is built on top of
        // ---------------------------------------------------------------------------------

        [Fact]
        public void The_read_makes_findings_that_keep_their_identity_across_a_rename()
        {
            // The whole point of the seam having a fake: this is the path that lost every
            // decision under a renamed test, and it could only be run on a licensed seat.
            var seat = Seat();
            var id = TestId(seat);

            var before = ClashFindings.ForTest(id, seat.Tests()[0].Name, seat.Results(id));

            seat.RenameTest(id, "Renamed");

            var after = ClashFindings.ForTest(id, seat.Tests()[0].Name, seat.Results(id));

            Assert.Equal(before.Select(one => one.Id), after.Select(one => one.Id));
            Assert.Equal("Renamed", after[0].Rule);
        }

        [Fact]
        public void Selecting_results_reports_how_many_elements_it_reached()
        {
            var seat = Seat();
            var id = TestId(seat);

            Assert.Equal(4, seat.SelectResults(id, seat.Results(id).Select(r => r.Id).ToList(),
                                               add: false));

            Assert.Equal(-1, seat.SelectResults("gone", new[] { "r1" }, add: false));
        }
    }
}
