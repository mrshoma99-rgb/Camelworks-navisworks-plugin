using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Compare
{
    /// <summary>One place in the building where something happened.</summary>
    public sealed class ChangeCluster
    {
        internal ChangeCluster(IReadOnlyList<ElementChange> changes, BoundingBox bounds,
                              IReadOnlyList<ElementKey> keys)
        {
            Changes = changes;
            Bounds = bounds;
            Keys = keys;
        }

        /// <summary>The changes in it.</summary>
        public IReadOnlyList<ElementChange> Changes { get; }

        /// <summary>What it covers, in world coordinates.</summary>
        public BoundingBox Bounds { get; }

        /// <summary>
        /// The elements in it that are in the current document.
        /// </summary>
        /// <remarks>
        /// Empty for a cluster of removed elements, which is not a gap: <see cref="Bounds"/> is
        /// recorded either way, so a viewpoint can still be framed on where they used to be. That
        /// is the one thing people most want to look at and the one thing a key cannot reach.
        /// </remarks>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>How many changes it holds.</summary>
        public int Count => Changes.Count;

        /// <summary>How many of each kind.</summary>
        public IReadOnlyDictionary<ChangeBucket, int> ByBucket =>
            Changes.GroupBy(c => c.Bucket).ToDictionary(g => g.Key, g => g.Count());

        /// <summary>The kind that dominates it — what the viewpoint is really about.</summary>
        public ChangeBucket Bucket =>
            Changes.GroupBy(c => c.Bucket)
                   .OrderByDescending(g => g.Count())
                   .ThenBy(g => (int)g.Key)
                   .Select(g => g.Key)
                   .FirstOrDefault();

        /// <summary>True when nothing in it is in the current document.</summary>
        public bool IsGone => Keys.Count == 0;

        /// <summary>How it reads on a viewpoint name and in a list.</summary>
        public string Title
        {
            get
            {
                var parts = new List<string>();

                foreach (var bucket in new[]
                         {
                             ChangeBucket.Added, ChangeBucket.Removed,
                             ChangeBucket.Moved, ChangeBucket.Changed,
                         })
                {
                    if (ByBucket.TryGetValue(bucket, out var n) && n > 0)
                        parts.Add(n.ToString(CultureInfo.InvariantCulture) + " "
                                  + ElementChange.Show(bucket).ToLowerInvariant());
                }

                return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
            }
        }

        /// <inheritdoc />
        public override string ToString() => Title;
    }

    /// <summary>
    /// Groups a comparison's changes into the places they happened.
    ///
    /// Four hundred rows in a table do not say where to stand. Twelve viewpoints — one per area
    /// where something moved — is a walk that takes ten minutes and can be handed to whoever has to
    /// fix it.
    ///
    /// Cells first, then merge what touches. A plain grid splits one changed riser across two cells
    /// and reports it as two unrelated areas; merging adjacent occupied cells lets a cluster take
    /// the shape of the thing that changed rather than the shape of the grid.
    /// </summary>
    public static class CompareClusters
    {
        /// <summary>The default cell, in the document's own length unit. Ten metres in a metric model.</summary>
        public const double DefaultCell = 10;

        /// <summary>How many clusters are returned before the tail is summarised instead.</summary>
        public const int DefaultCap = 50;

        /// <summary>
        /// Work out where the changes are.
        /// </summary>
        /// <param name="result">What the comparison found.</param>
        /// <param name="cell">Grid cell size, in the document's length unit.</param>
        /// <param name="cap">How many clusters to return, largest first.</param>
        /// <param name="buckets">Which kinds of change take part. Defaults to everything but unchanged.</param>
        public static IReadOnlyList<ChangeCluster> Of(CompareResult? result,
                                                      double cell = DefaultCell,
                                                      int cap = DefaultCap,
                                                      IReadOnlyList<ChangeBucket>? buckets = null)
        {
            if (result == null) return Array.Empty<ChangeCluster>();

            if (cell <= 0 || double.IsNaN(cell) || double.IsInfinity(cell))
                throw new ArgumentOutOfRangeException(nameof(cell), "a cell has to have a size");

            var wanted = buckets ?? new[]
            {
                ChangeBucket.Added, ChangeBucket.Removed, ChangeBucket.Moved, ChangeBucket.Changed,
            };

            var changes = result.Changes.Where(c => wanted.Contains(c.Bucket)).ToList();
            if (changes.Count == 0) return Array.Empty<ChangeCluster>();

            var cells = new Dictionary<(long X, long Y, long Z), List<ElementChange>>();

            foreach (var change in changes)
            {
                var print = change.Print;

                if (!Finite(print.CentreX) || !Finite(print.CentreY) || !Finite(print.CentreZ)) continue;

                var key = (Floor(print.CentreX, cell), Floor(print.CentreY, cell), Floor(print.CentreZ, cell));

                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<ElementChange>();
                list.Add(change);
            }

            if (cells.Count == 0) return Array.Empty<ChangeCluster>();

            var merged = Merge(cells);

            return merged
                .Select(Build)
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Bounds.MinX)
                .ThenBy(c => c.Bounds.MinY)
                .ThenBy(c => c.Bounds.MinZ)
                .Take(cap <= 0 ? int.MaxValue : cap)
                .ToList();
        }

        /// <summary>
        /// Join cells that touch, including diagonally.
        ///
        /// Union-find over the twenty-six neighbours. Diagonals included on purpose: a riser that
        /// steps sideways as it rises occupies cells that meet only at an edge, and treating those
        /// as two separate places produces two viewpoints of the same problem.
        /// </summary>
        private static List<List<ElementChange>> Merge(
            Dictionary<(long X, long Y, long Z), List<ElementChange>> cells)
        {
            var keys = cells.Keys.ToList();
            var index = new Dictionary<(long, long, long), int>();

            for (var i = 0; i < keys.Count; i++) index[keys[i]] = i;

            var parent = Enumerable.Range(0, keys.Count).ToArray();

            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }

                return i;
            }

            void Union(int a, int b)
            {
                var ra = Find(a);
                var rb = Find(b);
                if (ra != rb) parent[rb] = ra;
            }

            for (var i = 0; i < keys.Count; i++)
            {
                var (x, y, z) = keys[i];

                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;

                            if (index.TryGetValue((x + dx, y + dy, z + dz), out var j)) Union(i, j);
                        }
            }

            var groups = new Dictionary<int, List<ElementChange>>();

            for (var i = 0; i < keys.Count; i++)
            {
                var root = Find(i);

                if (!groups.TryGetValue(root, out var list)) groups[root] = list = new List<ElementChange>();
                list.AddRange(cells[keys[i]]);
            }

            return groups.Values.ToList();
        }

        private static ChangeCluster Build(List<ElementChange> changes)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            var keys = new List<ElementKey>();

            foreach (var change in changes)
            {
                var print = change.Print;

                minX = Math.Min(minX, print.CentreX - (Math.Abs(print.SizeX) / 2));
                minY = Math.Min(minY, print.CentreY - (Math.Abs(print.SizeY) / 2));
                minZ = Math.Min(minZ, print.CentreZ - (Math.Abs(print.SizeZ) / 2));
                maxX = Math.Max(maxX, print.CentreX + (Math.Abs(print.SizeX) / 2));
                maxY = Math.Max(maxY, print.CentreY + (Math.Abs(print.SizeY) / 2));
                maxZ = Math.Max(maxZ, print.CentreZ + (Math.Abs(print.SizeZ) / 2));

                // Only the current side has an element to point at. A removed one is named by the
                // baseline and is not in this document, so it contributes its extent and no key.
                if (change.After != null && change.After.TryKey(out var key)) keys.Add(key);
            }

            return new ChangeCluster(changes, new BoundingBox(minX, minY, minZ, maxX, maxY, maxZ), keys);
        }

        private static long Floor(double value, double cell) =>
            (long)Math.Floor(value / cell);

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1e12;

        /// <summary>
        /// A viewpoint name for a cluster: where it is in the walk, and what is in it.
        /// </summary>
        /// <param name="cluster">The cluster.</param>
        /// <param name="number">Its place in the ordering, from one.</param>
        /// <param name="total">How many there are.</param>
        public static string Name(ChangeCluster cluster, int number, int total)
        {
            if (cluster == null) throw new ArgumentNullException(nameof(cluster));

            var width = total >= 100 ? "000" : "00";

            return number.ToString(width, CultureInfo.InvariantCulture) + " — " + cluster.Title;
        }

        /// <summary>The folder a comparison's viewpoints go in.</summary>
        /// <param name="result">What the comparison found.</param>
        public static string Folder(CompareResult? result)
        {
            var label = result?.BaselineLabel;

            return string.IsNullOrWhiteSpace(label)
                ? "Compare"
                : "Compare — since " + label!.Trim();
        }
    }
}
