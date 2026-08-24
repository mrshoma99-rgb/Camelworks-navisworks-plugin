using System;
using System.Collections.Generic;
using CamelWorks.Core.Report;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class DeliverableTests
    {
        private static readonly DateTimeOffset When =
            new DateTimeOffset(2026, 8, 22, 9, 30, 0, TimeSpan.Zero);

        [Theory]
        [InlineData(null, "A")]
        [InlineData("", "A")]
        [InlineData("   ", "A")]
        [InlineData("A", "B")]
        [InlineData("Y", "Z")]
        [InlineData("Z", "AA")]
        [InlineData("AZ", "BA")]
        [InlineData("ZZ", "AAA")]
        [InlineData("a", "b")]
        [InlineData("z", "aa")]
        public void Letters_run_the_way_a_drawing_register_does(string? previous, string expected)
        {
            Assert.Equal(expected, Deliverable.NextRevision(previous));
        }

        [Theory]
        [InlineData("1", "2")]
        [InlineData("09", "10")]
        [InlineData("099", "100")]
        [InlineData("P1", "P2")]
        [InlineData("C09", "C10")]
        public void A_project_that_numbers_its_revisions_keeps_numbering_them(string previous, string expected)
        {
            // Switching a numbered register to letters halfway through is not a service to anybody.
            Assert.Equal(expected, Deliverable.NextRevision(previous));
        }

        [Fact]
        public void A_revision_nobody_can_increment_is_left_alone_rather_than_mangled()
        {
            Assert.Equal("REV-1", Deliverable.NextRevision("REV-"));
            Assert.Equal("9999999999999999999", Deliverable.NextRevision("9999999999999999999"));
        }

        [Fact]
        public void A_name_carries_the_things_that_tell_two_issues_apart()
        {
            Assert.Equal("Riverside - clash report - Mechanical - Rev B - 2026-08-22",
                Deliverable.Name(null, "Riverside", "Mechanical", "B", When));
        }

        [Fact]
        public void A_template_can_be_anything_and_the_tokens_still_fill()
        {
            Assert.Equal("2026-08-22 Mechanical rev B",
                Deliverable.Name("{Date} {Party} rev {Rev}", "Riverside", "Mechanical", "B", When));
        }

        [Fact]
        public void What_comes_out_can_be_written_to_disk()
        {
            // A trailing dot is silently stripped by Windows, leaving a file nobody can find by the
            // name they were told it had. Better to remove it where it can be seen.
            Assert.Equal("A-B-C", Deliverable.Safe("A/B\\C"));
            Assert.Equal("no name", Deliverable.Safe("  no    name.  "));
            Assert.Equal("report", Deliverable.Safe("   "));
            Assert.Equal("report", Deliverable.Safe(null));
            Assert.DoesNotContain(":", Deliverable.Name("{Party}", null, "M:E:P", "A", When));
        }

        [Fact]
        public void A_cover_page_says_what_it_replaces()
        {
            var previous = new ReportIssue("A", When, "Riverside - Rev A.pdf");

            Assert.Equal("Supersedes revision A, issued 22 August 2026 as Riverside - Rev A.pdf.",
                previous.Supersedes);
        }

        [Fact]
        public void A_previous_issue_with_no_filename_still_reads()
        {
            Assert.Equal("Supersedes revision A, issued 22 August 2026.",
                new ReportIssue("A", When).Supersedes);
        }

        [Fact]
        public void The_register_writes_down_and_reads_back()
        {
            var issues = new Dictionary<string, ReportIssue>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mechanical"] = new ReportIssue("B", When, "b.pdf"),
                ["Structures"] = new ReportIssue("A", When),
            };

            var back = Deliverable.Read(JsonReader.Parse(Deliverable.Write(issues).ToJson()));

            Assert.Equal(2, back.Count);
            Assert.Equal("B", back["mechanical"].Revision);
            Assert.Equal("b.pdf", back["Mechanical"].File);
            Assert.Null(back["Structures"].File);
            Assert.Equal(When, back["Structures"].IssuedUtc);
        }

        [Fact]
        public void An_empty_or_unreadable_register_is_no_issues_rather_than_an_error()
        {
            Assert.Empty(Deliverable.Read(null));
            Assert.Empty(Deliverable.Read(JsonValue.Array()));
            Assert.Empty(Deliverable.Read(JsonValue.Object().Set("Mechanical", JsonValue.String("no"))));
        }
    }
}
