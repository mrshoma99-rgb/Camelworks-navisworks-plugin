using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Measurement;
using CamelWorks.Core.Project;
using CamelWorks.Core.Store;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class ModelManifestTests
    {
        private static readonly DateTimeOffset Monday =
            new DateTimeOffset(2026, 8, 17, 8, 0, 0, TimeSpan.Zero);

        private static readonly DateTimeOffset Friday =
            new DateTimeOffset(2026, 8, 21, 17, 0, 0, TimeSpan.Zero);

        private static FakeDocument Federation(DateTimeOffset structuralWritten, bool moved = false)
        {
            var document = new FakeDocument();

            var arch = document.AddModel("ARCH.nwc", @"C:\p\ARCH.nwc");
            arch.SourceModifiedUtc = Monday;
            arch.SourceBytes = 4_194_304;
            arch.ModelUnits = "Meters";

            var str = document.AddModel("STR.nwc", @"C:\p\STR.nwc");
            str.SourceModifiedUtc = structuralWritten;
            str.SourceBytes = 8_388_608;
            str.ModelUnits = "Millimeters";
            str.HasTransform = moved;

            return document;
        }

        /// <summary>A federation whose structural model sits at the given translation.</summary>
        private static FakeDocument Placed(double x, double y, double z)
        {
            var document = Federation(Monday);

            foreach (FakeSource model in document.Models)
                model.Placement = new[] { 1d, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };

            ((FakeSource)document.Models[1]).Placement =
                new[] { 1d, 0, 0, 0, 1, 0, 0, 0, 1, x, y, z };

            return document;
        }

        [Fact]
        public void The_manifest_says_what_the_run_was_run_against()
        {
            var manifest = ModelManifest.Of(Federation(Monday).Models, version: "1.0.0",
                                            takenUtc: Friday);

            Assert.Equal(2, manifest.Models.Count);

            var structural = manifest.Models[1];

            Assert.Equal("STR.nwc", structural.DisplayName);
            Assert.Equal(@"C:\p\STR.nwc", structural.SourcePath);
            Assert.Equal("2026-08-17 08:00Z", structural.When);
            Assert.Equal("8.0 MB", structural.Size);
            Assert.Equal("Millimeters", structural.ModelUnits);
            Assert.False(structural.HasTransform);
        }

        [Fact]
        public void A_model_whose_source_cannot_be_read_says_so_rather_than_guessing()
        {
            var document = new FakeDocument();
            document.AddModel("OFFLINE.nwc", @"\\share\OFFLINE.nwc");

            var record = Assert.Single(ModelManifest.Of(document.Models).Models);

            Assert.Equal(Measure.Nothing, record.When);
            Assert.Equal(Measure.Nothing, record.Size);
            Assert.Contains("1 with no readable source", ModelManifest.Of(document.Models).Explain());
        }

        [Fact]
        public void A_moved_model_is_named_as_moved_because_that_is_the_silent_killer()
        {
            var manifest = ModelManifest.Of(Federation(Monday, moved: true).Models);

            Assert.Contains("1 moved by the federation", manifest.Explain());

            Assert.Equal("moved by the federation", manifest.ModelTable().Rows[1][5]);
            Assert.Equal("as authored", manifest.ModelTable().Rows[0][5]);
        }

        [Fact]
        public void Counts_and_extents_land_on_the_record_when_somebody_measured_them()
        {
            var document = Federation(Monday);

            var manifest = ModelManifest.Of(document.Models, itemCounts:
                new Dictionary<string, int> { [document.Models[0].Scope] = 12_400 },
                bounds: new Dictionary<string, BoundingBox>
                {
                    [document.Models[0].Scope] = new BoundingBox(0, 0, 0, 40, 25, 12),
                });

            Assert.Equal(12_400, manifest.Models[0].Items);
            Assert.Null(manifest.Models[1].Items);

            var row = manifest.ModelTable(Measure.Millimetres).Rows[0];

            Assert.Equal("12,400", row[6]);
            Assert.Equal("40,000 x 25,000 x 12,000", row[7]);
            Assert.Equal(Measure.Nothing, manifest.ModelTable().Rows[1][7]);
        }

        [Fact]
        public void The_tests_and_when_they_last_ran_go_on_the_manifest_too()
        {
            var manifest = new ModelManifest(null, new[]
            {
                new TestRecord("L03 MEP v STR", Friday, 214),
                new TestRecord("Never run"),
            });

            Assert.Equal("2026-08-21 17:00Z", manifest.Tests[0].When);
            Assert.Equal("never run", manifest.Tests[1].When);
            Assert.Equal("214", manifest.TestTable().Rows[0][2]);
            Assert.Equal(Measure.Nothing, manifest.TestTable().Rows[1][2]);
        }

        [Fact]
        public void It_writes_down_and_reads_back_whole()
        {
            var document = Federation(Monday, moved: true);

            var before = ModelManifest.Of(document.Models,
                tests: new[] { new TestRecord("L03 MEP v STR", Friday, 214) },
                itemCounts: new Dictionary<string, int> { [document.Models[0].Scope] = 12_400 },
                bounds: new Dictionary<string, BoundingBox>
                {
                    [document.Models[0].Scope] = new BoundingBox(0, 0, 0, 40, 25, 12),
                },
                version: "1.0.0", takenUtc: Friday);

            var after = ModelManifest.FromJson(JsonReader.Parse(before.ToJson().ToJson()));

            Assert.Equal(2, after.Models.Count);
            Assert.Equal("1.0.0", after.Version);
            Assert.Equal(Friday, after.TakenUtc);
            Assert.Equal(Monday, after.Models[1].Modified);
            Assert.Equal(8_388_608, after.Models[1].Bytes);
            Assert.True(after.Models[1].HasTransform);
            Assert.Equal(12_400, after.Models[0].Items);
            Assert.Equal(40, after.Models[0].Bounds!.Value.MaxX, 6);
            Assert.Equal(214, after.Tests[0].Results);
        }

        [Fact]
        public void Anything_unreadable_reads_back_as_an_empty_manifest_rather_than_throwing()
        {
            Assert.True(ModelManifest.FromJson(null).IsEmpty);
            Assert.True(ModelManifest.FromJson(JsonValue.Array()).IsEmpty);
            Assert.Equal("nothing loaded", ModelManifest.FromJson(JsonValue.Object()).Explain());
        }

        // ---------------------------------------------------------------------------------
        // The intake register
        // ---------------------------------------------------------------------------------

        [Fact]
        public void A_first_run_has_nothing_to_compare_against_and_says_so()
        {
            var intake = ModelManifest.Of(Federation(Monday).Models).Since(null);

            Assert.False(intake.HasPrevious);
            Assert.Equal(2, intake.Arrived.Count);
            Assert.Contains("first run", intake.Explain());
        }

        [Fact]
        public void A_week_where_nobody_published_is_worth_saying_out_loud()
        {
            // A run against exactly last week's files produces exactly last week's board, and a
            // coordinator who does not know that spends the meeting explaining why nothing moved.
            var last = ModelManifest.Of(Federation(Monday).Models);
            var now = ModelManifest.Of(Federation(Monday).Models);

            var intake = now.Since(last);

            Assert.True(intake.NothingNew);
            Assert.Equal(2, intake.Unchanged.Count);
            Assert.Contains("nothing has been published", intake.Explain());
        }

        [Fact]
        public void A_republished_model_is_named()
        {
            var last = ModelManifest.Of(Federation(Monday).Models);
            var now = ModelManifest.Of(Federation(Friday).Models);

            var intake = now.Since(last);

            Assert.False(intake.NothingNew);
            Assert.Equal("STR.nwc", Assert.Single(intake.Changed).DisplayName);
            Assert.Equal("ARCH.nwc", Assert.Single(intake.Unchanged).DisplayName);
            Assert.Contains("1 republished", intake.Explain());
        }

        [Fact]
        public void A_model_that_stopped_being_loaded_is_not_silently_forgotten()
        {
            var last = ModelManifest.Of(Federation(Monday).Models);

            var thin = new FakeDocument();
            var arch = thin.AddModel("ARCH.nwc", @"C:\p\ARCH.nwc");
            arch.SourceModifiedUtc = Monday;
            arch.SourceBytes = 4_194_304;

            var intake = ModelManifest.Of(thin.Models).Since(last);

            Assert.Equal("STR.nwc", Assert.Single(intake.Missing).DisplayName);
            Assert.Contains("1 no longer loaded", intake.Explain());
            Assert.Contains(intake.Table().Rows, r => r[2] == "no longer loaded");
        }

        [Fact]
        public void A_model_the_federation_has_moved_is_named_as_moved()
        {
            // The classic silent killer: somebody drags a model in the units-and-transform dialog,
            // nobody is told, and every clash from that moment on is in the wrong place.
            var last = ModelManifest.Of(Placed(0, 0, 0).Models);
            var now = ModelManifest.Of(Placed(0, 0, 1.5).Models);

            var intake = now.Since(last);

            Assert.Equal("STR.nwc", Assert.Single(intake.Moved).DisplayName);

            // Not "nothing has been published": the file is untouched, and that early answer would bury
            // exactly the thing worth reporting.
            Assert.False(intake.NothingNew);
            Assert.Contains("MOVED BY THE FEDERATION", intake.Explain());
            Assert.Contains(intake.Table().Rows, r => (r[2] ?? string.Empty).Contains("moved"));
        }

        [Fact]
        public void The_previous_placement_is_kept_so_the_model_can_be_put_back()
        {
            var last = ModelManifest.Of(Placed(0, 0, 0).Models);

            var intake = ModelManifest.Of(Placed(0, 0, 1.5).Models).Since(last);

            var moved = Assert.Single(intake.Moved);

            Assert.True(intake.Before.TryGetValue(moved.Scope, out var was));
            Assert.Equal(0, was!.Placement![11], 6);
            Assert.Equal(1.5, moved.Placement![11], 6);
        }

        [Fact]
        public void A_placement_that_wobbles_in_the_last_bit_of_a_double_is_not_a_move()
        {
            // A re-export nudges the last bit, and a health check that cried wolf every week is one
            // people learn to skip.
            var last = ModelManifest.Of(Placed(0, 0, 0).Models);

            Assert.Empty(ModelManifest.Of(Placed(0, 0, 0.00001).Models).Since(last).Moved);
        }

        [Fact]
        public void A_placement_survives_being_written_down()
        {
            var before = ModelManifest.Of(Placed(1, 2, 3).Models);
            var after = ModelManifest.FromJson(JsonReader.Parse(before.ToJson().ToJson()));

            Assert.Equal(3, after.Models[1].Placement![11], 6);
            Assert.False(after.Models[1].MovedSince(before.Models[1]));
        }

        [Fact]
        public void A_model_whose_placement_nobody_recorded_is_not_reported_as_moved()
        {
            // Null means the host would not say, and "would not say" is not "has moved".
            var last = ModelManifest.Of(Federation(Monday).Models);

            Assert.Empty(ModelManifest.Of(Placed(0, 0, 9).Models).Since(last).Moved);
        }

        [Fact]
        public void A_model_re_pointed_at_a_new_folder_is_still_the_same_model()
        {
            // Compared by scope rather than by path. Otherwise a re-point reads as one arrival and
            // one disappearance, and the register says two things happened when one did.
            var last = ModelManifest.Of(Federation(Monday).Models);

            var moved = new List<ModelRecord>();

            foreach (var record in last.Models)
                moved.Add(new ModelRecord(record.Scope, record.DisplayName,
                                          record.SourcePath.Replace(@"C:\p\", @"D:\archive\"))
                {
                    Modified = record.Modified,
                    Bytes = record.Bytes,
                });

            var intake = new ModelManifest(moved).Since(last);

            Assert.Empty(intake.Arrived);
            Assert.Empty(intake.Missing);
            Assert.Equal(2, intake.Unchanged.Count);
        }
    }
}
