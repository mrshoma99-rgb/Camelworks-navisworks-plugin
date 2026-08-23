using System;
using System.Linq;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using CamelWorks.Core.Testing;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

namespace CamelWorks.Core.Tests
{
    public class CompareSnapshotTests
    {
        [Fact]
        public void A_snapshot_survives_a_round_trip()
        {
            var snapshot = Snapshot("Rev C", new[]
            {
                Print("Wall 01", x: 1.5, y: -2.25, z: 3, sx: 0.2, sy: 4, sz: 3)
                    .With("Element", "Fire Rating", "120")
                    .With("Element", "Level", "L03"),
                Print("Duct", rung: KeyRung.Geometry, local: "shape"),
            });

            var read = ModelSnapshot.Read(snapshot.Write());

            Assert.NotNull(read);
            Assert.Equal("Rev C", read!.Label);
            Assert.Equal(snapshot.TakenUtc, read.TakenUtc);
            Assert.Equal(2, read.Count);

            var wall = read.Elements[0];

            Assert.Equal("Wall 01", wall.Name);
            Assert.Equal(1.5, wall.CentreX, 6);
            Assert.Equal(-2.25, wall.CentreY, 6);
            Assert.Equal(4, wall.SizeY, 6);
            Assert.Equal("120", wall.ValueOf(new PropertyRef("Element", "Fire Rating")));
            Assert.Equal(KeyRung.Geometry, read.Elements[1].Rung);
        }

        [Fact]
        public void Model_records_survive_the_round_trip()
        {
            var snapshot = Snapshot("A", new[] { Print("Wall") },
                new ModelRecord(ScopeA, "Structure", @"C:\job\STR.nwc")
                {
                    Modified = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000),
                    Bytes = 12345,
                    Items = 42,
                });

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Single(read.Models);
            Assert.Equal("Structure", read.Models[0].DisplayName);
            Assert.Equal(@"C:\job\STR.nwc", read.Models[0].SourcePath);
            Assert.Equal(12345, read.Models[0].Bytes);
            Assert.Equal(42, read.Models[0].Items);
        }

        [Fact]
        public void A_tab_in_a_name_cannot_break_the_format()
        {
            var snapshot = Snapshot("A", new[] { Print("Wall\twith\ta\ttab") });

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Single(read.Elements);
            Assert.Equal("Wall with a tab", read.Elements[0].Name);
        }

        [Fact]
        public void A_property_name_containing_a_slash_reads_back_as_itself()
        {
            // L/100 is a real property on real models, and the naive Category/Name split turns it
            // into a category nobody wrote.
            var snapshot = Snapshot("A", new[] { Print("Beam").With("Structural", "L/100", "12") });
            snapshot.Watch = PropertyWatch.All;

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Equal("12", read.Elements[0].ValueOf(new PropertyRef("Structural", "L/100")));
        }

        [Fact]
        public void The_watched_and_ignored_lists_are_recorded_so_a_comparison_knows_what_it_may_say()
        {
            var snapshot = Snapshot("A", new[] { Print("Wall") });
            snapshot.Watch = PropertyWatch.Named;
            snapshot.Watched = new[] { new PropertyRef("Element", "Fire Rating"), new PropertyRef(null, "Level") };

            var read = ModelSnapshot.Read(snapshot.Write())!;

            Assert.Equal(PropertyWatch.Named, read.Watch);
            Assert.Equal(2, read.Watched.Count);
            Assert.Contains(read.Watched, r => r.Category == "Element" && r.Name == "Fire Rating");
            Assert.Contains(read.Watched, r => r.Category.Length == 0 && r.Name == "Level");
            Assert.True(read.Terms().Watches(new PropertyRef("Element", "Fire Rating")));
            Assert.False(read.Terms().Watches(new PropertyRef("Element", "Comments")));
        }

        [Fact]
        public void Text_that_is_not_a_snapshot_reads_as_nothing_rather_than_throwing()
        {
            Assert.Null(ModelSnapshot.Read(null));
            Assert.Null(ModelSnapshot.Read(string.Empty));
            Assert.Null(ModelSnapshot.Read("{\"not\": \"a snapshot\"}"));
        }

        [Fact]
        public void A_damaged_line_costs_that_line_and_not_the_baseline()
        {
            var text = Snapshot("A", new[] { Print("Wall"), Print("Duct") }).Write();
            var lines = text.Split('\n').ToList();

            lines.Insert(lines.Count - 2, "this\tis\tnot\tan\telement");

            var read = ModelSnapshot.Read(string.Join("\n", lines))!;

            Assert.Equal(2, read.Count);
        }

        [Fact]
        public void Watched_values_come_back_sorted_however_they_were_written()
        {
            // The property comparison is a merge-walk of two ordered lists. An unordered one makes
            // it silently report properties as arrived and departed.
            var print = Print("Wall");
            print.Values = new[]
            {
                new WatchedValue(new PropertyRef("Zeta", "Last"), "1"),
                new WatchedValue(new PropertyRef("Alpha", "First"), "2"),
            };

            Assert.Equal("Alpha", print.Values[0].Property.Category);
            Assert.Equal("Zeta", print.Values[1].Property.Category);
        }

        [Fact]
        public void A_fingerprint_of_a_live_element_carries_its_identity_and_its_box()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");

            var item = document.AddItem(model, "Wall 01", "Walls", "Basic Wall",
                                        new BoundingBox(0, 0, 0, 2, 0.2, 3))
                               .WithProperty("Element", "Fire Rating", "120")
                               .WithProperty("Item", "Source File", @"C:\job\STR.rvt");

            var print = ElementFingerprint.Of(item);

            Assert.Equal(item.Key.Rung, print.Rung);
            Assert.Equal(item.Key.UnscopedValue, print.Local);
            Assert.Equal(item.Key.Value, print.Scoped);
            Assert.Equal(1, print.CentreX, 6);
            Assert.Equal(3, print.SizeZ, 6);

            // Fire Rating is on the standard list; Source File is denied as noise.
            Assert.Equal("120", print.ValueOf(new PropertyRef("Element", "Fire Rating")));
            Assert.Null(print.ValueOf(new PropertyRef("Item", "Source File")));
        }

        [Fact]
        public void A_fingerprint_rebuilds_the_key_it_was_taken_from()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");
            var item = document.AddItem(model, "Wall 01");

            var print = ElementFingerprint.Of(item);

            Assert.True(print.TryKey(out var key));
            Assert.Equal(item.Key, key);
        }

        [Fact]
        public void Watching_nothing_records_nothing()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");
            var item = document.AddItem(model, "Wall").WithProperty("Element", "Level", "L03");

            var print = ElementFingerprint.Of(item, new CompareOptions { Watch = PropertyWatch.None });

            Assert.Empty(print.Values);
        }

        [Fact]
        public void Watching_everything_keeps_what_the_standard_list_leaves_out()
        {
            var document = new FakeDocument();
            var model = document.AddModel("Structure", @"C:\job\STR.nwc");
            var item = document.AddItem(model, "Wall").WithProperty("Custom", "Job Number", "24-118");

            Assert.Empty(ElementFingerprint.Of(item).Values);

            var all = ElementFingerprint.Of(item, new CompareOptions { Watch = PropertyWatch.All });

            Assert.Equal("24-118", all.ValueOf(new PropertyRef("Custom", "Job Number")));
        }

        [Fact]
        public void A_long_value_is_cut_and_says_so()
        {
            var value = new string('x', ComparePolicy.MaxValueLength + 50);

            Assert.Equal(ComparePolicy.MaxValueLength, ComparePolicy.Trim(value).Length);
            Assert.EndsWith("…", ComparePolicy.Trim(value));
        }
    }

    public class SnapshotStoreTests
    {
        private static SnapshotStore Store(InMemoryFileSystem fs) =>
            new SnapshotStore(fs, @"C:\job\.camelworks\compare");

        [Fact]
        public void A_baseline_is_saved_listed_and_read_back()
        {
            var fs = new InMemoryFileSystem();
            var store = Store(fs);

            var entry = store.Save(Snapshot("Rev C", new[] { Print("Wall"), Print("Duct") }));

            Assert.NotNull(entry);
            Assert.Equal(2, entry!.Elements);

            var listed = store.Baselines();

            Assert.Single(listed);
            Assert.Equal("Rev C", listed[0].Label);

            var read = store.Load(listed[0]);

            Assert.Equal(2, read!.Count);
        }

        [Fact]
        public void The_newest_baseline_is_the_one_offered_first()
        {
            var fs = new InMemoryFileSystem();
            var store = Store(fs);

            var older = Snapshot("Rev B", new[] { Print("Wall") });
            older.TakenUtc = DateTimeOffset.FromUnixTimeSeconds(1_600_000_000);

            var newer = Snapshot("Rev C", new[] { Print("Wall") });
            newer.TakenUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

            store.Save(older);
            store.Save(newer);

            Assert.Equal("Rev C", store.Latest()!.Label);
        }

        [Fact]
        public void A_lost_index_is_rebuilt_from_the_files_themselves()
        {
            var fs = new InMemoryFileSystem();
            var store = Store(fs);

            store.Save(Snapshot("Rev C", new[] { Print("Wall") }));

            fs.Delete(@"C:\job\.camelworks\compare\" + SnapshotStore.IndexName);

            var listed = store.Baselines();

            Assert.Single(listed);
            Assert.Equal("Rev C", listed[0].Label);
        }

        [Fact]
        public void Deleting_a_baseline_takes_it_out_of_the_list()
        {
            var fs = new InMemoryFileSystem();
            var store = Store(fs);

            var entry = store.Save(Snapshot("Rev C", new[] { Print("Wall") }))!;

            Assert.True(store.Delete(entry));
            Assert.Empty(store.Baselines());
        }

        [Fact]
        public void With_nowhere_to_save_the_reason_is_said_rather_than_thrown()
        {
            var store = new SnapshotStore(new InMemoryFileSystem(), null);

            Assert.True(store.IsMemoryOnly);
            Assert.Null(store.Save(Snapshot("Rev C", new[] { Print("Wall") })));
            Assert.Contains("never been saved", store.LastProblem);
        }

        [Fact]
        public void The_file_name_leads_with_the_date_so_the_folder_sorts_by_itself()
        {
            var snapshot = Snapshot("Rev C — issue 4", new[] { Print("Wall") });

            var name = SnapshotStore.Name(snapshot);

            Assert.StartsWith("20231114", name);
            Assert.EndsWith(ModelSnapshot.Extension, name);
            Assert.DoesNotContain("—", name);
        }
    }
}
