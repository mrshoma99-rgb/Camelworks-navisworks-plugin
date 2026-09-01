using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelWorks.Core.Abstractions;
using CamelWorks.Core.Clash;
using CamelWorks.Core.Identity;

namespace CamelWorks.Nav.Clash
{
    /// <summary>
    /// Reads the host's clash tests and results.
    ///
    /// <b>This type lives in its own assembly on purpose, and nothing may move it.</b>
    /// <c>Autodesk.Navisworks.Clash.dll</c> ships with Navisworks Manage and not with Simulate. A
    /// type from a missing assembly costs nothing until the JIT reaches a method that mentions it,
    /// and then it is a FileNotFoundException in the middle of whatever the user was doing, naming
    /// a DLL they have never heard of. Behind an assembly boundary, a Simulate user simply never
    /// loads this — the clash tools are shown disabled, with a reason.
    ///
    /// <b>Main thread only.</b>
    /// </summary>
    public sealed class NavClashSource : IClashSource
    {
        private readonly NavDocument _document;

        /// <summary>Wrap a document.</summary>
        public NavClashSource(NavDocument document) =>
            _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <summary>
        /// Whether this document can supply clash data at all.
        ///
        /// False on Simulate, and false on Manage for a document that has never had the clash tool
        /// opened. Both are ordinary states, not errors: the caller disables the clash tools and
        /// says which one it is.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    return Clash() != null;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashTestInfo> Tests()
        {
            var tests = new List<ClashTestInfo>();

            var clash = Clash();
            if (clash == null) return tests;

            foreach (var found in AllTests(clash))
            {
                tests.Add(new ClashTestInfo(
                    found.Test.Guid.ToString(),
                    found.Test.DisplayName ?? "(unnamed test)",
                    found.Folder,

                    // Counted, not collected. This used to build a list of every ClashResult in
                    // the test purely to read its Length, and the read that follows immediately
                    // walks them all again anyway.
                    CountResults(found.Test.Children),
                    LastRunTicksOf(found.Test)));
            }

            return tests;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashResultInfo> Results(string testId)
        {
            var results = new List<ClashResultInfo>();

            var clash = Clash();
            if (clash == null || string.IsNullOrWhiteSpace(testId)) return results;

            var test = FindTest(clash, testId);
            if (test == null) return results;

            var found = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, found);

            foreach (var (result, group) in found)
            {
                var info = Described(result, group);
                if (info != null) results.Add(info);
            }

            return results;
        }

        /// <summary>
        /// Whether tests can be made and run here, as opposed to only read.
        ///
        /// Checked by asking the host's own guard rather than by inferring it. A clash collection
        /// can be present and read-only, and finding that out by having thirty-six creates throw is
        /// not a design.
        /// </summary>
        public bool CanEdit
        {
            get
            {
                try
                {
                    var clash = Clash();
                    return clash != null && !clash.TestsData.Value.IsReadOnly;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <inheritdoc />
        public string? LastProblem { get; private set; }

        /// <inheritdoc />
        public ClashTestInfo? Create(ClashTestSpec spec)
        {
            LastProblem = null;

            if (spec == null) { LastProblem = "No test was described."; return null; }

            var clash = Clash();

            if (clash == null)
            {
                LastProblem = "This document has no clash data to add a test to.";
                return null;
            }

            try
            {
                var test = new ClashTest
                {
                    CustomTestName = spec.Name,

                    // The kind the cell's legend chose. For a long time this said Hard for
                    // every spec, so a clearance matrix quietly made hard tests.
                    TestType = spec.Kind switch
                    {
                        Core.Clash.MatrixTest.HardConservative => ClashTestType.HardConservative,
                        Core.Clash.MatrixTest.Clearance => ClashTestType.Clearance,
                        Core.Clash.MatrixTest.Duplicate => ClashTestType.Duplicate,
                        _ => ClashTestType.Hard,
                    },
                    Tolerance = spec.Tolerance * NavUnits.UnitsPerMetre(_document.Document.Units),
                };

                if (!Fill(test.SelectionA, spec.A)) return null;
                if (!Fill(test.SelectionB, spec.B)) return null;

                var root = clash.TestsData.Value.TestsRoot;

                if (root == null)
                {
                    LastProblem = "The document's clash tests could not be reached.";
                    return null;
                }

                // AddCopy, not Add: the host takes a copy of the test and owns it from then on, so
                // the object built above is not the one in the document and must not be run.
                clash.TestsData.TestsAddCopy(root, test);

                var made = AllTests(clash)
                    .Select(t => t.Test)
                    .LastOrDefault(t => string.Equals(t.DisplayName, spec.Name, StringComparison.Ordinal));

                if (made == null)
                {
                    LastProblem = "The host accepted the test but it cannot be found again by name.";
                    return null;
                }

                return new ClashTestInfo(made.Guid.ToString(), made.DisplayName ?? spec.Name,
                                         spec.Folder, 0, LastRunTicksOf(made));
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the test: " + e.Message;
                return null;
            }
        }

        /// <inheritdoc />
        public bool Run(string testId)
        {
            LastProblem = null;

            var clash = Clash();

            if (clash == null || string.IsNullOrWhiteSpace(testId))
            {
                LastProblem = "There is no clash engine, or no test was named.";
                return false;
            }

            var test = AllTests(clash)
                .Select(t => t.Test)
                .FirstOrDefault(t => string.Equals(t.Guid.ToString(), testId, StringComparison.OrdinalIgnoreCase));

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            try
            {
                clash.TestsData.TestsRunTest(test);
                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host would not run \"" + (test.DisplayName ?? testId) + "\": " + e.Message;
                return false;
            }
        }

        // -----------------------------------------------------------------------------------
        // The manager's write-backs
        // -----------------------------------------------------------------------------------

        /// <inheritdoc />
        public bool SetStatus(string testId, IReadOnlyList<string> resultIds, string status)
        {
            if (!Enum.TryParse<ClashResultStatus>(status, true, out var parsed))
            {
                LastProblem = "\"" + status + "\" is not a status the host knows.";
                return false;
            }

            return OnResults(testId, resultIds, (clash, result) =>
#if NAV_STRING_ASSIGNEE
                clash.TestsData.TestsEditResultStatus(result, parsed));
#else
                clash.TestsData.TestsEditResultStatus(result, parsed, Me()));
#endif
        }

        /// <inheritdoc />
        public bool Assign(string testId, IReadOnlyList<string> resultIds, string assignee) =>
            OnResults(testId, resultIds, (clash, result) =>
                clash.TestsData.TestsEditResultAssignedTo(result, Party(assignee)));

        /// <inheritdoc />
        public bool Comment(string testId, IReadOnlyList<string> resultIds, string body, string author)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                LastProblem = "There is no comment to add.";
                return false;
            }

            return OnResults(testId, resultIds, (clash, result) =>
            {
                // The host edits comments by handing back the whole collection, so the new entry
                // is appended to a copy of what is there. Written as a native comment with its
                // author, so the host's own clash window shows it too.
                var comments = new CommentCollection();

                try
                {
                    foreach (Comment existing in result.Comments)
                    {
                        // The same author saying the same thing is the same comment. Skipping it
                        // here is what lets a markup sync run twice without doubling every
                        // conversation.
                        if (string.Equals(existing.Body, body, StringComparison.Ordinal)
                            && string.Equals(Named(existing.Author), author ?? string.Empty,
                                             StringComparison.OrdinalIgnoreCase))
                            return;

                        comments.Add(existing);
                    }
                }
                catch (Exception) { /* a result whose comments cannot be read still takes one */ }

                comments.Add(new Comment(body, CommentStatus.New, Writer(author)));
                clash.TestsData.TestsEditResultComments(result, comments);
            });
        }

        /// <inheritdoc />
        public bool Regroup(string testId, IReadOnlyList<string> resultIds, string groupName)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);
            var target = (groupName ?? string.Empty).Trim();

            try
            {
                // The host disposes a saved item's native handle the moment it leaves its
                // collection, so results cannot be lifted out and re-added — the whole test is
                // rebuilt on a copy from deep copies, then swapped in as one edit. Statuses,
                // comments and assignments ride along on the copies.
                var copy = test.CreateCopy() as ClashTest;

                if (copy == null)
                {
                    LastProblem = "The host would not copy the test for editing.";
                    return false;
                }

                var flat = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(copy.Children, null, flat);

                var rebuilt = new List<(ClashResult Result, string? Group)>();

                foreach (var (result, group) in flat)
                {
                    var clone = result.CreateCopy() as ClashResult;
                    if (clone == null) continue;

                    var into = wanted.Contains(result.Guid.ToString())
                        ? (target.Length == 0 ? null : target)
                        : group;

                    rebuilt.Add((clone, into));
                }

                copy.Children.Clear();

                var groups = new Dictionary<string, ClashResultGroup>(StringComparer.Ordinal);

                foreach (var (result, group) in rebuilt)
                {
                    if (string.IsNullOrEmpty(group))
                    {
                        copy.Children.Add(result);
                        continue;
                    }

                    if (!groups.TryGetValue(group!, out var host))
                    {
                        host = new ClashResultGroup { DisplayName = group };
                        groups[group!] = host;
                        copy.Children.Add(host);
                    }

                    host.Children.Add(result);
                }

                clash.TestsData.TestsEditTestFromCopy(test, copy);
                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the regroup: " + e.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public bool RenameResults(string testId, string prefix)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            var stem = string.IsNullOrWhiteSpace(prefix) ? "Clash" : prefix.Trim();

            try
            {
                var found = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(test.Children, null, found);

                var number = 1;

                foreach (var (result, _) in found)
                    clash.TestsData.TestsEditDisplayName(result,
                        stem + number++.ToString(System.Globalization.CultureInfo.InvariantCulture));

                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the rename: " + e.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public bool RenamePicked(string testId, IReadOnlyList<string> resultIds, string prefix)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);
            var stem = string.IsNullOrWhiteSpace(prefix) ? "Clash" : prefix.Trim();

            try
            {
                var found = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(test.Children, null, found);

                var number = 1;
                var touched = 0;

                foreach (var (result, _) in found)
                {
                    if (!wanted.Contains(result.Guid.ToString())) continue;

                    clash.TestsData.TestsEditDisplayName(result,
                        stem + number++.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    touched++;
                }

                if (touched == 0)
                {
                    LastProblem = "None of those results are in the test any more. Re-read it.";
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the rename: " + e.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public bool CompactTests(IReadOnlyList<string> testIds)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            foreach (var id in testIds ?? Array.Empty<string>())
            {
                var test = FindTest(clash, id);
                if (test == null) continue;

                try
                {
                    clash.TestsData.TestsCompactTest(test);
                }
                catch (Exception e)
                {
                    LastProblem = "The host refused to compact \"" + test.DisplayName + "\": " + e.Message;
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool RenameTest(string testId, string name)
        {
            LastProblem = null;

            if (string.IsNullOrWhiteSpace(name))
            {
                LastProblem = "A test cannot have an empty name.";
                return false;
            }

            var clash = Writable();
            if (clash == null) return false;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            try
            {
                clash.TestsData.TestsEditDisplayName(test, name.Trim());
                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the rename: " + e.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public bool DeleteTests(IReadOnlyList<string> testIds)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var failed = 0;

            foreach (var id in testIds ?? Array.Empty<string>())
            {
                var test = FindTest(clash, id);
                if (test == null) continue;

                try
                {
                    // Always the two-argument form: it is the only one 2027 kept, and the root
                    // folder is a GroupItem like any other, so a test at the top level is not a
                    // special case that needs its own call.
                    var parent = test.Parent as GroupItem ?? clash.TestsData.Value.TestsRoot;

                    if (parent == null)
                    {
                        failed++;
                        LastProblem = "The document's clash tests could not be reached.";
                        continue;
                    }

                    clash.TestsData.TestsRemove(parent, test);
                }
                catch (Exception e)
                {
                    failed++;
                    LastProblem = "The host refused to delete \"" + test.DisplayName + "\": " + e.Message;
                }
            }

            return failed == 0;
        }

        /// <inheritdoc />
        public bool MoveTests(IReadOnlyList<string> testIds, string? folder)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var root = clash.TestsData.Value.TestsRoot;

            if (root == null)
            {
                LastProblem = "The document's clash tests could not be reached.";
                return false;
            }

            GroupItem destination;

            if (string.IsNullOrWhiteSpace(folder))
            {
                destination = root;
            }
            else
            {
                var found = FolderIn(root, folder!.Trim());

                if (found == null)
                {
                    try
                    {
                        clash.TestsData.TestsAddCopy(root, new ClashTestFolder { DisplayName = folder.Trim() });
                        found = FolderIn(root, folder.Trim());
                    }
                    catch (Exception e)
                    {
                        LastProblem = "The host refused to make the folder: " + e.Message;
                        return false;
                    }
                }

                if (found == null)
                {
                    LastProblem = "The host accepted the folder but it cannot be found again.";
                    return false;
                }

                destination = found;
            }

            foreach (var id in testIds ?? Array.Empty<string>())
            {
                var test = FindTest(clash, id);
                if (test == null) continue;

                var parent = test.Parent as GroupItem ?? root;
                if (ReferenceEquals(parent, destination)) continue;

                var index = IndexIn(parent, test.Guid);

                if (index < 0) continue;

                try
                {
                    clash.TestsData.TestsMove(parent, index, destination, destination.Children.Count);
                }
                catch (Exception e)
                {
                    LastProblem = "The host refused to move \"" + test.DisplayName + "\": " + e.Message;
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public ClashTestSides? Sides(string testId)
        {
            LastProblem = null;

            var clash = Clash();
            if (clash == null) return null;

            var test = FindTest(clash, testId);
            if (test == null) return null;

            return ShapeOf(test, SetSources());
        }

        /// <summary>What one test is between, against saved-set sources already in hand.</summary>
        /// <param name="test">The test.</param>
        /// <param name="sets">The document's saved sets, sourced once.</param>
        private ClashTestSides? ShapeOf(ClashTest test, IReadOnlyList<(string Name, SelectionSource Source)> sets)
        {
            try
            {
                var (setA, scopesA) = SideOf(test.SelectionA, sets);
                var (setB, scopesB) = SideOf(test.SelectionB, sets);

                var metres = NavUnits.MetresPerUnit(_document.Document.Units);

                return new ClashTestSides(setA, setB, scopesA, scopesB,
                                          test.TestType.ToString(),
                                          test.Tolerance * metres);
            }
            catch (Exception e)
            {
                LastProblem = "The host would not describe that test: " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// Every saved set with the source that points at it, built once.
        ///
        /// Describing a test's sides compares each side against these. Built per side per test,
        /// as it was, a document with two hundred sets and a hundred and sixty tests asked the
        /// host to create sixty-four thousand selection sources for two lines of text.
        /// </summary>
        private IReadOnlyList<(string Name, SelectionSource Source)> SetSources()
        {
            var found = new List<(string, SelectionSource)>();

            try
            {
                var sets = _document.Document.SelectionSets;

                foreach (var set in AllSets(sets.Value))
                {
                    try
                    {
                        found.Add((set.DisplayName ?? string.Empty, sets.CreateSelectionSource(set)));
                    }
                    catch (Exception)
                    {
                        // A set the host will not source cannot be any test's side.
                    }
                }
            }
            catch (Exception)
            {
                // A document with no sets at all describes no side as a set, which is not an
                // error and not worth a message.
            }

            return found;
        }

        /// <summary>
        /// What one side is: a single saved set, whole models, or neither.
        ///
        /// A set is recognised by value against every saved set's own source - the host compares
        /// sources by what they point at, so a renamed folder does not break the match. Whole
        /// models are recognised as a selection made only of root items.
        /// </summary>
        private (string? Set, IReadOnlyList<string> Scopes) SideOf(
            ClashSelection selection, IReadOnlyList<(string Name, SelectionSource Source)> sets)
        {
            var none = (string?)null;
            var empty = (IReadOnlyList<string>)Array.Empty<string>();

            try
            {
                var sources = selection.Selection.SelectionSources;

                if (sources != null && sources.Count == 1)
                {
                    var wanted = sources[0];

                    foreach (var (name, source) in sets)
                    {
                        try
                        {
                            if (source.Equals(wanted)) return (name, empty);
                        }
                        catch (Exception)
                        {
                            // A source the host will not compare cannot be this side.
                        }
                    }

                    return (none, empty);
                }

                var items = selection.Selection.GetSelectedItems();

                if (items != null && items.Count > 0)
                {
                    var scopes = new List<string>();

                    foreach (ModelItem item in items)
                    {
                        if (item.Parent != null) return (none, empty);
                        scopes.Add(NavKeys.ScopeOf(item.Model));
                    }

                    return (none, scopes);
                }
            }
            catch (Exception)
            {
                // A side the host will not describe is a side we do not guess at.
            }

            return (none, empty);
        }

        private static IEnumerable<SelectionSet> AllSets(SavedItemCollection items)
        {
            foreach (SavedItem item in items)
            {
                if (item is SelectionSet set) yield return set;
                else if (item is GroupItem folder)
                    foreach (var nested in AllSets(folder.Children)) yield return nested;
            }
        }

        /// <inheritdoc />
        public bool ApplyResultView(string testId, string resultId)
        {
            LastProblem = null;

            var clash = Clash();

            if (clash == null)
            {
                LastProblem = "This document has no clash engine.";
                return false;
            }

            var found = FindResult(clash, testId, resultId);

            if (found == null)
            {
                LastProblem = "That result is no longer in the document. Re-read it.";
                return false;
            }

            try
            {
                var viewpoint = clash.TestsData.TestsViewpointForResult(found);

                if (viewpoint == null)
                {
                    LastProblem = "The host has no saved view for that result.";
                    return false;
                }

                _document.Document.CurrentViewpoint.CopyFrom(viewpoint);
                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host would not show that result: " + e.Message;
                return false;
            }
        }

        /// <inheritdoc />
        public int SelectResults(string testId, IReadOnlyList<string> resultIds, bool add)
        {
            LastProblem = null;

            var clash = Clash();

            if (clash == null)
            {
                LastProblem = "This document has no clash engine.";
                return -1;
            }

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document. Re-read it.";
                return -1;
            }

            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);

            if (wanted.Count == 0) return 0;

            // One pass over this test's results rather than one pass per row: the walk is over
            // the test's own children, not the federation, but a board with a thousand picked
            // rows would still do it a thousand times.
            var found = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, found);

            var items = new ModelItemCollection();

            foreach (var entry in found)
            {
                if (!wanted.Contains(entry.Result.Guid.ToString())) continue;

                // The two items the engine recorded when the test ran. No key, no lookup: it is
                // still holding them.
                Take(items, entry.Result.CompositeItem1);
                Take(items, entry.Result.CompositeItem2);
            }

            try
            {
                var selection = _document.Document.CurrentSelection;

                if (add)
                {
                    foreach (ModelItem item in items)
                        if (!selection.SelectedItems.Contains(item)) selection.SelectedItems.Add(item);
                }
                else
                {
                    selection.CopyFrom(items);
                }

                return items.Count;
            }
            catch (Exception e)
            {
                LastProblem = "The host would not change the selection: " + e.Message;
                return -1;
            }
        }

        /// <summary>Add one of a result's sides to a collection, when the engine recorded one.</summary>
        private static void Take(ModelItemCollection items, ModelItem? item)
        {
            if (item == null || items.Contains(item)) return;

            items.Add(item);
        }

        /// <summary>
        /// One host result as the board wants it, read while the elements are in hand.
        ///
        /// Everything here used to be re-derived above the seam by resolving the result's element
        /// keys back through the document - a full-federation index the board paid for on its
        /// first row, which is what froze it on a real project. The engine is holding both
        /// elements; nothing here is a lookup.
        ///
        /// The comment trail is not read. A board shows how many comments there are and the
        /// newest one, and reading every body of every comment was a large share of a read for
        /// something almost nothing looks at. <see cref="CommentsFor"/> fetches them when an
        /// export or an opened row actually wants them.
        /// </summary>
        /// <param name="result">The host result.</param>
        /// <param name="group">The clash group it sits in, or null.</param>
        /// <returns>The descriptor, or null when neither element can be named.</returns>
        private static ClashResultInfo? Described(ClashResult result, string? group)
        {
            var a = KeyOf(result.CompositeItem1);
            var b = KeyOf(result.CompositeItem2);

            // Both participants have to resolve or there is no clash key, and a key made from
            // one side would collide with every other result on that element. Skipped, and the
            // count difference shows up in the funnel rather than being invented here.
            if (a.IsEmpty || b.IsEmpty) return null;

            var centre = result.Center;

            var info = new ClashResultInfo(
                result.Guid.ToString(),
                a, b,
                centre.X, centre.Y, centre.Z,
                StatusOf(result),
                AssignedToOf(result))
            {
                Name = result.DisplayName,
                GroupName = group,
                NameA = NameOf(result.CompositeItem1),
                NameB = NameOf(result.CompositeItem2),
                CategoryA = CategoryOf(result.CompositeItem1),
                CategoryB = CategoryOf(result.CompositeItem2),
            };

            try
            {
                var comments = result.Comments;
                info.CommentCount = comments.Count;

                if (comments.Count > 0)
                    info.LastComment = comments[comments.Count - 1].Body;
            }
            catch (Exception)
            {
                // A result whose comments cannot be counted still counts and displays.
            }

            try
            {
                info.Distance = result.Distance;
            }
            catch (Exception)
            {
                // A result with no distance simply has none.
            }

            return info;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashTestRead> ReadEverything(Func<int, bool>? onProgress)
        {
            var read = new List<ClashTestRead>();

            var clash = Clash();
            if (clash == null) return read;

            // The document's saved sets, sourced once. Describing what a test is between used to
            // compare a side against every set in the document, per side, per test - so a job
            // with two hundred sets and a hundred and sixty tests asked the host to build a
            // selection source sixty-four thousand times for a line of text on a rail.
            var sets = SetSources();

            var seen = 0;

            foreach (var found in AllTests(clash))
            {
                var flat = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(found.Test.Children, null, flat);

                var results = new List<ClashResultInfo>(flat.Count);

                foreach (var (result, group) in flat)
                {
                    if (onProgress != null && !onProgress(++seen)) return read;

                    var info = Described(result, group);
                    if (info != null) results.Add(info);
                }

                var test = new ClashTestInfo(
                    found.Test.Guid.ToString(),
                    found.Test.DisplayName ?? "(unnamed test)",
                    found.Folder,
                    flat.Count,
                    LastRunTicksOf(found.Test));

                read.Add(new ClashTestRead(test, ShapeOf(found.Test, sets), results));
            }

            return read;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashResultValues> ValuesFor(string testId,
                                                          IReadOnlyList<ClashPropertyWanted> wanted,
                                                          Func<int, bool>? onProgress)
        {
            var read = new List<ClashResultValues>();

            LastProblem = null;

            var clash = Clash();
            if (clash == null || wanted == null || wanted.Count == 0) return read;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document. Re-read it.";
                return read;
            }

            var flat = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, flat);

            var seen = 0;

            foreach (var (result, _) in flat)
            {
                if (onProgress != null && !onProgress(++seen)) return read;

                var values = new ClashResultValues(result.Guid.ToString());

                // Read once per element rather than once per column: a board with six columns
                // was six property enumerations of the same duct.
                var a = PropertiesOf(result.CompositeItem1);
                var b = PropertiesOf(result.CompositeItem2);

                foreach (var column in wanted)
                {
                    var fromA = column.Side == ClashElementSide.Second ? null : Look(a, column);
                    var fromB = column.Side == ClashElementSide.First ? null : Look(b, column);

                    var value = column.Side == ClashElementSide.Both
                        ? (fromA != null && fromB != null && fromA != fromB
                            ? fromA + " | " + fromB
                            : fromA ?? fromB)
                        : column.Side == ClashElementSide.First ? fromA : fromB;

                    if (value != null) values.Values[column.Id] = value;
                }

                read.Add(values);
            }

            return read;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashResultIfc> IfcFor(string testId, Func<int, bool>? onProgress)
        {
            var read = new List<ClashResultIfc>();

            LastProblem = null;

            var clash = Clash();
            if (clash == null) return read;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document. Re-read it.";
                return read;
            }

            var flat = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, flat);

            var seen = 0;

            foreach (var (result, _) in flat)
            {
                if (onProgress != null && !onProgress(++seen)) return read;

                read.Add(new ClashResultIfc(
                    result.Guid.ToString(),
                    IfcOf(result.CompositeItem1),
                    IfcOf(result.CompositeItem2)));
            }

            return read;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashResultComments> CommentsFor(string testId, Func<int, bool>? onProgress)
        {
            var read = new List<ClashResultComments>();

            LastProblem = null;

            var clash = Clash();
            if (clash == null) return read;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document. Re-read it.";
                return read;
            }

            var flat = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, flat);

            var seen = 0;

            foreach (var (result, _) in flat)
            {
                if (onProgress != null && !onProgress(++seen)) return read;

                var trail = new ClashResultComments(result.Guid.ToString());

                try
                {
                    foreach (Comment comment in result.Comments)
                        trail.Trail.Add(new Core.Clash.ClashComment(Named(comment.Author),
                            CreationTicks(comment), comment.Body ?? string.Empty));
                }
                catch (Exception)
                {
                    // A result whose comments cannot be read carries none.
                }

                read.Add(trail);
            }

            return read;
        }

        /// <inheritdoc />
        public IReadOnlyList<ClashPropertyName> PropertyVocabulary(int settleAfter,
                                                                   Func<int, bool>? onProgress)
        {
            var found = new List<ClashPropertyName>();

            var clash = Clash();
            if (clash == null) return found;

            var seen = new HashSet<(string, string)>();

            // An element appears in as many results as it clashes in, and a federation's property
            // vocabulary repeats immediately: the fiftieth duct carries what the first one did.
            var asked = new HashSet<string>(StringComparer.Ordinal);
            var quiet = 0;
            var read = 0;

            foreach (var test in AllTests(clash))
            {
                var flat = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(test.Test.Children, null, flat);

                foreach (var (result, _) in flat)
                {
                    foreach (var item in new[] { result.CompositeItem1, result.CompositeItem2 })
                    {
                        if (item == null) continue;

                        // The same element on both sides of ten clashes is one enumeration.
                        var mark = ItemMark(item);

                        if (mark != null && !asked.Add(mark)) continue;

                        if (onProgress != null && !onProgress(++read)) return Named(seen, found);

                        var before = seen.Count;

                        foreach (var property in PropertiesOf(item))
                            seen.Add((property.Key.Tab, property.Key.Name));

                        quiet = seen.Count == before ? quiet + 1 : 0;

                        if (settleAfter > 0 && quiet >= settleAfter) return Named(seen, found);
                    }
                }
            }

            return Named(seen, found);
        }

        private static IReadOnlyList<ClashPropertyName> Named(
            HashSet<(string Tab, string Name)> seen, List<ClashPropertyName> into)
        {
            foreach (var (tab, name) in seen) into.Add(new ClashPropertyName(tab, name));

            return into;
        }

        /// <summary>Something that tells one element from another without keying it.</summary>
        private static string? ItemMark(ModelItem item)
        {
            try
            {
                // Not an ElementKey: this only has to separate elements within one read, and
                // keying costs a GUID read or a walk up the tree per item.
                return item.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Every property an element carries, by tab and name.</summary>
        private static Dictionary<(string Tab, string Name), string> PropertiesOf(ModelItem? item)
        {
            var found = new Dictionary<(string, string), string>();

            if (item == null) return found;

            try
            {
                foreach (var category in item.PropertyCategories)
                {
                    var tab = category.DisplayName ?? category.Name ?? "Properties";

                    foreach (var property in category.Properties)
                    {
                        var name = property.DisplayName ?? property.Name ?? string.Empty;
                        if (name.Length == 0) continue;

                        var key = (tab, name);

                        if (!found.ContainsKey(key)) found[key] = Text(property.Value);
                    }
                }
            }
            catch (Exception)
            {
                // An element the host will not enumerate carries nothing as far as this is
                // concerned, which is the same answer as an element with no properties.
            }

            return found;
        }

        /// <summary>The value one wanted property has on an element, or null.</summary>
        private static string? Look(Dictionary<(string Tab, string Name), string> properties,
                                    ClashPropertyWanted wanted)
        {
            if (wanted.Tab.Length > 0)
                return properties.TryGetValue((wanted.Tab, wanted.Name), out var exact)
                    ? exact
                    : null;

            // No tab named: the first tab that carries a property of this name.
            foreach (var pair in properties)
                if (string.Equals(pair.Key.Name, wanted.Name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;

            return null;
        }

        /// <summary>A property value as text, however the host typed it.</summary>
        private static string Text(VariantData? value)
        {
            if (value == null) return string.Empty;

            try
            {
                return value.ToDisplayString() ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>One element's IFC GlobalId, through the identity the exporters share.</summary>
        private string? IfcOf(ModelItem? item)
        {
            if (item == null) return null;

            try
            {
                return Core.OpenBim.IfcIdentity.Of(_document.Wrap(item));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>How many results a test holds, without building a list of them.</summary>
        private static int CountResults(SavedItemCollection items)
        {
            if (items == null) return 0;

            var count = 0;

            foreach (SavedItem item in items)
            {
                if (item is ClashResult) count++;
                else if (item is ClashResultGroup group) count += CountResults(group.Children);
            }

            return count;
        }

        /// <inheritdoc />
        public byte[]? ResultSnapshot(string testId, string resultId, int width, int height)
        {
            LastProblem = null;

            var clash = Clash();
            if (clash == null) return null;

            var found = FindResult(clash, testId, resultId);

            if (found == null)
            {
                LastProblem = "That result is no longer in the document. Re-read it.";
                return null;
            }

            try
            {
                using (var image = clash.TestsData.TestsImageForResult(found,
                           ImageGenerationStyle.ScenePlusOverlay,
                           Math.Max(64, width), Math.Max(64, height)))
                {
                    if (image == null) return null;

                    using (var bytes = new System.IO.MemoryStream())
                    {
                        image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
                        return bytes.ToArray();
                    }
                }
            }
            catch (Exception e)
            {
                LastProblem = "The host would not render that result: " + e.Message;
                return null;
            }
        }

        private static ClashResult? FindResult(DocumentClash clash, string testId, string resultId)
        {
            var test = FindTest(clash, testId);
            if (test == null) return null;

            var found = new List<(ClashResult Result, string? Group)>();
            CollectWithGroups(test.Children, null, found);

            return found
                .Select(f => f.Result)
                .FirstOrDefault(r => string.Equals(r.Guid.ToString(), resultId,
                                                   StringComparison.OrdinalIgnoreCase));
        }

        private static long CreationTicks(Comment comment)
        {
            try
            {
                return comment.CreationDate.ToUniversalTime().Ticks;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // -----------------------------------------------------------------------------------

        /// <summary>Run one in-place edit over each named result of one test.</summary>
        private bool OnResults(string testId, IReadOnlyList<string> resultIds,
                               Action<DocumentClash, ClashResult> edit)
        {
            LastProblem = null;

            var clash = Writable();
            if (clash == null) return false;

            var test = FindTest(clash, testId);

            if (test == null)
            {
                LastProblem = "That test is no longer in the document.";
                return false;
            }

            var wanted = new HashSet<string>(resultIds ?? Array.Empty<string>(),
                                             StringComparer.OrdinalIgnoreCase);

            if (wanted.Count == 0)
            {
                LastProblem = "No results were named.";
                return false;
            }

            try
            {
                var found = new List<(ClashResult Result, string? Group)>();
                CollectWithGroups(test.Children, null, found);

                var touched = 0;

                foreach (var (result, _) in found)
                {
                    if (!wanted.Contains(result.Guid.ToString())) continue;

                    edit(clash, result);
                    touched++;
                }

                if (touched == 0)
                {
                    LastProblem = "None of those results are in the test any more. Re-read it.";
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                LastProblem = "The host refused the edit: " + e.Message;
                return false;
            }
        }

        /// <summary>The clash document when it can be written, or null with the reason said.</summary>
        private DocumentClash? Writable()
        {
            var clash = Clash();

            if (clash == null)
            {
                LastProblem = "This document has no clash engine to write to.";
                return null;
            }

            try
            {
                if (clash.TestsData.Value.IsReadOnly)
                {
                    LastProblem = "The document's clash data is read-only here.";
                    return null;
                }
            }
            catch (Exception)
            {
                LastProblem = "The document's clash data could not be reached.";
                return null;
            }

            return clash;
        }

        private static ClashTest? FindTest(DocumentClash clash, string testId) =>
            AllTests(clash)
                .Select(t => t.Test)
                .FirstOrDefault(t => string.Equals(t.Guid.ToString(), testId,
                                                   StringComparison.OrdinalIgnoreCase));

        private static ClashTestFolder? FolderIn(GroupItem root, string name)
        {
            foreach (SavedItem item in root.Children)
            {
                if (item is ClashTestFolder found &&
                    string.Equals(found.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return found;
            }

            return null;
        }

        private static int IndexIn(GroupItem parent, Guid guid)
        {
            for (var i = 0; i < parent.Children.Count; i++)
            {
                if (parent.Children[i] is ClashTest test && test.Guid == guid) return i;
            }

            return -1;
        }

        private static string? NameOf(ModelItem? item)
        {
            if (item == null) return null;

            try
            {
                if (!string.IsNullOrEmpty(item.DisplayName)) return item.DisplayName;
                return item.Parent?.DisplayName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? CategoryOf(ModelItem? item)
        {
            if (item == null) return null;

            try
            {
                return item.ClassDisplayName ?? item.ClassName;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void CollectWithGroups(SavedItemCollection items, string? group,
                                              List<(ClashResult Result, string? Group)> results)
        {
            if (items == null) return;

            foreach (SavedItem item in items)
            {
                if (item is ClashResult result)
                    results.Add((result, group));
                else if (item is ClashResultGroup nested)
                    CollectWithGroups(nested.Children, nested.DisplayName, results);
            }
        }

        /// <summary>
        /// Point one side of a test at what the spec says.
        ///
        /// A saved set becomes a selection SOURCE rather than a copy of its members, which is the
        /// difference between a matrix that stays right when somebody adds a wall and one that
        /// silently stops covering it. It is also what Clash Detective itself does when a set is
        /// picked by hand, so a test CamelWorks made and one made by hand behave the same way.
        /// </summary>
        private bool Fill(ClashSelection selection, ClashSide side)
        {
            switch (side.Kind)
            {
                case ClashSideKind.Models:
                {
                    var wanted = new HashSet<string>(side.Scopes, StringComparer.Ordinal);
                    var roots = new ModelItemCollection();

                    foreach (Model loaded in _document.Document.Models)
                    {
                        if (wanted.Contains(NavKeys.ScopeOf(loaded))) roots.Add(loaded.RootItem);
                    }

                    if (roots.Count == 0)
                    {
                        LastProblem = "None of the models this test is about are loaded any more.";
                        return false;
                    }

                    selection.Selection.CopyFrom(roots);
                    return true;
                }

                case ClashSideKind.Set:
                {
                    var sets = _document.Document.SelectionSets;
                    var found = Find(sets.Value, side.Name);

                    if (found == null)
                    {
                        LastProblem = "There is no set called \"" + side.Name + "\" in this document.";
                        return false;
                    }

                    var sources = new SelectionSourceCollection { sets.CreateSelectionSource(found) };
                    selection.Selection.CopyFrom(sources);
                    return true;
                }

                default:
                {
                    var items = new ModelItemCollection();

                    foreach (var key in side.Keys)
                    {
                        if (_document.Resolve(key) is NavModelItem resolved) items.Add(resolved.Item);
                    }

                    if (items.Count == 0)
                    {
                        LastProblem = "None of the elements on one side of this test still resolve.";
                        return false;
                    }

                    selection.Selection.CopyFrom(items);
                    return true;
                }
            }
        }

        private static SavedItem? Find(SavedItemCollection? items, string? name)
        {
            if (items == null || string.IsNullOrWhiteSpace(name)) return null;

            foreach (SavedItem item in items)
            {
                if (string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase)) return item;

                if (item is GroupItem folder)
                {
                    var nested = Find(folder.Children, name);
                    if (nested != null) return nested;
                }
            }

            return null;
        }

        private DocumentClash? Clash()
        {
            // GetClash() is an extension on Document that only exists when the Manage-only
            // assembly is present, which is the whole reason this file is in its own project.
            var clash = _document.Document.GetClash();
            return clash;
        }

        // The tests hang off TestsData.Value.TestsRoot, and can be nested in folders. Flattened
        // with the folder path kept, so a board can still show "Structural / L03 hard clash"
        // rather than losing the only thing that distinguishes two tests with the same name.
        //
        // TestsRoot rather than a Tests collection: the reference assemblies for the four supported
        // years do not all carry the latter, and reaching through the root works on every one.
        private static List<(ClashTest Test, string? Folder)> AllTests(DocumentClash clash)
        {
            var found = new List<(ClashTest, string?)>();

            var root = clash.TestsData.Value.TestsRoot;
            if (root != null) CollectTests(root.Children, null, found);

            return found;
        }

        private static void CollectTests(SavedItemCollection items, string? folder,
                                         List<(ClashTest Test, string? Folder)> found)
        {
            if (items == null) return;

            foreach (SavedItem item in items)
            {
                if (item is ClashTest test) found.Add((test, folder));
                else if (item is ClashTestFolder nested)
                    CollectTests(nested.Children, Join(folder, nested.DisplayName), found);
            }
        }

        // ------------------------------------------------------------------------------------
        // The four supported releases are not one API
        // ------------------------------------------------------------------------------------
        //
        // 2026 made an assignee an object rather than a string; 2027 did the same to a comment's
        // author. These four are every place that reaches the product, so the difference lives
        // here and nowhere else - the code above reads the same on every release.

#if NAV_STRING_COMMENT_AUTHOR
        /// <summary>A comment's author, as text.</summary>
        private static string Named(string? author) => author ?? string.Empty;

        /// <summary>An author, as the host's comment constructor takes one.</summary>
        private static string Writer(string? author) => author ?? string.Empty;
#else
        /// <summary>A comment's author, as text.</summary>
        private static string Named(Assignee? author) =>
            author == null ? string.Empty : author.DisplayName ?? author.ToString() ?? string.Empty;

        /// <summary>An author, as the host's comment constructor takes one.</summary>
        private static Assignee Writer(string? author) => new Assignee(author ?? string.Empty);
#endif

#if NAV_STRING_ASSIGNEE
        /// <summary>Whoever the result is being given to, as the host's write takes them.</summary>
        private static string Party(string? assignee) => assignee ?? string.Empty;
#else
        /// <summary>Whoever the result is being given to, as the host's write takes them.</summary>
        private static Assignee Party(string? assignee) => new Assignee(assignee ?? string.Empty);

        /// <summary>
        /// Who is making the change, which the status write has required since 2026.
        ///
        /// The signed-in Windows user: the host records it against the result, and a status
        /// changed by "CamelWorks" would be a worse answer to "who marked this approved" than
        /// the name of the person sitting at the machine.
        /// </summary>
        private static Assignee Me() => new Assignee(Environment.UserName ?? "CamelWorks");
#endif

        private static string? Join(string? folder, string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return folder;
            return string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
        }

        // AssignedTo is an Assignee, not a string. Guarded rather than dereferenced, because it is
        // null on every result nobody has assigned, which is most of them.
        private static string? AssignedToOf(ClashResult result)
        {
            try
            {
                var text = result.AssignedTo.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Collect(SavedItemCollection items, List<ClashResult> results)
        {
            if (items == null) return;

            foreach (SavedItem item in items)
            {
                if (item is ClashResult result)
                {
                    results.Add(result);
                }
                else if (item is ClashResultGroup group)
                {
                    // The host's own grouping. Flattened here on purpose: CamelWorks re-derives
                    // groups from its own rule stack, and carrying two competing groupings through
                    // the pipeline would mean neither could be trusted.
                    Collect(group.Children, results);
                }
            }
        }

        private static ElementKey KeyOf(ModelItem? item)
        {
            if (item == null) return default;

            try
            {
                return NavKeys.Of(item);
            }
            catch (Exception)
            {
                return default;
            }
        }

        // The host's status, as text, without asserting it means what CamelWorks means by it. The
        // engine recomputes New, Active and Resolved on every run, so only Reviewed and Approved
        // are read back as human judgement — and that decision is made above the seam, on the
        // string this returns.
        private static string? StatusOf(ClashResult result)
        {
            try
            {
                return result.Status.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static long LastRunTicksOf(ClashTest test)
        {
            try
            {
                // Nullable: a test that has never been run has no last-run time at all.
                return test.LastRun?.Ticks ?? 0;
            }
            catch (Exception)
            {
                // A test that has never been run. Zero says so, and the board shows it as never
                // run rather than as run at the epoch.
                return 0;
            }
        }
    }
}
