using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CamelWorks.Core.Abstractions;

namespace CamelWorks.Core.Clash
{
    /// <summary>
    /// One ball hanging in the view, at the point where two things met.
    ///
    /// In the document's own units, both the position and the radius, because that is what the
    /// host's own graphics calls take. Converting is the caller's job and it is the caller who
    /// knows which way round its numbers are: a board read from the model already counts in
    /// document units, a board read from a file counts in metres.
    /// </summary>
    public sealed class SceneMarker
    {
        /// <summary>Place one.</summary>
        /// <param name="x">Easting, in document units.</param>
        /// <param name="y">Northing, in document units.</param>
        /// <param name="z">Height, in document units.</param>
        /// <param name="radius">How big the ball is, in document units.</param>
        /// <param name="colour">What colour it is drawn.</param>
        public SceneMarker(double x, double y, double z, double radius, Colour colour)
        {
            X = x; Y = y; Z = z; Radius = radius; Colour = colour;
        }

        /// <summary>Easting, in document units.</summary>
        public double X { get; }

        /// <summary>Northing, in document units.</summary>
        public double Y { get; }

        /// <summary>Height, in document units.</summary>
        public double Z { get; }

        /// <summary>How big the ball is, in document units.</summary>
        public double Radius { get; }

        /// <summary>What colour it is drawn.</summary>
        public Colour Colour { get; }
    }

    /// <summary>
    /// What is currently hanging in the view, and nothing else.
    ///
    /// Temporary graphics rather than geometry. A marker is wanted for the few seconds one clash is
    /// being looked at: it should appear the instant Focus is pressed and clear on request. Writing
    /// a mesh and appending it as a file makes it a model — it joins the selection tree, has to be
    /// unloaded by hand and costs a re-index. The host draws whatever a render plug-in hands it on
    /// each frame, so that is what a marker is here.
    ///
    /// Read on the render thread, written on the UI thread, which is why the set is swapped whole
    /// rather than mutated: a reader sees either the old array or the new one, never a list part way
    /// through being filled.
    ///
    /// The store is static and host-free. It lives in Core so that the render plug-in, which must
    /// sit in the assembly Navisworks scans, and the screens, which must not know that assembly
    /// exists, can both reach the same set of markers.
    /// </summary>
    public static class MarkerOverlay
    {
        private static SceneMarker[] _shown = Array.Empty<SceneMarker>();
        private static int _version;

        /// <summary>The markers to draw this frame. Never null.</summary>
        public static IReadOnlyList<SceneMarker> Shown => Volatile.Read(ref _shown);

        /// <summary>How many are up.</summary>
        public static int Count => Volatile.Read(ref _shown).Length;

        /// <summary>Whether anything is up.</summary>
        public static bool Any => Count > 0;

        /// <summary>
        /// Bumped on every change, so a caller can tell whether a redraw is owed without
        /// comparing the sets.
        /// </summary>
        public static int Version => Volatile.Read(ref _version);

        /// <summary>
        /// How see-through a marker is drawn, 0 solid to 1 invisible.
        ///
        /// Half by default: solid enough to spot from across the model, clear enough that the two
        /// elements it sits between are still readable through it - which is the whole reason
        /// somebody dropped it there.
        /// </summary>
        public static double Transparency { get; set; } = 0.5;

        /// <summary>Put these up, replacing whatever was up before.</summary>
        /// <param name="markers">The markers. Null or empty clears.</param>
        /// <returns>How many went up.</returns>
        public static int Show(IEnumerable<SceneMarker>? markers)
        {
            var set = markers == null
                ? Array.Empty<SceneMarker>()
                : markers.Where(m => m != null).ToArray();

            Volatile.Write(ref _shown, set);
            Interlocked.Increment(ref _version);

            return set.Length;
        }

        /// <summary>Take them down, remembering them so they can come back.</summary>
        /// <returns>How many went down.</returns>
        public static int Hide()
        {
            var down = Volatile.Read(ref _shown);

            if (down.Length > 0) Volatile.Write(ref _hidden, down);

            Show(null);

            return down.Length;
        }

        /// <summary>
        /// Put back what <see cref="Hide"/> took down.
        ///
        /// The reason hiding remembers at all: a marker somebody has just dropped on a row they
        /// then scrolled away from cannot be recovered by pressing anything - the rows it came
        /// from are no longer picked. Hiding that could not be undone was a one-way door out of
        /// the feature.
        /// </summary>
        /// <returns>How many came back.</returns>
        public static int Unhide()
        {
            var back = Volatile.Read(ref _hidden);

            return back.Length == 0 ? 0 : Show(back);
        }

        /// <summary>Whether there is a hidden set to put back.</summary>
        public static bool CanUnhide => Volatile.Read(ref _hidden).Length > 0;

        /// <summary>Take them all down and forget them.</summary>
        public static void Clear()
        {
            Volatile.Write(ref _hidden, Array.Empty<SceneMarker>());
            Show(null);
        }

        private static SceneMarker[] _hidden = Array.Empty<SceneMarker>();
    }
}
