using System;
using System.Linq;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class CodeTableTests
    {
        private static CodeTable Uniclass() => CodeTable.Parse("Uniclass", string.Join("\n", new[]
        {
            "# the ones this project uses",
            "Ss_25_10_30, Wall systems",
            "Ss_30_12_25, Floor systems",
            "Ss_65_40_33, Ductwork systems, Ss_65",
        }));

        [Fact]
        public void A_pasted_column_becomes_a_table()
        {
            // A code list arrives as a column out of a spreadsheet, and asking somebody to retype it
            // into a grid is how a feature goes unused.
            var table = Uniclass();

            Assert.Equal("Uniclass", table.Name);
            Assert.Equal(3, table.Entries.Count);
            Assert.Equal("Ss_25_10_30", table.Entries[0].Code);
            Assert.Equal("Wall systems", table.Entries[0].Description);
            Assert.Null(table.Entries[0].Parent);
            Assert.Equal("Ss_65", table.Entries[2].Parent);
        }

        [Theory]
        [InlineData("code\tdescription")]
        [InlineData("code,description")]
        [InlineData("code;description")]
        public void Tabs_commas_and_semicolons_all_separate(string line)
        {
            var entry = Assert.Single(CodeTable.Parse("t", line).Entries);

            Assert.Equal("code", entry.Code);
            Assert.Equal("description", entry.Description);
        }

        [Fact]
        public void Blank_lines_and_comments_are_skipped()
        {
            Assert.Empty(CodeTable.Parse("t", "\n\n# nothing here\n   \n").Entries);
        }

        [Fact]
        public void The_same_code_twice_is_one_row()
        {
            Assert.Single(CodeTable.Parse("t", "A, one\nA, two").Entries);
        }

        [Fact]
        public void A_value_typed_by_a_person_is_matched_forgivingly()
        {
            // A code typed by a person has a trailing space and a lowercase letter in it about a
            // third of the time, and refusing those makes the table an obstacle rather than a
            // standard.
            var table = Uniclass();

            Assert.True(table.Knows("Ss_25_10_30"));
            Assert.True(table.Knows("  ss_25_10_30  "));
            Assert.False(table.Knows("Ss_25_10_31"));
            Assert.False(table.Knows(null));
            Assert.False(table.Knows("   "));
        }

        [Fact]
        public void What_gets_written_is_the_table_spelling_not_the_typed_one()
        {
            // So a column ends up with one form of each code rather than four.
            Assert.Equal("Ss_25_10_30", Uniclass().Canonical(" ss_25_10_30 "));
            Assert.Null(Uniclass().Canonical("Ss_99"));
        }

        [Fact]
        public void Coverage_counts_coded_uncoded_and_invalid_apart()
        {
            // A green tick over a column full of near-misses is worth less than a figure with an
            // invalid count beside it.
            var coverage = Uniclass().Check(new[]
            {
                "Ss_25_10_30", "Ss_30_12_25", null, "  ", "Ss_25_10_31", "Ss_25_10_31", "nonsense",
            });

            Assert.Equal(2, coverage.Coded);
            Assert.Equal(2, coverage.Uncoded);
            Assert.Equal(3, coverage.Invalid);
            Assert.False(coverage.IsClean);

            // Most common first, so the one worth fixing is at the front.
            Assert.Equal("Ss_25_10_31", coverage.InvalidValues[0]);
            Assert.Contains("2 of 7 coded", coverage.Explain());
            Assert.Contains("Ss_25_10_31", coverage.Explain());
        }

        [Fact]
        public void A_scope_where_everything_listed_is_listed_is_clean()
        {
            var coverage = Uniclass().Check(new[] { "Ss_25_10_30", null });

            Assert.True(coverage.IsClean);
            Assert.Empty(coverage.InvalidValues);
        }

        [Fact]
        public void Nothing_to_check_is_a_readable_answer()
        {
            var coverage = Uniclass().Check(null);

            Assert.Equal(0, coverage.Coded);
            Assert.True(coverage.IsClean);
            Assert.Contains("0 of 0 coded", coverage.Explain());
        }

        [Fact]
        public void It_writes_down_and_reads_back_whole()
        {
            var back = CodeTable.FromJson("Uniclass",
                JsonReader.Parse(Uniclass().ToJson().ToJson()));

            Assert.Equal(3, back.Entries.Count);
            Assert.Equal("Ductwork systems", back.Entries[2].Description);
            Assert.Equal("Ss_65", back.Entries[2].Parent);
            Assert.True(back.Knows("Ss_30_12_25"));
        }

        [Fact]
        public void What_it_prints_it_can_read_back()
        {
            var back = CodeTable.Parse("Uniclass", Uniclass().ToText());

            Assert.Equal(3, back.Entries.Count);
            Assert.Equal("Ss_65", back.Entries[2].Parent);
            Assert.Equal("Wall systems", back.Entries[0].Description);
        }

        [Fact]
        public void Anything_unreadable_reads_back_as_an_empty_table()
        {
            Assert.True(CodeTable.FromJson("t", null).IsEmpty);
            Assert.True(CodeTable.FromJson("t", JsonValue.Object()).IsEmpty);
            Assert.True(CodeTable.FromJson("t", JsonValue.Array(new[] { JsonValue.String("x") })).IsEmpty);
        }

        [Fact]
        public void A_table_with_no_name_still_has_one()
        {
            Assert.Equal("Codes", new CodeTable("   ").Name);
        }

        // ---------------------------------------------------------------------------------
        // The set a project keeps
        // ---------------------------------------------------------------------------------

        [Fact]
        public void Tables_are_kept_by_name_and_come_back_in_order()
        {
            var tables = new CodeTables();

            tables.Put(Uniclass());
            tables.Put(CodeTable.Parse("Cost codes", "01, Preliminaries"));

            Assert.Equal(new[] { "Cost codes", "Uniclass" }, tables.Names);
            Assert.Equal(3, tables.Find("uniclass")!.Entries.Count);
            Assert.Null(tables.Find("Nothing"));
        }

        [Fact]
        public void An_empty_table_is_removed_rather_than_kept()
        {
            // Somebody clearing the box means "I do not want this table", not "keep an empty one".
            var tables = new CodeTables();

            tables.Put(Uniclass());
            tables.Put(CodeTable.Parse("Uniclass", string.Empty));

            Assert.True(tables.IsEmpty);
        }

        [Fact]
        public void The_set_writes_down_and_reads_back()
        {
            var tables = new CodeTables();
            tables.Put(Uniclass());
            tables.Put(CodeTable.Parse("Cost codes", "01, Preliminaries"));

            var back = CodeTables.FromJson(JsonReader.Parse(tables.ToJson().ToJson()));

            Assert.Equal(2, back.Names.Count);
            Assert.True(back.Find("Uniclass")!.Knows("Ss_65_40_33"));
            Assert.Equal("Preliminaries", back.Find("Cost codes")!.Entries[0].Description);
        }

        [Fact]
        public void An_unreadable_set_is_no_tables_rather_than_an_error()
        {
            Assert.True(CodeTables.FromJson(null).IsEmpty);
            Assert.True(CodeTables.FromJson(JsonValue.Array()).IsEmpty);
            Assert.Equal("no code tables", CodeTables.FromJson(null).ToString());
        }
    }
}
