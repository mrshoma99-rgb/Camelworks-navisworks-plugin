using System;
using System.Collections.Generic;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Identity;
using CamelWorks.Core.Project;

namespace CamelWorks.UI.Views
{
    /// <summary>One saved set, as a screen needs to read it: what it is called and what it holds.</summary>
    public sealed class SeatSet
    {
        /// <summary>Describe one.</summary>
        /// <param name="name">Its name.</param>
        /// <param name="folder">The folders it sits in, innermost last, or empty at the top.</param>
        /// <param name="search">True for a set the host holds as a query rather than a fixed list.</param>
        /// <param name="members">How many elements are in it.</param>
        public SeatSet(string name, string folder, bool search, int members)
        {
            Name = name ?? string.Empty;
            Folder = folder ?? string.Empty;
            IsSearch = search;
            Members = members;
        }

        /// <summary>Its name.</summary>
        public string Name { get; }

        /// <summary>The folders it sits in, innermost last, or empty at the top.</summary>
        public string Folder { get; }

        /// <summary>True for a search set — one the host re-runs rather than one it stored.</summary>
        public bool IsSearch { get; }

        /// <summary>How many elements are in it.</summary>
        public int Members { get; }
    }

    /// <summary>
    /// The seat a screen is running in: everything it needs from the host, and nothing else.
    ///
    /// <b>Why this exists.</b> Every screen in this product took a <see cref="Session"/>, and a
    /// Session cannot be made without Navisworks — its constructor takes an Autodesk document.
    /// So no screen could be built anywhere except inside a licensed CAD application, and the
    /// only way to look at one was to install the plug-in and click through to it.
    ///
    /// That is not an inconvenience, it is why layout faults ship. A column header drawn four
    /// pixels short of its own letters, a rotated name clipped by its border, a folder band that
    /// collapses to nothing and cannot be pressed to open it again — every one of those is
    /// visible in a picture and invisible in a compiler, and the picture cost a seat, a licence
    /// and somebody's afternoon. They were all found by a person looking at a screen and
    /// reporting it, which is the slowest and most expensive way a product can learn.
    ///
    /// <b>What a seat is.</b> The narrow questions a screen actually asks, not the host's whole
    /// API: what the clash engine can do, where the project is kept, what saved sets and models
    /// there are, and what is selected. Answered by Navisworks on a real seat, and by
    /// <see cref="EmptySeat"/> in the render harness — where the screen is the shipping screen,
    /// drawn by WPF at the size WPF gives it, in the font it will really be read in.
    ///
    /// <b>What it is not.</b> It is not a way to fake a model. Nothing here pretends a document
    /// is loaded that is not: an empty seat says it has no sets, no models and nothing selected,
    /// and every screen already has to be right about that — it is the state a coordinator is in
    /// for the first minute of every session.
    /// </summary>
    public interface ISeat
    {
        /// <summary>The host's clash engine. Never null; ask <see cref="IClashSource.IsAvailable"/> first.</summary>
        IClashSource Clash { get; }

        /// <summary>Why the clash engine is unavailable, when it is, or null.</summary>
        string? ClashProblem { get; }

        /// <summary>Where this project's own data is kept.</summary>
        ProjectStore Store { get; }

        /// <summary>Write a line into the project's history.</summary>
        /// <param name="kind">What sort of act it was.</param>
        /// <param name="summary">One line saying what happened.</param>
        /// <param name="detail">More, when there is more.</param>
        void Record(string kind, string summary, string? detail = null);

        /// <summary>Every saved set the document holds, in the order it lists them.</summary>
        IReadOnlyList<SeatSet> Sets();

        /// <summary>The names of the models loaded, in the order they were appended.</summary>
        IReadOnlyList<string> Models();

        /// <summary>The keys of whatever is selected in the host right now.</summary>
        IReadOnlyList<ElementKey> Selected();

        /// <summary>
        /// The keys of everything drawn in the document: not hidden, and carrying geometry.
        ///
        /// Streamed rather than listed, because a federated model is millions of items and the
        /// screen that asks for this reports progress while it reads. A list would have to be
        /// built in full before the first number could be shown.
        /// </summary>
        IEnumerable<ElementKey> Visible();
    }

    /// <summary>
    /// A seat with nothing in it: no clash engine, no sets, no models, nothing selected.
    ///
    /// <b>This is a real state, not a stub.</b> It is what a screen faces before a document is
    /// open, on a Navisworks edition with no clash engine, and in the render harness — and a
    /// screen that draws wrongly in it draws wrongly on somebody's first minute of the day. The
    /// harness renders this seat precisely because it is the state hardest to remember to check.
    /// </summary>
    public sealed class EmptySeat : ISeat
    {
        private readonly ProjectStore _store;
        private readonly IClashSource _clash;

        /// <summary>Take a seat with nothing in it.</summary>
        /// <param name="store">Where to keep things. A scratch store is fine.</param>
        /// <param name="clash">The clash engine to answer with. <see cref="Host.WithoutClash"/>
        /// hands out one that says it is unavailable, which is the usual choice here.</param>
        public EmptySeat(ProjectStore store, IClashSource clash)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _clash = clash ?? throw new ArgumentNullException(nameof(clash));
        }

        /// <inheritdoc />
        public IClashSource Clash => _clash;

        /// <inheritdoc />
        public string? ClashProblem => _clash.IsAvailable ? null : "No document is open.";

        /// <inheritdoc />
        public ProjectStore Store => _store;

        /// <inheritdoc />
        public void Record(string kind, string summary, string? detail = null) { }

        /// <inheritdoc />
        public IReadOnlyList<SeatSet> Sets() => Array.Empty<SeatSet>();

        /// <inheritdoc />
        public IReadOnlyList<string> Models() => Array.Empty<string>();

        /// <inheritdoc />
        public IReadOnlyList<ElementKey> Selected() => Array.Empty<ElementKey>();

        /// <inheritdoc />
        public IEnumerable<ElementKey> Visible() => Array.Empty<ElementKey>();
    }
}
