using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Testing;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

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
        public void Only_the_elements_the_baseline_measured_are_worth_measuring_again()
        {
            // A shape signature compares against nothing on its own, so an element the baseline
            // never measured is a geometry pass that cannot change any answer.
            var before = Snapshot("A", new[] { Print("A"), Print("B", x: 1), Print("C", x: 2) });
            before.Elements[0].MeshHash = "aaaa";
            before.Elements[1].MeshHash = "bbbb";

            var after = Snapshot("B", new[] { Print("A"), Print("B", x: 1), Print("C", x: 2) });

            var wanted = ModelCompare.WorthDeepening(before, after);

            Assert.Equal(2, wanted.Count);
            Assert.Contains(after.Elements[0].Scoped, wanted);
            Assert.DoesNotContain(after.Elements[2].Scoped, wanted);
        }

        [Fact]
        public void An_element_the_box_already_calls_resized_is_not_worth_measuring()
        {
            var before = Snapshot("A", new[] { Print("Duct", sx: 0.4) });
            before.Elements[0].MeshHash = "aaaa";

            var after = Snapshot("B", new[] { Print("Duct", sx: 0.5) });

            Assert.Empty(ModelCompare.WorthDeepening(before, after));
        }

        [Fact]
        public void An_element_already_measured_on_this_side_is_not_measured_twice()
        {
            var before = Snapshot("A", new[] { Print("Duct") });
            before.Elements[0].MeshHash = "aaaa";

            var after = Snapshot("B", new[] { Print("Duct") });
            after.Elements[0].MeshHash = "bbbb";

            Assert.Empty(ModelCompare.WorthDeepening(before, after));
        }

        [Fact]
        public void With_nothing_measured_in_the_baseline_there_is_nothing_to_measure_now()
        {
            var before = Snapshot("A", new[] { Print("Duct") });
            var after = Snapshot("B", new[] { Print("Duct") });

            Assert.Equal(0, before.Shapes);
            Assert.Empty(ModelCompare.WorthDeepening(before, after));
            Assert.Empty(ModelCompare.WorthDeepening(null, after));
        }

        [Fact]
        public void A_baseline_says_how_many_shapes_it_measured()
        {
            var snapshot = Snapshot("A", new[] { Print("A"), Print("B", x: 1) });
            snapshot.Elements[0].MeshHash = "aaaa";

            Assert.Equal(1, snapshot.Shapes);
            Assert.Equal(1, ModelSnapshot.Read(snapshot.Write())!.Shapes);
        }

        [Fact]
        public void A_run_measured_against_a_baseline_that_was_not_says_so()
        {
            var before = Snapshot("A", new[] { Print("Duct") });
            before.Elements[0].MeshHash = "aaaa";

            var after = Snapshot("B", new[] { Print("Duct") });

            var result = ModelCompare.Of(before, after);

            Assert.Contains(result.Notes, n => n.Contains("this run measured none"));
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

        [Fact]
        public void An_element_the_host_will_not_answer_for_is_counted_rather_than_thrown()
        {
            // The crash-shaped case: the host will not answer for one element - a handle that has
            // gone stale because somebody refreshed a model while the snapshot was being taken.
            // One element must not cost the other two hundred thousand, and a snapshot quietly
            // missing them would compare as that many things removed.
            var document = new AwkwardDocument(Federation(perModel: 2));

            var snapshot = SnapshotBuilder.Take(document, When, "Rev C");

            Assert.Equal(1, snapshot.Unreadable);
            Assert.Equal(document.Total - 1, snapshot.Count);
        }

        /// <summary>A federation with one element the host refuses to describe.</summary>
        private sealed class AwkwardDocument : IModelDocument
        {
            private readonly FakeDocument _real;

            internal AwkwardDocument(FakeDocument real) => _real = real;

            internal int Total => _real.Traverse(TraversalScope.WholeDocument).Count();

            public int Generation => _real.Generation;

            public bool IsEmpty => _real.IsEmpty;

            public IReadOnlyList<IModelSource> Models => _real.Models;

            public IModelItem? Resolve(ElementKey key) => _real.Resolve(key);

            public IEnumerable<IModelItem> Traverse(TraversalScope scope)
            {
                var first = true;

                foreach (var item in _real.Traverse(scope))
                {
                    yield return first ? new Awkward(item) : item;
                    first = false;
                }
            }
        }

        /// <summary>An element that throws the moment anything is read off it.</summary>
        private sealed class Awkward : IModelItem
        {
            private readonly IModelItem _real;

            internal Awkward(IModelItem real) => _real = real;

            public ElementKey Key => _real.Key;

            public string DisplayName => throw new InvalidOperationException("gone");

            public string? Category => throw new InvalidOperationException("gone");

            public string? TypeName => throw new InvalidOperationException("gone");

            public IModelSource Model => _real.Model;

            public IReadOnlyList<string> TreePath => throw new InvalidOperationException("gone");

            public IModelItem? Parent => throw new InvalidOperationException("gone");

            public bool HasGeometry => throw new InvalidOperationException("gone");

            public BoundingBox Bounds => throw new InvalidOperationException("gone");

            public bool IsHidden => throw new InvalidOperationException("gone");

            public string? Property(string category, string name) =>
                throw new InvalidOperationException("gone");

            public IEnumerable<PropertyValue> Properties() =>
                throw new InvalidOperationException("gone");
        }
    }
}
