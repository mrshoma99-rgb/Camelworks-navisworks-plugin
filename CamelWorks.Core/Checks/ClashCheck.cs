using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Findings;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Checks
{
    /// <summary>
    /// Clash detection, as a check: the host's own clash tests, read through
    /// <see cref="IClashSource"/>.
    ///
    /// In Core rather than beside the board it draws, because the read, the scope, the run wording
    /// and the host echo are the parts that can be wrong, and a licensed seat is the only place
    /// the board itself can be looked at. Everything here is asserted over
    /// <see cref="Testing.FakeClashSource"/>.
    ///
    /// The host handle is private to this class. A finding carries its host test and result id in
    /// <see cref="Finding.Facts"/> under <see cref="ClashFindings.HostTest"/> and
    /// <see cref="ClashFindings.HostId"/>, and every capability here reads them back off the
    /// findings it is handed, so no signature on the seam names a host id.
    /// </summary>
    public sealed class ClashCheck : ICheck
    {
        private readonly IClashSource _source;
        private readonly double _metresPerUnit;
        private readonly double _cell;
        private readonly string? _unavailable;

        // The tolerance and kind of each test, as the last read or Runs() call saw them. Held
        // because Sides() is a host call that walks the test's selection: the rail asks every test
        // what it is between on every keystroke in its search box.
        private readonly Dictionary<string, ClashTestSides?> _shapes =
            new Dictionary<string, ClashTestSides?>(StringComparer.OrdinalIgnoreCase);

        // Which test each result the last read reported came from. ClashGroupPath names results
        // and not the test they are in, so this is how the grouping capability knows which test
        // to nest them in without the seam carrying a host id.
        private readonly Dictionary<string, string> _testOfResult =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Read the host's clash tests as a check.</summary>
        /// <param name="source">The host's clash data.</param>
        /// <param name="metresPerUnit">
        /// How many metres one model unit is.
        ///
        /// The host reports points and distances in the document's own units. This is where they
        /// become metres, once, on the way into the findings, so nothing above this has to know
        /// which leg a finding came from to read a length off it.
        /// </param>
        /// <param name="unavailable">
        /// Why this seat has no clash engine, for the empty state. Null uses a plain sentence.
        /// </param>
        /// <param name="cell">The cell size the clash keys are quantised to.</param>
        public ClashCheck(IClashSource source, double metresPerUnit = 1.0,
                          string? unavailable = null, double cell = ClashKey.DefaultCell)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _metresPerUnit = metresPerUnit > 0 ? metresPerUnit : 1.0;
            _unavailable = unavailable;
            _cell = cell;

            Columns = ClashColumns.Declare(finding => ThresholdMetres(TestOf(finding)));

            HostEcho = new Echo(this);
            Grouping = new Groups(this);
            Runner = new Rerun(this);
            Naming = new Names(this);
            Properties = new Props(this);
            Geometry = new Sight(this);
        }

        /// <inheritdoc />
        public string Key => FindingSource.Clash;

        /// <inheritdoc />
        public string Name => "Clash detection";

        /// <inheritdoc />
        public string RunWord => "test";

        /// <inheritdoc />
        public string ResultWord => "clash";

        /// <inheritdoc />
        public bool IsAvailable => _source.IsAvailable;

        /// <inheritdoc />
        public string? Unavailable =>
            IsAvailable ? null
            : _unavailable ?? "This Navisworks edition has no clash engine.";

        /// <summary>
        /// The two participants and how deep the clash is.
        ///
        /// The depth is toned against the tolerance of the test the finding came from, read off
        /// the run rather than off a field the board holds for whichever run is open: a 50 mm
        /// drainage test and a 10 mm ceiling test do not share a definition of bad, and a board
        /// showing both at once was toning them against one number.
        /// </summary>
        public IReadOnlyList<ResultColumn> Columns { get; }

        /// <inheritdoc />
        public ICheckHostEcho? HostEcho { get; }

        /// <inheritdoc />
        public ICheckGrouping? Grouping { get; }

        /// <inheritdoc />
        public ICheckRunner? Runner { get; }

        /// <inheritdoc />
        public ICheckNaming? Naming { get; }

        /// <inheritdoc />
        public ICheckProperties? Properties { get; }

        /// <inheritdoc />
        public ICheckGeometry? Geometry { get; }

        /// <summary>
        /// The number behind <see cref="CheckRun.Threshold"/>, in metres, or null when the host
        /// records none for this test.
        ///
        /// A typed accessor rather than a parse of the worded threshold. The worded form is for
        /// the rail to print; a caller that needs the number to tone rows by must never read it
        /// back out of the words, which are rounded and carry their unit in the string.
        /// </summary>
        /// <param name="runId">The host's id for the test.</param>
        public double? ThresholdMetres(string? runId)
        {
            var shape = Shape(runId);

            return shape?.ToleranceMetres;
        }

        /// <summary>
        /// One property, read off every result of one test in a single pass.
        ///
        /// Not on <see cref="ICheckProperties"/>, which answers for one finding at a time. Filling
        /// a column over a whole test through that would be one host call per result, and each one
        /// of them locates its test by walking the document's test tree.
        /// </summary>
        /// <param name="runId">The host's id for the test.</param>
        /// <param name="wanted">The properties to read, each with the id to answer under.</param>
        /// <param name="onProgress">Called with how many results have been read; false stops it.</param>
        public IReadOnlyList<ClashResultValues> ValuesFor(string runId,
                                                          IReadOnlyList<ClashPropertyWanted> wanted,
                                                          Func<int, bool>? onProgress) =>
            _source.ValuesFor(runId, wanted, onProgress);

        /// <summary>
        /// How bad, against this test's own tolerance: 1.0 exactly at it.
        ///
        /// Negative distances are penetrations and are what a hard clash test reports, so the sign
        /// is dropped and the ratio is against the tolerance the test ran at. Null when the host
        /// reported no distance, or the test records no tolerance to measure against.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public double? Severity(Finding finding)
        {
            var metres = DistanceMetres(finding);
            var threshold = ThresholdMetres(TestOf(finding));

            if (metres == null || threshold == null || Math.Abs(threshold.Value) <= 0) return null;

            return Math.Abs(metres.Value) / Math.Abs(threshold.Value);
        }

        /// <inheritdoc />
        public string SeverityBand(double? severity) =>
            severity == null ? "no measure"
            : severity.Value >= 4 ? "far past tolerance"
            : severity.Value >= 1 ? "past tolerance"
            : "within tolerance";

        /// <summary>
        /// The ids this clash would also be recognised under: the same pair, one cell away.
        ///
        /// A clash finding's id carries the cell its point falls in, so a model re-exported 40 mm
        /// out reports every clash gone and an equal number new. The 26 neighbouring cells are
        /// offered so a run comparison can match across that, and a match made through one of them
        /// is counted separately because a large count means the model has moved.
        ///
        /// Within the test, and deliberately so. A finding's id carries the test that found it and
        /// so does every alias offered here, so a result re-scoped from one test to another is one
        /// gone and one new rather than one carried across. The alternative — aliases that ignore
        /// the test — lets a finding match a result in a different test, and a journalled id is
        /// claimed once, so whichever finding offered it first would take another test's history.
        /// A re-scoped result losing its history is visible in the counts and can be put right by
        /// hand; a sign-off moved onto a different test's clash is neither.
        ///
        /// Nothing is offered for a finding with no point: its own id carries no cell, so a
        /// neighbouring cell is not an id it could have been recorded under.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public IEnumerable<string> Aliases(Finding finding)
        {
            if (finding == null || finding.Elements.Count != 2) yield break;
            if (finding.X == null || finding.Y == null || finding.Z == null) yield break;

            var key = ClashKey.Create(finding.Elements[0], finding.Elements[1],
                                      finding.X.Value, finding.Y.Value, finding.Z.Value, _cell);

            var identity = ClashFindings.Identity(TestOf(finding));

            foreach (var near in key.NeighbourCells())
            {
                var id = Finding.Create(FindingSource.Clash, finding.Rule, finding.Title,
                                        finding.Elements, ClashFindings.CellOf(near),
                                        identity).Id;

                if (!string.Equals(id, finding.Id, StringComparison.Ordinal)) yield return id;
            }
        }

        /// <summary>
        /// Every clash test in the document, without reading a result.
        ///
        /// Two host calls per test — the test list, then its two sides — which is what the rail
        /// costs before anybody presses Read.
        /// </summary>
        public IReadOnlyList<CheckRun> Runs()
        {
            var found = new List<CheckRun>();

            foreach (var test in _source.Tests())
                found.Add(Run(test, Shape(test.Id, reread: true)));

            return found;
        }

        /// <summary>
        /// Read the host's results.
        ///
        /// Every test in one walk when no runs are named, because the host's per-test read locates
        /// its test by walking the whole test tree and reading twelve tests one at a time is
        /// quadratic in the number of tests.
        /// </summary>
        /// <param name="runIds">The host ids of the tests to read, or null for every test.</param>
        /// <param name="onProgress">Called with how many results have been read; false stops it.</param>
        public CheckRead Read(IReadOnlyList<string>? runIds, Func<int, bool>? onProgress)
        {
            var runs = new List<CheckRun>();
            var findings = new List<Finding>();
            var notes = new List<string>();

            if (!_source.IsAvailable)
            {
                notes.Add(Unavailable ?? "This Navisworks edition has no clash engine.");

                return new CheckRead(runs, findings, _ => false, notes);
            }

            if (runIds == null)
            {
                foreach (var read in _source.ReadEverything(onProgress))
                {
                    _shapes[read.Test.Id] = read.Shape;

                    runs.Add(Run(read.Test, read.Shape));
                    findings.AddRange(Found(read.Test, read.Results));
                }
            }
            else
            {
                var seen = 0;

                foreach (var id in runIds)
                {
                    var held = _source.Tests()
                        .Where(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (held.Count == 0)
                    {
                        notes.Add("That test is no longer in the document: " + id);
                        continue;
                    }

                    var test = held[0];
                    var results = _source.Results(test.Id);

                    if (onProgress != null && !onProgress(seen += results.Count)) break;

                    runs.Add(Run(test, Shape(test.Id, reread: true)));
                    findings.AddRange(Found(test, results));
                }
            }

            return new CheckRead(runs, findings, Scope(runs.Select(run => run.Id).ToList()), notes);
        }

        /// <summary>
        /// Which held findings a read of these tests speaks for.
        ///
        /// The run fact where the finding carries one, and the host test fact where it does not.
        /// The second half is for findings written before run membership was a fact: without it a
        /// clash the host has stopped reporting would never fall inside any read's scope again, so
        /// it would never be marked absent and would sit on the board as present for good.
        /// </summary>
        /// <param name="runIds">The host ids of the tests that were read.</param>
        private static Func<Finding, bool> Scope(IReadOnlyList<string> runIds)
        {
            var byRun = CheckFacts.ScopeOf(runIds);
            var byTest = ClashFindings.Scope(runIds);

            return finding => CheckFacts.RunsOf(finding).Count > 0
                ? byRun(finding)
                : byTest(finding);
        }

        /// <summary>One test's results, as findings that name the run they came from.</summary>
        /// <param name="test">The test.</param>
        /// <param name="results">Its results.</param>
        private IReadOnlyList<Finding> Found(ClashTestInfo test,
                                             IReadOnlyList<ClashResultInfo> results)
        {
            var found = ClashFindings.ForTest(test.Id, test.Name, results, _cell, _metresPerUnit);

            var byId = new Dictionary<string, ClashResultInfo>(StringComparer.Ordinal);

            foreach (var result in results)
            {
                byId[result.Id] = result;
                _testOfResult[result.Id] = test.Id;
            }

            foreach (var finding in found)
            {
                CheckFacts.SetRuns(finding, new[] { test.Id });

                // The host reports how many remarks a result carries and what the last one says
                // without handing over the trail itself, which costs a call per result. The board
                // shows both, so they ride as facts rather than being re-read per row.
                if (finding.Facts.TryGetValue(ClashFindings.HostId, out var id)
                    && id != null && byId.TryGetValue(id, out var info))
                {
                    var count = Math.Max(info.CommentCount, info.Comments.Count);

                    var last = info.LastComment
                               ?? (info.Comments.Count > 0
                                       ? info.Comments[info.Comments.Count - 1].Body
                                       : null);

                    // Both written every read, including empty. Facts merge additively and none
                    // is ever removed, so a fact written only when there was something to say
                    // would leave the count and the text of remarks somebody has since deleted
                    // on the finding for the life of the project.
                    finding.Facts[HostComments] = count.ToString(CultureInfo.InvariantCulture);
                    finding.Facts[HostLastComment] = last ?? string.Empty;
                }
            }

            return found;
        }

        /// <summary>How many remarks the host holds on a result, as a fact.</summary>
        public const string HostComments = "Host comments";

        /// <summary>The newest remark the host holds on a result, as a fact.</summary>
        public const string HostLastComment = "Host last comment";

        /// <summary>How many remarks the host holds on this result.</summary>
        /// <param name="finding">The finding.</param>
        public static int CommentsOn(Finding? finding) =>
            finding != null
            && finding.Facts.TryGetValue(HostComments, out var written)
            && int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : 0;

        /// <summary>
        /// Say what the host holds on this result now, after an edit the host has taken.
        ///
        /// The two facts are written by the read and re-read by nothing until the run is read
        /// again, so a board that amends or deletes a remark has to restate them itself. Both are
        /// written whatever the values are: an empty last remark and a count of nought are the
        /// answer after the only remark was deleted, and a fact left off would leave the deleted
        /// wording on the finding.
        /// </summary>
        /// <param name="finding">The finding.</param>
        /// <param name="count">How many remarks the host holds now.</param>
        /// <param name="last">The newest remark the host holds now, or null for none.</param>
        public static void SetHostComments(Finding? finding, int count, string? last)
        {
            if (finding == null) return;

            finding.Facts[HostComments] = Math.Max(0, count)
                                              .ToString(CultureInfo.InvariantCulture);
            finding.Facts[HostLastComment] = last ?? string.Empty;
        }

        /// <summary>The newest remark the host holds on this result, or null.</summary>
        /// <param name="finding">The finding.</param>
        public static string? LastCommentOn(Finding? finding) =>
            finding != null && finding.Facts.TryGetValue(HostLastComment, out var said)
            && !string.IsNullOrEmpty(said)
                ? said
                : null;

        /// <summary>
        /// How deep this clash is, in metres, or null when the host reported no distance.
        ///
        /// Negative for a penetration, which is the sign the host uses. No conversion: the
        /// producer writes metres on both legs, so what used to be a scale by the document's
        /// units here — with nothing on the finding saying whether it had already been applied —
        /// is now nothing at all.
        /// </summary>
        /// <param name="finding">The finding.</param>
        public static double? DistanceMetres(Finding? finding) =>
            ClashFindings.DistanceMetres(finding);

        /// <summary>The host's id for the test a finding came from, or null.</summary>
        /// <param name="finding">The finding.</param>
        public static string? TestOf(Finding? finding) =>
            finding != null && finding.Facts.TryGetValue(ClashFindings.HostTest, out var id)
                ? id
                : null;

        /// <summary>The host's id for the result a finding is of, or null.</summary>
        /// <param name="finding">The finding.</param>
        public static string? ResultOf(Finding? finding) =>
            finding != null && finding.Facts.TryGetValue(ClashFindings.HostId, out var id)
                ? id
                : null;

        // ---- The run's own wording -----------------------------------------------------------

        /// <summary>One host clash test, worded for the rail.</summary>
        /// <param name="test">The test.</param>
        /// <param name="shape">Its two sides and tolerance, or null when the host would not say.</param>
        private CheckRun Run(ClashTestInfo test, ClashTestSides? shape)
        {
            _shapes[test.Id] = shape;

            var trades = Trades(shape);

            return new CheckRun(test.Id, test.Name, Kinded(shape), Between(trades),
                                test.Folder, Threshold(shape), trades,
                                test.ResultCount, test.LastRunTicks, isOurs: false);
        }

        /// <summary>
        /// The two disciplines a test compares, as the host records them.
        ///
        /// Each side is either a saved selection set — which on a real project carries the
        /// discipline, the sets being named Mechanical, Structure, Fire — or, where the side is
        /// whole models rather than a set, the model's own name. Nothing is derived: a test built
        /// out of an ad-hoc selection records neither, and it offers no discipline rather than one
        /// guessed out of its name.
        /// </summary>
        /// <param name="shape">The test's two sides, or null.</param>
        private static IReadOnlyList<string> Trades(ClashTestSides? shape)
        {
            var found = new List<string>();

            void Add(string? name)
            {
                var trimmed = (name ?? string.Empty).Trim();

                if (trimmed.Length > 0
                    && !found.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    found.Add(trimmed);
            }

            if (shape != null)
            {
                Add(shape.SetA ?? shape.ScopesA.FirstOrDefault());
                Add(shape.SetB ?? shape.ScopesB.FirstOrDefault());
            }

            return found;
        }

        /// <summary>What a test is between, as one phrase.</summary>
        /// <param name="trades">The disciplines its two sides record.</param>
        private static string Between(IReadOnlyList<string> trades) =>
            trades.Count >= 2 ? trades[0] + " v " + trades[1]
            : trades.Count == 1 ? trades[0] + " against a selection this test does not name"
            : "two selections this test does not name";

        /// <summary>What kind of test this is, in the words the matrix legend uses.</summary>
        /// <param name="shape">The test's shape, or null.</param>
        private static string Kinded(ClashTestSides? shape) =>
            MatrixNaming.Worded(MatrixDiff.Kinded(shape?.Kind));

        /// <summary>This test's tolerance in the units it was set in, or null when it has none.</summary>
        /// <param name="shape">The test's shape, or null.</param>
        private static string? Threshold(ClashTestSides? shape)
        {
            var metres = shape?.ToleranceMetres;

            if (!metres.HasValue) return null;

            var mm = Math.Abs(metres.Value) * 1000;

            return mm >= 1000
                ? (mm / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " m"
                : mm.ToString("0.##", CultureInfo.InvariantCulture) + " mm";
        }

        /// <summary>The test's shape, from the last read or from the host.</summary>
        /// <param name="runId">The host's id for the test.</param>
        /// <param name="reread">True to ask the host even when one is held.</param>
        private ClashTestSides? Shape(string? runId, bool reread = false)
        {
            if (runId == null) return null;

            if (!reread && _shapes.TryGetValue(runId, out var held)) return held;

            var shape = _source.Sides(runId);

            _shapes[runId] = shape;

            return shape;
        }

        /// <summary>The findings of one test, by the host id of the result they are of.</summary>
        /// <param name="findings">The findings.</param>
        private static IEnumerable<IGrouping<string, Finding>> ByTest(
            IReadOnlyList<Finding>? findings) =>
            (findings ?? Array.Empty<Finding>())
                .Where(finding => TestOf(finding) != null && ResultOf(finding) != null)
                .GroupBy(finding => TestOf(finding)!, StringComparer.OrdinalIgnoreCase);

        /// <summary>The host ids of these findings' results, in order.</summary>
        /// <param name="findings">The findings.</param>
        private static IReadOnlyList<string> Ids(IEnumerable<Finding> findings) =>
            findings.Select(ResultOf).Where(id => id != null).Select(id => id!).ToList();

        // ---- The capabilities ------------------------------------------------------------------

        /// <summary>
        /// The host echo.
        ///
        /// Non-null even on a seat with no clash engine and on a document that refuses every
        /// write. The two are different from having nowhere to echo to at all: a refusal is
        /// reported and marks the field pending on the finding, which is what the board says and
        /// what the sync later offers again. A null echo would record the edit and say nothing.
        /// </summary>
        private sealed class Echo : ICheckHostEcho
        {
            private readonly ClashCheck _check;

            internal Echo(ClashCheck check) => _check = check;

            public bool CanEdit => _check._source.IsAvailable && _check._source.CanEdit;

            public string? LastProblem => _check._source.LastProblem;

            public bool SetStatus(IReadOnlyList<Finding> findings, FindingStatus status)
            {
                var word = FindingStatusLattice.ToName(status);

                return Each(findings, (test, ids) => _check._source.SetStatus(test, ids, word));
            }

            public bool Assign(IReadOnlyList<Finding> findings, string who) =>
                Each(findings, (test, ids) => _check._source.Assign(test, ids, who ?? string.Empty));

            public bool Comment(IReadOnlyList<Finding> findings, string body, string author) =>
                Each(findings, (test, ids) => _check._source.Comment(test, ids, body, author));

            public bool SetGroup(IReadOnlyList<Finding> findings, string? groupName) =>
                Each(findings, (test, ids) =>
                    _check._source.Regroup(test, ids, groupName ?? string.Empty));

            /// <summary>
            /// Write the titles the register has just taken.
            ///
            /// One host call per test where the titles are a prefix numbered from one in the order
            /// given, which is what a renumber produces and what the host's own verb does. Anything
            /// else is written a result at a time, which is the only exact way to say what each one
            /// is called — at the cost of the host locating its test again per result.
            /// </summary>
            public bool SetTitle(IReadOnlyList<Finding> findings, IReadOnlyList<string> titles)
            {
                if (findings == null || titles == null || findings.Count != titles.Count)
                    return false;

                var named = new Dictionary<string, string>(StringComparer.Ordinal);

                for (var i = 0; i < findings.Count; i++)
                {
                    var id = ResultOf(findings[i]);

                    if (id != null) named[id] = titles[i];
                }

                return Each(findings, (test, ids) =>
                {
                    var wanted = ids.Select(id => named.TryGetValue(id, out var call)
                                                      ? call : string.Empty).ToList();

                    var prefix = Numbered(wanted);

                    if (prefix != null) return _check._source.RenamePicked(test, ids, prefix);

                    var all = true;

                    for (var i = 0; i < ids.Count; i++)
                        all &= _check._source.RenameResult(test, ids[i], wanted[i]);

                    return all;
                });
            }

            /// <summary>The stem these titles number from, or null when they are not a numbering.</summary>
            /// <param name="titles">The titles, in the order the results were given in.</param>
            private static string? Numbered(IReadOnlyList<string> titles)
            {
                if (titles.Count == 0) return null;

                var first = titles[0];

                if (!first.EndsWith("1", StringComparison.Ordinal)) return null;

                var stem = first.Substring(0, first.Length - 1);

                if (stem.Length == 0) return null;

                for (var i = 0; i < titles.Count; i++)
                    if (!string.Equals(titles[i],
                                       stem + (i + 1).ToString(CultureInfo.InvariantCulture),
                                       StringComparison.Ordinal))
                        return null;

                return stem;
            }

            public IReadOnlyList<FindingComment> CommentsOn(Finding finding)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                if (test == null || result == null) return Array.Empty<FindingComment>();

                return _check._source.CommentsOn(test, result)
                    .Select(said => new FindingComment(said.Author, said.WhenTicks, said.Body)
                    {
                        InHost = true,
                        HostId = said.HostId,
                    })
                    .ToList();
            }

            public bool EditComment(Finding finding, long commentId, string body)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                return test != null && result != null
                       && _check._source.EditComment(test, result, commentId, body);
            }

            public bool DeleteComment(Finding finding, long commentId)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                return test != null && result != null
                       && _check._source.DeleteComment(test, result, commentId);
            }

            /// <summary>One write per test, true only when every one of them landed.</summary>
            /// <param name="findings">The findings.</param>
            /// <param name="write">What to ask the host, per test.</param>
            private static bool Each(IReadOnlyList<Finding> findings,
                                     Func<string, IReadOnlyList<string>, bool> write)
            {
                var any = false;
                var all = true;

                foreach (var byTest in ByTest(findings))
                {
                    any = true;
                    all &= write(byTest.Key, Ids(byTest));
                }

                return any && all;
            }
        }

        /// <summary>Nesting results into the host's own clash groups.</summary>
        private sealed class Groups : ICheckGrouping
        {
            private readonly ClashCheck _check;

            internal Groups(ClashCheck check) => _check = check;

            public bool CanPin => true;

            /// <summary>
            /// Nest results into the host's groups.
            ///
            /// The test is looked up from the results themselves: a trail names the results at the
            /// end of it and not the test they are in, and the host groups within one test. The
            /// first result the last read placed decides, so a caller that hands over results from
            /// two tests groups them in the first one's — which is what the host would do with
            /// them anyway.
            /// </summary>
            /// <param name="paths">Each trail of group names and the results at the end of it.</param>
            public ClashNesting Regroup(IReadOnlyList<ClashGroupPath> paths)
            {
                var wanted = paths ?? Array.Empty<ClashGroupPath>();

                var test = wanted
                    .SelectMany(path => path.ResultIds)
                    .Select(id => _check._testOfResult.TryGetValue(id, out var found) ? found : null)
                    .FirstOrDefault(found => found != null);

                return test == null
                    ? new ClashNesting(false, false, 0, "Those results are not in a test this "
                                                        + "board has read.")
                    : _check._source.Regroup(test, wanted);
            }
        }

        /// <summary>Running, deleting and compacting the host's tests.</summary>
        private sealed class Rerun : ICheckRunner
        {
            private readonly ClashCheck _check;

            internal Rerun(ClashCheck check) => _check = check;

            public bool CanRun => _check._source.IsAvailable && _check._source.CanEdit;

            public string? CannotRun =>
                !_check._source.IsAvailable ? _check.Unavailable
                : _check._source.CanEdit ? null
                : "This document will not take a clash write.";

            public bool Run(string runId, Func<int, bool>? onProgress)
            {
                _ = onProgress;

                return _check._source.Run(runId);
            }

            public bool Delete(IReadOnlyList<string> runIds) => _check._source.DeleteTests(runIds);

            public bool Compact(IReadOnlyList<string> runIds) => _check._source.CompactTests(runIds);
        }

        /// <summary>Renaming and refiling the host's tests.</summary>
        private sealed class Names : ICheckNaming
        {
            private readonly ClashCheck _check;

            internal Names(ClashCheck check) => _check = check;

            public bool HasBook => true;

            public bool Rename(string runId, string name) =>
                _check._source.RenameTest(runId, name);

            public bool Refile(string runId, string? folder) =>
                _check._source.MoveTests(new[] { runId }, folder);
        }

        /// <summary>Element properties, one finding at a time.</summary>
        private sealed class Props : ICheckProperties
        {
            private readonly ClashCheck _check;

            internal Props(ClashCheck check) => _check = check;

            public IReadOnlyList<PropertyValue> ValuesFor(Finding finding, int elementIndex,
                                                          string category, string name)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                if (test == null || result == null) return Array.Empty<PropertyValue>();

                var side = elementIndex == 0 ? ClashElementSide.First
                         : elementIndex == 1 ? ClashElementSide.Second
                         : ClashElementSide.Both;

                var wanted = new[] { new ClashPropertyWanted("value", category, name, side) };

                foreach (var answer in _check._source.ValuesFor(test, wanted, null))
                {
                    if (!string.Equals(answer.ResultId, result, StringComparison.Ordinal)) continue;

                    return answer.Values.TryGetValue("value", out var value)
                        ? new[] { new PropertyValue(category, name, value) }
                        : Array.Empty<PropertyValue>();
                }

                return Array.Empty<PropertyValue>();
            }
        }

        /// <summary>Selecting, framing and picturing results through the host's own handles.</summary>
        private sealed class Sight : ICheckGeometry
        {
            private readonly ClashCheck _check;

            internal Sight(ClashCheck check) => _check = check;

            /// <summary>
            /// Select what the clash engine is already holding for these results.
            ///
            /// A copy of handles the engine has, never a search: the engine holds both elements of
            /// every result it reported, so nothing here resolves an element key and nothing walks
            /// the federation.
            /// </summary>
            public int? Select(IReadOnlyList<Finding> findings, bool add)
            {
                if (!_check._source.IsAvailable) return null;

                var selected = 0;
                var replacing = !add;
                var any = false;

                foreach (var byTest in ByTest(findings))
                {
                    var count = _check._source.SelectResults(byTest.Key, Ids(byTest), !replacing);

                    if (count < 0) continue;

                    any = true;
                    selected += count;
                    replacing = false;
                }

                return any ? selected : (int?)null;
            }

            /// <summary>
            /// Nothing to locate: a host-read board reaches its elements through the engine.
            ///
            /// The keys are on the findings, but resolving one that is not an instance GUID
            /// indexes every geometry leaf in the federation, and <see cref="Select"/> already has
            /// the handles. A caller that gets an empty list here selects through the engine.
            /// </summary>
            public IReadOnlyList<ElementLocator> Locate(IReadOnlyList<Finding> findings)
            {
                _ = findings;

                return Array.Empty<ElementLocator>();
            }

            public bool ApplyView(Finding finding)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                return test != null && result != null
                       && _check._source.ApplyResultView(test, result);
            }

            public byte[]? Snapshot(Finding finding, int width, int height)
            {
                var test = TestOf(finding);
                var result = ResultOf(finding);

                return test == null || result == null
                    ? null
                    : _check._source.ResultSnapshot(test, result, width, height);
            }
        }
    }
}
