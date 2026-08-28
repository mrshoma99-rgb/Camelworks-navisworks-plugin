using System;
using System.Collections.Generic;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Project;

namespace CamelWorks.Core.Compare
{
    /// <summary>How far a snapshot has got, so a walk of a federation is not a frozen window.</summary>
    public sealed class SnapshotProgress
    {
        internal SnapshotProgress(int seen, int kept, bool stopped)
        {
            Seen = seen;
            Kept = kept;
            Stopped = stopped;
        }

        /// <summary>How many elements the walk has looked at.</summary>
        public int Seen { get; }

        /// <summary>How many it recorded.</summary>
        public int Kept { get; }

        /// <summary>True when somebody stopped it.</summary>
        public bool Stopped { get; }

        /// <inheritdoc />
        public override string ToString() =>
            ComparePolicy.Count(Kept) + " of " + ComparePolicy.Count(Seen) + " recorded"
            + (Stopped ? " (stopped)" : string.Empty);
    }

    /// <summary>
    /// Takes a snapshot of a live document.
    ///
    /// <b>In Core, not in the host adapter.</b> Everything a snapshot needs — identity, name,
    /// category, type, bounding box, properties — is on the seam, so the walk that produces a
    /// baseline is testable on a Linux CI job against a fake document. Only the optional mesh
    /// signature needs the host, and that is the one thing that lives on the far side.
    ///
    /// <b>Stoppable, and reports how far it got.</b> A federation is a few hundred thousand
    /// elements and this reads every property of every one of them. A modal progress dialog that
    /// cannot be cancelled is how a plug-in earns the reputation of hanging Navisworks.
    /// </summary>
    public static class SnapshotBuilder
    {
        /// <summary>How often <see cref="Take"/> reports progress and asks whether to keep going, in elements.</summary>
        public const int ProgressEvery = 2000;

        /// <summary>
        /// Walk the document and record what is there.
        /// </summary>
        /// <param name="document">The open document.</param>
        /// <param name="takenUtc">When this is being taken. Passed in so a snapshot is reproducible.</param>
        /// <param name="label">How it should read on the picker — a milestone, an issue number.</param>
        /// <param name="options">What to watch. Defaults to <see cref="CompareSettings.Default"/>.</param>
        /// <param name="scope">What to walk. Defaults to the whole document.</param>
        /// <param name="documentPath">The document's path, for the record.</param>
        /// <param name="units">The document's length unit, as the host names it.</param>
        /// <param name="keepGoing">Called every few thousand elements; return false to stop.</param>
        /// <param name="onProgress">Called every few thousand elements.</param>
        public static ModelSnapshot Take(IModelDocument document,
                                         DateTimeOffset takenUtc,
                                         string? label = null,
                                         CompareSettings? options = null,
                                         TraversalScope? scope = null,
                                         string? documentPath = null,
                                         string? units = null,
                                         Func<bool>? keepGoing = null,
                                         Action<SnapshotProgress>? onProgress = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var settings = options ?? CompareSettings.Default;

            var snapshot = new ModelSnapshot
            {
                Label = (label ?? string.Empty).Trim(),
                TakenUtc = takenUtc,
                DocumentPath = documentPath ?? string.Empty,
                Units = units ?? string.Empty,
                Watch = settings.Watch,
                Watched = settings.Watched,
                Ignored = settings.Ignored,
            };

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var unreadable = 0;
            var seen = 0;
            var stopped = false;

            // Read into a list before anything is recorded. The walk is the part that cannot be
            // interrupted safely - reporting progress pumps the host's message loop, and a pump
            // part way through a walk of the host's own tree is how a plug-in gets blamed for
            // Navisworks vanishing. The seam's walk closes each host enumerator as it goes, and
            // this list means the expensive half below - reading properties off each element -
            // reports progress with nothing of the host's open at all.
            var items = document.Traverse(scope ?? TraversalScope.WholeDocument).ToList();

            foreach (var item in items)
            {
                if (item == null) continue;

                seen++;

                if (seen % ProgressEvery == 0)
                {
                    onProgress?.Invoke(new SnapshotProgress(seen, snapshot.Count, false));

                    if (keepGoing != null && !keepGoing())
                    {
                        stopped = true;
                        break;
                    }
                }

                // Grouping nodes are recorded rather than filtered here, and filtered at comparison
                // time by CompareSettings.IncludeGroupingNodes. A baseline that threw them away
                // could never be compared with them included, and the person who wants them is
                // always the person looking at a six-month-old baseline.
                // One element the host will not answer for does not cost the other two hundred
                // thousand. A handle can go stale between the walk and the read - somebody
                // refreshed a model - and the answer to that is a smaller snapshot, not none.
                try
                {
                    snapshot.Add(ElementFingerprint.Of(item, settings));
                }
                catch (Exception)
                {
                    unreadable++;
                    continue;
                }

                var scopeHash = item.Model?.Scope;

                if (!string.IsNullOrEmpty(scopeHash) && item.HasGeometry)
                    counts[scopeHash!] = counts.TryGetValue(scopeHash!, out var n) ? n + 1 : 1;
            }

            snapshot.Models = ModelManifest.Of(document.Models, itemCounts: counts, takenUtc: takenUtc).Models;
            snapshot.Unreadable = unreadable;

            onProgress?.Invoke(new SnapshotProgress(seen, snapshot.Count, stopped));

            return snapshot;
        }

        /// <summary>
        /// A rough size for a snapshot before anybody waits for one, so the screen can say what
        /// the expensive setting is about to cost.
        /// </summary>
        /// <param name="elements">How many elements the document has.</param>
        /// <param name="watch">Which properties would be recorded.</param>
        public static string Estimate(int elements, PropertyWatch watch)
        {
            if (elements <= 0) return "nothing to record";

            // Measured against real federations rather than derived: an element line is around 140
            // bytes before properties, a standard-list property about 30, and an all-properties
            // element carries roughly sixty of them.
            var perElement = 140 + watch switch
            {
                PropertyWatch.None => 0,
                PropertyWatch.All => 60 * 30,
                PropertyWatch.Named => 4 * 30,
                _ => 8 * 30,
            };

            var bytes = (long)elements * perElement;

            return ComparePolicy.Count(elements) + " elements, roughly " + Readable(bytes);
        }

        private static string Readable(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " B";

            double size = bytes;
            var suffixes = new[] { "KB", "MB", "GB" };
            var at = -1;

            do
            {
                size /= 1024;
                at++;
            }
            while (size >= 1024 && at < suffixes.Length - 1);

            return size.ToString(size >= 100 ? "N0" : "N1", System.Globalization.CultureInfo.InvariantCulture)
                   + " " + suffixes[at];
        }
    }
}
