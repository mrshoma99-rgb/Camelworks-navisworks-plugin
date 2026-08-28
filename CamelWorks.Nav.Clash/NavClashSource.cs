using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using CamelWorks.Core.Abstractions;
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
                var results = new List<ClashResult>();
                Collect(found.Test.Children, results);

                tests.Add(new ClashTestInfo(
                    found.Test.Guid.ToString(),
                    found.Test.DisplayName ?? "(unnamed test)",
                    found.Folder,
                    results.Count,
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
                var a = KeyOf(result.CompositeItem1);
                var b = KeyOf(result.CompositeItem2);

                // Both participants have to resolve or there is no clash key, and a key made from
                // one side would collide with every other result on that element. Skipped, and the
                // count difference shows up in the funnel rather than being invented here.
                if (a.IsEmpty || b.IsEmpty) continue;

                var centre = result.Center;

                var info = new ClashResultInfo(
                    result.Guid.ToString(),
                    a, b,
                    centre.X, centre.Y, centre.Z,
                    StatusOf(result),
                    AssignedToOf(result))
                {
                    // Read while the elements are in hand. Everything here used to be re-derived
                    // above the seam by resolving the key back through the document index — a
                    // full-federation walk the board paid on its first result, which is what froze
                    // it on a real project.
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
                    {
                        info.LastComment = comments[comments.Count - 1].Body;

                        // The whole trail, not just the newest: it displays in the manager and
                        // travels in the baseline. Results carry a handful at most.
                        var trail = new List<Core.Clash.ClashComment>(comments.Count);

                        foreach (Comment comment in comments)
                            trail.Add(new Core.Clash.ClashComment(comment.Author ?? string.Empty,
                                CreationTicks(comment), comment.Body ?? string.Empty));

                        info.Comments = trail;
                    }
                }
                catch (Exception)
                {
                    // A result whose comments cannot be read still counts and displays.
                }

                try
                {
                    info.Distance = result.Distance;
                }
                catch (Exception)
                {
                    // A result with no distance simply has none.
                }

                results.Add(info);
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
                    TestType = ClashTestType.Hard,
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
                clash.TestsData.TestsEditResultStatus(result, parsed));
        }

        /// <inheritdoc />
        public bool Assign(string testId, IReadOnlyList<string> resultIds, string assignee) =>
            OnResults(testId, resultIds, (clash, result) =>
                clash.TestsData.TestsEditResultAssignedTo(result, assignee ?? string.Empty));

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
                            && string.Equals(existing.Author, author ?? string.Empty,
                                             StringComparison.OrdinalIgnoreCase))
                            return;

                        comments.Add(existing);
                    }
                }
                catch (Exception) { /* a result whose comments cannot be read still takes one */ }

                comments.Add(new Comment(body, CommentStatus.New, author ?? string.Empty));
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
                    if (test.Parent is GroupItem parent) clash.TestsData.TestsRemove(parent, test);
                    else clash.TestsData.TestsRemove(test);
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
