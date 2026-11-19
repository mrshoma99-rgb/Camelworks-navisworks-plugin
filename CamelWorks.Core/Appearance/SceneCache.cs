using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Appearance
{
    /// <summary>
    /// A scan, kept so it need not be run again, with the time it was taken.
    ///
    /// The time is part of it rather than a nicety. Two of the three things that can make a reading
    /// wrong are detectable and this cache handles them; the third is not, so the screen shows when
    /// the reading was taken and offers to take it again. A cache that claimed freshness it cannot
    /// verify would be worse than no cache, because the user would stop checking.
    /// </summary>
    public sealed class SceneReading
    {
        internal SceneReading(string key, OriginReport report, int generation, long whenTicks)
        {
            Key = key;
            Report = report;
            Generation = generation;
            WhenTicks = whenTicks;
        }

        /// <summary>Which scene this is: the viewpoint's id, or empty for the live one.</summary>
        public string Key { get; }

        /// <summary>What the scan found.</summary>
        public OriginReport Report { get; }

        /// <summary>The document generation it was taken in.</summary>
        public int Generation { get; }

        /// <summary>When it was taken, in ticks, as the session counts them.</summary>
        public long WhenTicks { get; }

        /// <summary>Whether this is the live scene rather than a saved viewpoint.</summary>
        public bool IsLiveScene => Key.Length == 0;

        /// <summary>How old this reading is, against a time the caller supplies.</summary>
        /// <param name="nowTicks">Now, as the session counts it.</param>
        public TimeSpan Age(long nowTicks) =>
            nowTicks <= WhenTicks ? TimeSpan.Zero : TimeSpan.FromTicks(nowTicks - WhenTicks);

        /// <summary>How old this reading is, in words, for the line above a cached table.</summary>
        /// <param name="nowTicks">Now, as the session counts it.</param>
        public string Describe(long nowTicks)
        {
            var age = Age(nowTicks);

            var when = age.TotalSeconds < 45 ? "a moment ago"
                     : age.TotalMinutes < 90
                        ? ((int)Math.Round(age.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + " min ago"
                        : ((int)Math.Round(age.TotalHours)).ToString(CultureInfo.InvariantCulture) + " h ago";

            return "Read " + when + ", over "
                 + Report.Elements.ToString("N0", CultureInfo.InvariantCulture) + " elements.";
        }
    }

    /// <summary>
    /// Readings of the live scene and of saved viewpoints, so clicking back to one does not scan it
    /// again.
    ///
    /// Three things can make a reading wrong, and they are not equally knowable:
    ///
    ///   * The model changed — appended, removed or reloaded. Detectable, and the hard case: every
    ///     reading is void, because the element keys and the tree itself have moved. The document
    ///     generation says so, and a <see cref="Put"/> or <see cref="Get"/> at a newer generation
    ///     empties the cache rather than letting one stale row through.
    ///   * The scene changed under the user — somebody else coloured something, or CamelWorks
    ///     applied a stack. The caller knows when it did that and calls <see cref="Forget"/>; for
    ///     the live scene that is any write at all, which is why the live reading is the one most
    ///     often dropped.
    ///   * A viewpoint was re-saved with different state. NOT detectable: a SavedViewpoint carries
    ///     no version and no timestamp, and the flags that say whether it holds appearance state do
    ///     not change when the state inside it does. So nothing here pretends to catch it — the
    ///     reading carries the time it was taken, the screen shows it, and taking it again is one
    ///     press.
    ///
    /// A stopped scan is never kept. It describes the part of a model somebody lost patience with,
    /// and handing that back later as the answer is the one failure a cache must not have.
    ///
    /// A viewpoint's reading is filed under its id and the live scene's under
    /// <see cref="LiveScene"/>, and neither stands in for the other. A viewpoint is read from its
    /// own records rather than by being applied, so reading one changes nothing about the live
    /// scene and leaves its reading alone.
    /// </summary>
    public sealed class SceneCache
    {
        /// <summary>The key the live scene is kept under.</summary>
        public const string LiveScene = "";

        private readonly Dictionary<string, SceneReading> _readings =
            new Dictionary<string, SceneReading>(StringComparer.Ordinal);

        /// <summary>The generation every reading in here was taken in, or -1 when it is empty.</summary>
        public int Generation { get; private set; } = -1;

        /// <summary>How many readings are held.</summary>
        public int Count => _readings.Count;

        /// <summary>
        /// The reading for a scene, or null when there is none worth having.
        ///
        /// Null rather than a stale answer whenever the generation has moved, and the cache empties
        /// itself on the way out so the next call does not re-examine readings that are already
        /// void.
        /// </summary>
        /// <param name="key">A viewpoint id, or <see cref="LiveScene"/>.</param>
        /// <param name="generation">The document generation now.</param>
        public SceneReading? Get(string key, int generation)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (generation != Generation)
            {
                // The model moved. Every reading in here describes a tree that no longer exists,
                // and the items inside them are handles into it.
                Clear();
                return null;
            }

            return _readings.TryGetValue(key, out var reading) ? reading : null;
        }

        /// <summary>
        /// Keep a reading, dropping everything from an older generation.
        /// </summary>
        /// <param name="key">A viewpoint id, or <see cref="LiveScene"/>.</param>
        /// <param name="report">What the scan found.</param>
        /// <param name="generation">The document generation it was taken in.</param>
        /// <param name="whenTicks">When it was taken.</param>
        /// <returns>The reading as kept, or null when it was not worth keeping.</returns>
        public SceneReading? Put(string key, OriginReport report, int generation, long whenTicks)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (report == null) throw new ArgumentNullException(nameof(report));

            // A scan somebody stopped is a reading of part of a model. Keeping it would hand it
            // back later as the whole answer, with nothing on screen to say it was not.
            if (report.Stopped) return null;

            if (generation != Generation)
            {
                Clear();
                Generation = generation;
            }

            var reading = new SceneReading(key, report, generation, whenTicks);
            _readings[key] = reading;

            return reading;
        }

        /// <summary>
        /// Drop one scene's reading, because something was written to it.
        ///
        /// Called by whatever did the writing. Applying a stack, clearing an override and putting a
        /// place back all change the live scene, so all three drop <see cref="LiveScene"/>; none of
        /// them changes what a saved viewpoint holds, so none of them drops a viewpoint's.
        /// </summary>
        /// <param name="key">A viewpoint id, or <see cref="LiveScene"/>.</param>
        /// <returns>True when there was one to drop.</returns>
        public bool Forget(string key) => key != null && _readings.Remove(key);

        /// <summary>
        /// A reading of a scene, scanning it only if there is not one already.
        ///
        /// Cache-aside, and the one place the decision is made, so the live scene and a viewpoint
        /// cannot drift apart on it.
        /// </summary>
        /// <param name="view">The session to scan through.</param>
        /// <param name="key">A viewpoint id, or <see cref="LiveScene"/>.</param>
        /// <param name="generation">The document generation now.</param>
        /// <param name="nowTicks">Now, as the session counts it.</param>
        /// <param name="again">True to scan even when there is a kept reading.</param>
        /// <param name="onProgress">Running count of elements walked; false stops the walk.</param>
        /// <returns>The reading, or null when the walk was stopped.</returns>
        public SceneReading? Read(IViewSession view, string key, int generation, long nowTicks,
                                  bool again, Func<int, bool>? onProgress)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (!again)
            {
                var kept = Get(key, generation);
                if (kept != null) return kept;
            }

            var report = view.ScanOrigins(TraversalScope.WholeDocument, onProgress);

            return Put(key, report, generation, nowTicks);
        }

        /// <summary>Drop everything.</summary>
        public void Clear()
        {
            _readings.Clear();
            Generation = -1;
        }
    }
}
