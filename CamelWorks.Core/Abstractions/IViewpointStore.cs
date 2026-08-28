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

        /// <summary>
        /// Move a saved viewpoint into a folder, making the folder if it is not there.
        /// </summary>
        /// <param name="viewId">The viewpoint's path.</param>
        /// <param name="folder">The folder path, or null to move it to the top.</param>
        /// <returns>The viewpoint's new path, or null when it could not be moved.</returns>
        string? MoveTo(string viewId, string? folder);

        /// <summary>
        /// Give one viewpoint the overrides another one carries, keeping its own camera.
        ///
        /// The host has no way to set a saved viewpoint's overrides directly, so this is done by
        /// restoring the source, putting the camera back where the target had it, and re-capturing
        /// the target from that. Which means <b>the target's redlines are replaced by the
        /// source's</b> — the host's own replace does that and there is no call that does not.
        /// Stated rather than discovered, since redlines somebody drew are not recoverable.
        /// </summary>
        /// <param name="fromViewId">The viewpoint whose overrides are wanted.</param>
        /// <param name="toViewId">The viewpoint to put them on.</param>
        /// <returns>True when it happened.</returns>
        bool CopyOverrides(string fromViewId, string toViewId);

        /// <summary>Delete a saved viewpoint.</summary>
        void Delete(string viewId);
    }

    /// <summary>One saved viewpoint.</summary>
    public readonly struct SavedView
    {
        private readonly string? _id;
        private readonly string? _name;

        /// <summary>Host identity, stable for the life of the document.</summary>
        public string Id => _id ?? string.Empty;

        /// <summary>Display name.</summary>
        public string Name => _name ?? string.Empty;

        /// <summary>Containing folder, or null at the root.</summary>
        public string? Folder { get; }

        /// <summary>True when the viewpoint carries appearance overrides of its own.</summary>
        public bool HasOverrides { get; }

        /// <summary>
        /// True when the host reports native redlines on this viewpoint.
        ///
        /// Always false against Navisworks, and the reason is worth recording where somebody will
        /// look for it: a saved viewpoint's redlines come back as an <c>LcOpRedlineList</c>, which
        /// exposes no public member at all — not a count, not an enumerator. There is no call that
        /// answers "does this viewpoint have redlines on it", so nothing here can claim one does.
        /// The field stays because a host that could answer would fill it in.
        /// </summary>
        public bool HasRedlines { get; }

        /// <summary>Create a descriptor.</summary>
        public SavedView(string id, string name, string? folder, bool hasOverrides, bool hasRedlines)
        {
            _id = id; _name = name; Folder = folder; HasOverrides = hasOverrides; HasRedlines = hasRedlines;
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

        /// <summary>Why the last write returned nothing or false.</summary>
        string? LastProblem { get; }

        // ---- The manager's write-backs ----------------------------------------------------
        //
        // Every one of these follows the same host contract: copy the test, mutate the copy,
        // replace the original with it. Mutating a live test in place is not supported by the
        // host, and half-mutating one before an exception would corrupt what the user had. All
        // return false and set LastProblem when the host refuses — a read-only document and a
        // Simulate seat are ordinary states, not exceptions.

        /// <summary>
        /// Set the host status on the given results of one test.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="status">A host status name: New, Active, Reviewed, Approved, Resolved.</param>
        bool SetStatus(string testId, IReadOnlyList<string> resultIds, string status);

        /// <summary>
        /// Set the assignee on the given results, into the host's own assigned-to field so the
        /// native clash window shows it too. Empty clears the assignment.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="assignee">Who, or empty to clear.</param>
        bool Assign(string testId, IReadOnlyList<string> resultIds, string assignee);

        /// <summary>
        /// Append an authored comment to the given results, as a native comment so every tool
        /// that reads the document sees it.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="body">The comment.</param>
        /// <param name="author">Who wrote it.</param>
        bool Comment(string testId, IReadOnlyList<string> resultIds, string body, string author);

        /// <summary>
        /// Put the given results of one test into a named group, making the group when it is not
        /// there. An empty name moves them out of any group. Projected as the host's own result
        /// groups, so the native clash window shows the same structure.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="groupName">The group, or empty for none.</param>
        bool Regroup(string testId, IReadOnlyList<string> resultIds, string groupName);

        /// <summary>
        /// Rename the results of one test sequentially: prefix1, prefix2, ... in host order.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="prefix">What each name starts with.</param>
        bool RenameResults(string testId, string prefix);

        /// <summary>Rename one test.</summary>
        /// <param name="testId">Which test.</param>
        /// <param name="name">The new name.</param>
        bool RenameTest(string testId, string name);

        /// <summary>Delete the given tests, results and all.</summary>
        /// <param name="testIds">Which tests.</param>
        bool DeleteTests(IReadOnlyList<string> testIds);

        /// <summary>
        /// Move the given tests into a folder, making it when it is not there. Null or empty
        /// moves them to the root.
        /// </summary>
        /// <param name="testIds">Which tests.</param>
        /// <param name="folder">The folder name, or null for the root.</param>
        bool MoveTests(IReadOnlyList<string> testIds, string? folder);
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
        private readonly string? _id;
        private readonly string? _name;

        /// <summary>Host identity.</summary>
        public string Id => _id ?? string.Empty;

        /// <summary>Display name, typically naming zone, level, phase and the discipline pair.</summary>
        public string Name => _name ?? string.Empty;

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
            _id = id; _name = name; Folder = folder; ResultCount = resultCount; LastRunTicks = lastRunTicks;
        }

        /// <inheritdoc />
        public override string ToString() => (Folder == null ? Name : Folder + "/" + Name) + " (" + ResultCount + ")";
    }

    /// <summary>
    /// One clash result, as the host reports it.
    ///
    /// A class rather than a struct since the manager grew it: the adapter reads everything the
    /// board or the manager could want in the one pass where it is already holding the elements,
    /// because resolving an element key back to its item later costs a full federation index —
    /// which is exactly what froze the board on a real project.
    /// </summary>
    public sealed class ClashResultInfo
    {
        /// <summary>Create a descriptor.</summary>
        public ClashResultInfo(string id, ElementKey a, ElementKey b, double x, double y, double z,
                               string? hostStatus, string? hostAssignedTo)
        {
            Id = id ?? string.Empty; A = a; B = b; PointX = x; PointY = y; PointZ = z;
            HostStatus = hostStatus; HostAssignedTo = hostAssignedTo;
        }

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

        /// <summary>The result's display name, as the host shows it.</summary>
        public string? Name { get; set; }

        /// <summary>The host result group it sits in, or null at the test's root.</summary>
        public string? GroupName { get; set; }

        /// <summary>First participant's display name.</summary>
        public string? NameA { get; set; }

        /// <summary>Second participant's display name.</summary>
        public string? NameB { get; set; }

        /// <summary>First participant's category, read while the adapter held the element.</summary>
        public string? CategoryA { get; set; }

        /// <summary>Second participant's category, read while the adapter held the element.</summary>
        public string? CategoryB { get; set; }

        /// <summary>How many native comments the result carries.</summary>
        public int CommentCount { get; set; }

        /// <summary>The newest native comment's text, or null when there is none.</summary>
        public string? LastComment { get; set; }

        /// <summary>The stable key for this result.</summary>
        public ClashKey Key(double cell = ClashKey.DefaultCell) =>
            ClashKey.Create(A, B, PointX, PointY, PointZ, cell);
    }
}
