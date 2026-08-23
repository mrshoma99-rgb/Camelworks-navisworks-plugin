using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Testing;
using Xunit;

namespace CamelWorks.Core.Tests
{
    public class SnapshotBuilderTests
    {
        private static readonly DateTimeOffset When = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        private static FakeDocument Federation(int perModel = 3)
        {
            var document = new FakeDocument();

            var structure = document.AddModel("Structure", @"C:\job\STR.nwc");
            var services = document.AddModel("Services", @"C:\job\MEP.nwc");

            for (var i = 0; i < perModel; i++)
            {
                document.AddItem(structure, "Beam " + i, "Structural Framing", "UB 305x165",
                                 new BoundingBox(i, 0, 0, i + 4, 0.3, 0.6))
                        .WithProperty("Element", "Level", "L0" + i);

                document.AddItem(services, "Duct " + i, "Ducts", "Rectangular Duct",
                                 new BoundingBox(i, 2, 3, i + 2, 2.4, 3.25));
            }

            return document;
        }

        [Fact]
        public void A_snapshot_records_every_element_and_every_model()
        {
            var snapshot = SnapshotBuilder.Take(Federation(), When, "Rev C");

            Assert.Equal(6, snapshot.Count);
            Assert.Equal(2, snapshot.Models.Count);
            Assert.Equal("Rev C", snapshot.Label);
            Assert.Equal(When, snapshot.TakenUtc);
        }

        [Fact]
        public void The_model_records_carry_the_element_counts_the_intake_register_wants()
        {
            var snapshot = SnapshotBuilder.Take(Federation(), When);

            Assert.All(snapshot.Models, m => Assert.Equal(3, m.Items));
        }

        [Fact]
        public void The_settings_a_snapshot_was_taken_under_are_recorded_with_it()
        {
            var options = new CompareSettings
            {
                Watch = PropertyWatch.Named,
                Watched = new[] { new PropertyRef("Element", "Level") },
            };

            var snapshot = SnapshotBuilder.Take(Federation(), When, "Rev C", options);

            Assert.Equal(PropertyWatch.Named, snapshot.Watch);
            Assert.Single(snapshot.Watched);
            Assert.True(snapshot.Terms().Watches(new PropertyRef("Element", "Level")));
        }

        [Fact]
        public void A_snapshot_taken_now_compares_clean_against_itself()
        {
            var document = Federation();

            var a = SnapshotBuilder.Take(document, When, "A");
            var b = SnapshotBuilder.Take(document, When, "B");

            Assert.True(ModelCompare.Of(a, b).IsQuiet);
        }

        [Fact]
        public void An_element_removed_between_two_walks_reads_as_removed()
        {
            var document = Federation();

            var before = SnapshotBuilder.Take(document, When, "A");

            var gone = document.Traverse(TraversalScope.WholeDocument).First(i => i.DisplayName == "Beam 1");
            document.RemoveItem(gone.Key);

            var after = SnapshotBuilder.Take(document, When, "B");
            var result = ModelCompare.Of(before, after);

            Assert.Equal(1, result.Removed);
            Assert.Equal("Beam 1", result.In(ChangeBucket.Removed)[0].Print.Name);
        }

        [Fact]
        public void A_walk_too_short_to_be_stopped_still_reports_when_it_finished()
        {
            SnapshotProgress? last = null;

            var snapshot = SnapshotBuilder.Take(Federation(), When, "A",
                                                keepGoing: () => false,
                                                onProgress: p => last = p);

            // Six elements is fewer than one progress interval, so nothing gets the chance to stop
            // it — and the final report still arrives, which is what the screen binds to.
            Assert.NotNull(last);
            Assert.Equal(snapshot.Count, last!.Kept);
        }

        [Fact]
        public void Progress_is_reported_and_a_long_walk_can_be_stopped()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Big", @"C:\job\BIG.nwc");

            for (var i = 0; i < SnapshotBuilder.ProgressEvery * 2 + 10; i++)
                document.AddItem(model, "Item " + i, bounds: new BoundingBox(i, 0, 0, i + 1, 1, 1));

            var seen = new List<SnapshotProgress>();

            var snapshot = SnapshotBuilder.Take(document, When, "A",
                                                keepGoing: () => seen.Count < 2,
                                                onProgress: seen.Add);

            Assert.True(seen.Count >= 2);
            Assert.True(seen[seen.Count - 1].Stopped);
            Assert.True(snapshot.Count < SnapshotBuilder.ProgressEvery * 2 + 10);
        }

        [Fact]
        public void The_estimate_scales_with_what_is_being_watched()
        {
            var none = SnapshotBuilder.Estimate(100_000, PropertyWatch.None);
            var standard = SnapshotBuilder.Estimate(100_000, PropertyWatch.Standard);
            var all = SnapshotBuilder.Estimate(100_000, PropertyWatch.All);

            Assert.Contains("100,000 elements", none);
            Assert.NotEqual(none, standard);
            Assert.NotEqual(standard, all);
            Assert.Equal("nothing to record", SnapshotBuilder.Estimate(0, PropertyWatch.Standard));
        }

        [Fact]
        public void A_snapshot_of_a_live_document_round_trips_through_its_own_file()
        {
            var snapshot = SnapshotBuilder.Take(Federation(), When, "Rev C");

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Equal(snapshot.Count, read.Count);
            Assert.True(ModelCompare.Of(snapshot, read).IsQuiet);
        }
    }
}
