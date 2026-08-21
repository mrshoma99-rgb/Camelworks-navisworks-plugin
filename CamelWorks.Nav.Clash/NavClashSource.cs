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

            var test = AllTests(clash)
                .Select(t => t.Test)
                .FirstOrDefault(t => string.Equals(t.Guid.ToString(), testId, StringComparison.OrdinalIgnoreCase));

            if (test == null) return results;

            var found = new List<ClashResult>();
            Collect(test.Children, found);

            foreach (var result in found)
            {
                var a = KeyOf(result.CompositeItem1);
                var b = KeyOf(result.CompositeItem2);

                // Both participants have to resolve or there is no clash key, and a key made from
                // one side would collide with every other result on that element. Skipped, and the
                // count difference shows up in the funnel rather than being invented here.
                if (a.IsEmpty || b.IsEmpty) continue;

                var centre = result.Center;

                results.Add(new ClashResultInfo(
                    result.Guid.ToString(),
                    a, b,
                    centre.X, centre.Y, centre.Z,
                    StatusOf(result),
                    AssignedToOf(result)));
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
