using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Compare;
using Xunit;
using static CamelWorks.Core.Tests.CompareFixtures;

namespace CamelWorks.Core.Tests
{
    public class CompareClusterTests
    {
        private static CompareResult Between(IEnumerable<ElementFingerprint> before,
                                             IEnumerable<ElementFingerprint> after) =>
            ModelCompare.Of(Snapshot("Rev C", before), Snapshot("Rev D", after));

        [Fact]
        public void Changes_in_one_place_are_one_place()
        {
            var before = new List<ElementFingerprint>();
            var after = Enumerable.Range(0, 5).Select(i => Print("New " + i, x: i)).ToList();

            var clusters = CompareClusters.Of(Between(before, after));

            Assert.Single(clusters);
            Assert.Equal(5, clusters[0].Count);
            Assert.Equal(ChangeBucket.Added, clusters[0].Bucket);
        }

        [Fact]
        public void Changes_a_long_way_apart_are_different_places()
        {
            var after = new[] { Print("Near", x: 0), Print("Far", x: 500) };

            var clusters = CompareClusters.Of(Between(new List<ElementFingerprint>(), after));

            Assert.Equal(2, clusters.Count);
        }

        [Fact]
        public void A_run_that_steps_across_a_cell_corner_is_still_one_place()
        {
            // The reason cells are merged rather than reported as they fall: a riser that steps
            // sideways as it rises occupies cells that meet only at a corner, and two viewpoints
            // of one riser is two viewpoints of the same problem.
            var after = new[]
            {
                Print("A", x: 5, y: 5, z: 5),
                Print("B", x: 15, y: 15, z: 15),
                Print("C", x: 25, y: 25, z: 25),
            };

            var clusters = CompareClusters.Of(Between(new List<ElementFingerprint>(), after), cell: 10);

            Assert.Single(clusters);
            Assert.Equal(3, clusters[0].Count);
        }

        [Fact]
        public void The_biggest_place_comes_first()
        {
            var after = new[]
            {
                Print("Lonely", x: 500),
                Print("A", x: 0), Print("B", x: 1), Print("C", x: 2),
            };

            var clusters = CompareClusters.Of(Between(new List<ElementFingerprint>(), after));

            Assert.Equal(3, clusters[0].Count);
            Assert.Equal(1, clusters[1].Count);
        }

        [Fact]
        public void A_cluster_of_removed_elements_carries_no_keys_and_still_carries_its_extent()
        {
            // The one thing people most want to look at, and the one thing a key cannot reach:
            // the element is not in this document. Its bounds are, so a viewpoint can be framed
            // on where it used to be.
            var before = new[] { Print("Gone", x: 10, y: 20, z: 30, sx: 2, sy: 2, sz: 2) };

            var clusters = CompareClusters.Of(Between(before, new List<ElementFingerprint>()));

            Assert.Single(clusters);
            Assert.True(clusters[0].IsGone);
            Assert.Empty(clusters[0].Keys);
            Assert.Equal(9, clusters[0].Bounds.MinX, 6);
            Assert.Equal(31, clusters[0].Bounds.MaxZ, 6);
        }

        [Fact]
        public void A_cluster_of_live_elements_carries_the_keys_a_viewpoint_zooms_to()
        {
            var after = new[] { Print("New", x: 1), Print("Also new", x: 2) };

            var clusters = CompareClusters.Of(Between(new List<ElementFingerprint>(), after));

            Assert.Equal(2, clusters[0].Keys.Count);
            Assert.False(clusters[0].IsGone);
        }

        [Fact]
        public void The_title_says_what_is_in_it_rather_than_only_how_many()
        {
            var before = new[] { Print("Gone", x: 0) };
            var after = new[] { Print("New", x: 1) };

            var cluster = CompareClusters.Of(Between(before, after)).Single();

            Assert.Contains("1 added", cluster.Title);
            Assert.Contains("1 removed", cluster.Title);
        }

        [Fact]
        public void Unchanged_elements_are_not_places_worth_looking_at()
        {
            var same = new[] { Print("Kept") };

            Assert.Empty(CompareClusters.Of(Between(same, same)));
        }

        [Fact]
        public void The_walk_is_numbered_so_it_reads_in_order_in_the_viewpoint_tree()
        {
            var after = new[] { Print("New", x: 0) };
            var cluster = CompareClusters.Of(Between(new List<ElementFingerprint>(), after)).Single();

            Assert.StartsWith("01 — ", CompareClusters.Name(cluster, 1, 9));
            Assert.StartsWith("001 — ", CompareClusters.Name(cluster, 1, 120));
        }

        [Fact]
        public void The_folder_names_the_baseline_so_two_weeks_of_them_do_not_collide()
        {
            var result = Between(new List<ElementFingerprint>(), new[] { Print("New") });

            Assert.Equal("Compare — since Rev C", CompareClusters.Folder(result));
            Assert.Equal("Compare", CompareClusters.Folder(null));
        }

        [Fact]
        public void The_cap_keeps_the_biggest_and_never_the_first_that_happened_to_be_found()
        {
            var after = Enumerable.Range(0, 20).Select(i => Print("New " + i, x: i * 500)).ToList();
            after.Add(Print("Crowd A", x: 0));
            after.Add(Print("Crowd B", x: 1));

            var clusters = CompareClusters.Of(Between(new List<ElementFingerprint>(), after), cap: 3);

            Assert.Equal(3, clusters.Count);
            Assert.Equal(3, clusters[0].Count);
        }

        [Fact]
        public void A_cell_with_no_size_is_refused_rather_than_dividing_by_it()
        {
            var result = Between(new List<ElementFingerprint>(), new[] { Print("New") });

            Assert.Throws<ArgumentOutOfRangeException>(() => CompareClusters.Of(result, cell: 0));
        }

        [Fact]
        public void Nothing_compared_is_no_places_rather_than_a_crash()
        {
            Assert.Empty(CompareClusters.Of(null));
        }
    }
}
