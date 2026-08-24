using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.OpenBim;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class IssueSheetTests
    {
        private static SheetTable Back(params string?[][] rows)
        {
            var sheet = new XlsxSheet("Mechanical");
            foreach (var row in rows) sheet.Row(row);

            return XlsxReader.Read(XlsxWriter.Write(new[] { sheet })).First!;
        }

        private static string?[] Header() => new string?[]
        {
            IssueSheet.IdHeading, "Issue", "Level", "Where", "What is wrong", "What to do",
            "Status", "Due", IssueSheet.ResponseHeading, IssueSheet.StatusHeading,
        };

        private static string?[] Row(string id, string? response = null, string? status = null) =>
            new string?[] { id, "Level 2 MEP v STR", "L02", "1, 2, 3 m", "Duct through beam",
                            "Move it down", "Active", null, response, status };

        private static Dictionary<string, string> Sent(params string[] groups) =>
            groups.ToDictionary(IssueSheet.IdOf, g => g, StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, FindingStatus> Board(
            params (string Group, FindingStatus Status)[] rows) =>
            rows.ToDictionary(r => r.Group, r => r.Status, StringComparer.Ordinal);

        [Fact]
        public void An_id_is_short_opaque_and_the_same_every_time()
        {
            var id = IssueSheet.IdOf("Level 2 MEP v STR");

            Assert.Equal(id, IssueSheet.IdOf("Level 2 MEP v STR"));
            Assert.NotEqual(id, IssueSheet.IdOf("Level 3 MEP v STR"));
            Assert.StartsWith("CW-", id);
            Assert.Equal(3 + IssueSheet.IdWidth, id.Length);
            Assert.DoesNotContain(" ", id);
        }

        [Fact]
        public void The_sheet_that_goes_out_carries_two_empty_columns_and_nothing_else_to_fill_in()
        {
            var sheet = IssueSheet.Write("Mechanical", new[]
            {
                new IssueLine("Level 2 MEP v STR")
                {
                    Party = "Mechanical", Level = "L02", Location = "1, 2, 3 m",
                    Description = "Duct through beam", Instruction = "Move it down",
                    Status = FindingStatus.Active,
                },
            });

            Assert.Equal("Mechanical", sheet.Name);

            var header = sheet.Rows[0];

            Assert.Equal(IssueSheet.IdHeading, header[0]);
            Assert.Equal(IssueSheet.ResponseHeading, header[8]);
            Assert.Equal(IssueSheet.StatusHeading, header[9]);

            var row = sheet.Rows[1];

            Assert.Equal(IssueSheet.IdOf("Level 2 MEP v STR"), row[0]);
            Assert.Equal("Level 2 MEP v STR", row[1]);
            Assert.Equal("Move it down", row[5]);
            Assert.Null(row[8]);
            Assert.Null(row[9]);
        }

        [Fact]
        public void A_returned_closure_moves_the_board()
        {
            var report = IssueSheet.Read(
                Back(Header(), Row(IssueSheet.IdOf("Level 2 MEP v STR"), "Dropped the duct", "Closed")),
                Sent("Level 2 MEP v STR"),
                Board(("Level 2 MEP v STR", FindingStatus.Active)));

            var only = Assert.Single(report.Matched);

            Assert.Equal(FindingStatus.Resolved, only.Merged);
            Assert.True(only.Changes);
            Assert.Equal("Dropped the duct", only.Note);
            Assert.Equal(1, report.Changes);
        }

        [Fact]
        public void A_stale_open_does_not_undo_an_approval_here_either()
        {
            // The same rule as the BCF leg, from the same function. Two copies of it would
            // eventually disagree about somebody's approval.
            var report = IssueSheet.Read(
                Back(Header(), Row(IssueSheet.IdOf("G"), "still looking", "Open")),
                Sent("G"),
                Board(("G", FindingStatus.Approved)));

            var only = Assert.Single(report.Matched);

            Assert.True(only.Refused);
            Assert.False(only.Changes);
            Assert.Equal(FindingStatus.Approved, only.Merged);
            Assert.Contains("stays where it is", only.Explain());
        }

        [Fact]
        public void An_explicit_re_open_is_honoured_even_downwards()
        {
            var report = IssueSheet.Read(
                Back(Header(), Row(IssueSheet.IdOf("G"), "not fixed", "Reopened")),
                Sent("G"),
                Board(("G", FindingStatus.Resolved)));

            var only = Assert.Single(report.Matched);

            Assert.True(only.IsReopen);
            Assert.Equal(FindingStatus.Active, only.Merged);
            Assert.Equal(1, report.Reopened);
            Assert.Contains("re-opened", only.Explain());
        }

        [Fact]
        public void A_status_nobody_knows_changes_nothing_and_the_note_still_comes_across()
        {
            var report = IssueSheet.Read(
                Back(Header(), Row(IssueSheet.IdOf("G"), "waiting on the client", "Awaiting client")),
                Sent("G"),
                Board(("G", FindingStatus.Active)));

            var only = Assert.Single(report.Matched);

            Assert.Equal("Awaiting client", only.UnknownStatus);
            Assert.False(only.Changes);
            Assert.Equal("waiting on the client", only.Note);
            Assert.Equal(1, report.Unknown);
        }

        [Fact]
        public void A_row_naming_an_issue_this_project_never_sent_is_kept_apart()
        {
            var report = IssueSheet.Read(
                Back(Header(),
                     Row(IssueSheet.IdOf("G"), "done", "Closed"),
                     Row("CW-ABCDEF", "theirs", "Open")),
                Sent("G"),
                Board(("G", FindingStatus.Active)));

            Assert.Single(report.Matched);
            Assert.Equal("CW-ABCDEF", Assert.Single(report.Unmatched).Key);
            Assert.Contains("never sent", report.Explain());
        }

        [Fact]
        public void An_id_that_came_back_in_a_different_case_still_matches()
        {
            // Through Excel, an email and a paste into a new sheet, in whatever case the last
            // person's autocorrect felt like.
            var id = IssueSheet.IdOf("G");

            var report = IssueSheet.Read(
                Back(Header(), Row(id.ToLowerInvariant(), "done", "Closed")),
                Sent("G"),
                Board(("G", FindingStatus.Active)));

            Assert.Equal(FindingStatus.Resolved, Assert.Single(report.Matched).Merged);
        }

        [Fact]
        public void A_repeated_id_is_reported_and_the_first_row_wins()
        {
            var id = IssueSheet.IdOf("G");

            var report = IssueSheet.Read(
                Back(Header(), Row(id, "first", "Closed"), Row(id, "second", "Open")),
                Sent("G"),
                Board(("G", FindingStatus.Active)));

            Assert.Equal("first", Assert.Single(report.Matched).Note);
            Assert.Single(report.Duplicates);
            Assert.Contains("repeating an earlier row", report.Explain());
        }

        [Fact]
        public void A_sheet_with_nowhere_to_write_a_response_says_so()
        {
            var sheet = Back(
                new string?[] { IssueSheet.IdHeading, "Issue" },
                new string?[] { IssueSheet.IdOf("G"), "Level 2" });

            var report = IssueSheet.Read(sheet, Sent("G"), Board(("G", FindingStatus.Active)));

            Assert.False(report.IsUsable);
            Assert.Contains("nothing on it to bring back", report.Problem);
        }

        [Fact]
        public void A_sheet_with_no_id_column_says_what_to_do_about_it()
        {
            var sheet = Back(
                new string?[] { "Issue", IssueSheet.StatusHeading },
                new string?[] { "Level 2", "Closed" });

            var report = IssueSheet.Read(sheet, Sent("G"), Board(("G", FindingStatus.Active)));

            Assert.False(report.IsUsable);
            Assert.Contains("no way to tell which element", report.Problem);
            Assert.Contains("Send the worksheet this board produced", report.Problem);
        }

        [Fact]
        public void A_group_the_board_no_longer_holds_is_treated_as_outstanding()
        {
            var report = IssueSheet.Read(
                Back(Header(), Row(IssueSheet.IdOf("G"), "done", "Closed")),
                Sent("G"),
                Board());

            var only = Assert.Single(report.Matched);

            Assert.Equal(FindingStatus.Active, only.Current);
            Assert.Equal(FindingStatus.Resolved, only.Merged);
        }

        [Fact]
        public void Nothing_returned_is_a_readable_answer_rather_than_an_error()
        {
            var report = IssueSheet.Read(null, Sent("G"), Board());

            Assert.False(report.IsUsable);
            Assert.NotNull(report.Problem);
        }

        [Fact]
        public void The_two_legs_agree_on_where_a_clash_is()
        {
            Assert.Equal("1,200, 2,300, 3,400 mm",
                IssueSheet.Where(1.2, 2.3, 3.4, Measure.Millimetres));
        }

        [Fact]
        public void The_sheet_says_what_the_far_end_is_supposed_to_write()
        {
            var said = IssueSheet.Instructions();

            Assert.Contains(IssueSheet.ResponseHeading, said);
            Assert.Contains("Reopened", said);
            Assert.Contains("Adding rows is fine", said);
        }
    }
}
