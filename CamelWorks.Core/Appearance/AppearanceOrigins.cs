using System;
using System.Collections.Generic;
using System.Globalization;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Appearance
{
    /// <summary>
    /// Where a state was applied, rather than where it ended up.
    ///
    /// Navisworks applies an override to a node in the project tree and every element under it
    /// takes it. One act on "Level 03" is four thousand coloured elements, and a scan that reports
    /// what it finds element by element reports that act four thousand times — so adopting it makes
    /// a layer holding four thousand keys, and a model with a dozen such acts makes a layer list
    /// nobody can read.
    ///
    /// The two kinds of state are not symmetric about this, and the difference is the host's, not
    /// a design choice:
    ///
    ///   * Hiding is recorded on the item it was applied to, and descendants inherit it. The level
    ///     is therefore readable: a node that hides with no hidden ancestor is exactly where
    ///     somebody pressed Hide. <see cref="IsRead"/> is true for those.
    ///   * Colour and transparency are stored on geometry, so applying one to a parent pushes it
    ///     down to the leaves and the level it was applied at is recorded nowhere. It can only be
    ///     inferred: the highest node whose whole geometry subtree shares one state. That is
    ///     indistinguishable from a parent-level override and is almost always one, but it is an
    ///     inference, and <see cref="IsRead"/> is false so a screen can say so rather than
    ///     claiming to know something the host threw away.
    /// </summary>
    public sealed class AppearanceOrigin
    {
        /// <summary>
        /// State the model is in, at the node it was applied to.
        ///
        /// Public because the host adapter builds these as well as <see cref="OriginScan"/> does.
        /// A saved viewpoint records each override against the item it was applied to, so reading
        /// one produces origins directly, with nothing to infer and nothing to fold — which is why
        /// those come back with <paramref name="isRead"/> true.
        /// </summary>
        /// <param name="node">The walk's number for the node, or the reader's.</param>
        /// <param name="depth">How deep the node is, a model root being zero.</param>
        /// <param name="covers">How many geometry elements sit under it, itself included.</param>
        /// <param name="isHidden">Whether this origin is a hiding.</param>
        /// <param name="colour">The colour, or null.</param>
        /// <param name="transparency">The transparency, or null.</param>
        /// <param name="isRead">
        /// True when the host recorded the level, false when it was inferred from the subtree.
        /// </param>
        public AppearanceOrigin(int node, int depth, int covers, bool isHidden,
                                Colour? colour, double? transparency, bool isRead)
        {
            Node = node;
            Depth = depth;
            Covers = covers;
            IsHidden = isHidden;
            Colour = colour;
            Transparency = transparency;
            IsRead = isRead;
        }

        /// <summary>
        /// The node, as the walk numbered it.
        ///
        /// An int rather than an element: this runs above the host seam, and the point of it is
        /// that the walk builds a key for the handful of nodes that turn out to be origins instead
        /// of for every element it passes. The walk maps the number back.
        /// </summary>
        public int Node { get; }

        /// <summary>
        /// How deep the node is, a model root being zero.
        ///
        /// This is what orders the layers. An override on "Level 03 / Mechanical" has to sit above
        /// one on "Level 03", or the shallower layer decides the elements the deeper one was made
        /// for and the deeper one reads as dead. Deeper is more specific is higher.
        /// </summary>
        public int Depth { get; }

        /// <summary>How many geometry elements sit under the node, itself included.</summary>
        public int Covers { get; }

        /// <summary>Whether this origin is a hiding.</summary>
        public bool IsHidden { get; }

        /// <summary>The colour, or null when this origin does not carry one.</summary>
        public Colour? Colour { get; }

        /// <summary>The transparency, or null when this origin does not carry one.</summary>
        public double? Transparency { get; }

        /// <summary>
        /// True when the host recorded the level, false when it was inferred from the subtree.
        ///
        /// Worth showing. "Hidden at Level 03" is a fact; "every element under Level 03 is red" is
        /// a reading of the model that is almost certainly a parent-level override and might be a
        /// coincidence of four thousand individual ones.
        /// </summary>
        public bool IsRead { get; }

        /// <summary>What this origin does, in words.</summary>
        public string Describe()
        {
            if (IsHidden) return "hidden";

            var parts = new List<string>(2);

            if (Colour != null) parts.Add(Colour.Value.ToString());

            if (Transparency != null)
                parts.Add(((int)Math.Round(Transparency.Value * 100)).ToString(CultureInfo.InvariantCulture)
                          + "% transparent");

            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        /// <summary>
        /// A layer that reproduces this origin, targeting the node rather than what is under it.
        ///
        /// One key, not the thousands beneath it, and that is not only tidiness. The host applies
        /// an override to a node and pushes it down itself, so a layer naming the node applies in
        /// one call and keeps applying to elements added under that node afterwards — which a list
        /// of the keys that happened to be there when somebody pressed Scan does not. It is also
        /// what makes a re-scan able to say "this one is ours already": the layer covers the node,
        /// and the node is what the scan reports.
        ///
        /// Visibility is only claimed when the origin is a hiding. A layer asserting "visible" on
        /// everything it adopted would start overruling the layers beneath it.
        /// </summary>
        /// <param name="nodes">
        /// The nodes this state was applied to. More than one where the same state was applied in
        /// several places, which is one layer and not several: a state is the thing somebody
        /// recognises, and four thousand elements coloured one at a time is still one decision to
        /// take responsibility for.
        /// </param>
        /// <param name="id">The new layer's id.</param>
        /// <param name="where">How the place reads, for the layer's name.</param>
        /// <exception cref="ArgumentException">No nodes were given.</exception>
        public AppearanceLayer AsLayer(IReadOnlyList<ElementKey> nodes, string id, string? where = null)
        {
            if (nodes == null || nodes.Count == 0)
                throw new ArgumentException("a layer needs somewhere to point", nameof(nodes));

            var place = string.IsNullOrWhiteSpace(where) ? "what was already there" : where!.Trim();

            var target = LayerTarget.Elements(
                nodes,
                nodes.Count == 1
                    ? place + " and everything under it"
                    : place + ", and everything under them");

            return new AppearanceLayer(id, place + " — " + Describe(), target)
            {
                Visible = IsHidden ? false : (bool?)null,
                Colour = Colour,
                Transparency = Transparency,
                Note = IsRead
                    ? "Adopted from a hiding the host recorded here. Nothing moved when this was made."
                    : "Adopted from what the model was already in. The host does not record where a "
                      + "colour was applied, so this is the highest node whose every element was in "
                      + "that one state. Nothing moved when this was made.",
            };
        }

        /// <inheritdoc />
        public override string ToString() =>
            Describe() + " on " + Covers.ToString("N0", CultureInfo.InvariantCulture)
            + (Covers == 1 ? " element" : " elements") + ", at depth "
            + Depth.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Folds a depth-first walk of the project tree into the levels at which states were applied.
    ///
    /// Fed by whatever is walking the host: <see cref="Enter"/> on the way down, <see cref="Exit"/>
    /// on the way back up, one pair per node. It holds frames for the current path only, so a
    /// federation costs the depth of its deepest branch rather than its size, and it never needs an
    /// element key — which is the expensive thing and the reason the walk does not build one until
    /// this says which nodes matter.
    ///
    /// The colour roll-up is a single rule applied on the way back up: a node whose whole geometry
    /// subtree shares one non-untouched state replaces every origin found beneath it, because those
    /// are the same act seen further down. A node whose subtree disagrees keeps what its children
    /// reported. Run to the root, what survives is the set of highest nodes that are each uniform
    /// in one state, which is the set of places somebody applied something.
    /// </summary>
    public sealed class OriginScan
    {
        private readonly List<Frame> _stack = new List<Frame>();
        private readonly List<AppearanceOrigin> _hidden = new List<AppearanceOrigin>();
        private readonly List<AppearanceOrigin> _finished = new List<AppearanceOrigin>();

        /// <summary>How many nodes have been entered.</summary>
        public int Nodes { get; private set; }

        /// <summary>How many geometry elements have been walked.</summary>
        public int Elements { get; private set; }

        /// <summary>Go down into a node.</summary>
        /// <param name="node">The walk's number for it, handed back on any origin found here.</param>
        /// <param name="hasGeometry">Whether it is a geometry element rather than a grouping node.</param>
        /// <param name="hidesHere">Whether the host records a hiding on this item, not on an ancestor.</param>
        /// <param name="colour">Its colour override, for a geometry element; ignored otherwise.</param>
        /// <param name="transparency">Its transparency override, for a geometry element.</param>
        public void Enter(int node, bool hasGeometry, bool hidesHere,
                          Colour? colour = null, double? transparency = null)
        {
            var parent = _stack.Count == 0 ? null : _stack[_stack.Count - 1];

            // Inherited rather than re-read, because the host does not re-record it: an element
            // inside a hidden parent reports itself as visible and is not drawn. A scan that read
            // IsHidden off the leaves and no further reports a hidden storey as nothing hidden.
            var hiddenAbove = parent != null && (parent.HidesHere || parent.HiddenAbove);

            var frame = new Frame
            {
                Node = node,
                Depth = _stack.Count,
                HidesHere = hidesHere,
                HiddenAbove = hiddenAbove,

                // The one place the level is the host's own answer rather than an inference: this
                // node hides and nothing above it does, so this is where Hide was pressed.
                IsHidingOrigin = hidesHere && !hiddenAbove,
            };

            if (hasGeometry)
            {
                frame.Leaves = 1;
                frame.Any = true;
                frame.Colour = colour;
                frame.Transparency = transparency;
                Elements++;
            }

            _stack.Add(frame);
            Nodes++;
        }

        /// <summary>Come back up out of the node most recently entered.</summary>
        /// <exception cref="InvalidOperationException">There is no node to come up out of.</exception>
        public void Exit()
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("Exit without a matching Enter");

            var frame = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);

            // A hiding is reported at the level the host recorded it, and the count is only known
            // now that the subtree beneath it has been walked.
            if (frame.IsHidingOrigin)
                _hidden.Add(new AppearanceOrigin(frame.Node, frame.Depth, frame.Leaves,
                                                 true, null, null, isRead: true));

            var uniform = frame.Any && !frame.Mixed
                          && (frame.Colour != null || frame.Transparency != null);

            if (uniform)
            {
                // Everything beneath is in this one state, so everything beneath reported this
                // same act from further down. This node replaces the lot: one row for "Level 03 is
                // red" instead of four thousand for the elements that ended up red.
                frame.Found.Clear();
                frame.Found.Add(new AppearanceOrigin(frame.Node, frame.Depth, frame.Leaves,
                                                     false, frame.Colour, frame.Transparency,
                                                     isRead: false));
            }

            var parent = _stack.Count == 0 ? null : _stack[_stack.Count - 1];

            if (parent == null)
            {
                _finished.AddRange(frame.Found);
                return;
            }

            parent.Leaves += frame.Leaves;
            parent.Found.AddRange(frame.Found);

            // Uniformity merges upward: a parent is uniform only when every branch under it agreed
            // and they all agreed with each other. A branch with no geometry says nothing either
            // way rather than making its parent mixed, or a model's folders would break every
            // roll-up above them.
            if (!frame.Any) return;

            if (!parent.Any)
            {
                parent.Any = true;
                parent.Mixed = frame.Mixed;
                parent.Colour = frame.Colour;
                parent.Transparency = frame.Transparency;
                return;
            }

            if (frame.Mixed || parent.Mixed
                || !Nullable.Equals(parent.Colour, frame.Colour)
                || !Nullable.Equals(parent.Transparency, frame.Transparency))
                parent.Mixed = true;
        }

        /// <summary>
        /// The levels at which states were applied, deepest first.
        ///
        /// Deepest first because that is the order the layers go in: a layer on
        /// "Level 03 / Mechanical" has to decide those elements before one on "Level 03" gets to,
        /// and a stack built the other way round has a dead layer in it. Within a depth, the
        /// biggest first, so the same model twice gives the same order.
        /// </summary>
        /// <exception cref="InvalidOperationException">The walk is not finished.</exception>
        public IReadOnlyList<AppearanceOrigin> Origins()
        {
            if (_stack.Count != 0)
                throw new InvalidOperationException(
                    "the walk is still inside " + _stack.Count.ToString(CultureInfo.InvariantCulture)
                    + " node(s); every Enter needs its Exit before the origins mean anything");

            var all = new List<AppearanceOrigin>(_hidden.Count + _finished.Count);

            all.AddRange(_hidden);
            all.AddRange(_finished);

            all.Sort((a, b) =>
            {
                var byDepth = b.Depth.CompareTo(a.Depth);
                if (byDepth != 0) return byDepth;

                var bySize = b.Covers.CompareTo(a.Covers);
                if (bySize != 0) return bySize;

                return a.Node.CompareTo(b.Node);
            });

            return all;
        }

        private sealed class Frame
        {
            internal int Node;
            internal int Depth;
            internal bool HidesHere;
            internal bool HiddenAbove;
            internal bool IsHidingOrigin;

            /// <summary>Geometry elements beneath this node, itself included.</summary>
            internal int Leaves;

            /// <summary>Whether any geometry has been seen beneath this node.</summary>
            internal bool Any;

            /// <summary>Whether the geometry beneath this node disagrees about its state.</summary>
            internal bool Mixed;

            internal Colour? Colour;
            internal double? Transparency;

            /// <summary>Origins found beneath this node that nothing above has subsumed yet.</summary>
            internal readonly List<AppearanceOrigin> Found = new List<AppearanceOrigin>();
        }
    }
}
