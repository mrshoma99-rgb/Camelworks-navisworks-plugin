using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Data;
using CamelWorks.Core.Publish;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class CompilationTests
    {
        private static SourceFile File(string path, bool exists = true, long bytes = 1000) =>
            new SourceFile(path, exists, bytes);

        private static SheetTable Sheet(params string?[][] rows) =>
            new SheetTable("Register", rows.Select(r => (IReadOnlyList<string?>)r).ToList());

        // ---- Reading a revision out of a name --------------------------------------------

        [Theory]
        [InlineData(@"C:\Drops\Tower_Rev03.rvt", "Tower", "Rev03", 3)]
        [InlineData(@"C:\Drops\Tower-Rev12.rvt", "Tower", "Rev12", 12)]
        [InlineData(@"C:\Drops\Tower Rev 7.ifc", "Tower", "Rev7", 7)]
        [InlineData(@"C:\Drops\Tower_REV_04.rvt", "Tower", "REV04", 4)]
        [InlineData(@"C:\Drops\Tower_R2.dwg", "Tower", "R2", 2)]
        [InlineData(@"C:\Drops\Tower_v11.nwc", "Tower", "v11", 11)]
        public void A_numbered_revision_is_read_off_the_end_and_the_rest_is_the_name(
            string path, string expectedBase, string expectedText, int expectedOrder)
        {
            var file = File(path);

            Assert.Equal(expectedBase, file.Base);
            Assert.Equal(RevisionKind.Number, file.Revision.Kind);
            Assert.Equal(expectedText, file.Revision.Text);
            Assert.Equal(expectedOrder, file.Revision.Order);
        }

        [Theory]
        [InlineData(@"C:\Drops\Tower_RevA.rvt", "Tower", 1)]
        [InlineData(@"C:\Drops\Tower-RevC.rvt", "Tower", 3)]
        [InlineData(@"C:\Drops\Tower_REV_B.rvt", "Tower", 2)]
        [InlineData(@"C:\Drops\Tower_RevAA.rvt", "Tower", 27)]
        public void A_lettered_revision_counts_in_the_way_a_drawing_register_does(
            string path, string expectedBase, int expectedOrder)
        {
            var file = File(path);

            Assert.Equal(expectedBase, file.Base);
            Assert.Equal(RevisionKind.Letter, file.Revision.Kind);
            Assert.Equal(expectedOrder, file.Revision.Order);
        }

        [Theory]
        [InlineData(@"C:\Drops\Tower.rvt", "Tower")]
        [InlineData(@"C:\Drops\Block_02.rvt", "Block_02")]
        [InlineData(@"C:\Drops\Level 3.ifc", "Level 3")]
        [InlineData(@"C:\Drops\Tower_V.rvt", "Tower_V")]
        [InlineData(@"C:\Drops\Rev03.rvt", "Rev03")]
        public void A_name_with_no_revision_in_it_is_left_exactly_as_it_is(string path, string expected)
        {
            // "Block_02" is a building, not a second issue of "Block". Guessing wrong there merges
            // two models into one and hides half a project.
            var file = File(path);

            Assert.Equal(expected, file.Base);
            Assert.True(file.Revision.IsNone);
            Assert.Equal("(no revision)", file.Revision.ToString());
        }

        [Fact]
        public void The_revision_keeps_the_spelling_the_file_had()
        {
            Assert.Equal("Rev03", File(@"C:\Drops\Tower_Rev03.rvt").Revision.Text);
            Assert.Equal("REV04", File(@"C:\Drops\Tower_REV_04.rvt").Revision.Text);
        }

        [Fact]
        public void A_higher_number_and_a_later_letter_each_beat_what_came_before()
        {
            var third = File(@"C:\Tower_Rev03.rvt").Revision;
            var second = File(@"C:\Tower_Rev02.rvt").Revision;

            Assert.True(third.Beats(second));
            Assert.False(second.Beats(third));
            Assert.False(third.Beats(third));

            var c = File(@"C:\Tower_RevC.rvt").Revision;
            var b = File(@"C:\Tower_RevB.rvt").Revision;

            Assert.True(c.Beats(b));
            Assert.False(b.Beats(c));
        }

        [Fact]
        public void A_number_and_a_letter_beat_each_other_not_at_all()
        {
            var number = File(@"C:\Tower_Rev03.rvt").Revision;
            var letter = File(@"C:\Tower_RevC.rvt").Revision;

            Assert.False(number.Beats(letter));
            Assert.False(letter.Beats(number));
            Assert.False(number.Comparable(letter));
            Assert.True(number.Comparable(File(@"C:\Tower_Rev01.rvt").Revision));
            Assert.False(Revision.None.Comparable(Revision.None));
        }

        [Fact]
        public void A_revision_nobody_constructed_reads_as_none_rather_than_throwing()
        {
            var never = default(Revision);

            Assert.True(never.IsNone);
            Assert.Equal(string.Empty, never.Text);
            Assert.Equal("(no revision)", never.ToString());
            Assert.Equal(Revision.None.Kind, never.Kind);
        }

        // ---- What goes in ----------------------------------------------------------------

        [Fact]
        public void An_older_revision_of_the_same_model_is_left_out_and_named()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev01.rvt"),
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Tower_Rev02.rvt"),
                File(@"C:\Drops\Podium_RevA.rvt"),
            }, new CompileRecipe("Weekly"));

            Assert.Null(plan.Refusal);
            Assert.Single(plan.Jobs);
            Assert.Equal(2, plan.Sources);
            Assert.Equal(2, plan.Superseded.Count);

            Assert.All(plan.Superseded, s => Assert.Contains("Rev03", s.Newer.Path, StringComparison.Ordinal));
            Assert.Contains("2 older revisions are left out", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void Keeping_every_revision_is_a_choice_that_can_be_made()
        {
            var files = new[]
            {
                File(@"C:\Drops\Tower_Rev01.rvt"),
                File(@"C:\Drops\Tower_Rev03.rvt"),
            };

            var recipe = new CompileRecipe("All of them") { NewestOnly = false };

            var plan = Compilation.Plan(files, recipe);

            Assert.Equal(2, plan.Sources);
            Assert.Empty(plan.Superseded);
        }

        [Fact]
        public void Two_models_whose_revisions_are_written_differently_are_both_kept_and_reported()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Tower_RevC.rvt"),
            }, new CompileRecipe("Weekly"));

            Assert.Equal(2, plan.Sources);
            Assert.Empty(plan.Superseded);
            Assert.Contains(plan.Notes, n => n.Contains("cannot be ranked", StringComparison.Ordinal));
        }

        [Fact]
        public void The_same_base_in_two_formats_is_two_models_rather_than_two_revisions()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev01.rvt"),
                File(@"C:\Drops\Tower_Rev03.ifc"),
            }, new CompileRecipe("Weekly"));

            Assert.Equal(2, plan.Sources);
            Assert.Empty(plan.Superseded);
        }

        [Fact]
        public void A_file_that_is_not_there_is_counted_rather_than_quietly_skipped()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Gone_Rev01.rvt", exists: false),
            }, new CompileRecipe("Weekly"));

            Assert.Single(plan.Missing);
            Assert.Equal(1, plan.Sources);
            Assert.Contains("1 file is not there", plan.Explain(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_spreadsheet_in_the_delivery_folder_is_not_a_model()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Transmittal.xlsx"),
                File(@"C:\Drops\Cover.pdf"),
            }, new CompileRecipe("Weekly"));

            Assert.Equal(2, plan.Unreadable.Count);
            Assert.Equal(1, plan.Sources);
            Assert.Contains("not something Navisworks reads", plan.Explain(), StringComparison.Ordinal);
        }

        // ---- What comes out --------------------------------------------------------------

        [Fact]
        public void One_combined_file_is_named_after_the_compilation_and_stamped_with_the_day()
        {
            var plan = Compilation.Plan(new[] { File(@"C:\Drops\Tower_Rev03.rvt") },
                                        new CompileRecipe("Riverside federation"), "2026-08-24");

            Assert.Single(plan.Jobs);
            Assert.Equal("Riverside federation 2026-08-24.nwd", plan.Jobs[0].Output);
        }

        [Fact]
        public void Leaving_the_stamp_off_writes_over_last_weeks()
        {
            var recipe = new CompileRecipe("Riverside federation") { Stamped = false };

            var plan = Compilation.Plan(new[] { File(@"C:\Drops\Tower_Rev03.rvt") }, recipe, "2026-08-24");

            Assert.Equal("Riverside federation.nwd", plan.Jobs[0].Output);
        }

        [Fact]
        public void One_file_each_is_named_after_the_model_without_its_revision()
        {
            var recipe = new CompileRecipe("Drops") { Mode = CompileMode.Individual, Stamped = false };

            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Podium_RevA.rvt"),
            }, recipe);

            Assert.Equal(2, plan.Jobs.Count);
            Assert.Equal(new[] { "Tower.nwd", "Podium.nwd" }, plan.Jobs.Select(j => j.Output));
            Assert.All(plan.Jobs, j => Assert.Single(j.Sources));
        }

        [Fact]
        public void A_name_the_file_system_refuses_is_made_into_one_it_accepts()
        {
            var plan = Compilation.Plan(new[] { File(@"C:\Drops\Tower_Rev03.rvt") },
                                        new CompileRecipe("Riverside: Phase 2/3") { Stamped = false });

            Assert.Equal("Riverside- Phase 2-3.nwd", plan.Jobs[0].Output);
        }

        [Fact]
        public void A_combined_NWC_is_called_out_as_the_thing_it_cannot_be()
        {
            var recipe = new CompileRecipe("Weekly") { Format = Compilation.Nwc };

            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Podium_RevA.rvt"),
            }, recipe);

            Assert.Contains(plan.Notes, n => n.Contains("An NWC holds one model", StringComparison.Ordinal));
        }

        [Fact]
        public void An_unknown_output_format_falls_back_to_the_one_a_federation_is_written_as()
        {
            var recipe = new CompileRecipe("Weekly") { Format = ".zip", Stamped = false };

            Assert.EndsWith(".nwd",
                Compilation.Plan(new[] { File(@"C:\Drops\Tower_Rev03.rvt") }, recipe).Jobs[0].Output,
                StringComparison.Ordinal);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Fact]
        public void An_empty_list_says_the_three_ways_of_filling_it()
        {
            var plan = Compilation.Plan(null, new CompileRecipe("Weekly"));

            Assert.NotNull(plan.Refusal);
            Assert.False(plan.IsUsable);
            Assert.Contains("point at a folder", plan.Refusal!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_list_where_nothing_can_go_in_says_which_of_the_two_reasons_it_is()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Gone.rvt", exists: false),
                File(@"C:\Drops\Cover.pdf"),
            }, new CompileRecipe("Weekly"));

            Assert.NotNull(plan.Refusal);
            Assert.Contains("not where the list says", plan.Refusal!, StringComparison.Ordinal);
            Assert.Contains("does not read those formats", plan.Refusal!, StringComparison.Ordinal);
            Assert.Single(plan.Missing);
            Assert.Single(plan.Unreadable);
        }

        // ---- The list from a spreadsheet -------------------------------------------------

        [Fact]
        public void The_column_of_paths_is_found_by_what_is_in_it_rather_than_its_heading()
        {
            var sheet = Sheet(
                new string?[] { "Package", "Issued", "Where it is" },
                new string?[] { "Architecture", "2026-08-01", @"C:\Drops\Tower_Rev03.rvt" },
                new string?[] { "Structure", "2026-08-02", @"C:\Drops\Frame_Rev02.ifc" },
                new string?[] { "Services", "2026-08-02", "not issued yet" });

            var paths = Compilation.Paths(sheet);

            Assert.Equal(2, paths.Count);
            Assert.Contains(@"C:\Drops\Tower_Rev03.rvt", paths, StringComparer.Ordinal);
            Assert.DoesNotContain("not issued yet", paths, StringComparer.Ordinal);
        }

        [Fact]
        public void A_named_column_is_used_when_one_is_named()
        {
            var sheet = Sheet(
                new string?[] { "Native", "Published" },
                new string?[] { @"C:\Native\Tower.rvt", @"C:\Pub\Tower.nwc" });

            Assert.Equal(new[] { @"C:\Pub\Tower.nwc" }, Compilation.Paths(sheet, "Published"));
            Assert.Equal(new[] { @"C:\Native\Tower.rvt" }, Compilation.Paths(sheet, "Native"));
        }

        [Fact]
        public void A_sheet_with_no_paths_in_it_comes_back_empty_rather_than_throwing()
        {
            Assert.Empty(Compilation.Paths(null));
            Assert.Empty(Compilation.Paths(Sheet(new string?[] { "Package" },
                                                 new string?[] { "Architecture" })));
        }

        // ---- The log ---------------------------------------------------------------------

        [Fact]
        public void The_log_says_what_went_in_what_was_left_out_and_what_failed()
        {
            var plan = Compilation.Plan(new[]
            {
                File(@"C:\Drops\Tower_Rev01.rvt"),
                File(@"C:\Drops\Tower_Rev03.rvt"),
                File(@"C:\Drops\Gone_Rev01.rvt", exists: false),
                File(@"C:\Drops\Cover.pdf"),
            }, new CompileRecipe("Weekly"), "2026-08-24");

            var log = Compilation.Log(plan, new[]
            {
                new CompileResult("Weekly 2026-08-24.nwd", 1, 42.5),
            }, "2026-08-24 18:30");

            Assert.Contains("CamelWorks compilation log", log, StringComparison.Ordinal);
            Assert.Contains("bimcamel.com", log, StringComparison.Ordinal);
            Assert.Contains("2026-08-24 18:30", log, StringComparison.Ordinal);

            Assert.Contains("OK      Weekly 2026-08-24.nwd", log, StringComparison.Ordinal);
            Assert.Contains(@"C:\Drops\Tower_Rev03.rvt", log, StringComparison.Ordinal);

            Assert.Contains("LEFT OUT - AN OLDER REVISION", log, StringComparison.Ordinal);
            Assert.Contains("superseded by Tower_Rev03.rvt", log, StringComparison.Ordinal);
            Assert.Contains("LEFT OUT - NOT THERE", log, StringComparison.Ordinal);
            Assert.Contains("LEFT OUT - NOT A FORMAT NAVISWORKS READS", log, StringComparison.Ordinal);
        }

        [Fact]
        public void A_job_that_failed_says_so_in_the_log_with_the_reason()
        {
            var plan = Compilation.Plan(new[] { File(@"C:\Drops\Tower_Rev03.rvt") },
                                        new CompileRecipe("Weekly"), "2026-08-24");

            var log = Compilation.Log(plan, new[]
            {
                new CompileResult("Weekly 2026-08-24.nwd", 1, 3.2, "the file is open in Navisworks"),
            }, "2026-08-24 18:30");

            Assert.Contains("FAILED  Weekly 2026-08-24.nwd", log, StringComparison.Ordinal);
            Assert.Contains("the file is open in Navisworks", log, StringComparison.Ordinal);
        }

        [Fact]
        public void A_run_that_never_started_still_writes_a_log_saying_why()
        {
            var log = Compilation.Log(Compilation.Plan(null, new CompileRecipe("Weekly")), null,
                                      "2026-08-24 18:30");

            Assert.Contains("Nothing was compiled:", log, StringComparison.Ordinal);
            Assert.Contains("There is nothing on the list", log, StringComparison.Ordinal);
        }

        [Fact]
        public void A_log_of_nothing_at_all_is_still_a_log()
        {
            var log = Compilation.Log(null, null);

            Assert.Contains("CamelWorks compilation log", log, StringComparison.Ordinal);
            Assert.Contains("(not recorded)", log, StringComparison.Ordinal);
        }

        // ---- Saving ----------------------------------------------------------------------

        [Fact]
        public void A_compilation_survives_being_written_down_and_read_back()
        {
            var recipe = new CompileRecipe("Weekly federation")
            {
                Mode = CompileMode.Individual,
                Folder = @"C:\Jobs\Riverside\Published",
                Format = Compilation.Nwf,
                NewestOnly = false,
                Stamped = false,
            };

            var back = CompileRecipe.FromJson(recipe.ToJson());

            Assert.Equal("Weekly federation", back.Name);
            Assert.Equal(CompileMode.Individual, back.Mode);
            Assert.Equal(@"C:\Jobs\Riverside\Published", back.Folder);
            Assert.Equal(Compilation.Nwf, back.Format);
            Assert.False(back.NewestOnly);
            Assert.False(back.Stamped);
        }

        [Fact]
        public void An_unreadable_compilation_comes_back_as_the_ordinary_weekly_one()
        {
            var empty = CompileRecipe.FromJson(null);

            Assert.Equal(CompileMode.Combine, empty.Mode);
            Assert.Equal(Compilation.Nwd, empty.Format);
            Assert.True(empty.NewestOnly);
            Assert.True(empty.Stamped);
        }

        [Fact]
        public void Saving_one_under_a_name_already_used_replaces_it()
        {
            var first = new CompileRecipe("Weekly");
            var kept = Compilation.Keep(Compilation.Saved(null), first);

            Assert.Single(Compilation.Saved(kept));

            var second = new CompileRecipe("WEEKLY") { Mode = CompileMode.Individual };
            var again = Compilation.Saved(Compilation.Keep(Compilation.Saved(kept), second));

            Assert.Single(again);
            Assert.Equal(CompileMode.Individual, again[0].Mode);
        }

        [Fact]
        public void A_copy_can_be_edited_without_disturbing_what_was_saved()
        {
            var saved = new CompileRecipe("Weekly");
            var working = saved.Copy();

            working.Name = "Something else";
            working.Mode = CompileMode.Individual;

            Assert.Equal("Weekly", saved.Name);
            Assert.Equal(CompileMode.Combine, saved.Mode);
        }
    }
}
