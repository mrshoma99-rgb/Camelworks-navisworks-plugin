using System;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SheetImportTests
    {
        private static SheetTable Sheet(params string?[][] rows)
        {
            var sheet = new XlsxSheet("Properties");
            foreach (var row in rows) sheet.Row(row);

            return XlsxReader.Read(XlsxWriter.Write(new[] { sheet })).First!;
        }

        private static SheetTable Exported() => Sheet(
            new[] { SheetImport.KeyHeading, "Element", "Mark", "Fire rating" },
            new[] { "k1", "Door 1", "D-12", null },
            new[] { "k2", "Door 2", "D-13", "FD30" });

        [Fact]
        public void Rows_whose_identity_is_in_scope_come_back_matched()
        {
            var report = SheetImport.Of(Exported(), new[] { "k1", "k2" });

            Assert.True(report.IsUsable);
            Assert.Equal(2, report.Matched.Count);
            Assert.Equal("k1", report.Matched[0].Key);
            Assert.Equal(2, report.Matched[0].Number);
            Assert.Empty(report.Unmatched);
            Assert.Contains("2 rows match elements", report.Explain());
        }

        [Fact]
        public void A_row_that_matches_nothing_is_a_visible_decision_never_a_silent_drop()
        {
            // Somebody added rows at the bottom, or sent a sheet from another package, or the model
            // has been re-exported. All three need a decision and all three look identical to a
            // tool that just skips them.
            var report = SheetImport.Of(Exported(), new[] { "k1" });

            Assert.Equal("k2", Assert.Single(report.Unmatched).Key);
            Assert.Contains("1 match nothing in scope", report.Explain());
        }

        [Fact]
        public void A_repeated_identity_is_reported_and_the_first_row_wins()
        {
            var sheet = Sheet(
                new[] { SheetImport.KeyHeading, "Mark" },
                new[] { "k1", "first" },
                new[] { "k1", "second" });

            var report = SheetImport.Of(sheet, new[] { "k1" });

            Assert.Equal("first", Assert.Single(report.Matched).Cell(1));
            Assert.Equal(3, Assert.Single(report.Duplicates).Number);
            Assert.Contains("1 repeat an earlier row", report.Explain());
        }

        [Fact]
        public void A_row_somebody_typed_but_left_unidentified_is_counted()
        {
            var sheet = Sheet(
                new[] { SheetImport.KeyHeading, "Mark" },
                new[] { "k1", "D-12" },
                new[] { null, "D-99" });

            var report = SheetImport.Of(sheet, new[] { "k1" });

            Assert.Equal(1, report.Blank);
            Assert.Contains("1 carry no identity", report.Explain());
        }

        [Fact]
        public void A_blank_spacer_row_is_not_a_mistake()
        {
            var sheet = Sheet(
                new[] { SheetImport.KeyHeading, "Mark" },
                new[] { "k1", "D-12" },
                new string?[] { null, null });

            Assert.Equal(0, SheetImport.Of(sheet, new[] { "k1" }).Blank);
        }

        [Fact]
        public void A_sheet_with_no_identity_column_says_what_to_do_about_it()
        {
            var sheet = Sheet(
                new[] { "Element", "Mark" },
                new[] { "Door 1", "D-12" });

            var report = SheetImport.Of(sheet, new[] { "k1" });

            Assert.False(report.IsUsable);
            Assert.Contains("no way to tell which element", report.Problem);
            Assert.Contains("Export the properties from here first", report.Problem);
        }

        [Fact]
        public void The_identity_column_is_not_offered_as_something_to_write()
        {
            // A typo in a key would otherwise rename the element it points at.
            var report = SheetImport.Of(Exported(), new[] { "k1", "k2" });

            Assert.Equal(new[] { "Element", "Mark", "Fire rating" }, report.Writable);
            Assert.DoesNotContain(SheetImport.KeyHeading, report.Writable);
        }

        [Fact]
        public void One_column_comes_back_keyed_by_identity()
        {
            var report = SheetImport.Of(Exported(), new[] { "k1", "k2" });

            var values = report.Column(report.ColumnOf("Fire rating"));

            Assert.Equal(2, values.Count);
            Assert.Null(values["k1"]);
            Assert.Equal("FD30", values["k2"]);
        }

        [Fact]
        public void A_column_nobody_named_is_no_values_rather_than_an_exception()
        {
            var report = SheetImport.Of(Exported(), new[] { "k1" });

            Assert.Equal(-1, report.ColumnOf("Nothing"));
            Assert.Empty(report.Column(-1));
        }

        [Fact]
        public void An_identity_is_matched_after_trimming_because_Excel_pads()
        {
            var sheet = Sheet(
                new[] { SheetImport.KeyHeading, "Mark" },
                new[] { "  k1  ", "D-12" });

            Assert.Equal("k1", Assert.Single(SheetImport.Of(sheet, new[] { "k1" }).Matched).Key);
        }

        [Fact]
        public void Nothing_in_the_sheet_is_a_readable_answer()
        {
            Assert.Contains("nothing in that sheet", SheetImport.Of(null, new[] { "k1" }).Problem);

            var empty = XlsxReader.Read(XlsxWriter.Write(new[] { new XlsxSheet("Empty") })).First;

            Assert.Contains("nothing in that sheet", SheetImport.Of(empty, new[] { "k1" }).Problem);
        }

        [Fact]
        public void Nothing_in_scope_matches_nothing_rather_than_matching_everything()
        {
            var report = SheetImport.Of(Exported(), Array.Empty<string>());

            Assert.Empty(report.Matched);
            Assert.Equal(2, report.Unmatched.Count);
            Assert.False(report.IsUsable);
        }
    }
}
