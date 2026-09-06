using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;

namespace CamelWorks.Core.Testing
{
    /// <summary>
    /// A clash engine in memory, for everything downstream of the clash read.
    ///
    /// <see cref="IClashSource"/> had no fake, so the board, the matrix, the exchange and the
    /// naming were verified only by hand on a licensed seat: the one seam in the product where a
    /// regression could not be caught before it shipped. This is the same shape as
    /// <see cref="FakeDocument"/> — a small mutable model with a log of what was asked of it, and
    /// a switch for each way the host refuses.
    ///
    /// It is a fake and not a mock: the writes really change what the reads return, so a test can
    /// set a status and then read it back the way the board does.
    /// </summary>
    public sealed class FakeClashSource : IClashSource
    {
        private readonly List<FakeTest> _tests = new List<FakeTest>();
        private readonly List<string> _log = new List<string>();

        /// <summary>One test, and what it found.</summary>
        private sealed class FakeTest
        {
            internal FakeTest(string id, string name, string? folder)
            {
                Id = id; Name = name; Folder = folder;
            }

            internal string Id { get; }
            internal string Name { get; set; }
            internal string? Folder { get; set; }
            internal long LastRunTicks { get; set; }
            internal double ToleranceMetres { get; set; } = 0.025;
            internal MatrixTest Kind { get; set; } = MatrixTest.Hard;
            internal string? SetA { get; set; }
            internal string? SetB { get; set; }
            internal List<FakeResult> Results { get; } = new List<FakeResult>();
        }

        /// <summary>One result, as the host holds it.</summary>
        private sealed class FakeResult
        {
            internal FakeResult(string id, ElementKey a, ElementKey b, double x, double y, double z)
            {
                Id = id; A = a; B = b; X = x; Y = y; Z = z;
            }

            internal string Id { get; }
            internal string Name { get; set; } = string.Empty;
            internal ElementKey A { get; }
            internal ElementKey B { get; }
            internal double X { get; }
            internal double Y { get; }
            internal double Z { get; }
            internal string Status { get; set; } = "New";
            internal string? AssignedTo { get; set; }
            internal string? Group { get; set; }
            internal List<ClashComment> Comments { get; } = new List<ClashComment>();
        }

        // ---- What the seat is ------------------------------------------------------------

        /// <inheritdoc />
        public bool IsAvailable { get; set; } = true;

        /// <inheritdoc />
        public bool CanEdit { get; set; } = true;

        /// <inheritdoc />
        public string? LastProblem { get; private set; }

        /// <summary>
        /// What the host refuses. Every write checks it first, so a test can ask what the board
        /// does on a document that will not take an edit.
        /// </summary>
        public string? Refuses { get; set; }

        /// <summary>Everything that was asked of it, in order, for a test to assert on.</summary>
        public IReadOnlyList<string> Log => _log;

        // ---- Building one ----------------------------------------------------------------

        /// <summary>Put a test in the document.</summary>
        /// <param name="name">What it is called.</param>
        /// <param name="folder">Which clash folder, or null for the root.</param>
        /// <param name="id">Its host id, or null to derive one from the name.</param>
        public FakeClashSource With(string name, string? folder = null, string? id = null)
        {
            _tests.Add(new FakeTest(id ?? "test-" + name.ToLowerInvariant().Replace(' ', '-'),
                                    name, folder));

            return this;
        }

        /// <summary>Put a result in the last test added.</summary>
        /// <param name="a">The first element.</param>
        /// <param name="b">The second element.</param>
        /// <param name="x">Where it is, X.</param>
        /// <param name="y">Where it is, Y.</param>
        /// <param name="z">Where it is, Z.</param>
        /// <param name="status">The host status.</param>
        public FakeClashSource Finding(ElementKey a, ElementKey b, double x = 0, double y = 0,
                                       double z = 0, string status = "New")
        {
            if (_tests.Count == 0) throw new InvalidOperationException("add a test first");

            var test = _tests[_tests.Count - 1];

            var result = new FakeResult(
                "r" + (test.Results.Count + 1).ToString(CultureInfo.InvariantCulture), a, b, x, y, z)
            {
                Status = status,
            };

            result.Name = "Clash" + (test.Results.Count + 1).ToString(CultureInfo.InvariantCulture);

            test.Results.Add(result);

            return this;
        }

        // ---- Reading ---------------------------------------------------------------------

        /// <inheritdoc />
        public IReadOnlyList<ClashTestInfo> Tests()
        {
            _log.Add("Tests");

            return IsAvailable
                ? _tests.Select(Described).ToList()
                : (IReadOnlyList<ClashTestInfo>)Array.Empty<ClashTestInfo>();
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashResultInfo> Results(string testId)
        {
            _log.Add("Results " + testId);

            var test = IsAvailable ? Found(testId) : null;

            if (test == null) return Array.Empty<ClashResultInfo>();

            return test.Results.Select(one => new ClashResultInfo(
                one.Id, one.A, one.B, one.X, one.Y, one.Z, one.Status, one.AssignedTo)
            {
                Name = one.Name,
                GroupName = one.Group,
                Comments = one.Comments.ToList(),
            }).ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashTestRead> ReadEverything(Func<int, bool>? onProgress)
        {
            var read = new List<ClashTestRead>();
            var seen = 0;

            if (!IsAvailable) return read;

            foreach (var test in _tests)
            {
                if (onProgress != null && !onProgress(++seen)) break;

                read.Add(new ClashTestRead(Described(test), Sides(test.Id), Results(test.Id)));
            }

            return read;
        }

        /// <inheritdoc />
        public ClashTestSides? Sides(string testId)
        {
            var test = IsAvailable ? Found(testId) : null;

            return test == null
                ? null
                : new ClashTestSides(test.SetA, test.SetB, Array.Empty<string>(),
                                     Array.Empty<string>(), test.Kind.ToString(),
                                     test.ToleranceMetres);
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashComment> CommentsOn(string testId, string resultId) =>
            (IsAvailable ? Result(testId, resultId)?.Comments.ToList() : null)
            ?? (IReadOnlyList<ClashComment>)Array.Empty<ClashComment>();

        /// <inheritdoc />
        public IReadOnlyList<ClashResultComments> CommentsFor(string testId,
                                                              Func<int, bool>? onProgress)
        {
            var test = IsAvailable ? Found(testId) : null;

            if (test == null) return Array.Empty<ClashResultComments>();

            var read = new List<ClashResultComments>();
            var seen = 0;

            foreach (var one in test.Results)
            {
                if (onProgress != null && !onProgress(++seen)) break;
                if (one.Comments.Count == 0) continue;

                var held = new ClashResultComments(one.Id);

                held.Trail.AddRange(one.Comments);
                read.Add(held);
            }

            return read;
        }

        /// <summary>
        /// Nothing. A fake federation has no properties, no IFC and no snapshots, and a caller
        /// that needs one of those is testing the adapter rather than what sits above it.
        /// </summary>
        /// <param name="testId">Which test.</param>
        /// <param name="wanted">The properties asked for.</param>
        /// <param name="onProgress">Progress, ignored.</param>
        public IReadOnlyList<ClashResultValues> ValuesFor(string testId,
                                                          IReadOnlyList<ClashPropertyWanted> wanted,
                                                          Func<int, bool>? onProgress) =>
            Array.Empty<ClashResultValues>();

        /// <inheritdoc cref="ValuesFor" />
        /// <param name="testId">Which test.</param>
        /// <param name="onProgress">Progress, ignored.</param>
        public IReadOnlyList<ClashResultIfc> IfcFor(string testId, Func<int, bool>? onProgress) =>
            Array.Empty<ClashResultIfc>();

        /// <inheritdoc cref="ValuesFor" />
        /// <param name="settleAfter">Ignored.</param>
        /// <param name="onProgress">Progress, ignored.</param>
        public IReadOnlyList<ClashPropertyName> PropertyVocabulary(int settleAfter,
                                                                   Func<int, bool>? onProgress) =>
            Array.Empty<ClashPropertyName>();

        /// <inheritdoc cref="ValuesFor" />
        /// <param name="testId">Which test.</param>
        /// <param name="resultId">Which result.</param>
        /// <param name="width">Ignored.</param>
        /// <param name="height">Ignored.</param>
        public byte[]? ResultSnapshot(string testId, string resultId, int width, int height) => null;

        // ---- Writing ---------------------------------------------------------------------

        /// <inheritdoc />
        public ClashTestInfo? Create(ClashTestSpec spec)
        {
            if (!Writable("Create " + spec?.Name)) return null;

            if (spec == null)
            {
                LastProblem = "No test was described.";
                return null;
            }

            // The host resolves both sides against the document and returns null when either
            // names nothing. There is no document here, so the fake refuses the case it can see:
            // a side that names nothing at all.
            if (spec.A == null || spec.B == null)
            {
                LastProblem = "A clash test is between two things, and one side is missing.";
                return null;
            }

            var made = new FakeTest("test-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                                    spec.Name, spec.Folder)
            {
                ToleranceMetres = spec.Tolerance,
                Kind = spec.Kind,
                SetA = spec.A?.Name,
                SetB = spec.B?.Name,
            };

            _tests.Add(made);

            return Described(made);
        }

        /// <inheritdoc />
        public bool Run(string testId)
        {
            if (!Writable("Run " + testId)) return false;

            var test = Found(testId);

            if (test == null) return Missing();

            test.LastRunTicks = DateTime.UtcNow.Ticks;

            return true;
        }

        /// <inheritdoc />
        public bool SetStatus(string testId, IReadOnlyList<string> resultIds, string status)
        {
            // The host parses the word against its own enum and refuses anything else, so a
            // status the product invents fails on a seat and passed against the fake.
            var said = status ?? string.Empty;

            if (!Statuses.Contains(said))
            {
                _log.Add("SetStatus " + said);
                LastProblem = "\"" + said + "\" is not a status the host knows.";
                return false;
            }

            return Each(testId, resultIds, "SetStatus " + said, one => one.Status = said);
        }

        /// <summary>The only five words the host's result status takes.</summary>
        private static readonly HashSet<string> Statuses = new HashSet<string>(
            new[] { "New", "Active", "Reviewed", "Approved", "Resolved" },
            StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc />
        public bool Assign(string testId, IReadOnlyList<string> resultIds, string assignee) =>
            Each(testId, resultIds, "Assign " + assignee,
                 one => one.AssignedTo = string.IsNullOrWhiteSpace(assignee) ? null : assignee);

        /// <inheritdoc />
        public bool Comment(string testId, IReadOnlyList<string> resultIds, string body,
                            string author)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                _log.Add("Comment");
                LastProblem = "There is no comment to add.";
                return false;
            }

            // The same author saying the same thing is the same comment. Skipped here as the host
            // skips it, which is what lets a markup sync run twice without doubling every trail.
            return Each(testId, resultIds, "Comment", one =>
            {
                if (one.Comments.Any(had => string.Equals(had.Body, body, StringComparison.Ordinal)
                                            && string.Equals(had.Author, author ?? string.Empty,
                                                             StringComparison.OrdinalIgnoreCase)))
                    return;

                one.Comments.Add(new ClashComment(author, DateTime.UtcNow.Ticks, body));
            });
        }

        /// <inheritdoc />
        public bool Regroup(string testId, IReadOnlyList<string> resultIds, string groupName) =>
            Each(testId, resultIds, "Regroup " + groupName, one => one.Group = groupName);

        /// <inheritdoc />
        public ClashNesting Regroup(string testId, IReadOnlyList<ClashGroupPath> paths)
        {
            if (!Writable("RegroupNested " + testId))
                return new ClashNesting(false, false, 0, LastProblem);

            foreach (var path in paths ?? Array.Empty<ClashGroupPath>())
                Each(testId, path.ResultIds, null,
                     one => one.Group = ClashTrail.Join(path.Trail));

            var groups = Found(testId)?.Results
                .Select(one => one.Group).Where(one => one != null)
                .Distinct(StringComparer.Ordinal).Count() ?? 0;

            // Flattened, like the hosts that do not nest. A caller that wants the other answer
            // sets Nests.
            return new ClashNesting(true, Nests, groups,
                                    Nests ? null : "This document does not nest clash groups.");
        }

        /// <summary>Whether nested groups survive as a tree, as they do on some seats.</summary>
        public bool Nests { get; set; }

        /// <inheritdoc />
        public bool RenameResults(string testId, string prefix)
        {
            var test = Found(testId);

            if (test == null) return Missing();

            return Each(testId, test.Results.Select(one => one.Id).ToList(),
                        "RenameResults " + prefix, one => { }) && Renamed(test, prefix);
        }

        /// <inheritdoc />
        public bool RenamePicked(string testId, IReadOnlyList<string> resultIds, string prefix)
        {
            var test = Found(testId);

            if (test == null) return Missing();
            if (!Writable("RenamePicked " + prefix)) return false;

            var at = 1;
            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);

            foreach (var one in test.Results)
                if (wanted.Contains(one.Id))
                    one.Name = prefix + (at++).ToString(CultureInfo.InvariantCulture);

            if (at == 1)
            {
                LastProblem = "None of those results are in the test any more.";
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public bool RenameResult(string testId, string resultId, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _log.Add("RenameResult " + name);
                LastProblem = "A result needs a name.";
                return false;
            }

            return Each(testId, new[] { resultId }, "RenameResult " + name,
                        one => one.Name = name);
        }

        /// <inheritdoc />
        public bool Retune(string testId, double toleranceMetres, MatrixTest kind)
        {
            if (!Writable("Retune " + testId)) return false;

            var test = Found(testId);

            if (test == null) return Missing();

            test.ToleranceMetres = toleranceMetres;
            test.Kind = kind;

            return true;
        }

        /// <inheritdoc />
        public bool RenameTest(string testId, string name)
        {
            if (!Writable("RenameTest " + name)) return false;

            if (string.IsNullOrWhiteSpace(name))
            {
                LastProblem = "A test needs a name.";
                return false;
            }

            var test = Found(testId);

            if (test == null) return Missing();

            test.Name = name;

            return true;
        }

        /// <inheritdoc />
        public bool DeleteTests(IReadOnlyList<string> testIds)
        {
            if (!Writable("DeleteTests")) return false;

            foreach (var id in testIds ?? Array.Empty<string>())
            {
                var test = Found(id);

                if (test != null) _tests.Remove(test);
            }

            return true;
        }

        /// <inheritdoc />
        public bool MoveTests(IReadOnlyList<string> testIds, string? folder)
        {
            if (!Writable("MoveTests " + folder)) return false;

            foreach (var id in testIds ?? Array.Empty<string>())
            {
                var test = Found(id);

                if (test != null) test.Folder = folder;
            }

            return true;
        }

        /// <inheritdoc />
        public bool CompactTests(IReadOnlyList<string> testIds)
        {
            if (!Writable("CompactTests")) return false;

            foreach (var id in testIds ?? Array.Empty<string>())
                Found(id)?.Results.RemoveAll(one =>
                    string.Equals(one.Status, "Resolved", StringComparison.OrdinalIgnoreCase));

            return true;
        }

        /// <inheritdoc />
        public bool DeleteComment(string testId, string resultId, long commentId)
        {
            if (!Writable("DeleteComment")) return false;

            var result = Result(testId, resultId);

            if (result == null) return Missing();

            result.Comments.RemoveAll(one => one.WhenTicks == commentId);

            return true;
        }

        /// <inheritdoc />
        public bool EditComment(string testId, string resultId, long commentId, string body)
        {
            if (!Writable("EditComment")) return false;

            var result = Result(testId, resultId);

            if (result == null) return Missing();

            for (var i = 0; i < result.Comments.Count; i++)
                if (result.Comments[i].WhenTicks == commentId)
                    result.Comments[i] = new ClashComment(result.Comments[i].Author,
                                                          commentId, body);

            return true;
        }

        // ---- The view ---------------------------------------------------------------------

        /// <summary>What was last focused, so a test can assert the board asked for the right one.</summary>
        public string? Focused { get; private set; }

        /// <inheritdoc />
        public bool ApplyResultView(string testId, string resultId)
        {
            _log.Add("ApplyResultView " + testId + "/" + resultId);

            if (!IsAvailable)
            {
                LastProblem = "This document has no clash engine.";
                return false;
            }

            if (Result(testId, resultId) == null) return Missing();

            Focused = testId + "/" + resultId;

            return true;
        }

        /// <summary>What was last selected, in the order it was asked for.</summary>
        public IReadOnlyList<string> Selected { get; private set; } = Array.Empty<string>();

        /// <inheritdoc />
        public int SelectResults(string testId, IReadOnlyList<string> resultIds, bool add)
        {
            _log.Add("SelectResults " + testId);

            var test = IsAvailable ? Found(testId) : null;

            if (test == null) { Missing(); return -1; }

            if (resultIds == null || resultIds.Count == 0) return 0;

            var wanted = resultIds
                .Where(id => test.Results.Any(one => string.Equals(one.Id, id,
                                                                   StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Selected = add ? Selected.Concat(wanted).Distinct(StringComparer.Ordinal).ToList()
                           : wanted;

            // Two elements per result named in THIS call, de-duplicated the way the adapter does.
            // Counting the whole selection reported the elements already selected as though this
            // call had added them.
            return wanted.Count * 2;
        }

        // ---- Plumbing ---------------------------------------------------------------------

        private static ClashTestInfo Described(FakeTest test) =>
            new ClashTestInfo(test.Id, test.Name, test.Folder, test.Results.Count,
                              test.LastRunTicks);

        private FakeTest? Found(string? testId) =>
            _tests.FirstOrDefault(one => string.Equals(one.Id, testId,
                                                       StringComparison.OrdinalIgnoreCase));

        private FakeResult? Result(string? testId, string? resultId) =>
            Found(testId)?.Results.FirstOrDefault(
                one => string.Equals(one.Id, resultId, StringComparison.OrdinalIgnoreCase));

        private bool Missing()
        {
            LastProblem = "That test is no longer in the document. Re-read it.";

            return false;
        }

        /// <summary>Whether this write goes ahead, and the note in the log either way.</summary>
        /// <param name="said">What was asked, or null to leave the log alone.</param>
        private bool Writable(string? said)
        {
            if (said != null) _log.Add(said);

            LastProblem = null;

            if (!IsAvailable)
            {
                LastProblem = "This document has no clash engine.";
                return false;
            }

            if (!CanEdit || Refuses != null)
            {
                LastProblem = Refuses ?? "This document will not take clash edits.";
                return false;
            }

            return true;
        }

        /// <summary>Do the same thing to each named result.</summary>
        /// <param name="testId">Which test.</param>
        /// <param name="resultIds">Which results.</param>
        /// <param name="said">What was asked, for the log.</param>
        /// <param name="what">What to do to each.</param>
        private bool Each(string testId, IReadOnlyList<string>? resultIds, string? said,
                          Action<FakeResult> what)
        {
            if (!Writable(said)) return false;

            var test = Found(testId);

            if (test == null) return Missing();

            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);

            foreach (var one in test.Results)
                if (wanted.Contains(one.Id)) what(one);

            return true;
        }

        private static bool Renamed(FakeTest test, string prefix)
        {
            var at = 1;

            foreach (var one in test.Results)
                one.Name = prefix + (at++).ToString(CultureInfo.InvariantCulture);

            return true;
        }
    }
}
