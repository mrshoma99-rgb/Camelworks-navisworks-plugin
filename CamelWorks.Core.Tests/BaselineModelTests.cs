using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Project;
using CamelWorks.Core.Testing;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

namespace CamelWorks.Core.Tests
{
    public class BaselineModelTests
    {
        private static ModelSnapshot Baseline(params ModelRecord[] models) =>
            Snapshot("Rev C", new List<ElementFingerprint>(), models);

        private static ModelRecord Was(string scope, string name, string source, string? loaded)
        {
            var record = new ModelRecord(scope, name, source);
            record.LoadedPath = loaded;
            return record;
        }

        private static IReadOnlyList<IModelSource> Loaded(params FakeSource[] sources) => sources;

        private static FakeSource Open(string name, string source, string? loaded = null)
        {
            var document = new FakeDocument();
            var model = document.AddModel(name, source);
            model.LoadedPath = loaded ?? source;
            return model;
        }

        [Fact]
        public void An_old_revision_still_on_disk_can_be_brought_in()
        {
            var fs = new InMemoryFileSystem().With(@"C:\job\issue-C\STR.nwc", "x");

            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc")),
                Loaded(Open("Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-D\STR.nwc")),
                fs);

            Assert.Single(models);
            Assert.Equal(BaselineStanding.CanBring, models[0].Standing);
            Assert.Equal(@"C:\job\issue-C\STR.nwc", models[0].Path);
            Assert.Equal(new[] { @"C:\job\issue-C\STR.nwc" }, BaselineModels.ToBring(models));
        }

        [Fact]
        public void The_loaded_file_is_preferred_over_the_authoring_path()
        {
            // SourcePath names a CAD file that has been every revision in turn; only the loaded
            // file names the one revision worth opening.
            var record = Was("a", "Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc");

            var models = BaselineModels.Of(Baseline(record), Loaded(), null);

            Assert.Equal(@"C:\job\issue-C\STR.nwc", models[0].Path);
        }

        [Fact]
        public void A_model_issued_by_overwriting_one_file_has_nothing_left_to_open()
        {
            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"C:\job\STR.nwc", @"C:\job\STR.nwc")),
                Loaded(Open("Structure", @"C:\job\STR.nwc")),
                null);

            Assert.Equal(BaselineStanding.Overwritten, models[0].Standing);
            Assert.Contains("overwritten in place", models[0].Reason);
            Assert.Empty(BaselineModels.ToBring(models));
        }

        [Fact]
        public void A_file_that_is_no_longer_there_says_so_rather_than_failing_on_the_append()
        {
            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc")),
                Loaded(Open("Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-D\STR.nwc")),
                new InMemoryFileSystem());

            Assert.Equal(BaselineStanding.Gone, models[0].Standing);
            Assert.Contains("not there any more", models[0].Reason);
        }

        [Fact]
        public void A_baseline_that_recorded_no_file_cannot_be_opened()
        {
            var models = BaselineModels.Of(
                Baseline(new ModelRecord("a", "Structure", string.Empty)), Loaded(), null);

            Assert.Equal(BaselineStanding.Gone, models[0].Standing);
        }

        [Fact]
        public void Once_it_is_in_it_reads_as_in_and_can_be_taken_back_out()
        {
            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc")),
                Loaded(Open("Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-D\STR.nwc"),
                       Open("Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc")),
                null);

            Assert.Equal(BaselineStanding.AlreadyIn, models[0].Standing);
            Assert.Equal(new[] { @"C:\job\issue-C\STR.nwc" }, BaselineModels.ToSendBack(models));
            Assert.True(BaselineModels.IsHoldingBoth(models));
        }

        [Fact]
        public void Holding_both_revisions_is_something_the_run_tab_has_to_know()
        {
            // With the old revision appended, the next comparison sees both, pairs the baseline
            // model to itself and reports no changes. That is a banner, not a surprise.
            var neither = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\a.rvt", @"C:\c\STR.nwc")),
                Loaded(Open("Structure", @"X:\a.rvt", @"C:\d\STR.nwc")),
                null);

            Assert.False(BaselineModels.IsHoldingBoth(neither));
        }

        [Fact]
        public void Separators_and_case_do_not_make_two_files_out_of_one()
        {
            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\a.rvt", @"C:\JOB\Issue-C\str.NWC")),
                Loaded(Open("Structure", @"X:\a.rvt", @"C:\job\issue-D\STR.nwc"),
                       Open("Structure", @"X:\a.rvt", @"c:/job/issue-c/STR.nwc")),
                null);

            Assert.Equal(BaselineStanding.AlreadyIn, models[0].Standing);
        }

        [Fact]
        public void The_readout_counts_each_kind()
        {
            var models = BaselineModels.Of(
                Baseline(Was("a", "Structure", @"X:\a.rvt", @"C:\c\STR.nwc"),
                         Was("b", "Services", @"C:\job\MEP.nwc", @"C:\job\MEP.nwc")),
                Loaded(Open("Structure", @"X:\a.rvt", @"C:\d\STR.nwc"),
                       Open("Services", @"C:\job\MEP.nwc")),
                null);

            var explained = BaselineModels.Explain(models);

            Assert.Contains("1 can be brought in", explained);
            Assert.Contains("1 overwritten in place", explained);
        }

        [Fact]
        public void Nothing_to_work_from_is_an_empty_answer_rather_than_a_crash()
        {
            Assert.Empty(BaselineModels.Of(null, null, null));
            Assert.Empty(BaselineModels.ToBring(null));
            Assert.Empty(BaselineModels.ToSendBack(null));
            Assert.False(BaselineModels.IsHoldingBoth(null));
            Assert.Contains("named no models", BaselineModels.Explain(null));
        }

        [Fact]
        public void The_loaded_path_survives_the_snapshot_round_trip()
        {
            var snapshot = Snapshot("Rev C", new[] { Print("Wall") },
                Was("aaaaaaaa", "Structure", @"X:\authoring\STR.rvt", @"C:\job\issue-C\STR.nwc"));

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Equal(@"C:\job\issue-C\STR.nwc", read.Models[0].LoadedPath);
        }

        [Fact]
        public void A_snapshot_from_before_the_field_existed_still_reads()
        {
            var text = Snapshot("Rev C", new[] { Print("Wall") }).Write();

            // Strip the last field off the model line, which is what an older writer produced.
            var lines = text.Split('\n').Select(line =>
                line.StartsWith("model\t", StringComparison.Ordinal)
                    ? string.Join("\t", line.Split('\t').Take(7))
                    : line);

            var read = ModelSnapshot.Read(string.Join("\n", lines))!;

            Assert.Single(read.Models);
            Assert.Null(read.Models[0].LoadedPath);
        }
    }
}
