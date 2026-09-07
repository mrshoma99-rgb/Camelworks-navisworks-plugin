using System;
using System.Collections.Generic;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Abstractions
{
    /// <summary>
    /// Clash tests and their results.
    ///
    /// Manage only. The host's clash assembly ships with Manage and not with Simulate, and any type
    /// whose signature names a clash type fails to load where it is absent — including, if left
    /// unguarded, the probe whose job is to report that it is absent. So this interface sits behind
    /// a capability check and its implementation lives in its own assembly, loaded only after that
    /// check passes. On Simulate the board still opens, hydrating from the sidecar store, and
    /// refuses two things: running or creating a test, and projecting status back into the host.
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
        /// Put every result of one test where a chain of bands says it belongs, nesting the host's
        /// groups along each trail when the document will hold them.
        ///
        /// Not the call above in a loop. That one takes one name and leaves the rest of the test
        /// alone; this states the whole assignment at once, which is what makes it a single edit to
        /// the document rather than one per group — on a real board, fifty rebuilds of the same
        /// test. It is also the only way to ask for depth, because a trail is only a trail relative
        /// to the others beside it.
        ///
        /// The result may not match the request. Whether the host's clash groups hold other groups
        /// is a fact about the document rather than about the API's types, so the implementation
        /// tries, reads the tree back, and falls back to one group per trail, named for the whole
        /// trail so nothing collides and the board reads the same either way. The answer says which
        /// happened.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="paths">Each bucket: the trail down to it and the results in it.</param>
        ClashNesting Regroup(string testId, IReadOnlyList<ClashGroupPath> paths);

        /// <summary>
        /// Rename the results of one test sequentially: prefix1, prefix2, ... in host order.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="prefix">What each name starts with.</param>
        bool RenameResults(string testId, string prefix);

        /// <summary>
        /// Rename just the given results of one test: prefix1, prefix2, ... in host order.
        /// The rest of the test's results keep their names.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="prefix">What each name starts with.</param>
        bool RenamePicked(string testId, IReadOnlyList<string> resultIds, string prefix);

        /// <summary>
        /// Give one result the name it was given.
        ///
        /// Not the numbering call with a list of one. Renaming a single clash previously went
        /// through <see cref="RenamePicked"/>, which takes a stem and numbers what it is given, so
        /// typing "Corridor duct" produced "Corridor duct1" on every rename. The host has an exact
        /// rename as well.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <param name="name">Its new name, exactly.</param>
        bool RenameResult(string testId, string resultId, string name);

        /// <summary>
        /// Change an existing test's tolerance and kind, keeping the test and its results.
        ///
        /// Not delete-and-remake. A matrix is edited over the life of a project and the results are
        /// the work: statuses, assignments, comment trails, groups, weeks of review by several
        /// people. Moving a legend from a hard clash at 10 mm to a clearance at 50 mm must change
        /// the test that already carries those results in place.
        /// </summary>
        /// <param name="testId">The test.</param>
        /// <param name="toleranceMetres">The tolerance it should now have, in metres.</param>
        /// <param name="kind">The kind of test it should now be.</param>
        bool Retune(string testId, double toleranceMetres, Clash.MatrixTest kind);

        /// <summary>Rename one test.</summary>
        /// <param name="testId">Which test.</param>
        /// <param name="name">The new name.</param>
        bool RenameTest(string testId, string name);

        /// <summary>
        /// Delete results from one test, in the document.
        ///
        /// The one write here that cannot be taken back: the results are gone from the Navisworks
        /// document and from the native clash window, and re-running the test is what brings back
        /// whatever it finds again. Every caller confirms before it asks.
        /// </summary>
        /// <param name="testId">Which test they are in.</param>
        /// <param name="resultIds">Which results.</param>
        bool DeleteResults(string testId, IReadOnlyList<string> resultIds);

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

        /// <summary>
        /// Drop the given tests' Resolved results from storage - the host's own compact, and
        /// the only per-result delete any clash tool honestly has: the engine owns its
        /// results, and the next run recreates whatever still clashes.
        /// </summary>
        /// <param name="testIds">Which tests.</param>
        bool CompactTests(IReadOnlyList<string> testIds);

        /// <summary>
        /// What one test is made of, as far as the host will say: a saved set per side, whole
        /// models per side, or neither when the side is an arbitrary selection. Null when the
        /// test is gone. This is what lets a matrix be read back OUT of a document's tests.
        /// </summary>
        /// <param name="testId">Which test.</param>
        ClashTestSides? Sides(string testId);

        /// <summary>
        /// Put the camera where the host's own saved view of one result stands — the view the
        /// engine composed when the test ran, which frames both elements the way the native
        /// clash window would. False when the host has no view for it.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        bool ApplyResultView(string testId, string resultId);

        /// <summary>
        /// Select the elements the named results are between, using the handles the engine already
        /// holds.
        ///
        /// Not a key resolve. A row on the board carries an
        /// <see cref="CamelWorks.Core.Identity.ElementKey"/> for each side, and turning one of
        /// those back into an element can mean indexing every geometry leaf in the federation. That
        /// is the right cost for a key that arrived in a file from elsewhere and the wrong cost
        /// here: these results came out of this document and the engine still holds the two items
        /// each is between, so selecting them is a copy rather than a search.
        ///
        /// Focus is the most frequent action on a board and must stay cheap.
        /// </summary>
        /// <param name="testId">Which test the results belong to.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="add">True to add to the current selection rather than replace it.</param>
        /// <returns>How many elements were selected, or -1 when there is no engine.</returns>
        int SelectResults(string testId, IReadOnlyList<string> resultIds, bool add);

        /// <summary>
        /// Everything the board needs, in one walk of the clash tree.
        ///
        /// Reading a board was previously <c>Tests()</c>, then <c>Results(id)</c> and
        /// <c>Sides(id)</c> per test. Each of those located the test by walking the whole test tree,
        /// so the read was quadratic in the number of tests, and <c>Tests()</c> also materialised
        /// every result a second time just to count them. One walk answers all of it.
        ///
        /// The comment trail is deliberately excluded: a board shows the newest comment and the
        /// count, and reading every comment body is a large share of the read for something rarely
        /// looked at. <see cref="CommentsFor"/> fetches them on demand.
        /// </summary>
        /// <param name="onProgress">Called with the running result count; return false to stop.</param>
        /// <returns>Every test, with what it is between and what it found.</returns>
        IReadOnlyList<ClashTestRead> ReadEverything(Func<int, bool>? onProgress);

        /// <summary>
        /// Read named properties off the elements each of a test's results is between.
        ///
        /// On the clash seam rather than the document seam. The alternative is to take each result's
        /// element keys and resolve them through the document, which for a key that is not an
        /// instance GUID means indexing every geometry leaf in the federation. The engine already
        /// holds both elements of every result.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="wanted">The properties to read, each with the id to answer under.</param>
        /// <param name="onProgress">Called with the running result count; return false to stop.</param>
        IReadOnlyList<ClashResultValues> ValuesFor(string testId,
                                                   IReadOnlyList<ClashPropertyWanted> wanted,
                                                   Func<int, bool>? onProgress);

        /// <summary>
        /// The IFC GlobalIds of the elements each of a test's results is between.
        ///
        /// For an export a receiving tool can select from. Off the engine's own elements, for the
        /// same reason as <see cref="ValuesFor"/>.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="onProgress">Called with the running result count; return false to stop.</param>
        IReadOnlyList<ClashResultIfc> IfcFor(string testId, Func<int, bool>? onProgress);

        /// <summary>
        /// The whole comment trail of every result of one test.
        ///
        /// Kept off the read so a board opens on its clashes rather than on everything anybody
        /// ever said about them. Wanted by an export, which carries the conversation, and by a
        /// row somebody opens.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="onProgress">Called with the running result count; return false to stop.</param>
        IReadOnlyList<ClashResultComments> CommentsFor(string testId, Func<int, bool>? onProgress);

        /// <summary>
        /// One result's comment trail, oldest first.
        ///
        /// The whole-test read above is for an export. This is for a person who has opened one
        /// clash and wants to read what has been said about it - which, until it existed, nothing
        /// in the product could do: the board showed how many comments there were and the newest
        /// one, and the rest were write-only.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <returns>The trail, oldest first, each carrying the host's id for it.</returns>
        IReadOnlyList<ClashComment> CommentsOn(string testId, string resultId);

        /// <summary>
        /// Take one comment off a result.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <param name="commentId">The host's id for the comment.</param>
        /// <returns>True when the document took it.</returns>
        bool DeleteComment(string testId, string resultId, long commentId);

        /// <summary>
        /// Change what one comment says.
        ///
        /// The host has no edit. Every field of a comment is read-only, so this rebuilds the trail
        /// with a new comment in the old one's place, and the original's date and id do not survive.
        /// The caller has to be prepared to state that.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <param name="commentId">The host's id for the comment being replaced.</param>
        /// <param name="body">What it should say now.</param>
        /// <returns>True when the document took it.</returns>
        bool EditComment(string testId, string resultId, long commentId, string body);

        /// <summary>
        /// Every property name carried by the elements the clashes are between.
        ///
        /// Not the model's properties. A property column reads off a clashing element, so the list
        /// to choose from is the vocabulary of the clashing elements rather than a sample of the
        /// federation, which is what it used to be. A sample can miss the one property only the
        /// clashing pipes carry, and it reads thousands of elements nobody asked about.
        ///
        /// Across every test, because a column added here is added to all of them.
        /// </summary>
        /// <param name="settleAfter">Stop once this many elements in a row add no new name.</param>
        /// <param name="onProgress">Called with the running element count; return false to stop.</param>
        IReadOnlyList<ClashPropertyName> PropertyVocabulary(int settleAfter,
                                                            Func<int, bool>? onProgress);

        /// <summary>
        /// The host's rendered image of one result — scene plus the clash highlight — as PNG
        /// bytes, or null when the host cannot make one.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <param name="width">Pixels.</param>
        /// <param name="height">Pixels.</param>
        byte[]? ResultSnapshot(string testId, string resultId, int width, int height);
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
        private ClashSide(ClashSideKind kind, IReadOnlyList<ClashSetName> named,
                          IReadOnlyList<string> scopes, IReadOnlyList<ElementKey> keys)
        {
            Kind = kind; Named = named; Scopes = scopes; Keys = keys;
        }

        /// <summary>
        /// The folders the set sits in, innermost last, or null when none were recorded.
        ///
        /// A name is not an identity. A real document has "MEP" inside Level 01, another inside
        /// Level 02, and more under package folders. Resolved by name, all of them become whichever
        /// one a depth-first walk reaches first, so a matrix pointed at four different disciplines
        /// tests one of them four times.
        /// </summary>
        public string? Path => Named.Count == 0 ? null : Named[0].Path;

        /// <summary>Which of the three this is.</summary>
        public ClashSideKind Kind { get; }

        /// <summary>
        /// The first set's name, when the side is saved sets.
        ///
        /// For the callers that only ever put one set on a side. <see cref="Named"/> is what a
        /// side actually holds.
        /// </summary>
        public string? Name => Named.Count == 0 ? null : Named[0].Name;

        /// <summary>
        /// The saved sets this side is made of, in the order they were chosen.
        ///
        /// More than one, because a side is a discipline and a discipline is rarely one set: the
        /// host takes a selection built from several sources, and a side that could hold only one
        /// meant making the same test three times or building a set whose only purpose was to be
        /// the union of three others.
        /// </summary>
        public IReadOnlyList<ClashSetName> Named { get; }

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
            new ClashSide(ClashSideKind.Models, Array.Empty<ClashSetName>(),
                          scopes ?? Array.Empty<string>(), Array.Empty<ElementKey>());

        /// <summary>A saved selection or search set, by name, kept as a source so it re-resolves.</summary>
        /// <param name="name">The set's name in the host.</param>
        /// <param name="path">The folders it sits in, innermost last, or null.</param>
        public static ClashSide Set(string name, string? path = null) =>
            Sets(new[] { new ClashSetName(name, path) });

        /// <summary>Several saved sets at once, each kept as a source so it re-resolves.</summary>
        /// <param name="named">The sets, in the order they were chosen.</param>
        public static ClashSide Sets(IReadOnlyList<ClashSetName> named) =>
            new ClashSide(ClashSideKind.Set, named ?? Array.Empty<ClashSetName>(),
                          Array.Empty<string>(), Array.Empty<ElementKey>());

        /// <summary>A fixed list of elements.</summary>
        /// <param name="keys">The elements.</param>
        public static ClashSide Elements(IReadOnlyList<ElementKey> keys) =>
            new ClashSide(ClashSideKind.Elements, Array.Empty<ClashSetName>(),
                          Array.Empty<string>(), keys ?? Array.Empty<ElementKey>());

        /// <inheritdoc />
        public override string ToString() => Kind switch
        {
            ClashSideKind.Models => Scopes.Count == 1 ? "one model" : Scopes.Count + " models",
            ClashSideKind.Set => Named.Count == 1 ? "set " + Name : Named.Count + " sets",
            _ => Keys.Count + " elements",
        };
    }

    /// <summary>One saved set a side is made of: its name, and the folders it sits in.</summary>
    public sealed class ClashSetName
    {
        /// <summary>Name a set.</summary>
        /// <param name="name">The set's name in the host.</param>
        /// <param name="path">The folders it sits in, innermost last, or null.</param>
        public ClashSetName(string name, string? path = null)
        {
            Name = name ?? string.Empty;
            Path = path;
        }

        /// <summary>The set's name in the host.</summary>
        public string Name { get; }

        /// <summary>
        /// The folders it sits in, innermost last, or null when none were recorded.
        ///
        /// A name is not an identity. A real document has "MEP" inside Level 01, another inside
        /// Level 02, and more under package folders. Resolved by name, all of them become
        /// whichever one a depth-first walk reaches first, so a matrix pointed at four different
        /// disciplines tests one of them four times.
        /// </summary>
        public string? Path { get; }

        /// <inheritdoc />
        public override string ToString() =>
            string.IsNullOrEmpty(Path) ? Name : Path + " / " + Name;
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
        /// <param name="kind">Which kind of test. Hard unless the matrix's legend says otherwise.</param>
        public ClashTestSpec(string name, ClashSide a, ClashSide b, double tolerance,
                             string? folder = null,
                             Clash.MatrixTest kind = Clash.MatrixTest.Hard)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "CamelWorks test" : name.Trim();
            A = a ?? throw new ArgumentNullException(nameof(a));
            B = b ?? throw new ArgumentNullException(nameof(b));
            Tolerance = tolerance;
            Folder = folder;
            Kind = kind;
        }

        /// <summary>
        /// Which kind of test: hard, conservative, clearance or duplicates.
        ///
        /// On the spec and not left to the adapter's default, because the matrix's whole point is
        /// that each cell chooses — and for a long time this was silently Hard for every cell, so
        /// a clearance legend produced hard tests and nobody was told.
        /// </summary>
        public Clash.MatrixTest Kind { get; }

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

    /// <summary>What a test's two sides are made of, as read back from the host.</summary>
    public sealed class ClashTestSides
    {
        /// <summary>Create a description.</summary>
        public ClashTestSides(string? setA, string? setB,
                              IReadOnlyList<string> scopesA, IReadOnlyList<string> scopesB,
                              string kind, double toleranceMetres)
        {
            SetA = setA;
            SetB = setB;
            ScopesA = scopesA ?? Array.Empty<string>();
            ScopesB = scopesB ?? Array.Empty<string>();
            Kind = kind ?? "Hard";
            ToleranceMetres = toleranceMetres;
        }

        /// <summary>The saved set behind side A, or null when it is not a single set.</summary>
        public string? SetA { get; }

        /// <summary>The saved set behind side B, or null when it is not a single set.</summary>
        public string? SetB { get; }

        /// <summary>The whole models behind side A, when it is whole models.</summary>
        public IReadOnlyList<string> ScopesA { get; }

        /// <summary>The whole models behind side B, when it is whole models.</summary>
        public IReadOnlyList<string> ScopesB { get; }

        /// <summary>The host's test type name.</summary>
        public string Kind { get; }

        /// <summary>The tolerance, converted to metres.</summary>
        public double ToleranceMetres { get; }

        /// <summary>True when side A could be described at all.</summary>
        public bool KnowsA => SetA != null || ScopesA.Count > 0;

        /// <summary>True when side B could be described at all.</summary>
        public bool KnowsB => SetB != null || ScopesB.Count > 0;
    }

    /// <summary>One test as a single read hands it back: what it is, what it is between, what it found.</summary>
    public sealed class ClashTestRead
    {
        /// <summary>Name it.</summary>
        /// <param name="test">The test.</param>
        /// <param name="shape">What it is between, or null when the host will not say.</param>
        /// <param name="results">What it found.</param>
        public ClashTestRead(ClashTestInfo test, ClashTestSides? shape,
                             IReadOnlyList<ClashResultInfo> results)
        {
            Test = test;
            Shape = shape;
            Results = results ?? Array.Empty<ClashResultInfo>();
        }

        /// <summary>The test.</summary>
        public ClashTestInfo Test { get; }

        /// <summary>What it is between, or null when the host will not say.</summary>
        public ClashTestSides? Shape { get; }

        /// <summary>What it found.</summary>
        public IReadOnlyList<ClashResultInfo> Results { get; }
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

        /// <summary>
        /// The full authored comment trail, oldest first.
        ///
        /// The whole trail and not just the newest, because a review round exports it and a
        /// manager displays it — and a conversation whose middle is missing is not a record.
        /// </summary>
        public IReadOnlyList<Clash.ClashComment> Comments { get; set; } =
            Array.Empty<Clash.ClashComment>();

        /// <summary>
        /// The host's distance for the result, in document units, or null when it has none.
        /// Negative is penetration on a hard clash; positive is the shortfall on a clearance.
        /// </summary>
        public double? Distance { get; set; }

        /// <summary>The stable key for this result.</summary>
        public ClashKey Key(double cell = ClashKey.DefaultCell) =>
            ClashKey.Create(A, B, PointX, PointY, PointZ, cell);
    }
}
