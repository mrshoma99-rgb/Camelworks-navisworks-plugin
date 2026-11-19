using System;
using System.Collections.Generic;
using CamelWorks.Core.Appearance;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Abstractions
{
    /// <summary>
    /// Everything that changes what the user sees: colour, transparency, visibility, camera and
    /// the section box.
    ///
    /// This one is behind the seam for a reason that only showed up when the panel looked at it.
    /// The host's two override layers are NOT symmetric: the permanent layer can be reset for a
    /// given collection of elements, while the temporary layer can only be reset globally — and
    /// the temporary layer cannot be read back at all. That asymmetry is what forces the Appearance
    /// Manager's stack onto the permanent layer, and it is a host fact that logic above this seam
    /// must never have to know or re-derive.
    /// </summary>
    public interface IViewSession
    {
        /// <summary>Which layer a write targets. See the type remarks for why this is not free choice.</summary>
        OverrideLayer Layer { get; set; }

        /// <summary>Override colour on the given elements.</summary>
        void SetColour(IReadOnlyList<ElementKey> keys, Colour colour);

        /// <summary>Override transparency, 0 (opaque) to 1 (invisible).</summary>
        void SetTransparency(IReadOnlyList<ElementKey> keys, double transparency);

        /// <summary>Show or hide the given elements.</summary>
        void SetVisible(IReadOnlyList<ElementKey> keys, bool visible);

        /// <summary>
        /// Hide everything except the given elements — the "look at just these two pipes" move
        /// every clash reviewer makes. Undone by <see cref="ShowAll"/>, and by nothing subtler:
        /// the host records hiding per element, so a selective undo would have to remember what
        /// was already hidden before, which is a lie waiting to be told.
        /// </summary>
        /// <param name="keys">What stays visible.</param>
        void Isolate(IReadOnlyList<ElementKey> keys);

        /// <summary>
        /// Hide everything but what the host has selected right now.
        ///
        /// Separate from the call above because a caller that already has the elements — the clash
        /// board asks the engine for the two a result is between, which costs nothing — previously
        /// had to turn them into keys and hand those over, and resolving a key that is not an
        /// instance GUID means indexing every geometry leaf in the federation. So isolating what was
        /// already selected started a walk of the whole model to find what it was already holding.
        /// </summary>
        /// <returns>How many items were left visible. Zero means nothing was selected to keep.</returns>
        int IsolateSelection();

        /// <summary>
        /// Frame what the host has selected right now.
        ///
        /// The counterpart of <see cref="IsolateSelection"/>, and for the same reason: the host
        /// focuses on its own selection, so a caller that has already made one has nothing left
        /// to look up.
        /// </summary>
        /// <param name="marginMetres">How much room to leave around it.</param>
        void ZoomToSelection(double marginMetres);

        /// <summary>Unhide everything, including what somebody else hid.</summary>
        void ShowAll();

        /// <summary>
        /// Clear CamelWorks overrides on the given elements. On the permanent layer this is
        /// per-element; on the temporary layer the host only offers a global reset, so an
        /// implementation must either clear everything or refuse — never pretend it was selective.
        /// </summary>
        void ClearOverrides(IReadOnlyList<ElementKey> keys);

        /// <summary>Clear every override on the current layer, including ones CamelWorks did not author.</summary>
        void ClearAllOverrides();

        /// <summary>
        /// Read the appearance the document is actually in, for the elements given.
        ///
        /// Only the permanent layer can be reported. The Appearance Manager's foreign-state row
        /// says so explicitly rather than implying completeness — claiming to show everything while
        /// silently omitting temporary overrides would be worse than the host's own blindness.
        /// </summary>
        IReadOnlyList<AppearanceState> ReadAppearance(IReadOnlyList<ElementKey> keys);

        /// <summary>
        /// Every element in a scope that is not in its untouched state, found in one walk.
        ///
        /// This exists because the obvious composition of the two calls above is quadratic. Listing
        /// a federation gives a key per element; handing those keys back to
        /// <see cref="ReadAppearance"/> makes the implementation resolve each one to the item the
        /// walk was already holding, and resolving a key means a search of the whole document. Two
        /// hundred thousand elements is two hundred thousand searches of two hundred thousand
        /// elements, which is not slow but hung.
        ///
        /// So the walk and the reading are one pass, and an implementation reads the host's own
        /// cheap signals before it builds a key — building a key costs a GUID read, and for an
        /// element without one a parent-chain walk or a bounding box. On a real model almost
        /// everything is untouched, so almost nothing needs a key at all.
        ///
        /// Three obligations on an implementation:
        ///
        ///   * Only what is not <see cref="AppearanceState.IsPristine"/> is yielded. An untouched
        ///     element is skipped, which is the whole point: the caller gets the sparse set, and
        ///     <paramref name="onProgress"/> is what tells it how much was looked at.
        ///   * Lazily, so the caller can stop. Materialising the walk and returning a list would
        ///     put the freeze back where it was.
        ///   * The item yielded alongside the reading is the one the walk held, so a caller that
        ///     also wants a name or a tree path does not resolve it again.
        ///
        /// Only the permanent layer can be reported, as for <see cref="ReadAppearance"/>.
        /// </summary>
        /// <param name="scope">What to walk.</param>
        /// <param name="onProgress">
        /// Called with the running count of elements walked — not elements yielded; false stops the
        /// walk. The count is a liveness figure and not a total.
        /// </param>
        IEnumerable<ScannedAppearance> ScanAppearance(TraversalScope scope, Func<int, bool>? onProgress);

        /// <summary>
        /// The levels in the project tree at which states were applied, rather than the elements
        /// that ended up in them.
        ///
        /// The host applies an override to a node and every element under it takes it, so the
        /// element-by-element reading above answers "what is this element in" and cannot answer
        /// "what did somebody do". One act on a storey is four thousand coloured elements; reported
        /// element by element it is four thousand rows, and adopting it makes a layer holding four
        /// thousand keys. A dozen such acts and the layer list is unreadable.
        ///
        /// The two kinds of state are not alike here, and the difference is the host's:
        ///
        ///   * Hiding is recorded on the item it was applied to and inherited by descendants, so
        ///     the level is readable. It also means the reading above is wrong about hiding: an
        ///     element inside a hidden parent reports itself visible, so a flat walk of the
        ///     geometry sees a hidden storey as nothing hidden at all.
        ///   * Colour and transparency are stored on geometry, so applying one to a parent pushes
        ///     it to the leaves and the level is recorded nowhere. It is inferred as the highest
        ///     node whose whole geometry subtree shares one state, and the origin says that it was
        ///     inferred.
        ///
        /// Not lazy, unlike <see cref="ScanAppearance"/>, and it cannot be: a roll-up is only true
        /// once the subtree under it has been walked, so there is nothing correct to hand over
        /// early. The walk still reports and can still be stopped, and a stopped walk says so
        /// rather than handing back a roll-up of the part of the model it got to.
        /// </summary>
        /// <param name="scope">What to walk.</param>
        /// <param name="onProgress">
        /// Called with the running count of elements walked; false stops the walk.
        /// </param>
        OriginReport ScanOrigins(TraversalScope scope, Func<int, bool>? onProgress);

        /// <summary>Fit the camera to the given elements, with a context margin in model units.</summary>
        void ZoomTo(IReadOnlyList<ElementKey> keys, double marginMetres);

        /// <summary>Fit the camera to a box, in metres.</summary>
        /// <param name="box">What has to be visible.</param>
        void FrameBox(BoundingBox box);

        /// <summary>
        /// Look at a point in model space, from a step back.
        ///
        /// The camera move that needs no elements at all. Framing a clash result means resolving
        /// two elements and asking the host to focus on them; a clash result already carries the
        /// point where the two meet, and moving the camera there is instant however large the
        /// federation is. The elements are still selected, for the coordinator who wants to see
        /// what was hit - but the view no longer waits on them.
        /// </summary>
        /// <param name="x">Model X.</param>
        /// <param name="y">Model Y.</param>
        /// <param name="z">Model Z.</param>
        /// <param name="backMetres">How far back to stand, in metres.</param>
        void LookAt(double x, double y, double z, double backMetres);

        /// <summary>
        /// Look straight down at a box, orthographic, so a render of it is a plan.
        ///
        /// Orthographic because a perspective plan is not a plan: markers near the edges land where
        /// the projection put them rather than where the thing is, and a reader scaling off it gets
        /// a different answer at every corner.
        ///
        /// Returns false when the host refuses, which is an ordinary outcome — a 2D sheet view has
        /// no camera to rotate — and the caller prints the report without the plan rather than
        /// failing the export.
        /// </summary>
        /// <param name="box">What has to be visible, in metres.</param>
        bool LookDown(BoundingBox box);

        /// <summary>
        /// Remember where the camera is, and put it back when the returned handle is disposed.
        ///
        /// A <c>using</c> rather than a save/restore pair, because anything that moves the camera in
        /// a loop - exporting a snapshot per clash group, say - has to put it back afterwards, and
        /// a pair of calls is a pair somebody eventually only writes half of. Leaving a coordinator
        /// looking at the last clash of a hundred is a small betrayal they will remember.
        /// </summary>
        IDisposable BorrowCamera();

        /// <summary>
        /// Render what the camera can see, as PNG bytes, or null when the host cannot.
        ///
        /// Null rather than an exception: an export that wanted snapshots and could not get them
        /// should still write the file, note that the images are missing, and let the receiving
        /// tool show the camera instead.
        /// </summary>
        /// <param name="width">Pixels.</param>
        /// <param name="height">Pixels.</param>
        byte[]? Capture(int width, int height);

        /// <summary>
        /// Whether this host can hang temporary graphics in the view.
        ///
        /// False on a host that has no live view - the Studio harness, a headless run - and the
        /// caller says so rather than reporting markers that were never drawn.
        /// </summary>
        bool CanMark { get; }

        /// <summary>
        /// Hang these markers in the view, replacing whatever was there.
        ///
        /// Temporary graphics: they are drawn on top of the model each frame and are gone the
        /// moment <see cref="ClearMarkers"/> is called or Navisworks closes. Nothing is added to
        /// the document, nothing appears in the selection tree, and nothing needs unloading.
        /// </summary>
        /// <param name="markers">Where the balls go, in document units. Empty clears.</param>
        void ShowMarkers(IReadOnlyList<Clash.SceneMarker> markers);

        /// <summary>Take every marker down, remembering them.</summary>
        /// <returns>How many went down.</returns>
        int HideMarkers();

        /// <summary>Put back what <see cref="HideMarkers"/> took down.</summary>
        /// <returns>How many came back.</returns>
        int UnhideMarkers();

        /// <summary>Whether there is a hidden set of markers to put back.</summary>
        bool CanUnhideMarkers { get; }

        /// <summary>Take every marker down and forget them.</summary>
        void ClearMarkers();

        /// <summary>Set a section box around the given elements, with a context margin.</summary>
        void SetSectionBox(BoundingBox box, double marginMetres);

        /// <summary>Turn sectioning off without discarding the box, so it can be toggled back.</summary>
        void SetSectionBoxEnabled(bool enabled);

        /// <summary>
        /// Whether sectioning is on right now.
        ///
        /// Part of the contract rather than a convenience: <see cref="SetSectionBoxEnabled"/>
        /// promises the box survives being switched off, and a caller cannot offer a toggle at all
        /// without being able to ask which way it currently is.
        /// </summary>
        bool IsSectionBoxEnabled { get; }
    }

    /// <summary>Which override layer a <see cref="IViewSession"/> write targets.</summary>
    public enum OverrideLayer
    {
        /// <summary>
        /// Survives into the saved document, resettable per element, and readable back. The
        /// Appearance Manager's stack lives here — not by preference, but because the alternative
        /// supports neither selective reset nor read-back.
        /// </summary>
        Permanent = 0,

        /// <summary>
        /// Scoped to the session and to saved viewpoints. Cannot be read back and can only be
        /// reset globally, so it is used for transient effects that nothing needs to inspect.
        /// </summary>
        Temporary = 1,
    }

    /// <summary>The appearance one element is actually in, as read from the document.</summary>
    public readonly struct AppearanceState
    {
        /// <summary>The element.</summary>
        public ElementKey Key { get; }

        /// <summary>Current colour, or null when the element carries no colour override.</summary>
        public Colour? Colour { get; }

        /// <summary>Current transparency, or null when the element carries no transparency override.</summary>
        public double? Transparency { get; }

        /// <summary>Whether the host currently hides the element.</summary>
        public bool IsHidden { get; }

        /// <summary>
        /// True when the override differs from the element's original appearance but no CamelWorks
        /// layer accounts for it — i.e. somebody else set it. This is the signal behind the
        /// Appearance Manager's foreign-state row, and the answer to "why is this thing pink".
        /// </summary>
        public bool IsForeign { get; }

        /// <summary>Create a reading.</summary>
        public AppearanceState(ElementKey key, Colour? colour, double? transparency, bool isHidden, bool isForeign)
        {
            Key = key; Colour = colour; Transparency = transparency; IsHidden = isHidden; IsForeign = isForeign;
        }

        /// <summary>True when the element is in its untouched state.</summary>
        public bool IsPristine => Colour == null && Transparency == null && !IsHidden;
    }

    /// <summary>
    /// One element a scan found, with the item it was found on.
    ///
    /// The item travels with the reading because the scan is the only thing that had it. A caller
    /// writing an appearance scheme out needs each element's tree path as well as its colour, and
    /// asking for that path by key afterwards would re-resolve every element the walk just held —
    /// reintroducing, for the elements that matter, exactly the cost
    /// <see cref="IViewSession.ScanAppearance"/> exists to remove.
    /// </summary>
    public readonly struct ScannedAppearance
    {
        /// <summary>Pair a reading with the item it was read from.</summary>
        /// <param name="item">The item the walk was holding.</param>
        /// <param name="state">What that element currently looks like.</param>
        public ScannedAppearance(IModelItem item, AppearanceState state)
        {
            Item = item;
            State = state;
        }

        /// <summary>The item the walk was holding.</summary>
        public IModelItem Item { get; }

        /// <summary>What that element currently looks like.</summary>
        public AppearanceState State { get; }

        /// <summary>The element's key, which is the reading's.</summary>
        public ElementKey Key => State.Key;
    }

    /// <summary>
    /// One level at which a state was applied, with the tree node it was applied to.
    ///
    /// The node travels with the origin because the scan is the only thing that had it, and because
    /// it is the point of the whole exercise: a layer made from this names the node, so it covers
    /// whatever is under the node rather than a list of the keys that happened to be under it when
    /// somebody pressed Scan.
    /// </summary>
    public sealed class ScannedOrigin
    {
        /// <summary>Pair an origin with its node.</summary>
        /// <param name="item">The tree node the state was applied to.</param>
        /// <param name="origin">What was applied, and over how much.</param>
        public ScannedOrigin(IModelItem item, AppearanceOrigin origin)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        }

        /// <summary>The tree node the state was applied to.</summary>
        public IModelItem Item { get; }

        /// <summary>What was applied, and over how much.</summary>
        public AppearanceOrigin Origin { get; }

        /// <summary>Where the node sits in the tree, for a row that has to read as a place.</summary>
        public string Path => string.Join(" / ", Item.TreePath);

        /// <inheritdoc />
        public override string ToString() => Origin.Describe() + " at " + Path;
    }

    /// <summary>
    /// What a walk of the tree found, and how much of the tree it got through.
    ///
    /// The size of the walk is reported separately from what was found because the two are no
    /// longer the same number. "41 states" means nothing on its own; "41 states over 230,000
    /// elements" is the sentence a coordinator needs, and a scan that was stopped must be able to
    /// say so rather than presenting a third of a federation as the answer.
    /// </summary>
    public sealed class OriginReport
    {
        /// <summary>Report a finished or stopped walk.</summary>
        /// <param name="origins">What was found, deepest first.</param>
        /// <param name="elements">Geometry elements walked.</param>
        /// <param name="nodes">Tree nodes walked, geometry or not.</param>
        /// <param name="stopped">True when the walk was stopped before it finished.</param>
        public OriginReport(IReadOnlyList<ScannedOrigin> origins, int elements, int nodes, bool stopped)
        {
            Origins = origins ?? Array.Empty<ScannedOrigin>();
            Elements = elements;
            Nodes = nodes;
            Stopped = stopped;
        }

        /// <summary>
        /// What was found, deepest first.
        ///
        /// Deepest first because that is the order the layers go in: a layer on a deeper node has
        /// to decide its elements before one on a shallower node gets to, or the deeper layer
        /// covers elements and decides nothing about any of them.
        /// </summary>
        public IReadOnlyList<ScannedOrigin> Origins { get; }

        /// <summary>How many geometry elements the walk covered.</summary>
        public int Elements { get; }

        /// <summary>How many tree nodes the walk covered, grouping nodes included.</summary>
        public int Nodes { get; }

        /// <summary>True when the walk was stopped, so the origins describe part of a model.</summary>
        public bool Stopped { get; }

        /// <summary>True when nothing in what was walked is in a state somebody set.</summary>
        public bool IsClean => Origins.Count == 0;

        /// <summary>An empty report, for a seat with no document open.</summary>
        public static OriginReport Empty { get; } =
            new OriginReport(Array.Empty<ScannedOrigin>(), 0, 0, false);
    }

    /// <summary>An 8-bit-per-channel colour.</summary>
    public readonly struct Colour : System.IEquatable<Colour>
    {
        /// <summary>Red, 0-255.</summary>
        public byte R { get; }

        /// <summary>Green, 0-255.</summary>
        public byte G { get; }

        /// <summary>Blue, 0-255.</summary>
        public byte B { get; }

        /// <summary>Create a colour.</summary>
        public Colour(byte r, byte g, byte b) { R = r; G = g; B = b; }

        /// <summary>Parse <c>#rrggbb</c> or <c>rrggbb</c>. Returns false rather than throwing.</summary>
        public static bool TryParse(string? hex, out Colour colour)
        {
            colour = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var s = hex!.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6) return false;

            if (!byte.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var r)) return false;
            if (!byte.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var g)) return false;
            if (!byte.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var b)) return false;

            colour = new Colour(r, g, b);
            return true;
        }

        /// <summary>Packed as <c>0xRRGGBB</c> — a cheap dictionary key when batching writes by colour.</summary>
        public int Packed => (R << 16) | (G << 8) | B;

        /// <inheritdoc />
        public bool Equals(Colour other) => Packed == other.Packed;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Colour c && Equals(c);

        /// <inheritdoc />
        public override int GetHashCode() => Packed;

        /// <summary>Value equality.</summary>
        public static bool operator ==(Colour a, Colour b) => a.Equals(b);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(Colour a, Colour b) => !a.Equals(b);

        /// <summary><c>#rrggbb</c>.</summary>
        public override string ToString() =>
            "#" + R.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
                + G.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)
                + B.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
