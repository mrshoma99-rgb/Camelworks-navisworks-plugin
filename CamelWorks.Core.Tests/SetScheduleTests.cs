using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Report;
using CamelWorks.Core.Sets;
using Xunit;

namespace CamelWorks.Core.Tests
{
    /// <summary>
    /// Forty sets from a spreadsheet, which is what a project needs on day one — not one set at a
    /// time, and not one family split off one property.
    /// </summary>
    public class SetScheduleTests
    {
        private static SheetTable Sheet(params string?[][] rows)
        {
            var sheet = new XlsxSheet("Sets");
            foreach (var row in rows) sheet.Row(row);

            return XlsxReader.Read(XlsxWriter.Write(new[] { sheet })).First!;
        }

        // ---- the schedule itself ------------------------------------------------------------

        [Fact]
        public void Rows_sharing_a_name_become_one_set_of_ANDed_conditions()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Level 2 ducts", "Element", "Category", SetOperator.Equals, "Duct"),
                new SetRow("Level 2 ducts", "Element", "Level", SetOperator.Equals, "L02"),
            });

            var set = Assert.Single(plan.Sets);
            Assert.Equal("Level 2 ducts", set.Name);
            Assert.Equal(2, set.Conditions);
            Assert.Equal(2, plan.Rows);
        }

        [Fact]
        public void Sets_come_out_in_the_order_their_first_row_appeared()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Ducts", "Element", "Category", SetOperator.Equals, "Duct"),
                new SetRow("Pipes", "Element", "Category", SetOperator.Equals, "Pipe"),
                new SetRow("Ducts", "Element", "Level", SetOperator.Equals, "L02"),
            });

            Assert.Equal(new[] { "Ducts", "Pipes" }, plan.Sets.Select(s => s.Name));
        }

        [Fact]
        public void A_folder_files_the_set_and_a_disagreement_is_reported()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Ducts", "Element", "Category", SetOperator.Equals, "Duct", folder: "Mechanical"),
                new SetRow("Ducts", "Element", "Level", SetOperator.Equals, "L02", folder: "Services"),
            });

            Assert.Equal("Mechanical", plan.Sets.Single().Folder);
            Assert.Contains(plan.Problems, p => p.Contains("different folders"));
        }

        [Fact]
        public void A_comparison_with_no_value_is_refused_rather_than_matching_everything()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Ducts", "Element", "Category", SetOperator.Equals, ""),
            });

            Assert.Empty(plan.Sets);
            Assert.Contains(plan.Problems, p => p.Contains("match everything"));
        }

        [Fact]
        public void Defined_needs_no_value_because_it_is_about_the_property_existing()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Anything coded", "Element", "Code", SetOperator.Defined),
            });

            Assert.Single(plan.Sets);
            Assert.Empty(plan.Problems);
        }

        [Fact]
        public void A_row_with_no_set_name_is_reported_rather_than_guessed_at()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("", "Element", "Category", SetOperator.Equals, "Duct"),
            });

            Assert.Empty(plan.Sets);
            Assert.Single(plan.Problems);
        }

        [Fact]
        public void Blank_rows_are_not_counted_as_rows()
        {
            var plan = SetSchedule.Plan(new[]
            {
                new SetRow("Ducts", "Element", "Category", SetOperator.Equals, "Duct"),
                new SetRow("", "", "", SetOperator.Equals, ""),
            });

            Assert.Equal(1, plan.Rows);
            Assert.Empty(plan.Problems);
        }

        // ---- the condition-table layout -----------------------------------------------------

        [Fact]
        public void A_condition_table_reads_row_by_row()
        {
            var sheet = Sheet(
                new string?[] { "Folder", "Set", "Category", "Property", "Op", "Value" },
                new string?[] { "Mechanical", "Ducts", "Element", "Category", "=", "Duct" },
                new string?[] { "Mechanical", "Ducts", "Element", "Level", "equals", "L02" },
                new string?[] { "Electrical", "Trays", "Element", "Category", "contains", "Tray" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                Layout = SetSheetLayout.Conditions,
                HeaderRow = 1, FolderColumn = 1, NameColumn = 2,
                CategoryColumn = 3, PropertyColumn = 4, OperatorColumn = 5, ValueColumn = 6,
            });

            Assert.Equal(3, read.Rows.Count);
            Assert.Empty(read.Warnings);

            var plan = SetSchedule.Plan(read.Rows);

            Assert.Equal(2, plan.Sets.Count);
            Assert.Equal(2, plan.Sets[0].Conditions);
            Assert.Equal("Electrical", plan.Sets[1].Folder);
        }

        [Fact]
        public void Operators_are_read_the_way_people_write_them()
        {
            var sheet = Sheet(
                new string?[] { "Set", "Category", "Property", "Op", "Value" },
                new string?[] { "A", "Element", "Name", ">=", "3" },
                new string?[] { "B", "Element", "Name", "starts with", "AHU" },
                new string?[] { "C", "Element", "Name", "nonsense", "x" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                HeaderRow = 1, NameColumn = 1, CategoryColumn = 2,
                PropertyColumn = 3, OperatorColumn = 4, ValueColumn = 5,
            });

            Assert.Equal(SetOperator.GreaterThanOrEqual, read.Rows[0].Operator);
            Assert.Equal(SetOperator.StartsWith, read.Rows[1].Operator);

            // Unreadable falls back to Equals, and says so rather than dropping the row.
            Assert.Equal(SetOperator.Equals, read.Rows[2].Operator);
            Assert.Contains(read.Warnings, w => w.Contains("not an operator"));
        }

        // ---- the one-set-per-row layout -----------------------------------------------------

        [Fact]
        public void A_column_layout_makes_one_condition_per_filled_cell()
        {
            var sheet = Sheet(
                new string?[] { "Set", "Discipline", "Level", "System" },
                new string?[] { "L2 mech", "Mechanical", "L02", "" },
                new string?[] { "L3 supply", "Mechanical", "L03", "Supply air" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                Layout = SetSheetLayout.Columns,
                HeaderRow = 1, NameColumn = 1, FirstValueColumn = 2, Category = "Element",
            });

            // Two filled cells on the first row, three on the second.
            Assert.Equal(5, read.Rows.Count);

            var plan = SetSchedule.Plan(read.Rows);

            Assert.Equal(2, plan.Sets.Count);
            Assert.Equal(2, plan.Sets[0].Conditions);
            Assert.Equal(3, plan.Sets[1].Conditions);
            Assert.Equal("Level", read.Rows[1].Property);
        }

        [Fact]
        public void A_column_with_no_heading_cannot_say_what_property_it_is()
        {
            var sheet = Sheet(
                new string?[] { "Set", "Discipline", "" },
                new string?[] { "L2 mech", "Mechanical", "orphan" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                Layout = SetSheetLayout.Columns,
                HeaderRow = 1, NameColumn = 1, FirstValueColumn = 2, Category = "Element",
            });

            Assert.Single(read.Rows);
            Assert.Contains(read.Warnings, w => w.Contains("no heading"));
        }

        [Fact]
        public void A_row_defining_nothing_is_reported()
        {
            var sheet = Sheet(
                new string?[] { "Set", "Discipline" },
                new string?[] { "Empty one", "" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                Layout = SetSheetLayout.Columns,
                HeaderRow = 1, NameColumn = 1, FirstValueColumn = 2, Category = "Element",
            });

            Assert.Empty(read.Rows);
            Assert.Contains(read.Warnings, w => w.Contains("defines nothing"));
        }

        [Fact]
        public void Rows_below_the_table_are_not_read()
        {
            var sheet = Sheet(
                new string?[] { "Set", "Category", "Property", "Op", "Value" },
                new string?[] { "Ducts", "Element", "Category", "=", "Duct" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "" },
                new string?[] { "Signed off 3 April" });

            var read = SheetSets.Read(sheet, new SetSheetMapping
            {
                HeaderRow = 1, NameColumn = 1, CategoryColumn = 2,
                PropertyColumn = 3, OperatorColumn = 4, ValueColumn = 5,
            });

            Assert.Single(read.Rows);
        }

        [Fact]
        public void No_sheet_is_a_warning_not_an_exception()
        {
            var read = SheetSets.Read(null, new SetSheetMapping());

            Assert.False(read.IsUsable);
            Assert.NotEmpty(read.Warnings);
        }
    }
}
