using System;
using System.Collections.Generic;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Abstractions
{
    /// <summary>
    /// Saved viewpoints, behind the seam.
    ///
    /// Behind this interface because saved viewpoints are a second appearance write channel: a
    /// viewpoint captures overrides, so restoring one changes what the Appearance Manager's stack
    /// says is true. Routing both through seams is what lets the arbitration between them be
    /// written once, in logic, and tested without a host.
    /// </summary>
    public interface IViewpointStore
    {
        /// <summary>Every saved viewpoint, in host order, flattened across folders.</summary>
        IReadOnlyList<SavedView> All();

        /// <summary>Save the current camera and appearance under a name, returning the new view.</summary>
        SavedView SaveCurrent(string name, string? folder = null);

        /// <summary>Apply a saved viewpoint. This restores its overrides too — see the type remarks.</summary>
        void Apply(string viewId);

        /// <summary>Rename a saved viewpoint.</summary>
        void Rename(string viewId, string newName);

        /// <summary>Delete a saved viewpoint.</summary>
        void Delete(string viewId);
    }

    /// <summary>One saved viewpoint.</summary>
    public readonly struct SavedView
    {
        /// <summary>Host identity, stable for the life of the document.</summary>
        public string Id { get; }

        /// <summary>Display name.</summary>
        public string Name { get; }

        /// <summary>Containing folder, or null at the root.</summary>
        public string? Folder { get; }

        /// <summary>True when the viewpoint carries appearance overrides of its own.</summary>
        public bool HasOverrides { get; }

        /// <summary>
        /// True when the host reports native redlines on this viewpoint. CamelWorks can read their
        /// text but cannot see their geometry, so a report carrying this view says so rather than
        /// silently omitting an instruction somebody drew.
        /// </summary>
        public bool HasRedlines { get; }

        /// <summary>Create a descriptor.</summary>
        public SavedView(string id, string name, string? folder, bool hasOverrides, bool hasRedlines)
        {
            Id = id; Name = name; Folder = folder; HasOverrides = hasOverrides; HasRedlines = hasRedlines;
        }

        /// <inheritdoc />
        public override string ToString() => Folder == null ? Name : Folder + "/" + Name;
    }

    /// <summary>
    /// Clash tests and their results.
    ///
    /// <b>Manage only.</b> The host's clash assembly ships with Manage and not with Simulate, and
    /// any type whose signature names a clash type fails to load where it is absent — including,
    /// left unguarded, the very probe whose job is to report that it is absent. So this interface
    /// lives behind a capability check and its implementation lives in its own assembly, loaded
    /// only after that check passes. On Simulate the board still opens: it hydrates from the
    /// sidecar store instead, and refuses exactly two things — running or creating a test, and
    /// projecting status back into the host.
    /// </summary>
    public interface IClashSource
    {
        /// <summary>False on a host without clash support. Every caller checks before anything else.</summary>
        bool IsAvailable { get; }

        /// <summary>The tests in the document.</summary>
        IReadOnlyList<ClashTestInfo> Tests();

        /// <summary>The results of one test.</summary>
        IReadOnlyList<ClashResultInfo> Results(string testId);

        /// <summary>
        /// Whether tests can be created and run, as opposed to only read.
        ///
        /// Separate from <see cref="IsAvailable"/> on purpose. A document opened read-only, or one
        /// whose clash data the host will not let a plug-in edit, reads perfectly well and refuses
        /// every write — and the difference has to be visible before somebody spends five minutes
        /// building a matrix that cannot be created.
        /// </summary>
        bool CanEdit { get; }

        /// <summary>
        /// Make a test. Returns it as the host now holds it, or null when the host refused.
        ///
        /// Refusal is ordinary: a side that resolves to nothing, a name the host will not take, a
        /// read-only document. It returns null and says why through <see cref="LastProblem"/>
        /// rather than throwing, because a matrix of thirty-six tests must not stop at the fourth.
        /// </summary>
        /// <param name="spec">What the test should be.</param>
        ClashTestInfo? Create(ClashTestSpec spec);

        /// <summary>
        /// Run one test, by the id <see cref="Tests"/> gave it. False when the host refused.
        /// </summary>
        /// <param name="testId">Which test.</param>
        bool Run(string testId);

        /// <summary>Why the last <see cref="Create"/> or <see cref="Run"/> returned nothing.</summary>
        string? LastProblem { get; }
    }

    /// <summary>
    /// What one side of a clash test is made of.
    ///
    /// Three kinds, because the host resolves each one differently and the difference matters. A
    /// whole model is its root item. A saved set is a <i>source</i>, not a snapshot of its
    /// contents — which is what makes a test built on a search set still right after somebody adds
    /// a wall, and is exactly what Clash Detective does when you pick a set by hand. A list of
    /// elements is a list of elements, and stops being right the moment the model changes.
    /// </summary>
    public sealed class ClashSide
    {
        private ClashSide(ClashSideKind kind, string? name, IReadOnlyList<string> scopes,
                          IReadOnlyList<ElementKey> keys)
        {
            Kind = kind; Name = name; Scopes = scopes; Keys = keys;
        }

        /// <summary>Which of the three this is.</summary>
        public ClashSideKind Kind { get; }

        /// <summary>The set name, when the side is a saved set.</summary>
        public string? Name { get; }

        /// <summary>The model scopes, when the side is one or more whole models.</summary>
        public IReadOnlyList<string> Scopes { get; }

        /// <summary>The elements, when the side is an explicit list.</summary>
        public IReadOnlyList<ElementKey> Keys { get; }

        /// <summary>One whole loaded model, named by its scope.</summary>
        /// <param name="scope">The model scope, as <c>IModelSource.Scope</c> gives it.</param>
        public static ClashSide Model(string scope) => Models(new[] { scope });

        /// <summary>
        /// Several whole models at once.
        ///
        /// A discipline is rarely one file. "Structure" on a real job is a frame model, a
        /// foundations model and somebody's precast package, and a side that could only hold one
        /// of them would make the discipline matrix — the matrix people actually draw — impossible
        /// to express.
        /// </summary>
        /// <param name="scopes">The model scopes.</param>
        public static ClashSide Models(IReadOnlyList<string> scopes) =>
            new ClashSide(ClashSideKind.Models, null, scopes ?? Array.Empty<string>(),
                          Array.Empty<ElementKey>());

        /// <summary>A saved selection or search set, by name, kept as a source so it re-resolves.</summary>
        /// <param name="name">The set's name in the host.</param>
        public static ClashSide Set(string name) =>
            new ClashSide(ClashSideKind.Set, name, Array.Empty<string>(), Array.Empty<ElementKey>());

        /// <summary>A fixed list of elements.</summary>
        /// <param name="keys">The elements.</param>
        public static ClashSide Elements(IReadOnlyList<ElementKey> keys) =>
            new ClashSide(ClashSideKind.Elements, null, Array.Empty<string>(),
                          keys ?? Array.Empty<ElementKey>());

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            ClashSideKind.Models => Scopes.Count == 1 ? "one model" : Scopes.Count + " models",
            ClashSideKind.Set => "set " + Name,
            _ => Keys.Count + " elements",
        };
    }

    /// <summary>Which of the three a <see cref="ClashSide"/> is.</summary>
    public enum ClashSideKind
    {
        /// <summary>One or more whole loaded models.</summary>
        Models = 0,

        /// <summary>A saved selection or search set, kept as a source.</summary>
        Set = 1,

        /// <summary>A fixed list of elements.</summary>
        Elements = 2,
    }

    /// <summary>Everything the host needs to make one clash test.</summary>
    public sealed class ClashTestSpec
    {
        /// <summary>Create a spec.</summary>
        /// <param name="name">What the test is called.</param>
        /// <param name="a">The first side.</param>
        /// <param name="b">The second side.</param>
        /// <param name="tolerance">Tolerance in metres. The adapter converts to document units.</param>
        /// <param name="folder">Which clash folder to put it in, or null for the root.</param>
        public ClashTestSpec(string name, ClashSide a, ClashSide b, double tolerance, string? folder = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "CamelWorks test" : name.Trim();
            A = a ?? throw new ArgumentNullException(nameof(a));
            B = b ?? throw new ArgumentNullException(nameof(b));
            Tolerance = tolerance;
            Folder = folder;
        }

        /// <summary>What the test is called.</summary>
        public string Name { get; }

        /// <summary>The first side.</summary>
        public ClashSide A { get; }

        /// <summary>The second side.</summary>
        public ClashSide B { get; }

        /// <summary>
        /// Tolerance in metres.
        ///
        /// In metres here and converted by the adapter, because a millimetre model and a metre
        /// model would otherwise need two different numbers for the same intent, and every caller
        /// above this seam already states thresholds in metres.
        /// </summary>
        public double Tolerance { get; }

        /// <summary>Which clash folder to put it in, or null for the root.</summary>
        public string? Folder { get; }

        /// <inheritdoc />
        public override string ToString() => Name + ": " + A + " against " + B;
    }

    /// <summary>One clash test.</summary>
    public readonly struct ClashTestInfo
    {
        /// <summary>Host identity.</summary>
        public string Id { get; }

        /// <summary>Display name, typically naming zone, level, phase and the discipline pair.</summary>
        public string Name { get; }

        /// <summary>Containing folder, or null at the root.</summary>
        public string? Folder { get; }

        /// <summary>Number of results the test currently holds.</summary>
        public int ResultCount { get; }

        /// <summary>
        /// When the host last ran this test, as ticks. Compared against the newest sidecar
        /// snapshot to notice a run started outside CamelWorks — which is the most frequent action
        /// of the week and raises no plug-in event at all.
        /// </summary>
        public long LastRunTicks { get; }

        /// <summary>Create a descriptor.</summary>
        public ClashTestInfo(string id, string name, string? folder, int resultCount, long lastRunTicks)
        {
            Id = id; Name = name; Folder = folder; ResultCount = resultCount; LastRunTicks = lastRunTicks;
        }

        /// <inheritdoc />
        public override string ToString() => (Folder == null ? Name : Folder + "/" + Name) + " (" + ResultCount + ")";
    }

    /// <summary>One clash result, as the host reports it.</summary>
    public readonly struct ClashResultInfo
    {
        /// <summary>Host identity within its test.</summary>
        public string Id { get; }

        /// <summary>First participant.</summary>
        public ElementKey A { get; }

        /// <summary>Second participant.</summary>
        public ElementKey B { get; }

        /// <summary>Clash point, X.</summary>
        public double PointX { get; }

        /// <summary>Clash point, Y.</summary>
        public double PointY { get; }

        /// <summary>Clash point, Z.</summary>
        public double PointZ { get; }

        /// <summary>The host's own status string.</summary>
        public string? HostStatus { get; }

        /// <summary>The host's own assignee string.</summary>
        public string? HostAssignedTo { get; }

        /// <summary>Create a descriptor.</summary>
        public ClashResultInfo(string id, ElementKey a, ElementKey b, double x, double y, double z,
                               string? hostStatus, string? hostAssignedTo)
        {
            Id = id; A = a; B = b; PointX = x; PointY = y; PointZ = z;
            HostStatus = hostStatus; HostAssignedTo = hostAssignedTo;
        }

        /// <summary>The stable key for this result.</summary>
        public ClashKey Key(double cell = ClashKey.DefaultCell) =>
            ClashKey.Create(A, B, PointX, PointY, PointZ, cell);
    }
}
