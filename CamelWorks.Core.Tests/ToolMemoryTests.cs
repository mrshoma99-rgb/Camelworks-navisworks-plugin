using System;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ToolMemoryTests
    {
        private static ProjectStore Store(IFileSystem? fs = null) =>
            ProjectStore.Open(fs ?? new InMemoryFileSystem(), @"C:\Jobs\Riverside\Federated.nwf", null);

        [Fact]
        public void A_tool_used_for_the_first_time_gives_back_exactly_its_fallbacks()
        {
            // Which is what makes it optional: a tool works before there is anything to remember.
            var memory = ToolMemory.Of(Store(), "matrix");

            Assert.True(memory.IsEmpty);
            Assert.Equal(0, memory.Count);

            Assert.Equal(@"C:\default.xlsx", memory.Text("sheet", @"C:\default.xlsx"));
            Assert.Null(memory.Text("sheet"));
            Assert.Equal(3, memory.Number("headerRow", 3));
            Assert.True(memory.Flag("keepGroups", true));
            Assert.False(memory.Flag("keepGroups"));
        }

        [Fact]
        public void What_it_was_told_is_what_it_says_next_time()
        {
            var store = Store();

            ToolMemory.Of(store, "matrix")
                .Set("sheet", @"C:\Jobs\Riverside\matrix.xlsx")
                .Set("headerRow", 4)
                .Set("keepGroups", false)
                .Keep();

            // A second reader of the same project, which is what the next morning is.
            var again = ToolMemory.Of(store, "matrix");

            Assert.Equal(@"C:\Jobs\Riverside\matrix.xlsx", again.Text("sheet"));
            Assert.Equal(4, again.Number("headerRow", 1));
            Assert.False(again.Flag("keepGroups", true));
            Assert.Equal(3, again.Count);
            Assert.False(again.IsEmpty);
        }

        [Fact]
        public void It_survives_the_project_file_itself()
        {
            var fs = new InMemoryFileSystem();

            ToolMemory.Of(Store(fs), "schedule").Set("sheet", @"C:\sets.xlsx").Set("nameColumn", 2).Keep();

            // Reopened from disk, which is the day-two case this exists for.
            var reopened = ToolMemory.Of(Store(fs), "schedule");

            Assert.Equal(@"C:\sets.xlsx", reopened.Text("sheet"));
            Assert.Equal(2, reopened.Number("nameColumn"));
        }

        [Fact]
        public void Two_tools_do_not_share_a_drawer()
        {
            var store = Store();

            ToolMemory.Of(store, "matrix").Set("sheet", "a.xlsx");
            ToolMemory.Of(store, "schedule").Set("sheet", "b.xlsx");

            Assert.Equal("a.xlsx", ToolMemory.Of(store, "matrix").Text("sheet"));
            Assert.Equal("b.xlsx", ToolMemory.Of(store, "schedule").Text("sheet"));
        }

        [Fact]
        public void A_box_somebody_cleared_is_a_box_they_want_empty()
        {
            // A stored empty string is indistinguishable from never having used the tool, which is
            // the one case the fallback exists for.
            var store = Store();

            ToolMemory.Of(store, "report").Set("path", @"C:\out");

            Assert.Equal(@"C:\out", ToolMemory.Of(store, "report").Text("path", "beside the model"));

            ToolMemory.Of(store, "report").Set("path", "   ");

            Assert.Equal("beside the model", ToolMemory.Of(store, "report").Text("path", "beside the model"));
            Assert.Equal(0, ToolMemory.Of(store, "report").Count);
        }

        [Fact]
        public void Text_is_kept_trimmed_because_a_pasted_path_carries_a_space()
        {
            var store = Store();

            ToolMemory.Of(store, "report").Set("path", "  C:\\out  ");

            Assert.Equal(@"C:\out", ToolMemory.Of(store, "report").Text("path"));
        }

        [Fact]
        public void A_number_read_as_a_tick_or_the_other_way_round_gives_the_fallback()
        {
            var store = Store();

            ToolMemory.Of(store, "matrix").Set("headerRow", 4).Set("keepGroups", true);

            var memory = ToolMemory.Of(store, "matrix");

            Assert.Equal(9, memory.Number("keepGroups", 9));
            Assert.True(memory.Flag("headerRow", true));
        }

        [Fact]
        public void Forgetting_one_thing_leaves_the_rest()
        {
            var store = Store();

            var memory = ToolMemory.Of(store, "matrix").Set("sheet", "a.xlsx").Set("headerRow", 4);

            memory.Forget("sheet");

            Assert.Null(memory.Text("sheet"));
            Assert.Equal(4, memory.Number("headerRow"));
            Assert.Equal(1, memory.Count);
        }

        [Fact]
        public void Forgetting_everything_puts_the_tool_back_to_its_first_morning()
        {
            var store = Store();

            var memory = ToolMemory.Of(store, "matrix").Set("sheet", "a.xlsx").Set("headerRow", 4);

            memory.Clear();

            Assert.True(memory.IsEmpty);
            Assert.Equal("a fallback", memory.Text("sheet", "a fallback"));
        }

        [Fact]
        public void A_panel_can_show_what_it_is_holding()
        {
            var memory = ToolMemory.Of(Store(), "matrix")
                .Set("sheet", "a.xlsx")
                .Set("headerRow", 4)
                .Set("keepGroups", true);

            Assert.Equal(new[] { "headerRow", "keepGroups", "sheet" },
                         memory.Everything().Select(p => p.Key));

            Assert.Equal(new[] { "4", "yes", "a.xlsx" }, memory.Everything().Select(p => p.Value));

            Assert.Contains("remembers 3 things", memory.ToString(), StringComparison.Ordinal);
            Assert.Contains("remembers nothing yet", ToolMemory.Of(Store(), "x").ToString(),
                            StringComparison.Ordinal);
        }

        [Fact]
        public void With_no_project_it_reads_its_fallbacks_and_keeps_nothing()
        {
            // A document nobody has saved has no project file yet, and that is an ordinary state
            // rather than a reason for a tool to refuse to open.
            var memory = ToolMemory.Of(null, "matrix");

            memory.Set("sheet", "a.xlsx");

            Assert.Equal("a.xlsx", memory.Text("sheet"));
            Assert.False(memory.Keep());
            Assert.Null(memory.Store);
        }

        [Fact]
        public void A_tool_with_no_name_would_share_the_untitled_drawer_with_every_other_one()
        {
            Assert.Throws<ArgumentException>(() => ToolMemory.Of(Store(), "  "));
            Assert.Throws<ArgumentException>(() => ToolMemory.Of(Store(), "matrix").Set("  ", "x"));
        }
    }
}
