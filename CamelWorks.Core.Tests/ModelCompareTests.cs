using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Compare;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

namespace CamelWorks.Core.Tests
{
    public class ModelCompareTests
    {
        [Fact]
        public void Identical_snapshots_report_nothing()
        {
            var a = Snapshot("Rev A", new[] { Print("Wall"), Print("Duct", x: 5) });
            var b = Snapshot("Rev B", new[] { Print("Wall"), Print("Duct", x: 5) });

            var result = ModelCompare.Of(a, b);

            Assert.True(result.IsQuiet);
            Assert.Equal(2, result.Unchanged);
            Assert.Equal(0, result.Differences);
        }

        [Fact]
        public void An_element_only_in_the_current_model_is_added()
        {
            var a = Snapshot("Rev A", new[] { Print("Wall") });
            var b = Snapshot("Rev B", new[] { Print("Wall"), Print("Duct") });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.Added);
            Assert.Equal(0, result.Removed);
            Assert.Equal(ChangeBucket.Added, result.Find("Duct")!.Bucket);
        }

        [Fact]
        public void An_element_only_in_the_baseline_is_removed()
        {
            var a = Snapshot("Rev A", new[] { Print("Wall"), Print("Duct") });
            var b = Snapshot("Rev B", new[] { Print("Wall") });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.Removed);
            Assert.Equal(ChangeBucket.Removed, result.Find("Duct")!.Bucket);
        }

        [Fact]
        public void A_move_beyond_the_tolerance_is_a_move()
        {
            var a = Snapshot("Rev A", new[] { Print("Duct", x: 0) });
            var b = Snapshot("Rev B", new[] { Print("Duct", x: 0.4) });

            var result = ModelCompare.Of(a, b);

            var change = result.Find("Duct")!;

            Assert.Equal(ChangeBucket.Moved, change.Bucket);
            Assert.True((change.Kind & ChangeKind.Moved) != 0);
            Assert.Equal(0.4, change.Distance, 6);
        }

        [Fact]
        public void A_move_inside_the_tolerance_is_not_a_move()
        {
            // A re-export nudges the last bits of a coordinate. Reporting that as a move once is a
            // curiosity; reporting a whole model of it every week is why people turn compare off.
            var a = Snapshot("Rev A", new[] { Print("Duct", x: 0) });
            var b = Snapshot("Rev B", new[] { Print("Duct", x: 0.0002) });

            var result = ModelCompare.Of(a, b);

            Assert.True(result.IsQuiet);
        }

        [Fact]
        public void The_tolerance_can_be_tightened()
        {
            var a = Snapshot("Rev A", new[] { Print("Duct", x: 0) });
            var b = Snapshot("Rev B", new[] { Print("Duct", x: 0.0002) });

            var result = ModelCompare.Of(a, b, new CompareSettings { Tolerance = 0.00001 });

            Assert.Equal(1, result.Moved);
        }

        [Fact]
        public void A_change_of_extent_is_a_resize_and_not_a_move()
        {
            var a = Snapshot("Rev A", new[] { Print("Duct", sx: 0.4, sy: 0.25, sz: 0.25) });
            var b = Snapshot("Rev B", new[] { Print("Duct", sx: 0.5, sy: 0.3, sz: 0.25) });

            var result = ModelCompare.Of(a, b);
            var change = result.Find("Duct")!;

            Assert.True((change.Kind & ChangeKind.Resized) != 0);
            Assert.Equal(ChangeBucket.Changed, change.Bucket);
        }

        [Fact]
        public void The_same_extents_in_a_different_order_are_a_rotation()
        {
            var a = Snapshot("Rev A", new[] { Print("Beam", sx: 4, sy: 0.3, sz: 0.6) });
            var b = Snapshot("Rev B", new[] { Print("Beam", sx: 0.3, sy: 4, sz: 0.6) });

            var result = ModelCompare.Of(a, b);
            var change = result.Find("Beam")!;

            Assert.True((change.Kind & ChangeKind.Rotated) != 0);
            Assert.True((change.Kind & ChangeKind.Resized) == 0);
        }

        [Fact]
        public void A_move_and_a_resize_together_belong_with_the_changed_and_not_the_moved()
        {
            var a = Snapshot("Rev A", new[] { Print("Duct", x: 0, sx: 0.4) });
            var b = Snapshot("Rev B", new[] { Print("Duct", x: 2, sx: 0.5) });

            var result = ModelCompare.Of(a, b);
            var change = result.Find("Duct")!;

            Assert.Equal(ChangeBucket.Changed, change.Bucket);
            Assert.True((change.Kind & ChangeKind.Moved) != 0);
            Assert.True((change.Kind & ChangeKind.Resized) != 0);
        }

        [Fact]
        public void A_rename_keeps_the_match_and_is_reported()
        {
            var before = Print("Duct 1");
            var after = Copy(before);
            after.Name = "Duct 001";

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));

            Assert.Equal(1, result.Changed);
            Assert.True((result.Changes[0].Kind & ChangeKind.Renamed) != 0);
            Assert.Contains("Duct 1", result.Changes[0].Summary());
        }

        [Fact]
        public void A_change_of_type_is_reported()
        {
            var before = Print("Duct", type: "400x250");
            var after = Copy(before);
            after.Type = "500x300";

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));

            Assert.True((result.Changes[0].Kind & ChangeKind.Retyped) != 0);
        }

        [Fact]
        public void A_watched_property_change_carries_its_before_and_after()
        {
            var before = Print("Wall").With("Element", "Fire Rating", "60");
            var after = Copy(before);
            after.Values = new[] { new WatchedValue(new PropertyRef("Element", "Fire Rating"), "120") };

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));
            var change = result.Changes[0];

            Assert.True((change.Kind & ChangeKind.PropertyChanged) != 0);
            Assert.Single(change.Properties);
            Assert.Equal("60", change.Properties[0].Before);
            Assert.Equal("120", change.Properties[0].After);
        }

        [Fact]
        public void A_property_neither_side_watches_is_not_a_change()
        {
            // Source File moves on every export. If it counted, every comparison would report the
            // whole model as changed and the feature would be worthless.
            var before = Print("Wall").With("Item", "Source File", @"C:\old\model.rvt");
            var after = Copy(before);
            after.Values = new[] { new WatchedValue(new PropertyRef("Item", "Source File"), @"C:\new\model.rvt") };

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));

            Assert.True(result.IsQuiet);
        }

        [Fact]
        public void A_baseline_that_watched_nothing_says_so_rather_than_reporting_nothing()
        {
            var a = Snapshot("A", new[] { Print("Wall") });
            a.Watch = PropertyWatch.None;

            var b = Snapshot("B", new[] { Print("Wall").With("Element", "Fire Rating", "120") });

            var result = ModelCompare.Of(a, b);

            Assert.True(result.IsQuiet);
            Assert.Contains(result.Notes, n => n.Contains("baseline recorded no properties"));
        }

        [Fact]
        public void Grouping_nodes_take_no_part_unless_asked_for()
        {
            var a = Snapshot("A", new[] { Print("Level 3", hasGeometry: false), Print("Wall") });
            var b = Snapshot("B", new[] { Print("Wall") });

            var quiet = ModelCompare.Of(a, b);

            Assert.True(quiet.IsQuiet);
            Assert.Equal(1, quiet.Skipped);

            var loud = ModelCompare.Of(a, b, new CompareSettings { IncludeGroupingNodes = true });

            Assert.Equal(1, loud.Removed);
        }

        [Fact]
        public void Two_identical_parts_where_one_moved_read_as_one_move_not_a_deletion_and_an_addition()
        {
            // Both are rung 3 and share a key, so nothing can tell them apart but where they are.
            const string same = "shared-shape";

            var a = Snapshot("A", new[]
            {
                Print("Bracket", x: 0, rung: KeyRung.Geometry, local: same),
                Print("Bracket", x: 10, rung: KeyRung.Geometry, local: same),
            });

            var b = Snapshot("B", new[]
            {
                Print("Bracket", x: 0, rung: KeyRung.Geometry, local: same),
                Print("Bracket", x: 10.04, rung: KeyRung.Geometry, local: same),
            });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Removed);
            Assert.Equal(1, result.Moved);
            Assert.Equal(1, result.Unchanged);
        }

        [Fact]
        public void A_strong_match_is_never_re_offered_to_a_weaker_rung()
        {
            var a = Snapshot("A", new[]
            {
                Print("Beam", x: 0, rung: KeyRung.InstanceGuid, local: "guid-1"),
                Print("Beam", x: 1, rung: KeyRung.Geometry, local: "shape"),
            });

            var b = Snapshot("B", new[]
            {
                Print("Beam", x: 0, rung: KeyRung.InstanceGuid, local: "guid-1"),
                Print("Beam", x: 1, rung: KeyRung.Geometry, local: "shape"),
            });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(2, result.Unchanged);
            Assert.Equal(1, result.MatchesByRung[KeyRung.InstanceGuid]);
            Assert.Equal(1, result.MatchesByRung[KeyRung.Geometry]);
        }

        [Fact]
        public void An_element_that_changed_rung_between_revisions_still_matches_on_its_shape()
        {
            // The exporter changed and the instance GUID went away. Without the fallback this is a
            // removal and an addition of the same object in the same place.
            var a = Snapshot("A", new[] { Print("Pump", x: 3, sx: 1.2, sy: 0.8, sz: 0.9,
                                                rung: KeyRung.InstanceGuid, local: "guid-9") });

            var b = Snapshot("B", new[] { Print("Pump", x: 3, sx: 1.2, sy: 0.8, sz: 0.9,
                                                rung: KeyRung.TreePath, local: "path-9") });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Removed);
            Assert.Equal(1, result.Unchanged);
        }

        [Fact]
        public void A_real_addition_and_a_real_deletion_of_the_same_shape_do_not_collapse_into_a_move()
        {
            // The trap in the rung fallback. Both are 1x1x1 of the same category and type, so a
            // fallback keyed on shape alone pairs them and reports one fictitious move instead of
            // the addition and the deletion that actually happened — removing both from the report.
            var a = Snapshot("A", new[] { Print("Old bracket", x: 0) });
            var b = Snapshot("B", new[] { Print("New handrail", x: 40) });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.Added);
            Assert.Equal(1, result.Removed);
            Assert.Equal(0, result.Moved);
        }

        [Fact]
        public void Restricting_the_rung_skips_the_elements_it_excludes_rather_than_calling_them_removed()
        {
            var a = Snapshot("A", new[] { Print("Bracket", rung: KeyRung.Geometry, local: "shape") });
            var b = Snapshot("B", new[] { Print("Bracket", rung: KeyRung.Geometry, local: "shape") });

            var result = ModelCompare.Of(a, b, new CompareSettings { WeakestRung = KeyRung.TreePath });

            Assert.Equal(0, result.Removed);
            Assert.Equal(0, result.Added);
            Assert.Equal(2, result.Skipped);
        }

        [Fact]
        public void Elements_pair_across_two_different_files()
        {
            // The whole reason model pairing exists: different paths mean different scopes, and no
            // scoped key on one side can ever equal one on the other.
            var a = Snapshot("Rev C",
                new[] { Print("Wall", scope: ScopeA, local: "w1") },
                Model(ScopeA, "Structure", @"C:\job\issue-C\STR-MODEL-R03.nwc"));

            var b = Snapshot("Rev D",
                new[] { Print("Wall", scope: ScopeB, local: "w1", x: 2) },
                Model(ScopeB, "Structure", @"C:\job\issue-D\STR-MODEL-R04.nwc"));

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.Moved);
            Assert.Equal(0, result.Added);
            Assert.Equal(0, result.Removed);
        }

        [Fact]
        public void A_model_with_no_counterpart_is_reported_as_such_rather_than_silently_dropped()
        {
            // Two models on each side, so the last-resort "only one either way" rule cannot fire
            // and the unpaired pair really is unpaired.
            var a = Snapshot("A",
                new[] { Print("Wall", scope: ScopeA), Print("Slab", scope: "cccccccc") },
                Model(ScopeA, "Structure", @"C:\job\STR.nwc"),
                Model("cccccccc", "Architecture", @"C:\job\ARC.nwc"));

            var b = Snapshot("B",
                new[] { Print("Duct", scope: ScopeB), Print("Slab", scope: "cccccccc") },
                Model(ScopeB, "Services", @"C:\job\MEP.nwc"),
                Model("cccccccc", "Architecture", @"C:\job\ARC.nwc"));

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.Added);
            Assert.Equal(1, result.Removed);
            Assert.Contains(result.Notes, n => n.Contains("Structure") && n.Contains("reads as removed"));
            Assert.Contains(result.Notes, n => n.Contains("Services") && n.Contains("reads as added"));
        }

        [Fact]
        public void The_cap_limits_the_list_and_never_the_counts()
        {
            var before = Enumerable.Range(0, 40).Select(i => Print("Wall " + i, x: i)).ToList();
            var after = new List<ElementFingerprint>();

            var result = ModelCompare.Of(Snapshot("A", before), Snapshot("B", after),
                                         new CompareSettings { ExampleCap = 5 });

            Assert.Equal(40, result.Removed);
            Assert.Equal(5, result.In(ChangeBucket.Removed).Count);
            Assert.Equal(35, result.Dropped[ChangeBucket.Removed]);
            Assert.Contains(result.Notes, n => n.Contains("not listed here"));
        }

        [Fact]
        public void The_key_list_a_view_is_painted_from_is_never_capped()
        {
            // Colouring from the capped list would leave everything past the fifth uncoloured and
            // looking unchanged, which is worse than not colouring at all.
            var before = Enumerable.Range(0, 40).Select(i => Print("Wall " + i, x: i)).ToList();

            var result = ModelCompare.Of(Snapshot("A", before), Snapshot("B", new List<ElementFingerprint>()),
                                         new CompareSettings { ExampleCap = 5 });

            Assert.Equal(40, result.KeysIn(ChangeBucket.Removed).Count);
        }

        [Fact]
        public void The_unchanged_keys_are_kept_even_though_the_unchanged_rows_are_not()
        {
            // The compare view fades the unchanged elements by name. Collecting their keys only
            // when KeepUnchanged is on would make the fade quietly cover nothing on every default
            // run — a compare view where the changes do not stand out at all.
            var before = Enumerable.Range(0, 30).Select(i => Print("Wall " + i, x: i)).ToList();
            var after = Enumerable.Range(0, 30).Select(i => Print("Wall " + i, x: i)).ToList();

            var result = ModelCompare.Of(Snapshot("A", before), Snapshot("B", after));

            Assert.Empty(result.Changes);
            Assert.Equal(30, result.KeysIn(ChangeBucket.Unchanged).Count);
        }

        [Fact]
        public void Unchanged_rows_are_counted_but_not_listed_unless_asked_for()
        {
            var a = Snapshot("A", new[] { Print("Wall") });
            var b = Snapshot("A", new[] { Print("Wall") });

            Assert.Empty(ModelCompare.Of(a, b).Changes);
            Assert.Single(ModelCompare.Of(a, b, new CompareSettings { KeepUnchanged = true }).Changes);
        }

        [Fact]
        public void A_mesh_hash_catches_a_remodelling_the_bounding_box_cannot_see()
        {
            var before = Print("Pipe");
            before.MeshHash = "aaaa";

            var after = Copy(before);
            after.MeshHash = "bbbb";

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));

            Assert.True((result.Changes[0].Kind & ChangeKind.Remodelled) != 0);
        }

        [Fact]
        public void With_no_mesh_hash_anywhere_the_comparison_says_what_it_cannot_see()
        {
            var result = ModelCompare.Of(Snapshot("A", new[] { Print("Pipe") }),
                                         Snapshot("B", new[] { Print("Pipe") }));

            Assert.Contains(result.Notes, n => n.Contains("shape signature"));
        }

        [Fact]
        public void Weak_matches_are_counted_and_called_out()
        {
            var a = Snapshot("A", new[] { Print("Bracket", rung: KeyRung.Geometry, local: "shape") });
            var b = Snapshot("B", new[] { Print("Bracket", rung: KeyRung.Geometry, local: "shape") });

            var result = ModelCompare.Of(a, b);

            Assert.Equal(1, result.WeakMatches);
            Assert.Contains(result.Notes, n => n.Contains("category, type and size alone"));
        }

        [Fact]
        public void Comparing_against_nothing_is_an_answer_rather_than_a_crash()
        {
            var result = ModelCompare.Of(null, Snapshot("B", new[] { Print("Wall") }));

            Assert.Equal(1, result.Added);
        }

        [Fact]
        public void Every_flag_that_applies_is_set_at_once()
        {
            var before = Print("Duct", x: 0, sx: 0.4, type: "400x250")
                .With("Element", "System Name", "SA-01");

            var after = Copy(before);
            after.CentreX = 3;
            after.SizeX = 0.5;
            after.Name = "Duct 002";
            after.Type = "500x250";
            after.Values = new[] { new WatchedValue(new PropertyRef("Element", "System Name"), "SA-02") };

            var result = ModelCompare.Of(Snapshot("A", new[] { before }), Snapshot("B", new[] { after }));
            var kind = result.Changes[0].Kind;

            Assert.True((kind & ChangeKind.Moved) != 0);
            Assert.True((kind & ChangeKind.Resized) != 0);
            Assert.True((kind & ChangeKind.Renamed) != 0);
            Assert.True((kind & ChangeKind.Retyped) != 0);
            Assert.True((kind & ChangeKind.PropertyChanged) != 0);
        }
    }
}
